using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// A role-change ritual writes its result to a Precept_Role and therefore to
    /// the ideology owned by the ritual's player faction.  The vanilla role
    /// assignment also reads Faction.OfPlayer for leader-role side effects.
    /// In a multifaction MP command replay that field is local-player state, so
    /// different peers can write or clear roles against different factions.
    /// Keep the faction context at the ritual ideology's stable owning faction
    /// for the complete outcome application.  Precept_Role's normal serialized
    /// chosen-pawn state then remains the single durable record of the result.
    /// </summary>
    internal static class Patch_RitualRoleChangeMultifactionPersistence
    {
        private static bool _applied;
        private static FieldInfo _ofPlayerField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(RitualOutcomeEffectWorker_RoleChange),
                    nameof(RitualOutcomeEffectWorker_RoleChange.Apply));
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(ApplyPrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_RitualRoleChangeMultifactionPersistence),
                    nameof(ApplyFinalizer));
                _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");

                if (target == null || prefix == null || finalizer == null || _ofPlayerField == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Role-change ritual multifaction " +
                        "persistence target resolution failed.");
                    return;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

                Log.Message(
                    "[MP-MeowOnlineShop] Role-change ritual multifaction persistence active: " +
                    "outcomes use the ritual ideology's owning faction.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Role-change ritual multifaction " +
                    "persistence patch failed: " + e.Message);
            }
        }

        private static void ApplyPrefix(LordJob_Ritual jobRitual, ref Faction __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || jobRitual?.Ritual?.ideo == null ||
                !MpRuntimeInfo.TryGetMultifactionActive(out bool multifaction) || !multifaction)
            {
                return;
            }

            try
            {
                Faction owner = Find.FactionManager?.AllFactionsListForReading?
                    .Where(faction => faction?.ideos != null && faction.ideos.Has(jobRitual.Ritual.ideo))
                    .OrderBy(faction => faction.loadID)
                    .FirstOrDefault();
                FactionManager factionManager = Find.FactionManager;
                if (owner == null || factionManager == null)
                    return;

                __state = _ofPlayerField.GetValue(factionManager) as Faction;
                _ofPlayerField.SetValue(factionManager, owner);
            }
            catch
            {
                __state = null;
            }
        }

        private static void ApplyFinalizer(Faction __state)
        {
            if (__state == null)
                return;

            try
            {
                FactionManager factionManager = Find.FactionManager;
                if (factionManager != null)
                    _ofPlayerField.SetValue(factionManager, __state);
            }
            catch
            {
                // Preserve Multiplayer's original faction context if restoration fails.
            }
        }
    }
}
