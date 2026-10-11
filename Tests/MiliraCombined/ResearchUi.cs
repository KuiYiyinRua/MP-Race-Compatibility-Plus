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

// Exercise the native research-tab render methods, including their real button
// branches, while the non-host peer is in MP's interface context.
internal static class ResearchUiProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static readonly string Root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    static Type tabType, gcType, recordType;
    static ISyncMethod prepare;
    static IDictionary saved, history;
    static HashSet<string> baselineKeys;
    static object tab, historyRecord;
    static string savedKey;
    const string HistoryKey = "milira-research-ui-probe";
    static NativeWindow window;
    static int stage, start, frames;
    static float deadline;
    static bool requested, prepared, sent, advance, clicked, drawing, done, failed;

    static void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    static object GC => Current.Game.components.Single(c => c.GetType() == gcType);
    static IDictionary Dictionary(string name) => (IDictionary)AccessTools.Field(gcType, name).GetValue(GC);
    static string Receipt()
    {
        var extras = saved.Keys.Cast<string>().Where(k => !baselineKeys.Contains(k)).OrderBy(k => k).ToArray();
        var details = extras.Select(k => k + ":" + string.Join(",", ((IEnumerable)saved[k]).Cast<ThingDefCount>()
            .Select(c => c.ThingDef.defName + "=" + c.Count)));
        return "stage=" + stage + ";saved=" + string.Join("|", details) + ";historyFixture=" + history.Contains(HistoryKey) +
            ";minSkill=" + AccessTools.Field(gcType, "minResearchSkill").GetValue(GC);
    }
    internal static void Init(Harmony harmony)
    {
        tabType = AccessTools.TypeByName("MilianModification.ITab_Pawn_MilianComponentResearch");
        gcType = AccessTools.TypeByName("MilianModification.MiliraGameComponent_MilianComponentRecipe");
        recordType = AccessTools.TypeByName("MilianModification.ResearchHistoryRecord");
        Check(tabType != null && gcType != null && recordType != null, "research UI native types missing");
        foreach (var name in new[] { "DrawFunctionKeys", "DrawStoredResearchBill", "DrawHistoryRecord" })
            Check(AccessTools.DeclaredMethod(tabType, name) != null, "research UI native method missing: " + name);
        var button = AccessTools.Method(typeof(Widgets), "ButtonImage", new[] { typeof(Rect), typeof(Texture2D), typeof(bool), typeof(string) });
        Check(button != null, "native image button missing");
        harmony.Patch(button, prefix: new HarmonyMethod(typeof(ResearchUiProbe), nameof(ImageButton)));
        var withBg = AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m => m.Name == "ButtonImageWithBG" &&
            m.ReturnType == typeof(bool) && m.GetParameters().Length >= 2 &&
            m.GetParameters()[0].ParameterType == typeof(Rect) && m.GetParameters()[1].ParameterType == typeof(Texture2D)).ToArray();
        Check(withBg.Length > 0, "native history image button missing");
        foreach (var method in withBg) harmony.Patch(method, prefix: new HarmonyMethod(typeof(ResearchUiProbe), nameof(ImageButton)));
        var numeric = AccessTools.GetDeclaredMethods(typeof(Widgets)).Where(m => m.Name == "TextFieldNumeric" && m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 5 && m.GetParameters()[0].ParameterType == typeof(Rect)).ToArray();
        Check(numeric.Length == 1, "native numeric input overload changed: " + numeric.Length);
        harmony.Patch(numeric[0].MakeGenericMethod(typeof(int)), prefix: new HarmonyMethod(typeof(ResearchUiProbe), nameof(NumericInput)));
        prepare = MP.RegisterSyncMethod(typeof(ResearchUiProbe), nameof(Prepare));
    }
    static bool ImageButton(Texture2D __1, ref bool __result)
    {
        if (!drawing || stage == 3 || Event.current.type != EventType.Repaint) return true;
        var wanted = stage == 0 ? TexButton.Paste : stage == 1 ? TexButton.Delete :
            (Texture2D)AccessTools.Field(AccessTools.TypeByName("AncotLibrary.AncotLibraryIcon"), "Dismiss_Transparent").GetValue(null);
        __result = !clicked && __1 == wanted;
        if (__result) clicked = true;
        return false;
    }
    static bool NumericInput(ref int __1, ref string __2)
    {
        if (!drawing || stage != 3 || Event.current.type != EventType.Repaint) return true;
        __1 = 11; __2 = "11"; clicked = true;
        return false;
    }
    public static void Prepare(int next)
    {
        stage = next; clicked = sent = advance = false; frames = 0;
        saved = Dictionary("researchBillSaved"); history = Dictionary("researchHistoryRecord");
        if (next == 0)
        {
            baselineKeys = new HashSet<string>(saved.Keys.Cast<string>());
            tab = Activator.CreateInstance(tabType);
            AccessTools.Field(tabType, "num_Plasteel").SetValue(null, 9);
        }
        else if (next == 1)
        {
            var extras = saved.Keys.Cast<string>().Where(k => !baselineKeys.Contains(k)).ToArray();
            Check(extras.Length == 1, "save did not create exactly one preset");
            savedKey = extras[0];
        }
        else if (next == 2)
        {
            Check(!history.Contains(HistoryKey), "history fixture key already present");
            historyRecord = Activator.CreateInstance(recordType);
            var pawn = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).FirstOrDefault();
            Check(pawn != null, "no pawn for history fixture");
            AccessTools.Field(recordType, "pawn").SetValue(historyRecord, pawn);
            AccessTools.Field(recordType, "label").SetValue(historyRecord, "Research UI probe");
            AccessTools.Field(recordType, "desc").SetValue(historyRecord, "Client delete test");
            AccessTools.Field(recordType, "thingDefCounts").SetValue(historyRecord, new List<ThingDefCount> { new ThingDefCount(ThingDefOf.Plasteel, 9) });
            history.Add(HistoryKey, historyRecord);
        }
        else if (next == 3)
            AccessTools.Field(gcType, "minResearchSkill").SetValue(GC, 5);
        start = TickPatch.Timer; deadline = Time.realtimeSinceStartup + 120; prepared = true;
        Log.Message("RESEARCH_UI PREPARE " + Receipt());
    }
    sealed class NativeWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(700, 150);
        public NativeWindow() { forcePause = false; }
        public override void DoWindowContents(Rect rect)
        {
            if (Event.current.type != EventType.Repaint || sent) return;
            drawing = true;
            try
            {
                string before = Receipt();
                if (stage == 0)
                    AccessTools.Method(tabType, "DrawFunctionKeys").Invoke(tab, new object[] { new Rect(0, 0, 70, 30) });
                else if (stage == 1)
                    AccessTools.Method(tabType, "DrawStoredResearchBill").Invoke(tab, new object[] { new Rect(0, 0, 180, 40), savedKey });
                else if (stage == 2)
                    AccessTools.Method(tabType, "DrawHistoryRecord").Invoke(tab, new object[] { new Rect(0, 0, 180, 60), historyRecord, HistoryKey });
                else
                    AccessTools.Method(tabType, "DrawSkillRequireSetting").Invoke(tab, new object[] { new Rect(0, 0, 180, 30) });
                Check(clicked, "native research button was not invoked");
                Check(Receipt() == before, "research UI mutated local MP state before command execution");
                sent = true; frames++;
                Log.Message("RESEARCH_UI DISPATCH stage=" + stage + " native button rollback=True");
            }
            catch (Exception e) { failed = true; Log.Error("COMBINED FAILED RESEARCH_UI draw " + e); }
            finally { drawing = false; }
        }
    }
    internal static bool Update()
    {
        if (done) return true;
        if (failed) return false;
        try
        {
            Check(!Mp.session.desynced && Mp.session.players.Count == 2, "network health");
            if (!prepared) { if (client && !requested) requested = prepare.DoSync(null, 0); return false; }
            if (client && !sent && TickPatch.Timer - start > 60 && window == null)
            {
                window = new NativeWindow(); Find.WindowStack.Add(window);
            }
            Check(Time.realtimeSinceStartup < deadline, "research UI timeout");
            if (TickPatch.Timer - start < 240) return false;
            Check(!client || (sent && frames == 1), "native research button not exercised exactly once");
            var extras = saved.Keys.Cast<string>().Where(k => !baselineKeys.Contains(k)).ToArray();
            Check(extras.Length == (stage == 0 ? 1 : 0), "preset count after sync");
            if (stage == 0)
            {
                var bill = ((IEnumerable)saved[extras[0]]).Cast<ThingDefCount>().ToArray();
                Check(bill.Any(c => c.ThingDef == ThingDefOf.Plasteel && c.Count == 9), "saved preset lost material count");
            }
            Check(!history.Contains(HistoryKey), "history deletion outcome");
            if (stage == 3) Check((int)AccessTools.Field(gcType, "minResearchSkill").GetValue(GC) == 11, "numeric skill input not synchronized");
            var path = Path.Combine(Root, Peer + ".researchui" + stage);
            var other = Path.Combine(Root, (client ? "host" : "client") + ".researchui" + stage);
            if (!File.Exists(path)) { File.WriteAllText(path, Receipt()); Log.Message("RESEARCH_UI ASSERT " + Receipt()); }
            if (!File.Exists(other)) return false;
            Check(File.ReadAllText(path) == File.ReadAllText(other), "research UI peer receipt mismatch");
            if (client && window != null) { window.Close(false); window = null; }
            if (stage == 3) { done = true; Log.Message("RESEARCH_UI ACTIONS_PASS native save/delete/history/min-skill"); return true; }
            if (client && !advance) { advance = true; prepare.DoSync(null, stage + 1); }
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED RESEARCH_UI " + e); }
        return false;
    }
}
