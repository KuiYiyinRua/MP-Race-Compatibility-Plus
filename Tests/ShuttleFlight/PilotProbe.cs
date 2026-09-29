using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Persistent;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;
using UnityEngine;
public static class PilotProbe
{
 public static bool Done{get;private set;}
 static Faction owner;static Building_GravEngine engine;static CompPilotConsole console;static Pawn pilot;static Map destination;static ISyncMethod setup,check;static int phase;static bool requested,ready,checking,placing;static float deadline,nextTrace;
 static void Require(bool ok,string why){if(!ok)throw new Exception("pilot: "+why);}
 public static void Install(){var h=new Harmony("meow.odyssey.pilot.trace");h.Patch(AccessTools.Method(typeof(RitualBehaviorWorker_GravshipLaunch),"TryExecuteOn"),prefix:new HarmonyMethod(typeof(PilotProbe),nameof(TraceBegin)));h.Patch(AccessTools.Method(typeof(RitualOutcomeEffectWorker_GravshipLaunch),"Apply"),prefix:new HarmonyMethod(typeof(PilotProbe),nameof(TraceOutcome)));h.Patch(AccessTools.Method(typeof(RitualBehaviorWorker),"CanStartRitualNow"),postfix:new HarmonyMethod(typeof(PilotProbe),nameof(TraceCanStart)));setup=MP.RegisterSyncMethod(typeof(PilotProbe),nameof(Setup));check=MP.RegisterSyncMethod(typeof(PilotProbe),nameof(Check));}
 static void TraceBegin(RitualRoleAssignments assignments){Log.Message("ODYSSEY_TRACE RITUAL_BEGIN faction="+Faction.OfPlayer.loadID+" pilot="+assignments.FirstAssignedPawn("pilot")?.thingIDNumber+" participants="+string.Join(";",assignments.Participants.Select(p=>p.thingIDNumber+":"+p.Faction?.loadID)));}
 static void TraceOutcome(float progress,LordJob_Ritual jobRitual){Log.Message("ODYSSEY_TRACE RITUAL_OUTCOME progress="+progress+" faction="+Faction.OfPlayer.loadID+" pilot="+jobRitual.PawnWithRole("pilot")?.thingIDNumber); }
 static void TraceCanStart(RitualBehaviorWorker __instance,string __result){if(__instance is RitualBehaviorWorker_GravshipLaunch&&__result!=null)Log.Message("ODYSSEY_TRACE RITUAL_REJECT "+__result); }
 static Building Spawn(string name,IntVec3 cell,Map map){var b=(Building)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));b.SetFaction(owner);GenSpawn.Spawn(b,cell,map);return b;}
 public static void Setup(){
  var maps=Find.Maps.OrderBy(m=>m.uniqueID).ToArray();var map=maps[0];destination=maps[1];owner=destination.ParentFaction;
  var cell=GenRadial.RadialCellsAround(map.Center,60,true).First(c=>CellRect.CenteredOn(c,7).Cells.All(p=>p.InBounds(map)&&p.Standable(map)&&p.GetThingList(map).All(t=>t is Plant)));
  foreach(var c in CellRect.CenteredOn(cell,5).Cells){map.terrainGrid.SetFoundation(c,DefDatabase<TerrainDef>.GetNamed("Substructure"));map.fogGrid.Unfog(c);}
  engine=(Building_GravEngine)Spawn("GravEngine",cell,map);console=Spawn("PilotConsole",cell+new IntVec3(0,0,3),map).GetComp<CompPilotConsole>();
  Spawn("ChemfuelTank",cell+new IntVec3(4,0,3),map).GetComp<CompRefuelable>().Refuel(250);
  Spawn("SmallThruster",cell+new IntVec3(3,0,-5),map);engine.ForceSubstructureDirty();
  pilot=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,owner,forceGenerateNewPawn:true));GenSpawn.Spawn(pilot,console.parent.InteractionCell,map);
  Require(console.CanUseNow(),"console unavailable: "+console.CanUseNow().Reason);Require(!pilot.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled,"pilot incapable");ready=true;
 }
 public static void Check(){Require(engine.Spawned&&engine.Map==destination,"engine destination");Require(pilot.Spawned&&pilot.Map==destination,"pilot destination");Require(Find.Maps.Count==2,"foreign base lost");Require(engine.Faction==pilot.Faction,"ownership");Log.Message("ODYSSEY_PILOT COMPLETE map="+engine.Map.uniqueID+" engine="+engine.thingIDNumber+" pawn="+pilot.thingIDNumber+" fuel="+engine.TotalFuel);Done=true;if(!GenCommandLine.CommandLineArgPassed("oppilotafter"))OdysseyProbe.Complete();}
 public static void Update(){
  if(!GenCommandLine.CommandLineArgPassed("opclient"))return;
  if(!requested){requested=setup.DoSync(null);deadline=Time.realtimeSinceStartup+240;return;}Require(Time.realtimeSinceStartup<deadline,"timeout phase="+phase);if(!ready)return;
  if(MP.RealPlayerFaction!=owner){Multiplayer.Client.Multiplayer.Client.Send(new ClientSetFactionPacket(Multiplayer.Client.Multiplayer.session.playerId,owner.loadID));return;}
  if(Time.realtimeSinceStartup>=nextTrace){nextTrace=Time.realtimeSinceStartup+10;Log.Message("ODYSSEY_TRACE PILOT phase="+phase+" faction="+MP.RealPlayerFaction.loadID+" pawnLord="+pilot.GetLord()?.LordJob?.GetType().Name+" boarding="+engine.pawnsToBoard?.Count+" leaving="+engine.pawnsToLeave?.Count+" pawnJob="+pilot.CurJobDef+" spawned="+engine.Spawned+" ritual="+(engine.Map?.MpComp().sessionManager.GetFirstOfType<RitualSession>()!=null)+" windows="+string.Join(";",Find.WindowStack.Windows.Select(w=>w.GetType().Name))+" tilePicker="+Find.TilePicker.Active);}
  switch(phase){
   case 0:Current.Game.CurrentMap=engine.Map;var option=console.CompFloatMenuOptions(pilot).Single(o=>o.Label=="PilotGravship".Translate());Require(option.action!=null,"pilot menu disabled");option.action();phase++;break;
   case 1:var session=engine.Map.MpComp().sessionManager.GetFirstOfType<RitualSession>();if(session!=null){Require(session.data.isGravshipRitual,"wrong ritual");Log.Message("ODYSSEY_TRACE ASSIGNMENTS chosen="+pilot.thingIDNumber+" actual="+session.data.assignments.FirstAssignedPawn("pilot")?.thingIDNumber+" candidates="+string.Join(";",session.data.assignments.AllCandidatePawns.Select(p=>p.thingIDNumber+":"+p.Faction?.loadID)));Require(session.data.assignments.FirstAssignedPawn("pilot")==pilot,"native dialog did not assign chosen pilot");Require(session.data.assignments.Participants.Contains(pilot),"pilot absent from native participants");Log.Message("ODYSSEY_TRACE RITUAL_START pilot="+session.data.assignments.FirstAssignedPawn("pilot")?.thingIDNumber+" participants="+string.Join(";",session.data.assignments.Participants.Select(p=>p.thingIDNumber+":"+p.Faction?.loadID)));session.Start();phase++;}break;
   case 2:var box=Find.WindowStack.WindowOfType<Dialog_MessageBox>();if(box!=null){var action=(Action)AccessTools.Field(typeof(Dialog_MessageBox),"buttonAAction").GetValue(box);Require(action!=null,"prelaunch action absent");action();phase++;}break;
   case 3:if(Find.TilePicker.Active){var valid=(Func<PlanetTile,bool>)AccessTools.Field(typeof(TilePicker),"validator").GetValue(Find.TilePicker);Require(valid(destination.Tile),"target tile invalid");var chosen=(Action<PlanetTile>)AccessTools.Field(typeof(TilePicker),"tileChosen").GetValue(Find.TilePicker);chosen(destination.Tile);phase++;}break;
   case 4:var confirm=Find.WindowStack.WindowOfType<Dialog_MessageBox>();if(confirm!=null){((Action)AccessTools.Field(typeof(Dialog_MessageBox),"buttonAAction").GetValue(confirm))?.Invoke();confirm.Close(false);}var marker=(GravshipLandingMarker)AccessTools.Field(typeof(WorldComponent_GravshipController),"landingMarker").GetValue(Find.GravshipController);if(marker!=null){Current.Game.CurrentMap=destination;if(!marker.Spawned&&!placing){var designator=(Designator_MoveGravship)AccessTools.Method(typeof(WorldComponent_GravshipController),"MoveDesignator").Invoke(Find.GravshipController,null);Find.DesignatorManager.Select(designator);var cell=GenRadial.RadialCellsAround(destination.Center,30,true).First(c=>designator.CanDesignateCell(c).Accepted);designator.DesignateSingleCell(cell);placing=true;}else if(marker.Spawned){Find.DesignatorManager.Deselect();marker.BeginLanding(Find.GravshipController);phase++;}}break;
   case 5:if(engine.Spawned&&engine.Map==destination&&Current.Game.Gravship==null&&!checking)checking=check.DoSync(null);break;
  }
 }
}
