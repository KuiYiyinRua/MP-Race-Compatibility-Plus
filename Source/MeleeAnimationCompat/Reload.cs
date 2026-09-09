using System;
using System.Collections.Generic;
using System.Linq;
using AM;
using AM.Grappling;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    internal static class Reload
    {
        private static readonly Action AddPending = AccessTools.MethodDelegate<Action>(
            AccessTools.Method(typeof(AnimRenderer), "AddFromPostLoad"));
        private static readonly HashSet<Pawn> Grabs = (HashSet<Pawn>)AccessTools.Field(typeof(GrabUtility), "pawnsBeingTargetedByGrapples").GetValue(null);
        private static void Postfix()
        {
            if (!Bootstrap.Active) return;
            AnimationClock.Advancing = true;
            try { AddPending(); }
            finally { AnimationClock.Advancing = false; }
            Grabs.Clear();
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
                foreach (var pawn in map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber))
                    if (pawn.jobs?.curDriver is JobDriver_GrapplePawn driver && !driver.HasEnsnared && driver.GrappledPawn != null)
                        GrabUtility.TryRegisterGrabAttempt(driver.GrappledPawn);
        }
    }

    [HarmonyPatch]
    internal static class LiveGrappleReservations
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GrabUtility),nameof(GrabUtility.IsBeingTargetedForGrapple));
            yield return AccessTools.Method(typeof(GrabUtility),nameof(GrabUtility.TryRegisterGrabAttempt));
        }
        private static bool Prefix(Pawn pawn,System.Reflection.MethodBase __originalMethod,ref bool __result)
        {
            if(!Bootstrap.Active) return true;
            bool reserved=pawn!=null && Find.Maps.SelectMany(m=>m.mapPawns.AllPawnsSpawned)
                .Any(p=>p.jobs?.curDriver is JobDriver_GrapplePawn d && !d.HasEnsnared && d.GrappledPawn==pawn);
            __result=pawn!=null && (__originalMethod.Name==nameof(GrabUtility.TryRegisterGrabAttempt) ? !reserved : reserved);
            return false;
        }
    }

    [HarmonyPatch(typeof(AnimRenderer), nameof(AnimRenderer.OnStart))]
    internal static class AlreadyStartedOnLoad
    {
        private static bool Prefix(bool ___hasStarted) => !Bootstrap.Active || !___hasStarted;
    }
}
