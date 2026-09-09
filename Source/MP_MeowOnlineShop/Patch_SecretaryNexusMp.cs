using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Secretary Nexus has two peer-divergent paths. The seasonal rebuild
    /// regenerates pawn apparel candidates with different world Rand usage, and
    /// the first-contact incident runs from the world GameComponent tick and
    /// generates five pawns plus edge/spawn Rand. Both must be suppressed in
    /// multiplayer: a late-joining client can otherwise fire the incident once
    /// while the host already did, consuming ~1,900 extra map Rand calls and
    /// desyncing immediately after the "书姬已造访" log.
    /// </summary>
    internal static class Patch_SecretaryNexusMp
    {
        private const string AssemblyName = "SEC_Assemblies";
        private const string GameCompTypeName = "SEC_Assemblies.SEC_GameComp";

        private static FieldInfo _visitedField;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.GetName().Name, AssemblyName, StringComparison.Ordinal));
                var gameCompType = assembly?.GetType(GameCompTypeName, false);
                var resetMethod = AccessTools.Method(gameCompType, "ResetRandomSecretaries");
                var prefix = AccessTools.Method(typeof(Patch_SecretaryNexusMp), nameof(ResetRandomSecretariesPrefix));
                var tickRareMethod = AccessTools.Method(gameCompType, "SEC_GameComponent_TickRare");
                var tickRarePrefix = AccessTools.Method(
                    typeof(Patch_SecretaryNexusMp),
                    nameof(FirstContactTickRarePrefix));
                var visitedField = gameCompType == null
                    ? null
                    : AccessTools.Field(gameCompType, "SecretaryVisited");
                if (resetMethod == null || prefix == null ||
                    tickRareMethod == null || tickRarePrefix == null ||
                    visitedField == null)
                {
                    if (assembly != null)
                        Log.Warning("[MP-MeowOnlineShop] Secretary Nexus MP patch target was not resolved.");
                    return;
                }

                _visitedField = visitedField;
                harmony.Patch(
                    resetMethod,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                harmony.Patch(
                    tickRareMethod,
                    prefix: new HarmonyMethod(tickRarePrefix) { priority = Priority.First });
                Log.Message(
                    "[MP-MeowOnlineShop] Secretary Nexus MP patch active: " +
                    "seasonal random secretary regeneration and the first-contact " +
                    "incident are disabled only in multiplayer.");
            }
            catch (Exception exception)
            {
                Log.Warning("[MP-MeowOnlineShop] Secretary Nexus MP patch failed: " + exception);
            }
        }

        private static bool ResetRandomSecretariesPrefix()
        {
            return !MP.IsInMultiplayer;
        }

        private static bool FirstContactTickRarePrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            if (_visitedField != null && _visitedField.FieldType == typeof(bool))
                _visitedField.SetValue(__instance, true);

            return true;
        }
    }
}
