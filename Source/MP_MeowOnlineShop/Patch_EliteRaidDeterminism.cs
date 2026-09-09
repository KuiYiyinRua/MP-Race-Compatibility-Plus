using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
    /// The deterministic boundaries are `EliteRaid.ThreadSafeRandom` and the
    /// `EliteRaid.EliteLevel.myRandom` getter. The latter is a separate
    /// ThreadLocal<System.Random> initialized from Environment.TickCount, so
    /// patching ThreadSafeRandom alone does not cover elite trait generation.
    /// In multiplayer every draw from both boundaries is taken from the
    /// synchronized Verse.Rand stream instead of a per-process random seed.
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
        private const string EliteLevelTypeName = "EliteRaid.EliteLevel";
        private const string CompressionHelperTypeName =
            "EliteRaid.PatchContinuityHelper";
        private const string DropPodPatchTypeName =
            "EliteRaid.DropPodUtility_Patch";

        private static int _patchedCount;
        private static bool _compressionCachePatched;
        private static int _simulationClockPatchedCount;
        private static bool _dropPodGroupIdPatched;

        [ThreadStatic]
        private static int _compressionScopeDepth;

        [ThreadStatic]
        private static List<PawnGenOption> _scopedCompressedPawnGenOptions;

        [ThreadStatic]
        private static Stack<List<PawnGenOption>> _compressionOptionStack;

        [ThreadStatic]
        private static Random _eliteLevelRandom;

        [ThreadStatic]
        private static DropPodGroupContext _dropPodGroupContext;

        [ThreadStatic]
        private static int _lastDropPodGroupSeed;

        [ThreadStatic]
        private static int _dropPodGroupOrdinal;

        private sealed class DropPodGroupContext
        {
            internal DropPodGroupContext Previous;
            internal int Seed;
        }

        private sealed class DeterministicEliteRandom : Random
        {
            public override int Next()
            {
                return Verse.Rand.Range(0, int.MaxValue);
            }

            public override int Next(int maxValue)
            {
                if (maxValue < 0)
                    throw new ArgumentOutOfRangeException(nameof(maxValue));
                return maxValue == 0 ? 0 : Verse.Rand.Range(0, maxValue);
            }

            public override int Next(int minValue, int maxValue)
            {
                if (minValue > maxValue)
                    throw new ArgumentOutOfRangeException(nameof(minValue));
                return minValue == maxValue
                    ? minValue
                    : Verse.Rand.Range(minValue, maxValue);
            }

            public override double NextDouble()
            {
                return Verse.Rand.Value;
            }

            public override void NextBytes(byte[] buffer)
            {
                if (buffer == null)
                    throw new ArgumentNullException(nameof(buffer));
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = (byte)Verse.Rand.Range(0, 256);
            }

            protected override double Sample()
            {
                return Verse.Rand.Value;
            }
        }

        internal static void Apply(Harmony harmony)
        {
            Type tsrType = AccessTools.TypeByName(ThreadSafeRandomTypeName);
            int patched = 0;
            if (tsrType == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid ThreadSafeRandom type not found; " +
                    "continuing with EliteLevel.myRandom resolution.");
            }
            else
            {
                MethodInfo[] tsrMethods =
                {
                    tsrType.GetMethod("Next", Type.EmptyTypes),
                    tsrType.GetMethod("Next", new[] { typeof(int) }),
                    tsrType.GetMethod("Next", new[] { typeof(int), typeof(int) }),
                    tsrType.GetMethod("NextDouble", Type.EmptyTypes)
                };

                MethodInfo[] prefixes =
                {
                    AccessTools.Method(
                        typeof(Patch_EliteRaidDeterminism), nameof(DeterministicNextPrefix)),
                    AccessTools.Method(
                        typeof(Patch_EliteRaidDeterminism), nameof(DeterministicNextMaxPrefix)),
                    AccessTools.Method(
                        typeof(Patch_EliteRaidDeterminism), nameof(DeterministicRangePrefix)),
                    AccessTools.Method(
                        typeof(Patch_EliteRaidDeterminism), nameof(DeterministicNextDoublePrefix))
                };

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
            }

            _patchedCount = patched;

            bool eliteLevelPatched = TryPatchEliteLevelRandom(harmony);
            _compressionCachePatched = TryPatchCompressionCache(harmony);
            _simulationClockPatchedCount = TryPatchSimulationClock(harmony);
            _dropPodGroupIdPatched = TryPatchDropPodGroupId(harmony);
            if (patched == 0 && !eliteLevelPatched && !_compressionCachePatched)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid deterministic Rand NOT active: " +
                    "no supported random boundary was patched.");
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Elite Raid deterministic Rand active: " +
                $"ThreadSafeRandom methods patched={patched}/4, " +
                $"EliteLevel.myRandom={(eliteLevelPatched ? "patched" : "not found")}, " +
                $"CompressedPawnGenOptions={(_compressionCachePatched ? "incident-scoped" : "not found")}, " +
                $"simulationClockMethods={_simulationClockPatchedCount}, " +
                $"dropPodGroupId={(_dropPodGroupIdPatched ? "deterministic" : "not found")}.");
        }

        /// <summary>
        /// EliteRaid's raid-record bridge uses wall-clock timestamps to pair a
        /// raid generation call with a later drop-pod callback. Wall-clock
        /// time is process-local and can differ between host and client,
        /// especially after a reconnect. Replace only the EliteRaid methods
        /// that use this bridge with a DateTime derived from the synchronized
        /// game tick. Single-player keeps DateTime.Now.
        /// </summary>
        private static int TryPatchSimulationClock(Harmony harmony)
        {
            Type helperType = AccessTools.TypeByName(CompressionHelperTypeName);
            if (helperType == null)
                return 0;

            MethodInfo clockGetter = AccessTools.PropertyGetter(
                typeof(DateTime), nameof(DateTime.Now));
            MethodInfo transpiler = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(ReplaceDateTimeNow));
            if (clockGetter == null || transpiler == null)
                return 0;

            int patched = 0;
            foreach (string methodName in new[]
            {
                "RecordRaidInfo",
                "TryFindClosestRaidRecord"
            })
            {
                MethodInfo target = helperType.GetMethods(
                        BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic)
                    .FirstOrDefault(method =>
                        method.Name == methodName &&
                        method.GetMethodBody() != null);
                if (target == null)
                    continue;

                try
                {
                    harmony.Patch(
                        target,
                        transpiler: new HarmonyMethod(transpiler)
                        {
                            priority = Priority.First
                        });
                    patched++;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Elite Raid simulation clock patch failed on " +
                        $"{target.Name}: {e.Message}");
                }
            }

            Type dropPodType = AccessTools.TypeByName(DropPodPatchTypeName);
            MethodInfo dropPodPostfix = dropPodType == null
                ? null
                : AccessTools.Method(
                    dropPodType,
                    "DropThingGroupsNear_Postfix");
            if (dropPodPostfix == null)
                return patched;

            try
            {
                harmony.Patch(
                    dropPodPostfix,
                    transpiler: new HarmonyMethod(transpiler)
                    {
                        priority = Priority.First
                    });
                patched++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid drop-pod clock patch failed: " +
                    e.Message);
            }

            return patched;
        }

        private static IEnumerable<CodeInstruction> ReplaceDateTimeNow(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo clockGetter = AccessTools.PropertyGetter(
                typeof(DateTime), nameof(DateTime.Now));
            MethodInfo replacement = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(GetSimulationClock));

            foreach (CodeInstruction instruction in instructions)
            {
                if (clockGetter != null && replacement != null &&
                    instruction.Calls(clockGetter))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static DateTime GetSimulationClock()
        {
            if (!MP.IsInMultiplayer)
                return DateTime.Now;

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < 0)
                tick = 0;

            // DateTime is only used as a relative timestamp by EliteRaid.
            // Anchor it away from DateTime.MinValue because the original code
            // subtracts seconds/minutes for its matching windows.
            DateTime epoch = new DateTime(
                2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long elapsedTicks = (long)tick * TimeSpan.TicksPerSecond / 60L;
            return epoch.AddTicks(elapsedTicks);
        }

        /// <summary>
        /// DropThingGroupsNear_Postfix creates a process-random Guid for its
        /// pending pawn group. The Guid is not network state, but it is used
        /// as the key for later pawn matching; making it a deterministic
        /// function of the simulation call keeps the local pending-group
        /// state identical even when multiple pods share a tick.
        /// </summary>
        private static bool TryPatchDropPodGroupId(Harmony harmony)
        {
            Type dropPodType = AccessTools.TypeByName(DropPodPatchTypeName);
            MethodInfo target = dropPodType == null
                ? null
                : AccessTools.Method(
                    dropPodType,
                    "DropThingGroupsNear_Postfix");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(DropPodGroupPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(DropPodGroupFinalizer));
            MethodInfo transpiler = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(ReplaceGuidNewGuid));

            if (target == null || prefix == null || finalizer == null ||
                transpiler == null)
            {
                return false;
            }

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
                    },
                    transpiler: new HarmonyMethod(transpiler)
                    {
                        priority = Priority.First
                    });
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid drop-pod group id patch failed: " +
                    e.Message);
                return false;
            }
        }

        private static IEnumerable<CodeInstruction> ReplaceGuidNewGuid(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo newGuid = AccessTools.Method(
                typeof(Guid), nameof(Guid.NewGuid), Type.EmptyTypes);
            MethodInfo replacement = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(CreateDeterministicDropPodGroupId));

            foreach (CodeInstruction instruction in instructions)
            {
                if (newGuid != null && replacement != null &&
                    instruction.Calls(newGuid))
                {
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        private static void DropPodGroupPrefix(
            object[] __args,
            ref DropPodGroupContext __state)
        {
            DropPodGroupContext previous = _dropPodGroupContext;
            int seed;
            try
            {
                seed = BuildDropPodGroupSeed(__args);
            }
            catch
            {
                // This is an optional identity aid. Never let an unusual
                // collection implementation prevent the original drop pod
                // callback from running.
                seed = Gen.HashCombineInt(
                    Find.TickManager?.TicksGame ?? 0,
                    Find.CurrentMap?.Index ?? -1);
            }
            DropPodGroupContext current = new DropPodGroupContext
            {
                Previous = previous,
                Seed = seed
            };
            _dropPodGroupContext = current;
            __state = current;
        }

        private static Exception DropPodGroupFinalizer(
            Exception __exception,
            DropPodGroupContext __state)
        {
            _dropPodGroupContext = __state?.Previous;
            return __exception;
        }

        private static int BuildDropPodGroupSeed(object[] args)
        {
            if (!MP.IsInMultiplayer)
                return 0;

            IntVec3 center = IntVec3.Invalid;
            Map map = null;
            Faction faction = null;
            List<int> pawnIds = new List<int>();

            if (args != null)
            {
                foreach (object arg in args)
                {
                    if (arg is IntVec3 value)
                        center = value;
                    else if (arg is Map mapValue)
                        map = mapValue;
                    else if (arg is Faction factionValue)
                        faction = factionValue;
                    else if (arg is IEnumerable groups)
                        CollectPawnIds(groups, pawnIds);
                }
            }

            pawnIds.Sort();
            int seed = Gen.HashCombineInt(
                map?.Index ?? -1,
                Find.TickManager?.TicksGame ?? 0);
            seed = Gen.HashCombineInt(seed, center.x);
            seed = Gen.HashCombineInt(seed, center.y);
            seed = Gen.HashCombineInt(seed, center.z);
            seed = Gen.HashCombineInt(seed, faction?.loadID ?? -1);
            seed = Gen.HashCombineInt(seed, pawnIds.Count);
            foreach (int pawnId in pawnIds)
                seed = Gen.HashCombineInt(seed, pawnId);

            if (seed == _lastDropPodGroupSeed)
                _dropPodGroupOrdinal++;
            else
            {
                _lastDropPodGroupSeed = seed;
                _dropPodGroupOrdinal = 0;
            }

            return Gen.HashCombineInt(seed, _dropPodGroupOrdinal);
        }

        private static void CollectPawnIds(
            IEnumerable values,
            List<int> pawnIds)
        {
            if (values == null)
                return;

            foreach (object value in values)
            {
                if (value is Pawn pawn)
                {
                    pawnIds.Add(pawn.thingIDNumber);
                }
                else if (value is IEnumerable nested &&
                    !(value is string))
                {
                    CollectPawnIds(nested, pawnIds);
                }
            }
        }

        private static Guid CreateDeterministicDropPodGroupId()
        {
            if (!MP.IsInMultiplayer || _dropPodGroupContext == null)
                return Guid.NewGuid();

            int seed = _dropPodGroupContext.Seed;
            int part2 = Gen.HashCombineInt(seed, 0x04E31A7B);
            int part3 = Gen.HashCombineInt(seed, 0x07C91D23);
            int part4 = Gen.HashCombineInt(seed, 0x019F0B5D);
            return new Guid(
                seed,
                (short)(part2 >> 16),
                (short)part3,
                (byte)part4,
                (byte)(part4 >> 8),
                (byte)(part4 >> 16),
                (byte)(part4 >> 24),
                (byte)part2,
                (byte)(part2 >> 8),
                (byte)(part3 >> 8),
                (byte)(part3 >> 16));
        }

        /// <summary>
        /// EliteRaid stores the compressed pawn candidates in a process-wide
        /// static property. In a multifaction/async-time session, a nested or
        /// adjacent raid on another map can overwrite that list between the
        /// candidate-building and SpawnThreats phases. Keep the same call-chain
        /// list in a thread-local incident scope instead. The scope is opened by
        /// Patch_IncidentRaidFactionContext around every raid worker, so normal
        /// single-player and non-raid EliteRaid code retain their original path.
        /// </summary>
        private static bool TryPatchCompressionCache(Harmony harmony)
        {
            Type helperType = AccessTools.TypeByName(CompressionHelperTypeName);
            PropertyInfo property = helperType == null
                ? null
                : AccessTools.Property(helperType, "CompressedPawnGenOptions");
            MethodInfo getter = property?.GetGetMethod(true);
            MethodInfo setter = property?.GetSetMethod(true);
            MethodInfo getterPrefix = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(CompressedOptionsGetterPrefix));
            MethodInfo setterPrefix = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(CompressedOptionsSetterPrefix));

            if (getter == null || setter == null ||
                getterPrefix == null || setterPrefix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid compressed pawn option " +
                    "cache property not found; incident cache isolation is inactive.");
                return false;
            }

            bool getterPatched = false;
            try
            {
                harmony.Patch(
                    getter,
                    prefix: new HarmonyMethod(getterPrefix)
                    {
                        priority = Priority.First
                    });
                getterPatched = true;

                harmony.Patch(
                    setter,
                    prefix: new HarmonyMethod(setterPrefix)
                    {
                        priority = Priority.First
                    });
                return true;
            }
            catch (Exception e)
            {
                if (getterPatched)
                    harmony.Unpatch(getter, HarmonyPatchType.Prefix, harmony.Id);

                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid compressed pawn option " +
                    "cache patch failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Opens an incident-local view of EliteRaid's compressed candidate list.
        /// Nested raid workers save/restore the outer scope and are balanced
        /// by the matching finalizers in Patch_IncidentRaidFactionContext.
        /// </summary>
        internal static bool BeginRaidCompressionScope()
        {
            if (!MP.IsInMultiplayer || !_compressionCachePatched)
                return false;

            if (_compressionScopeDepth++ == 0)
            {
                _compressionOptionStack = new Stack<List<PawnGenOption>>();
            }
            else if (_compressionOptionStack == null)
            {
                _compressionOptionStack = new Stack<List<PawnGenOption>>();
            }
            else
            {
                _compressionOptionStack.Push(_scopedCompressedPawnGenOptions);
            }

            _scopedCompressedPawnGenOptions = null;
            return true;
        }

        internal static void EndRaidCompressionScope()
        {
            if (!_compressionCachePatched || _compressionScopeDepth <= 0)
                return;

            _compressionScopeDepth--;
            if (_compressionScopeDepth == 0)
            {
                _scopedCompressedPawnGenOptions = null;
                _compressionOptionStack = null;
            }
            else if (_compressionOptionStack != null &&
                _compressionOptionStack.Count > 0)
            {
                _scopedCompressedPawnGenOptions =
                    _compressionOptionStack.Pop();
            }
            else
            {
                _scopedCompressedPawnGenOptions = null;
            }
        }

        private static bool CompressedOptionsGetterPrefix(
            ref List<PawnGenOption> __result)
        {
            if (!MP.IsInMultiplayer || _compressionScopeDepth <= 0)
                return true;

            __result = _scopedCompressedPawnGenOptions;
            return false;
        }

        private static bool CompressedOptionsSetterPrefix(object[] __args)
        {
            if (!MP.IsInMultiplayer || _compressionScopeDepth <= 0)
                return true;

            List<PawnGenOption> value = __args != null && __args.Length > 0
                ? __args[0] as List<PawnGenOption>
                : null;
            _scopedCompressedPawnGenOptions = value == null
                ? null
                : new List<PawnGenOption>(value);
            return false;
        }

        private static bool TryPatchEliteLevelRandom(Harmony harmony)
        {
            Type eliteLevelType = AccessTools.TypeByName(EliteLevelTypeName);
            MethodInfo getter = eliteLevelType == null
                ? null
                : AccessTools.Method(eliteLevelType, "get_myRandom");
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_EliteRaidDeterminism),
                nameof(DeterministicEliteLevelRandomPrefix));

            if (getter == null || prefix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid EliteLevel.myRandom getter not found; " +
                    "trait generation may still use wall-clock random.");
                return false;
            }

            try
            {
                harmony.Patch(
                    getter,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });
                return true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Elite Raid EliteLevel.myRandom patch failed: " +
                    e.Message);
                return false;
            }
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
        // Next(int a, int maxValue), so a prefix parameter named minValue would
        // not bind reliably. Read the two arguments from __args instead, which
        // is independent of parameter names.
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

        private static bool DeterministicEliteLevelRandomPrefix(ref Random __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            if (_eliteLevelRandom == null)
                _eliteLevelRandom = new DeterministicEliteRandom();
            __result = _eliteLevelRandom;
            return false;
        }
    }
}
