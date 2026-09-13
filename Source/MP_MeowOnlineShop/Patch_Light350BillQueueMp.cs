using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350BillQueueMp
    {
        private static bool applied;
        private static FieldInfo lastTable, refresh, draggedIndex, rowHeights;
        private static MethodInfo dropIndex, scroll;
        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || !ModsConfig.IsActive("andromeda.nicebilltab")) return;
            applied = true;
            try
            {
                Type drawer = AccessTools.TypeByName("NiceBillTab.TabBillsDrawer") ?? throw new TypeLoadException("NiceBillTab.TabBillsDrawer");
                Type utils = AccessTools.TypeByName("NiceBillTab.Utils") ?? throw new TypeLoadException("NiceBillTab.Utils");
                lastTable = Field(drawer, "LastSelTable", typeof(Building_WorkTable));
                refresh = Field(drawer, "shouldRefreshFilter", typeof(bool));
                draggedIndex = Field(drawer, "draggedBillIndex", typeof(int));
                rowHeights = AccessTools.DeclaredField(drawer, "lastBillHeight");
                if (rowHeights == null || !rowHeights.IsStatic || !typeof(ICollection).IsAssignableFrom(rowHeights.FieldType))
                    throw new MissingFieldException(drawer.FullName, "lastBillHeight");
                dropIndex = Method(drawer, "GetDropIndex", typeof(List<Bill>), typeof(Vector2), typeof(float));
                if (dropIndex.ReturnType != typeof(int)) throw new InvalidOperationException("GetDropIndex return type changed");
                scroll = Method(drawer, "DoAutomaticScrollDecision", typeof(bool));
                MethodInfo insert = Method(drawer, "InsertBill", typeof(Building_WorkTable), typeof(Bill), typeof(int));
                MethodInfo craft = Method(utils, "TryAddBillToQueue", typeof(ThingWithComps), typeof(RecipeDef), typeof(int), typeof(Map));
                MethodInfo drop = Method(drawer, "HandleBillDrop", typeof(List<Bill>), typeof(Vector2), typeof(float));
                MP.RegisterSyncMethod(typeof(Patch_Light350BillQueueMp), nameof(InsertConfiguredBill)).ExposeParameter(1);
                MP.RegisterSyncMethod(typeof(Patch_Light350BillQueueMp), nameof(MoveBill));
                harmony.Patch(insert, prefix: new HarmonyMethod(typeof(Patch_Light350BillQueueMp), nameof(InsertPrefix)));
                harmony.Patch(craft, prefix: new HarmonyMethod(typeof(Patch_Light350BillQueueMp), nameof(CraftPrefix)));
                harmony.Patch(drop, prefix: new HarmonyMethod(typeof(Patch_Light350BillQueueMp), nameof(DropPrefix)));
                Log.Message("[MP-MeowOnlineShop][Light350-B10] NiceBillTab: paste transaction, quick craft quantity and drag reorder installed.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B10] REQUIRED TARGET FAILED NiceBillTab queue: " + e); }
        }
        private static bool LocalInterface => MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;
        private static MethodInfo Method(Type type, string name, params Type[] args)
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name, args);
            if (method == null || !method.IsStatic) throw new MissingMethodException(type.FullName, name);
            return method;
        }
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            FieldInfo field = AccessTools.DeclaredField(type, name);
            if (field == null || !field.IsStatic || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static bool InsertPrefix(Building_WorkTable __0, Bill __1, int __2)
        {
            if (!LocalInterface) return true;
            if (__0 != null && __0.Spawned && __1?.recipe != null && __2 >= 0)
                InsertConfiguredBill(__0, __1, __2);
            return false;
        }
        private static void InsertConfiguredBill(Building_WorkTable table, Bill bill, int index)
        {
            if (table == null || !table.Spawned || bill?.recipe == null || table.billStack.Bills.Contains(bill)) return;
            // Native AddBill converts MP's temporary UI load ID into a real shared ID.
            table.billStack.AddBill(bill);
            if (index >= 0)
            {
                table.billStack.Bills.Remove(bill);
                table.billStack.Bills.Insert(Math.Min(index, table.billStack.Bills.Count), bill);
            }
            RefreshAfterCommand();
        }
        private static bool CraftPrefix(ThingWithComps __0, RecipeDef __1, int __2, Map __3)
        {
            if (!LocalInterface) return true;
            if (__1 == null || __3 == null) return false;
            // Unlike the drawer's F10 helper, these original advisory branches prevent creation.
            if (ModsConfig.BiotechActive && __1.mechanitorOnlyRecipe && !__3.mapPawns.FreeColonists.Any(MechanitorUtility.IsMechanitor))
                Find.WindowStack.Add(new Dialog_MessageBox("RecipeRequiresMechanitor".Translate(__1.LabelCap)));
            else if (!__3.mapPawns.FreeColonists.Any(p => __1.PawnSatisfiesSkillRequirements(p)))
                Bill.CreateNoPawnsWithSkillDialog(__1);
            else if (__0 is Building_WorkTable table && table.Spawned)
            {
                if (ReferenceEquals(table, lastTable.GetValue(null)))
                {
                    refresh.SetValue(null, true);
                    scroll.Invoke(null, new object[] { true });
                }
                Bill bill = __1.MakeNewBill();
                if (bill is Bill_Production production)
                {
                    production.repeatMode = BillRepeatModeDefOf.RepeatCount;
                    production.repeatCount = Mathf.CeilToInt((float)__2 / __1.products[0].count);
                }
                InsertConfiguredBill(table, bill, -1);
                if (__1.conceptLearned != null) PlayerKnowledgeDatabase.KnowledgeDemonstrated(__1.conceptLearned, KnowledgeAmount.Total);
            }
            return false;
        }
        private static bool DropPrefix(List<Bill> __0, Vector2 __1, float __2)
        {
            if (!LocalInterface) return true;
            int from = (int)draggedIndex.GetValue(null);
            if (__0 == null || from < 0 || from >= __0.Count || ((ICollection)rowHeights.GetValue(null)).Count < __0.Count) return false;
            int to = (int)dropIndex.Invoke(null, new object[] { __0, __1, __2 });
            if (to >= 0 && to != from && Patch_Light350BillsMp.TryGetBillIdentity(__0[from], out Building_WorkTable table, out int id))
                MoveBill(table, id, to);
            return false;
        }
        private static void MoveBill(Building_WorkTable table, int id, int index)
        {
            Bill bill = Patch_Light350BillsMp.FindBill(table, id);
            if (bill == null || index < 0) return;
            // Preserve the original absolute insertion position, including filtered queue behavior.
            table.billStack.Bills.Remove(bill);
            table.billStack.Bills.Insert(Math.Min(index, table.billStack.Bills.Count), bill);
            RefreshAfterCommand();
        }
        private static void RefreshAfterCommand()
        {
            if (MP.IsExecutingSyncCommandIssuedBySelf)
                Patch_Light350BillsMp.QueueLocalUpdate(() => refresh.SetValue(null, true));
        }
    }
}
