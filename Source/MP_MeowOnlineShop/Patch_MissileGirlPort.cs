using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// MP-safe ports of MissileGirl's deterministic optimization ideas.
    ///
    /// This is a port, not a compatibility shim: the patches live in this mod,
    /// use their own Harmony owner, and never depend on MissileGirl being
    /// installed. Process-local caches and camera/Rand-driven paths from the
    /// original mod are intentionally not copied.
    /// </summary>
    internal static class Patch_MissileGirlPort
    {
        internal const string HarmonyId = "mp.meowonlineshop.missilegirlport";

        private static readonly Harmony Harmony = new Harmony(HarmonyId);

        private static bool _applied;
        private static bool _mothballPatched;
        private static bool _beautyPatched;
        private static bool _timetablePatched;

        private static Pawn _beautyPawn;
        private static AccessTools.FieldRef<Pawn_NeedsTracker, Pawn> _needsPawnFieldRef;
        private static bool _needsPawnFieldRefAttempted;

        internal static void Apply()
        {
            if (_applied)
                return;
            _applied = true;

            try
            {
                PatchMothballPort();
                PatchBeautyPort();
                PatchTimetableFix();

                Log.Message(
                    "[MP-MeowOnlineShop] MissileGirl port initialized: " +
                    $"mothball={_mothballPatched}, beauty={_beautyPatched}, " +
                    $"timetableFix={_timetablePatched}.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MissileGirl port apply failed: " + e);
            }
        }

        private static void PatchMothballPort()
        {
            MethodInfo target = AccessTools.PropertyGetter(
                typeof(HediffDef),
                nameof(HediffDef.AlwaysAllowMothball));
            MethodInfo postfix = AccessTools.Method(
                typeof(Patch_MissileGirlPort),
                nameof(AlwaysAllowMothball_Postfix));
            if (target == null || postfix == null)
                return;

            Harmony.Patch(
                target,
                postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
            _mothballPatched = true;
        }

        private static void PatchBeautyPort()
        {
            MethodInfo fillTarget = AccessTools.Method(
                typeof(BeautyUtility),
                nameof(BeautyUtility.FillBeautyRelevantCells));
            MethodInfo transpiler = AccessTools.Method(
                typeof(Patch_MissileGirlPort),
                nameof(FillBeautyRelevantCells_Transpiler));
            if (fillTarget == null || transpiler == null)
                return;

            Harmony.Patch(
                fillTarget,
                transpiler: new HarmonyMethod(transpiler) { priority = Priority.First });

            MethodInfo needsTarget = AccessTools.Method(
                typeof(Pawn_NeedsTracker),
                nameof(Pawn_NeedsTracker.NeedsTrackerTickInterval));
            MethodInfo needsPrefix = AccessTools.Method(
                typeof(Patch_MissileGirlPort),
                nameof(NeedsTrackerTickInterval_Prefix));
            MethodInfo needsFinalizer = AccessTools.Method(
                typeof(Patch_MissileGirlPort),
                nameof(NeedsTrackerTickInterval_Finalizer));
            if (needsTarget != null && needsPrefix != null && needsFinalizer != null)
            {
                Harmony.Patch(
                    needsTarget,
                    prefix: new HarmonyMethod(needsPrefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(needsFinalizer) { priority = Priority.Last });
            }

            _beautyPatched = true;
        }

        private static void PatchTimetableFix()
        {
            MethodInfo target = AccessTools.Method(
                typeof(Pawn_TimetableTracker),
                nameof(Pawn_TimetableTracker.GetAssignment),
                new[] { typeof(int) });
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_MissileGirlPort),
                nameof(TimetableGetAssignment_Finalizer));
            if (target == null || finalizer == null)
                return;

            Harmony.Patch(
                target,
                finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });
            _timetablePatched = true;
        }

        private static void AlwaysAllowMothball_Postfix(
            HediffDef __instance,
            ref bool __result)
        {
            if (!IsMothballPortActive() || __instance == null || __result)
                return;

            if (__instance.IsAddiction ||
                __instance.defName.EndsWith("Addiction", StringComparison.Ordinal) ||
                __instance.defName.EndsWith("Tolerance", StringComparison.Ordinal))
            {
                __result = true;
            }
        }

        private static IEnumerable<CodeInstruction> FillBeautyRelevantCells_Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            FieldInfo sampleField = AccessTools.Field(
                typeof(BeautyUtility),
                nameof(BeautyUtility.SampleNumCells_Beauty));
            MethodInfo getter = AccessTools.Method(
                typeof(Patch_MissileGirlPort),
                nameof(GetBeautySampleNumCells));
            bool replaced = false;

            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                if (!replaced &&
                    code.opcode == OpCodes.Ldsfld &&
                    Equals(code.operand, sampleField) &&
                    getter != null)
                {
                    yield return new CodeInstruction(OpCodes.Call, getter)
                    {
                        labels = code.labels,
                        blocks = code.blocks
                    };
                    replaced = true;
                }
                else
                {
                    yield return code;
                }
            }

            if (!replaced)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] MissileGirl beauty port target pattern " +
                    "not found; vanilla beauty sampling retained.");
            }
        }

        private static int GetBeautySampleNumCells()
        {
            if (!IsBeautyPortActive() || _beautyPawn == null || _beautyPawn.pather == null)
                return BeautyUtility.SampleNumCells_Beauty;

            int min = GenRadial.NumCellsInRadius(2.6f);
            int max = GenRadial.NumCellsInRadius(6.9f);
            if (_beautyPawn.InBed() || _beautyPawn.Downed)
                return min;

            float progress = Mathf.Clamp01(
                (GenTicks.TicksGame - _beautyPawn.pather.LastMovedTick) / 60f);
            return Mathf.CeilToInt(Mathf.Lerp(min, max, progress));
        }

        private static void NeedsTrackerTickInterval_Prefix(Pawn_NeedsTracker __instance)
        {
            _beautyPawn = GetNeedsPawn(__instance);
        }

        private static void NeedsTrackerTickInterval_Finalizer()
        {
            _beautyPawn = null;
        }

        private static Pawn GetNeedsPawn(Pawn_NeedsTracker tracker)
        {
            if (tracker == null)
                return null;

            if (_needsPawnFieldRef == null && !_needsPawnFieldRefAttempted)
            {
                _needsPawnFieldRefAttempted = true;
                try
                {
                    _needsPawnFieldRef =
                        AccessTools.FieldRefAccess<Pawn_NeedsTracker, Pawn>("pawn");
                }
                catch
                {
                    // Keep the beauty port non-fatal if the field shape changes.
                }
            }

            return _needsPawnFieldRef?.Invoke(tracker);
        }

        private static Exception TimetableGetAssignment_Finalizer(
            Exception __exception,
            Pawn_TimetableTracker __instance,
            int hour,
            ref TimeAssignmentDef __result)
        {
            if (__exception == null || !IsTimetableFixActive() || __instance == null)
                return __exception;

            try
            {
                __result = TimeAssignmentDefOf.Anything;
                __instance.SetAssignment(hour, TimeAssignmentDefOf.Anything);
                return null;
            }
            catch
            {
                return __exception;
            }
        }

        private static bool IsMothballPortActive()
        {
            return (MpMeowOnlineShopMod.Settings?.enableMpMissileGirlMothballPort ?? false) &&
                   MP.IsInMultiplayer;
        }

        private static bool IsBeautyPortActive()
        {
            return (MpMeowOnlineShopMod.Settings?.enableMpMissileGirlBeautyPort ?? false) &&
                   MP.IsInMultiplayer &&
                   !MP.IsExecutingSyncCommand &&
                   !MpRuntimeInfo.RequiresVanillaPerMapPipelines(out _);
        }

        private static bool IsTimetableFixActive()
        {
            return MpMeowOnlineShopMod.Settings?.enableMpMissileGirlTimetableFix ?? true;
        }
    }
}
