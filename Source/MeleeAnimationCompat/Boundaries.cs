using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AM;
using AM.Buildings;
using AM.Idle;
using AM.Patches;
using AM.UI;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    [HarmonyPatch]
    internal static class VisibilityThread
    {
        [ThreadStatic] internal static bool Rendering;
        private static readonly FieldInfo Original = AccessTools.Field(typeof(Patch_InvisibilityUtility_IsPsychologicallyInvisible), "IsRendering");
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(var type in typeof(Core).Assembly.GetTypes())
                foreach(var method in type.GetMethods(AccessTools.allDeclared))
                    if(!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody()!=null &&
                        PatchProcessor.GetOriginalInstructions(method).Any(i=>Equals(i.operand,Original)))
                        yield return method;
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach(var i in instructions)
            {
                if(Equals(i.operand,Original)) i.operand=AccessTools.Field(typeof(VisibilityThread),nameof(Rendering));
                yield return i;
            }
        }
        internal static void RepairPathScope(Harmony harmony)
        {
            var target=AccessTools.Method(typeof(PawnUtility),nameof(PawnUtility.PawnBlockingPathAt));
            // The installed target mistakenly declares its restore callback as a second Prefix.
            foreach(var prefix in typeof(Patch_PawnUtility_PawnBlockingPathAt).GetMethods(AccessTools.allDeclared).Where(m=>m.Name=="Prefix"))
                harmony.Unpatch(target,prefix);
        }
    }

    [HarmonyPatch]
    internal static class VisibilityScope
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PawnUtility),nameof(PawnUtility.PawnBlockingPathAt));
            yield return AccessTools.Method(typeof(Building_ProximityDetector),"RunDetection");
            yield return AccessTools.Method(typeof(PawnRenderTree),nameof(PawnRenderTree.ParallelPreDraw));
            yield return AccessTools.Method(typeof(AnimRenderer),"DrawPawns");
        }
        [HarmonyPriority(Priority.First)]
        private static void Prefix(MethodBase __originalMethod,out bool __state)
        {
            __state=VisibilityThread.Rendering;
            if(__originalMethod.DeclaringType==typeof(PawnUtility)) VisibilityThread.Rendering=true;
        }
        private static void Finalizer(bool __state) => VisibilityThread.Rendering=__state;
    }

    [HarmonyPatch(typeof(Building_DuelSpot),nameof(Building_DuelSpot.GetFreeSpectateSpots))]
    internal static class FreshSpectatorCells
    {
        private static readonly Action<Building_DuelSpot> Refresh=AccessTools.MethodDelegate<Action<Building_DuelSpot>>(
            AccessTools.Method(typeof(Building_DuelSpot),"UpdateSpectateSpots"));
        private static void Prefix(Building_DuelSpot __instance)
        {
            if(Bootstrap.Active && __instance.Spawned) Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(Patch_PawnGenerator_GeneratePawn),"GetRandomLasso")]
    internal static class OrderedLasso
    {
        private static bool Prefix(ref ThingDef __result)
        {
            if(!Bootstrap.Active) return true;
            __result=Content.LassoDefs.OrderBy(d=>d.defName,StringComparer.Ordinal)
                .RandomElementByWeightWithFallback(d=>1f/Mathf.Pow(d.BaseMarketValue,3));
            return false;
        }
    }

    [HarmonyPatch(typeof(AM.Extensions),nameof(AM.Extensions.MakeAnimationMatrix),new[]{typeof(Pawn),typeof(float)})]
    internal static class StableAnimationRoot
    {
        private static bool Prefix(Pawn pawn,float yOffset,ref Matrix4x4 __result)
        {
            if(!Bootstrap.Active) return true;
            __result=Matrix4x4.TRS(pawn.Position.ToVector3ShiftedWithAltitude(AltitudeLayer.Pawn.AltitudeFor()+yOffset),Quaternion.identity,Vector3.one);
            return false;
        }
    }

    [HarmonyPatch(typeof(IdleControllerComp),"ShouldHaveSkills")]
    internal static class ReadSavedSkills
    {
        private static bool Prefix(AM.UniqueSkills.UniqueSkillInstance[] ___skills,ref bool __result)
        {
            // Incoming pawn data precedes the save-owned settings component. Never gate
            // deserialization on joining-client preferences or unresolved pawn faction.
            if(Scribe.mode==LoadSaveMode.Inactive || Scribe.mode==LoadSaveMode.Saving) return true;
            if(Scribe.mode==LoadSaveMode.LoadingVars)
                __result=DefDatabase<UniqueSkillDef>.AllDefsListForReading.Any(d=>Scribe.loader.curXmlParent?[d.instanceClass.FullName]!=null);
            else
                __result=___skills!=null && ___skills.Any(s=>s!=null);
            return false;
        }
    }

    [HarmonyPatch(typeof(Dialog_AnimationDebugger),nameof(Dialog_AnimationDebugger.IsInRehearsalMode),MethodType.Getter)]
    internal static class RehearsalCannotAffectSimulation
    {
        private static void Postfix(ref bool __result)
        {
            if(Bootstrap.Active) __result=false;
        }
    }

    [HarmonyPatch(typeof(Dialog_AnimationDebugger),nameof(Dialog_AnimationDebugger.DoWindowContents))]
    internal static class DebuggerBoundary
    {
        private static bool Prefix(Rect inRect)
        {
            if(!Bootstrap.Active) return true;
            Widgets.Label(inRect,"Melee Animation: use the animation debugger in singleplayer. Live animation editing is unavailable during multiplayer.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Dialog_TweakEditor),nameof(Dialog_TweakEditor.DoWindowContents))]
    internal static class TweakEditorBoundary
    {
        private static bool Prefix(Rect inRect)
        {
            if(!Bootstrap.Active) return true;
            Widgets.Label(inRect,"Melee Animation: edit weapon compatibility data in singleplayer, then use identical mod files on every multiplayer peer.");
            return false;
        }
    }
}
