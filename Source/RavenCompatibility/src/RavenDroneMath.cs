using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;

namespace MP_MeowOnlineShop
{
    internal static class RavenDroneMath
    {
        private static MethodInfo nativeGetter;
        internal static void Apply(Harmony harmony)
        {
            nativeGetter = AccessTools.PropertyGetter(AccessTools.TypeByName("RavenRace.Core.Runtime.RavenBurstAotRuntime"), "IsNativeCodeActive")
                ?? throw new MissingMethodException("Raven native-code status");
            var names = new[] {
                "RavenRace.Features.Drone.Burst.RavenDroneMovementBatch",
                "RavenRace.Features.Drone.Combat.MapComponent_RavenCombatDroneManager",
                "RavenRace.Features.Drone.Medical.MapComponent_RavenMedicalDroneManager"
            };
            for (int i = 0; i < names.Length; i++)
            {
                var target = AccessTools.DeclaredMethod(AccessTools.TypeByName(names[i]), i == 0 ? "Execute" : "PrepareBurstMovement")
                    ?? throw new MissingMethodException(names[i]);
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(RavenDroneMath), nameof(UseSharedMathPath)));
            }
        }
        private static bool NativeDroneMovementAllowed()
            => !MP.IsInMultiplayer && (bool)nativeGetter.Invoke(null, null);

        // Native drone math uses Unity.Mathematics sqrt/atan2; the managed fallback uses
        // different floating-point routines. Select the same existing simulation path on
        // every peer, including the request prepass which can assign a return station.
        // Rendering and fixed-point conveyor jobs retain their original acceleration.
        private static IEnumerable<CodeInstruction> UseSharedMathPath(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            var replacement = AccessTools.DeclaredMethod(typeof(RavenDroneMath), nameof(NativeDroneMovementAllowed));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(nativeGetter)) { instruction.operand = replacement; replaced++; }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Raven drone native branch mismatch: " + replaced);
        }
    }
}
