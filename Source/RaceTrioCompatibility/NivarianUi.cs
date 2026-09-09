using System;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class NivarianUi
    {
        static Type tuning;
        internal static void Apply(Harmony harmony)
        {
            Bills.Apply(harmony);
            tuning=AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompTuneableMachines");
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetThreshold));
            MP.RegisterSyncMethod(typeof(NivarianUi),nameof(SetQuality));
            harmony.Patch(AccessTools.Method(tuning,"CompGetGizmosExtra"),postfix:new HarmonyMethod(typeof(NivarianUi),nameof(TuningGizmos)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompCryoPrinter"),"OpenMaximumQualityFloatMenu"),prefix:new HarmonyMethod(typeof(NivarianUi),nameof(QualityMenu)));
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
