using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Elite Raid (trigger.eliteRaid) generates raid elite-level distributions
    /// through its own static `EliteRaid.ThreadSafeRandom`, a wall-clock-seeded
    /// System.Random. Two peers therefore derive different elite levels, powerup
    /// assignments, and pawn upgrades from the same raid, and then consume a
    /// different number of Verse.Rand draws while applying them — producing
    /// "Wrong random state on map X" / "Trace hashes don't match" desyncs at
    /// raid generation (Desync-101, and the raid-heavy moments of 95..100, 102).
    ///
    /// The narrow, deterministic boundary is `EliteRaid.ThreadSafeRandom`:
    /// in multiplayer every `Next/NextDouble` draw is taken from the
    /// synchronized Verse.Rand stream instead of a per-process System.Random.
    /// Elite raid generation runs inside the deterministic incident/storyteller
    /// context that Multiplayer replays on every peer, so drawing from the
    /// active Verse.Rand stream yields the identical elite distribution on all
    /// peers (the same pattern already validated for Static Quality).
    ///
    /// Single-player behavior is untouched (every gate checks MP.IsInMultiplayer).
    /// </summary>
    internal static class Patch_EliteRaidDeterminism
    {
        private const string ThreadSafeRandomTypeName = "EliteRaid.ThreadSafeRandom";

        private static int _patchedCount;

        internal static void Apply(Harmony harmony)
        {
            Type tsrType = AccessTools.TypeByName(ThreadSafeRandomTypeName);
            if (tsrType == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid ThreadSafeRandom type not found; deterministic raid Rand skipped.");
                return;
            }

            MethodInfo[] tsrMethods =
            {
                tsrType.GetMethod("Next", Type.EmptyTypes),
                tsrType.GetMethod("Next", new[] { typeof(int) }),
                tsrType.GetMethod("Next", new[] { typeof(int), typeof(int) }),
                tsrType.GetMethod("NextDouble", Type.EmptyTypes)
            };

            MethodInfo detNext = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism), nameof(DeterministicNextPrefix));
            MethodInfo detNextMax = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism), nameof(DeterministicNextMaxPrefix));
            MethodInfo detRange = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism), nameof(DeterministicRangePrefix));
            MethodInfo detDouble = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism), nameof(DeterministicNextDoublePrefix));

            MethodInfo[] prefixes = { detNext, detNextMax, detRange, detDouble };
            int patched = 0;
            for (int i = 0; i < tsrMethods.Length; i++)
            {
                if (tsrMethods[i] == null || prefixes[i] == null)
                    continue;
                try
                {
                    harmony.Patch(
                        tsrMethods[i],
                        prefix: new HarmonyMethod(prefixes[i])
                        {
                            priority = Priority.First
                        });
                    patched++;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Elite Raid ThreadSafeRandom patch failed on " +
                        $"{tsrMethods[i].Name}: {e.Message}");
                }
            }

            _patchedCount = patched;

            if (patched == 0)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid deterministic Rand NOT active: no ThreadSafeRandom methods patched.");
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Elite Raid deterministic Rand active: " +
                $"ThreadSafeRandom methods patched={patched}/4.");
        }

        // ---- Deterministic ThreadSafeRandom replacements ----
        // System.Random semantics: Next() is [0, Int32.MaxValue), Next(max) is
        // [0, max), Next(min,max) is [min, max), NextDouble() is [0, 1).
        // Each replacement draws from the synchronized Verse.Rand stream so both
        // peers derive the identical elite distribution.

        private static bool DeterministicNextPrefix(ref int __result)
        {
            if (!MP.IsInMultiplayer)
                return true;
            __result = Verse.Rand.Range(0, int.MaxValue);
            return false;
        }

        private static bool DeterministicNextMaxPrefix(int maxValue, ref int __result)
        {
            if (!MP.IsInMultiplayer)
                return true;
            __result = maxValue <= 0 ? 0 : Verse.Rand.Range(0, maxValue);
            return false;
        }

        // Harmony binds prefix parameters by name when an overload has several
        // parameters of the same type. The installed EliteRaid 1.5.2 declares
        // Next(int a, int maxValue), so a prefix parameter named minValue never
        // binds and the overload silently stays on the per-process Random
        // (startup log: "patch failed on Next ... patched=3/4"). Read the two
        // arguments from __args instead, which is independent of parameter names.
        private static bool DeterministicRangePrefix(object[] __args, ref int __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            int minValue = (int)__args[0];
            int maxValue = (int)__args[1];
            __result = maxValue <= minValue ? minValue : Verse.Rand.Range(minValue, maxValue);
            return false;
        }

        private static bool DeterministicNextDoublePrefix(ref double __result)
        {
            if (!MP.IsInMultiplayer)
                return true;
            __result = (double)Verse.Rand.Value;
            return false;
        }
    }
}
