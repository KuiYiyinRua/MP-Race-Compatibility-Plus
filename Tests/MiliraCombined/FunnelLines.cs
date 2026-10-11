using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class FunnelLinesProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string[] apparelDefs = { "Milira_CombatDrone_FunnelBitC", "Milira_CombatDrone_FunnelBitE" };
    static readonly string[] abilityDefs = { "Milira_CombatDrone_FunnelBitB_Ability_C", "Ability_Milira_FunnelBit_LinearShield" };
    static readonly string[] compNames = { "FunnelBit.CompAbilityEffect_LaunchLineAoE", "FunnelBit.CompAbilityEffect_LaunchLinearShield" };
    static readonly string[] targeterNames = { "FunnelBit.Targeter_LineAoE", "FunnelBit.Targeter_LineShield" };
    static Pawn[] pawns = new Pawn[2];
    static Ability[] abilities = new Ability[2];
    static AbilityComp[] comps = new AbilityComp[2];
    static ISyncMethod prepare;
    static bool requested, ready, sent, advancing, finished, failed;
    static int round, phase, start;
    static readonly int[] applies = new int[2];
    static float deadline;
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static void Require(bool condition, string why) { if (!condition) throw new Exception(why); }

    internal static void Init(Harmony harmony)
    {
        prepare = MP.RegisterSyncMethod(typeof(FunnelLinesProbe), nameof(Prepare));
        for (int i = 0; i < 2; i++)
        {
            var type = AccessTools.TypeByName(compNames[i]);
            Require(type != null && AccessTools.TypeByName(targeterNames[i]) != null, "Funnel line type missing: " + i);
            harmony.Patch(AccessTools.DeclaredMethod(type, "Apply"), postfix: new HarmonyMethod(typeof(FunnelLinesProbe), nameof(AfterApply)));
        }
    }
    static void AfterApply(AbilityComp __instance)
    {
        for (int i = 0; i < 2; i++) if (ReferenceEquals(comps[i], __instance)) applies[i]++;
    }
    public static void Prepare(int n)
    {
        round = n; phase = 0; sent = advancing = false; applies[0] = applies[1] = 0;
        var map = Find.Maps.First(m => m.IsPlayerHome);
        for (int i = 0; i < 2; i++)
        {
            var pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopIII_ArsenalCarrier"), Faction.OfPlayer);
            var cell = CellFinder.RandomClosewalkCellNear(map.Center + new IntVec3(n * 8, 0, i * 8), map, 5);
            GenSpawn.Spawn(pawn, cell, map); pawn.drafter.Drafted = true;
            var garment = (Apparel)ThingMaker.MakeThing(ThingDef.Named(apparelDefs[i]));
            pawn.apparel.Wear(garment, false);
            var ability = garment.AllAbilitiesForReading.Single(a => a.def.defName == abilityDefs[i]);
            pawns[i] = pawn; abilities[i] = ability;
            comps[i] = ability.comps.Single(c => c.GetType().FullName == compNames[i]);
            Require(ReferenceEquals(garment.Wearer, pawn), "Funnel apparel not worn: " + i);
        }
        start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; ready = true;
        Log.Message("FUNNEL_LINES PREPARE round=" + n + " pawns=" + string.Join(",", pawns.Select(p => p.ThingID)));
    }
    static (IntVec3, IntVec3) FindLine(int i)
    {
        var pawn = pawns[i]; var type = AccessTools.TypeByName(targeterNames[i]);
        var validator = i == 1 ? AccessTools.Method(type, "IsValidBarrierLine") : null;
        for (int dx = 3; dx <= 9; dx++) for (int dz = -8; dz <= 8; dz++)
        {
            var begin = pawn.Position + new IntVec3(dx, 0, dz);
            var end = begin + new IntVec3(0, 0, 5);
            if (!begin.InBounds(pawn.Map) || !end.InBounds(pawn.Map)) continue;
            if (i == 1)
            {
                var args = new object[] { pawn, abilities[i], comps[i].props, begin, end, null };
                if (!(bool)validator.Invoke(null, args)) continue;
            }
            else if (!GenSight.LineOfSight(pawn.Position, begin, pawn.Map)) continue;
            return (begin, end);
        }
        throw new Exception("No valid native Funnel line target: " + i);
    }
    static void SendNative(int i)
    {
        var command = (Command_Ability)Activator.CreateInstance(AccessTools.TypeByName("FunnelBit.Command_FunnelBitAbility"), abilities[i], pawns[i]);
        command.ProcessInput(null);
        var field = AccessTools.Field(AccessTools.TypeByName(targeterNames[i]), "onConfirm");
        var confirm = (Action<IntVec3, IntVec3>)field.GetValue(null);
        Require(confirm != null, "Native Funnel drag callback absent: " + i);
        var line = FindLine(i);
        confirm(line.Item1, line.Item2);
        Require(applies[i] == 0, "Funnel native UI changed simulation locally before sync: " + i);
        Find.Targeter.StopTargeting();
        Log.Message("FUNNEL_LINES NATIVE_CONFIRM round=" + round + " phase=" + i + " line=" + line.Item1 + "->" + line.Item2);
    }
    internal static bool Update()
    {
        if (finished) return true;
        if (failed) return false;
        try
        {
            Require(!Mp.session.desynced, "Funnel line desync");
            if (!ready) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            Require(Time.realtimeSinceStartup < deadline, "Funnel line timeout round=" + round + " phase=" + phase);
            if (client && !sent && TickPatch.Timer - start > 60) { sent = true; SendNative(phase); }
            if (TickPatch.Timer - start < 500) return false;
            Require(applies[phase] == 1, "Funnel Apply count=" + applies[phase] + " round=" + round + " phase=" + phase);
            var receipt = pawns[phase].ThingID + ":" + abilities[phase].def.defName + ":" + applies[phase] + ":cooling=" + (abilities[phase].CooldownTicksRemaining > 0);
            var own = Path.Combine(Root, Peer + ".funnelline" + round + "." + phase);
            if (!File.Exists(own)) { File.WriteAllText(own + ".tmp", receipt); File.Move(own + ".tmp", own); Log.Message("FUNNEL_LINES ASSERT " + receipt); }
            var other = Path.Combine(Root, (client ? "host" : "client") + ".funnelline" + round + "." + phase);
            if (!File.Exists(other)) return false;
            Require(File.ReadAllText(own) == File.ReadAllText(other), "Funnel line peer mismatch round=" + round + " phase=" + phase);
            if (phase == 0) { phase = 1; sent = false; start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; return false; }
            if (round == 2) { finished = true; Log.Message("FUNNEL_LINES PASS three casts of each native line ability"); return true; }
            if (client && !advancing) { advancing = true; prepare.DoSync(null, round + 1); }
            return false;
        }
        catch (Exception e) { failed = true; File.WriteAllText(Path.Combine(Root, Peer + ".failed"), e.ToString()); Log.Error("COMBINED FAILED FUNNEL_LINES " + e); return false; }
    }
}
