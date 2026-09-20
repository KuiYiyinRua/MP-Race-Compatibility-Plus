using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianRecruitmentPayment
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.GameComp_NivarianRecruitment")
                ?? throw new TypeLoadException("Nivarian.GameComp_NivarianRecruitment");
            harmony.Patch(AccessTools.DeclaredMethod(type, "TrySpendSilver", new[] { typeof(int) })
                ?? throw new MissingMethodException(type.FullName, "TrySpendSilver"),
                prefix: new HarmonyMethod(typeof(NivarianRecruitmentPayment), nameof(Spend)));
            Log.Message("[TaleNivarianCompat] recruitment silver spending uses stable map/item order.");
        }
        static bool Spend(int amount, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            // Preserve the original all-maps payment scope. Lister insertion order is rebuilt
            // on loading, so it cannot decide which stack survives a partial payment.
            var silver = Find.Maps.OrderBy(m => m.uniqueID)
                .SelectMany(m => m.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                    .Where(t => t.def == ThingDefOf.Silver).OrderBy(t => t.thingIDNumber)).ToArray();
            if (silver.Sum(t => (long)t.stackCount) < amount) { __result = false; return false; }
            int remaining = amount;
            foreach (var stack in silver)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, stack.stackCount);
                if (take == 0) continue;
                stack.SplitOff(take).Destroy();
                remaining -= take;
            }
            __result = remaining <= 0;
            return false;
        }
    }
}
