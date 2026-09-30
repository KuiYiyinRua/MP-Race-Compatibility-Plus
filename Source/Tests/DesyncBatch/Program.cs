// Offline behavioral fixtures only. Actual DLL signatures and IL are checked by
// DesyncBatchSurface; these tests do not launch or emulate the Unity game loop.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Meow.DesyncBatchCompatibility;

namespace Meow.DesyncBatchCompatibility
{
    internal static class Bootstrap
    {
        internal static Type Type(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        internal static System.Reflection.MethodInfo Method(Type type,string name,params Type[] args) =>
            AccessTools.DeclaredMethod(type,name,args) ?? throw new MissingMethodException(type.FullName,name);
        internal static System.Reflection.FieldInfo Field(Type type,string name,Type expected)
        {
            var field=AccessTools.Field(type,name);
            if(field==null||field.FieldType!=expected)throw new MissingFieldException(type.FullName,name);
            return field;
        }
    }
}

namespace Multiplayer.API { public static class MP { public static bool enabled, IsInMultiplayer, InInterface; } }
namespace Multiplayer.Client { public static class AsyncTimeComp { public static Map tickingMap, executingCmdMap; } }
namespace Multiplayer.Client.Patches { public static class UniqueIdsPatch { public static bool useLocalIdsOverride; } }
namespace UnityEngine
{
    public struct Color { public float r; public Color(float value) { r=value; } public static Color white => new Color(1); }
    public struct Vector3 { }
}
namespace Verse
{
    public class StaticConstructorOnStartup : Attribute { }
    public static class LongEventHandler { public static void ExecuteWhenFinished(Action a) { } }
    public static class ModsConfig { public static bool IsActive(string id) => false; }
    public static class Log { public static void Message(string s) { } public static void Error(string s) => throw new Exception(s); }
    public class CompProperties { }
    public struct IntVec3 { }
    public class FleckDef { }
    public class ThingComp { public ThingWithComps parent; public CompProperties props; }
    public class ThingDef { public string defName; public bool isSaveable; public Type thingClass; }
    public class Thing { public ThingDef def; public int thingIDNumber; }
    public class ThingWithComps : Thing { public List<ThingComp> AllComps = new List<ThingComp>(); }
    public class PawnKindDef { public List<ThingDef> apparelRequired; }
    public struct PawnGenerationRequest { public PawnKindDef KindDef; }
    public static class ThingMaker
    {
        public static int ids=100, local=-2;
        [MethodImpl(MethodImplOptions.NoInlining)] public static Thing MakeThing(ThingDef def, ThingDef stuff=null)
        {
            float draw=Rand.Value;
            var thing=(Thing)Activator.CreateInstance(def.thingClass); thing.def=def;
            thing.thingIDNumber=Multiplayer.Client.Patches.UniqueIdsPatch.useLocalIdsOverride?local--:ids++;
            return thing;
        }
    }
    public static class GenSpawn
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public static Thing Spawn(ThingDef def, Map map)
        { var t=ThingMaker.MakeThing(def); return Spawn(t,map); }
        [MethodImpl(MethodImplOptions.NoInlining)] public static Thing Spawn(Thing thing, Map map)
        { float draw=Rand.Value; return thing; }
    }
    public class Map { public Faction ParentFaction; }
    public class Pawn { public int thingIDNumber; public Map MapHeld; public Faction Faction; public GeneTracker genes; }
    public enum EndogeneCategory { Melanin, Other }
    public struct FloatRange { public float min,max; }
    public class GeneDef { public Color? skinColorBase; public string defName; public EndogeneCategory endogeneCategory; public float minMelanin,selectionWeight=1; }
    public static class DefDatabase<T> { public static List<T> AllDefs=new List<T>(); }
    public class GeneTracker
    {
        public GeneDef melanin; public int added;
        public GeneDef GetMelaninGene() => melanin;
        public void AddGene(GeneDef gene) { melanin=gene; added++; }
    }
    public class Hediff
    {
        public Pawn pawn; private List<Ability> abilities; public static int nextId;
        public List<Ability> AllAbilitiesForReading
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get
            {
                if (abilities==null) abilities=new List<Ability> { new Ability { id=++nextId } };
                return abilities;
            }
        }
    }
    public static class Gen { public static int HashCombineInt(int a,int b) => unchecked(a*397^b); }
    public static class Rand
    {
        public static uint State=10; public static Stack<uint> states=new Stack<uint>();
        public static void PushState() => states.Push(State);
        public static void PushState(int seed) { PushState(); State=unchecked((uint)seed); }
        public static void PopState() => State=states.Pop();
        public static float Value { get { State=unchecked(State*1664525+1013904223); return (State>>8)/(float)(1<<24); } }
    }
    public static class Find { public static FactionManager FactionManager=new FactionManager(); }
    public class FactionManager { public List<Faction> AllFactionsListForReading=new List<Faction>(); }
    public static class Scribe { public static bool Saving; public static Dictionary<string,int> data=new Dictionary<string,int>(); }
    public static class Scribe_Values
    {
        public static void Look(ref int value,string key,int fallback) { if(Scribe.Saving) Scribe.data[key]=value; else value=Scribe.data.TryGetValue(key,out int old)?old:fallback; }
    }
}
namespace Nivarian_Race.Code.NivarianThing.Base
{
    public class ProgrammableThing : Thing
    {
        public static bool Fail;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Tick()
        { float draw=Rand.Value; if(Fail)throw new Exception("trail fixture"); }
    }
}
namespace Nivarian_Race.Code.NivarianThing
{
    public class ProgrammableMoverThing : Base.ProgrammableThing { }
}
namespace Nivarian_Race.Code.Comps.BuildingComps
{
    public class CompNiraControlCenter
    {
        public List<int> pending=new List<int>(); public ThingDef trail;
        [MethodImpl(MethodImplOptions.NoInlining)] public void EnqueueControlTrail(Thing target=null,string trailerDefName=null,bool reversed=false)
        { int v=(int)(Rand.Value*40); pending.Add(v); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void TickPendingTrails()
        { foreach(var p in pending)SpawnControlTrail();pending.Clear(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void SpawnControlTrail(ThingDef def=null,Thing target=null,bool reversed=false)
        { float draw=Rand.Value; GenSpawn.Spawn(def??trail,null); }
    }
}
namespace Nivarian_Race.Code.Comps.ThingComps
{
    public class ThingComp_ExpCanister
    {
        public ThingDef trail;
        [MethodImpl(MethodImplOptions.NoInlining)] public void SpawnAbsorbTrail() { GenSpawn.Spawn(trail,null); }
    }
}
namespace Milira
{
    public static class Milira_MilianPawnGenerator_Patch
    {
        public static ThingDef Chosen; public static bool Fail; public static List<ThingDef> Used;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void Postfix(ref Pawn pawn, PawnGenerationRequest request)
        {
            var apparel=request.KindDef.apparelRequired; apparel.Add(Chosen); Used=apparel;
            if(Fail)throw new Exception("apparel fixture");
        }
    }
}
namespace RimWorld
{
    public class Plant : ThingWithComps { public float Growth; }
    public class Ability { public int id; }
    public class FactionDef { public bool isPlayer; public FloatRange melaninRange; }
    public class Faction
    {
        public int loadID; public FactionDef def=new FactionDef(); public static Faction current;
        public static Faction OfPlayer { [MethodImpl(MethodImplOptions.NoInlining)] get => current; }
        public bool IsPlayer { [MethodImpl(MethodImplOptions.NoInlining)] get => this==current; }
    }
    public static class PawnSkinColors
    {
        public static bool Fail;
        public static GeneDef RandomSkinColorGene(Pawn p) { float v=Rand.Value; if(Fail) throw new Exception("color fixture"); return new GeneDef { skinColorBase=new Color(v) }; }
    }
    public class Pawn_StoryTracker
    {
        private Pawn pawn; private Color? skinColorBase;
        public Pawn_StoryTracker(Pawn p, Color? color=null) { pawn=p; skinColorBase=color; }
        public Color SkinColorBase
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get
            {
                if(!skinColorBase.HasValue && pawn.genes!=null)
                {
                    var gene=pawn.genes.GetMelaninGene();
                    if(gene==null) { gene=PawnSkinColors.RandomSkinColorGene(pawn); pawn.genes.AddGene(gene); }
                    skinColorBase=gene.skinColorBase;
                }
                return skinColorBase.Value;
            }
        }
    }
}
namespace Nivarian_Race.Code.Comps.ThingComps
{
    public class CompProperties_PlantRenderer : CompProperties { public float matureGrowth=0.99f; }
    public class Comp_PlantRenderer : ThingComp { public bool IsMature; }
    public class Comp_FruitTree : ThingComp
    {
        public bool IsMature
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get
            {
                var plant=parent as Plant; if(plant==null)return false;
                var renderer=plant.AllComps.Find(c=>c is Comp_PlantRenderer) as Comp_PlantRenderer;
                return renderer?.IsMature ?? plant.Growth>=0.99f;
            }
        }
    }
}
namespace AriandelLibrary
{
    public class DamageWorker_AddInjury_NoDamageFactor
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public void ThrowDamageMote(Vector3 pos,Map map,string text,Color color) { float draw=Rand.Value; }
    }
    public class Visual_Lightning_Red
    {
        public static bool Visible, Fail;
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void ThrowLightningGlow(Vector3 pos,Map map,float size)
        { if(Visible) for(int i=0;i<5;i++){float value=Rand.Value;if(Fail)throw new Exception("visual fixture");} }
    }
    public class Visual_Lightning_Gold : Visual_Lightning_Red
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public override void ThrowLightningGlow(Vector3 pos,Map map,float size)
        { if(Visible)for(int i=0;i<5;i++){float value=Rand.Value;if(Fail)throw new Exception("visual fixture");} }
    }
    public class Visual_Lightning_Purple : Visual_Lightning_Red
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public override void ThrowLightningGlow(Vector3 pos,Map map,float size)
        { if(Visible)for(int i=0;i<5;i++){float value=Rand.Value;if(Fail)throw new Exception("visual fixture");} }
    }
}
class Program
{
    static int checks;
    static void Check(bool b,string name){if(!b)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);checks++;}
    static Faction Player(int id)=>new Faction{loadID=id,def=new FactionDef{isPlayer=true}};
    static void Main()
    {
        var h=new Harmony("desync-batch-fixtures");MP.IsInMultiplayer=true;
        var plant=new Plant{Growth=1};
        var renderer=new Nivarian_Race.Code.Comps.ThingComps.Comp_PlantRenderer{props=new Nivarian_Race.Code.Comps.ThingComps.CompProperties_PlantRenderer{matureGrowth=0.8f}};
        plant.AllComps.Add(renderer);
        var tree=new Nivarian_Race.Code.Comps.ThingComps.Comp_FruitTree{parent=plant};
        renderer.IsMature=false;bool cold=tree.IsMature;renderer.IsMature=true;
        Check(cold!=tree.IsMature,"old fruit behavior diverges with renderer cache");
        FruitTree.Apply(h);
        foreach(bool cached in new[]{true,false}) {renderer.IsMature=cached;Check(tree.IsMature,"mature fruit independent of renderer "+cached);}
        plant.Growth=0.79f;Check(!tree.IsMature,"custom threshold retained below boundary");
        plant.Growth=0.8f;Check(tree.IsMature,"custom threshold inclusive boundary");
        plant.AllComps.Clear();Check(!tree.IsMature,"no renderer uses original .99 threshold");
        plant.Growth=1;Check(tree.IsMature,"no renderer mature");
        MP.IsInMultiplayer=false;plant.AllComps.Add(renderer);renderer.IsMature=false;Check(!tree.IsMature,"single player renderer behavior retained");MP.IsInMultiplayer=true;

        var red=new AriandelLibrary.Visual_Lightning_Red();var gold=new AriandelLibrary.Visual_Lightning_Gold();var purple=new AriandelLibrary.Visual_Lightning_Purple();
        uint before=Rand.State;AriandelLibrary.Visual_Lightning_Red.Visible=true;red.ThrowLightningGlow(default,null,1);Check(Rand.State!=before,"old visible lightning advances simulation RNG");
        LightningVisuals.Apply(h);
        foreach(var effect in new AriandelLibrary.Visual_Lightning_Red[]{red,gold,purple})
        foreach(bool visible in new[]{false,true})
        {before=Rand.State;AriandelLibrary.Visual_Lightning_Red.Visible=visible;effect.ThrowLightningGlow(default,null,1);Check(Rand.State==before,"lightning state restored "+effect.GetType().Name+"/"+visible);}
        before=Rand.State;AriandelLibrary.Visual_Lightning_Red.Fail=true;
        bool threw=false;try{gold.ThrowLightningGlow(default,null,1);}catch(Exception){threw=true;}
        Check(threw&&Rand.State==before&&Rand.states.Count==0,"lightning finalizer restores state and preserves exception");
        AriandelLibrary.Visual_Lightning_Red.Fail=false;
        MP.IsInMultiplayer=false;red.ThrowLightningGlow(default,null,1);Check(Rand.State!=before,"single player visual RNG retained");MP.IsInMultiplayer=true;

        var p=new Pawn{thingIDNumber=7,genes=new GeneTracker()};
        var original=new Pawn_StoryTracker(p);before=Rand.State;var oldColor=original.SkinColorBase;
        Check(p.genes.added==1&&Rand.State!=before,"old skin read mutates genes and RNG");
        var light=new GeneDef{defName="light",minMelanin=0,skinColorBase=new Color(.1f)};
        var dark=new GeneDef{defName="dark",minMelanin=.5f,skinColorBase=new Color(.8f)};
        DefDatabase<GeneDef>.AllDefs.Add(dark);DefDatabase<GeneDef>.AllDefs.Add(light);
        LazySimulationCaches.Apply(h);p.genes=new GeneTracker();var tracker=new Pawn_StoryTracker(p);
        before=Rand.State;MP.InInterface=true;var ui=tracker.SkinColorBase;
        Rand.State=123;MP.InInterface=false;var sim=tracker.SkinColorBase;
        Check(ui.r==sim.r&&Rand.State==123&&p.genes.added==0,"fallback color stable across UI/simulation without gene writes");
        var stored=new Pawn_StoryTracker(p,new Color(.2f));Check(stored.SkinColorBase.r==.2f,"existing saved skin preserved");
        p.genes.melanin=new GeneDef{skinColorBase=new Color(.7f)};Check(tracker.SkinColorBase.r==.7f,"existing melanin respected");
        p.genes.melanin=null;p.Faction=Player(1);p.Faction.def.melaninRange=new FloatRange{min=.6f,max=.9f};
        Check(tracker.SkinColorBase.r==.8f,"fallback respects faction melanin range");p.Faction=null;
        light.selectionWeight=0;Check(tracker.SkinColorBase.r==.8f,"fallback respects skin selection weights");light.selectionWeight=1;
        before=Rand.State;float expected=tracker.SkinColorBase.r;
        System.Threading.Tasks.Parallel.For(0,1000,i=>{if(tracker.SkinColorBase.r!=expected)throw new Exception("parallel skin mismatch");});
        Check(Rand.State==before&&Rand.states.Count==0&&p.genes.added==0,"parallel color reads leave RNG and genes untouched");
        MP.IsInMultiplayer=false;var spColor=new Pawn_StoryTracker(p).SkinColorBase;Check(p.genes.added==1,"single player skin repair retained");MP.IsInMultiplayer=true;

        MP.InInterface=true;int ids=Hediff.nextId;var hediff=new Hediff();var empty=hediff.AllAbilitiesForReading;
        Check(empty.Count==0&&ids==Hediff.nextId,"UI read does not allocate ability IDs");
        empty.Add(new Ability());Check(hediff.AllAbilitiesForReading.Count==0,"UI placeholder cannot poison real cache");
        MP.InInterface=false;var cache=hediff.AllAbilitiesForReading;Check(cache.Count==1&&Hediff.nextId==ids+1,"simulation still creates real ability");
        MP.InInterface=true;Check(ReferenceEquals(cache,hediff.AllAbilitiesForReading),"UI returns existing simulation cache");
        MP.IsInMultiplayer=false;var sp=new Hediff().AllAbilitiesForReading;Check(Hediff.nextId==ids+2,"single player ability initialization retained");MP.IsInMultiplayer=true;

        var a=Player(4);var b=Player(9);var npc=new Faction{loadID=2};Find.FactionManager.AllFactionsListForReading=new List<Faction>{b,npc,a};
        hediff.pawn=new Pawn{Faction=npc,MapHeld=new Map{ParentFaction=b}};
        foreach(var view in new[]{a,b,npc}){Faction.current=view;Check(EliteState.TargetFaction(hediff)==b,"elite map owner independent of perspective "+view.loadID);Check(EliteState.IsAnyPlayer(a),"all actual player factions recognized");}
        hediff.pawn.MapHeld=null;Check(EliteState.TargetFaction(hediff)==a,"world fallback uses shared minimum ID");
        EliteState.tickingMap=typeof(Multiplayer.Client.AsyncTimeComp).GetField("tickingMap");
        EliteState.executingMap=typeof(Multiplayer.Client.AsyncTimeComp).GetField("executingCmdMap");
        Multiplayer.Client.AsyncTimeComp.tickingMap=new Map{ParentFaction=b};
        Check(EliteState.TargetFaction(hediff)==b,"unspawned raid pawn uses shared incident map");
        Multiplayer.Client.AsyncTimeComp.executingCmdMap=new Map{ParentFaction=a};
        Check(EliteState.TargetFaction(hediff)==a,"synchronized command map takes precedence");
        Multiplayer.Client.AsyncTimeComp.tickingMap=Multiplayer.Client.AsyncTimeComp.executingCmdMap=null;
        hediff.pawn.Faction=b;Check(EliteState.TargetFaction(hediff)==b,"unspawned player pawn ownership");
        MP.IsInMultiplayer=false;Faction.current=a;Check(EliteState.TargetFaction(hediff)==a&&!EliteState.IsAnyPlayer(b),"single player faction behavior retained");MP.IsInMultiplayer=true;
        int spawn=3,current=44,wound=59;
        Scribe.Saving=true;EliteState.Expose(ref spawn,ref current,ref wound);
        spawn=current=wound=0;Scribe.Saving=false;EliteState.Expose(ref spawn,ref current,ref wound);
        Check(spawn==3&&current==44&&wound==59,"all elite schedule fields roundtrip");
        Scribe.data.Clear();EliteState.Expose(ref spawn,ref current,ref wound);
        Check(spawn==0&&current==0&&wound==0,"old saves use constructor defaults");
        TransientChecks(h);
        Console.WriteLine("OFFLINE_FIXTURE_PASS checks="+checks);
    }

    static void TransientChecks(Harmony h)
    {
        MP.InInterface=false; MP.IsInMultiplayer=true;
        var trail=new ThingDef {defName="Nivarian_SimpleTrailer_Blue", thingClass=typeof(Nivarian_Race.Code.NivarianThing.ProgrammableMoverThing)};
        var mover=new ThingDef {defName="GameplayDrone",isSaveable=true,thingClass=trail.thingClass};
        DefDatabase<ThingDef>.AllDefs.Add(trail);
        uint rng=Rand.State; int next=ThingMaker.ids;
        GenSpawn.Spawn(trail,null);
        Check(Rand.State!=rng&&ThingMaker.ids!=next,"old unsaved trail advances shared RNG and IDs");
        NivarianTrailBoundary.Apply(h);
        rng=Rand.State;next=ThingMaker.ids;
        var host=(Nivarian_Race.Code.NivarianThing.ProgrammableMoverThing)GenSpawn.Spawn(trail,null);
        Check(host.thingIDNumber<0&&Rand.State==rng&&ThingMaker.ids==next,"visual spawn uses local IDs and restores nested RNG scopes");
        for(int i=0;i<30;i++)host.Tick(); // A cold peer has none of these unsaved things.
        Check(Rand.State==rng&&ThingMaker.ids==next,"host-only unsaved trail ticks leave cold peer baseline intact");
        var center=new Nivarian_Race.Code.Comps.BuildingComps.CompNiraControlCenter {trail=trail};
        center.EnqueueControlTrail(); center.EnqueueControlTrail();center.TickPendingTrails();
        Check(Rand.State==rng&&ThingMaker.ids==next&&center.pending.Count==0,"host-only pending queue dispatch is cosmetic");
        new Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister {trail=trail}.SpawnAbsorbTrail();
        Check(Rand.State==rng&&ThingMaker.ids==next,"experience canister visual preserves shared streams");
        var drone=(Nivarian_Race.Code.NivarianThing.ProgrammableMoverThing)GenSpawn.Spawn(mover,null);drone.Tick();
        Check(drone.thingIDNumber>=0&&Rand.State!=rng&&ThingMaker.ids==next+1,"saveable gameplay mover retains native RNG and ID allocation");
        rng=Rand.State;
        Nivarian_Race.Code.NivarianThing.Base.ProgrammableThing.Fail=true;bool threw=false;
        try{host.Tick();}catch(Exception){threw=true;}
        Nivarian_Race.Code.NivarianThing.Base.ProgrammableThing.Fail=false;
        Check(threw&&Rand.State==rng&&Rand.states.Count==0&&!Multiplayer.Client.Patches.UniqueIdsPatch.useLocalIdsOverride,"exception restores local ID override and Rand stack");
        Multiplayer.Client.Patches.UniqueIdsPatch.useLocalIdsOverride=true;host.Tick();
        Check(Multiplayer.Client.Patches.UniqueIdsPatch.useLocalIdsOverride,"preexisting local ID override preserved");
        Multiplayer.Client.Patches.UniqueIdsPatch.useLocalIdsOverride=false;
        MP.IsInMultiplayer=false;next=ThingMaker.ids;GenSpawn.Spawn(trail,null);
        Check(ThingMaker.ids==next+1&&Rand.State!=rng,"single-player trail allocation remains native");MP.IsInMultiplayer=true;

        var fixedArmor=new ThingDef{defName="fixed"};var tagged=new ThingDef{defName="tagged"};
        var kind=new PawnKindDef {apparelRequired=new List<ThingDef>{fixedArmor}};
        var req=new PawnGenerationRequest {KindDef=kind};var pawn=new Pawn();
        Milira.Milira_MilianPawnGenerator_Patch.Chosen=tagged;
        Milira.Milira_MilianPawnGenerator_Patch.Postfix(ref pawn,req);
        Milira.Milira_MilianPawnGenerator_Patch.Postfix(ref pawn,req);
        Check(kind.apparelRequired.Count==3,"old generator permanently accumulates shared-def equipment");
        kind.apparelRequired=new List<ThingDef>{fixedArmor};MilianApparelBoundary.Apply(h);
        for(int i=0;i<3;i++) { Milira.Milira_MilianPawnGenerator_Patch.Postfix(ref pawn,req);
            Check(kind.apparelRequired.Count==1&&Milira.Milira_MilianPawnGenerator_Patch.Used.Count==2,"generated equipment retained without def mutation round "+i); }
        var coldKind=new PawnKindDef{apparelRequired=new List<ThingDef>{fixedArmor}};
        Milira.Milira_MilianPawnGenerator_Patch.Postfix(ref pawn,new PawnGenerationRequest{KindDef=coldKind});
        Check(Milira.Milira_MilianPawnGenerator_Patch.Used.Count==2&&coldKind.apparelRequired.Count==kind.apparelRequired.Count,"cold peer and warmed host use equal generation inputs");
        Milira.Milira_MilianPawnGenerator_Patch.Fail=true;threw=false;
        try{Milira.Milira_MilianPawnGenerator_Patch.Postfix(ref pawn,req);}catch(Exception){threw=true;}
        Check(threw&&kind.apparelRequired.Count==1,"generator exception cannot leak apparel into defs");
        Milira.Milira_MilianPawnGenerator_Patch.Fail=false;
        Check(MilianApparelBoundary.ForGeneration(null)==null,"null source preserved");
        MP.IsInMultiplayer=false;Milira.Milira_MilianPawnGenerator_Patch.Postfix(ref pawn,req);
        Check(kind.apparelRequired.Count==2,"single-player generator behavior retained");MP.IsInMultiplayer=true;
    }
}
