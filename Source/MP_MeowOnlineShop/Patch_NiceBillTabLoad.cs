using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Nice Bill Tab builds its mechanoid stat cache from LoadedGame by generating
    /// temporary pawns and equipment. On a joining client those disposable Things
    /// must not advance the synchronized positive Thing ID counter.
    /// </summary>
    internal static class Patch_NiceBillTabLoad
    {
        private static readonly FieldInfo LocalIdsOverrideField =
            AccessTools.Field(
                AccessTools.TypeByName("Multiplayer.Client.Patches.UniqueIdsPatch"),
                "useLocalIdsOverride");

        private struct ScopeState
        {
            internal bool active;
            internal bool previousOverride;
        }

        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("NiceBillTab.ModIntegration");
            var target = AccessTools.Method(type, "LoadedGame");
            var prefix = AccessTools.Method(typeof(Patch_NiceBillTabLoad), nameof(Prefix));
            var finalizer = AccessTools.Method(typeof(Patch_NiceBillTabLoad), nameof(Finalizer));

            if (type == null)
                return;

            if (target == null || prefix == null || finalizer == null || LocalIdsOverrideField == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Nice Bill Tab multiplayer load guard could not resolve its target.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

            Log.Message(
                "[MP-MeowOnlineShop] Nice Bill Tab multiplayer load guard active " +
                "(temporary local IDs and isolated Rand).");
        }

        private static void Prefix(ref ScopeState __state)
        {
            if (!MP.IsInMultiplayer)
                return;

            __state.active = true;
            __state.previousOverride = (bool)LocalIdsOverrideField.GetValue(null);
            LocalIdsOverrideField.SetValue(null, true);
            Rand.PushState(1312969805);
        }

        private static Exception Finalizer(Exception __exception, ScopeState __state)
        {
            if (__state.active)
            {
                try
                {
                    Rand.PopState();
                }
                finally
                {
                    LocalIdsOverrideField.SetValue(null, __state.previousOverride);
                }
            }

            return __exception;
        }
    }
}
