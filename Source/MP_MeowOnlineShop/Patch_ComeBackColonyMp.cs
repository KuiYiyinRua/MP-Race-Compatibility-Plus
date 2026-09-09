using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// I will be back (`hailuan.iwbb`) keeps a static `candidates` list on
    /// IncidentWork_ComeBack and never clears it. The list is filled while
    /// scanning currently kidnapped colonists, then read on later executions.
    /// It is not saved, so after a rejoin or after a prior incident ran at a
    /// different time on each peer, the two peers can start from different
    /// candidate sets and then take different Rand draws (desync).
    ///
    /// Multiplayer already replays incidents through the storyteller command,
    /// so the fix is to make the static cache start empty on every execution.
    /// The incident then deterministically rebuilds its candidates from the
    /// same world state on both peers.
    /// </summary>
    internal static class Patch_ComeBackColonyMp
    {
        private const string PackageId = "hailuan.iwbb";
        private const string IncidentTypeName = "ComeBackColony.IncidentWork_ComeBack";

        private static FieldInfo _candidatesField;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type incidentType = AccessTools.TypeByName(IncidentTypeName);
            FieldInfo candidatesField = incidentType == null
                ? null
                : AccessTools.Field(incidentType, "candidates");
            MethodInfo tryExecute = incidentType == null
                ? null
                : AccessTools.Method(incidentType, "TryExecuteWorker", new[] { typeof(IncidentParms) });
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_ComeBackColonyMp),
                nameof(TryExecuteWorkerPrefix));

            if (incidentType == null || candidatesField == null ||
                tryExecute == null || prefix == null)
            {
                Log.Warning("[MP-MeowOnlineShop] I will be back target resolution failed; patch skipped.");
                return;
            }

            _candidatesField = candidatesField;

            try
            {
                harmony.Patch(tryExecute, prefix: new HarmonyMethod(prefix));
                Log.Message("[MP-MeowOnlineShop] I will be back MP patch active: static candidate cache reset per incident.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] I will be back patch failed: " + e.Message);
            }
        }

        private static void TryExecuteWorkerPrefix()
        {
            if (!MP.IsInMultiplayer || _candidatesField == null)
                return;

            var list = _candidatesField.GetValue(null) as IList;
            list?.Clear();
        }
    }
}
