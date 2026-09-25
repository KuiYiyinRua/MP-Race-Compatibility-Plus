using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Factions;
using Verse;

namespace Meow.RjwInfrastructure
{
    internal static class EventMapContext
    {
        internal static void Apply(Harmony harmony)
        {
            var type=AccessTools.TypeByName("PLAMilira.PLAMilira_GameComponent");
            if(type==null)return;
            var tick=AccessTools.DeclaredMethod(type,"GameComponentTick",Type.EmptyTypes);
            if(tick==null||tick.ReturnType!=typeof(void))throw new MissingMethodException("INFRA REQUIRED_TARGET_FAILURE: event component tick changed");
            harmony.Patch(tick,prefix:new HarmonyMethod(typeof(EventMapContext),nameof(Before)){priority=Priority.Last},
                transpiler:new HarmonyMethod(typeof(EventMapContext),nameof(Transpile)),
                finalizer:new HarmonyMethod(typeof(EventMapContext),nameof(After)){priority=Priority.Last});
            Log.Message("[Meow.RjwInfrastructure] Periodic event targeting is independent of the local viewed map.");
        }
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var list=instructions.ToList();
            var current=AccessTools.PropertyGetter(typeof(Find),nameof(Find.CurrentMap));
            var home=AccessTools.PropertyGetter(typeof(Find),nameof(Find.AnyPlayerHomeMap));
            if(list.Count(i=>i.Calls(current))!=1||list.Count(i=>i.Calls(home))!=2)
                throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: event map getter shape changed");
            foreach(var i in list)
            {
                if(i.Calls(current))i.operand=AccessTools.Method(typeof(EventMapContext),nameof(CurrentMap));
                else if(i.Calls(home))i.operand=AccessTools.Method(typeof(EventMapContext),nameof(HomeMap));
            }
            return list;
        }
        private static Map CurrentMap()=>MP.IsInMultiplayer?CalendarFactionContext.FindOwnedHomeMap():Find.CurrentMap;
        private static Map HomeMap()=>MP.IsInMultiplayer?CalendarFactionContext.FindOwnedHomeMap():Find.AnyPlayerHomeMap;
        private static void Before(out Map __state)
        {
            __state=null;
            if(!MP.IsInMultiplayer)return;
            int tick=Find.TickManager.TicksAbs;
            if((tick+300)%60000!=0&&(tick+600)%60000!=0)return;
            var map=CalendarFactionContext.FindOwnedHomeMap();
            if(map==null)return;
            map.PushFaction(map.ParentFaction);__state=map;
        }
        private static Exception After(Exception __exception,Map __state){__state?.PopFaction();return __exception;}
    }
}
