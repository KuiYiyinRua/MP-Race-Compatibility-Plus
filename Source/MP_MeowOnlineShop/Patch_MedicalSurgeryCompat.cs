using System;
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
    /// Keeps third-party surgery hooks on a simulation-safe boundary.
    ///
    /// Multiplayer already owns HealthCardUtility.CreateSurgeryBill. The
    /// missing boundary found in Desync-622/623 was GodHands' surgery-fail
    /// prefix: it compared against MapComponent_GodAssistant.GodHandWorker,
    /// whose getter lazily generated an unsaved pawn during every surgery
    /// check. In multiplayer that can consume Rand/unique IDs on one side.
    /// Replace only that getter call with a read of the already-created
    /// worker. GodHands' real medical task creates the worker before invoking
    /// the recipe, so its behavior is preserved; ordinary surgery checks no
    /// longer create hidden state.
    /// </summary>
    internal static class Patch_MedicalSurgeryCompat
    {
        private const string GodHandPrefixTypeName =
            "GodHandMod.Patch_CheckSurgeryFail_GodHand";
        private const string GodHandComponentTypeName =
            "GodHandMod.MapComponent_GodAssistant";
        private const string KemhouseMedicalBillTypeName =
            "Kz.Bill_KemhouseWoolMedical";

        private static bool _applied;
        private static FieldInfo _godHandWorkerField;
        private static MethodInfo _godHandWorkerGetter;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;

            _applied = true;

            try
            {
                Type prefixType = AccessTools.TypeByName(GodHandPrefixTypeName);
                Type componentType = AccessTools.TypeByName(GodHandComponentTypeName);
                MethodInfo prefix = prefixType == null
                    ? null
                    : AccessTools.Method(prefixType, "Prefix");
                PropertyInfo workerProperty = componentType == null
                    ? null
                    : AccessTools.Property(componentType, "GodHandWorker");

                _godHandWorkerField = componentType == null
                    ? null
                    : AccessTools.Field(componentType, "_godHandWorker");
                _godHandWorkerGetter = workerProperty?.GetGetMethod(true);

                if (prefix == null || _godHandWorkerField == null ||
                    _godHandWorkerGetter == null)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] GodHands surgery lazy-worker guard skipped: " +
                        $"prefix={(prefix != null)}, field={(_godHandWorkerField != null)}, " +
                        $"getter={(_godHandWorkerGetter != null)}.");
                }
                else
                {
                    harmony.Patch(
                        prefix,
                        transpiler: new HarmonyMethod(
                            typeof(Patch_MedicalSurgeryCompat),
                            nameof(ReplaceGodHandWorkerGetter)));

                    Log.Message(
                        "[MP-MeowOnlineShop] GodHands surgery lazy-worker guard active: " +
                        "CheckSurgeryFail now reads the existing worker only in multiplayer.");
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] GodHands surgery lazy-worker guard failed: " +
                    e.Message);
            }

            ApplyKemhouseMedicalBillSync();
        }

        private static void ApplyKemhouseMedicalBillSync()
        {
            Type billType = AccessTools.TypeByName(KemhouseMedicalBillTypeName);
            if (billType == null)
                return;

            int registered = 0;

            try
            {
                PropertyInfo partProperty = AccessTools.Property(
                    typeof(Bill_Medical), nameof(Bill_Medical.Part));
                MethodInfo partSetter = partProperty?.GetSetMethod(true);
                if (partSetter == null)
                    throw new MissingMethodException(
                        typeof(Bill_Medical).FullName, "set_Part");

                MP.RegisterSyncMethod(partSetter, (SyncType[])null);
                registered++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] KemomimiHouse medical body-part " +
                    "SyncMethod registration failed: " + e.Message);
            }

            try
            {
                MethodInfo resetCurWool = AccessTools.Method(
                    billType, "ResetCurWool", new[] { typeof(ThingDef) });
                if (resetCurWool == null)
                    throw new MissingMethodException(
                        billType.FullName, "ResetCurWool(ThingDef)");

                MP.RegisterSyncMethod(resetCurWool, (SyncType[])null);
                registered++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] KemomimiHouse medical wool " +
                    "SyncMethod registration failed: " + e.Message);
            }

            if (registered > 0)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] KemomimiHouse medical bill sync active: " +
                    "Bill_Medical.Part and custom wool selection are synchronized.");
            }
        }

        private static IEnumerable<CodeInstruction> ReplaceGodHandWorkerGetter(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(
                typeof(Patch_MedicalSurgeryCompat),
                nameof(GetGodHandWorkerWithoutGeneration));
            List<CodeInstruction> codes = instructions.ToList();
            int replaced = 0;

            if (replacement != null && _godHandWorkerGetter != null)
            {
                foreach (CodeInstruction code in codes)
                {
                    if (code.Calls(_godHandWorkerGetter))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = replacement;
                        replaced++;
                    }
                }
            }

            if (replaced == 0)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] GodHands surgery lazy-worker guard found no " +
                    "GodHandWorker getter call in CheckSurgeryFail prefix; original path kept.");
            }
            else
            {
                Log.Message(
                    "[MP-MeowOnlineShop] GodHands surgery lazy-worker guard replaced " +
                    replaced + " getter call(s).");
            }

            return codes;
        }

        private static Pawn GetGodHandWorkerWithoutGeneration()
        {
            try
            {
                // Preserve single-player behavior exactly. The compatibility
                // patch is only a multiplayer guard.
                if (!MP.IsInMultiplayer && _godHandWorkerGetter != null)
                    return _godHandWorkerGetter.Invoke(null, null) as Pawn;

                return _godHandWorkerField?.GetValue(null) as Pawn;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] GodHands worker read failed; returning null: " +
                    e.Message);
                return null;
            }
        }
    }
}
