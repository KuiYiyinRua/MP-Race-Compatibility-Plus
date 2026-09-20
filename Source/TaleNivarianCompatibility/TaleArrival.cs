using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class TaleArrival
    {
        static Action<Map, Faction, bool> push;
        static Func<Map, Faction> pop;
        static Type arrivalWorker;
        internal sealed class Scope { internal Map Map; internal SavedMapManagers Managers; }
        internal static void Apply(Harmony harmony)
        {
            var context = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions") ?? throw new TypeLoadException("MP faction extensions");
            push = (Action<Map,Faction,bool>)Delegate.CreateDelegate(typeof(Action<Map,Faction,bool>), AccessTools.DeclaredMethod(context,"PushFaction",new[]{typeof(Map),typeof(Faction),typeof(bool)}));
            pop = (Func<Map,Faction>)Delegate.CreateDelegate(typeof(Func<Map,Faction>), AccessTools.DeclaredMethod(context,"PopFaction",new[]{typeof(Map)}));
            var component = AccessTools.TypeByName("TheTaleofMilira.GameComponent_MiliraMahouShoujoEvent") ?? throw new TypeLoadException("Tale arrival component");
            harmony.Patch(AccessTools.DeclaredMethod(component,"GameComponentTick"), transpiler:new HarmonyMethod(typeof(TaleArrival),nameof(MapSelection)));
            var worker = AccessTools.TypeByName("TheTaleofMilira.IncidentWorker_MiliraMahouShoujoArrival") ?? throw new TypeLoadException("Tale arrival worker");
            arrivalWorker = worker;
            // The base entry tests requireColonistsPresent before invoking the worker.
            // Under spectator context it returns true without spawning or registering a pawn.
            // Enter after MP's map context and restore before MP removes that context.
            harmony.Patch(AccessTools.DeclaredMethod(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute), new[]{typeof(IncidentParms)}),
                prefix:new HarmonyMethod(typeof(TaleArrival),nameof(BeforeEntry)){priority=Priority.Last},
                finalizer:new HarmonyMethod(typeof(TaleArrival),nameof(After)){priority=Priority.First});
            foreach(var name in new[]{"CanFireNowSub","TryExecuteWorker"})
                harmony.Patch(AccessTools.DeclaredMethod(worker,name), prefix:new HarmonyMethod(typeof(TaleArrival),nameof(Before)),finalizer:new HarmonyMethod(typeof(TaleArrival),nameof(After)));
            Log.Message("[TaleNivarianCompat] scheduled arrival selects shared player homes; incident uses its target faction.");
        }
        static IEnumerable<CodeInstruction> MapSelection(IEnumerable<CodeInstruction> instructions)
        {
            var home=AccessTools.PropertyGetter(typeof(Find),nameof(Find.RandomPlayerHomeMap));
            var current=AccessTools.PropertyGetter(typeof(Find),nameof(Find.CurrentMap));
            int homes=0,fallbacks=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(home)){instruction.operand=AccessTools.Method(typeof(TaleArrival),nameof(Home));homes++;}
                else if(instruction.Calls(current)){instruction.operand=AccessTools.Method(typeof(TaleArrival),nameof(Fallback));fallbacks++;}
                yield return instruction;
            }
            if(homes!=1 || fallbacks!=1)throw new InvalidOperationException("Tale arrival map calls changed: "+homes+"/"+fallbacks);
        }
        static Map Home()
        {
            if(!MP.IsInMultiplayer)return Find.RandomPlayerHomeMap;
            var homes=new List<Map>();
            foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
            {
                if(map.ParentFaction?.def?.isPlayer!=true)continue;
                var saved = new SavedMapManagers(map);
                push(map,map.ParentFaction,true);
                try{if(map.IsPlayerHome)homes.Add(map);}
                finally{try{pop(map);}finally{saved.Restore();}}
            }
            return homes.RandomElementWithFallback();
        }
        static Map Fallback()=>MP.IsInMultiplayer?null:Find.CurrentMap;
        static void BeforeEntry(IncidentWorker __instance,IncidentParms parms,out Scope __state)
        {
            __state=null;
            if(arrivalWorker.IsInstanceOfType(__instance))Before(parms,out __state);
        }
        static void Before(IncidentParms parms,out Scope __state)
        {
            __state=null;
            if(!MP.IsInMultiplayer || !(parms?.target is Map map) || map.ParentFaction?.def?.isPlayer!=true)return;
            __state=new Scope{Map=map,Managers=new SavedMapManagers(map)};
            push(map,map.ParentFaction,true);
        }
        static void After(Scope __state){if(__state!=null)try{pop(__state.Map);}finally{__state.Managers.Restore();}}
    }
}
