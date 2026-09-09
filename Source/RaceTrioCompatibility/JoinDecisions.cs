using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    public sealed class PendingWanderer : IExposable
    {
        public Pawn pawn;
        public Map map;
        public IncidentDef incident;
        public void ExposeData(){Scribe_Deep.Look(ref pawn,"pawn");Scribe_References.Look(ref map,"map");Scribe_Defs.Look(ref incident,"incident");}
    }
    public sealed class JoinDecisions : GameComponent
    {
        List<PendingWanderer> pending=new List<PendingWanderer>();
        static Type windowType;
        public JoinDecisions(Game game){}
        static JoinDecisions Instance=>Current.Game.GetComponent<JoinDecisions>();
        internal static void Apply(Harmony harmony)
        {
            windowType=AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_NivarianWandererDialog");
            MP.RegisterSyncMethod(typeof(JoinDecisions),nameof(Decide));
            var worker=AccessTools.TypeByName("Nivarian_Race.Code.Incidents.IncidentWorker_NivarianWanderer");
            harmony.Patch(AccessTools.Method(worker,"TryExecuteWorker"),postfix:new HarmonyMethod(typeof(JoinDecisions),nameof(IncidentPostfix)));
            var icy=AccessTools.TypeByName("Nivarian.QuestPart_IcyCheck");
            MP.RegisterSyncMethod(AccessTools.Method(icy,"ApplyJoinChoices"),null);
            harmony.Patch(AccessTools.Method(icy,"ApplyJoinChoices"),postfix:new HarmonyMethod(typeof(JoinDecisions),nameof(ChoicesPostfix)));
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pending,"meowPendingWanderers",LookMode.Deep);
            if(pending==null)pending=new List<PendingWanderer>();
        }
        static void IncidentPostfix(IncidentWorker __instance,IncidentParms parms,bool __result)
        {
            if(!__result||!MP.IsInMultiplayer)return;
            var window=Find.WindowStack.Windows.LastOrDefault(w=>w.GetType()==windowType);
            if(window==null)return;
            var pawn=(Pawn)AccessTools.Field(windowType,"pawn").GetValue(window);
            var item=new PendingWanderer{pawn=pawn,map=parms.target as Map,incident=__instance.def};
            Instance.pending.Add(item);
            BindWindow(window,item);
        }
        static void BindWindow(Window window,PendingWanderer item)
        {
            int id=item.pawn.thingIDNumber;
            AccessTools.Field(windowType,"acceptAction").SetValue(window,new Action(()=>Decide(id,0)));
            AccessTools.Field(windowType,"refuseAction").SetValue(window,new Action(()=>Decide(id,1)));
            AccessTools.Field(windowType,"laterAction").SetValue(window,new Action(()=>Decide(id,2)));
        }
        public override void GameComponentUpdate()
        {
            if(!MP.IsInMultiplayer||windowType==null||pending.Count==0)return;
            foreach(var item in pending)
            {
                if(item.pawn==null||Find.WindowStack.Windows.Any(w=>w.GetType()==windowType&&ReferenceEquals(AccessTools.Field(windowType,"pawn").GetValue(w),item.pawn)))continue;
                int id=item.pawn.thingIDNumber;
                var window=(Window)Activator.CreateInstance(windowType,"Nivarian.Wanderer.DialogText".Translate().ToString(),item.pawn,
                    new Action(()=>Decide(id,0)),new Action(()=>Decide(id,1)),new Action(()=>Decide(id,2)));
                Find.WindowStack.Add(window);
            }
        }
        static void Decide(int pawnId,int choice)
        {
            var item=Instance.pending.FirstOrDefault(p=>p.pawn?.thingIDNumber==pawnId);
            if(item==null||choice<0||choice>2)return;
            Instance.pending.Remove(item);
            if(choice==0&&item.map!=null)
            {
                if(RCellFinder.TryFindRandomPawnEntryCell(out var cell,item.map,CellFinder.EdgeRoadChance_Animal))
                {
                    item.pawn.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(item.pawn,cell,item.map,Rot4.Random);
                    Find.LetterStack.ReceiveLetter(item.incident.letterLabel,"Nivarian.Wanderer.LetterText".Translate(),LetterDefOf.PositiveEvent,new LookTargets(item.pawn));
                }
            }
            else if(choice==1&&item.map!=null)
            {
                var comp=item.map.components.FirstOrDefault(c=>c.GetType().FullName=="Nivarian.MapComp_NivarianCommon");
                if(comp!=null)AccessTools.Field(comp.GetType(),"nivarianWandererRefused").SetValue(comp,true);
            }
            foreach(var window in Find.WindowStack.Windows.Where(w=>w.GetType()==windowType&&ReferenceEquals(AccessTools.Field(windowType,"pawn").GetValue(w),item.pawn)).ToList())window.Close();
        }
        static void ChoicesPostfix(QuestPart __instance)
        {
            if(!MP.IsInMultiplayer||MP.InInterface)return;
            foreach(var window in Find.WindowStack.Windows.Where(w=>w.GetType().FullName=="Nivarian_Race.Code.UI.Window_NivarianColdShelterJoinDialog").ToList())
            {
                var callback=AccessTools.Field(window.GetType(),"confirmAction").GetValue(window) as Delegate;
                if(ReferenceEquals(callback?.Target,__instance))window.Close();
            }
        }
    }
}
