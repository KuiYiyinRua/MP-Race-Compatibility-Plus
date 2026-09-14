using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    public sealed class PendingShelterDecision : IExposable
    {
        public Quest Quest; public int PartIndex; public Map Map;
        public void ExposeData() { Scribe_References.Look(ref Quest,"quest"); Scribe_Values.Look(ref PartIndex,"part"); Scribe_References.Look(ref Map,"map"); }
    }
    public sealed class NivarianJoinContexts : GameComponent
    {
        List<PendingShelterDecision> pending = new List<PendingShelterDecision>();
        readonly Dictionary<Window,QuestPart> windows = new Dictionary<Window,QuestPart>();
        float nextUi;
        static Type oldType, icyType, windowType;
        static FieldInfo oldPending, wandererPawn, wandererMap, questParts, icyMap, icyPawns, confirmAction;
        static MethodInfo oldDecide, applyChoices;
        static ISyncMethod wandererCommand, shelterCommand;
        static NivarianJoinContexts Instance => Current.Game.GetComponent<NivarianJoinContexts>();
        public NivarianJoinContexts(Game game) { }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pending,"meowNivarianShelterDecisions",LookMode.Deep);
            if(pending==null)pending=new List<PendingShelterDecision>();
        }
        internal static void Apply(Harmony harmony)
        {
            oldType=AccessTools.TypeByName("Meow.RaceTrioCompatibility.JoinDecisions");
            // The narrow repair preserves the installed component and its existing pending wanderer saves.
            if(oldType!=null)
            {
                oldPending=AccessTools.Field(oldType,"pending");
                var itemType=oldPending.FieldType.GetGenericArguments()[0];
                wandererPawn=AccessTools.Field(itemType,"pawn");wandererMap=AccessTools.Field(itemType,"map");
                oldDecide=AccessTools.DeclaredMethod(oldType,"Decide",new[]{typeof(int),typeof(int)}) ?? throw new MissingMethodException("Legacy Nivarian wanderer decision");
                wandererCommand=MP.RegisterSyncMethod(typeof(NivarianJoinContexts),nameof(DecideWanderer),new[]{new SyncType(typeof(Map)){contextMap=true},new SyncType(typeof(int)),new SyncType(typeof(int))});
                harmony.Patch(oldDecide,prefix:Hook(nameof(WandererPrefix)));
            }
            icyType=AccessTools.TypeByName("Nivarian.QuestPart_IcyCheck") ?? throw new TypeLoadException("Nivarian icy quest");
            windowType=AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_NivarianColdShelterJoinDialog") ?? throw new TypeLoadException("Nivarian shelter dialogue");
            questParts=AccessTools.Field(typeof(Quest),"parts");icyMap=AccessTools.Field(icyType,"map");icyPawns=AccessTools.Field(icyType,"pawns");confirmAction=AccessTools.Field(windowType,"confirmAction");
            applyChoices=AccessTools.DeclaredMethod(icyType,"ApplyJoinChoices");
            shelterCommand=MP.RegisterSyncMethod(typeof(NivarianJoinContexts),nameof(DecideShelter),new[]{new SyncType(typeof(Map)){contextMap=true},new SyncType(typeof(QuestPart)),new SyncType(typeof(List<Pawn>))});
            harmony.Patch(AccessTools.DeclaredMethod(icyType,"SucceedQuest"),postfix:Hook(nameof(ShelterSucceeded)));
            harmony.Patch(applyChoices,prefix:Hook(nameof(ShelterPrefix)));
            Log.Message("[TaleNivarianCompat] wanderer map queue and saved shelter decisions installed.");
        }
        static HarmonyMethod Hook(string n)=>new HarmonyMethod(typeof(NivarianJoinContexts),n);
        static Map WandererMap(int id)
        {
            var component=Current.Game.components.FirstOrDefault(c=>c.GetType()==oldType);
            if(component==null)return null;
            foreach(var item in (IEnumerable)oldPending.GetValue(component))
                if((wandererPawn.GetValue(item) as Pawn)?.thingIDNumber==id)return (Map)wandererMap.GetValue(item);
            return null;
        }
        static bool WandererPrefix(int pawnId,int choice)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            var map=WandererMap(pawnId);if(map!=null)wandererCommand.DoSync(null,map,pawnId,choice);
            return false;
        }
        static void DecideWanderer(Map map,int id,int choice)
        {
            if(map==null||map!=WandererMap(id)||map.ParentFaction!=Faction.OfPlayer||choice<0||choice>2)return;
            oldDecide.Invoke(null,new object[]{id,choice});
        }
        static QuestPart Part(PendingShelterDecision item)
        {
            var parts=item.Quest==null?null:(List<QuestPart>)questParts.GetValue(item.Quest);
            return parts!=null&&item.PartIndex>=0&&item.PartIndex<parts.Count?parts[item.PartIndex]:null;
        }
        static void ShelterSucceeded(QuestPart __instance)
        {
            if(!MP.IsInMultiplayer)return;
            var map=(Map)icyMap.GetValue(__instance);if(map==null)return;
            var parts=(List<QuestPart>)questParts.GetValue(__instance.quest);
            if(!Instance.pending.Any(p=>ReferenceEquals(Part(p),__instance)))
                Instance.pending.Add(new PendingShelterDecision{Quest=__instance.quest,PartIndex=parts.IndexOf(__instance),Map=map});
            Instance.Close(__instance);
        }
        static bool ShelterPrefix(QuestPart __instance,List<Pawn> acceptedPawns)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            var item=Instance.pending.FirstOrDefault(p=>ReferenceEquals(Part(p),__instance));
            if(item!=null)shelterCommand.DoSync(null,item.Map,__instance,acceptedPawns);
            return false;
        }
        static void DecideShelter(Map map,QuestPart part,List<Pawn> accepted)
        {
            var item=Instance.pending.FirstOrDefault(p=>ReferenceEquals(Part(p),part));
            if(item==null||item.Map!=map||map==null||!Find.Maps.Contains(map)||map.ParentFaction!=Faction.OfPlayer)return;
            var chosen=new HashSet<Pawn>(accepted??new List<Pawn>());
            var joining=((List<Pawn>)icyPawns.GetValue(part)).Where(p=>p!=null&&!p.Dead&&!p.Destroyed&&chosen.Contains(p)).ToList();
            Instance.pending.Remove(item); // One outcome even when both peers answer the same window.
            applyChoices.Invoke(part,new object[]{joining});
            Instance.Close(part);
        }
        void Close(QuestPart part)
        {
            foreach(var window in Find.WindowStack.Windows.Where(w=>w.GetType()==windowType).ToArray())
                if(ReferenceEquals((confirmAction.GetValue(window) as Delegate)?.Target,part)||(windows.TryGetValue(window,out var owner)&&ReferenceEquals(owner,part)))
                { windows.Remove(window);window.Close(); }
        }
        public override void GameComponentUpdate()
        {
            // Empty fast path; only pending local dialogue reconstruction is polled, at most twice per second.
            if(icyType==null||pending.Count==0||!MP.IsInMultiplayer||!MP.InInterface||Time.realtimeSinceStartup<nextUi)return;
            nextUi=Time.realtimeSinceStartup+.5f;
            foreach(var window in windows.Keys.Where(w=>!Find.WindowStack.Windows.Contains(w)).ToArray())windows.Remove(window);
            foreach(var item in pending)
            {
                var part=Part(item);
                if(part==null||item.Map==null||!Find.Maps.Contains(item.Map)||item.Map.ParentFaction!=MP.RealPlayerFaction||windows.Values.Contains(part))continue;
                var pawns=((List<Pawn>)icyPawns.GetValue(part)).Where(p=>p!=null&&!p.Dead&&!p.Destroyed).ToList();
                var window=(Window)Activator.CreateInstance(windowType,pawns,new Action<List<Pawn>>(accepted=>shelterCommand.DoSync(null,item.Map,part,accepted)),new Action(()=>shelterCommand.DoSync(null,item.Map,part,new List<Pawn>())));
                windows.Add(window,part);Find.WindowStack.Add(window);
            }
        }
    }
}
