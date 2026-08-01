using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RJW Now with balls assigns genital type and size from Verse.Rand in a
    /// PawnGenerator postfix and in its late world-pawn repair pass. With
    /// asynchronous maps, those callbacks can run while different map Rand
    /// contexts are selected on the two peers. Seed the assignment by stable
    /// pawn identity and restore the surrounding stream after the call.
    /// </summary>
    internal static class Patch_BallzAutoOrganRandIsolation
    {
        private const string PackageId = "TeheeItsMe525.RJWGenderOrgansMod";
        private const string AutoOrganAdderTypeName = "Ballz.AutoOrganAdder";
        private const string CheckerComponentTypeName =
            "Ballz.NeuteredCheckerWorldComponent";
        private const int SeedSalt = 0x42414C5A;
        private const int CheckInterval = 2500;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type adderType = AccessTools.TypeByName(AutoOrganAdderTypeName);
            var target = adderType == null
                ? null
                : AccessTools.Method(
                    adderType,
                    "AddOrgansToPawn",
                    new[] { typeof(Pawn) });
            if (target == null || !target.IsStatic ||
                target.ReturnType != typeof(void))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][Ballz] AddOrgansToPawn signature drift; " +
                    "deterministic organ assignment was not installed.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_BallzAutoOrganRandIsolation),
                    nameof(Prefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_BallzAutoOrganRandIsolation),
                    nameof(Finalizer)))
                {
                    priority = Priority.Last
                });

            Type checkerType = AccessTools.TypeByName(CheckerComponentTypeName);
            var checkerTick = checkerType == null
                ? null
                : AccessTools.Method(checkerType, "WorldComponentTick", Type.EmptyTypes);
            if (checkerTick == null || checkerTick.ReturnType != typeof(void))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][Ballz] NeuteredCheckerWorldComponent tick " +
                    "signature drift; join-time repair suppression and deterministic " +
                    "check scheduling were not installed.");
            }
            else
            {
                harmony.Patch(
                    checkerTick,
                    prefix: new HarmonyMethod(AccessTools.Method(
                        typeof(Patch_BallzAutoOrganRandIsolation),
                        nameof(CheckerTickPrefix)))
                    {
                        priority = Priority.First
                    });
            }

            Log.Message(
                "[MP-MeowOnlineShop][Ballz] deterministic per-pawn organ Rand " +
                "isolation, join repair suppression, and tick-based checker " +
                "scheduling are active in multiplayer.");
        }

        private static void Prefix(Pawn pawn, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || pawn == null)
                return;

            int seed = Gen.HashCombineInt(SeedSalt, pawn.thingIDNumber);
            seed = Gen.HashCombineInt(seed, pawn.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(seed, (int)pawn.gender);
            Rand.PushState(seed);
            __state = true;
        }

        private static void CheckerTickPrefix(
            ref int ___tickCounter,
            ref bool ___hasRunInitialCheck)
        {
            if (!MP.IsInMultiplayer)
                return;

            // This component is appended from a World.ExposeData postfix. A joining
            // client therefore does not deserialize hasRunInitialCheck and would run
            // the migration pass locally, adding organs that already exist on the
            // host snapshot. PawnGenerator handles all genuinely new pawns.
            ___hasRunInitialCheck = true;

            // Ballz does not serialize tickCounter. Derive the value from the shared
            // game tick so its 2500-tick hormone/neutering scan fires simultaneously
            // after a late join instead of from each process's construction time.
            int tick = Find.TickManager?.TicksGame ?? 0;
            int modulo = tick % CheckInterval;
            if (modulo < 0)
                modulo += CheckInterval;
            ___tickCounter = (modulo + CheckInterval - 1) % CheckInterval;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
