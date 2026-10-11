using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

internal static class TechnologyProbe
{
    static bool client, requested, prepared, sent, advance, finished, failed, drawing;
    static int round, start;
    static float deadline;
    static Def selected;
    static Pawn pawn;
    static Type defType, handler, faction, church, game;
    static ISyncMethod prepare;
    static Window fixture;
    static string before;
    static bool IsChurch => round >= 3;
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static Type Type(string name) => AccessTools.TypeByName("PLAMilira." + name);
    static object Instance(Type type) => AccessTools.Property(type, "Instance").GetValue(null);
    static object Get(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
    internal static void Init(Harmony h)
    {
        client = GenCommandLine.CommandLineArgPassed("twclient");
        defType = Type("PLAMiliraFactionTecDef"); handler = Type("PLAMilira_Tech_Handler"); faction = Type("PLAMilira_Faction_Handler");
        church = Type("PLAMilira_Church_Faction_Handler"); game = Type("PLAMilira_GameComponent");
        prepare = MP.RegisterSyncMethod(typeof(TechnologyProbe), nameof(Prepare));
        var button = AccessTools.GetDeclaredMethods(typeof(Widgets)).Single(m => m.Name == "ButtonText" && m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(Rect));
        h.Patch(button, prefix: new HarmonyMethod(typeof(TechnologyProbe), nameof(Button)));
    }
    static bool Button(ref bool __result)
    {
        if (!drawing || Event.current.type != EventType.Repaint) return true;
        __result = true; return false;
    }
    public static void Prepare(int n)
    {
        var worldRandBefore = Mp.AsyncWorldTime.randState;
        var homeMap = Find.Maps.First(m => m.IsPlayerHome);
        var mapRandBefore = homeMap.AsyncTime().randState;
        round = n; sent = advance = false;
        if (pawn == null)
        {
            pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(homeMap.Center, homeMap, 15), homeMap);
        }
        var defs = ((IEnumerable)AccessTools.Property(typeof(DefDatabase<>).MakeGenericType(defType), "AllDefsListForReading").GetValue(null)).Cast<Def>()
            .Where(d => AccessTools.Field(handler, (string)Get(d, "techName"))?.FieldType == typeof(bool)).OrderBy(d => d.defName).ToArray();
        Require(defs.Length >= 6, "six native technology defs missing"); selected = defs[n];
        Set(Instance(handler), (string)Get(selected, "techName"), false);
        Set(Instance(faction), "reinforcements", 5); Set(Instance(faction), "threatPoints", 200); Set(Instance(church), "tecPoint", 500);
        Set(Instance(game), "isUnlocking", false); Set(Instance(game), "unlockTecDef", null);
        before = State(); start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; prepared = true;
        Log.Message("TECH PREPARE round=" + round + " " + before + ";pawn=" + pawn.ThingID +
            ";worldRand=" + worldRandBefore + ">" + Mp.AsyncWorldTime.randState +
            ";mapRand=" + mapRandBefore + ">" + homeMap.AsyncTime().randState);
    }
    static string State() => selected.defName + ":unlocked=" + Get(Instance(handler), (string)Get(selected, "techName")) +
        ":reinforcements=" + Get(Instance(faction), "reinforcements") + ":points=" + Get(Instance(church), "tecPoint") +
        ":unlocking=" + Get(Instance(game), "isUnlocking") + ":target=" + ((Def)Get(Instance(game), "unlockTecDef"))?.defName;
    sealed class NativePurchaseWindow : Window
    {
        readonly object native = Activator.CreateInstance(Type(IsChurch ? "Dialog_PLAMilira_Church_TECUP_Window" : "Dialog_PLAMilira_TECUP_Window"), pawn);
        public override Vector2 InitialSize => new Vector2(800, 240);
        public NativePurchaseWindow() { forcePause = false; }
        public override void DoWindowContents(Rect rect)
        {
            if (sent || Event.current.type != EventType.Repaint) return;
            drawing = true;
            try
            {
                var worldRandBefore = Mp.AsyncWorldTime.randState;
                var mapRandBefore = pawn.Map.AsyncTime().randState;
                var method = AccessTools.Method(native.GetType(), "DrawBottomInfo");
                // Two clicks before command delivery must still spend the cost only once.
                method.Invoke(native, new object[] { rect, selected });
                method.Invoke(native, new object[] { rect, selected });
                Require(Mp.AsyncWorldTime.randState == worldRandBefore && pawn.Map.AsyncTime().randState == mapRandBefore,
                    "technology client-only UI consumed simulation Rand");
                Require(State() == before, "technology changed locally before command execution"); sent = true;
                Log.Message("TECH DISPATCH round=" + round + " native purchase button twice; local rollback=True;simulationRandUnchanged=True");
            }
            catch (Exception e) { failed = true; Log.Error("COMBINED FAILED TECH draw " + e); }
            finally { drawing = false; }
        }
    }
    internal static bool Update()
    {
        if (finished) return true;
        if (failed) return false;
        try
        {
            Require(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            if (client && !sent && fixture == null && TickPatch.Timer - start > 60) { fixture = new NativePurchaseWindow(); Find.WindowStack.Add(fixture); }
            if (client && sent && fixture != null) { fixture.Close(false); fixture = null; }
            Require(Time.realtimeSinceStartup < deadline, "technology timeout");
            if (TickPatch.Timer - start < 400) return false;
            if (IsChurch)
            {
                Require((bool)Get(Instance(game), "isUnlocking") && Get(Instance(game), "unlockTecDef") == selected, "native church progress missing");
                Require((int)Get(Instance(church), "tecPoint") == 500 - (int)Get(selected, "tecPoint"), "church cost not exactly once: " + State());
            }
            else
            {
                Require((bool)Get(Instance(handler), (string)Get(selected, "techName")), "native technology not unlocked");
                Require((int)Get(Instance(faction), "reinforcements") == 5 - (int)Get(selected, "reinforcements"), "reinforcements cost not exactly once: " + State());
            }
            var path = Path.Combine(Root, Peer + ".technology" + round); var other = Path.Combine(Root, (client ? "host" : "client") + ".technology" + round);
            if (!File.Exists(path)) { File.WriteAllText(path, State()); Log.Message("TECH ASSERT round=" + round + " " + State()); }
            if (!File.Exists(other)) return false;
            Require(File.ReadAllText(path) == File.ReadAllText(other), "technology peer mismatch");
            if (round == 5) { finished = true; Log.Message("TECH ACTIONS_PASS normal3 church3"); return true; }
            if (client && !advance) { advance = true; prepare.DoSync(null, round + 1); }
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED TECH " + e); }
        return false;
    }
}
