using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;

public sealed class MermeisterColdState : GameComponent
{
    public bool Prepared;
    public int DialogId, QuestCountBefore, TimeBefore;
    public Map Map;
    public ThingDef Component;
    public MermeisterColdState(Game game) { }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref Prepared, "prepared");
        Scribe_Values.Look(ref DialogId, "dialogId");
        Scribe_Values.Look(ref QuestCountBefore, "questCountBefore");
        Scribe_Values.Look(ref TimeBefore, "timeBefore");
        Scribe_References.Look(ref Map, "map");
        Scribe_Defs.Look(ref Component, "component");
    }
}

internal static class MermeisterColdProbe
{
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static bool Enabled => !GenCommandLine.CommandLineArgPassed("studentcoldonly");
    static ISyncMethod prepare;
    static bool requested, selected;
    static float deadline;
    static MermeisterColdState S => Current.Game.GetComponent<MermeisterColdState>();
    static object GameComp => Current.Game.components.Single(c => c.GetType().FullName == "MilianModification.MiliraGameComponent_MilianComponentRecipe");
    static object Get(object value, string name) => AccessTools.Field(value.GetType(), name).GetValue(value);
    static void Set(object value, string name, object field) => AccessTools.Field(value.GetType(), name).SetValue(value, field);
    static int ResearchTime => (int)Get(GameComp, "MermeisterLastFriendlyResearchAssistanceTime");
    static int QuestCount => Find.QuestManager.QuestsListForReading.Count(q => q.root?.defName == "Milira_Mermeister_FriendlyResearchAssistance");
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static DiaNode Node(PersistentDialog d) => (DiaNode)AccessTools.Field(typeof(Dialog_NodeTree), "curNode").GetValue(d.Dialog);
    static PersistentDialog Dialog
    {
        get
        {
            if (S.Map == null) return null;
            foreach (var dialog in S.Map.MpComp().mapDialogs) if (dialog.id == S.DialogId) return dialog;
            return null;
        }
    }
    internal static void Init() => prepare = MP.RegisterSyncMethod(typeof(MermeisterColdProbe), nameof(Prepare)).SetContext(SyncContext.CurrentMap);
    public static void Prepare()
    {
        Check(!S.Prepared, "duplicate Mermeister fixture");
        S.Map = Find.CurrentMap;
        Set(GameComp, "MermeisterCommAvaliable", true);
        Set(GameComp, "MermeisterLeftChurch", false);
        Set(GameComp, "MermeisterFamiliarityPoint", 11f);
        Set(GameComp, "MermeisterLastFriendlyResearchAssistanceTime", -2000000);
        S.QuestCountBefore = QuestCount; S.TimeBefore = ResearchTime;
        // Native public factory under a map-context sync command. This focused
        // test covers persistence/quest execution, not the preceding comms job.
        var option = (DiaOption)AccessTools.Method(AccessTools.TypeByName("MilianModification.MermeisterDialogUtility"), "RequestResearchAssistance", new[] { typeof(Map) }).Invoke(null, new object[] { S.Map });
        Check(option.link != null && option.link.options[0].action?.Target?.GetType().Name == "SavedResearch", "native Mermeister research option absent");
        S.Component = (ThingDef)Get(option.link.options[0].action.Target, "component");
        var window = new Dialog_NodeTree(option.link, false, false, "Mermeister cold fixture");
        Find.WindowStack.Add(window);
        var persistent = PersistentDialog.FindDialog(window);
        Check(persistent != null, "native Mermeister dialog not captured by MP map context");
        S.DialogId = persistent.id; S.Prepared = true;
        Log.Message("MERMEISTER_COLD PREPARED " + Snapshot());
    }
    internal static bool Ready()
    {
        if (!Enabled) return true;
        if (S.Prepared) return true;
        if (deadline == 0) deadline = Time.realtimeSinceStartup + 120;
        Check(Time.realtimeSinceStartup < deadline, "Mermeister fixture preparation timeout");
        if (client && !requested)
        {
            Current.Game.CurrentMap = Current.Game.GetComponent<MiliraColdState>().Launcher.Map;
            requested = prepare.DoSync(null);
        }
        return false;
    }
    internal static string Snapshot()
    {
        if (!Enabled) return "mermeisterFactoryTest=notRequested";
        var dialog = Dialog;
        Check(dialog != null, "Mermeister persistent dialog missing");
        return "mermeisterDialog=" + S.DialogId + ":" + dialog.ver + ":" + S.Component.defName + ":" + Node(dialog).options.Count;
    }
    internal static bool Finish()
    {
        if (!Enabled) return true;
        var dialog = Dialog;
        Check(dialog != null, "Mermeister dialog lost before post-cold choice");
        if (dialog.ver == 0)
        {
            if (client && !selected)
            {
                selected = true;
                AccessTools.Method(typeof(DiaOption), "Activate", Type.EmptyTypes).Invoke(Node(dialog).options[0], null);
                Check(QuestCount == S.QuestCountBefore && ResearchTime == S.TimeBefore && dialog.ver == 0, "Mermeister choice mutated before synchronization");
                Log.Message("MERMEISTER_COLD native research choice queued");
            }
            return false;
        }
        Check(dialog.ver == 1 && QuestCount == S.QuestCountBefore + 1 && ResearchTime > S.TimeBefore, "Mermeister native quest/cooldown result missing");
        Check(Node(dialog).text.ToString().Contains(S.Component.LabelCap.ToString()), "Mermeister restored response text incorrect");
        string receipt = S.Component.defName + ":" + QuestCount + ":" + ResearchTime;
        string root = Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
        string peer = client ? "client" : "host";
        string path = Path.Combine(root, peer + ".mermeistercold");
        if (!File.Exists(path)) { File.WriteAllText(path, receipt); Log.Message("MERMEISTER_COLD ASSERT " + receipt); }
        string other = Path.Combine(root, (client ? "host" : "client") + ".mermeistercold");
        if (!File.Exists(other)) return false;
        Check(File.ReadAllText(other) == receipt, "Mermeister post-cold peer mismatch");
        return true;
    }
    internal static void Cleanup()
    {
        if (!Enabled) return;
        var dialog = Dialog;
        Check(dialog != null && dialog.ver == 1, "Mermeister fixture cleanup before assertion");
        dialog.Dialog.Close(false);
        dialog.map.MpComp().mapDialogs.Remove(dialog);
    }
}
