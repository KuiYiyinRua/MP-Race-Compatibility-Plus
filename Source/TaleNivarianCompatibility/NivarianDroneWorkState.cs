using System;
using HarmonyLib;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianDroneWorkState
    {
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_Engineer")
                ?? throw new TypeLoadException("NivarianDroneComp_Engineer");
            if (AccessTools.Field(type, "_repairTickCounter")?.FieldType != typeof(int))
                throw new MissingFieldException(type.FullName, "_repairTickCounter");
            harmony.Patch(AccessTools.DeclaredMethod(type, "PostExposeData"),
                postfix: new HarmonyMethod(typeof(NivarianDroneWorkState), nameof(Expose)));
            Log.Message("[TaleNivarianCompat] engineering drone repair cadence persistence installed.");
        }
        static void Expose(ref int ____repairTickCounter)
        {
            Scribe_Values.Look(ref ____repairTickCounter, "meowDroneRepairTickCounter");
        }
    }
}
