using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// AncotLibrary.Beam keeps a reference to its attached beam mote and
    /// serializes it through Scribe_References. Motes are purely visual and
    /// do not have a numeric Thing ID, so a live Milira_Mote_LaserPulse is
    /// written as "Thing_Milira_Mote_LaserPulse" and fails to load in an MP
    /// snapshot. Keep the projectile's real state serialization intact while
    /// omitting only this visual reference in multiplayer.
    /// </summary>
    internal static class Patch_AncotBeamMoteSaveCompat
    {
        private const string BeamTypeName = "AncotLibrary.Beam";

        private static bool _applied;
        private static bool _transpilerMatched;
        private static MethodInfo _replacement;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;

            Type beamType = AccessTools.TypeByName(BeamTypeName);
            MethodInfo target = beamType == null
                ? null
                : AccessTools.DeclaredMethod(beamType, nameof(Thing.ExposeData));
            MethodInfo transpiler = AccessTools.Method(
                typeof(Patch_AncotBeamMoteSaveCompat),
                nameof(ExposeDataTranspiler));
            _replacement = AccessTools.Method(
                typeof(Patch_AncotBeamMoteSaveCompat),
                nameof(LookBeamMoteReference));

            if (target == null || transpiler == null || _replacement == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Ancot Beam mote save compatibility " +
                    "skipped (Ancot Library is not active or its signature changed).");
                return;
            }

            try
            {
                _transpilerMatched = false;
                harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
                if (!_transpilerMatched)
                {
                    harmony.Unpatch(target, HarmonyPatchType.Transpiler, harmony.Id);
                    Log.Warning(
                        "[MP-MeowOnlineShop] Ancot Beam mote save compatibility " +
                        "skipped (the expected Mote reference was not found).");
                    return;
                }

                _applied = true;
                Log.Message(
                    "[MP-MeowOnlineShop] Ancot Beam mote save compatibility active: " +
                    "MP snapshots omit visual beam-mote references.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Ancot Beam mote save compatibility " +
                    "install failed: " + e.Message);
            }
        }

        private static IEnumerable<CodeInstruction> ExposeDataTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (IsBeamMoteReferenceLook(instruction.operand as MethodInfo))
                {
                    instruction.operand = _replacement;
                    _transpilerMatched = true;
                }

                yield return instruction;
            }
        }

        private static bool IsBeamMoteReferenceLook(MethodInfo method)
        {
            if (method == null || method.DeclaringType != typeof(Scribe_References) ||
                method.Name != nameof(Scribe_References.Look) ||
                !method.IsGenericMethod)
            {
                return false;
            }

            Type[] genericArguments = method.GetGenericArguments();
            return genericArguments.Length == 1 && genericArguments[0] == typeof(Mote);
        }

        private static void LookBeamMoteReference(
            ref Mote mote,
            string label,
            bool saveDestroyedThings)
        {
            if (MP.IsInMultiplayer)
            {
                // Also accepts snapshots written before this patch: avoiding
                // Scribe_References.Look prevents their invalid load ID from
                // reaching LoadedObjectDirectory during cross-reference pass.
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                    mote = null;
                return;
            }

            Scribe_References.Look(ref mote, label, saveDestroyedThings);
        }
    }
}
