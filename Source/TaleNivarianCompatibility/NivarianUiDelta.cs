using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianUiDelta
    {
        static Type scales;
        static FieldInfo thresholdCallback, scaleCallback, scaleRange;
        sealed class RefreshRequest { public int Day=int.MinValue; }
        static readonly ConditionalWeakTable<object,RefreshRequest> RefreshRequests=new ConditionalWeakTable<object,RefreshRequest>();
        static ISyncMethod refreshDice;
        internal static void Apply(Harmony harmony)
        {
            thresholdCallback=AccessTools.Field(AccessTools.TypeByName("Nivarian_Race.Code.UI.Gizmo_SetThreshold"),"onValueChanged") ?? throw new MissingFieldException("Nivarian threshold callback");
            var scaleGizmo=AccessTools.TypeByName("Nivarian_Race.Code.UI.Gizmo_ScaleSetting");
            scaleCallback=AccessTools.Field(scaleGizmo,"action") ?? throw new MissingFieldException("Nivarian scale callback");
            scaleRange=AccessTools.Field(scaleGizmo,"range") ?? throw new MissingFieldException("Nivarian scale range");
            MP.RegisterSyncMethod(typeof(NivarianUiDelta),nameof(SetManualPriorities));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.UI.NiraControlCenterWorkTabPanel"),"DoManualPrioritiesCheckbox"),prefix:new HarmonyMethod(typeof(NivarianUiDelta),nameof(ManualPriorities)));
            var refresh=AccessTools.Method(AccessTools.TypeByName("Nivarian.GameComp_NivarianNiraMetrics"),"RefreshHoloDice");
            refreshDice=MP.RegisterSyncMethod(refresh);
            harmony.Patch(refresh,prefix:new HarmonyMethod(typeof(NivarianUiDelta),nameof(RefreshDicePrefix)));
            foreach(var type in new[]{"ThingComp_ExpCanister","Comp_FlyActivator"})
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps."+type),"PostSpawnSetup"),
                    transpiler:new HarmonyMethod(typeof(NivarianUiDelta),nameof(PreserveLoadedSetting)));
            scales=AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.Comp_ScaleControl");
            MP.RegisterSyncMethod(AccessTools.Method(scales,"SetFocus"));
            MP.RegisterSyncMethod(typeof(NivarianUiDelta),nameof(SetScaleRange));
            MP.RegisterSyncMethod(typeof(NivarianUiDelta),nameof(SetAbsorbedSkill));
            MP.RegisterSyncMethod(typeof(NivarianUiDelta),nameof(SetDrain));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister"),"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianUiDelta),nameof(KnowledgeGizmos)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.Comp_KnowledgeAbsorber"),"CompGetGizmosExtraNivarian"),postfix:new HarmonyMethod(typeof(NivarianUiDelta),nameof(KnowledgeGizmos)));
            MP.RegisterSyncMethod(AccessTools.PropertySetter(scales,"ScaleSeverity")).SetDebugOnly();
            harmony.Patch(AccessTools.Method(scales,"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianUiDelta),nameof(ScaleGizmos)));
        }
        static bool RefreshDicePrefix(object __instance)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            // The UI calls this every draw. Request the initial refresh once, never a command per frame.
            // Normal world ticks still perform the authoritative daily refresh in the original component.
            var state=RefreshRequests.GetOrCreateValue(__instance);int day=GenDate.DaysPassedSinceSettle;
            if(state.Day!=day&&refreshDice.DoSync(__instance))state.Day=day;
            return false;
        }
        static bool KeepLoadedSetting(bool respawningAfterLoad)=>MP.IsInMultiplayer&&respawningAfterLoad;
        internal static IEnumerable<CodeInstruction> PreserveLoadedSetting(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod,ILGenerator generator)
        {
            string name=__originalMethod.DeclaringType.Name=="ThingComp_ExpCanister"?"_curSkill":"CurrentMode";
            var field=AccessTools.Field(__originalMethod.DeclaringType,name);int count=0;
            foreach(var original in instructions)
            {
                var code=new CodeInstruction(original);
                if(code.opcode==OpCodes.Stfld&&Equals(code.operand,field))
                {
                    // Skip only the default assignment on load; registration and drawer refresh then see the saved value.
                    var store=generator.DefineLabel();var done=generator.DefineLabel();
                    var arg=new CodeInstruction(OpCodes.Ldarg_1);arg.labels.AddRange(code.labels);arg.blocks.AddRange(code.blocks);code.labels.Clear();code.blocks.Clear();
                    yield return arg;yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(NivarianUiDelta),nameof(KeepLoadedSetting)));
                    yield return new CodeInstruction(OpCodes.Brfalse,store);yield return new CodeInstruction(OpCodes.Pop);yield return new CodeInstruction(OpCodes.Pop);yield return new CodeInstruction(OpCodes.Br,done);
                    code.labels.Add(store);yield return code;var end=new CodeInstruction(OpCodes.Nop);end.labels.Add(done);yield return end;count++;
                }
                else yield return code;
            }
            if(count!=1)throw new InvalidOperationException("Nivarian respawn assignment changed: "+__originalMethod+" count="+count);
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
                    thresholdCallback.SetValue(gizmo,new Action<float>(value=>SetDrain(comp,value)));
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
                    scaleCallback.SetValue(gizmo,new Action(()=>{
                        var range=(FloatRange)scaleRange.GetValue(gizmo);
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
    }
}
