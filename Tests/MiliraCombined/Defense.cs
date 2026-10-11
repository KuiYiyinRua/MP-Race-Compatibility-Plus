using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using RimWorld;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class DefenseProbe
{
    static readonly bool client=GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer=>client?"client":"host";
    static ISyncMethod prepare;
    static ThingWithComps thing;
    static ThingComp comp;
    static Thing shardFixture,plane;
    static float fuelBefore;
    static Pawn pawn,target;
    static HediffComp archer;
    static Map source,destination;
    static Gizmo gizmo;
    static Window window;
    static int round,start,effects,signals,questsBefore,lettersBefore,crystalsBefore,shardsBefore;
    static float deadline;
    static bool prepared,requested,sent,observed,advancing,finished,failed,drawing,clicked;
    static string atExecution;
    static readonly string[] fields={"fireAtWill","currentTarget","forcedTarget","isForcetargetDowned","burstWarmupTicksLeft"};
    static object Get(object o,string n)=>AccessTools.Field(o.GetType(),n).GetValue(o);
    static void Set(object o,string n,object v)=>AccessTools.Field(o.GetType(),n).SetValue(o,v);
    static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    static string TurretState=>string.Join(";",fields.Select(n=>n+"="+Get(comp,n)));
    static List<FloatMenuOption> Options(FloatMenu menu)=>(List<FloatMenuOption>)Get(menu,"options");
    static int Crystals=>source.listerThings.ThingsOfDef(ThingDef.Named("Milira_SolarCrystal")).Sum(t=>t.stackCount);
    internal static void Init(Harmony h)
    {
        if(!GenCommandLine.CommandLineArgPassed("miliradefense"))return;
        prepare=MP.RegisterSyncMethod(typeof(DefenseProbe),nameof(Prepare));
        if(!MiliraActionsProbe.TraceDiagnostic)
            h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp"),"Tick"),prefix:new HarmonyMethod(typeof(DefenseProbe),nameof(MapTickBefore)),finalizer:new HarmonyMethod(typeof(DefenseProbe),nameof(MapTickAfter)));
        h.Patch(AccessTools.DeclaredMethod(typeof(Widgets),"ButtonInvisible",new[]{typeof(Rect),typeof(bool)}),prefix:new HarmonyMethod(typeof(DefenseProbe),nameof(Button)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("MP_MeowOnlineShop.MiliraAddonCompat.PLAMilira_GizmoCompat"),"ApplyTurret"),postfix:new HarmonyMethod(typeof(DefenseProbe),nameof(TurretApplied)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("PLAMilira.CompNaglfarSupportWorld"),"CallGameCondition"),postfix:new HarmonyMethod(typeof(DefenseProbe),nameof(SupportApplied)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("PLAMilira.Dialog_Secret_ArrowWindow"),"OnComboSuccess"),postfix:new HarmonyMethod(typeof(DefenseProbe),nameof(SecretApplied)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("PLAMilira.CompGodTowerDeactivationInteractor"),"OnInteracted"),postfix:new HarmonyMethod(typeof(DefenseProbe),nameof(TowerApplied)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("PLAMilira.Command_AddHediffs"),"ProcessInput"),postfix:new HarmonyMethod(typeof(DefenseProbe),nameof(FieldApplied)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("PLAMilira.CompAntiHarrierLauncher"),"TryLaunchInterception"),postfix:new HarmonyMethod(typeof(DefenseProbe),nameof(InterceptionApplied)));
        h.Patch(AccessTools.DeclaredMethod(typeof(SignalManager),"SendSignal",new[]{typeof(Signal)}),postfix:new HarmonyMethod(typeof(DefenseProbe),nameof(SignalSent)));
    }
    static void MapTickBefore(object __instance,out string __state)
    {
        __state=null;if(!prepared||round>=9)return;
        var map=(Map)Get(__instance,"map");var tick=(int)Get(__instance,"mapTicks");
        if(TickPatch.Timer-start>32 && tick%32!=0)return;
        __state="round="+round+";map="+map.uniqueID+";input="+tick+";rand="+Get(__instance,"randState");
    }
    static void MapTickAfter(object __instance,string __state)
    {
        if(__state==null)return;
        var map=(Map)Get(__instance,"map");var state=comp!=null&&comp.parent.Map==map?";"+TurretState+";idle="+Get(comp,"ticksUntilIdleTurn")+";warming="+Get(comp,"IsWarmingup"):"";
        File.AppendAllText(Path.Combine(root,Peer+".defense-map-rng.log"),__state+";output="+Get(__instance,"mapTicks")+";randOut="+Get(__instance,"randState")+state+Environment.NewLine);
    }
    static bool Button(Rect __0,ref bool __result)
    {
        if(!drawing||Event.current.type!=EventType.Repaint)return true;
        __result=!clicked&&__0.width==24&&__0.height==24&&Math.Abs(__0.y-(round%3==0?0:24))<0.1f;
        if(__result)clicked=true;return false;
    }
    static void TurretApplied(ThingComp comp)
    {
        if(comp!=DefenseProbe.comp||!prepared||MP.InInterface)return;
        if(round%3==0)Check((bool)Get(comp,"fireAtWill"),"turret fire toggle absent");
        if(round%3==1)Check(((LocalTargetInfo)Get(comp,"forcedTarget")).Thing==target&&((LocalTargetInfo)Get(comp,"currentTarget")).Thing==target&&(bool)Get(comp,"isForcetargetDowned"),"turret native Thing/downed target absent");
        if(round%3==2)Check(!((LocalTargetInfo)Get(comp,"currentTarget")).IsValid&&!((LocalTargetInfo)Get(comp,"forcedTarget")).IsValid&&!(bool)Get(comp,"isForcetargetDowned")&&(int)Get(comp,"burstWarmupTicksLeft")== (int)AccessTools.Property(comp.GetType(),"WarmupTick").GetValue(comp),"turret native reset incomplete");
        effects++;atExecution=TurretState;
    }
    static void SupportApplied(ThingComp __instance,GameConditionDef conditionDef,int conditionDuration,Map map)
    {
        if(__instance!=comp||!prepared||MP.InInterface)return;
        effects++;Check(map==destination,"support wrong destination map");
        Check((int)Get(comp,"supportCooldownticks")== (int)Get(comp.props,"cooldownTicks"),"support cooldown absent");
        var condition=map.gameConditionManager.ActiveConditions.Single(c=>c.def==conditionDef);
        atExecution=thing.ThingID+";source="+source.uniqueID+";target="+destination.uniqueID+";condition="+conditionDef.defName+";duration="+conditionDuration+";cooldown="+Get(comp,"supportCooldownticks")+";conditionID="+condition.uniqueID;
    }
    static void SignalSent(Signal signal)
    {
        if(prepared&&round>=15&&round<18&&thing!=null&&Get(thing,"quest") is Quest q&&signal.tag=="Quest"+q.id+".SecretSuccess")signals++;
    }
    static void SecretApplied(object __instance)
    {
        if(!prepared||round<15||round>=18||Get(__instance,"thing")!=thing||MP.InInterface)return;
        effects++;Check(thing.Destroyed&&signals==1,"secret native signal/consumption missing");
        Check(Find.QuestManager.QuestsListForReading.Count==questsBefore+1,"secret quest reward count");
        Check(Find.LetterStack.LettersListForReading.Count>lettersBefore,"secret letter reward missing");
        var newest=Find.QuestManager.QuestsListForReading.Last();
        atExecution=thing.ThingID+";consumed=True;signals="+signals+";questDelta=1;questID="+newest.id+";letterDelta="+(Find.LetterStack.LettersListForReading.Count-lettersBefore)+";letters="+string.Join("|",Find.LetterStack.LettersListForReading.Skip(lettersBefore).Select(l=>l.Label.ToString()));
    }
    static void TowerApplied(ThingComp __instance)
    {
        if(!prepared||__instance!=comp)return;effects++;Check(thing.Destroyed&&Crystals==crystalsBefore+50&&source.listerThings.ThingsOfDef(ThingDefOf.Shard).Sum(t=>t.stackCount)==shardsBefore-2&&pawn.carryTracker.CarriedThing==null,"god tower native destruction/reward/shard consumption");
        atExecution=thing.ThingID+";destroyed=True;crystalDelta=50;shardsConsumed=True";
    }
    static void FieldApplied(object __instance)
    {
        if(!prepared||round<21||Get(__instance,"parentComp")!=comp||MP.InInterface)return;
        effects++;var def=(HediffDef)Get(comp.props,"hediffToAdd");
        Check((int)Get(comp,"cooldownTicks")== (int)Get(comp.props,"cooldownTicksTotal"),"field cooldown not set");
        Check(pawn.health.hediffSet.HasHediff(def),"field hediff absent");
        atExecution=thing.ThingID+";hediff="+def.defName+";cooldown="+Get(comp,"cooldownTicks")+";pawns="+string.Join(",",source.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer).OrderBy(p=>p.thingIDNumber).Select(p=>p.ThingID+":"+p.health.hediffSet.GetFirstHediffOfDef(def).Severity));
    }
    static void InterceptionApplied(ThingComp __instance)
    {
        if(!prepared||round<24||__instance!=comp||MP.InInterface)return;
        var cost=(float)Get(comp.props,"fuelCostPerShot");var fuel=thing.GetComp<CompRefuelable>().Fuel;
        Check(Math.Abs(fuel-(fuelBefore-cost))<0.001f && (int)Get(comp,"cooldownRemaining")== (int)Get(comp.props,"cooldownTicks"),"native interception cost/cooldown missing");
        var projectile=source.listerThings.ThingsOfDef((ThingDef)Get(comp.props,"projectileDef")).Single(t=>((LocalTargetInfo)Get(t,"intendedTarget")).Thing==plane);
        atExecution=thing.ThingID+";source="+source.uniqueID+";plane="+plane.ThingID+";projectile="+projectile.ThingID+";fuel="+fuel+";cost="+cost+";cooldown="+Get(comp,"cooldownRemaining");effects++;
    }
    static ThingWithComps Spawn(string name)
    {
        var result=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named(name));
        Check(CellFinder.TryFindRandomCellNear(source.Center,source,35,c=>GenAdj.OccupiedRect(c,Rot4.North,result.def.size).All(x=>x.InBounds(source)&&x.Standable(source)&&!source.thingGrid.ThingsListAtFast(x).Any(t=>t.def.category==ThingCategory.Building)),out var cell),"no valid native fixture footprint");
        GenSpawn.Spawn(result,cell,source);if(result.def.category==ThingCategory.Building)result.SetFaction(Faction.OfPlayer);return result;
    }
    public static void Prepare(int n)
    {
        prepared=false;
        if(thing!=null&&!thing.Destroyed)thing.Destroy(DestroyMode.Vanish);
        round=n;effects=signals=0;sent=observed=advancing=clicked=false;atExecution="";window=null;archer=null;
        var maps=Find.Maps.Where(m=>m.IsPlayerHome).OrderBy(m=>m.uniqueID).ToArray();Check(maps.Length==2,"defense probe needs two maps");
        source=maps[n%2];destination=maps[1-n%2];
        if(n<9)
        {
            thing=Spawn("PLAMilira_Naglfar_Left");
            comp=thing.AllComps.First(c=>c.GetType().FullName=="PLAMilira.CompPLAMilira_TurretGun_Building");
            Set(comp,"fireAtWill",false);
            target=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);GenSpawn.Spawn(target,CellFinder.RandomClosewalkCellNear(thing.Position,source,12),source);
            target.health.AddHediff(HediffDefOf.Anesthetic);Check(target.Downed,"native downed target fixture absent");
            if(n%3==2){Set(comp,"currentTarget",new LocalTargetInfo(target));Set(comp,"forcedTarget",new LocalTargetInfo(target));Set(comp,"isForcetargetDowned",true);Set(comp,"burstWarmupTicksLeft",37);}
        }
        else if(n<12){thing=Spawn(new[]{"PLAMilira_Naglfar_Left","PLAMilira_Naglfar_Middle","PLAMilira_Naglfar_Right"}[n-9]);comp=thing.AllComps.Single(c=>c.GetType().FullName=="PLAMilira.CompNaglfarSupportWorld");}
        else if(n<15)
        {
            pawn=PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_KnightIV"),Faction.OfPlayer);GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(source.Center,source,10),source);pawn.drafter.Drafted=true;
            var def=ThingDef.Named("PLAMilianFitting_Core_KnightIV_Archer");var data=Get(def,"milianComponent");var slot=((IEnumerable)Get(data,"possibleSlots")).Cast<object>().First();
            var mod=pawn.AllComps.Single(c=>c.GetType().FullName=="MilianModification.CompModification");((IDictionary)Get(mod,"componentSlotInRecipe")).Add(slot,def);
            AccessTools.Method(AccessTools.TypeByName("MilianModification.MilianModificationUtility"),"InstallComponent",new[]{typeof(Pawn),def.GetType()}).Invoke(null,new object[]{pawn,def});
            archer=pawn.health.hediffSet.hediffs.OfType<HediffWithComps>().SelectMany(h=>h.comps).Single(c=>c.GetType().FullName=="PLAMilianModification.HediffComp_Core_Archer");Check((bool)Get(archer,"canFireMissile"),"Archer initial state");
        }
        else if(n<18)
        {
            pawn=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(source.Center,source,10),source);
            thing=Spawn("PLAMilira_Apparel_SecretConsole");pawn.apparel.Wear((Apparel)thing);
            var quest=QuestUtility.GenerateQuestAndMakeAvailable(QuestScriptDefOf.OpportunitySite_AncientComplex,100);
            Set(thing,"quest",quest);questsBefore=Find.QuestManager.QuestsListForReading.Count;lettersBefore=Find.LetterStack.LettersListForReading.Count;
        }
        else if(n<21)
        {
            thing=Spawn("PLAMilira_God_Tower");pawn=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(thing.Position,source,7),source);
            var study=thing.TryGetComp<CompStudyUnlocks>();var count=((CompProperties_StudyUnlocks)study.props).studyNotes.Count;
            for(int i=0;i<count;i++)AccessTools.Method(typeof(CompStudyUnlocks),"RegisterStudyLevel").Invoke(study,new object[]{pawn,i});Check(study.Completed,"god tower study fixture incomplete");
            comp=thing.AllComps.Single(c=>c.GetType().FullName=="PLAMilira.CompGodTowerDeactivationInteractor");
            shardFixture=ThingMaker.MakeThing(ThingDefOf.Shard);shardFixture.stackCount=2;GenSpawn.Spawn(shardFixture,CellFinder.RandomClosewalkCellNear(pawn.Position,source,2),source);crystalsBefore=Crystals;shardsBefore=source.listerThings.ThingsOfDef(ThingDefOf.Shard).Sum(t=>t.stackCount);
        }
        else if(n<24)
        {
            var def=DefDatabase<ThingDef>.AllDefs.First(d=>d.comps?.Any(c=>c.compClass.FullName=="PLAMilira.CompPLAMilira_Field_Button")==true);
            thing=Spawn(def.defName);comp=thing.AllComps.Single(c=>c.GetType().FullName=="PLAMilira.CompPLAMilira_Field_Button");
            pawn=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(thing.Position,source,7),source);
        }
        else
        {
            var type=AccessTools.TypeByName("PLAMilira.CompAntiHarrierLauncher");
            var def=DefDatabase<ThingDef>.AllDefs.Where(d=>d.comps?.Any(c=>c.compClass==type)==true && typeof(Building).IsAssignableFrom(d.thingClass) && d.comps.Any(c=>c.compClass.FullName=="PLAMilira.CompAutoAntiHarrierDefenseSystem")).OrderBy(d=>d.defName).First();
            thing=Spawn(def.defName);comp=thing.AllComps.Single(c=>c.GetType()==type);Set(comp,"shouldAutoLaunch",false);
            var cost=(float)Get(comp.props,"fuelCostPerShot");thing.GetComp<CompRefuelable>().Refuel(cost*3);fuelBefore=thing.GetComp<CompRefuelable>().Fuel;
            plane=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("AirstrikePlaneSweep"));
            var origin=CellFinder.RandomClosewalkCellNear(thing.Position,source,2);GenSpawn.Spawn(plane,origin,source);
            var end=new IntVec3(origin.x<source.Size.x/2?source.Size.x-12:12,0,origin.z<source.Size.z/2?source.Size.z-12:12);
            AccessTools.Method(plane.GetType(),"Launch",new[]{typeof(Thing),typeof(Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(bool),typeof(Thing),typeof(ThingDef),typeof(bool)}).Invoke(plane,new object[]{thing,origin.ToVector3Shifted(),new LocalTargetInfo(end),new LocalTargetInfo(end),false,null,null,false});
            Check(!plane.Destroyed&&plane.Spawned,"native incoming plane unavailable");
        }
        prepared=true;start=TickPatch.Timer;deadline=Time.realtimeSinceStartup+180;Log.Message("DEFENSE PREPARE round="+round+" source="+source.uniqueID);
    }
    sealed class NativeWindow:Window
    {
        public NativeWindow(){forcePause=false;}
        public override Vector2 InitialSize=>new Vector2(350,150);
        public override void DoWindowContents(Rect rect)
        {
            if(Event.current.type!=EventType.Repaint||sent)return;
            drawing=true;try{var before=TurretState;gizmo.GizmoOnGUI(Vector2.zero,150,new GizmoRenderParms());Check(clicked,"turret native GUI button not reached");Check(before==TurretState&&effects==0,"turret GUI local mutation before sync");sent=true;Log.Message("DEFENSE GUI_DISPATCH round="+round+" rollback=True");}finally{drawing=false;}
        }
    }
    static void SimulationSpeed(TimeSpeed speed)
    {
        Mp.Client.SendCommand(CommandType.GlobalTimeSpeed,ScheduledCommand.Global,(byte)speed);
        foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
            Mp.Client.SendCommand(CommandType.MapTimeSpeed,map.uniqueID,(byte)(speed==TimeSpeed.Superfast&&MiliraActionsProbe.AsyncTime&&map!=Find.Maps.OrderBy(m=>m.uniqueID).First()?TimeSpeed.Fast:speed));
    }
    internal static bool Update()
    {
        if(finished)return true;if(failed)return false;
        try
        {
            Check(!Mp.session.desynced&&Mp.session.players.Count==2,"network health");
            if(!prepared){if(client&&!requested)requested=prepare.DoSync(null,0);return false;}
            Check(Time.realtimeSinceStartup<deadline,"defense timeout round="+round+" effects="+effects);
            if(client&&!sent&&TickPatch.Timer-start>(round<24?60:2))
            {
                Current.Game.CurrentMap=source;
                if(round<9)
                {
                    gizmo=comp.CompGetGizmosExtra().Single(g=>g.GetType().FullName=="PLAMilira.Gizmo_PLAMilira_TurretGunBuilding");
                    if(round%3==1)
                    {
                        var before=TurretState;AccessTools.Method(gizmo.GetType(),"ProcessTargetingInput").Invoke(gizmo,null);Find.Targeter.StopTargeting();AccessTools.Method(gizmo.GetType(),"ProcessTargetingInput").Invoke(gizmo,null);
                        ((Action<LocalTargetInfo>)Get(Find.Targeter,"action"))(target);Find.Targeter.StopTargeting();Check(before==TurretState&&effects==0,"turret target local mutation before sync");sent=true;
                    }
                    else if(window==null){window=new NativeWindow();Find.WindowStack.Add(window);}
                }
                else if(round<12)
                {
                    var command=comp.CompGetGizmosExtra().OfType<Command_Action>().Single();command.action();var menu=Find.WindowStack.Windows.OfType<FloatMenu>().Last();menu.Close(false);command.action();menu=Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                    var index=Find.Maps.Where(m=>!m.IsPocketMap).ToList().IndexOf(destination);Options(menu)[index].action();menu.Close(false);Check(effects==0&&(int)Get(comp,"supportCooldownticks")<=0,"support local mutation before sync");sent=true;
                }
                else if(round<15){archer.CompGetGizmos().OfType<Command_Toggle>().Single().toggleAction();Check((bool)Get(archer,"canFireMissile"),"Archer local mutation before sync");sent=true;}
                else if(round<18)
                {
                    ((Apparel)thing).GetWornGizmos().OfType<Command_Action>().First(g=>g.defaultLabel=="PLAMilira_PortableSecretConsole_Label".Translate().ToString()).action();
                    var secret=Find.WindowStack.Windows.Last(w=>w.GetType().FullName=="PLAMilira.Dialog_Secret_ArrowWindow");var input=AccessTools.Method(secret.GetType(),"RegisterInput");var code=((IEnumerable)Get(thing,"secretCode")).Cast<KeyCode>().ToArray();
                    foreach(var key in code)input.Invoke(secret,new object[]{key});Check(!thing.Destroyed&&effects==0&&signals==0&&Find.QuestManager.QuestsListForReading.Count==questsBefore,"secret local mutation before sync");sent=true;
                }
                else if(round<21){var before=pawn.CurJob;comp.CompFloatMenuOptions(pawn).Single(o=>!o.Disabled&&o.action!=null).action();Check(ReferenceEquals(before,pawn.CurJob)&&!thing.Destroyed&&effects==0,"tower local mutation before sync");sent=true;}
                else if(round<24){comp.CompGetGizmosExtra().OfType<Command>().Single(g=>g.GetType().FullName=="PLAMilira.Command_AddHediffs").ProcessInput(new Event());Check(effects==0&&(int)Get(comp,"cooldownTicks")==0,"field local mutation before sync");sent=true;}
                else{comp.CompGetGizmosExtra().OfType<Command_Action>().Single().ProcessInput(new Event());Check(effects==0&&thing.GetComp<CompRefuelable>().Fuel==fuelBefore&&(int)Get(comp,"cooldownRemaining")==0,"interception local mutation before sync");sent=true;}
                if(sent)Log.Message("DEFENSE DISPATCH round="+round+" rollback=True");
            }
            if(round>=12&&round<15){if((bool)Get(archer,"canFireMissile"))return false;atExecution=pawn.ThingID+";canFireMissile=False";}
            else if(effects!=1)return false;
            var receipt="round="+round+";effects="+(archer==null?effects:1)+";"+atExecution;
            var local=Path.Combine(root,Peer+".defense"+round);var other=Path.Combine(root,(client?"host":"client")+".defense"+round);
            if(!observed){observed=true;window?.Close(false);File.WriteAllText(local+".tmp",receipt);File.Move(local+".tmp",local);Log.Message("DEFENSE ACTION_PASS "+receipt);}
            if(!File.Exists(other))return false;Check(File.ReadAllText(local)==File.ReadAllText(other),"defense peer mismatch round="+round);
            if(round==(GenCommandLine.CommandLineArgPassed("miliraturretdiagnostic")?8:26)){if(client&&round==26)SimulationSpeed(TimeSpeed.Superfast);finished=true;return true;}if(client&&!advancing){advancing=true;if(round==23)SimulationSpeed(TimeSpeed.Paused);prepare.DoSync(null,round+1);}
        }
        catch(Exception e){failed=true;Log.Error("COMBINED FAILED DEFENSE "+e);}
        return false;
    }
}
