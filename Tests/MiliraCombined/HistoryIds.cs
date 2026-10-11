using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

public sealed class MiliraHistoryIdState : GameComponent
{
    public List<string> Seen = new List<string>();
    public MiliraHistoryIdState(Game game) { }
    public override void ExposeData()
    {
        Scribe_Collections.Look(ref Seen, "historyIdsSeen", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit && Seen == null) Seen = new List<string>();
    }
}

internal static class HistoryIdsProbe
{
    static ISyncMethod run;
    static MethodInfo allocate;
    static Type recordType;
    static bool requested, done, coldRequested, coldDone, failed;
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static string Root => Path.GetDirectoryName(GenFilePaths.SaveDataFolderPath);
    static string Peer => client ? "client" : "host";
    internal static void Init()
    {
        allocate = AccessTools.DeclaredMethod(AccessTools.TypeByName("MilianModification.Recipe_MilianComponentResearch"), "GenerateUniqueID", Type.EmptyTypes);
        recordType = AccessTools.TypeByName("MilianModification.ResearchHistoryRecord");
        if (allocate == null || recordType == null) throw new MissingMemberException("Native history ID fixture targets");
        run = MP.RegisterSyncMethod(typeof(HistoryIdsProbe), nameof(Run)).SetContext(SyncContext.MapSelected);
    }
    public static void Run(bool afterCold)
    {
        var gc = Current.Game.components.Single(c => c.GetType().FullName == "MilianModification.MiliraGameComponent_MilianComponentRecipe");
        var records = (IDictionary)AccessTools.Field(gc.GetType(), "researchHistoryRecord").GetValue(gc);
        var original = new List<DictionaryEntry>();
        foreach (DictionaryEntry entry in records) original.Add(entry);
        var ids = new List<string>();
        try
        {
            var seen = Current.Game.GetComponent<MiliraHistoryIdState>().Seen;
            if (afterCold && seen.Count != 12) throw new InvalidOperationException("Saved history ID baseline lost after cold rejoin");
            var worker = Activator.CreateInstance(allocate.DeclaringType);
            records.Clear();
            for (int round = 0; round < 3; round++)
            {
                string first = (string)allocate.Invoke(worker, null);
                records.Add(first, Activator.CreateInstance(recordType));
                string second = (string)allocate.Invoke(worker, null);
                records.Add(second, Activator.CreateInstance(recordType));
                records.Remove(first);
                string third = (string)allocate.Invoke(worker, null);
                Log.Message("HISTORY_IDS native allocate/delete round=" + round + " ids=" + first + "," + second + "," + third);
                if (records.Contains(third)) throw new InvalidOperationException("Native history ID collides with retained row: " + third);
                records.Add(third, Activator.CreateInstance(recordType));
                records.Clear();
                string fourth = (string)allocate.Invoke(worker, null);
                foreach (var id in new[] { first, second, third, fourth })
                {
                    if (ids.Contains(id) || seen.Contains(id)) throw new InvalidOperationException("Native history ID reused after deletion or cold rejoin: " + id);
                    ids.Add(id);
                }
            }
            seen.AddRange(ids);
            File.WriteAllText(Path.Combine(Root, Peer + Suffix(afterCold)), string.Join(";", seen));
            if (afterCold) coldDone = true; else done = true;
            Log.Message("HISTORY_IDS ASSERT afterCold=" + afterCold + " unique=" + seen.Count + " three same-tick deletion cycles");
        }
        catch (Exception e) { failed = true; Log.Error("COMBINED FAILED HISTORY_IDS " + e); }
        finally
        {
            records.Clear();
            foreach (var entry in original) records.Add(entry.Key, entry.Value);
        }
    }
    static string Suffix(bool afterCold) => afterCold ? ".historyids-aftercold" : ".historyids";
    internal static bool FinishCold() => (MiliraActionsProbe.TraceDiagnostic && !MiliraActionsProbe.TraceHistory) || GenCommandLine.CommandLineArgPassed("studentcoldonly") || Update(true);
    internal static bool Update(bool afterCold = false)
    {
        if (failed) return false;
        if (client && !(afterCold ? coldRequested : requested))
        {
            bool sent = run.DoSync(null, afterCold);
            if (afterCold) coldRequested = sent; else requested = sent;
        }
        if (!(afterCold ? coldDone : done)) return false;
        string other = Path.Combine(Root, (client ? "host" : "client") + Suffix(afterCold));
        if (!File.Exists(other)) return false;
        if (File.ReadAllText(other) != File.ReadAllText(Path.Combine(Root, Peer + Suffix(afterCold))))
        { failed = true; Log.Error("COMBINED FAILED HISTORY_IDS peer ID mismatch"); return false; }
        return true;
    }
}
