using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-419: RigorMortis.CompChangeCorpseApparel opens a two-stage
    /// targeter. SecondarySelect stores the chosen worker pawn in tmpPawn on
    /// the issuing peer only, then FinalEffect(LocalTargetInfo) creates the
    /// change-apparel job. Multiplayer already syncs FinalEffect, but the
    /// command carries only the apparel target; if tmpPawn was never set on
    /// the replaying peer, the job is created on one side and map Rand/job
    /// streams diverge immediately afterwards.
    ///
    /// Registering tmpPawn as a SyncField is the narrowest stable boundary:
    /// the field write happens before FinalEffect in the same UI flow, so
    /// every peer receives the chosen pawn before the apparel target command
    /// is replayed.
    /// </summary>
    internal static class Patch_RigorMortisChangeApparel
    {
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                Type compType =
                    AccessTools.TypeByName("RigorMortis.CompChangeCorpseApparel");
                FieldInfo tmpPawn =
                    compType == null ? null : AccessTools.Field(compType, "tmpPawn");
                if (tmpPawn == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] RigorMortis change-apparel sync " +
                        "skipped: CompChangeCorpseApparel.tmpPawn not resolved.");
                    return;
                }

                MP.RegisterSyncField(tmpPawn);
                Log.Message(
                    "[MP-MeowOnlineShop] RigorMortis change-apparel sync active: " +
                    "tmpPawn is synchronized before FinalEffect.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] RigorMortis change-apparel sync apply " +
                    "failed: " + e.Message);
            }
        }
    }
}
