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

internal static class TransactionProbe
{
    static bool client, requested, prepared, sent, advance, done, failed, drawing, clicked;
    static int stage, start, frames;
    static float deadline;
    static Pawn pawn;
    static ThingComp comp;
    static Def component;
    static object[] slots;
    static object tab, column;
    static Window fixture;
    static string baseline;
    static ISyncMethod prepare;
    static readonly string[] flags = { "autoReplenish", "autoUseModifyAbility", "autoUseModifyAbility_Consumptive" };
    static readonly string[] columns = { "AutoReplenishMilian", "AutoUseModifyAbility", "AutoUseModifyAbilityConsumptive" };
    static int Phase => stage % 9;
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static object Get(object o, string name) => AccessTools.Field(o.GetType(), name).GetValue(o);
    static void Set(object o, string name, object value) => AccessTools.Field(o.GetType(), name).SetValue(o, value);
    static Type Type(string name) => AccessTools.TypeByName("MilianModification." + name);
    static IDictionary Recipe => (IDictionary)Get(comp, "componentSlotInRecipe");
    static IDictionary Uninstall => (IDictionary)Get(comp, "slotUninstallRecipe");
    static IDictionary Installed => (IDictionary)Get(comp, "componentSlotInstalled");
    static void Check(bool v, string reason) { if (!v) throw new Exception(reason); }
    static string Dict(IDictionary d) => string.Join(",", d.Keys.Cast<object>().OrderBy(Convert.ToInt32).Select(k => Convert.ToInt32(k) + ":" + ((Def)d[k]).defName));
    static string State() => "recipe=" + Dict(Recipe) + ";uninstall=" + Dict(Uninstall) + ";installed=" + Dict(Installed) + ";flags=" + string.Join(",", flags.Select(f => Get(comp, f)));
    internal static void Init(Harmony h)
    {
        client = GenCommandLine.CommandLineArgPassed("twclient");
        prepare = MP.RegisterSyncMethod(typeof(TransactionProbe), nameof(Prepare));
        foreach (var name in new[] { "ButtonImage", "ButtonInvisible", "ButtonText" })
        {
            var m = AccessTools.GetDeclaredMethods(typeof(Widgets)).Single(x => x.Name == name && x.GetParameters()[0].ParameterType == typeof(Rect) && x.GetParameters().Length == (name == "ButtonImage" ? 4 : name == "ButtonText" ? 6 : 2));
            h.Patch(m, prefix: new HarmonyMethod(typeof(TransactionProbe), nameof(Button)));
        }
    }
    static bool Button(Rect __0, ref bool __result)
    {
        if (!drawing || Event.current.type != EventType.Repaint) return true;
        __result = Phase != 8 && !clicked && (Phase != 1 || Mathf.Abs(__0.x - 30 * Convert.ToInt32(slots[0])) < 0.1f);
        if (__result) clicked = true;
        return false;
    }
    public static void Prepare(int n)
    {
        stage = n; sent = advance = clicked = false; frames = 0;
        if (pawn == null)
        {
            var map = Find.Maps.First(m => m.IsPlayerHome);
            pawn = PawnGenerator.GeneratePawn(PawnKindDef.Named("Milian_Mechanoid_BishopII"), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 12), map);
            pawn.drafter.Drafted = true;
            comp = pawn.AllComps.Single(c => c.GetType() == Type("CompModification"));
            component = DefDatabase<ThingDef>.AllDefs.Where(d => d.GetType() == Type("MilianComponentDef"))
                .OrderBy(d => d.defName).First(d => ((IList)Get(Get(d, "milianComponent"), "possibleSlots")).Count > 0);
            slots = ((IList)Get(Get(component, "milianComponent"), "possibleSlots")).Cast<object>().ToArray();
            tab = Activator.CreateInstance(Type("ITab_Pawn_MilianModification"));
            column = Activator.CreateInstance(Type("PawnColumnWorker_MilianComponent"));
        }
        Recipe.Clear(); Uninstall.Clear(); Installed.Clear();
        foreach (var slot in slots)
        {
            Installed.Add(slot, component);
            if (Phase < 2) Recipe.Add(slot, component);
            if (Phase == 3) Uninstall.Add(slot, component);
        }
        foreach (var f in flags) Set(comp, f, Phase == 7);
        baseline = State(); start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; prepared = true;
        Log.Message("TRANSACTION PREPARE stage=" + stage + " pawn=" + pawn.ThingID + " " + baseline);
    }
    sealed class NativeWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(800, 150);
        public NativeWindow() { forcePause = false; }
        public override void DoWindowContents(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            drawing = true;
            try
            {
                var old = State();
                if (Phase == 1 && !sent)
                {
                    AccessTools.Method(column.GetType(), "DoCell").Invoke(column, new object[] { new Rect(0, 0, 740, 30), pawn, null });
                    var menu = Find.WindowStack.Windows.OfType<FloatMenu>().Last();
                    var options = (System.Collections.Generic.List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu);
                    options[0].action(); menu.Close(false);
                }
                else if (Phase != 1)
                    AccessTools.Method(tab.GetType(), Phase == 0 ? "ButtonCancelInstall" : "ButtonUninstall").Invoke(tab, new object[] { new Rect(0, 0, 200, 30), slots[0] });
                Check(State() == old, "premature local UI mutation");
                frames++;
                if (!sent) { sent = true; Log.Message("TRANSACTION DISPATCH stage=" + stage + " native UI, immediate state unchanged"); }
            }
            catch (Exception e) { Fail(e); }
            finally { drawing = false; }
        }
    }
    static void Fail(Exception e) { failed = true; Log.Error("COMBINED FAILED TRANSACTION " + e); }
    internal static bool Update()
    {
        if (done) return true;
        if (failed) return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            if (client && !sent && TickPatch.Timer - start > 60)
            {
                Current.Game.CurrentMap = pawn.Map; Find.Selector.ClearSelection(); Find.Selector.Select(pawn);
                if (Phase >= 4 && Phase <= 7)
                {
                    foreach (int i in Phase == 7 ? new[] { 0, 1, 2 } : new[] { Phase - 4 })
                    {
                        var type = Type("PawnColumnWorker_" + columns[i]);
                        AccessTools.DeclaredMethod(type, "SetValue").Invoke(Activator.CreateInstance(type), new object[] { pawn, Phase != 7, null });
                    }
                    Check(State() == baseline, "checkbox mutated locally"); sent = true;
                    Log.Message("TRANSACTION DISPATCH stage=" + stage + " native setter, immediate state unchanged");
                }
                else if (fixture == null) { fixture = new NativeWindow(); Find.WindowStack.Add(fixture); }
            }
            Check(Time.realtimeSinceStartup < deadline, "transaction timeout");
            if (TickPatch.Timer - start < 260) return false;
            Check(Recipe.Count == 0, "cancel recipe failed: " + State());
            Check(Uninstall.Count == (Phase == 2 ? (((bool)Get(Get(component, "milianComponent"), "occupyAllPossibleSlots")) ? slots.Length : 1) : 0), "uninstall outcome: " + State());
            Check((bool)Get(comp, flags[0]) == (Phase == 4) && (bool)Get(comp, flags[1]) == (Phase == 5 || Phase == 6) && (bool)Get(comp, flags[2]) == (Phase == 6), "checkbox outcome: " + State());
            if (client && fixture != null) { Check(frames > 2 && (Phase == 8 || clicked), "UI frames/click missing"); fixture.Close(false); fixture = null; }
            var path = Path.Combine(Root, Peer + ".transaction" + stage); var other = Path.Combine(Root, (client ? "host" : "client") + ".transaction" + stage);
            if (!File.Exists(path)) { File.WriteAllText(path, State()); Log.Message("TRANSACTION ASSERT stage=" + stage + " " + State()); }
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(path) == File.ReadAllText(other), "peer mismatch");
            if (stage == 26) { done = true; Log.Message("TRANSACTION ACTIONS_PASS stages=27"); return true; }
            if (client && !advance) { advance = true; prepare.DoSync(null, stage + 1); }
        }
        catch (Exception e) { Fail(e); }
        return false;
    }
}
