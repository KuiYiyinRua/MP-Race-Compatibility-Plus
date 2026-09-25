using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Factions;
using RimWorld;
using Verse;

namespace Meow.RjwInfrastructure
{
    internal static class CalendarFactionContext
    {
        private static int logged;
        internal static void Apply(Harmony harmony)
        {
            var type=AccessTools.TypeByName("Kiiro_Event.KiiroEventGameComponent_OverallControl");
            if(type==null)return;
            var target=AccessTools.DeclaredMethod(type,"GameComponentTick",Type.EmptyTypes);
            if(target==null||target.ReturnType!=typeof(void))throw new MissingMethodException("INFRA REQUIRED_TARGET_FAILURE: calendar component tick changed");
            var body=PatchProcessor.GetOriginalInstructions(target);
            var quadrum=AccessTools.Method(typeof(GenLocalDate),nameof(GenLocalDate.DayOfQuadrum),new[]{typeof(Map)});
            var year=AccessTools.Method(typeof(GenLocalDate),nameof(GenLocalDate.DayOfYear),new[]{typeof(Map)});
            if(body.Count(i=>i.Calls(AccessTools.PropertyGetter(typeof(Find),nameof(Find.AnyPlayerHomeMap))))!=3||
                body.Count(i=>i.Calls(quadrum))!=1||body.Count(i=>i.Calls(year))!=1||
                !body.Any(i=>i.opcode==System.Reflection.Emit.OpCodes.Ldc_I4&&Equals(i.operand,30000)))
                throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: calendar periodic block changed");
            // Let existing state-capturing prefixes run before a no-home skip,
            // so their finalizers can restore the state they temporarily replace.
            harmony.Patch(target,prefix:new HarmonyMethod(typeof(CalendarFactionContext),nameof(Before)) { priority=Priority.Last },
                finalizer:new HarmonyMethod(typeof(CalendarFactionContext),nameof(After)) { priority=Priority.Last });
            Log.Message("[Meow.RjwInfrastructure] Calendar component uses an owned home-map faction context.");
        }
        private static bool Before(out Map __state)
        {
            __state=null;
            if(!MP.IsInMultiplayer||Find.TickManager.TicksAbs%30000!=0)return true;
            var selected=FindOwnedHomeMap();
            // The periodic block requires a home-map incident target. A game
            // with no home has no valid target yet; do not call date APIs on null.
            if(selected==null)return false;
            selected.PushFaction(selected.ParentFaction);__state=selected;
            if(logged++<4)Log.Message("[Meow.RjwInfrastructure] Calendar context map="+selected.uniqueID+" faction="+selected.ParentFaction.loadID);
            return true;
        }
        internal static Map FindOwnedHomeMap()
        {
            Map selected=null;
            foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
            {
                if(map.ParentFaction?.IsPlayer!=true)continue;
                map.PushFaction(map.ParentFaction);
                try{if(map.IsPlayerHome)selected=map;}
                finally{map.PopFaction();}
                if(selected!=null)break;
            }
            return selected;
        }
        private static Exception After(Exception __exception,Map __state)
        {
            __state?.PopFaction();return __exception;
        }
    }
}
