using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class NaglfarWorldProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static bool requested, prepared, sent, finished, failed, advancing;
    static int round, start, skyfallers, missions, cooldownSet;
    static float deadline;
    static Map sourceMap, targetMap;
    static ThingWithComps ship;
    static ThingComp turret;
    static ISyncMethod prepare;
    static readonly List<string> landings = new List<string>();
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Init(Harmony h)
    {
        if (!MiliraFeatures.Enabled) return;
        prepare = MP.RegisterSyncMethod(typeof(NaglfarWorldProbe), nameof(Prepare));
        var method = typeof(SkyfallerMaker).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "SpawnSkyfaller" && m.GetParameters().Select(p => p.ParameterType)
                .SequenceEqual(new[] { typeof(ThingDef), typeof(IntVec3), typeof(Map) }));
        h.Patch(method, prefix: new HarmonyMethod(typeof(NaglfarWorldProbe), nameof(BeforeSkyfaller)));
        h.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("PLAMilira.CompPLAMilira_TurretGun_Building"),
            "TryCastFireMission"), postfix: new HarmonyMethod(typeof(NaglfarWorldProbe), nameof(AfterMission)));
    }
    public static void Prepare(int n)
    {
        var maps = Find.Maps.Where(m => m.IsPlayerHome).OrderBy(m => m.uniqueID).ToArray();
        Require(maps.Length == 2, "Naglfar world support needs two maps");
        round = n; sourceMap = maps[n % 2]; targetMap = maps[1 - n % 2];
        var def = ThingDef.Named("PLAMilira_Naglfar_Middle");
        ship = (ThingWithComps)ThingMaker.MakeThing(def);
        var sourceCell = new IntVec3(sourceMap.Size.x / 4 + 25 * n, 0, sourceMap.Size.z / 4 + 25 * n);
        GenSpawn.Spawn(ship, sourceCell, sourceMap);
        ship.SetFaction(Faction.OfPlayer);
        turret = ship.AllComps.Single(c => c.GetType().FullName == "PLAMilira.CompPLAMilira_TurretGun_Building" &&
            (bool)AccessTools.Field(c.props.GetType(), "showWorldGizmo").GetValue(c.props));
        start = TickPatch.Timer; deadline = UnityEngine.Time.realtimeSinceStartup + 120;
        skyfallers = missions = cooldownSet = 0; landings.Clear(); sent = advancing = false; prepared = true;
        Log.Message("NAGLFAR_WORLD PREPARE round=" + n + " ship=" + ship.ThingID + " source=" + sourceMap.uniqueID + " target=" + targetMap.uniqueID);
    }
    static void BeforeSkyfaller(object[] __args)
    {
        if (!prepared || __args[0] is not ThingDef def || def.defName != "PLAMilira_Naglfar_Middle_Bombardment") return;
        Require(ReferenceEquals(__args[2], targetMap), "Naglfar skyfaller on wrong map");
        skyfallers++; landings.Add(((IntVec3)__args[1]).ToString());
    }
    static void AfterMission(object __instance)
    {
        if (prepared && ReferenceEquals(__instance, turret))
        {
            missions++;
            cooldownSet = (int)AccessTools.Field(turret.GetType(), "ticksToReset").GetValue(turret);
        }
    }
    internal static bool Update()
    {
        if (finished) return true;
        if (failed) return false;
        try
        {
            Require(!Mp.session.desynced, "Naglfar desync");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            Require(UnityEngine.Time.realtimeSinceStartup < deadline, "Naglfar target timeout");
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                sent = true;
                var previous = Find.CurrentMap;
                try
                {
                    Current.Game.CurrentMap = sourceMap;
                    var command = turret.CompGetGizmosExtra().OfType<Command_Action>()
                        .Single(g => g.GetType().Name == "Command_ActionWithCooldown");
                    command.action();
                    var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                    var options = (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
                    int index = Find.Maps.Where(m => !m.IsPocketMap).ToList().IndexOf(targetMap);
                    Require(index >= 0, "Naglfar target map missing from native menu");
                    options[index].action(); menu.Close(false);
                    var action = (Action<LocalTargetInfo>)AccessTools.Field(typeof(Targeter), "action").GetValue(Find.Targeter);
                    Require(action != null, "Naglfar native target callback missing");
                    var cell = targetMap.Center + new IntVec3(Math.Min(40, targetMap.Size.x / 5), 0, Math.Min(35, targetMap.Size.z / 5));
                    action(new LocalTargetInfo(cell)); Find.Targeter.StopTargeting();
                    Require(skyfallers == 0 && missions == 0 && (int)AccessTools.Field(turret.GetType(), "ticksToReset").GetValue(turret) <= 0,
                        "Naglfar UI mutated simulation before sync");
                    Log.Message("NAGLFAR_WORLD NATIVE_TARGET source=" + sourceMap.uniqueID + " target=" + targetMap.uniqueID + " cell=" + cell);
                }
                finally { Current.Game.CurrentMap = previous; }
            }
            if (TickPatch.Timer - start < 300) return false;
            Require(skyfallers == 2 && missions == 2 && cooldownSet == 900,
                "Naglfar action not synchronized skyfallers=" + skyfallers + " missions=" + missions + " cooldownSet=" + cooldownSet);
            var receipt = ship.ThingID + ":" + skyfallers + ":" + missions + ":" + cooldownSet + ":" + string.Join(";", landings);
            var path = Path.Combine(Root, Peer + ".naglfarworld" + round);
            if (!File.Exists(path)) { File.WriteAllText(path + ".tmp", receipt); File.Move(path + ".tmp", path); Log.Message("NAGLFAR_WORLD ASSERT " + receipt); }
            var other = Path.Combine(Root, (client ? "host" : "client") + ".naglfarworld" + round);
            if (!File.Exists(other)) return false;
            Require(File.ReadAllText(path) == File.ReadAllText(other), "Naglfar peer result mismatch");
            if (round == 2) { finished = true; Log.Message("NAGLFAR_WORLD PASS three native cross-map actions"); return true; }
            if (client && !advancing) { advancing = true; prepare.DoSync(null, round + 1); }
            return false;
        }
        catch (Exception e) { failed = true; File.WriteAllText(Path.Combine(Root, Peer + ".failed"), e.ToString()); Log.Error("COMBINED FAILED NAGLFAR_WORLD " + e); return false; }
    }
}
