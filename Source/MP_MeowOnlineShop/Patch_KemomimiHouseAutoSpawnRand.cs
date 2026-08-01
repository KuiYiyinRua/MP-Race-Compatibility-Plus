using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Kemomimi House Kz runs its automatic spawn scheduler from a global
    /// GameComponent. In async-time games, unscoped Verse.Rand calls made there
    /// can be charged to whichever map context happens to be active locally.
    /// Its ExposeData also calls state-mutating maintenance methods while saving.
    /// Keep the live scheduler deterministic and make serialization read-only.
    /// </summary>
    internal static class Patch_KemomimiHouseAutoSpawnRand
    {
        private const string PackageId = "Moo.kemomimihouse.Kz";
        private const string ComponentTypeName = "Kz.GameComponent_Kemhouse";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type type = AccessTools.TypeByName(ComponentTypeName);
            if (type == null)
                return;

            bool complete = true;
            complete &= PatchRandTarget(
                harmony, type, "AutoSpawnNewPawn", nameof(AutoSpawnPrefix));
            complete &= PatchRandTarget(
                harmony, type, "ReturnWorldPawn", nameof(ReturnPawnPrefix));
            complete &= PatchRandTarget(
                harmony, type, "UpdateSpawnTime", nameof(UpdateSpawnTimePrefix));
            complete &= PatchRandTarget(
                harmony, type, "UpdateReturnTime", nameof(UpdateReturnTimePrefix));

            var verify = AccessTools.Method(type, "TryVerifyCurSpawnStatus", Type.EmptyTypes);
            if (verify == null || verify.ReturnType != typeof(void))
            {
                complete = false;
            }
            else
            {
                harmony.Patch(
                    verify,
                    prefix: new HarmonyMethod(AccessTools.Method(
                        typeof(Patch_KemomimiHouseAutoSpawnRand),
                        nameof(SerializationMaintenancePrefix)))
                    {
                        priority = Priority.First
                    });
            }

            if (complete)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][KemomimiHouse] deterministic auto-spawn " +
                    "Rand scopes and read-only multiplayer serialization are active.");
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][KemomimiHouse] one or more scheduler " +
                    "signatures drifted; only the compatible deterministic guards " +
                    "were installed.");
            }
        }

        private static bool PatchRandTarget(
            Harmony harmony,
            Type type,
            string methodName,
            string prefixName)
        {
            var target = AccessTools.Method(type, methodName, Type.EmptyTypes);
            var prefix = AccessTools.Method(
                typeof(Patch_KemomimiHouseAutoSpawnRand),
                prefixName);
            var finalizer = AccessTools.Method(
                typeof(Patch_KemomimiHouseAutoSpawnRand),
                nameof(Finalizer));
            if (target == null || prefix == null || finalizer == null ||
                target.ReturnType != typeof(void))
            {
                return false;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                transpiler: methodName == "AutoSpawnNewPawn"
                    ? new HarmonyMethod(AccessTools.Method(
                        typeof(Patch_KemomimiHouseAutoSpawnRand),
                        nameof(AutoSpawnTranspiler)))
                    : null,
                finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });
            return true;
        }

        private static IEnumerable<CodeInstruction> AutoSpawnTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo objectHash = AccessTools.Method(
                typeof(object),
                nameof(GetHashCode),
                Type.EmptyTypes);
            MethodInfo stableHash = AccessTools.Method(
                typeof(Patch_KemomimiHouseAutoSpawnRand),
                nameof(StableDefHash));
            int replaced = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(objectHash))
                {
                    instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                    instruction.operand = stableHash;
                    replaced++;
                }
                yield return instruction;
            }

            if (replaced != 1)
            {
                Log.Warning(
                    $"[MP-MeowOnlineShop][KemomimiHouse] expected one process-local " +
                    $"GetHashCode seed in AutoSpawnNewPawn, replaced={replaced}.");
            }
        }

        private static int StableDefHash(object value)
        {
            var def = value as Def;
            return def == null
                ? 0
                : Gen.HashCombineInt(0x4B5A4446, def.shortHash);
        }

        private static void AutoSpawnPrefix(ref bool __state)
        {
            Begin(0x4B5A4153, ref __state);
        }

        private static void ReturnPawnPrefix(ref bool __state)
        {
            Begin(0x4B5A5250, ref __state);
        }

        private static void UpdateSpawnTimePrefix(ref bool __state)
        {
            Begin(0x4B5A5354, ref __state);
        }

        private static bool UpdateReturnTimePrefix(ref bool __state)
        {
            __state = false;
            if (MP.IsInMultiplayer && Scribe.mode != LoadSaveMode.Inactive)
                return false;

            Begin(0x4B5A5254, ref __state);
            return true;
        }

        private static bool SerializationMaintenancePrefix()
        {
            return !MP.IsInMultiplayer || Scribe.mode == LoadSaveMode.Inactive;
        }

        private static void Begin(int salt, ref bool state)
        {
            state = false;
            if (!MP.IsInMultiplayer)
                return;

            int tick = Find.TickManager?.TicksGame ?? 0;
            Rand.PushState(Gen.HashCombineInt(salt, tick));
            state = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
