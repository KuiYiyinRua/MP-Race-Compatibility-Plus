using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Mp=Multiplayer.Client.Multiplayer;

public sealed class BooleanControlsState:GameComponent
{
    public List<ThingWithComps> parents=new List<ThingWithComps>();
    public List<int> indexes=new List<int>();
    public List<string> names=new List<string>();
    public List<bool> values=new List<bool>();
    public BooleanControlsState(Game game){}
    public override void ExposeData()
    {
        Scribe_Collections.Look(ref parents,"booleanParents",LookMode.Reference);
        Scribe_Collections.Look(ref indexes,"booleanIndexes",LookMode.Value);
        Scribe_Collections.Look(ref names,"booleanNames",LookMode.Value);
        Scribe_Collections.Look(ref values,"booleanValues",LookMode.Value);
    }
}
internal static class BooleanControlsProbe
{
    static readonly bool client=GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root=Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer=>client?"client":"host";
    static readonly string[,] actions={
        {"CompAntiballistic","switchOn"},{"CompAntiballistic","switchOnAntiAir"},
        {"CompAntiHarrierLauncher","shouldAutoLaunch"},{"CompNaglfar_Middle_Buff","isOn"},
        {"CompNaglfar_Right_Buff","isOn"},{"CompNaglfar_Left_Armor","isOn"},{"CompNaglfar_Middle_Armor","isOn"}};
    static ISyncMethod prepare;
    static List<ThingComp> comps=new List<ThingComp>();
    static List<Command_Toggle> commands=new List<Command_Toggle>();
    static List<bool> before=new List<bool>(),expected=new List<bool>();
    static int round,start;
    static float deadline;
    static bool prepared,requested,sent,advancing,finished,failed;
    static BooleanControlsState S=>Current.Game.GetComponent<BooleanControlsState>();
    static void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
    static bool Value(ThingComp c,string field)=>(bool)AccessTools.Field(c.GetType(),field).GetValue(c);
    static string Receipt()=>string.Join(";",comps.Select((c,i)=>c.parent.ThingID+":"+c.GetType().Name+":"+actions[i,1]+"="+Value(c,actions[i,1])));
    internal static void Init(Harmony h)
    {
        if(!GenCommandLine.CommandLineArgPassed("milirabooleans"))return;
        prepare=MP.RegisterSyncMethod(typeof(BooleanControlsProbe),nameof(Prepare));
        h.Patch(AccessTools.Method(typeof(ColdProbe),"Release"),postfix:new HarmonyMethod(typeof(BooleanControlsProbe),nameof(AfterCold)));
    }
    public static void Prepare(int n)
    {
        prepared=false;
        round=n;comps.Clear();commands.Clear();before.Clear();expected.Clear();sent=advancing=false;
        var map=Find.Maps.Where(m=>m.IsPlayerHome).OrderBy(m=>m.uniqueID).First();
        var created=new Dictionary<ThingDef,ThingWithComps>();
        for(int i=0;i<actions.GetLength(0);i++)
        {
            var type=AccessTools.TypeByName("PLAMilira."+actions[i,0]);
            var def=DefDatabase<ThingDef>.AllDefs.Where(d=>d.comps?.Any(p=>p.compClass==type)==true && (actions[i,0]!="CompAntiHarrierLauncher" || d.comps.Any(p=>p.compClass.FullName=="PLAMilira.CompAutoAntiHarrierDefenseSystem"))).OrderBy(d=>d.defName).FirstOrDefault();
            Check(def!=null,"no enabled Def for required native boolean "+actions[i,0]);
            if(!created.TryGetValue(def,out var parent))
            {
                parent=(ThingWithComps)ThingMaker.MakeThing(def);
                Func<IntVec3,bool> valid=c=>GenAdj.OccupiedRect(c,Rot4.North,def.size).All(x=>x.InBounds(map)&&x.Standable(map)&&!map.thingGrid.ThingsListAtFast(x).Any(t=>t.def.category==ThingCategory.Building));
                var cell=map.AllCells.OrderBy(c=>c.DistanceToSquared(map.Center)).ThenBy(c=>c.x).ThenBy(c=>c.z).FirstOrDefault(c=>valid(c));
                Check(valid(cell),"no free boolean fixture footprint for "+def.defName);
                GenSpawn.Spawn(parent,cell,map);parent.SetFaction(Faction.OfPlayer);created.Add(def,parent);
            }
            var comp=parent.AllComps.Single(c=>c.GetType()==type);
            var field=AccessTools.Field(type,actions[i,1]);
            var available=comp.CompGetGizmosExtra().OfType<Command_Toggle>().ToArray();
            var matching=available.Where(g=>PatchProcessor.GetOriginalInstructions(g.toggleAction.Method).Any(ins=>ins.opcode==OpCodes.Stfld&&Equals(ins.operand,field))).ToArray();
            Check(matching.Length==1,"native toggle unavailable index="+i+" type="+type.FullName+" field="+field.Name+" def="+def.defName+" available="+string.Join(",",available.Select(g=>g.GetType().FullName+":"+g.toggleAction.Method.Name)));
            var command=matching.Single();
            Check(!command.Disabled,"native toggle disabled "+actions[i,0]);
            comps.Add(comp);commands.Add(command);before.Add(Value(comp,actions[i,1]));expected.Add(!before[i]);
            S.parents.Add(parent);S.indexes.Add(parent.AllComps.IndexOf(comp));S.names.Add(actions[i,1]);S.values.Add(expected[i]);
        }
        prepared=true;start=TickPatch.Timer;deadline=Time.realtimeSinceStartup+120;
        Log.Message("BOOLEAN_CONTROLS PREPARE round="+round+" "+Receipt());
    }
    static void AfterCold()
    {
        if(MP.InInterface)return;
        Check(S.parents.Count==21,"boolean cold fixture count");
        for(int i=0;i<S.parents.Count;i++)Check(S.parents[i]!=null&&!S.parents[i].Destroyed&&Value(S.parents[i].AllComps[S.indexes[i]],S.names[i])==S.values[i],"native boolean cold restoration "+i);
        var receipt=string.Join(";",S.parents.Select((p,i)=>p.ThingID+":"+S.indexes[i]+":"+S.names[i]+"="+Value(p.AllComps[S.indexes[i]],S.names[i])));
        var file=Path.Combine(root,Peer+".booleans-aftercold");File.WriteAllText(file+".tmp",receipt);File.Move(file+".tmp",file);
        Log.Message("BOOLEAN_CONTROLS COLD_PASS count=21");
    }
    internal static bool Update()
    {
        if(finished)return true;if(failed)return false;
        try
        {
            Check(!Mp.session.desynced&&Mp.session.players.Count==2,"boolean network health");
            if(!prepared){if(client&&!requested)requested=prepare.DoSync(null,0);return false;}
            Check(Time.realtimeSinceStartup<deadline,"boolean timeout round="+round);
            if(client&&!sent&&TickPatch.Timer-start>60)
            {
                for(int i=0;i<commands.Count;i++){commands[i].toggleAction();Check(Value(comps[i],actions[i,1])==before[i],"boolean native local mutation before sync");}
                sent=true;Log.Message("BOOLEAN_CONTROLS DISPATCH round="+round+" callbacks=7 rollback=True");
            }
            for(int i=0;i<comps.Count;i++)if(Value(comps[i],actions[i,1])!=expected[i])return false;
            var local=Path.Combine(root,Peer+".booleans"+round);var other=Path.Combine(root,(client?"host":"client")+".booleans"+round);
            if(!File.Exists(local)){File.WriteAllText(local+".tmp",Receipt());File.Move(local+".tmp",local);Log.Message("BOOLEAN_CONTROLS ACTION_PASS round="+round+" "+Receipt());}
            if(!File.Exists(other))return false;Check(File.ReadAllText(local)==File.ReadAllText(other),"boolean peer mismatch");
            if(round==2){finished=true;return true;}if(client&&!advancing){advancing=true;prepare.DoSync(null,round+1);}
        }
        catch(Exception e){failed=true;Log.Error("COMBINED FAILED BOOLEAN_CONTROLS "+e);}
        return false;
    }
}
