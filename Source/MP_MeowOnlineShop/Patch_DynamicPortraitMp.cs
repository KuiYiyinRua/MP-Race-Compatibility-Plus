using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Dynamic Portraits 1.6 assumes every building used as a pawn job target
    /// has ThingDef.graphicData. Some modded/non-rendered building targets do
    /// not. DrawWorkItems then dereferences graphicData every OnGUI pass and
    /// floods the client log from the colonist bar.
    ///
    /// This is presentation-only and client-local. In multiplayer, skip only
    /// the unsafe work-item overlay for that pawn/frame; the normal portrait,
    /// mood bar, job and simulation state remain untouched.
    /// </summary>
    internal static class Patch_DynamicPortraitMp
    {
        private const string RenderColonistTypeName =
            "DynamicPortrait.RenderColonist";

        private static bool _applied;
        private static bool _loggedUnsafeTarget;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            Type renderColonistType = AccessTools.TypeByName(
                RenderColonistTypeName);
            if (renderColonistType == null)
                return;

            try
            {
                MethodInfo target = AccessTools.Method(
                    renderColonistType,
                    "DrawWorkItems",
                    new[] { typeof(Pawn), typeof(UnityEngine.Rect).MakeByRefType() });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_DynamicPortraitMp),
                    nameof(DrawWorkItemsPrefix));

                if (target == null || prefix == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Dynamic Portraits work-item " +
                        "guard target resolution failed.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Dynamic Portraits MP work-item " +
                    "null guard applied.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Dynamic Portraits work-item " +
                    "guard failed: " + e.Message);
            }
        }

        private static bool DrawWorkItemsPrefix(Pawn pawn)
        {
            if (!MP.IsInMultiplayer)
                return true;

            Job job = pawn?.CurJob;
            if (job == null)
                return false;

            Thing unsafeTarget = FindUnsafeWorkTarget(job.targetA) ??
                                 FindUnsafeWorkTarget(job.targetB) ??
                                 FindUnsafeWorkTarget(job.targetC);
            if (unsafeTarget == null)
                return true;

            if (!_loggedUnsafeTarget)
            {
                _loggedUnsafeTarget = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Dynamic Portraits skipped an unsafe " +
                    "work-item overlay in multiplayer " +
                    $"(pawn={pawn?.LabelShort ?? "null"}, " +
                    $"target={unsafeTarget.def?.defName ?? "null"}, " +
                    "reason=target definition cannot be drawn safely). " +
                    "Further occurrences are suppressed.");
            }

            return false;
        }

        private static Thing FindUnsafeWorkTarget(LocalTargetInfo target)
        {
            Thing thing = target.Thing;
            ThingDef def = thing?.def;
            Type thingClass = def?.thingClass;
            if (thing == null || def == null || thingClass == null)
                return thing;

            bool isBuilding = thingClass == typeof(Building) ||
                              thingClass.IsSubclassOf(typeof(Building));
            return isBuilding && def.graphicData == null ? thing : null;
        }
    }
}
