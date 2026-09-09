using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Visual Brutality (Thumb.GoreMod2) turns dismemberment into real Things:
    /// Hediff_MissingPart.PostAdd spawns FlyingFlesh projectiles and launches
    /// HeadProjectiles, and those projectiles later spawn meat/HeadItems on
    /// Destroy. Every object gets a positive thing ID and consumes the active
    /// map Rand stream, so a peer-local VFX branch advances synchronized state
    /// and shifts every later thing ID. Desync-285 reached
    /// DismembermentUtils.MakeFlyingFlesh / SpawnFragment one tick before its
    /// first divergence; Desync-289 then shows the same map tick spawning an
    /// Explosion with thing IDs already 4 apart on host/client.
    ///
    /// The visual fragments and head projectile are cosmetic only in MP, so
    /// suppress those object-producing flows on every peer. Rand used by
    /// FlyingFlesh.SpawnSetup (Rot4.Random) is additionally scoped so a
    /// projectile loaded from an older snapshot cannot advance the shared map
    /// stream. Rendering-only corpse masks and colors are left untouched.
    /// </summary>
    internal static class Patch_VisualBrutalityMp
    {
        private const int SpawnSeedSalt = 0x56424652; // "VBFR"

        [ThreadStatic]
        private static bool _spawnScopeActive;

        private static bool _applied;
        private static bool _logSpamSuppressed;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            Type fragmentsUtils =
                AccessTools.TypeByName("VisualBrutalityFragments.DismembermentUtils");
            Type flyingFleshType =
                AccessTools.TypeByName("VisualBrutalityFragments.FlyingFlesh");
            Type headUtilityType =
                AccessTools.TypeByName("VisualBrutalityHead.DecapitationUtility");
            Type vbLogType =
                AccessTools.TypeByName("VisualBrutalityCorpses.Utils.VBLog");

            int skippedFlows = 0;
            skippedFlows += TryPatchSkip(
                harmony,
                fragmentsUtils,
                "MakeFlyingFlesh",
                new[] { typeof(Pawn), typeof(float) });
            skippedFlows += TryPatchSkip(
                harmony,
                fragmentsUtils,
                "TryDestroyHead",
                new[] { typeof(Pawn) });
            skippedFlows += TryPatchSkip(
                harmony,
                headUtilityType,
                "LaunchHead",
                new[] { typeof(Pawn) });

            bool spawnSetupScope =
                TryPatchSpawnSetupRandScope(harmony, flyingFleshType);
            _logSpamSuppressed = TryPatchLogSpamSuppression(
                harmony,
                vbLogType);

            if (skippedFlows == 0 && !spawnSetupScope && !_logSpamSuppressed)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Visual Brutality MP guard skipped " +
                    "(target mod not active or signatures changed).");
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Visual Brutality MP guard active: " +
                $"skippedFlows={skippedFlows}, spawnSetupRandScope={spawnSetupScope}, " +
                $"logSpamSuppressed={_logSpamSuppressed}.");
        }

        private static bool TryPatchLogSpamSuppression(
            Harmony harmony,
            Type vbLogType)
        {
            if (harmony == null || vbLogType == null)
                return false;

            MethodInfo target = AccessTools.Method(
                vbLogType,
                "Message",
                new[] { typeof(string) });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_VisualBrutalityMp),
                nameof(VbMessagePrefix));
            if (target == null || prefix == null)
                return false;

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Visual Brutality log spam suppression " +
                    $"failed: {e.Message}");
                return false;
            }
        }

        private static bool VbMessagePrefix(string message)
        {
            // Visual Brutality logs this debug line once per dead-pawn render
            // frame, which floods the multiplayer log window and even breaks
            // EditWindow_Log while it is open.
            return !string.Equals(
                message,
                "Try to apply head graphics",
                StringComparison.Ordinal);
        }

        private static int TryPatchSkip(
            Harmony harmony,
            Type type,
            string methodName,
            Type[] parameterTypes)
        {
            if (type == null || harmony == null)
                return 0;

            MethodInfo target =
                AccessTools.Method(type, methodName, parameterTypes);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_VisualBrutalityMp),
                nameof(SkipVisualFlowPrefix));
            if (target == null || prefix == null)
                return 0;

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Visual Brutality skip patch failed " +
                    $"for {type.Name}.{methodName}: {e.Message}");
                return 0;
            }
        }

        private static bool TryPatchSpawnSetupRandScope(
            Harmony harmony,
            Type type)
        {
            if (type == null || harmony == null)
                return false;

            MethodInfo target = AccessTools.Method(
                type,
                "SpawnSetup",
                new[] { typeof(Map), typeof(bool) });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_VisualBrutalityMp),
                nameof(SpawnSetupPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_VisualBrutalityMp),
                nameof(SpawnSetupFinalizer));
            if (target == null || prefix == null || finalizer == null)
                return false;

            try
            {
                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    },
                    finalizer: new HarmonyMethod(finalizer)
                    {
                        priority = Priority.Last
                    });
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Visual Brutality spawn Rand scope " +
                    $"failed: {e.Message}");
                return false;
            }
        }

        private static bool SkipVisualFlowPrefix()
        {
            return !MP.IsInMultiplayer;
        }

        private static void SpawnSetupPrefix(
            Thing __instance,
            Map map,
            ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer ||
                __instance == null ||
                _spawnScopeActive)
            {
                return;
            }

            try
            {
                int seed = Gen.HashCombineInt(
                    SpawnSeedSalt,
                    __instance.thingIDNumber);
                seed = Gen.HashCombineInt(
                    seed,
                    map?.uniqueID ?? 0);
                seed = Gen.HashCombineInt(
                    seed,
                    Find.TickManager?.TicksGame ?? 0);

                Rand.PushState(seed);
                _spawnScopeActive = true;
                __state = true;
            }
            catch
            {
                __state = false;
                _spawnScopeActive = false;
            }
        }

        private static Exception SpawnSetupFinalizer(
            Exception __exception,
            bool __state)
        {
            if (__state)
            {
                try
                {
                    Rand.PopState();
                }
                finally
                {
                    _spawnScopeActive = false;
                }
            }

            return __exception;
        }
    }
}
