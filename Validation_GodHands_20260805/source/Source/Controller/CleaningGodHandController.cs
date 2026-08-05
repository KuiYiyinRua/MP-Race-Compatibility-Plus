using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    // 清洁模式枚举
    public enum CleaningMode
    {
        Cleaning,
        Pumping,
        WaterErase
    }

    // 清洁控制器核心
    public static partial class CleaningGodHandController
    {
        public static CleaningMode CurrentMode
        {
            get => currentMode;
            set => currentMode = value;
        }
        private static CleaningMode currentMode = CleaningMode.Cleaning;

        public static int CleanRadius
        {
            get => cleanRadius;
            set => cleanRadius = Mathf.Clamp(value, MIN_RADIUS, MAX_RADIUS);
        }
        private static int cleanRadius = 2;
        private const int MIN_RADIUS = 1;
        private const int MAX_RADIUS = 50;
        private const int ACCELERATE_THRESHOLD = 10;

        private const int EFFECT_INTERVAL_TICKS = 60;
        private static Dictionary<Thing, int> lastProcessedTick = new Dictionary<Thing, int>();

        public static int GetCleanRadius() => cleanRadius;
        public static CleaningMode GetCurrentMode() => currentMode;

        public static void ToggleMode()
        {
            currentMode = currentMode switch
            {
                CleaningMode.Cleaning => CleaningMode.Pumping,
                CleaningMode.Pumping => CleaningMode.WaterErase,
                CleaningMode.WaterErase => CleaningMode.Cleaning,
                _ => CleaningMode.Cleaning
            };

            ShowModeMessage();
        }

        private static void ShowModeMessage()
        {
            string key = currentMode switch
            {
                CleaningMode.Cleaning => "GodHand.Cleaning.Mode.Cleaning",
                CleaningMode.Pumping => "GodHand.Cleaning.Mode.Pumping",
                CleaningMode.WaterErase => "GodHand.Cleaning.Mode.WaterErase",
                _ => "Unknown"
            };
            Messages.Message(key.Translate(), MessageTypeDefOf.SilentInput);
        }

        public static void IncreaseRadius()
        {
            if (cleanRadius >= MAX_RADIUS) return;
            cleanRadius++;
            if (cleanRadius > ACCELERATE_THRESHOLD) cleanRadius++;
            ShowRadiusMessage();
        }

        public static void DecreaseRadius()
        {
            if (cleanRadius <= MIN_RADIUS) return;
            cleanRadius--;
            if (cleanRadius > ACCELERATE_THRESHOLD) cleanRadius--;
            ShowRadiusMessage();
        }

        private static void ShowRadiusMessage()
        {
            string msg = "GodHand.Cleaning.RadiusMsg".Translate(cleanRadius * 2 + 1);
            Messages.Message(msg, MessageTypeDefOf.SilentInput);
        }

        public static void CleanupProcessedThings()
        {
            try { RemoveDestroyedThings(); }
            catch (Exception e) { Log.Error($"[清洁] 缓存清理错误 {e.Message}"); }
        }

        private static void RemoveDestroyedThings()
        {
            List<Thing> toRemove = new List<Thing>();
            foreach (var kvp in lastProcessedTick)
            {
                if (kvp.Key == null || kvp.Key.Destroyed) toRemove.Add(kvp.Key);
            }
            foreach (Thing t in toRemove) lastProcessedTick.Remove(t);
        }

        private static bool ShouldProcess(Thing thing)
        {
            int current = Find.TickManager.TicksGame;
            if (lastProcessedTick.TryGetValue(thing, out int last))
            {
                if (current - last < EFFECT_INTERVAL_TICKS) return false;
            }
            lastProcessedTick[thing] = current;
            return true;
        }
    }
}
