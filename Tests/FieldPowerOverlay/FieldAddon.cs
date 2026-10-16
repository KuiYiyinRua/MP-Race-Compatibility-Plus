using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

public static class FieldAddon
{
    static readonly Type fieldType=AccessTools.TypeByName("NivarianCommander.CommanderField");
    static readonly Type energyType=AccessTools.TypeByName("NivarianCommander.FieldEnergy");
    static readonly IntVec3 center=new IntVec3(12,0,11);
    static readonly string[] consumers={"StandingLamp","CommsConsole","ElectricStove"};
    public static Map TargetMap=>Find.Maps.OrderBy(m=>m.uniqueID).Last();
    static object Field=>AccessTools.Property(fieldType,"Current").GetValue(null);
    static Thing[] Things()=>TargetMap.listerThings.AllThings.Where(t=>t.Position.x<20&&t.Position.z<20&&consumers.Contains(t.def.defName)).OrderBy(t=>t.thingIDNumber).ToArray();
    public static void Create()
    {
        var ledgerType=AccessTools.TypeByName("NivarianCommander.NativeLedger");
        var ledgerComp=AccessTools.Property(ledgerType,"Component").GetValue(null);
        var money=(IDictionary)AccessTools.Property(ledgerComp.GetType(),"CurrencyHold").GetValue(ledgerComp);
        var currency=AccessTools.TypeByName("Nivarian_Race.Code.Defs.NivarianMotherShipRequestCurrencyDef");
        foreach(var name in new[]{"PowerCoin","VolatileReactive"})money[GenDefDatabase.GetDef(currency,name)]=100000;
        var map=TargetMap;
        for(int x=8;x<20;x++)for(int z=8;z<20;z++)
        {
            var cell=new IntVec3(x,0,z);
            foreach(var t in cell.GetThingList(map).ToArray())if(!(t is Pawn))t.Destroy(DestroyMode.Vanish);
            map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
        }
        for(int x=9;x<=17;x++)Spawn("PowerConduit",x,11);
        Spawn("StandingLamp",10,12);Spawn("CommsConsole",13,13);Spawn("ElectricStove",16,13);
        map.powerNetManager.UpdatePowerNetsAndConnections_First();
        AccessTools.Method(fieldType,"Deploy").Invoke(Field,new object[]{map,center});
        foreach(var t in Things())t.TryGetComp<CompPowerTrader>().PowerOn=true;
        object energy=AccessTools.Method(energyType,"For").Invoke(null,new object[]{map});
        AccessTools.Method(energyType,"Rebuild").Invoke(energy,null);
        Log.Message("FIELD_PROBE SETUP map="+map.uniqueID+" field-only grid prepared before native save/reload");
    }
    static void Spawn(string def,int x,int z)
    {
        var t=GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(def)),new IntVec3(x,0,z),TargetMap);t.SetFaction(Faction.OfPlayer);
    }
    public static void Change(int round)
    {
        var generatorCell=new IntVec3(10,0,9);
        foreach(var old in TargetMap.listerThings.AllThings.Where(t=>t.def.defName=="WoodFiredGenerator"&&t.Position==generatorCell).ToArray())old.Destroy(DestroyMode.Vanish);
        if(round%3==0)AccessTools.Method(fieldType,"Remove").Invoke(Field,null);
        else if(round%3==1)
        {
            var generator=GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("WoodFiredGenerator")),generatorCell,TargetMap);
            generator.SetFaction(Faction.OfPlayer);generator.TryGetComp<CompRefuelable>().Refuel(75);
            TargetMap.powerNetManager.UpdatePowerNetsAndConnections_First();
            if(!ReferenceEquals(generator.TryGetComp<CompPowerTrader>().PowerNet,Things()[0].TryGetComp<CompPowerTrader>().PowerNet))throw new Exception("NATIVE_FIELD_BACKUP_NOT_CONNECTED");
            AccessTools.Method(fieldType,"Deploy").Invoke(Field,new object[]{TargetMap,center});
            AccessTools.Method(fieldType,"Remove").Invoke(Field,null);
        }
        else AccessTools.Method(fieldType,"Deploy").Invoke(Field,new object[]{TargetMap,center+new IntVec3(round%3-1,0,0)});
        Log.Message("FIELD_PROBE EXECUTED round="+round+" map="+TargetMap.uniqueID+" expected="+(round%3!=0)+" source="+(round%3==0?"off":round%3==1?"native-generator-after-field-removal":"field-only"));
    }
    public static void Assert(string stage,bool expected)
    {
        var things=Things();if(things.Length!=3)throw new Exception("FIELD_FIXTURE_MISSING "+things.Length);
        foreach(var t in things)
        {
            var p=t.TryGetComp<CompPowerTrader>();
            bool icon=AccessTools.Field(typeof(CompPowerTrader),"overlayNeedsPower").GetValue(p)!=null;
            var text=p.CompInspectStringExtra();
            if(p.PowerOn!=expected||icon==expected||string.IsNullOrEmpty(text)||!text.Contains("Wd"))throw new Exception("FIELD_POWER_OR_ICON_MISMATCH stage="+stage+" id="+t.ThingID+" power="+p.PowerOn+" needsIcon="+icon+" expected="+expected+" inspect="+text);
            if(t is Building_CommsConsole comms&&comms.CanUseCommsNow!=expected)throw new Exception("COMMS_NOT_USABLE "+stage);
            if(t is Building_WorkTable table&&table.CurrentlyUsableForBills()!=expected)throw new Exception("STOVE_NOT_USABLE "+stage);
            if(t.TryGetComp<CompGlower>() is CompGlower glow&&glow.Glows!=expected)throw new Exception("LAMP_GLOW_MISMATCH "+stage+" cached="+glow.Glows);
            Log.Message("FIELD_PROBE ASSERT stage="+stage+" map="+TargetMap.uniqueID+" id="+t.ThingID+" power="+p.PowerOn+" needsIcon="+icon+" inspect=True usable=True");
        }
    }
}
