using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianUplinkProgressSignal
    {
        const string OpenedTag = "Nivarian.Progress.UplinkOpened";
        static MethodInfo nativeEmit;
        static Action<Signal> emit;
        static ISyncMethod opened;

        internal static void Apply(Harmony harmony)
        {
            var receiver = AccessTools.TypeByName("Nivarian_Race.Code.Progress.ProgressSignalReceiver")
                ?? throw new TypeLoadException("Nivarian progress signal receiver");
            nativeEmit = AccessTools.DeclaredMethod(receiver, "Emit", new[] { typeof(Signal) })
                ?? throw new MissingMethodException(receiver.FullName, "Emit");
            emit = (Action<Signal>)Delegate.CreateDelegate(typeof(Action<Signal>), nativeEmit);
            opened = MP.RegisterSyncMethod(typeof(NivarianUplinkProgressSignal), nameof(Opened));
            var window = AccessTools.TypeByName("Nivarian_Race.Code.UI.Window_UplinkResearch")
                ?? throw new TypeLoadException("Nivarian uplink window");
            var target = AccessTools.DeclaredMethod(window, "TryEmitUplinkOpenedSignal", Type.EmptyTypes)
                ?? throw new MissingMethodException(window.FullName, "TryEmitUplinkOpenedSignal");
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(NivarianUplinkProgressSignal), nameof(RouteSignal)));
            Log.Message("[TaleNivarianCompat] uplink opening progress event synchronized; window boot and per-window guard remain local.");
        }

        static IEnumerable<CodeInstruction> RouteSignal(IEnumerable<CodeInstruction> instructions)
        {
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(nativeEmit))
                {
                    instruction.operand = AccessTools.Method(typeof(NivarianUplinkProgressSignal), nameof(EmitOpened));
                    matches++;
                }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Uplink opened signal call count changed: " + matches);
        }

        static void EmitOpened(Signal signal)
        {
            if (MP.IsInMultiplayer && MP.InInterface) opened.DoSync(null);
            else emit(signal);
        }

        // The window emits a constant event without target data. The native receiver
        // commits it on every peer; no window instance or local animation state is sent.
        static void Opened() => emit(new Signal(OpenedTag, false));
    }
}
