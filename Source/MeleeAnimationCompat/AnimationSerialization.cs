using AM;
using AM.Grappling;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    [HarmonyPatch(typeof(AnimationStartParameters), nameof(AnimationStartParameters.ExposeData))]
    internal static class AnimationSerialization
    {
        // Serialization must also be correct for a single-player save later hosted.
        private static bool Prefix(ref AnimationStartParameters __instance)
        {
            Scribe_References.Look(ref __instance.MainPawn, "mainPawn");
            Scribe_References.Look(ref __instance.SecondPawn, "secondPawn");
            Scribe_Collections.Look(ref __instance.ExtraPawns, "pawns", LookMode.Reference);
            Scribe_Defs.Look(ref __instance.Animation, "animation");
            Scribe_Values.Look(ref __instance.FlipX, "flipX");
            Scribe_Values.Look(ref __instance.FlipY, "flipY");
            Scribe_References.Look(ref __instance.Map, "map");
            LookMatrix(ref __instance.RootTransform);
            Scribe_Values.Look(ref __instance.ExecutionOutcome, "mpExecutionOutcome", ExecutionOutcome.Down);
            Scribe_Values.Look(ref __instance.DoNotRegisterPawns, "mpDoNotRegisterPawns");
            Scribe_Defs.Look(ref __instance.CustomJobDef, "mpCustomJobDef");
            return false;
        }

        private static void LookMatrix(ref Matrix4x4 matrix)
        {
            if(Scribe.mode==LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent?["mpTrs0"]==null)
            {
                // Legacy Unity ToString stores rows separated by whitespace. The
                // game's ParseHelper has no Matrix4x4 parser.
                var legacy=Scribe.loader.curXmlParent?["trs"]?.InnerText;
                if(legacy!=null)
                {
                    var values=legacy.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
                    if(values.Length==16)
                        for(int i=0;i<16;i++) matrix[i/4,i%4]=float.Parse(values[i],CultureInfo.InvariantCulture);
                }
                return;
            }
            for(int i=0;i<16;i++)
            {
                var value=matrix[i];
                Scribe_Values.Look(ref value,"mpTrs"+i,0f,forceSave:true);
                matrix[i]=value;
            }
        }
    }

    [HarmonyPatch(typeof(JobDriver_GrapplePawn),nameof(JobDriver_GrapplePawn.ExposeData))]
    internal static class GrappleParameterNode
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced=0;
            foreach(var instruction in instructions)
            {
                if(instruction.operand is MethodInfo method && method.DeclaringType==typeof(Scribe_Deep) && method.Name=="Look" &&
                    method.IsGenericMethod && method.GetGenericArguments()[0]==typeof(AnimationStartParameters?))
                {
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(GrappleParameterNode),nameof(Look));
                    replaced++;
                }
                yield return instruction;
            }
            if(replaced!=1) throw new InvalidOperationException("Expected one grapple animation parameter save node, found "+replaced);
        }

        private static void Look(ref AnimationStartParameters? data,string label,object[] constructorArgs)
        {
            if(Scribe.mode!=LoadSaveMode.LoadingVars && !data.HasValue) return;
            if(!Scribe.EnterNode(label))
            {
                if(Scribe.mode==LoadSaveMode.LoadingVars) data=null;
                return;
            }
            try
            {
                if(Scribe.mode==LoadSaveMode.LoadingVars && string.Equals(Scribe.loader.curXmlParent?.Attributes?["IsNull"]?.Value,"true",StringComparison.OrdinalIgnoreCase))
                {data=null;return;}
                // Keep the owning JobDriver as Scribe's reference parent. Deep
                // saving a boxed struct loses cross-reference assignments.
                var args=data.GetValueOrDefault();
                args.ExposeData();
                data=args;
            }
            finally {Scribe.ExitNode();}
        }
    }

    [HarmonyPatch(typeof(JobDriver_GrapplePawn), nameof(JobDriver_GrapplePawn.ExposeData))]
    internal static class RecoverLegacyGrappleActors
    {
        private static void Postfix(JobDriver_GrapplePawn __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || !__instance.AnimationStartParameters.HasValue) return;
            var args = __instance.AnimationStartParameters.Value;
            if (args.SecondPawn == null || args.SecondPawn == args.MainPawn)
            {
                args.MainPawn = __instance.pawn;
                args.SecondPawn = __instance.GrappledPawn;
                __instance.AnimationStartParameters = args;
            }
            if(args.Map==null) args.Map=__instance.pawn?.Map;
            if(args.RootTransform==Matrix4x4.zero && __instance.pawn!=null) args.RootTransform=__instance.pawn.MakeAnimationMatrix();
            __instance.AnimationStartParameters=args;
        }
    }
}
