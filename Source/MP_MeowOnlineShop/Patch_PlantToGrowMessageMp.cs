using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-330: Command_SetPlantToGrow.WarnAsAppropriate shows a warning
    /// dialog or Messages.Message while a synchronized plant command is
    /// replayed. The warning depends on per-peer skill/roof/glow state and can
    /// fire on only one peer, adding a MessageID trace and shifting the JobID
    /// stream. The warnings are informational, so skip them in multiplayer.
    /// </summary>
    internal static class Patch_PlantToGrowMessageMp
    {
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(Command_SetPlantToGrow),
                    "WarnAsAppropriate",
                    new[] { typeof(ThingDef) });
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_PlantToGrowMessageMp),
                    nameof(WarnAsAppropriatePrefix));
                if (target == null || prefix == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Plant-to-grow warning suppression " +
                        "skipped: target method was not resolved.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix)
                    {
                        priority = Priority.First
                    });

                Log.Message(
                    "[MP-MeowOnlineShop] Plant-to-grow warning suppression active: " +
                    "synchronized grow-zone plant changes do not allocate local " +
                    "MessageIDs or open warning dialogs in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Plant-to-grow warning suppression " +
                    "install failed: " + e.Message);
            }
        }

        private static bool WarnAsAppropriatePrefix()
        {
            return !MP.IsInMultiplayer;
        }
    }
}
