using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

internal static class StudentColdProbe
{
    static bool requested;
    static int clickSent = -1;
    static float deadline;
    static float nextDiagnostic;
    static readonly bool client = GenCommandLine.CommandLineArgPassed("twclient");
    static MiliraColdState S => Current.Game.GetComponent<MiliraColdState>();
    static object GameComp => AccessTools.Property(AccessTools.TypeByName("PLAMilira.PLAMilira_GameComponent"), "Instance").GetValue(null);
    static object Student => AccessTools.Field(GameComp.GetType(), "student").GetValue(GameComp);
    static DiaNode Node(PersistentDialog d) => (DiaNode)AccessTools.Field(typeof(Dialog_NodeTree), "curNode").GetValue(d.Dialog);
    static bool Matches(PersistentDialog d) => Node(d).options.Any(o =>
        o.linkLateBind?.Target?.GetType().FullName == "MP_MeowOnlineShop.MiliraAddonCompat.PLAMilira_DialogCallbacks+SavedLink" &&
        ((Hediff)AccessTools.Field(o.linkLateBind.Target.GetType(), "owner").GetValue(o.linkLateBind.Target)).pawn == S.Student);
    static PersistentDialog Dialog
    {
        get
        {
            PersistentDialog found = null;
            foreach (var candidate in S.Student.Map.MpComp().mapDialogs)
            {
                if (!Matches(candidate)) continue;
                Check(found == null, "duplicate student persistent dialogue");
                found = candidate;
            }
            return found;
        }
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    internal static void Init(Harmony harmony)
    {
        var method = AccessTools.Method(AccessTools.TypeByName("PLAMilira.PLAMiliraDialogUtility"), "OpenPLAMiliraDialog", new[] { typeof(Pawn), typeof(Pawn) });
        Check(method != null, "student native dialogue method missing");
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(StudentColdProbe), nameof(Opened)));
    }

    static void Opened(Pawn __0, Pawn __1)
    {
        if (S?.Active != true || __0 != S.Student) return;
        Log.Message("STUDENT_COLD NATIVE_OPEN target=" + __0.ThingID + " speaker=" + __1.ThingID +
            " mapContext=" + Multiplayer.Client.Multiplayer.MapContext?.uniqueID + " " + JobState());
    }

    internal static void Prepare(Map map)
    {
        S.Speaker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
        GenSpawn.Spawn(S.Speaker, CellFinder.RandomClosewalkCellNear(S.Student.Position, map, 2), map);
        AccessTools.Field(GameComp.GetType(), "student").SetValue(GameComp, null);
    }

    internal static bool Ready()
    {
        if (Dialog != null) return true;
        if (deadline == 0) deadline = Time.realtimeSinceStartup + 120;
        Check(Time.realtimeSinceStartup < deadline, "student native job did not create persistent dialogue; " + JobState());
        if (Time.realtimeSinceStartup >= nextDiagnostic)
        {
            nextDiagnostic = Time.realtimeSinceStartup + 20;
            Log.Message("STUDENT_COLD WAIT " + JobState());
        }
        if (client && !requested)
        {
            requested = true;
            var def = DefDatabase<JobDef>.AllDefs.Single(d => d.driverClass?.FullName == "PLAMilira.JobDriver_PLAMiliraDialog");
            var job = JobMaker.MakeJob(def, S.Student);
            S.Speaker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            Log.Message("STUDENT_COLD native ordered dialogue job queued");
        }
        return false;
    }

    static string JobState() => "speaker=" + S.Speaker?.ThingID + ":job=" + S.Speaker?.CurJob?.def?.defName +
        ":A=" + S.Speaker?.CurJob?.targetA + ":B=" + S.Speaker?.CurJob?.targetB + ":pos=" + S.Speaker?.Position +
        ":downed=" + S.Speaker?.Downed + ":mental=" + S.Speaker?.InMentalState +
        ";student=" + S.Student?.ThingID + ":job=" + S.Student?.CurJob?.def?.defName + ":pos=" + S.Student?.Position +
        ":downed=" + S.Student?.Downed + ":mental=" + S.Student?.InMentalState +
        ";dialogs=" + S.Student?.Map?.MpComp().mapDialogs.Count;

    internal static string Snapshot()
    {
        var dialog = Dialog;
        Check(dialog != null, "student persistent dialogue missing after load");
        return "dialog=" + dialog.id + ":" + dialog.ver + ":" + string.Join(",", Node(dialog).options.Select(o => o.linkLateBind?.Target?.GetType().FullName));
    }

    internal static bool Finish()
    {
        var dialog = Dialog;
        Check(dialog != null, "student persistent dialogue lost before selection");
        if (dialog.ver >= 3)
        {
            Check(ReferenceEquals(Student, S.Student), "native dialogue callback did not set its owner as student");
            return true;
        }
        if (client && clickSent != dialog.ver)
        {
            clickSent = dialog.ver;
            object previousStudent = Student;
            AccessTools.Method(typeof(DiaOption), "Activate", Type.EmptyTypes).Invoke(Node(dialog).options[0], null);
            Check(dialog.ver == clickSent && ReferenceEquals(Student, previousStudent), "dialogue option mutated before synchronized execution");
            Log.Message("STUDENT_COLD native option queued version=" + clickSent);
        }
        return false;
    }

    internal static void Cleanup()
    {
        var dialog = Dialog;
        Check(dialog != null && dialog.ver == 3 && ReferenceEquals(Student, S.Student), "student post-cold dialogue assertions");
        dialog.Dialog.Close(false);
        dialog.map.MpComp().mapDialogs.Remove(dialog);
        Log.Message("STUDENT_COLD ASSERT three native choices after cold; owner=" + S.Student.ThingID + " speaker=" + S.Speaker.ThingID);
    }
}
