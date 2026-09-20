using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianResearchCache
    {
        sealed class Owner { internal Faction Faction; }
        static readonly ConditionalWeakTable<object, Owner> Owners = new ConditionalWeakTable<object, Owner>();
        public struct Snapshot { public bool Restore; public int Tick; public bool Finished; }
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.GameComp_NivarianGlobalPowerTransmitter") ?? throw new TypeLoadException("Nivarian global power");
            foreach (var field in new[] { "_cachedResearchTick", "_cachedResearchFinished" })
                if (AccessTools.Field(type, field) == null) throw new MissingFieldException(type.FullName, field);
            harmony.Patch(AccessTools.PropertyGetter(type, "IsWirelessResearchCompleted"),
                prefix: new HarmonyMethod(typeof(NivarianResearchCache), nameof(Before)),
                finalizer: new HarmonyMethod(typeof(NivarianResearchCache), nameof(After)));
            Log.Message("[TaleNivarianCompat] wireless research cache is faction-scoped; interface reads preserve simulation cache.");
        }
        static void Before(object __instance, ref int ____cachedResearchTick, bool ____cachedResearchFinished, out Snapshot __state)
        {
            __state = default;
            if (!MP.IsInMultiplayer) return;
            if (MP.InInterface)
            {
                __state = new Snapshot { Restore = true, Tick = ____cachedResearchTick, Finished = ____cachedResearchFinished };
                ____cachedResearchTick = -1;
                return;
            }
            var owner = Owners.GetOrCreateValue(__instance);
            if (owner.Faction != Faction.OfPlayer)
            {
                ____cachedResearchTick = -1;
                owner.Faction = Faction.OfPlayer;
            }
        }
        static void After(ref int ____cachedResearchTick, ref bool ____cachedResearchFinished, Snapshot __state)
        {
            if (!__state.Restore) return;
            ____cachedResearchTick = __state.Tick;
            ____cachedResearchFinished = __state.Finished;
        }
    }
}
