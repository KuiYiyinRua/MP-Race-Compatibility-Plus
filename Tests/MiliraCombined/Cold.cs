using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using RimWorld;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

public sealed class MiliraColdState : GameComponent
{
    public bool Active, Released;
    public Pawn Launcher, Student, Speaker;
    public Thing TurretParent;
    public int CompIndex, MapId, OtherMapId, WorldStart, MapStart, OtherMapStart, Launches;
    public MiliraColdState(Game game) { }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref Active, "active"); Scribe_Values.Look(ref Released, "released");
        Scribe_References.Look(ref Launcher, "launcher"); Scribe_References.Look(ref Student, "student"); Scribe_References.Look(ref TurretParent, "turretParent");
        Scribe_References.Look(ref Speaker, "speaker");
        Scribe_Values.Look(ref CompIndex, "compIndex"); Scribe_Values.Look(ref MapId, "mapId"); Scribe_Values.Look(ref OtherMapId, "otherMapId"); Scribe_Values.Look(ref WorldStart, "worldStart"); Scribe_Values.Look(ref MapStart, "mapStart"); Scribe_Values.Look(ref OtherMapStart, "otherMapStart"); Scribe_Values.Look(ref Launches, "launches");
    }
}
internal static class ColdProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly int argCycle = int.Parse(Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("-coldcycle="))?.Substring(11) ?? "0");
    static int cycle = MiliraActionsProbe.ReplayCold3 ? 3 : 0;
    static int phase = MiliraActionsProbe.ReplayCold3 ? 2 : 0;
    static int until, advanceUntil = -1, checkpoint, lastFocusWindow = -1;
    static int lastSoakWorld = -1;
    static float lastSoakProgress;
    static bool started, resumed, releaseSent, failed;
    static ISyncMethod begin, release;
    static Type manager, turretType;
    static MethodInfo ensureSlots, schedulePending, initWait;
    static readonly string[] flags = { "flagA", "flagB", "flagC", "flagS_A", "flagS_B", "flagS_C" };
    static MiliraColdState S => Current.Game?.GetComponent<MiliraColdState>();
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string FileAt(string name) => Path.Combine(Root, name);
    static string Peer => client ? "client" : "host";
    internal static bool Active => S?.Active == true;
    internal static bool QuitRequested => client && File.Exists(FileAt("client" + argCycle + ".quit"));
    static object Get(object o, string name) => AccessTools.Field(o.GetType(), name).GetValue(o);
    static void Set(object o, string name, object value) => AccessTools.Field(o.GetType(), name).SetValue(o, value);
    static void Check(bool v, string reason) { if (!v) throw new Exception(reason); }
    static ThingComp Turret => ((ThingWithComps)S.TurretParent).AllComps[S.CompIndex];
    static object Slot => ((IEnumerable)AccessTools.Method(manager, "GetSlots", new[] { turretType }).Invoke(null, new object[] { Turret })).Cast<object>().First();
    static Hediff StudentHediff
    {
        get
        {
            var value = S.Student.health.hediffSet.hediffs.SingleOrDefault(h => h.GetType().FullName == "PLAMilira.PLAMilira_Student_Dialog.Hediff_PLAMilira_Dialog_A");
            Check(value != null, "student dialogue hediff missing; " + StudentState());
            return value;
        }
    }
    static string StudentState() => S.Student.ThingID + ":" + S.Student.LabelShort + ":dead=" + S.Student.Dead +
        ":destroyed=" + S.Student.Destroyed + ":spawned=" + S.Student.Spawned + ":downed=" + S.Student.Downed +
        ":world=" + Mp.AsyncWorldTime?.worldTicks + ":hediffs=" + string.Join(",", S.Student.health.hediffSet.hediffs.Select(h => h.def.defName + "=" + h.Severity));
    static void StudentDying(Pawn __instance)
    {
        if (Active && __instance == S.Student) Log.Message("COLD STUDENT_DYING " + StudentState() + "\n" + Environment.StackTrace);
    }
    static void StudentHediffRemoving(Hediff __0)
    {
        if (Active && __0 != null && __0.pawn == S.Student && __0.GetType().FullName == "PLAMilira.PLAMilira_Student_Dialog.Hediff_PLAMilira_Dialog_A")
            Log.Message("COLD STUDENT_HEDIFF_REMOVING " + StudentState() + "\n" + Environment.StackTrace);
    }
    internal static void Init(Harmony h)
    {
        manager = AccessTools.TypeByName("FunnelBit.FunnelBitManager"); turretType = AccessTools.TypeByName("FunnelBit.CompTurretGun_FunnelBit");
        ensureSlots = AccessTools.Method(manager, "EnsureSlots", new[] { turretType, typeof(int), AccessTools.TypeByName("FunnelBit.VerbProperties_LaunchFunnelBit") });
        schedulePending = AccessTools.Method(manager, "SchedulePendingLaunch", new[] { AccessTools.TypeByName("FunnelBit.FunnelBitSlot"), typeof(int), turretType, typeof(Pawn), AccessTools.TypeByName("FunnelBit.FunnelBitAbilityWorker"), typeof(LocalTargetInfo), typeof(float) });
        initWait = AccessTools.Method(AccessTools.TypeByName("FunnelBit.Worker_Ability_Wait"), "Init", new[] { typeof(LocalTargetInfo), AccessTools.TypeByName("FunnelBit.CompProperties_AbilityLaunchFunnel") });
        Check(ensureSlots != null && schedulePending != null && initWait != null, "cold fixture exact targets unresolved");
        begin = MP.RegisterSyncMethod(typeof(ColdProbe), nameof(Begin)); release = MP.RegisterSyncMethod(typeof(ColdProbe), nameof(Release));
        h.Patch(AccessTools.Method(typeof(Pawn), "Kill", new[] { typeof(DamageInfo?), typeof(Hediff) }), prefix: new HarmonyMethod(typeof(ColdProbe), nameof(StudentDying)));
        h.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "RemoveHediff", new[] { typeof(Hediff) }), prefix: new HarmonyMethod(typeof(ColdProbe), nameof(StudentHediffRemoving)));
        h.Patch(AccessTools.Method(turretType, "LaunchSlotForAbility"), postfix: new HarmonyMethod(typeof(ColdProbe), nameof(Launched)));
    }
    static void Launched(object __instance, bool __result)
    {
        if (Active && S.Released && __result && ReferenceEquals(__instance, Turret)) S.Launches++;
    }
    internal static void Start() { if (client && !started) started = begin.DoSync(null); }
    public static void Begin()
    {
        var map = Find.Maps.First(m => m.IsPlayerHome);
        S.Launcher = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopIII_ArsenalCarrier"), Faction.OfPlayer);
        GenSpawn.Spawn(S.Launcher, CellFinder.RandomClosewalkCellNear(map.Center, map, 12), map); S.Launcher.drafter.Drafted = true;
        var comp = S.Launcher.AllComps.Concat(S.Launcher.apparel.WornApparel.SelectMany(a => a.AllComps)).Concat(S.Launcher.equipment.AllEquipmentListForReading.SelectMany(e => e.AllComps)).First(c => turretType.IsInstanceOfType(c));
        S.TurretParent = comp.parent; S.CompIndex = comp.parent.AllComps.IndexOf(comp);
        foreach (var name in new[] { "fireAtWill", "autoFire", "droneWorkEnabled" }) Set(comp, name, false);
        ensureSlots.Invoke(null, new object[] { comp, 1, null });
        var target = new LocalTargetInfo(map.Center + new IntVec3(15, 0, 15));
        var worker = Activator.CreateInstance(AccessTools.TypeByName("FunnelBit.Worker_Ability_Wait"));
        initWait.Invoke(worker, new object[] { target, null });
        schedulePending.Invoke(null, new object[] { Slot, 1000000, comp, S.Launcher, worker, target, 73f });
        S.Student = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
        GenSpawn.Spawn(S.Student, CellFinder.RandomClosewalkCellNear(map.Center, map, 14), map);
        var def = DefDatabase<HediffDef>.AllDefs.Single(d => d.hediffClass.FullName == "PLAMilira.PLAMilira_Student_Dialog.Hediff_PLAMilira_Dialog_A");
        var hediff = HediffMaker.MakeHediff(def, S.Student); S.Student.health.AddHediff(hediff);
        var expected = new[] { false, true, true, true, false, true };
        for (int i = 0; i < flags.Length; i++) Set(hediff, flags[i], expected[i]);
        AccessTools.Field(AccessTools.TypeByName("MilianModification.MermeisterDialogUtility"), "dialogAvailable").SetValue(null, false);
        StudentColdProbe.Prepare(map);
        S.Active = true; Log.Message("COLD PREPARED " + Fixture());
    }
    internal static void ExportReplay(string stage)
    {
        if (client) return;
        try
        {
            string path = FileAt("evidence-" + stage + ".zip");
            if (File.Exists(path)) return;
            Replay.ForSaving(new FileInfo(path)).WriteCurrentData();
            Log.Message("COLD REPLAY_EXPORTED " + stage + " server=" + TickPatch.Timer + " world=" + Mp.AsyncWorldTime?.worldTicks);
        }
        catch (Exception e) { Log.Warning("COLD REPLAY_EXPORT_FAILED " + stage + " " + e); }
    }
    static string Fixture()
    {
        Check(S.Launcher != null && S.Student != null && S.TurretParent != null, "cold owner references missing");
        // The dialogue callback and its saved flags are verified before Release.
        // A pawn can die naturally during the later 120k-tick soak, removing
        // that hediff on both peers without invalidating the callback result.
        if (S.Released) return "launched=" + S.Launches + ";studentDead=" + S.Student.Dead;
        var values = string.Join(",", flags.Select(n => Get(StudentHediff, n)));
        Check(values == "False,True,True,True,False,True", "dialogue flags corrupted: " + values);
        var slot = Slot;
        Check(Get(slot, "State").ToString() == "PendingLaunch", "pending launch state lost");
        Check(ReferenceEquals(Get(slot, "pendingTurret"), Turret) && ReferenceEquals(Get(slot, "pendingLauncher"), S.Launcher), "pending owner lost");
        Check(Get(slot, "AbilityWorker") != null && ReferenceEquals(Get(slot, "pendingWorker"), Get(slot, "AbilityWorker")), "pending worker lost");
        return S.Launcher.ThingID + ":" + S.TurretParent.ThingID + ":" + Get(slot, "launchDelayTicks") + ":" + Get(slot, "pendingTarget") + ":" + Get(slot, "pendingRotation") + ":" + Get(slot, "pendingWorker").GetType().FullName + ";flags=" + values;
    }
    static string Snapshot() => MermeisterColdProbe.Snapshot() + ";" + StudentColdProbe.Snapshot() + ";mermeister=" + AccessTools.Field(AccessTools.TypeByName("MilianModification.MermeisterDialogUtility"), "dialogAvailable").GetValue(null) + ";" + Mp.AsyncWorldTime.worldTicks + ":" + Mp.AsyncWorldTime.randState + ";" + string.Join(";", Find.Maps.OrderBy(m => m.uniqueID).Select(m => m.uniqueID + ":" + m.AsyncTime().mapTicks + ":" + m.AsyncTime().randState)) + ";" + Fixture();
    static void Speed(TimeSpeed speed)
    {
        Mp.Client.SendCommand(CommandType.GlobalTimeSpeed, ScheduledCommand.Global, (byte)speed);
        var first = Find.Maps.OrderBy(m => m.uniqueID).FirstOrDefault();
        foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
            Mp.Client.SendCommand(CommandType.MapTimeSpeed, map.uniqueID, (byte)(MiliraActionsProbe.AsyncTime && speed == TimeSpeed.Superfast && map != first ? TimeSpeed.Fast : speed));
    }
    public static void Release()
    {
        StudentColdProbe.Cleanup(); MermeisterColdProbe.Cleanup();
        Check(!S.Released, "duplicate release"); Set(Slot, "launchDelayTicks", 200);
        S.Released = true; S.WorldStart = Mp.AsyncWorldTime.worldTicks; S.MapId = S.Launcher.Map.uniqueID; S.MapStart = S.Launcher.Map.AsyncTime().mapTicks;
        var other = Find.Maps.FirstOrDefault(m => m.uniqueID != S.MapId);
        S.OtherMapId = other?.uniqueID ?? -1; S.OtherMapStart = other?.AsyncTime().mapTicks ?? -1;
        Log.Message("COLD RELEASE after three fresh-client rejoins");
    }
    internal static void Update()
    {
        if (failed) return;
        try
        {
            Check(!Mp.session.desynced, "cold desync");
            if (S.Released)
            {
                Check(Mp.session.players.Count == 2, "soak disconnected"); Fixture();
                int elapsed = Mp.AsyncWorldTime.worldTicks - S.WorldStart;
                if (lastSoakWorld != Mp.AsyncWorldTime.worldTicks) { lastSoakWorld = Mp.AsyncWorldTime.worldTicks; lastSoakProgress = UnityEngine.Time.realtimeSinceStartup; }
                Check(UnityEngine.Time.realtimeSinceStartup - lastSoakProgress < 120, "soak simulation stopped advancing: " + RuntimeHeartbeat.State());
                if (elapsed > 1000) Check(S.Launches == 1, "restored pending launch not executed exactly once");
                if (elapsed >= checkpoint) { checkpoint = elapsed + 10000; Log.Message("COLD SOAK ticks=" + elapsed + " launches=" + S.Launches + " desynced=False"); }
                // The launcher may die or leave the map during a normal long soak.
                // The map clock belongs to the saved map identity, not the pawn.
                var soakMap = Find.Maps.FirstOrDefault(m => m.uniqueID == S.MapId);
                Check(soakMap != null, "soak map missing: " + S.MapId);
                if (MiliraActionsProbe.MultiMap)
                {
                    var otherMap = Find.Maps.FirstOrDefault(m => m.uniqueID == S.OtherMapId);
                    Check(otherMap != null && Find.Maps.Count == 2, "second soak map missing");
                    if (MiliraActionsProbe.AsyncTime && elapsed > 10000)
                        Check(otherMap.AsyncTime().mapTicks - S.OtherMapStart != soakMap.AsyncTime().mapTicks - S.MapStart, "async map clocks did not diverge");
                    if (client && elapsed / 5000 != lastFocusWindow)
                    {
                        lastFocusWindow = elapsed / 5000;
                        Current.Game.CurrentMap = lastFocusWindow % 2 == 0 ? soakMap : otherMap;
                        Log.Message("COLD CLIENT_MAP_FOCUS map=" + Current.Game.CurrentMap.uniqueID + " elapsed=" + elapsed);
                    }
                }
                if (elapsed >= (MiliraActionsProbe.TraceDiagnostic ? 10000 : 120000) && soakMap.AsyncTime().mapTicks - S.MapStart >= (MiliraActionsProbe.TraceDiagnostic ? 10000 : 120000))
                {
                    if (MiliraActionsProbe.MultiMap)
                    {
                        var otherMap = Find.Maps.First(m => m.uniqueID == S.OtherMapId);
                        // Fast runs at a lower tick rate than Superfast. Keep
                        // simulating until the slower map meets its own gate.
                        if (otherMap.AsyncTime().mapTicks - S.OtherMapStart < (MiliraActionsProbe.TraceDiagnostic ? 4000 : 50000)) return;
                    }
                    File.WriteAllText(FileAt(Peer + ".complete"), "three cold rejoins; native launch=" + S.Launches + "; worldTicks=" + elapsed + ";desynced=False");
                    var otherMapTicks = MiliraActionsProbe.MultiMap ? Find.Maps.First(m => m.uniqueID == S.OtherMapId).AsyncTime().mapTicks - S.OtherMapStart : -1;
                    Log.Message("COLD MAP_COMPLETE map=" + S.MapId + " mapTicks=" + (soakMap.AsyncTime().mapTicks - S.MapStart) + " otherMapTicks=" + otherMapTicks + " launcherMap=" + S.Launcher?.Map?.uniqueID);
                    Log.Message("COLD COMPLETE " + Fixture()); failed = true;
                }
                return;
            }
            if (!MermeisterColdProbe.Ready() || !StudentColdProbe.Ready()) return;
            if (!client)
            {
                int requested = File.Exists(FileAt("cold-cycle.txt")) ? int.Parse(File.ReadAllText(FileAt("cold-cycle.txt"))) : 0;
                if (requested != cycle)
                {
                    Check(requested == cycle + 1, "invalid cold transition"); cycle = requested; phase = 0; resumed = false;
                    Mp.Client.Send(new ClientFreezePacket(false)); Speed(TimeSpeed.Superfast); advanceUntil = TickPatch.Timer + 200;
                }
                if (advanceUntil > TickPatch.Timer) return;
                if (phase == 0) { Speed(TimeSpeed.Paused); until = TickPatch.Timer + 30; phase = 1; return; }
                if (phase == 1)
                {
                    if (TickPatch.Timer < until) return;
                    Check(Mp.LocalServer.worldData.TryStartJoinPointCreation(true), "joinpoint rejected"); phase = 2; return;
                }
                if (phase == 2)
                {
                    if (Mp.LocalServer.worldData.CreatingJoinPoint) return;
                    if (!TickPatch.Frozen) { Mp.Client.Send(new ClientFreezePacket(true)); return; }
                    ExportReplay("cold" + cycle);
                    File.WriteAllText(FileAt("host.coldfrozen" + cycle), Snapshot()); phase = 3;
                    Log.Message("COLD HOST_FROZEN cycle=" + cycle + " " + Snapshot());
                }
            }
            int current = client ? argCycle : cycle;
            if (!File.Exists(FileAt("host.coldfrozen" + current)) || Mp.session.players.Count != 2) return;
            if (client && TickPatch.Frozen && !File.Exists(FileAt("client.coldready" + current)))
            { File.WriteAllText(FileAt("client.coldready" + current), Snapshot()); Log.Message("COLD CLIENT_FROZEN cycle=" + current + " " + Snapshot()); }
            if (!File.Exists(FileAt("client.coldready" + current))) return;
            Check(File.ReadAllText(FileAt("host.coldfrozen" + current)) == File.ReadAllText(FileAt("client.coldready" + current)), "COLD_BASELINE_LOAD_DRIFT cycle=" + current);
            if (current < 3) { File.WriteAllText(FileAt(Peer + ".coldcomplete" + current), "equal frozen maps/world/RNG/pending/flags"); return; }
            if (!client && !resumed) { resumed = true; Mp.Client.Send(new ClientFreezePacket(false)); Speed(TimeSpeed.Superfast); }
            if (!TickPatch.Frozen && StudentColdProbe.Finish() && MermeisterColdProbe.Finish() && HistoryIdsProbe.FinishCold() && client && !releaseSent) releaseSent = release.DoSync(null);
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED COLD " + e); ExportReplay("failure"); }
    }
}
