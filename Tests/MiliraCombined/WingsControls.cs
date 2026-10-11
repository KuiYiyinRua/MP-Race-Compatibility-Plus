using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class WingsControlsProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Pawn owner, mech;
    static HediffComp comp;
    static ThingDef selected;
    static int round, start, phase;
    static float deadline;
    static bool prepared, requested, sent, observed, advancing, finished, failed;
    static object Get(object value, string name) => AccessTools.Field(value.GetType(), name).GetValue(value);
    static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    static string Turrets => string.Join(",", ((IEnumerable)Get(comp, "selectedTurretDef")).Cast<ThingDef>().Select(d => d.defName));
    static Pawn Bound => (Pawn)Get(comp, "boundMech");
    internal static void Init()
    {
        if (!GenCommandLine.CommandLineArgPassed("miliralowfrequency")) return;
        prepare = MP.RegisterSyncMethod(typeof(WingsControlsProbe), nameof(Prepare));
    }
    public static void Prepare(int n)
    {
        round=n; phase=0; sent=observed=advancing=false;
        var map=Find.Maps.First(m=>m.IsPlayerHome);
        if(n<3)
        {
            owner=PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_RookIV"),Faction.OfPlayer);
            GenSpawn.Spawn(owner,CellFinder.RandomClosewalkCellNear(map.Center,map,10),map);
            var def=ThingDef.Named("PLAMilianFitting_Core_RookIV");
            var data=Get(def,"milianComponent");
            var slot=((IEnumerable)Get(data,"possibleSlots")).Cast<object>().First();
            var mod=owner.AllComps.Single(c=>c.GetType().FullName=="MilianModification.CompModification");
            ((IDictionary)Get(mod,"componentSlotInRecipe")).Add(slot,def);
            AccessTools.Method(AccessTools.TypeByName("MilianModification.MilianModificationUtility"),"InstallComponent",new[]{typeof(Pawn),def.GetType()}).Invoke(null,new object[]{owner,def});
            comp=owner.health.hediffSet.hediffs.OfType<HediffWithComps>().SelectMany(h=>h.comps).Single(c=>c.GetType().FullName=="PLAMilianModification.HediffComp_UhlanTurretSelector");
            var options=((IEnumerable)Get(comp.props,"turretOptions")).Cast<ThingDef>().ToList();
            selected=options.First(d=>d!=((IList)Get(comp,"selectedTurretDef"))[n]);
        }
        else
        {
            owner=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);
            GenSpawn.Spawn(owner,CellFinder.RandomClosewalkCellNear(map.Center,map,10),map);
            owner.health.AddHediff(HediffDefOf.MechlinkImplant,owner.health.hediffSet.GetBrain());
            owner.health.AddHediff(HediffDef.Named("PLAMilira_Star_Argo"),owner.health.hediffSet.GetBrain());
            mech=PawnGenerator.GeneratePawn(PawnKindDef.Named("Mech_Lifter"),Faction.OfPlayer);
            GenSpawn.Spawn(mech,CellFinder.RandomClosewalkCellNear(owner.Position,map,5),map);
            mech.relations.AddDirectRelation(PawnRelationDefOf.Overseer,owner);
            owner.mechanitor.AssignPawnControlGroup(mech);
            Check(owner.mechanitor.ControlledPawns.Contains(mech),"native overseer control missing");
            owner.drafter.Drafted=true;
            comp=owner.health.hediffSet.hediffs.OfType<HediffWithComps>().SelectMany(h=>h.comps).Single(c=>c.GetType().FullName=="PLAMilira.HediffComp_Argo");
            Check(Bound==null,"Argo initial state");
        }
        prepared=true; start=TickPatch.Timer; deadline=Time.realtimeSinceStartup+120;
        Log.Message("WINGS_CONTROLS PREPARE round="+round+" owner="+owner.ThingID);
    }
    static List<FloatMenuOption> Open()
    {
        var command=comp.CompGetGizmos().OfType<Command_Action>().ElementAt(round<3?round:0);
        command.action();
        var menu=Find.WindowStack.Windows.OfType<FloatMenu>().Last();
        var options=(List<FloatMenuOption>)Get(menu,"options");
        // Keep native delegates, close only the local menu shell.
        menu.Close(false); return options;
    }
    internal static bool Update()
    {
        if(finished)return true; if(failed)return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count==2,"network health");
            if(!prepared){if(client&&!requested)requested=prepare.DoSync(null,0);return false;}
            Check(Time.realtimeSinceStartup<deadline,"Wings control timeout round="+round+" phase="+phase);
            if(client&&!sent&&TickPatch.Timer-start>60)
            {
                var before=round<3?Turrets:Bound?.ThingID;
                // Exercise cancel and reopen before the mutation.
                Open();var options=Open();
                var option=round<3?options.Single(o=>o.Label==selected.LabelCap.ToString()):options[phase==0?0:options.Count-1];
                Check(!option.Disabled,"native menu disabled");option.action();
                Check(before==(round<3?Turrets:Bound?.ThingID),"native menu local mutation before sync");
                sent=true;Log.Message("WINGS_CONTROLS DISPATCH round="+round+" phase="+phase+" rollback=True");
            }
            if(round<3){if(((IList)Get(comp,"selectedTurretDef"))[round]!=selected)return false;}
            else
            {
                if(phase==0){if(Bound!=mech)return false;phase=1;sent=false;start=TickPatch.Timer;Log.Message("WINGS_CONTROLS BIND_PASS round="+round+" bound="+mech.ThingID);return false;}
                if(Bound!=null)return false;
            }
            var receipt=owner.ThingID+";"+(round<3?"slot="+round+";turrets="+Turrets+";label="+comp.CompLabelInBracketsExtra:"mech="+mech.ThingID+";controlled=True;bind=True;unbind=True");
            var local=Path.Combine(root,Peer+".wingscontrols"+round);var other=Path.Combine(root,(client?"host":"client")+".wingscontrols"+round);
            if(!observed){observed=true;File.WriteAllText(local+".tmp",receipt);File.Move(local+".tmp",local);Log.Message("WINGS_CONTROLS ACTION_PASS round="+round+" "+receipt);}
            if(!File.Exists(other))return false;
            Check(File.ReadAllText(local)==File.ReadAllText(other),"Wings peer mismatch");
            if(round==5){finished=true;return true;}
            if(client&&!advancing){advancing=true;prepare.DoSync(null,round+1);}
        }
        catch(Exception e){failed=true;Log.Error("COMBINED FAILED WINGS_CONTROLS "+e);}
        return false;
    }
}
