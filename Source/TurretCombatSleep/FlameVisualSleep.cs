using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.TurretCombatSleep
{
    // Presentation-only state. No combat predicate may depend on this cache.
    internal static class FlameVisualSleep
    {
        private sealed class State
        {
            internal bool Dirty = true;
            internal Map Map;
            internal IntVec3 Position;
            internal Rot4 Rotation;
            internal GameObject Effect;
        }
        private static readonly ConditionalWeakTable<ThingComp, State> States = new ConditionalWeakTable<ThingComp, State>();
        private static Func<ThingComp, bool> Wants, Playing, Queued;
        private static Func<ThingComp, GameObject> Effect;

        internal static void Install(Harmony harmony)
        {
            var type = AccessTools.TypeByName("RavenRace.Features.CustomTurrets.Arknights.Flame.CompFlameTurretEffect");
            if (type == null) return;
            try
            {
                Wants = TurretCombatSleep.FieldGetter<ThingComp, bool>(type, "wantsParticlesPlaying");
                Playing = TurretCombatSleep.FieldGetter<ThingComp, bool>(type, "particlesPlaying");
                Queued = TurretCombatSleep.FieldGetter<ThingComp, bool>(type, "effectRefreshQueued");
                Effect = TurretCombatSleep.FieldGetter<ThingComp, GameObject>(type, "effectObject");
                var tick = AccessTools.DeclaredMethod(type, "CompTick", Type.EmptyTypes);
                var notify = AccessTools.DeclaredMethod(type, "NotifySprayingChanged", new[] { typeof(bool) });
                if (tick == null || notify == null) throw new MissingMethodException("Flame visual tick/notification");
                harmony.Patch(tick, prefix: new HarmonyMethod(typeof(FlameVisualSleep), nameof(TickPrefix)),
                    postfix: new HarmonyMethod(typeof(FlameVisualSleep), nameof(Refreshed)));
                harmony.Patch(notify, prefix: new HarmonyMethod(typeof(FlameVisualSleep), nameof(NotifyPrefix)),
                    postfix: new HarmonyMethod(typeof(FlameVisualSleep), nameof(Refreshed)));
                foreach (var name in new[] { "PostSpawnSetup", "PostExposeData", "PostDeSpawn", "ResetToDefaults" })
                {
                    var method = AccessTools.DeclaredMethod(type, name);
                    if (method == null) throw new MissingMethodException(type.FullName, name);
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(FlameVisualSleep), nameof(Dirty)));
                }
                foreach (var name in new[] { "OffsetX", "OffsetY", "OffsetZ", "RotationX", "RotationY", "RotationZ", "EffectScale" })
                {
                    var method = AccessTools.PropertySetter(type, name);
                    if (method == null) throw new MissingMethodException(type.FullName, "set_" + name);
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(FlameVisualSleep), nameof(Dirty)));
                }
                Log.Message("[Meow.TurretCombatSleep] Idle flame visuals sleep until changed; active playback remains live");
            }
            catch (Exception e) { Log.Error("[Meow.TurretCombatSleep] REQUIRED_TARGET_FAILED flame visual sleep " + e); }
        }

        private static bool NeedsRefresh(ThingComp comp)
        {
            var parent = comp.parent;
            if (parent == null || !parent.Spawned || Wants(comp) || Playing(comp) || Queued(comp)) return true;
            var state = States.GetValue(comp, _ => new State());
            var effect = Effect(comp);
            if (state.Dirty || state.Map != parent.Map || state.Position != parent.Position || state.Rotation != parent.Rotation ||
                !ReferenceEquals(state.Effect, effect)) return true;
            // Missing Unity resources still get a bounded retry. Existing idle
            // effects need no transform/particle refresh until a real change.
            return effect == null && parent.IsHashIntervalTick(60);
        }

        private static bool TickPrefix(ThingComp __instance) => !MP.IsInMultiplayer || NeedsRefresh(__instance);

        private static bool NotifyPrefix(ThingComp __instance, bool __0)
            => !MP.IsInMultiplayer || __0 || Wants(__instance) || NeedsRefresh(__instance);

        private static void Refreshed(ThingComp __instance, bool __runOriginal)
        {
            if (!MP.IsInMultiplayer || !__runOriginal || __instance.parent == null) return;
            var state = States.GetValue(__instance, _ => new State());
            state.Dirty = false;
            state.Map = __instance.parent.Map;
            state.Position = __instance.parent.Position;
            state.Rotation = __instance.parent.Rotation;
            state.Effect = Effect(__instance);
        }

        private static void Dirty(ThingComp __instance) => States.GetValue(__instance, _ => new State()).Dirty = true;
    }
}
