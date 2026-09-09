using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenMountActions
    {
        private const string Ns = "RavenRace.Features.Creatures.GreatRaven.";
        private static MethodInfo dismount, getMount;
        private static FieldInfo mountCache;
        private sealed class CacheState { public Pawn rider, mount; public bool present; }
        internal static void Apply(Harmony harmony)
        {
            var comp = AccessTools.TypeByName(Ns + "CompRavenMountable");
            var closure = comp?.GetNestedType("<>c__DisplayClass28_0", BindingFlags.NonPublic);
            if (closure == null || AccessTools.Field(closure, "localPawn") == null ||
                AccessTools.Field(closure, "<>4__this") == null || AccessTools.DeclaredMethod(closure, "<OpenAssignRiderMenu>b__0") == null)
                throw new MissingMemberException("Raven rider assignment closure");
            MP.RegisterSyncDelegate(comp, "<>c__DisplayClass28_0", "<OpenAssignRiderMenu>b__0", new[] { "<>4__this", "localPawn" });
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(comp, "<CompGetGizmosExtra>b__16_0"), null);
            var utility = AccessTools.TypeByName(Ns + "RavenMountUtility");
            mountCache = AccessTools.DeclaredField(utility, "riderMountCache") ?? throw new MissingFieldException("Raven rider cache");
            harmony.Patch(AccessTools.DeclaredMethod(utility, "NotifyDraftChanged"), prefix: new HarmonyMethod(typeof(RavenMountActions), nameof(ReplayOnly)));
            foreach (var name in new[] { "TryGetCachedMountForRider", "FindMountForRider" })
                harmony.Patch(AccessTools.DeclaredMethod(utility, name), prefix: new HarmonyMethod(typeof(RavenMountActions), nameof(BeforeQuery)), finalizer: new HarmonyMethod(typeof(RavenMountActions), nameof(AfterQuery)));
            dismount = AccessTools.DeclaredMethod(utility, "Dismount", new[] { typeof(Pawn), typeof(Pawn), typeof(string), typeof(bool), typeof(bool) });
            getMount = AccessTools.DeclaredMethod(utility, "GetMountComp");
            harmony.Patch(dismount, prefix: new HarmonyMethod(typeof(RavenMountActions), nameof(BeforeDismount)));
            MP.RegisterSyncMethod(typeof(RavenMountActions), nameof(Dismount));
            var flight = AccessTools.TypeByName(Ns + "RavenMountedFlightCommandUtility");
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(flight, "ExecuteCommand"), null);
        }
        // MP queues Drafted's setter, but ordinary Harmony postfixes still run.
        // Let its shared replay perform the paired draft mutation exactly once.
        private static bool ReplayOnly() => !MP.InInterface;
        private static void BeforeQuery(Pawn __0, out CacheState __state)
        {
            __state = null; if (!MP.InInterface || __0 == null) return;
            var cache = (Dictionary<Pawn, Pawn>)mountCache.GetValue(null);
            __state = new CacheState { rider = __0, present = cache.TryGetValue(__0, out var mount), mount = mount };
        }
        private static void AfterQuery(CacheState __state)
        {
            if (__state == null) return;
            var cache = (Dictionary<Pawn, Pawn>)mountCache.GetValue(null);
            if (__state.present) cache[__state.rider] = __state.mount; else cache.Remove(__state.rider);
        }
        private static bool BeforeDismount(Pawn __0, Pawn __1, string __2, bool __3, bool __4, ref bool __result)
        {
            if (!MP.InInterface) return true;
            Dismount(__0, __1, __2, __3, __4);
            __result = false;
            return false;
        }
        private static void Dismount(Pawn mount, Pawn rider, string reason, bool crash, bool undraft)
        {
            if (mount == null || rider == null) return;
            var comp = getMount.Invoke(null, new object[] { mount });
            if (comp == null || AccessTools.Property(comp.GetType(), "Rider").GetValue(comp) != rider) return;
            dismount.Invoke(null, new object[] { mount, rider, reason, crash, undraft });
        }
    }
}
