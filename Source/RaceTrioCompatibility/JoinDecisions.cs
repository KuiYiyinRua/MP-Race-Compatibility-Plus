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
    public sealed class PendingShelterJoin : IExposable
    {
        public Quest quest;
        public int partIndex;
        public Map map;
        public void ExposeData(){Scribe_References.Look(ref quest,"quest");Scribe_Values.Look(ref partIndex,"partIndex");Scribe_References.Look(ref map,"map");}
        public QuestPart Part
        {
            get
            {
                // MP filters the public list by the current async map; saved indices refer to the full list.
                var parts=quest==null?null:(List<QuestPart>)AccessTools.Field(typeof(Quest),"parts").GetValue(quest);
                return parts!=null&&partIndex>=0&&partIndex<parts.Count?parts[partIndex]:null;
            }
        }
    }
    public sealed class JoinDecisions : GameComponent
    {
        List<PendingWanderer> pending=new List<PendingWanderer>();
        List<PendingShelterJoin> shelterJoins=new List<PendingShelterJoin>();
        readonly Dictionary<Window,QuestPart> shelterWindows=new Dictionary<Window,QuestPart>();
        static Type windowType;
        static Type shelterWindowType;
        static Type icyType;
        public JoinDecisions(Game game){}
        static JoinDecisions Instance=>Current.Game.GetComponent<JoinDecisions>();
        internal static void Apply(Harmony harmony)
        {
            windowType=AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_NivarianWandererDialog");
            MP.RegisterSyncMethod(typeof(JoinDecisions),nameof(Decide));
            var worker=AccessTools.TypeByName("Nivarian_Race.Code.Incidents.IncidentWorker_NivarianWanderer");
            harmony.Patch(AccessTools.Method(worker,"TryExecuteWorker"),postfix:new HarmonyMethod(typeof(JoinDecisions),nameof(IncidentPostfix)));
            icyType=AccessTools.TypeByName("Nivarian.QuestPart_IcyCheck");
            shelterWindowType=AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_NivarianColdShelterJoinDialog");
            MP.RegisterSyncMethod(typeof(JoinDecisions),nameof(DecideShelter));
            harmony.Patch(AccessTools.Method(icyType,"SucceedQuest"),postfix:new HarmonyMethod(typeof(JoinDecisions),nameof(ShelterSucceeded)));
            harmony.Patch(AccessTools.Method(icyType,"ApplyJoinChoices"),prefix:new HarmonyMethod(typeof(JoinDecisions),nameof(ChoicesPrefix)));
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pending,"meowPendingWanderers",LookMode.Deep);
            if(pending==null)pending=new List<PendingWanderer>();
            Scribe_Collections.Look(ref shelterJoins,"meowPendingShelterJoins",LookMode.Deep);
            if(shelterJoins==null)shelterJoins=new List<PendingShelterJoin>();
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
            AccessTools.Field(windowType,"acceptAction").SetValue(window,new Action(()=>Decide(item.map,id,0)));
            AccessTools.Field(windowType,"refuseAction").SetValue(window,new Action(()=>Decide(item.map,id,1)));
            AccessTools.Field(windowType,"laterAction").SetValue(window,new Action(()=>Decide(item.map,id,2)));
        }
        public override void GameComponentUpdate()
        {
            if(!MP.IsInMultiplayer||windowType==null)return;
            UpdateShelterWindows();
            foreach(var item in pending)
            {
                if(item.pawn==null||item.map==null||item.map.ParentFaction!=MP.RealPlayerFaction||Find.WindowStack.Windows.Any(w=>w.GetType()==windowType&&ReferenceEquals(AccessTools.Field(windowType,"pawn").GetValue(w),item.pawn)))continue;
                int id=item.pawn.thingIDNumber;
                var window=(Window)Activator.CreateInstance(windowType,"Nivarian.Wanderer.DialogText".Translate().ToString(),item.pawn,
                    new Action(()=>Decide(item.map,id,0)),new Action(()=>Decide(item.map,id,1)),new Action(()=>Decide(item.map,id,2)));
                Find.WindowStack.Add(window);
            }
        }
        static void Decide(Map map,int pawnId,int choice)
        {
            var item=Instance.pending.FirstOrDefault(p=>p.pawn?.thingIDNumber==pawnId);
            if(item==null||choice<0||choice>2||map==null||item.map!=map||!Find.Maps.Contains(map)||map.ParentFaction!=Faction.OfPlayer)return;
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
        static void ShelterSucceeded(QuestPart __instance)
        {
            if(!MP.IsInMultiplayer)return;
            var map=(Map)AccessTools.Field(icyType,"map").GetValue(__instance);
            if(map==null)return;
            if(!Instance.shelterJoins.Any(p=>ReferenceEquals(p.Part,__instance)))
                Instance.shelterJoins.Add(new PendingShelterJoin{quest=__instance.quest,partIndex=__instance.Index,map=map});
            // Replace the transient original window with one rebuilt from shared pending state.
            CloseShelterWindows(__instance);
        }
        static bool ChoicesPrefix(QuestPart __instance,List<Pawn> acceptedPawns)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            var pendingJoin=Instance.shelterJoins.FirstOrDefault(p=>ReferenceEquals(p.Part,__instance));
            if(pendingJoin!=null)DecideShelter(pendingJoin.map,__instance,acceptedPawns);
            return false;
        }
        static void DecideShelter(Map map,QuestPart part,List<Pawn> accepted)
        {
            var item=Instance.shelterJoins.FirstOrDefault(p=>ReferenceEquals(p.Part,part));
            if(item==null||map==null||item.map!=map||!Find.Maps.Contains(map)||map.ParentFaction!=Faction.OfPlayer)return;
            var candidates=(List<Pawn>)AccessTools.Field(icyType,"pawns").GetValue(part);
            var selected=new HashSet<Pawn>(accepted??new List<Pawn>());
            var joining=candidates.Where(p=>p!=null&&!p.Dead&&!p.Destroyed&&selected.Contains(p)).ToList();
            // Consume before invoking the complete original callback so concurrent replies are harmless.
            Instance.shelterJoins.Remove(item);
            AccessTools.Method(icyType,"ApplyJoinChoices").Invoke(part,new object[]{joining});
            CloseShelterWindows(part);
        }
        void UpdateShelterWindows()
        {
            foreach(var window in shelterWindows.Keys.Where(w=>!Find.WindowStack.Windows.Contains(w)).ToList())shelterWindows.Remove(window);
            foreach(var item in shelterJoins)
            {
                var part=item.Part;
                if(part==null||item.map==null||!Find.Maps.Contains(item.map)||item.map.ParentFaction!=MP.RealPlayerFaction||shelterWindows.Values.Contains(part))continue;
                var pawns=((List<Pawn>)AccessTools.Field(icyType,"pawns").GetValue(part)).Where(p=>p!=null&&!p.Dead&&!p.Destroyed).ToList();
                var window=(Window)Activator.CreateInstance(shelterWindowType,pawns,
                    new Action<List<Pawn>>(accepted=>DecideShelter(item.map,part,accepted)),
                    new Action(()=>DecideShelter(item.map,part,new List<Pawn>())));
                shelterWindows.Add(window,part);
                Find.WindowStack.Add(window);
            }
        }
        static void CloseShelterWindows(QuestPart part)
        {
            foreach(var window in Find.WindowStack.Windows.Where(w=>w.GetType()==shelterWindowType).ToList())
            {
                var callback=AccessTools.Field(window.GetType(),"confirmAction").GetValue(window) as Delegate;
                if(ReferenceEquals(callback?.Target,part)||(Instance.shelterWindows.TryGetValue(window,out var owner)&&ReferenceEquals(owner,part)))
                {
                    Instance.shelterWindows.Remove(window);
                    window.Close();
                }
            }
        }
    }
}
