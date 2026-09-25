using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class WorldComponentClock
    {
        static FieldInfo tickingWorld, worldTicks;
        static MethodInfo worldTime;

        internal static void Apply(Harmony harmony)
        {
            var type = Bootstrap.Type("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp");
            tickingWorld = Bootstrap.Field(type, "tickingWorld", typeof(bool));
            worldTicks = Bootstrap.Field(type, "worldTicks", typeof(int));
            worldTime = AccessTools.PropertyGetter(Bootstrap.Type("Multiplayer.Client.Multiplayer"), "AsyncWorldTime");
            if (worldTime == null || worldTime.ReturnType != type || !tickingWorld.IsStatic)
                throw new InvalidOperationException("MP world clock signature changed");
            harmony.Patch(Bootstrap.Method(typeof(GameComponentUtility), "GameComponentTick"),
                transpiler: new HarmonyMethod(typeof(WorldComponentClock), nameof(Rewrite)));
            // WorldPawns and FactionManager execute before GameComponentUtility.
            // Desync-172/173 show map time leaking into these world-only managers.
            foreach (var target in new[] {
                Bootstrap.Method(typeof(RimWorld.Planet.WorldPawns), "WorldPawnsTick"),
                Bootstrap.Method(typeof(RimWorld.FactionManager), "FactionManagerTick") })
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(WorldComponentClock), nameof(BeforeManager)) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(typeof(WorldComponentClock), nameof(AfterManager)));
        }

        internal static void BeforeManager(out int? __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !(bool)tickingWorld.GetValue(null)) return;
            var clock = worldTime.Invoke(null, null);
            if (clock == null) throw new InvalidOperationException("Missing active MP world clock");
            __state = Find.TickManager.TicksGame;
            Find.TickManager.DebugSetTicksGame((int)worldTicks.GetValue(clock) + 1);
        }

        internal static void AfterManager(int? __state)
        {
            if (__state.HasValue) Find.TickManager.DebugSetTicksGame(__state.Value);
        }

        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var original = Bootstrap.Method(typeof(GameComponent), "GameComponentTick");
            var replacement = AccessTools.Method(typeof(WorldComponentClock), nameof(TickComponent));
            int count = 0;
            foreach (var instruction in code)
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
            if (count != 1) throw new InvalidOperationException("Expected one GameComponentTick dispatcher call, found " + count);
            return code;
        }

        internal static void TickComponent(GameComponent component)
        {
            if (!MP.IsInMultiplayer || !(bool)tickingWorld.GetValue(null))
            {
                component.GameComponentTick();
                return;
            }
            var clock = worldTime.Invoke(null, null);
            if (clock == null) throw new InvalidOperationException("Missing active MP world clock");
            var manager = Find.TickManager;
            int previous = manager.TicksGame;
            // MP increments worldTicks AFTER DoSingleTick returns; vanilla has
            // already incremented TicksGame before dispatching game components.
            // Desync-146 observes map clocks 1542200 vs 2712769 in this world
            // dispatcher. Use its authoritative clock, never the viewed map or
            // network Timer. Restore per invocation, including exception paths.
            manager.DebugSetTicksGame((int)worldTicks.GetValue(clock) + 1);
            try { component.GameComponentTick(); }
            finally { manager.DebugSetTicksGame(previous); }
        }
    }
}
