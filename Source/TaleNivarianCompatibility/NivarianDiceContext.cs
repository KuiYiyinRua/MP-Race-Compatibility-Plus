using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // These GameComponent methods are world commands, so MP does not supply a
    // map TimeSnapshot or select the map consulted by third-party incidents.
    internal static class NivarianDiceContext
    {
        internal sealed class Scope
        {
            internal Game Game;
            internal Map ViewedMap;
            internal sbyte MapIndex;
            internal int Ticks;
            internal NivarianMetricsRefresh.Scope Managers;
        }
        sealed class Request { internal int Day = int.MinValue; }
        static readonly ConditionalWeakTable<object, Request> requests = new ConditionalWeakTable<object, Request>();
        static MethodInfo refresh, worldGetter;
        static FieldInfo worldTicks;
        static ISyncMethod refreshCommand;
        static readonly bool diagnostic = GenCommandLine.CommandLineArgPassed("meow-dice-diagnostic");

        internal static void Apply(Harmony harmony)
        {
            var metrics = AccessTools.TypeByName("Nivarian.GameComp_NivarianNiraMetrics")
                ?? throw new TypeLoadException("Nivarian Nira metrics");
            var mp = AccessTools.TypeByName("Multiplayer.Client.Multiplayer")
                ?? throw new TypeLoadException("Multiplayer");
            worldGetter = AccessTools.PropertyGetter(mp, "AsyncWorldTime")
                ?? throw new MissingMemberException(mp.FullName, "AsyncWorldTime");
            worldTicks = AccessTools.Field(worldGetter.ReturnType, "worldTicks")
                ?? throw new MissingFieldException("MP worldTicks");
            refresh = AccessTools.DeclaredMethod(metrics, "RefreshHoloDice", Type.EmptyTypes)
                ?? throw new MissingMethodException(metrics.FullName, "RefreshHoloDice");
            refreshCommand = MP.RegisterSyncMethod(typeof(NivarianDiceContext), nameof(RefreshFromInterface));
            harmony.Patch(refresh, prefix: new HarmonyMethod(typeof(NivarianDiceContext), nameof(RequestRefresh)) { priority = Priority.First });
            foreach (var name in new[] { "RefreshHoloDice", "RollHoloDie", "TriggerHoloDieOption", "SkipHoloDie" })
            {
                var target = AccessTools.DeclaredMethod(metrics, name)
                    ?? throw new MissingMethodException(metrics.FullName, name);
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(NivarianDiceContext), nameof(Before)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(NivarianDiceContext), nameof(After)) { priority = Priority.Last });
            }
            Log.Message("[NivarianDice] resolved=4 native actions; world clock, home-map incident queries and daily UI refresh; missing=0");
        }

        static int WorldTick() => (int)worldTicks.GetValue(worldGetter.Invoke(null, null));
        static bool RequestRefresh(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            // The native panel calls every draw. Send at most one request/day,
            // keyed to the world clock rather than the locally viewed map.
            var request = requests.GetOrCreateValue(__instance);
            int day = WorldTick() / GenDate.TicksPerDay;
            if (request.Day != day && refreshCommand.DoSync(null, (GameComponent)__instance)) request.Day = day;
            return false;
        }
        static void RefreshFromInterface(GameComponent component) => refresh.Invoke(component, null);

        static void Before(object __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || MP.InInterface) return;
            __state = new Scope { Game = Current.Game, ViewedMap = Find.CurrentMap, MapIndex = Current.Game.currentMapIndex, Ticks = Find.TickManager.TicksGame };
            Find.TickManager.DebugSetTicksGame(WorldTick());
            NivarianMetricsRefresh.Before(out __state.Managers);
            // MP RunCmds already sorts the map list. Select the actual native
            // incident target, including Odyssey's gravship fallback, rather
            // than changing the target or sorting the player's map list here.
            var target = Find.AnyPlayerHomeMap;
            if (target != null) Current.Game.currentMapIndex = (sbyte)Find.Maps.IndexOf(target);
            if (__originalMethod.Name == "RollHoloDie")
            {
                // A panel can be opened before the native 60k assessment tick.
                // Bring the saved daily list to the authoritative day before
                // the original index/rolled guards run on every peer.
                refresh.Invoke(__instance, null);
                if (diagnostic)
                {
                    var list = (System.Collections.IList)AccessTools.Field(__instance.GetType(), "holoDice").GetValue(__instance);
                    Log.Message("[NivarianDice] ROLL worldTick=" + WorldTick() + " target=" + target?.uniqueID
                        + " day=" + AccessTools.Field(__instance.GetType(), "lastHoloDiceDay").GetValue(__instance)
                        + " rank=" + AccessTools.Field(__instance.GetType(), "currentRankLevel").GetValue(__instance)
                        + " dice=" + list.Count + " flags=" + string.Join(",", list.Cast<object>().Select(d =>
                            AccessTools.Field(d.GetType(), "rolled").GetValue(d) + "/" + AccessTools.Field(d.GetType(), "consumed").GetValue(d))));
                }
            }
        }
        static Exception After(Exception __exception, Scope __state)
        {
            if (__state == null) return __exception;
            try { NivarianMetricsRefresh.After(__state.Managers); }
            finally
            {
                Find.TickManager.DebugSetTicksGame(__state.Ticks);
                if (ReferenceEquals(Current.Game, __state.Game))
                {
                    int index = Find.Maps.IndexOf(__state.ViewedMap);
                    Current.Game.currentMapIndex = index >= 0 ? (sbyte)index : __state.MapIndex;
                }
            }
            return __exception;
        }
    }
}
