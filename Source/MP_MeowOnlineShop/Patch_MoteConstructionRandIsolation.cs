using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Verse.Mote field initializers call Rand.Range before any constructor
    /// body runs. The Odyssey landing sequence, Ancot effects, and a large mod
    /// stack can spawn or destroy visual motes on only one peer, or at a
    /// different point in the local update loop, which advances the map Rand
    /// stream on one side without the other (Desync-249/251-256 stale
    /// Mote_ChargingCablesPulse / Mote_MechCharging members are the visible
    /// symptom). Motes are visual-only, so creation is wrapped in a
    /// deterministic scope that restores the synchronized map stream.
    /// </summary>
    internal static class Patch_MoteConstructionRandIsolation
    {
        private const int MoteSeedSalt = 0x4D4F5445; // "MOTE"
        private const int WorldSeedOffset = 0x4D4F5446; // "MOTF"

        private static readonly object CachedTrue = new object();
        private static readonly ConditionalWeakTable<ThingDef, object>
            MoteDefCache = new ConditionalWeakTable<ThingDef, object>();
        private static bool _applied;

        [ThreadStatic]
        private static Map _mapForRandPop;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_MoteConstructionRandIsolation),
                    nameof(MakeThingPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_MoteConstructionRandIsolation),
                    nameof(MakeThingFinalizer));
                if (prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Mote construction Rand isolation " +
                        "patch methods missing; skipped.");
                    return;
                }

                int patched = 0;
                MethodInfo[] makeThingMethods = typeof(ThingMaker)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < makeThingMethods.Length; i++)
                {
                    MethodInfo method = makeThingMethods[i];
                    if (method.Name != "MakeThing" ||
                        method.ReturnType != typeof(Thing) ||
                        method.GetParameters().Length == 0)
                    {
                        continue;
                    }

                    harmony.Patch(
                        method,
                        prefix: new HarmonyMethod(prefix)
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(finalizer)
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Mote construction Rand isolation " +
                        "found no ThingMaker.MakeThing overloads; skipped.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Mote construction Rand isolation " +
                    "active: MakeThing overloads=" + patched + ".");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Mote construction Rand isolation " +
                    "install failed: " + e.Message);
            }
        }

        private static void MakeThingPrefix(
            object[] __args,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __args == null || __args.Length == 0)
                return;

            if (!(__args[0] is ThingDef def) || !IsMoteDef(def))
                return;

            Map map = Find.CurrentMap;
            int seed = Gen.HashCombineInt(MoteSeedSalt, map?.uniqueID ?? 0);
            seed = Gen.HashCombineInt(seed, def.shortHash);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    WorldSeedOffset,
                    ref __state,
                    out var mapForPop,
                    ignoreGate: true))
            {
                _mapForRandPop = mapForPop;
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception MakeThingFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }

        private static bool IsMoteDef(ThingDef def)
        {
            if (def == null || def.thingClass == null)
                return false;

            if (MoteDefCache.TryGetValue(def, out _))
                return true;

            if (typeof(Mote).IsAssignableFrom(def.thingClass))
            {
                MoteDefCache.Add(def, CachedTrue);
                return true;
            }

            return false;
        }
    }
}
