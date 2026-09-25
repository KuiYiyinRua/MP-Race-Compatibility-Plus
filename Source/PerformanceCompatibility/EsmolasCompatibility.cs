using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.PerformanceCompatibility
{
    internal static class EsmolasCompatibility
    {
        private sealed class SettingsSnapshot
        {
            public SettingsSnapshot() { }
            internal bool[] Original;
        }

        private static readonly string[] Names = { "enableMothballOptimization", "enableMeditationThrottle", "enableIdeoManagerThrottle", "enableStatCacheThrottle" };
        private static readonly bool[] Profile = { true, true, true, false };
        private static readonly ConditionalWeakTable<object, SettingsSnapshot> Snapshots = new ConditionalWeakTable<object, SettingsSnapshot>();
        private static FieldInfo[] fields;
        private static FieldInfo settingsField;

        internal static void InstallEarly()
        {
            if (!ModsConfig.IsActive("Arkymn.PerformanceEsmolas")) return;
            var modType = AccessTools.TypeByName("PerformanceEsmolas.PerformanceEsmolasMod");
            var settingsType = AccessTools.TypeByName("PerformanceEsmolas.PerformanceEsmolasSettings");
            if (modType == null || settingsType == null) throw new TypeLoadException("Esmolas settings types missing");
            fields = new FieldInfo[Names.Length];
            for (int i = 0; i < fields.Length; i++)
                fields[i] = AccessTools.Field(settingsType, Names[i]) ?? throw new MissingFieldException(settingsType.FullName, Names[i]);
            settingsField = AccessTools.Field(modType, "settings");
            var h = PerformanceCompatibilityMod.Patcher;
            h.Patch(AccessTools.Constructor(modType, new[] { typeof(ModContentPack) }),
                postfix: new HarmonyMethod(typeof(EsmolasCompatibility), nameof(AfterConstructor)));
            // Both relative Mod constructor orders are supported. Neither path
            // invokes Esmolas' StatCacheabilityPatch static constructor.
            ApplyProfile(settingsField.GetValue(null));
            h.Patch(AccessTools.Method(settingsType, "ExposeData"),
                prefix: new HarmonyMethod(typeof(EsmolasCompatibility), nameof(BeforeExpose)),
                finalizer: new HarmonyMethod(typeof(EsmolasCompatibility), nameof(AfterExpose)));
            PerformanceCompatibilityMod.Prefix(AccessTools.Method(modType, "DoSettingsWindowContents"), typeof(EsmolasCompatibility), nameof(SettingsWindow));
            Log.Message("[Meow.Performance] Esmolas fixed startup profile: mothball/meditation/ideo ON, extended stat cache OFF; original preferences preserved on disk. Applies while Multiplayer is enabled, including pre-host play.");
        }

        private static void AfterConstructor() => ApplyProfile(settingsField.GetValue(null));

        private static void ApplyProfile(object settings)
        {
            if (settings == null) return;
            if (!Snapshots.TryGetValue(settings, out var snapshot))
            {
                snapshot = new SettingsSnapshot { Original = new bool[fields.Length] };
                for (int i = 0; i < fields.Length; i++) snapshot.Original[i] = (bool)fields[i].GetValue(settings);
                Snapshots.Add(settings, snapshot);
            }
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(settings, Profile[i]);
        }

        private static void BeforeExpose(object __instance, out bool __state)
        {
            __state = Scribe.mode == LoadSaveMode.Saving && Snapshots.TryGetValue(__instance, out _);
            if (!__state) return;
            var snapshot = Snapshots.GetOrCreateValue(__instance);
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__instance, snapshot.Original[i]);
        }

        private static void AfterExpose(object __instance, bool __state)
        {
            // During initial LoadingVars, wait for the mod constructor to
            // finish reading the user's complete settings before taking a copy.
            if (__state) ApplyProfile(__instance);
        }

        private static bool SettingsWindow(Rect inRect)
        {
            Widgets.Label(inRect, "MP-Race-Compatibility-Plus: Performance Esmolas uses a fixed multiplayer profile.\n\nMothball optimization and meditation throttling remain enabled. Ideology scheduling uses stable IDs. Extended stat caching is disabled. Plant rendering keeps your startup setting.\n\nYour original single-player settings are preserved. Disable Multiplayer and restart to edit them.");
            return false;
        }

        internal static void InstallLate()
        {
            if (settingsField == null) return;
            var t = typeof(EsmolasCompatibility);
            PerformanceCompatibilityMod.Prefix(PerformanceCompatibilityMod.Require("PerformanceEsmolas.IdeoManagerPatch", "Prefix"), t, nameof(IdeoSchedule));
            PerformanceCompatibilityMod.Prefix(PerformanceCompatibilityMod.Require("PerformanceEsmolas.MeditationFocusStrengthCache", "TryGet"), t, nameof(FocusRead));
            PerformanceCompatibilityMod.Prefix(PerformanceCompatibilityMod.Require("PerformanceEsmolas.MeditationFocusStrengthCache", "Store"), t, nameof(FocusWrite));
            // Older distributed cores include Esmolas in their unconditional
            // pre-host cleanup list. Retire only that entry after all of the
            // replacement guards resolved; leave other performance owners alone.
            var legacyType = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_ThirdPartyPerformanceMp");
            var legacyField = legacyType == null ? null : AccessTools.Field(legacyType, "UnsafeOwners");
            if (legacyField != null && legacyField.FieldType == typeof(string[]))
            {
                var owners = AccessTools.StaticFieldRefAccess<string[]>(legacyField);
                owners() = owners().Where(owner => owner != "Arkymn.PerformanceEsmolas").ToArray();
                Log.Message("[Meow.Performance] Legacy blanket Esmolas cleanup retired; other owner guards retained.");
            }
            Log.Message("[Meow.Performance] Esmolas: stable ideology schedule; meditation throttling retained, approximate focus cache bypassed in MP.");
        }

        // Patch of a patch: __0 is Esmolas' Ideo argument; __result is the
        // decision returned by its prefix, not the original void IdeoTick.
        private static bool IdeoSchedule(Ideo __0, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            foreach (var faction in Find.FactionManager.AllFactionsListForReading)
                if (faction.def.isPlayer && faction.ideos != null && faction.ideos.Has(__0))
                { __result = true; return false; }
            // Invalid/unsaved IDs should use the vanilla path, never local hashes.
            __result = __0.id < 0 || __0.id % 250 == Find.TickManager.TicksGame % 250;
            return false;
        }

        private static bool FocusRead(ref float value, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            value = 0f;
            __result = false;
            return false;
        }

        private static bool FocusWrite() => !MP.IsInMultiplayer;
    }
}
