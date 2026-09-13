using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350BillsMp
    {
        private static bool applied;
        private static FieldInfo billLoadId;
        private static FieldInfo recipeField, styleField, tableField, materialField, selectedBillField;
        private static FieldInfo locked, lastTable, refresh, selections, autoNaming;
        private static MethodInfo setMaterial, autoRename, selectBill, scroll, cache;
        private static readonly Queue<Action> localUpdates = new Queue<Action>();

        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || !ModsConfig.IsActive("andromeda.nicebilltab")) return;
            applied = true;
            try
            {
                Type selection = RequiredType("RecipeSelection"), drawer = RequiredType("TabBillsDrawer");
                billLoadId = Field(typeof(Bill), "loadID", typeof(int));
                recipeField = Field(selection, "SelectedRecipe", typeof(RecipeDef));
                styleField = Field(selection, "style", typeof(Precept_ThingStyle));
                tableField = Field(selection, "workTable", typeof(Building_WorkTable));
                materialField = Field(selection, "material", typeof(ThingDef));
                selectedBillField = Field(selection, "SelectedBill", typeof(Bill));
                locked = Field(drawer, "LockedSelection", selection, true);
                lastTable = Field(drawer, "LastSelTable", typeof(Building_WorkTable), true);
                refresh = Field(drawer, "shouldRefreshFilter", typeof(bool), true);
                selections = Field(drawer, "Selections", typeof(List<>).MakeGenericType(selection), true);
                autoNaming = Field(RequiredType("Settings"), "EnableAutoNaming", typeof(bool), true);
                setMaterial = Method(drawer, "SetMaterialToBill", selection, typeof(RecipeDef), typeof(Bill));
                autoRename = Method(drawer, "AutoRenameBill", typeof(Bill));
                selectBill = Method(drawer, "SelectBill", typeof(Bill), typeof(bool));
                scroll = Method(drawer, "DoAutomaticScrollDecision", typeof(bool));
                cache = Method(selection, "DoCache", typeof(ThingDef));
                MethodInfo add = Method(drawer, "TryAddBillToQueue", selection);
                if (!add.IsStatic || add.ReturnType != typeof(bool) || !setMaterial.IsStatic || setMaterial.ReturnType != typeof(void))
                    throw new InvalidOperationException("NiceBillTab bill executor signatures changed");
                MP.RegisterSyncMethod(typeof(Patch_Light350BillsMp), nameof(AddConfiguredBill)).ExposeParameter(1);
                MP.RegisterSyncMethod(typeof(Patch_Light350BillsMp), nameof(SetExistingMaterial));
                harmony.Patch(add, prefix: new HarmonyMethod(typeof(Patch_Light350BillsMp), nameof(AddPrefix)));
                harmony.Patch(setMaterial, prefix: new HarmonyMethod(typeof(Patch_Light350BillsMp), nameof(MaterialPrefix)));
                Log.Message("[MP-MeowOnlineShop][Light350-B8] NiceBillTab: configured bill transaction and existing material update installed.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B8] REQUIRED TARGET FAILED NiceBillTab: " + e); }
        }
        private static Type RequiredType(string name) => AccessTools.TypeByName("NiceBillTab." + name) ?? throw new TypeLoadException(name);
        private static MethodInfo Method(Type type, string name, params Type[] args) => AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        private static FieldInfo Field(Type type, string name, Type expected, bool isStatic = false)
        {
            FieldInfo field = AccessTools.DeclaredField(type, name);
            if (field == null || field.IsStatic != isStatic || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static bool InLocalInterface => MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;

        private static bool AddPrefix(object __0, ref bool __result)
        {
            if (!InLocalInterface) return true;
            __result = false;
            Building_WorkTable table = (Building_WorkTable)tableField.GetValue(__0);
            RecipeDef recipe = (RecipeDef)recipeField.GetValue(__0);
            if (table == null || !table.Spawned || recipe == null) return false;
            if (ReferenceEquals(locked.GetValue(null), __0)) locked.SetValue(null, null);
            // Keep the original local advisory dialogs; the original does not cancel creation for missing skills.
            Building_WorkTable viewed = (Building_WorkTable)lastTable.GetValue(null) ?? table;
            if (ModsConfig.BiotechActive && recipe.mechanitorOnlyRecipe && !viewed.Map.mapPawns.FreeColonists.Any(MechanitorUtility.IsMechanitor))
                Find.WindowStack.Add(new Dialog_MessageBox("RecipeRequiresMechanitor".Translate(recipe.LabelCap)));
            else if (!viewed.Map.mapPawns.FreeColonists.Any(p => recipe.PawnSatisfiesSkillRequirements(p)))
                Bill.CreateNoPawnsWithSkillDialog(recipe);
            refresh.SetValue(null, true);
            // Native MP AddBill also sends an exposed, UI-created bill with a temporary negative ID.
            // Finish its filter and localized name BEFORE serialization, so the packet is the whole transaction.
            Bill bill = recipe.MakeNewBill((Precept_ThingStyle)styleField.GetValue(__0));
            setMaterial.Invoke(null, new[] { __0, recipe, bill });
            if ((bool)autoNaming.GetValue(null)) autoRename.Invoke(null, new object[] { bill });
            AddConfiguredBill(table, bill);
            if (recipe.conceptLearned != null) PlayerKnowledgeDatabase.KnowledgeDemonstrated(recipe.conceptLearned, KnowledgeAmount.Total);
            if (TutorSystem.TutorialMode) TutorSystem.Notify_Event("AddBill-" + recipe.LabelCap.Resolve());
            __result = true;
            return false;
        }

        private static void AddConfiguredBill(Building_WorkTable table, Bill bill)
        {
            if (table == null || !table.Spawned || bill?.recipe == null || table.billStack.Bills.Contains(bill)) return;
            table.billStack.AddBill(bill);
            if (MP.IsExecutingSyncCommandIssuedBySelf)
                localUpdates.Enqueue(() => FinishAddedBill(table, bill));
        }

        private static bool MaterialPrefix(object __0, RecipeDef __1, Bill __2)
        {
            // An unattached temporary bill belongs to AddPrefix's packet construction, not shared simulation.
            if (!InLocalInterface || __2?.billStack == null) return true;
            if (TryGetBillIdentity(__2, out Building_WorkTable table, out int billId))
                SetExistingMaterial(table, billId, (ThingDef)materialField.GetValue(__0));
            return false;
        }
        private static void SetExistingMaterial(Building_WorkTable table, int billId, ThingDef material)
        {
            Bill bill = FindBill(table, billId);
            if (bill?.billStack == null || !bill.billStack.Bills.Contains(bill) || material == null) return;
            if (bill.recipe.ProducedThingDef != null && bill.recipe.ProducedThingDef.MadeFromStuff)
            {
                if (bill.ingredientFilter == null) { Log.Warning("[MP-MeowOnlineShop] NiceBillTab material update has no ingredient filter."); return; }
                bill.ingredientFilter.SetDisallowAll();
                bill.ingredientFilter.SetAllow(material, true);
            }
            if (MP.IsExecutingSyncCommandIssuedBySelf)
                localUpdates.Enqueue(() => RefreshSelectedBill(bill));
        }
        internal static bool TryGetBillIdentity(Bill bill, out Building_WorkTable table, out int billId)
        {
            table = bill?.billStack?.billGiver as Building_WorkTable;
            billId = bill == null ? -1 : (int)billLoadId.GetValue(bill);
            return table != null && table.Spawned && billId >= 0 && table.billStack.Bills.Contains(bill);
        }
        internal static Bill FindBill(Building_WorkTable table, int billId)
        {
            if (table == null || !table.Spawned || billId < 0) return null;
            return table.billStack.Bills.FirstOrDefault(b => (int)billLoadId.GetValue(b) == billId);
        }
        private static void RefreshSelectedBill(Bill bill)
        {
            refresh.SetValue(null, true);
            foreach (object selection in (IEnumerable)selections.GetValue(null))
                if (ReferenceEquals(selectedBillField.GetValue(selection), bill))
                    cache.Invoke(selection, new object[] { materialField.GetValue(selection) });
        }
        private static void FinishAddedBill(Building_WorkTable table, Bill bill)
        {
            if (!table.Spawned || !table.billStack.Bills.Contains(bill)) return;
            refresh.SetValue(null, true);
            if (lastTable.GetValue(null) == null) return; // The player closed the tab while the command was in flight.
            if (!ReferenceEquals(lastTable.GetValue(null), table))
            {
                CameraJumper.TryJumpAndSelect(table);
                scroll.Invoke(null, new object[] { true });
            }
            else
            {
                selectBill.Invoke(null, new object[] { bill, false });
                scroll.Invoke(null, new object[] { false });
            }
        }
        internal static void ClearLocalUpdates() => localUpdates.Clear();
        internal static void QueueLocalUpdate(Action action) => localUpdates.Enqueue(action);
        internal static void DrainLocalUpdates()
        {
            if (!MP.InInterface || MP.IsExecutingSyncCommand) return;
            while (localUpdates.Count > 0)
            {
                Action next = localUpdates.Dequeue();
                try { next(); }
                catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B8] Local bill UI refresh failed: " + e); }
            }
        }
    }
    public sealed class Light350BillUiUpdates : GameComponent
    {
        public Light350BillUiUpdates(Game game) { Patch_Light350BillsMp.ClearLocalUpdates(); }
        public override void GameComponentUpdate() { Patch_Light350BillsMp.DrainLocalUpdates(); }
    }
}
