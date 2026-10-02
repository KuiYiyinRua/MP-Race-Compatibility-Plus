using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using Verse;

public static class Desync325Assertions
{
    static Hediff uiHediff;
    static bool uiChecked, collecting;
    static readonly List<int> hitOrder = new List<int>();
    static readonly HashSet<int> fixtureIds = new HashSet<int>();
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }

    public static void ReadInInterface()
    {
        if (uiChecked || !MP.InInterface || Find.Maps.Count == 0) return;
        uiHediff = new Hediff { pawn = Find.Maps[0].mapPawns.FreeColonistsSpawned.First(),
            def = new HediffDef { defName="MeowLocalAbilityReadProbe", abilities = new List<AbilityDef> { DefDatabase<AbilityDef>.GetNamed("Stun") } } };
        var abilities = uiHediff.AllAbilitiesForReading;
        Check(abilities.Count == 0 && AccessTools.Field(typeof(Hediff),"abilities").GetValue(uiHediff) == null,
            "UI allocated Hediff ability cache");
        uiChecked = true;
        Log.Message("DESYNC325 UI_PASS cacheStillNull=True");
    }

    public static void Exercise(Map map)
    {
        Check(uiChecked,"interface check not reached on both peers");
        var h = new Harmony("local.meow.desync325assertions");
        h.Patch(AccessTools.DeclaredMethod(typeof(DamageWorker),"ExplosionDamageThing"),
            prefix:new HarmonyMethod(typeof(Desync325Assertions),nameof(Hit)));
        for (int round=0;round<3;round++)
        {
            fixtureIds.Clear(); hitOrder.Clear();
            var cell=GenRadial.RadialCellsAround(map.Center,18,true).First(c=>c.InBounds(map)&&c.Standable(map)&&c.GetThingList(map).Count==0);
            // Different item defs wipe each other when spawned on one cell.
            // Item + two different filth defs coexist, as in the supplied trace.
            var items=new[] {ThingDefOf.WoodLog,DefDatabase<ThingDef>.GetNamed("Filth_Dirt"),DefDatabase<ThingDef>.GetNamed("Filth_Blood")}.Select(d=>ThingMaker.MakeThing(d)).ToArray();
            foreach(var item in items) {GenSpawn.Spawn(item,cell,map); fixtureIds.Add(item.thingIDNumber);}
            var original=map.thingGrid.ThingsListAt(cell).ToArray();
            if (!GenCommandLine.CommandLineArgPassed("cfgclient")) map.thingGrid.ThingsListAt(cell).Reverse();
            var explosion=(Explosion)GenSpawn.Spawn(ThingDefOf.Explosion,cell,map);
            explosion.radius=0.1f;explosion.damType=DamageDefOf.Bomb;explosion.damAmount=2;explosion.armorPenetration=0f;
            explosion.doSoundEffects=false;explosion.doVisualEffects=false;
            explosion.StartExplosion(null,new List<Thing>());
            collecting=true;
            try {explosion.DoTick();} finally {collecting=false;}
            Check(hitOrder.Count==3&&hitOrder.SequenceEqual(fixtureIds.OrderBy(x=>x)),"native explosion target order differs: hits="+string.Join(",",hitOrder)+" expected="+string.Join(",",fixtureIds.OrderBy(x=>x)));
            var grid=map.thingGrid.ThingsListAt(cell);
            var extra=grid.Where(t=>!original.Contains(t)).ToArray();grid.Clear();
            grid.AddRange(original.Where(t=>t.Spawned&&!t.Destroyed));grid.AddRange(extra);
            Log.Message("DESYNC325 ASSERT explosion="+round+" hits="+string.Join(",",hitOrder)+" health="+string.Join(",",items.Select(t=>t.HitPoints))+" rand="+AccessTools.Property(typeof(Rand),"StateCompressed").GetValue(null));
            foreach(var item in items) item.Destroy();
        }
        h.UnpatchAll(h.Id);
        var abilities=uiHediff.AllAbilitiesForReading;
        Check(abilities.Count==1&&ReferenceEquals(abilities,AccessTools.Field(typeof(Hediff),"abilities").GetValue(uiHediff)),"simulation failed to create real Hediff ability");
        Log.Message("DESYNC325 ASSERT ability="+abilities[0].GetUniqueLoadID());
        DroneRoundTrip(map);
    }

    static void Hit(Thing t) { if (collecting&&fixtureIds.Contains(t.thingIDNumber)) hitOrder.Add(t.thingIDNumber); }

    static void DroneRoundTrip(Map map)
    {
        var type=AccessTools.TypeByName("RavenRace.Features.Drone.Hauling.MapComponent_DroneManager");
        Check(type!=null,"Raven manager missing");
        var original=map.components.First(c=>c.GetType()==type);
        var cache=(IDictionary)AccessTools.Field(type,"ComponentCache").GetValue(null);
        MapComponent sample=(MapComponent)Activator.CreateInstance(type,map), restored=null;
        string path=Path.Combine(GenFilePaths.SaveDataFolderPath,"drone-roundtrip.xml");
        var dispatch=AccessTools.Field(type,"dispatchCounter");var finder=AccessTools.Field(type,"taskFinder");
        var group=AccessTools.Field(finder.FieldType,"nextGroupOffset");var source=AccessTools.Field(finder.FieldType,"nextGeneralStorageSourceOffset");
        try
        {
            dispatch.SetValue(sample,13);group.SetValue(finder.GetValue(sample),9);source.SetValue(finder.GetValue(sample),17);
            Scribe.saver.InitSaving(path,"fixture"); Scribe_Deep.Look(ref sample,"component",map); Scribe.saver.FinalizeSaving();
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref restored,"component",map); Scribe.loader.FinalizeLoading();
            Check((int)dispatch.GetValue(restored)==13&&(int)group.GetValue(finder.GetValue(restored))==9&&(int)source.GetValue(finder.GetValue(restored))==17,
                "native Raven PostLoadInit reset scheduler/cursors");
            Log.Message("DESYNC325 ASSERT droneSnapshot=13:9:17 freshInstance="+(!ReferenceEquals(sample,restored)));
        }
        finally { cache[map]=original; }
    }
}
