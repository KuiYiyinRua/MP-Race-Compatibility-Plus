using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynProduction
    {
        internal static void Apply()
        {
            try
            {
                var producer = AccessTools.TypeByName("ASEL.Building_MonolynProducer");
                var crucible = AccessTools.TypeByName("ASEL.Building_Crucible");
                var recipe = AccessTools.TypeByName("ASEL.MonolynRecipeDef");
                if (producer == null || crucible == null || recipe == null ||
                    !producer.IsAssignableFrom(crucible) || !typeof(Thing).IsAssignableFrom(crucible) ||
                    !typeof(Def).IsAssignableFrom(recipe))
                    throw new InvalidOperationException("Missing or incompatible Monolyn production types");

                var parameters = new[] { recipe, typeof(int) };
                var baseStart = AccessTools.DeclaredMethod(producer, "StartWork", parameters);
                var start = AccessTools.DeclaredMethod(crucible, "StartWork", parameters);
                if (baseStart == null || start == null || start.IsStatic ||
                    start.ReturnType != typeof(void) || !start.IsVirtual ||
                    !start.GetBaseDefinition().Equals(baseStart.GetBaseDefinition()))
                    throw new MissingMethodException("ASEL.Building_Crucible", "StartWork(MonolynRecipeDef, int) override");

                // The recipe tab dispatches virtually. This override never calls the
                // already-synced producer method, so it needs its own MP interception.
                // Replay the native override: its costs, timing and light consumption
                // differ from the base implementation. Cancel remains inherited.
                MP.RegisterSyncMethod(start, null).SetContext(SyncContext.CurrentMap);
                Log.Message("[MonolynProduction] synced Building_Crucible.StartWork(MonolynRecipeDef, int); missing=0");
            }
            catch (Exception exception)
            {
                Log.Error("[MonolynProduction] Failed to sync crucible recipe selection: " + exception);
            }
        }
    }
}
