using System.Collections.Generic;
using System.Reflection.Emit;
using AM.Grappling;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    // In 1.6, inherited DynamicDrawPhaseAt recomputes the base flight curve and
    // writes Thing.Position. The mod's flyers spawn at their destination and use
    // their own DrawPos curves; camera visibility must not move their map-grid entry.
    [HarmonyPatch(typeof(PawnFlyer), "RecomputePosition")]
    internal static class FlyerGridPosition
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var setter=AccessTools.PropertySetter(typeof(Thing),nameof(Thing.Position));
            int replaced=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(setter))
                {
                    replaced++;
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(FlyerGridPosition),nameof(SetPosition));
                }
                yield return instruction;
            }
            if(replaced!=1) throw new System.InvalidOperationException("Flyer render position: expected one setter, found "+replaced);
        }
        private static void SetPosition(Thing flyer,IntVec3 position)
        {
            if(Bootstrap.Active && (flyer is GrappleFlyer || flyer is KnockbackFlyer)) return;
            flyer.Position=position;
        }
    }

    [HarmonyPatch(typeof(KnockbackFlyer), nameof(KnockbackFlyer.MakeKnockbackFlyer))]
    internal static class KnockbackOrigin
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced=0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.DrawPos))))
                {
                    replaced++;
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(KnockbackOrigin),nameof(Position));
                }
                yield return instruction;
            }
            if(replaced!=1) throw new System.InvalidOperationException("Knockback origin: expected one Thing.DrawPos call, found "+replaced);
        }
        private static Vector3 Position(Thing pawn) => Bootstrap.Active
            ? pawn.Position.ToVector3ShiftedWithAltitude(AltitudeLayer.Pawn.AltitudeFor()) : pawn.DrawPos;
    }

    [HarmonyPatch(typeof(KnockbackFlyer), nameof(KnockbackFlyer.Tick))]
    internal static class KnockbackDustRandom
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var integer=AccessTools.Method(typeof(Rand),nameof(Rand.Range),new[]{typeof(int),typeof(int)});
            var real=AccessTools.Method(typeof(Rand),nameof(Rand.Range),new[]{typeof(float),typeof(float)});
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(integer)) instruction.operand=AccessTools.Method(typeof(KnockbackDustRandom),nameof(Integer));
                if(instruction.Calls(real)) instruction.operand=AccessTools.Method(typeof(KnockbackDustRandom),nameof(Real));
                yield return instruction;
            }
        }
        private static int Integer(int min,int max)
        {
            if(!Bootstrap.Active) return Rand.Range(min,max);
            Rand.PushState(); try{return Rand.Range(min,max);}finally{Rand.PopState();}
        }
        private static float Real(float min,float max)
        {
            if(!Bootstrap.Active) return Rand.Range(min,max);
            Rand.PushState(); try{return Rand.Range(min,max);}finally{Rand.PopState();}
        }
    }
}
