using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Globalization;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class BackupBatteryProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static ISyncMethod prepare;
    static Type compType;
    static Pawn pawn;
    static Hediff battery;
    static HediffComp comp;
    static bool requested, prepared, sent, observed, done, failed;
    static int start, nextTrace;
    static float deadline;
    static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    static string State() => pawn.ThingID + ";severity=" + battery.Severity.ToString("G9", CultureInfo.InvariantCulture) +
        ";energy=" + pawn.needs.energy.CurLevelPercentage.ToString("G9", CultureInfo.InvariantCulture);
    internal static void Init(Harmony harmony)
    {
        compType = AccessTools.TypeByName("MilianModification.HediffComp_BackupBattery");
        var charge = compType == null ? null : AccessTools.DeclaredMethod(compType, "BatteryChargeOnce", Type.EmptyTypes);
        Check(charge != null, "native backup battery charge method missing");
        harmony.Patch(charge, postfix: new HarmonyMethod(typeof(BackupBatteryProbe), nameof(ChargePostfix)));
        var wrapperType = AccessTools.TypeByName("MP_MeowOnlineShop.MiliraAddonCompat.MilianModification_StateSync");
        var wrapper = wrapperType == null ? null : AccessTools.DeclaredMethod(wrapperType, "SyncedBatteryChargeOnce", new[] { typeof(Pawn), typeof(HediffDef) });
        Check(wrapper != null, "backup battery sync wrapper missing");
        harmony.Patch(wrapper, prefix: new HarmonyMethod(typeof(BackupBatteryProbe), nameof(WrapperPrefix)));
        prepare = MP.RegisterSyncMethod(typeof(BackupBatteryProbe), nameof(Prepare));
    }
    public static void Prepare()
    {
        var map = Find.Maps.First(m => m.IsPlayerHome);
        pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
        GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 12), map);
        pawn.drafter.Drafted = true;
        var def = DefDatabase<HediffDef>.GetNamed("MilianFitting_BackupBattery");
        var componentDef = DefDatabase<ThingDef>.GetNamed("MilianFitting_BackupBattery");
        var componentData = AccessTools.Field(componentDef.GetType(), "milianComponent").GetValue(componentDef);
        var slots = (IEnumerable)AccessTools.Field(componentData.GetType(), "possibleSlots").GetValue(componentData);
        var slot = slots.Cast<object>().First();
        var modificationType = AccessTools.TypeByName("MilianModification.CompModification");
        var modification = pawn.AllComps.Single(c => c.GetType() == modificationType);
        var recipe = (IDictionary)AccessTools.Field(modificationType, "componentSlotInRecipe").GetValue(modification);
        recipe.Add(slot, componentDef);
        var utility = AccessTools.TypeByName("MilianModification.MilianModificationUtility");
        AccessTools.Method(utility, "InstallComponent", new[] { typeof(Pawn), componentDef.GetType() })
            .Invoke(null, new object[] { pawn, componentDef });
        battery = pawn.health.hediffSet.GetFirstHediffOfDef(def, false);
        Check(battery != null, "backup battery hediff not installed");
        var installed = (IDictionary)AccessTools.Field(modificationType, "componentSlotInstalled").GetValue(modification);
        Check(installed.Contains(slot) && installed[slot] == componentDef, "native install did not persist the slot");
        var comps = (IEnumerable)AccessTools.Field(typeof(HediffWithComps), "comps").GetValue(battery);
        comp = comps.Cast<HediffComp>().Single(c => c.GetType() == compType);
        Check(pawn.needs.energy != null, "Milian mech has no energy need");
        battery.Severity = 0.5f;
        pawn.needs.energy.CurLevelPercentage = 0.3f;
        prepared = true; start = TickPatch.Timer; nextTrace = start + 1000; deadline = UnityEngine.Time.realtimeSinceStartup + 120;
        Log.Message("BACKUP_BATTERY PREPARE " + State());
    }
    static void WrapperPrefix(Pawn pawn, HediffDef def)
    {
        if (pawn != BackupBatteryProbe.pawn) return;
        var matches = pawn.health.hediffSet.hediffs.Where(h => h.def?.defName == def?.defName).ToArray();
        var selected = pawn.health.hediffSet.GetFirstHediffOfDef(def, false);
        var comps = (selected as HediffWithComps)?.comps;
        Log.Message("BACKUP_BATTERY WRAPPER_ENTER interface=" + MP.InInterface + " sync=" + MP.IsExecutingSyncCommand +
            " sim=" + TickPatch.Simulating + " def=" + def?.defName + " sameDef=" + (def == battery.def) +
            " matches=" + matches.Length + " selected=" + (selected != null) + " sameHediff=" + (selected == battery) +
            " comps=" + (comps == null ? "null" : string.Join(",", comps.Select(c => c.GetType().FullName))) + " " + State());
    }
    static void ChargePostfix(HediffComp __instance)
    {
        if (__instance != comp) return;
        Log.Message("BACKUP_BATTERY NATIVE_POST interface=" + MP.InInterface + " sync=" + MP.IsExecutingSyncCommand +
            " sim=" + TickPatch.Simulating + " " + State());
        if (observed || Math.Abs(battery.Severity - 0.001f) >= 0.0001f) return;
        observed = true;
        string path = Path.Combine(Root, Peer + ".backupbattery");
        File.WriteAllText(path, State());
        Log.Message("BACKUP_BATTERY NATIVE_EXEC " + State());
    }
    internal static bool Update()
    {
        if (done) return true;
        if (failed) return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null); return false; }
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                Check(pawn.CurJob?.def != null, "backup battery pawn has no current job");
                var gizmos = comp.CompGetGizmos().OfType<Command_Action>().ToArray();
                Check(gizmos.Length == 1 && !gizmos[0].Disabled, "backup battery native gizmo unavailable");
                string before = State();
                gizmos[0].action();
                Check(State() == before, "backup battery changed local state before sync command");
                sent = true;
                Log.Message("BACKUP_BATTERY DISPATCH native gizmo action rollback=True");
            }
            Check(UnityEngine.Time.realtimeSinceStartup < deadline, "backup battery timeout");
            if (TickPatch.Timer >= nextTrace)
            {
                nextTrace += 1000;
                Log.Message("BACKUP_BATTERY WAIT sent=" + sent + " observed=" + observed + " " + State());
            }
            if (!observed) return false;
            Check(Math.Abs(battery.Severity - 0.001f) < 0.0001f, "backup battery severity not consumed");
            string path = Path.Combine(Root, Peer + ".backupbattery");
            string other = Path.Combine(Root, (client ? "host" : "client") + ".backupbattery");
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(path) == File.ReadAllText(other), "backup battery peer receipt mismatch");
            var fields = File.ReadAllText(path);
            Check(fields.Contains(";energy="), "energy receipt missing");
            done = true;
            Log.Message("BACKUP_BATTERY ACTION_PASS " + fields);
            return true;
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED BACKUP_BATTERY " + e); }
        return false;
    }
}
