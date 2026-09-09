using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for Ratkin Weapons+
    /// (bbb.ratkinweapon.morefailure, assembly RatkinWeapons).
    /// </summary>
    internal static class Patch_RatkinWeaponsMp
    {
        private const string BayonetCompTypeName = "RatkinWeapons.CompBayonet";
        private const string AntiTankTrapTypeName = "RatkinWeapons.Building_ATtrap";
        private const string BayonetJobGiverTypeName = "RatkinWeapons.JobGiver_TryUseBayonet";
        private const int BayonetRandomSalt = 0x4241594E; // "BAYN"

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied)
                return;

            _applied = true;

            Type bayonetType = AccessTools.TypeByName(BayonetCompTypeName);
            Type trapType = AccessTools.TypeByName(AntiTankTrapTypeName);
            Type bayonetJobGiverType = AccessTools.TypeByName(BayonetJobGiverTypeName);
            if (bayonetType == null && trapType == null)
            {
                Log.Message("[MP-MeowOnlineShop] Ratkin Weapons+ MP: target assembly not active; patch skipped.");
                return;
            }

            int resolved = 0;
            const int expected = 2;

            TryPatchDeterministicBayonetRandom(harmony, bayonetJobGiverType, bayonetType);

            try
            {
                MethodInfo bayonetAct = bayonetType?.GetMethod(
                    "BayonetAct",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(LocalTargetInfo) },
                    null);

                if (bayonetAct != null)
                {
                    MP.RegisterSyncMethod(bayonetAct, null).SetContext(SyncContext.MapSelected);
                    resolved++;
                    Log.Message("[MP-MeowOnlineShop] Ratkin Weapons+ MP: registered CompBayonet.BayonetAct(LocalTargetInfo).");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: CompBayonet.BayonetAct(LocalTargetInfo) not resolved.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: bayonet sync registration failed: " + e);
            }

            try
            {
                MethodInfo toggle = FindGeneratedToggleMethod(trapType, "GetGizmos");
                if (toggle != null)
                {
                    MP.RegisterSyncMethod(toggle, null).SetContext(SyncContext.MapSelected);
                    resolved++;
                    Log.Message("[MP-MeowOnlineShop] Ratkin Weapons+ MP: registered anti-tank trap auto-rearm toggle " + toggle.Name + ".");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: anti-tank trap auto-rearm toggle not resolved.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin Weapons+ MP: trap toggle sync registration failed: " + e);
            }

            Log.Message($"[MP-MeowOnlineShop] Ratkin Weapons+ MP targets resolved={resolved}/{expected}.");
        }

        private static void TryPatchDeterministicBayonetRandom(
            Harmony harmony,
            Type jobGiverType,
            Type compType)
        {
            if (harmony == null)
                return;

            MethodInfo transpiler = AccessTools.Method(
                typeof(Patch_RatkinWeaponsMp),
                nameof(SystemRandomTranspiler));
            if (transpiler == null)
                return;

            int patched = 0;
            MethodInfo bayonetJob = jobGiverType?.GetMethod(
                "BayonetJob",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (bayonetJob != null)
            {
                harmony.Patch(
                    bayonetJob,
                    transpiler: new HarmonyMethod(transpiler));
                patched++;
            }

            MethodInfo bayonetAct = compType?.GetMethod(
                "BayonetAct",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(LocalTargetInfo) },
                null);
            if (bayonetAct != null)
            {
                harmony.Patch(
                    bayonetAct,
                    transpiler: new HarmonyMethod(transpiler));
                patched++;
            }

            if (patched > 0)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Ratkin Weapons+ MP: bayonet System.Random " +
                    "is deterministic: patched=" + patched + ".");
            }
        }

        private static IEnumerable<CodeInstruction> SystemRandomTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConstructorInfo randomCtor = AccessTools.Constructor(
                typeof(System.Random),
                Type.EmptyTypes);
            MethodInfo factory = AccessTools.Method(
                typeof(Patch_RatkinWeaponsMp),
                nameof(CreateDeterministicBayonetRandom));

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj &&
                    instruction.operand is ConstructorInfo ctor &&
                    ctor == randomCtor)
                {
                    // Mutate the existing instruction so Harmony labels and
                    // exception blocks attached to the newobj are preserved.
                    // Replacing it with a fresh CodeInstruction caused the
                    // installed RatkinWeapons build to fail IL validation with
                    // an invalid branch label.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = factory;
                }

                yield return instruction;
            }
        }

        private static System.Random CreateDeterministicBayonetRandom()
        {
            int seed = Gen.HashCombineInt(
                BayonetRandomSalt,
                Find.TickManager?.TicksGame ?? 0);
            Thing current = GetCurrentThing();
            if (current != null)
                seed = Gen.HashCombineInt(seed, current.thingIDNumber);
            return new System.Random(seed);
        }

        private static Thing GetCurrentThing()
        {
            try
            {
                Type contextType =
                    AccessTools.TypeByName("Multiplayer.Client.Patches.ThingContext");
                PropertyInfo current =
                    AccessTools.Property(contextType, "Current");
                return current?.GetValue(null, null) as Thing;
            }
            catch
            {
                return null;
            }
        }

        private static MethodInfo FindGeneratedToggleMethod(Type type, string parentMethod)
        {
            if (type == null)
                return null;

            string prefix = "<" + parentMethod + ">b__";
            return type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal)
                            && m.Name.EndsWith("_1", StringComparison.Ordinal)
                            && m.ReturnType == typeof(void)
                            && m.GetParameters().Length == 0)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }
}
