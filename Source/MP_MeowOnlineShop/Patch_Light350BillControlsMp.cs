using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350BillControlsMp
    {
        private static bool applied, playingFeedback;
        private static MethodInfo plus, minus, suspend, feedback, multiplier;
        private static ISyncMethod adjust;
        [ThreadStatic] private static int? commandMultiplier;
        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || !ModsConfig.IsActive("andromeda.nicebilltab")) return;
            applied = true;
            try
            {
                Type drawer = AccessTools.TypeByName("NiceBillTab.TabBillsDrawer") ?? throw new TypeLoadException("NiceBillTab.TabBillsDrawer");
                plus = Required(drawer, "PlusAction", typeof(Bill_Production), typeof(RecipeDef));
                minus = Required(drawer, "MinusAction", typeof(Bill_Production), typeof(RecipeDef));
                feedback = Required(drawer, "PlusMinusActionTrigger", typeof(Bill_Production), typeof(RecipeDef));
                suspend = Required(drawer, "SuspendBill", typeof(Bill), typeof(bool));
                multiplier = Required(typeof(GenUI), nameof(GenUI.CurrentAdjustmentMultiplier));
                foreach (MethodInfo target in new[] { plus, minus })
                    if (PatchProcessor.GetOriginalInstructions(target).Count(i => i.Calls(multiplier)) != 3)
                        throw new InvalidOperationException(target.Name + ": expected three multiplier reads");
                adjust = MP.RegisterSyncMethod(typeof(Patch_Light350BillControlsMp), nameof(Adjust));
                foreach (MethodInfo target in new[] { plus, minus })
                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patch_Light350BillControlsMp), nameof(AdjustPrefix)),
                        transpiler: new HarmonyMethod(typeof(Patch_Light350BillControlsMp), nameof(MultiplierReads)));
                MP.RegisterSyncMethod(typeof(Patch_Light350BillControlsMp), nameof(SetSuspended));
                harmony.Patch(suspend, prefix: new HarmonyMethod(typeof(Patch_Light350BillControlsMp), nameof(SuspendPrefix)));
                harmony.Patch(feedback, prefix: new HarmonyMethod(typeof(Patch_Light350BillControlsMp), nameof(FeedbackPrefix)));
                Log.Message("[MP-MeowOnlineShop][Light350-B9] NiceBillTab: plus/minus with 3/3 captured multiplier reads and suspend installed.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B9] REQUIRED TARGET FAILED NiceBillTab controls: " + e); }
        }
        private static MethodInfo Required(Type type, string name, params Type[] args)
        {
            MethodInfo method = AccessTools.DeclaredMethod(type, name, args);
            if (method == null || !method.IsStatic) throw new MissingMethodException(type.FullName, name);
            return method;
        }
        private static bool AdjustPrefix(Bill_Production __0, RecipeDef __1, MethodBase __originalMethod)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand) return true;
            if (Patch_Light350BillsMp.TryGetBillIdentity(__0, out Building_WorkTable table, out int billId))
                adjust.DoSync(null, table, billId, __1, Equals(__originalMethod, plus), GenUI.CurrentAdjustmentMultiplier());
            return false;
        }
        private static void Adjust(Building_WorkTable table, int billId, RecipeDef recipe, bool increase, int step)
        {
            Bill_Production bill = Patch_Light350BillsMp.FindBill(table, billId) as Bill_Production;
            if (bill?.billStack == null || !bill.billStack.Bills.Contains(bill) || recipe == null || bill.recipe != recipe || step < 1) return;
            int? previous = commandMultiplier;
            try
            {
                commandMultiplier = step;
                (increase ? plus : minus).Invoke(null, new object[] { bill, recipe });
            }
            finally { commandMultiplier = previous; }
            if (MP.IsExecutingSyncCommandIssuedBySelf)
                Patch_Light350BillsMp.QueueLocalUpdate(() => PlayFeedback(bill, recipe));
        }
        private static bool SuspendPrefix(Bill __0, bool __1)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand) return true;
            if (Patch_Light350BillsMp.TryGetBillIdentity(__0, out Building_WorkTable table, out int billId))
                SetSuspended(table, billId, __1);
            return false;
        }
        private static void SetSuspended(Building_WorkTable table, int billId, bool value)
        {
            Bill bill = Patch_Light350BillsMp.FindBill(table, billId);
            if (bill != null) suspend.Invoke(null, new object[] { bill, value });
        }
        private static IEnumerable<CodeInstruction> MultiplierReads(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(multiplier))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Light350BillControlsMp), nameof(ReadMultiplier));
                    count++;
                }
                yield return instruction;
            }
            if (count != 3) throw new InvalidOperationException("Bill multiplier coverage changed");
        }
        private static int ReadMultiplier() => commandMultiplier ?? GenUI.CurrentAdjustmentMultiplier();
        private static bool FeedbackPrefix() => playingFeedback || !MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand;
        private static void PlayFeedback(Bill_Production bill, RecipeDef recipe)
        {
            bool previous = playingFeedback;
            try { playingFeedback = true; feedback.Invoke(null, new object[] { bill, recipe }); }
            finally { playingFeedback = previous; }
        }
    }
}
