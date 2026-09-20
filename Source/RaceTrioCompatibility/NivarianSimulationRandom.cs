using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class NivarianSimulationRandom
    {
        internal static void Apply(Harmony harmony)
        {
            Patch(harmony,"Nivarian.NivarianDrones.NivarianStraftingAttackerDrone","CalcEntryPoint");
            Patch(harmony,"Nivarian.NivarianDrones.NivarianStraftingAttackerDrone","CalcRepositionPoint");
            Patch(harmony,"Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_MovingBase","OrbitTarget");
            Patch(harmony,"Nivarian_Race.Code.NivarianThing.ProgrammableMoverThing","CreateParabolicArc");
            Patch(harmony,"Nivarian_Race.Code.Comps.ThingComps.CompAttachTurret","CompTick");
            if(ModsConfig.RoyaltyActive)Patch(harmony,"Nivarian.ReflectiveShield","LaunchReturnShot");
        }
        static void Patch(Harmony harmony,string name,string method)
        {
            var type=AccessTools.TypeByName(name)??throw new TypeLoadException(name);
            var target=AccessTools.DeclaredMethod(type,method)??throw new MissingMethodException(name,method);
            harmony.Patch(target,transpiler:new HarmonyMethod(typeof(NivarianSimulationRandom),nameof(Replace)));
        }
        static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            var range=AccessTools.Method(typeof(UnityEngine.Random),"Range",new[]{typeof(float),typeof(float)});
            var value=AccessTools.PropertyGetter(typeof(UnityEngine.Random),"value");
            var intRange=AccessTools.Method(typeof(UnityEngine.Random),"Range",new[]{typeof(int),typeof(int)});
            int count=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(range)||instruction.Calls(value)||instruction.Calls(intRange))
                {
                    bool isRange=instruction.Calls(range);
                    bool isInt=instruction.Calls(intRange);
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(NivarianSimulationRandom),isInt?nameof(IntRange):isRange?nameof(Range):nameof(Value));
                    count++;
                }
                yield return instruction;
            }
            if(count==0)throw new InvalidOperationException("Missing Nivarian random call: "+__originalMethod);
        }
        static float Range(float min,float max)=>MP.IsInMultiplayer?Rand.Range(min,max):UnityEngine.Random.Range(min,max);
        static float Value()=>MP.IsInMultiplayer?Rand.Value:UnityEngine.Random.value;
        static int IntRange(int min,int max)=>MP.IsInMultiplayer?Rand.Range(min,max):UnityEngine.Random.Range(min,max);
    }
}
