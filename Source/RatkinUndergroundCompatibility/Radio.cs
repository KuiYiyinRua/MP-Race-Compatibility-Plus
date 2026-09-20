using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RatkinUnderground;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Meow.RatkinUndergroundCompatibility
{
    [StaticConstructorOnStartup]
    public static class Radio
    {
        static readonly Harmony harmony=new Harmony("meow.ratkin.radio");
        static ISyncMethod action;
        static int constructing;
        static bool recording,presenting;
        public static object Get(object o,string f)=>AccessTools.Field(o.GetType(),f).GetValue(o);
        public static void Set(object o,string f,object value)=>AccessTools.Field(o.GetType(),f).SetValue(o,value);
        static HarmonyMethod H(string n)=>new HarmonyMethod(typeof(Radio),n);
        public static RadioState State(Thing radio)=>Current.Game.GetComponent<RadioStore>().Find(radio);
        static Radio()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("ratkin")) return;
            if(!MP.enabled)return;
            try{
                action=MP.RegisterSyncMethod(typeof(Radio),nameof(Act));
                MP.RegisterSyncMethod(typeof(Radio),nameof(Deliver));
                MP.RegisterSyncWorker<RadioState>((SyncWorker s,ref RadioState value)=>{Thing radio=value?.radio;s.Bind(ref radio);if(!s.isWriting)value=State(radio);});
                harmony.Patch(AccessTools.Constructor(typeof(Dialog_RKU_Radio),new[]{typeof(Thing)}),prefix:H(nameof(ConstructorPrefix)),postfix:H(nameof(ConstructorPostfix)),transpiler:H(nameof(ConstructorCode)),finalizer:H(nameof(ConstructorFinalizer)));
                harmony.Patch(AccessTools.Method(typeof(Comp_RKU_Radio),nameof(Comp_RKU_Radio.ClearMessageHistory)),prefix:H(nameof(ClearHistory)));
                harmony.Patch(AccessTools.Method(typeof(Comp_RKU_Radio),nameof(Comp_RKU_Radio.AddMessage)),prefix:H(nameof(HistoryWrite)));
                foreach(var method in new[]{"UpdateTradeStatus","UpdateScanStatus","UpdateRescueStatus"})harmony.Patch(AccessTools.Method(typeof(Dialog_RKU_Radio),method),prefix:H(nameof(NoUiMutation)));
                harmony.Patch(AccessTools.Method(typeof(Dialog_RKU_Radio),nameof(Dialog_RKU_Radio.DoWindowContents)),prefix:H(nameof(DrawPrefix)),transpiler:H(nameof(ButtonCode)));
                harmony.Patch(AccessTools.Method(typeof(Dialog_RKU_Radio),nameof(Dialog_RKU_Radio.Close)),prefix:H(nameof(ClosePrefix)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(RKU_MapParent),"scanMap"),postfix:H(nameof(ScannedSite)));
                harmony.Patch(AccessTools.DeclaredMethod(typeof(MapParent),"ExposeData"),postfix:H(nameof(SaveSite)));
                harmony.Patch(AccessTools.DeclaredMethod(typeof(RKU_DrillingCargoPodBullet),"ExposeData"),transpiler:H(nameof(PodOwnership)));
                foreach(var method in AccessTools.GetDeclaredMethods(typeof(Dialog_RKU_Radio)).Where(m=>m.Name=="AddMessage"))harmony.Patch(method,postfix:H(nameof(MessagePostfix)));
                Log.Message("[RatkinMP] Radio shared sessions active version=1.0.0");
            }catch(Exception e){Log.Error("[RatkinMP] REQUIRED TARGET FAILED radio "+e);}
        }
        static void ConstructorPrefix(out bool __state){__state=MP.IsInMultiplayer;if(__state){constructing++;Rand.PushState();}}
        static Exception ConstructorFinalizer(Exception __exception,bool __state){if(__state){constructing--;Rand.PopState();}return __exception;}
        static void ConstructorPostfix(Dialog_RKU_Radio __instance){if(MP.IsInMultiplayer&&MP.InInterface)action.DoSync(null,__instance.radio,-1);}
        static IEnumerable<CodeInstruction> ConstructorCode(IEnumerable<CodeInstruction> instructions)
        {
            foreach(var i in instructions){if(i.operand is MethodInfo m&&m.DeclaringType==typeof(LongEventHandler)&&m.Name=="QueueLongEvent")i.operand=AccessTools.Method(typeof(Radio),nameof(QueueStartup));yield return i;}
        }
        static void QueueStartup(Action work,string text,bool asynchronous,Action<Exception> handler,bool extra,bool force,Action finallyAction)
        {
            if(!MP.IsInMultiplayer)LongEventHandler.QueueLongEvent(work,text,asynchronous,handler,extra,force,finallyAction);
        }
        static bool ClearHistory()=>!MP.IsInMultiplayer||constructing==0;
        static bool HistoryWrite()=>!MP.IsInMultiplayer||recording;
        static bool NoUiMutation()=>!MP.IsInMultiplayer;
        static void MessagePostfix(Dialog_RKU_Radio __instance,string message)
        {
            if(!MP.IsInMultiplayer||MP.InInterface||constructing>0||presenting)return;
            recording=true;try{__instance.radio?.TryGetComp<Comp_RKU_Radio>()?.AddMessage(message);}finally{recording=false;}
        }
        static void DrawPrefix(Dialog_RKU_Radio __instance)
        {
            if(!MP.IsInMultiplayer)return;
            var state=Current.Game.GetComponent<RadioStore>().states.FirstOrDefault(s=>s.radio==__instance.radio);
            if(state==null)return;
            Set(__instance,"tradeReady",state.ready);Set(__instance,"isRoyalRadioMode",state.royal);
            __instance.dialogueEventDefNow=state.current;
            Set(__instance,"tradeInProgress",false);
        }
        static IEnumerable<CodeInstruction> ButtonCode(IEnumerable<CodeInstruction> instructions)
        {
            int index=0;
            foreach(var i in instructions){
                if(i.operand is MethodInfo m&&m.DeclaringType==typeof(Widgets)&&m.Name=="ButtonText"&&index<4){
                    yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Ldc_I4,index++);
                    i.operand=AccessTools.Method(typeof(Radio),nameof(Button));
                }
                yield return i;
            }
            if(index!=4)throw new Exception("Expected four radio action buttons; got "+index);
        }
        static bool Button(Rect rect,string label,bool drawBackground,bool mouseoverSound,bool active,TextAnchor? anchor,Dialog_RKU_Radio dialog,int index)
        {
            bool clicked=Widgets.ButtonText(rect,label,drawBackground,mouseoverSound,active,anchor);
            if(!MP.IsInMultiplayer)return clicked;
            if(clicked)action.DoSync(null,dialog.radio,index);
            return false;
        }
        static bool ClosePrefix(Dialog_RKU_Radio __instance)
        {
            if(!MP.IsInMultiplayer)return true;
            Set(__instance,"hasTraded",false);__instance.pendingCargo.Clear();return true;
        }
        public static void Act(Thing radio,int index)
        {
            if(radio==null||!radio.Spawned)return;
            var state=State(radio);var dialog=state.Dialog;var c=dialog.radioComponent;
            if(index==-1){
                if(RKU_Mod.Instance.settings.showOnlyCurrentDialogueMessages)radio.TryGetComp<Comp_RKU_Radio>().ClearMessageHistory();
                RKU_DialogueManager.TriggerDialogueEvents(dialog,c.isSearch&&Rand.Bool?"research":"startup");
            }
            else if(index==0&&c.ralationshipGrade>=1&&c.CanTradeNow){
                RKU_DialogueManager.TriggerDialogueEvents(dialog,"trade");
                if(!state.ready){c.StartTradeSignal();dialog.AddMessage("RKU_SignalReceived".Translate());}
                else{
                    if(state.stock.Count==0){foreach(var generator in state.TraderKind.stockGenerators)state.stock.AddRange(generator.GenerateThings(radio.Map.Tile));state.priceSeed=Rand.Int;}
                    var pawn=radio.Map.mapPawns.FreeColonists.Where(p=>!p.DeadOrDowned).OrderByDescending(p=>p.skills.GetSkill(SkillDefOf.Social).Level).ThenBy(p=>p.thingIDNumber).FirstOrDefault();
                    if(pawn!=null){
                        var sessionType=AccessTools.TypeByName("Multiplayer.Client.MpTradeSession");
                        var session=AccessTools.Method(sessionType,"TryCreate").Invoke(null,new object[]{state,pawn,false});
                        if(session!=null&&MP.IsExecutingSyncCommandIssuedBySelf)AccessTools.Method(sessionType,"OpenWindow").Invoke(session,new object[]{true});
                    }
                }
            }
            else if(index==1&&c.ralationshipGrade>=50&&c.canScan)new RadioActions(dialog).Scan();
            else if(index==2&&c.ralationshipGrade>=75&&c.canEmergency)new RadioActions(dialog).Emergency();
            else if(index==3&&c.canRescue)new RadioActions(dialog).Support();
            state.current=dialog.dialogueEventDefNow;state.royal=(bool)Get(dialog,"isRoyalRadioMode");
            foreach(var window in Find.WindowStack.Windows.OfType<Dialog_RKU_Radio>().Where(w=>w.radio==radio)){
                window.dialogueEventDefNow=state.current;Set(window,"isRoyalRadioMode",state.royal);
                string text=radio.TryGetComp<Comp_RKU_Radio>().MessageHistory.LastOrDefault();
                if(text!=null){presenting=true;try{window.AddMessage(text);}finally{presenting=false;}}
            }
        }
        static void ScannedSite(RKU_MapParent __instance,ref bool __result){if(MP.IsInMultiplayer&&Current.Game.GetComponent<RadioStore>().scanned.Contains(__instance))__result=true;}
        static void SaveSite(WorldObject __instance)
        {
            if(!(__instance is RKU_MapParent site))return;
            bool entered=(bool)Get(site,"isSpawn");
            Scribe_Values.Look(ref entered,"meow_entered");
            Set(site,"isSpawn",entered);
        }
        static IEnumerable<CodeInstruction> PodOwnership(IEnumerable<CodeInstruction> instructions)
        {
            int found=0;
            foreach(var i in instructions){
                if(i.operand is MethodInfo m&&m.DeclaringType==typeof(Scribe_References)&&m.IsGenericMethod&&m.GetGenericArguments()[0]==typeof(RKU_DrillingCargoPod)){
                    i.operand=AccessTools.Method(typeof(Radio),nameof(SavePod));found++;
                }
                yield return i;
            }
            if(found!=1)throw new Exception("Expected one cargo pod reference");
        }
        static void SavePod(ref RKU_DrillingCargoPod pod,string label,bool saveDestroyedThings)
        {
            if(MP.IsInMultiplayer)Scribe_Deep.Look(ref pod,"meow_cargoPod");
            else Scribe_References.Look(ref pod,label,saveDestroyedThings);
        }
        static void SiteOptions(RKU_MapParent __instance,Caravan caravan,ref IEnumerable<FloatMenuOption> __result)
        {
            if(!MP.IsInMultiplayer)return;
            __result=WrapOptions(__result,__instance,caravan);
        }
        static IEnumerable<FloatMenuOption> WrapOptions(IEnumerable<FloatMenuOption> options,RKU_MapParent site,Caravan caravan)
        {
            foreach(var option in options){if(option.Label=="进入地图")option.action=()=>EnterSite(site,caravan);yield return option;}
        }
        public static void EnterSite(RKU_MapParent site,Caravan caravan)
        {
            if(site==null||caravan==null||!site.Spawned||!caravan.Spawned||!(caravan is RKU_DrillingVehicleOnMap))return;
            if(caravan.Tile!=site.Tile){caravan.pather.StartPath(site.Tile,null,true);return;}
            var map=GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile,new IntVec3(250,1,250),site.def);
            map.fogGrid.Refog(CellRect.WholeMap(map));
            CaravanEnterMapUtility.Enter(caravan,map,CaravanEnterMode.Edge,CaravanDropInventoryMode.DoNotDrop,true);
            Set(site,"isSpawn",true);
        }
        public static void Deliver(Thing radio,IntVec3 target)
        {
            if(radio==null||!radio.Spawned||!target.InBounds(radio.Map)||!Utils.CanSpawnTunnelAt(target,radio.Map))return;
            var state=State(radio);if(state.pending.Count==0)return;
            var launch=Utils.FindLaunchSpot(radio.Map);if(launch==IntVec3.Invalid)return;
            var pod=(RKU_DrillingCargoPod)ThingMaker.MakeThing(ThingDef.Named("RKU_DrillingCargoPod"));
            foreach(var thing in state.pending.ToList())if(!thing.Destroyed)pod.AddCargo(thing);
            var bullet=(RKU_DrillingCargoPodBullet)ThingMaker.MakeThing(ThingDef.Named("RKU_DrillingCargoPodBullet"));
            bullet.rKU_DrillingCargoPod=pod;GenSpawn.Spawn(bullet,launch,radio.Map);bullet.Launch(null,target,target,ProjectileHitFlags.None);
            state.pending.Clear();state.deliveryNotified=false;
        }
        public static void TargetDelivery(Thing radio)
        {
            Find.Targeter.BeginTargeting(new TargetingParameters{canTargetLocations=true,canTargetBuildings=false,canTargetPawns=false,validator=t=>radio.Spawned&&t.Cell.InBounds(radio.Map)&&Utils.CanSpawnTunnelAt(t.Cell,radio.Map)},target=>Deliver(radio,target.Cell));
        }
    }
    public class RadioStore : GameComponent
    {
        public List<RadioState> states=new List<RadioState>();
        public List<WorldObject> scanned=new List<WorldObject>();
        public RadioStore(Game game){}
        public RadioState Find(Thing radio){var state=states.FirstOrDefault(s=>s.radio==radio);if(state==null){state=new RadioState{radio=radio,radioId=radio.thingIDNumber};states.Add(state);}return state;}
        public override void ExposeData(){Scribe_Collections.Look(ref states,"meow_radios",LookMode.Deep);Scribe_Collections.Look(ref scanned,"meow_scanned",LookMode.Reference);if(states==null)states=new List<RadioState>();if(scanned==null)scanned=new List<WorldObject>();}
        public override void GameComponentTick()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("ratkin")) return;
            if(!MP.IsInMultiplayer)return;
            var c=Current.Game.GetComponent<RKU_RadioGameComponent>();int tick=Verse.Find.TickManager.TicksGame;
            if(!c.canTrade&&tick-c.lastTradeTick>=c.tradeCooldownTicks){c.canTrade=true;c.isWaitingForTrade=false;}
            if(!c.canScan&&tick-c.lastScanTick>=c.scanCooldownTicks)c.canScan=true;
            if(!c.canEmergency&&tick-c.lastEmergencyTick>=c.emergencyCooldownTicks)c.canEmergency=true;
            if(!c.canRescue&&tick-c.lastRescueTick>=c.rescueCooldownTicks)c.canRescue=true;
            if(c.isWaitingForTrade&&tick-c.tradeStartTick>=c.currentTradeDelayTicks){c.isWaitingForTrade=false;foreach(var s in states)s.ready=true;}
            foreach(var s in states){
                if(s.pending.Count>0&&!s.deliveryNotified){
                    s.deliveryNotified=true;
                    var letter=(DeliveryLetter)LetterMaker.MakeLetter("RKU_GoodsArrived".Translate(),"RKU_OrderedGoodsArrived".Translate(),DeliveryLetter.Def,s.radio);
                    letter.radio=s.radio;Verse.Find.LetterStack.ReceiveLetter(letter);
                }
            }
        }
    }
    public class RadioState : IExposable,ILoadReferenceable,ITrader
    {
        public Thing radio;public List<Thing> stock=new List<Thing>(),pending=new List<Thing>();
        public bool ready,royal,deliveryNotified;public int priceSeed,radioId;public RKU_DialogueEventDef current;
        Dialog_RKU_Radio dialog;
        public Dialog_RKU_Radio Dialog{get{if(dialog==null){dialog=new Dialog_RKU_Radio(radio);dialog.dialogueEventDefNow=current;Radio.Set(dialog,"isRoyalRadioMode",royal);}return dialog;}}
        public string GetUniqueLoadID()=>"MeowRadio_"+radioId;
        public void ExposeData(){Scribe_Values.Look(ref radioId,"radioId");Scribe_References.Look(ref radio,"radio");Scribe_Collections.Look(ref stock,"stock",LookMode.Deep);Scribe_Collections.Look(ref pending,"pending",LookMode.Deep);Scribe_Values.Look(ref ready,"ready");Scribe_Values.Look(ref royal,"royal");Scribe_Values.Look(ref deliveryNotified,"deliveryNotified");Scribe_Values.Look(ref priceSeed,"priceSeed");Scribe_Defs.Look(ref current,"dialogue");}
        public TraderKindDef TraderKind=>DefDatabase<TraderKindDef>.GetNamed("RKU_RadioShop");
        public IEnumerable<Thing> Goods=>stock.Where(t=>!t.Destroyed);
        public int RandomPriceFactorSeed=>priceSeed;
        public string TraderName=>"RKU_TraderName".Translate();
        public bool CanTradeNow=>radio!=null&&radio.Spawned&&ready;
        public float TradePriceImprovementOffsetForPlayer=>0;
        public Faction Faction=>Find.FactionManager.FirstFactionOfDef(FactionDef.Named("RKU_Faction"));
        public TradeCurrency TradeCurrency=>TraderKind.tradeCurrency;
        public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn negotiator)=>Dialog.ColonyThingsWillingToBuy(negotiator).Distinct();
        public void GiveSoldThingToTrader(Thing thing,int count,Pawn negotiator){if(thing==null||thing.Destroyed||count<=0)return;thing.SplitOff(count).Destroy();Used();}
        public void GiveSoldThingToPlayer(Thing thing,int count,Pawn negotiator){if(thing==null||thing.Destroyed||count<=0)return;pending.Add(thing.SplitOff(count));stock.RemoveAll(t=>t.Destroyed||pending.Contains(t));Used();}
        void Used(){var c=Current.Game.GetComponent<RKU_RadioGameComponent>();c.canTrade=false;c.lastTradeTick=Find.TickManager.TicksGame;ready=false;}
    }
    public class DeliveryLetter : ChoiceLetter
    {
        public Thing radio;
        public static LetterDef Def=>DefDatabase<LetterDef>.GetNamed("Meow_RadioDelivery");
        public override IEnumerable<DiaOption> Choices{
            get{
                yield return new DiaOption("RKU_GoodsArrived".Translate()){action=()=>{if(radio!=null&&radio.Spawned){CameraJumper.TryJump(radio);Radio.TargetDelivery(radio);}},resolveTree=true};
                yield return Option_Close;
            }
        }
        public override void ExposeData(){base.ExposeData();Scribe_References.Look(ref radio,"radio");}
    }
}
