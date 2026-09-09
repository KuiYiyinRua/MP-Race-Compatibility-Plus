using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    // Companion to HKXluo's UF UI patch (source/author in Patch_UFSeriesMp.cs).
    // Native SRA and UFLIFB CompHolographic advance saved transition fields in
    // PostDraw and gate StartTransition on a lazy graphics cache. Keep graphics
    // local, but run the state machine on simulation ticks, even off camera.
    internal static class Patch_UFHologramClockMp
    {
        private sealed class Fields
        {
            internal FieldInfo ticks, from, index, graphics, duration;
        }
        private static readonly Dictionary<Type, Fields> fields = new Dictionary<Type, Fields>();
        internal static bool Apply(Harmony harmony, Type type)
        {
            if (type == null) return false;
            if (fields.ContainsKey(type)) return true;
            var propsType = AccessTools.Property(type, "Props")?.PropertyType;
            var f = new Fields { ticks = AccessTools.Field(type, "transitionTicks"), from = AccessTools.Field(type, "transitionFromIndex"),
                index = AccessTools.Field(type, "currentIndex"), graphics = propsType == null ? null : AccessTools.Field(propsType, "graphics"),
                duration = propsType == null ? null : AccessTools.Field(propsType, "transitionDuration") };
            var draw = AccessTools.DeclaredMethod(type, "PostDraw", Type.EmptyTypes);
            var tick = AccessTools.DeclaredMethod(type, "CompTick", Type.EmptyTypes);
            var start = AccessTools.DeclaredMethod(type, "StartTransition", Type.EmptyTypes);
            if (f.ticks?.FieldType != typeof(int) || f.from?.FieldType != typeof(int) || f.index?.FieldType != typeof(int) ||
                f.duration?.FieldType != typeof(int) || f.graphics == null || draw == null || tick == null || start == null)
            {
                Log.Warning("[MP-MeowOnlineShop] UF hologram clock layout changed; UI sync skipped for " + type.FullName);
                return false;
            }
            fields.Add(type, f);
            harmony.Patch(draw, prefix: new HarmonyMethod(typeof(Patch_UFHologramClockMp), nameof(DrawPrefix)),
                finalizer: new HarmonyMethod(typeof(Patch_UFHologramClockMp), nameof(DrawFinalizer)));
            harmony.Patch(tick, prefix: new HarmonyMethod(typeof(Patch_UFHologramClockMp), nameof(TickPrefix)));
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(Patch_UFHologramClockMp), nameof(StartPrefix)));
            return true;
        }
        private static bool StartPrefix(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer) return true;
            var f = fields[__instance.GetType()];
            var graphics = f.graphics.GetValue(__instance.props) as IList;
            if ((int)f.from.GetValue(__instance) == -1 && graphics != null && graphics.Count > 1)
            {
                int index = (int)f.index.GetValue(__instance);
                int duration = Math.Max(0, (int)f.duration.GetValue(__instance.props));
                f.from.SetValue(__instance, duration == 0 ? -1 : index);
                f.index.SetValue(__instance, (index + 1) % graphics.Count);
                f.ticks.SetValue(__instance, duration);
            }
            return false;
        }
        private static void TickPrefix(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer) return;
            var f = fields[__instance.GetType()];
            var power = __instance.parent.GetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) return;
            if ((int)f.from.GetValue(__instance) == -1) return;
            int ticks = Math.Max(0, (int)f.ticks.GetValue(__instance) - 1);
            f.ticks.SetValue(__instance, ticks);
            if (ticks == 0) f.from.SetValue(__instance, -1);
        }
        private static void DrawPrefix(ThingComp __instance, out int[] __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer) return;
            var f = fields[__instance.GetType()];
            __state = new[] { (int)f.ticks.GetValue(__instance), (int)f.from.GetValue(__instance) };
        }
        private static Exception DrawFinalizer(ThingComp __instance, Exception __exception, int[] __state)
        {
            if (__state != null)
            {
                var f = fields[__instance.GetType()];
                f.ticks.SetValue(__instance, __state[0]);
                f.from.SetValue(__instance, __state[1]);
            }
            return __exception;
        }
    }
}
