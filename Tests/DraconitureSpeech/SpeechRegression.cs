using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

// Only called by the command-line-gated regression driver. Never shipped.
internal static class SpeechRegression
{
    static readonly PropertyInfo randState = AccessTools.Property(typeof(Rand), "StateCompressed");
    static ulong RandomState => (ulong)randState.GetValue(null);
    static Type droneType;
    static MethodInfo speech;
    static FieldInfo queue, timer, speechEnabled, limit;
    static object coreSettings, furnitureSettings;
    static bool throwInterval, installed;
    static int nativeCalls, repairCalls;
    static readonly Dictionary<int, int> repairsByMap = new Dictionary<int, int>();
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static bool Client => GenCommandLine.CommandLineArgPassed("psclient");
    static bool Old => GenCommandLine.CommandLineArgPassed("speechold");
    static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    static object Settings(string type) => AccessTools.Property(AccessTools.TypeByName(type), "Settings")?.GetValue(null)
        ?? AccessTools.Field(AccessTools.TypeByName(type), "Settings").GetValue(null);

    internal static void Install(Harmony h)
    {
        if (installed) return;
        installed = true;
        droneType = AccessTools.TypeByName("NivarianRace.DraconicMilitary.EngineeringDroneThing");
        speech = AccessTools.DeclaredMethod(droneType, "TickThrowWords");
        queue = AccessTools.Field(droneType, "GlobalRecentWordThrowTicks");
        timer = AccessTools.Field(droneType, "_ticksToNextWord");
        coreSettings = Settings("Nivarian.NivarianMod");
        furnitureSettings = Settings("Nivarian_Race_Draconiture.NivarianDraconitureMod");
        speechEnabled = AccessTools.Field(coreSettings.GetType(), "EnableDroneSpeech");
        limit = AccessTools.Field(furnitureSettings.GetType(), "EngineerDroneMaxWordsPerSecond");
        Require(speech != null && queue != null && timer != null && speechEnabled != null && limit != null, "speech targets missing");
        h.Patch(speech, prefix: new HarmonyMethod(typeof(SpeechRegression), nameof(Before)) { priority = Priority.First },
            finalizer: new HarmonyMethod(typeof(SpeechRegression), nameof(After)) { priority = Priority.Last });
        h.Patch(AccessTools.DeclaredMethod(droneType, "NextWordInterval"),
            postfix: new HarmonyMethod(typeof(SpeechRegression), nameof(ThrowAfterInterval)));
        var engineer = AccessTools.TypeByName("Nivarian.NivarianDrones.NivarianDroneComps.NivarianDroneComp_Engineer");
        h.Patch(AccessTools.DeclaredMethod(engineer, "TickRepair"),
            postfix: new HarmonyMethod(typeof(SpeechRegression), nameof(AfterRepair)));
        Log.Message("SPEECH TARGET assembly=" + droneType.Assembly.GetName().Name + " mvid=" + droneType.Module.ModuleVersionId);
    }
    static void Before(out ulong __state) { __state = RandomState; }
    static void After(ulong __state)
    {
        if (!MP.IsInMultiplayer) return;
        nativeCalls++;
        if (__state != RandomState)
        {
            File.WriteAllText(Path.Combine(Root, "speech.leak"), "before=" + __state + ";after=" + RandomState);
            if (!Old) throw new InvalidOperationException("SPEECH_SHARED_RAND_LEAK");
        }
    }
    static void ThrowAfterInterval() { if (throwInterval) throw new InvalidOperationException("SPEECH_EXPECTED_EXCEPTION"); }
    static void AfterRepair(ThingComp __instance)
    {
        repairCalls++;
        int id = __instance.parent.Map.uniqueID;
        repairsByMap.TryGetValue(id, out int count); repairsByMap[id] = count + 1;
    }
    internal static void BeginCycle()
    {
        nativeCalls = repairCalls = 0; repairsByMap.Clear();
        foreach (var map in Find.Maps)
        foreach (var wall in map.listerThings.AllThings.Where(t => t.def == ThingDefOf.Wall && t.Faction == Faction.OfPlayer).OrderBy(t => t.thingIDNumber).Take(3))
        {
            Log.Message("SPEECH FIXTURE map=" + map.uniqueID + " wall=" + wall.thingIDNumber +
                " fogged=" + wall.Fogged() + " registered=" + map.listerBuildingsRepairable.Contains(Faction.OfPlayer, (Building)wall));
            // The native search excludes fogged targets. Directly changing HP in
            // a fixture also requires the notification normally sent by damage.
            map.fogGrid.Unfog(wall.Position);
            wall.HitPoints = Math.Max(1, wall.MaxHitPoints / 5);
            map.listerBuildingsRepairable.Notify_BuildingTookDamage((Building)wall);
            Require(!wall.Fogged() && map.listerBuildingsRepairable.Contains(Faction.OfPlayer, (Building)wall), "repair fixture not visible/registered");
        }
    }

    internal static void CreateWork(Map map)
    {
        var cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 8);
        foreach (var at in GenRadial.RadialCellsAround(cell, 9f, true))
            if (at.InBounds(map)) map.fogGrid.Unfog(at);
        var hub = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Nivarian_DroneHub_Engineer"));
        hub.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(hub, cell, map);
        for (int i = 0; i < 3; i++)
        {
            var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel);
            wall.SetFaction(Faction.OfPlayer);
            var at = CellFinder.RandomClosewalkCellNear(cell, map, 5);
            GenSpawn.Spawn(wall, at, map);
            wall.HitPoints = Math.Max(1, wall.MaxHitPoints / 5);
            map.listerBuildingsRepairable.Notify_BuildingTookDamage((Building)wall);
        }
    }

    internal static void Exercise(Map map, int cycle, int step)
    {
        // Re-run all branch combinations from the remote synchronized action.
        // The transient drone is a real Def/Thing; no compatibility helper is called.
        var drone = (ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Nivarian_Engineer"));
        drone.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(drone, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
        var state = AccessTools.Property(droneType, "State");
        state.SetValue(drone, Enum.Parse(state.PropertyType, "GoHome"));
        var slots = (IList)queue.GetValue(null);
        object[] savedSlots = slots.Cast<object>().ToArray();
        object enabledBefore = speechEnabled.GetValue(coreSettings), limitBefore = limit.GetValue(furnitureSettings);
        try
        {
            for (int mode = 0; mode < 4; mode++)
            {
                slots.Clear(); limit.SetValue(furnitureSettings, 2);
                speechEnabled.SetValue(coreSettings, mode != 1 || !Client);
                if ((mode == 0 && !Client) || (mode == 2 && Client))
                { slots.Add(GenTicks.TicksAbs); slots.Add(GenTicks.TicksAbs); }
                timer.SetValue(drone, 0);
                throwInterval = mode == 3;
                ulong before = RandomState;
                bool caught = false;
                try { speech.Invoke(drone, null); }
                catch (TargetInvocationException e)
                {
                    if (!throwInterval || !e.ToString().Contains("SPEECH_EXPECTED_EXCEPTION")) throw;
                    caught = true;
                }
                finally { throwInterval = false; }
                Require(caught == (mode == 3), "exception branch not reached");
                ulong after = RandomState;
                Log.Message("SPEECH ACTION cycle=" + cycle + " step=" + step + " map=" + map.uniqueID + " mode=" + mode + " before=" + before + " after=" + after + " nativeCalls=" + nativeCalls);
                if (Old && before != after)
                {
                    File.WriteAllText(Path.Combine(Root, "speech.baseline.reproduced"), "Native speech changed shared Rand: " + before + " -> " + after);
                    return;
                }
                Require(before == after, "SPEECH_SHARED_RAND_LEAK");
                if (mode < 3 && (mode != 1 || !Client)) Require((int)timer.GetValue(drone) > 0, "speech timer not updated");
            }
        }
        finally
        {
            throwInterval = false; slots.Clear(); foreach (object value in savedSlots) slots.Add(value);
            speechEnabled.SetValue(coreSettings, enabledBefore); limit.SetValue(furnitureSettings, limitBefore);
            drone.Destroy();
        }
        Log.Message("SPEECH ASSERT cycle=" + cycle + " step=" + step + " map=" + map.uniqueID + " branches=4 rand=preserved");
    }
    internal static void CheckWork()
    {
        Require(nativeCalls > 0, "native speech never executed");
        Require(repairCalls > 0, "native repair never executed");
        foreach (var map in Find.Maps) Require(repairsByMap.ContainsKey(map.uniqueID), "native repair not reached on map " + map.uniqueID);
        Log.Message("SPEECH WORK nativeSpeech=" + nativeCalls + " repairTicks=" + repairCalls);
    }
}
