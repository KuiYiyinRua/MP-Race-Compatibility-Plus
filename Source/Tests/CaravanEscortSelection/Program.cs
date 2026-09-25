// Offline boundary regression, NOT a RimWorld gameplay or multiplayer smoke.
// Runs the production Harmony patch against small stand-ins for the verified
// RW 1.6 Notify_TransferablesChanged / MP 0.11.5 session call graph.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client;
using MP_MeowOnlineShop;
using RimWorld;
using Verse;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAIL " + label);
        checks++;
        Console.WriteLine("PASS " + label);
    }

    private static void Main()
    {
        var oldHost = new CaravanFormingSession();
        var oldClient = new CaravanFormingSession();
        oldClient.OpenWindow();
        Check(oldClient.state.Escort != oldHost.state.Escort,
            "unpatched issuing-only OpenWindow reproduces asymmetric escort selection");

        var harmony = new Harmony("meow.tests.caravan-escort");
        Patch_CaravanEscortSelectionMp.Apply(harmony);
        var host = new CaravanFormingSession();
        var client = new CaravanFormingSession();
        // Also exercises the case of a local OpenWindow called from a sync
        // command. MP.InInterface is false here; it is still NOT shared work.
        client.OpenWindow();
        Check(host.state.Escort == 0 && client.state.Escort == 0,
            "issuing-only OpenWindow does not change shared escort count in command context");
        MP.InInterface = true;
        client.OpenWindow();
        client.OpenWindow();
        Check(client.state.Escort == 0, "reopen and ordinary interface refresh remain read-only");
        MP.InInterface = false;
        host.AddItems(); client.AddItems();
        Check(host.state.Escort == 1 && client.state.Escort == 1, "shared session initialization reconciles both peers");
        Check(FactionContext.Current == null && FactionContext.Depth == 0, "session faction context restored");

        host.state.Owner = 0; client.state.Owner = 0;
        host.Notify_CountChanged(new Transferable()); client.Notify_CountChanged(new Transferable());
        Check(host.state.Escort == 0 && client.state.Escort == 0, "synced overseer deselection removes escort on both peers");
        host.state.Owner = 1; client.state.Owner = 1;
        host.Notify_CountChanged(new Transferable()); client.Notify_CountChanged(new Transferable());
        Check(host.state.Escort == 1 && client.state.Escort == 1, "synced overseer selection adds escort on both peers");
        host.Reset(); client.Reset();
        Check(host.state.Owner == 0 && host.state.Escort == 0 && client.state.Escort == 0, "reset preserves shared zero selection");

        foreach (string action in new[] { "TryReformCaravan", "TryFormAndSendCaravan", "DebugTryFormCaravanInstantly" })
        {
            host.state.Owner = client.state.Owner = 1;
            host.state.ManualPawn = client.state.ManualPawn = 1;
            host.state.Escort = 0; client.state.Escort = 1; // contaminated old session
            var a = host.MakeDialog(); var b = client.MakeDialog();
            typeof(Dialog_FormCaravan).GetMethod(action).Invoke(a, null);
            typeof(Dialog_FormCaravan).GetMethod(action).Invoke(b, null);
            Check(a.DepartureCount == 3 && b.DepartureCount == 3, action + " reconciles before pawn extraction");
        }
        host.state.IsEscort = false; host.state.Escort = 0;
        host.Notify_CountChanged(new Transferable());
        Check(host.state.Escort == 0 && host.state.ManualPawn == 1, "non-escort and manual selections remain untouched");

        var broken = new CaravanFormingSession();
        broken.state.ThrowOnRefresh = true;
        try { broken.AddItems(); throw new Exception("Expected refresh exception"); }
        catch (TargetInvocationException) { }
        Check(FactionContext.Depth == 0 && FactionContext.Current == null, "exception restores faction stack");
        var afterException = new CaravanFormingSession();
        afterException.OpenWindow();
        Check(afterException.state.Escort == 0, "exception restores shared-refresh permission");
        var outer = new CaravanFormingSession();
        var nested = new CaravanFormingSession();
        outer.state.DuringRefresh = nested.OpenWindow;
        outer.AddItems();
        Check(outer.state.Escort == 1 && nested.state.Escort == 0,
            "shared refresh permission does not leak to another proxy opened reentrantly");
        outer.state.Escort = 0;
        outer.state.DuringRefresh = nested.AddItems;
        outer.AddItems();
        Check(outer.state.Escort == 1 && nested.state.Escort == 1 && FactionContext.Depth == 0,
            "nested shared refresh restores parent dialog and faction scope");
        ModsConfig.BiotechActive = false;
        afterException.AddItems();
        Check(afterException.state.Escort == 0, "Biotech-disabled game remains unchanged");
        ModsConfig.BiotechActive = true;
        MP.IsInMultiplayer = false;
        afterException.OpenWindow();
        Check(afterException.state.Escort == 1, "single-player retains vanilla refresh behavior");
        MP.IsInMultiplayer = true;
        var vanilla = new Dialog_FormCaravan(new State());
        vanilla.Notify_TransferablesChanged();
        Check(vanilla.State.Escort == 1, "non-proxy dialog retains vanilla behavior");

        var getter = AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.BiotechActive));
        var generator = new DynamicMethod("Labels", typeof(void), Type.EmptyTypes).GetILGenerator();
        var label = generator.DefineLabel();
        var original = new CodeInstruction(OpCodes.Call, getter);
        original.labels.Add(label);
        original.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var rewritten = Patch_CaravanEscortSelectionMp.EscortRefreshTranspiler(new[] { original }).ToList();
        Check(rewritten.Count == 2 && rewritten[0].opcode == OpCodes.Ldarg_0 && rewritten[0].labels.Contains(label) && rewritten[0].blocks.Count == 1,
            "transpiler preserves branch labels and exception boundaries");
        foreach (int count in new[] { 0, 2 })
        {
            bool rejected = false;
            try { Patch_CaravanEscortSelectionMp.EscortRefreshTranspiler(Enumerable.Range(0, count).Select(_ => new CodeInstruction(OpCodes.Call, getter))).ToList(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "changed vanilla IL fails closed: getter count=" + count);
        }
        harmony.UnpatchAll(harmony.Id);
        Console.WriteLine("OFFLINE_BOUNDARY_PASS checks=" + checks + "; game runtime remains unverified");
    }
}

namespace Multiplayer.API
{
    public static class MP { public static bool IsInMultiplayer = true; public static bool InInterface; }
}
namespace Verse
{
    public static class ModsConfig { public static bool BiotechActive { [MethodImpl(MethodImplOptions.NoInlining)] get; set; } = true; }
    public static class Log
    {
        public static void Message(string s) => Console.WriteLine(s);
        public static void Error(string s) => throw new Exception(s);
    }
}
namespace RimWorld
{
    public class Faction { }
    public class Transferable { }
    public sealed class State
    {
        public int Owner = 1, Escort, ManualPawn = 1;
        public bool IsEscort = true, ThrowOnRefresh;
        public Action DuringRefresh;
    }
    public class Dialog_FormCaravan
    {
        public readonly State State;
        public int DepartureCount;
        public Dialog_FormCaravan(State state) { State = state; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Notify_TransferablesChanged()
        {
            if (State.ThrowOnRefresh) throw new InvalidOperationException("fixture refresh failure");
            State.DuringRefresh?.Invoke();
            // Only this getter/escort branch is replaced by the production patch.
            if (ModsConfig.BiotechActive && State.IsEscort)
                State.Escort = State.Owner > 0 ? 1 : 0;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void TryReformCaravan() { DepartureCount = State.Owner + State.Escort + State.ManualPawn; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void TryFormAndSendCaravan() { DepartureCount = State.Owner + State.Escort + State.ManualPawn; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void DebugTryFormCaravanInstantly() { DepartureCount = State.Owner + State.Escort + State.ManualPawn; }
    }
}
namespace Multiplayer.Client
{
    public class CaravanFormingProxy : Dialog_FormCaravan
    {
        public CaravanFormingProxy(State state) : base(state) { }
    }
    public class CaravanFormingSession
    {
        public readonly Faction faction = new Faction();
        public readonly State state = new State();
        private CaravanFormingProxy PrepareDummyDialog() => new CaravanFormingProxy(state);
        public CaravanFormingProxy MakeDialog() => PrepareDummyDialog();
        public void OpenWindow() => PrepareDummyDialog().Notify_TransferablesChanged();
        [MethodImpl(MethodImplOptions.NoInlining)] public void AddItems() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Notify_CountChanged(Transferable tr) { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Reset() { state.Owner = state.Escort = state.ManualPawn = 0; }
    }
    public static class FactionContext
    {
        private static readonly Stack<Faction> stack = new Stack<Faction>();
        public static Faction Current;
        public static int Depth => stack.Count;
        public static Faction Push(Faction faction, bool force) { stack.Push(Current); return Current = faction; }
        public static Faction Pop() => Current = stack.Pop();
    }
}
