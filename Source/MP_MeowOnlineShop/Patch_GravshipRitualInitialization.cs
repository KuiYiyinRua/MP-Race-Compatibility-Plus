using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_GravshipRitualInitialization
    {
        private sealed class Scope { internal Map Map; internal bool Pushed; internal bool Initializing; }
        private static int initializing;
        private static MethodInfo pushFaction, popFaction, originalGate;
        private static FieldInfo targetField;

        internal static void Apply(Harmony harmony)
        {
            var postOpen = AccessTools.Method(typeof(Dialog_BeginRitual), nameof(Dialog_BeginRitual.PostOpen));
            var gate = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Persistent.PreventRepeatedRitualPostOpenCalls"), "Prefix");
            var outcome = AccessTools.Method(typeof(RitualOutcomeEffectWorker_GravshipLaunch), nameof(RitualOutcomeEffectWorker_GravshipLaunch.Apply));
            var extensions = AccessTools.TypeByName("Multiplayer.Client.Factions.FactionExtensions");
            pushFaction = AccessTools.Method(extensions, "PushFaction", new[] { typeof(Map), typeof(Faction), typeof(bool) });
            popFaction = AccessTools.Method(extensions, "PopFaction", new[] { typeof(Map) });
            targetField = AccessTools.Field(typeof(Dialog_BeginRitual), "target");
            if (postOpen == null || gate == null || outcome == null || pushFaction == null || popFaction == null || targetField == null)
                throw new InvalidOperationException("REQUIRED_TARGET_FAILED gravship ritual initialization");
            originalGate = gate;
            // Replace the registered prefix rather than detouring its body:
            // an already generated Harmony wrapper can inline that tiny gate.
            harmony.Unpatch(postOpen, gate);
            harmony.Patch(postOpen,
                prefix: new HarmonyMethod(typeof(Patch_GravshipRitualInitialization), nameof(Begin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(Patch_GravshipRitualInitialization), nameof(End)) { priority = Priority.Last });
            harmony.Patch(postOpen, prefix: new HarmonyMethod(typeof(Patch_GravshipRitualInitialization), nameof(AllowInitialization)));
            harmony.Patch(outcome,
                prefix: new HarmonyMethod(typeof(Patch_GravshipRitualInitialization), nameof(OutcomeBegin)) { priority = Priority.First },
                finalizer: new HarmonyMethod(typeof(Patch_GravshipRitualInitialization), nameof(End)) { priority = Priority.Last });

            Log.Message("[MP-MeowOnlineShop] Gravship ritual initialization active: native job-tick dialog initializes once under its ship owner.");
        }

        private static void Begin(Dialog_BeginRitual __instance, out Scope __state)
        {
            __state = null;
            // Multiplayer calls PostOpen on the original dialog while creating
            // its shared session. Its proxy must keep the usual reopen guard.
            if (!MP.IsInMultiplayer || MP.InInterface || !(__instance is Dialog_BeginGravshipLaunch)) return;
            var target = (TargetInfo)targetField.GetValue(__instance);
            var console = target.Thing?.TryGetComp<CompPilotConsole>();
            var owner = console?.engine?.Faction;
            if (owner == null || !owner.IsPlayer || target.Map == null) return;
            __state = new Scope { Map = target.Map, Initializing = true };
            initializing++;
            pushFaction.Invoke(null, new object[] { target.Map, owner, true });
            __state.Pushed = true;
        }

        private static void OutcomeBegin(LordJob_Ritual jobRitual, out Scope __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer) return;
            var engine = jobRitual?.selectedTarget.Thing?.TryGetComp<CompPilotConsole>()?.engine;
            var owner = engine?.Faction;
            if (engine?.Map == null || owner == null || !owner.IsPlayer) return;
            // Ritual outcomes run in the map tick, which can belong to another
            // faction. Prelaunch warnings/session creation concern this ship.
            __state = new Scope { Map = engine.Map };
            pushFaction.Invoke(null, new object[] { engine.Map, owner, true });
            __state.Pushed = true;
        }

        private static bool AllowInitialization()
        {
            // The installed core gate only accepts ExecutingCmds. PilotConsole
            // opens this native dialog from JobDriver ticking instead.
            return initializing > 0 || (bool)originalGate.Invoke(null, null);
        }

        private static Exception End(Scope __state, Exception __exception)
        {
            if (__state != null)
            {
                try { if (__state.Pushed) popFaction.Invoke(null, new object[] { __state.Map }); }
                finally { if (__state.Initializing) initializing--; }
            }
            return __exception;
        }
    }
}
