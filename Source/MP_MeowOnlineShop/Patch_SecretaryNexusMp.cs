using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Secretary Nexus rebuilds a list of generated pawns every season. The
    /// pawn apparel candidate path is not deterministic across MP peers, so the
    /// rebuild both mutates saved state differently and advances world Rand by
    /// a different amount. Keep existing/snapshotted entries, but suppress
    /// seasonal regeneration while connected to Multiplayer.
    /// </summary>
    internal static class Patch_SecretaryNexusMp
    {
        private const string AssemblyName = "SEC_Assemblies";
        private const string GameCompTypeName = "SEC_Assemblies.SEC_GameComp";

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
                if (resetMethod == null || prefix == null)
                {
                    if (assembly != null)
                        Log.Warning("[MP-MeowOnlineShop] Secretary Nexus MP patch target was not resolved.");
                    return;
                }

                harmony.Patch(
                    resetMethod,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                Log.Message(
                    "[MP-MeowOnlineShop] Secretary Nexus MP patch active: " +
                    "seasonal random secretary regeneration is disabled only in multiplayer.");
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
    }
}
