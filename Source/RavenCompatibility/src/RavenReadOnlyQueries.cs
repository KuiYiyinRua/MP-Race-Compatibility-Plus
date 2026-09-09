using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenReadOnlyQueries
    {
        private static Type hypnosis;
        internal static void Apply(Harmony harmony)
        {
            hypnosis = AccessTools.TypeByName("RavenRace.Features.Hypnosis.WorldComponent_Hypnosis")
                ?? throw new TypeLoadException("Raven hypnosis world component");
            Patch(harmony, hypnosis, "GetCommandCooldown", nameof(Cooldown));
            Patch(harmony, AccessTools.TypeByName("RavenRace.Features.Hypnosis.Commands.HypnosisCommandUtility"),
                "GetCooldownEndTick", nameof(CooldownEnd));
            Patch(harmony, AccessTools.TypeByName("RavenRace.Features.RavenLiquidPipe.CompRavenLiquidStorage"),
                "NormalizeRecipeSlotsAgainstCache", nameof(NormalizeLiquidSlots));
            var port = AccessTools.TypeByName("RavenRace.Features.RavenConveyor.CompRavenConveyorPort");
            harmony.Patch(AccessTools.DeclaredMethod(port, "ResolveBoundConveyor")
                ?? throw new MissingMethodException(port.FullName, "ResolveBoundConveyor"),
                prefix: new HarmonyMethod(typeof(RavenReadOnlyQueries), nameof(BeforeResolve)),
                finalizer: new HarmonyMethod(typeof(RavenReadOnlyQueries), nameof(AfterResolve)));
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix)
        {
            if (type == null) throw new TypeLoadException(method);
            harmony.Patch(AccessTools.DeclaredMethod(type, method) ?? throw new MissingMethodException(type.FullName, method),
                prefix: new HarmonyMethod(typeof(RavenReadOnlyQueries), prefix));
        }

        private static bool NormalizeLiquidSlots(ref bool ___recipeSlotsDirty)
        {
            if (!MP.InInterface) return true;
            // Capacity/inspect queries can rebuild the cache, but may not discard
            // stored liquid. Keep it dirty so simulation performs normalization.
            ___recipeSlotsDirty = true;
            return false;
        }

        private static int ReadCooldown(object instance, int pawn, string command)
        {
            var entries = (Dictionary<int, Dictionary<string, int>>)AccessTools.Field(hypnosis, "commandCooldowns").GetValue(instance);
            return !string.IsNullOrEmpty(command) && entries.TryGetValue(pawn, out var commands)
                && commands.TryGetValue(command, out int tick) && tick > RavenHypnosisClocks.PawnIdTick(pawn) ? tick : 0;
        }

        private static bool Cooldown(object __instance, int __0, string __1, ref int __result)
        {
            if (!MP.InInterface) return true;
            __result = ReadCooldown(__instance, __0, __1);
            return false;
        }

        // UI reads retain the legacy lookup, but leave migration and expiry cleanup to simulation.
        private static bool CooldownEnd(Pawn __0, Def __1, ref int __result)
        {
            if (!MP.InInterface) return true;
            __result = 0;
            if (__0 == null || __1 == null) return false;
            var instance = AccessTools.Property(hypnosis, "Instance").GetValue(null);
            __result = ReadCooldown(instance, __0.thingIDNumber, __1.defName);
            foreach (string field in new[] { "activeHediffDef", "jobDef" })
            {
                if (__result > 0) break;
                var legacy = AccessTools.Field(__1.GetType(), field).GetValue(__1) as Def;
                if (legacy != null) __result = ReadCooldown(instance, __0.thingIDNumber, legacy.defName);
            }
            return false;
        }

        private static void BeforeResolve(int ___boundConveyorLayerId, out int? __state)
            => __state = MP.InInterface ? ___boundConveyorLayerId : (int?)null;

        // ResolveBoundConveyor only writes this binding field; tmpConveyors is an ephemeral query buffer.
        private static void AfterResolve(ref int ___boundConveyorLayerId, int? __state)
        {
            if (__state.HasValue) ___boundConveyorLayerId = __state.Value;
        }
    }
}
