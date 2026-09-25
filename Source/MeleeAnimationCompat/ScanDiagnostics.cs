using System;
using System.Linq;
using AM;
using AM.Processing;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    // Explicitly opt-in, bounded evidence for differing scan eligibility. Never changes RNG or selection.
    [HarmonyPatch(typeof(MapPawnProcessor), "CompileListOfAttackers")]
    internal static class ScanDiagnostics
    {
        private static readonly bool Enabled = Environment.GetEnvironmentVariable("MEOW_MP_SCAN_DIAGNOSTICS") == "1";
        private static int remaining = 256;
        private static void Prefix(Map ___map)
        {
            if (!Enabled || !Bootstrap.Active || ___map == null || remaining-- <= 0) return;
            var rows = ___map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber).Select(p =>
                p.thingIDNumber + ":dead=" + p.Dead + ":down=" + p.Downed
                + ":animal=" + p.RaceProps.Animal + ":tool=" + p.RaceProps.ToolUser
                + ":fire=" + p.drafter?.FireAtWill + ":job=" + p.CurJobDef?.defName
                + ":anim=" + AnimRenderer.ActiveRenderers.Any(r => !r.IsDestroyed && r.Pawns.Contains(p)));
            Log.Message("[MeleeScanEvidence] map=" + ___map.uniqueID + " tick=" + Find.TickManager.TicksGame
                + " abs=" + GenTicks.TicksAbs + " interval=" + MeleeSessionState.CurrentRules().ScanTickInterval
                + " pawns=" + string.Join(";", rows));
        }
    }
}
