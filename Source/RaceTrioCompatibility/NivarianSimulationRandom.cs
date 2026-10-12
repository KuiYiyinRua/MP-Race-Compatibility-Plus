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
            if (CommanderOwns(target, method)) return;
            harmony.Patch(target,transpiler:new HarmonyMethod(typeof(NivarianSimulationRandom),nameof(Replace)));
        }
        // Reviewed installed NMC owns these draws and their save state. Do not
        // replace its input with a new Meow helper (NMC pins the older helper MVID).
        static bool CommanderOwns(MethodInfo target, string method)
        {
            string owner = method == "OrbitTarget" ? "local.nivarian.commander.native-orbit-random" :
                method == "CompTick" ? "local.nivarian.commander.native-turret-idle-random" : null;
            if (owner == null) return false;
            var patches = Harmony.GetPatchInfo(target);
            if (patches == null) return false;
            foreach (var patch in patches.Transpilers)
            {
                var p = patch.PatchMethod;
                string expectedType = "NivarianCommander.MultiplayerAdapter." +
                    (method == "OrbitTarget" ? "NativeDroneOrbitRandom" : "NativeTurretIdleRandom");
                if (patch.owner != owner || p.Name != "Transpiler" || p.DeclaringType?.FullName != expectedType ||
                    p.Module.Assembly.GetName().Name != "NivarianCommander.Multiplayer") continue;
                // Installed IL-reviewed NMC 0.9.6, 0.9.7 and 0.9.8 own the same narrow draws.
                if (p.Module.ModuleVersionId == new Guid("3e83802b-86f4-43c8-b39d-7557dbbc6406") ||
                    p.Module.ModuleVersionId == new Guid("1b86d2ca-7741-4c1e-9118-e76d8224e4e2") ||
                    p.Module.ModuleVersionId == new Guid("579a0deb-6d02-484c-a4c7-0e75d0df2fbb"))
                    {
                        Log.Message("[NivarianMP] reviewed NMC random owner retained: " + target);
                        return true;
                    }
            }
            return false;
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
