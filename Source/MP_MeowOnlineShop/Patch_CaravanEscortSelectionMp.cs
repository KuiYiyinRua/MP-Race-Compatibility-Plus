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
    // Desync 24/25: local performs an extra Pawn.DeSpawn during caravan reform.
    // The traces do not identify that pawn. Independently verified in RW 1.6:
    // Notify_TransferablesChanged changes escort-mech counts outside MP's row
    // field watcher. OpenWindow executes on the issuing peer only, even while
    // ExecutingCmds is true. Never use !MP.InInterface alone to bless a refresh.
    internal static class Patch_CaravanEscortSelectionMp
    {
        private static Type proxyType;
        private static MethodInfo refresh;
        private static MethodInfo prepareDialog;
        private static FieldInfo sessionFaction;
        private static MethodInfo pushFaction;
        private static MethodInfo popFaction;
        [ThreadStatic] private static Dialog_FormCaravan sharedRefreshDialog;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                proxyType = AccessTools.TypeByName("Multiplayer.Client.CaravanFormingProxy");
                var session = AccessTools.TypeByName("Multiplayer.Client.CaravanFormingSession");
                var factionContext = AccessTools.TypeByName("Multiplayer.Client.FactionContext");
                refresh = AccessTools.DeclaredMethod(typeof(Dialog_FormCaravan), "Notify_TransferablesChanged", Type.EmptyTypes);
                prepareDialog = AccessTools.DeclaredMethod(session, "PrepareDummyDialog", Type.EmptyTypes);
                sessionFaction = AccessTools.Field(session, "faction");
                pushFaction = AccessTools.Method(factionContext, "Push", new[] { typeof(Faction), typeof(bool) });
                popFaction = AccessTools.Method(factionContext, "Pop", Type.EmptyTypes);
                var actions = new[] { "TryReformCaravan", "TryFormAndSendCaravan", "DebugTryFormCaravanInstantly" }
                    .Select(name => AccessTools.DeclaredMethod(typeof(Dialog_FormCaravan), name, Type.EmptyTypes)).ToArray();
                var changes = new[] { "AddItems", "Notify_CountChanged", "Reset" }
                    .Select(name => AccessTools.DeclaredMethod(session, name)).ToArray();
                if (proxyType == null || refresh == null || prepareDialog == null || sessionFaction == null ||
                    pushFaction == null || popFaction == null || actions.Any(m => m == null) || changes.Any(m => m == null))
                    throw new MissingMethodException("Caravan escort refresh/session/faction targets did not resolve");

                // Validate the entire rewrite before installing any callbacks.
                EscortRefreshTranspiler(PatchProcessor.GetOriginalInstructions(refresh)).ToList();
                harmony.Patch(refresh, transpiler: new HarmonyMethod(typeof(Patch_CaravanEscortSelectionMp), nameof(EscortRefreshTranspiler)));
                foreach (var action in actions)
                    harmony.Patch(action, prefix: new HarmonyMethod(typeof(Patch_CaravanEscortSelectionMp), nameof(BeforeExecute)) { priority = Priority.Last });
                foreach (var change in changes)
                    harmony.Patch(change, postfix: new HarmonyMethod(typeof(Patch_CaravanEscortSelectionMp), nameof(AfterSessionChange)));
                Log.Message("[MP-MeowOnlineShop] Caravan escort selection: local proxy refresh is read-only for escort counts; shared initialization/count/reset and all three departure actions reconcile escorts (boundaries=7).");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILURE caravan escort selection: " + e);
            }
        }

        internal static IEnumerable<CodeInstruction> EscortRefreshTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var biotech = AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.BiotechActive));
            var matches = code.Where(c => c.Calls(biotech)).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException("Expected exactly one Biotech escort block in Notify_TransferablesChanged; found " + matches.Count);
            var replacement = AccessTools.Method(typeof(Patch_CaravanEscortSelectionMp), nameof(ShouldRefreshEscorts));
            foreach (var instruction in code)
            {
                if (!instruction.Calls(biotech))
                {
                    yield return instruction;
                    continue;
                }
                // Keep branch targets and exception-block boundaries on the first
                // replacement instruction, not on the call that consumes this.
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels);
                load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
        }

        private static bool ShouldRefreshEscorts(Dialog_FormCaravan dialog)
        {
            return ModsConfig.BiotechActive && (!MP.IsInMultiplayer ||
                !proxyType.IsInstanceOfType(dialog) || ReferenceEquals(sharedRefreshDialog, dialog));
        }

        private static void BeforeExecute(Dialog_FormCaravan __instance)
        {
            if (!MP.IsInMultiplayer || MP.InInterface || !proxyType.IsInstanceOfType(__instance)) return;
            // MP's native sync method already established map/faction context.
            // Recompute before GetPawnsFromTransferables, even for an old session
            // whose counts were previously changed by an unpatched UI.
            RefreshShared(__instance);
        }

        private static void AfterSessionChange(object __instance)
        {
            if (!MP.IsInMultiplayer || MP.InInterface) return;
            var faction = (Faction)sessionFaction.GetValue(__instance);
            if (faction == null) throw new InvalidOperationException("Caravan session has no faction");
            pushFaction.Invoke(null, new object[] { faction, false });
            try
            {
                var dialog = (Dialog_FormCaravan)prepareDialog.Invoke(__instance, null);
                RefreshShared(dialog);
            }
            finally
            {
                popFaction.Invoke(null, null);
            }
        }

        private static void RefreshShared(Dialog_FormCaravan dialog)
        {
            var previous = sharedRefreshDialog;
            sharedRefreshDialog = dialog;
            try { refresh.Invoke(dialog, null); }
            finally { sharedRefreshDialog = previous; }
        }
    }
}
