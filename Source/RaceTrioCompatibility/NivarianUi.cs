using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class NivarianUi
    {
        static Type tuning;
        static Type scales;
        internal static void Apply(Harmony harmony)
        {
            Bills.Apply(harmony);
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetManualPriorities));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.UI.NiraControlCenterWorkTabPanel"),"DoManualPrioritiesCheckbox"),prefix:new HarmonyMethod(typeof(NivarianUi),nameof(ManualPriorities)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian.GameComp_NivarianNiraMetrics"),"RefreshHoloDice"),prefix:new HarmonyMethod(typeof(NivarianUi),nameof(SimulationOnly)));
            foreach(var type in new[]{"ThingComp_ExpCanister","Comp_FlyActivator"})
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps."+type),"PostSpawnSetup"),
                    prefix:new HarmonyMethod(typeof(NivarianUi),nameof(BeforeRespawn)),postfix:new HarmonyMethod(typeof(NivarianUi),nameof(AfterRespawn)));
            scales=AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.Comp_ScaleControl");
            MP.RegisterSyncMethod(AccessTools.Method(scales,"SetFocus"));
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetScaleRange));
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetAbsorbedSkill));
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetDrain));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister"),"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianUi),nameof(KnowledgeGizmos)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.Comp_KnowledgeAbsorber"),"CompGetGizmosExtraNivarian"),postfix:new HarmonyMethod(typeof(NivarianUi),nameof(KnowledgeGizmos)));
            MP.RegisterSyncMethod(AccessTools.PropertySetter(scales,"ScaleSeverity")).SetDebugOnly();
            harmony.Patch(AccessTools.Method(scales,"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianUi),nameof(ScaleGizmos)));
            tuning=AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompTuneableMachines");
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetThreshold));
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetQuality));
            harmony.Patch(AccessTools.Method(tuning,"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianUi),nameof(TuningGizmos)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompCryoPrinter"),"OpenMaximumQualityFloatMenu"),prefix:new HarmonyMethod(typeof(NivarianUi),nameof(QualityMenu)));
        }
        static bool SimulationOnly()=>!MP.IsInMultiplayer||!MP.InInterface;
        static string SavedSetting(ThingComp comp)=>comp.GetType().Name=="ThingComp_ExpCanister"?"_curSkill":"CurrentMode";
        static void BeforeRespawn(ThingComp __instance,bool __0,out object __state)
        {
            __state=MP.IsInMultiplayer&&__0?AccessTools.Field(__instance.GetType(),SavedSetting(__instance)).GetValue(__instance):null;
        }
        static void AfterRespawn(ThingComp __instance,object __state)
        {
            if(__state!=null)AccessTools.Field(__instance.GetType(),SavedSetting(__instance)).SetValue(__instance,__state);
        }
        static bool ManualPriorities(UnityEngine.Rect __0)
        {
            if(!MP.IsInMultiplayer)return true;
            Text.Font=GameFont.Small;
            UnityEngine.GUI.color=UnityEngine.Color.white;
            Text.Anchor=UnityEngine.TextAnchor.UpperLeft;
            var rect=new UnityEngine.Rect(__0.x+5f,__0.y+5f,140f,30f);
            bool value=Current.Game.playSettings.useWorkPriorities;
            Widgets.CheckboxLabeled(rect,"ManualPriorities".Translate(),ref value);
            if(value!=Current.Game.playSettings.useWorkPriorities)SetManualPriorities(value);
            if(value)Widgets.Label(new UnityEngine.Rect(rect.x,rect.yMax-6f,rect.width,60f),"PriorityOneDoneFirst".Translate());
            else UIHighlighter.HighlightOpportunity(rect,"ManualPriorities-Off");
            return false;
        }
        static void SetManualPriorities(bool value)
        {
            if(Current.Game.playSettings.useWorkPriorities==value)return;
            Current.Game.playSettings.useWorkPriorities=value;
            foreach(var pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
                if(pawn.Faction==Faction.OfPlayer&&pawn.workSettings!=null)pawn.workSettings.Notify_UseWorkPrioritiesChanged();
        }
        static void ScaleGizmos(ThingComp __instance,ref IEnumerable<Gizmo> __result)
        {
            if(MP.IsInMultiplayer)__result=WrapScales(__instance,__result);
        }
        static void KnowledgeGizmos(ThingComp __instance,ref IEnumerable<Gizmo> __result)
        {
            if(MP.IsInMultiplayer)__result=WrapKnowledge(__instance,__result);
        }
        static IEnumerable<Gizmo> WrapKnowledge(ThingComp comp,IEnumerable<Gizmo> gizmos)
        {
            bool skillCommand=true;
            foreach(var gizmo in gizmos)
            {
                if(gizmo.GetType().FullName=="Nivarian_Race.Code.UI.Gizmo_SetThreshold")
                    AccessTools.Field(gizmo.GetType(),"onValueChanged").SetValue(gizmo,new Action<float>(value=>SetDrain(comp,value)));
                if(skillCommand&&gizmo is Command_Action command){skillCommand=false;command.action=()=>{
                    var skills=(IEnumerable<SkillDef>)AccessTools.Field(comp.props.GetType(),"availableSkillDefs").GetValue(comp.props);
                    Find.WindowStack.Add(new FloatMenu(skills.Select(skill=>new FloatMenuOption(skill.label,()=>SetAbsorbedSkill(comp,skill))).ToList()));
                };}
                yield return gizmo;
            }
        }
        static void SetAbsorbedSkill(ThingComp comp,SkillDef skill)
        {
            if(comp==null||comp.parent.Destroyed||skill==null
                ||(comp.GetType().FullName!="Nivarian_Race.Code.Comps.ThingComps.Comp_KnowledgeAbsorber"
                &&comp.GetType().FullName!="Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister"))return;
            var skills=(IEnumerable<SkillDef>)AccessTools.Field(comp.props.GetType(),"availableSkillDefs").GetValue(comp.props);
            if(skills.Contains(skill))AccessTools.Field(comp.GetType(),"_curSkill").SetValue(comp,skill);
        }
        static void SetDrain(ThingComp comp,float value)
        {
            if(comp==null||comp.parent.Destroyed||float.IsNaN(value)||float.IsInfinity(value)
                ||comp.GetType().FullName!="Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister")return;
            AccessTools.Field(comp.GetType(),"_drainAmountPercent").SetValue(comp,UnityEngine.Mathf.Clamp(value,0.01f,1f));
        }
        static IEnumerable<Gizmo> WrapScales(ThingComp comp,IEnumerable<Gizmo> gizmos)
        {
            foreach(var gizmo in gizmos)
            {
                if(gizmo.GetType().FullName=="Nivarian_Race.Code.UI.Gizmo_ScaleSetting")
                    AccessTools.Field(gizmo.GetType(),"action").SetValue(gizmo,new Action(()=>{
                        var range=(FloatRange)AccessTools.Field(gizmo.GetType(),"range").GetValue(gizmo);
                        // Capture the local selection before dispatch; replay never reads Selector.
                        var targets=Find.Selector.SelectedObjects.OfType<ThingWithComps>()
                            .SelectMany(t=>t.AllComps).Where(c=>c.GetType()==scales)
                            .Concat(new[]{comp}).Distinct().OrderBy(c=>c.parent.thingIDNumber).ToList();
                        foreach(var target in targets)SetScaleRange(target,range.min,range.max);
                    }));
                yield return gizmo;
            }
        }
        static void SetScaleRange(ThingComp comp,float min,float max)
        {
            if(comp==null||comp.parent.Destroyed||comp.GetType()!=scales
                ||float.IsNaN(min)||float.IsNaN(max)||float.IsInfinity(min)||float.IsInfinity(max))return;
            min=UnityEngine.Mathf.Clamp01(min);
            max=UnityEngine.Mathf.Clamp(max,min,1f);
            AccessTools.Field(scales,"_playerSetThreshold").SetValue(comp,new FloatRange(min,max));
        }
        static void TuningGizmos(ThingComp __instance,ref IEnumerable<Gizmo> __result)
        {
            if(MP.IsInMultiplayer)__result=Wrap(__instance,__result);
        }
        static IEnumerable<Gizmo> Wrap(ThingComp comp,IEnumerable<Gizmo> gizmos)
        {
            foreach(var gizmo in gizmos)
            {
                if(gizmo.GetType().FullName=="Nivarian_Race.Code.UI.Gizmo_SetThreshold")
                    AccessTools.Field(gizmo.GetType(),"onValueChanged").SetValue(gizmo,new Action<float>(value=>{
                        var visited=new HashSet<ThingComp>{comp};
                        SetThreshold(comp,value);
                        foreach(var selected in Find.Selector.SelectedObjects)
                            if(selected is ThingWithComps thing)
                                foreach(var other in thing.AllComps)
                                    if(other.GetType()==tuning&&visited.Add(other))SetThreshold(other,value);
                    }));
                yield return gizmo;
            }
        }
        static void SetThreshold(ThingComp comp,float value)
        {
            AccessTools.Field(tuning,"_playerSetThreshold").SetValue(comp,UnityEngine.Mathf.Clamp(value,0f,0.95f));
        }
        static bool QualityMenu(ThingComp __instance)
        {
            if(!MP.IsInMultiplayer)return true;
            var options=new List<FloatMenuOption>();
            foreach(QualityCategory value in Enum.GetValues(typeof(QualityCategory)))
            {
                var quality=value;
                options.Add(new FloatMenuOption(quality.GetLabel().CapitalizeFirst(),()=>SetQuality(__instance,(int)quality)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
            return false;
        }
        static void SetQuality(ThingComp comp,int quality)
        {
            if(quality<0||quality>6)return;
            AccessTools.Field(comp.GetType(),"_maximumQuality").SetValue(comp,(QualityCategory)quality);
        }
    }
}
