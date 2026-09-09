using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI.Group;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-600: the official Anomaly Chimera assault consumes map Rand from
    /// LordJob_ChimeraAssault while the lord is switching between stalk and
    /// attack modes. Under async map ticking, the same lord can reach this
    /// boundary with a different surrounding Rand stream on the two peers.
    ///
    /// Keep the fix at the official Chimera-specific mutation surface. The
    /// incident worker is already covered by the shared incident stabilizer;
    /// the mid-raid LordJob methods are the remaining random consumers.
    /// </summary>
    internal static class Patch_AnomalyChimeraAssaultDeterminism
    {
        private const string ChimeraLordJobTypeName =
            "RimWorld.LordJob_ChimeraAssault";
        private const int ChimeraSeedSalt = 0x4348494D; // "CHIM"
        private const int ChimeraWorldSeedOffset = 0x43484957; // "CHIW"
        private const int LordJobTickSalt = 0x4C544943; // "LTIC"
        private const int PawnDownedSalt = 0x444F574E; // "DOWN"
        private const int PawnLostSalt = 0x4C4F5354; // "LOST"

        private static readonly FieldInfo LordField =
            AccessTools.Field(typeof(LordJob), "lord");

        [ThreadStatic]
        private static Stack<Map> _mapRandPopStack;

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;

            _applied = true;
            try
            {
                Type chimeraLordJobType =
                    AccessTools.TypeByName(ChimeraLordJobTypeName);
                if (chimeraLordJobType == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Chimera assault Rand isolation " +
                        "skipped: LordJob_ChimeraAssault was not found.");
                    return;
                }

                bool lordJobTickPatched = PatchMethod(
                    harmony,
                    chimeraLordJobType,
                    "LordJobTick",
                    0,
                    nameof(LordJobTickPrefix)) != 0;
                bool pawnDownedPatched = PatchMethod(
                    harmony,
                    chimeraLordJobType,
                    "Notify_PawnDowned",
                    1,
                    nameof(PawnDownedPrefix)) != 0;
                bool pawnLostPatched = PatchMethod(
                    harmony,
                    chimeraLordJobType,
                    "Notify_PawnLost",
                    2,
                    nameof(PawnLostPrefix)) != 0;

                if (!lordJobTickPatched && !pawnDownedPatched &&
                    !pawnLostPatched)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Chimera assault Rand isolation " +
                        "skipped: no official LordJob methods resolved.");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Chimera assault Rand isolation " +
                    $"active: lordTick={lordJobTickPatched}, " +
                    $"pawnDowned={pawnDownedPatched}, " +
                    $"pawnLost={pawnLostPatched}.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Chimera assault Rand isolation " +
                    $"apply failed: {e.Message}");
            }
        }

        private static int PatchMethod(
            Harmony harmony,
            Type chimeraLordJobType,
            string methodName,
            int parameterCount,
            string prefixName)
        {
            MethodInfo target = FindDeclaredMethod(
                chimeraLordJobType,
                methodName,
                parameterCount);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_AnomalyChimeraAssaultDeterminism),
                prefixName);
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_AnomalyChimeraAssaultDeterminism),
                nameof(RandScopeFinalizer));
            if (target == null || prefix == null || finalizer == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Chimera assault Rand isolation " +
                    $"target skipped: {methodName}.");
                return 0;
            }

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
            return 1;
        }

        private static MethodInfo FindDeclaredMethod(
            Type type,
            string name,
            int parameterCount)
        {
            foreach (MethodInfo method in type.GetMethods(AccessTools.all))
            {
                if (method.DeclaringType == type &&
                    method.Name == name &&
                    method.GetParameters().Length == parameterCount)
                {
                    return method;
                }
            }

            return null;
        }

        private static void LordJobTickPrefix(
            object __instance,
            ref int __state)
        {
            BeginRandScope(__instance, LordJobTickSalt, ref __state);
        }

        private static void PawnDownedPrefix(
            object __instance,
            ref int __state)
        {
            BeginRandScope(__instance, PawnDownedSalt, ref __state);
        }

        private static void PawnLostPrefix(
            object __instance,
            ref int __state)
        {
            BeginRandScope(__instance, PawnLostSalt, ref __state);
        }

        private static void BeginRandScope(
            object instance,
            int methodSalt,
            ref int state)
        {
            state = 0;
            if (!MP.IsInMultiplayer)
                return;

            Lord lord = GetLord(instance);
            Map map = lord?.Map;
            if (lord == null || map == null)
                return;

            int seed = Gen.HashCombineInt(ChimeraSeedSalt, map.uniqueID);
            seed = Gen.HashCombineInt(seed, lord.loadID);
            seed = Gen.HashCombineInt(
                seed,
                Find.TickManager?.TicksGame ?? 0);
            seed = Gen.HashCombineInt(seed, methodSalt);

            if (!DeterministicRandScope.Begin(
                    map,
                    seed,
                    ChimeraWorldSeedOffset,
                    ref state,
                    out Map mapForPop,
                    ignoreGate: true))
            {
                state = 0;
                return;
            }

            if (_mapRandPopStack == null)
                _mapRandPopStack = new Stack<Map>();
            _mapRandPopStack.Push(mapForPop);
        }

        private static Exception RandScopeFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state == 0)
                return __exception;

            Map mapForPop = null;
            if (_mapRandPopStack != null && _mapRandPopStack.Count > 0)
            {
                mapForPop = _mapRandPopStack.Pop();
                if (_mapRandPopStack.Count == 0)
                    _mapRandPopStack = null;
            }

            DeterministicRandScope.End(__state, mapForPop);
            return __exception;
        }

        private static Lord GetLord(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                Lord lord = LordField?.GetValue(instance) as Lord;
                return lord ?? (instance as LordJob)?.lord;
            }
            catch
            {
                return (instance as LordJob)?.lord;
            }
        }
    }
}
