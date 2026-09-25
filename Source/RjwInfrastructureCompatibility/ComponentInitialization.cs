using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RjwInfrastructure
{
    internal static class ComponentInitialization
    {
        private static FieldInfo poolField;
        internal static void Apply(Harmony harmony)
        {
            var type=AccessTools.TypeByName("PLAMilira.PLAMilira_GameComponent");
            if(type==null)return;
            poolField=AccessTools.Field(type,"randomIncidentPool");
            var ctor=AccessTools.Constructor(type,new[]{typeof(Game)});
            if(poolField==null||!poolField.IsStatic||poolField.FieldType!=typeof(List<IncidentDef>)||ctor==null)
                throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: shared component pool signature changed");
            var add=AccessTools.Method(typeof(List<IncidentDef>),nameof(List<IncidentDef>.Add));
            if(PatchProcessor.GetOriginalInstructions(ctor).Count(i=>i.Calls(add))!=3)
                throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: shared component constructor additions changed");
            harmony.Patch(ctor,prefix:new HarmonyMethod(typeof(ComponentInitialization),nameof(BeforeConstructor)) { priority=Priority.First });
            Log.Message("[Meow.RjwInfrastructure] Component construction resets the static pool to its original two-entry seed.");
        }
        private static void BeforeConstructor()
        {
            if(!MP.enabled)return;
            var pool=(List<IncidentDef>)poolField.GetValue(null);
            if(pool==null||pool.Count<2||pool[0]?.defName!="PLAMilira_CrystalCluster_WorldMap"||pool[1]?.defName!="PLAMilira_HarrierCore_WorldMap")
                throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: shared component pool seed changed");
            // Keep the original first-construction weighting: the two static
            // entries plus the constructor's own additions. Do not deduplicate.
            if(pool.Count>2)pool.RemoveRange(2,pool.Count-2);
        }
    }
}
