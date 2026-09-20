using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Milira determines its initial diplomacy from FactionDef allowlists and from a
    /// permanent-enemy Harmony postfix which only handles Milira when it is argument A.
    /// Multiplayer creates additional player factions at runtime, with the new player
    /// faction as argument A, so a valid Milira/Kiiro start can retain the initial hostile
    /// relation produced before Milira's asymmetric postfix gets a chance to correct it.
    /// </summary>
    internal static class Patch_MiliraMultifactionRelations
    {
        private const string MiliraComponentTypeName = "Milira.MiliraGameComponent_OverallControl";
        private const string MultiplayerFactionCreatorTypeName = "Multiplayer.Client.Factions.FactionCreator";
        private const string MiliraFactionDefName = "Milira_Faction";
        private const string MiliraPlayerFactionDefName = "Milira_PlayerFaction";
        private const string KiiroPlayerCategoryTag = "Kiiro_PlayerFaction";

        internal static bool TargetAvailable { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || harmony == null)
                return;

            Type miliraComponentType = AccessTools.TypeByName(MiliraComponentTypeName);
            if (miliraComponentType == null)
            {
                Log.Message("[MP-MeowOnlineShop] Milira multifaction diplomacy compatibility skipped: Milira is not active.");
                return;
            }

            TargetAvailable = true;
            int patched = 0;

            try
            {
                MethodInfo permanentEnemyTarget = AccessTools.Method(
                    typeof(GoodwillSituationWorker_PermanentEnemy),
                    "ArePermanentEnemies",
                    new[] { typeof(Faction), typeof(Faction) });
                MethodInfo permanentEnemyPostfix = AccessTools.Method(
                    typeof(Patch_MiliraMultifactionRelations),
                    nameof(ArePermanentEnemiesPostfix));

                if (permanentEnemyTarget != null && permanentEnemyPostfix != null)
                {
                    harmony.Patch(permanentEnemyTarget, postfix: new HarmonyMethod(permanentEnemyPostfix)
                    {
                        priority = Priority.Last
                    });
                    patched++;
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Milira multifaction diplomacy: permanent-enemy target resolution failed.");
                }

                Type factionCreatorType = AccessTools.TypeByName(MultiplayerFactionCreatorTypeName);
                MethodInfo factionCreationTarget = factionCreatorType == null
                    ? null
                    : AccessTools.DeclaredMethod(factionCreatorType, "NewFactionWithIdeo");
                MethodInfo factionCreationPostfix = AccessTools.Method(
                    typeof(Patch_MiliraMultifactionRelations),
                    nameof(NewFactionWithIdeoPostfix));

                if (factionCreationTarget != null && factionCreationPostfix != null)
                {
                    harmony.Patch(factionCreationTarget, postfix: new HarmonyMethod(factionCreationPostfix));
                    patched++;
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Milira multifaction diplomacy: Multiplayer faction-creation target resolution failed.");
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Milira multifaction diplomacy compatibility active: " +
                    patched + "/2 runtime targets patched; existing-save migration armed.");
            }
            catch (Exception ex)
            {
                Log.Warning("[MP-MeowOnlineShop] Milira multifaction diplomacy patch failed: " + ex);
            }
        }

        private static void ArePermanentEnemiesPostfix(Faction a, Faction b, ref bool __result)
        {
            if (!MP.IsInMultiplayer)
                return;

            if ((IsMiliraFaction(a) && IsEligiblePlayerFaction(b)) ||
                (IsMiliraFaction(b) && IsEligiblePlayerFaction(a)))
            {
                __result = false;
            }
        }

        private static void NewFactionWithIdeoPostfix(Faction __result)
        {
            if (!MP.IsInMultiplayer || !IsEligiblePlayerFaction(__result))
                return;

            Faction milira = Find.FactionManager?.AllFactionsListForReading
                .FirstOrDefault(IsMiliraFaction);
            if (milira == null)
                return;

            EnsureWhitelisted(__result);
            if (NormalizeBuggedHostility(milira, __result))
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Corrected Milira diplomacy for newly created multiplayer faction " +
                    __result.Name + " (" + __result.loadID + ").");
            }
        }

        internal static bool PrepareSessionAndMigrateExistingSave()
        {
            if (!TargetAvailable || !MP.IsInMultiplayer || Find.FactionManager == null)
                return false;

            Faction milira = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(IsMiliraFaction);
            if (milira == null)
                return false;

            List<Faction> eligiblePlayers = Find.FactionManager.AllFactionsListForReading
                .Where(IsEligiblePlayerFaction)
                .OrderBy(faction => faction.loadID)
                .ToList();

            int corrected = 0;
            foreach (Faction playerFaction in eligiblePlayers)
            {
                EnsureWhitelisted(playerFaction);
                if (NormalizeBuggedHostility(milira, playerFaction))
                    corrected++;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Milira multifaction existing-save migration completed: " +
                corrected + " hostile relation(s) corrected across " + eligiblePlayers.Count +
                " eligible player faction(s).");
            return true;
        }

        internal static void EnsureSessionAllowlists()
        {
            if (!TargetAvailable || !MP.IsInMultiplayer || Find.FactionManager == null)
                return;

            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading
                         .Where(IsEligiblePlayerFaction)
                         .OrderBy(faction => faction.loadID))
            {
                EnsureWhitelisted(faction);
            }
        }

        private static void EnsureWhitelisted(Faction playerFaction)
        {
            FactionDef miliraDef = DefDatabase<FactionDef>.GetNamedSilentFail(MiliraFactionDefName);
            FactionDef playerDef = playerFaction?.def;
            if (miliraDef == null || playerDef == null)
                return;

            if (miliraDef.permanentEnemyToEveryoneExcept == null)
                miliraDef.permanentEnemyToEveryoneExcept = new List<FactionDef>();

            if (!miliraDef.permanentEnemyToEveryoneExcept.Contains(playerDef))
                miliraDef.permanentEnemyToEveryoneExcept.Add(playerDef);
        }

        internal static bool NormalizeBuggedHostility(Faction milira, Faction playerFaction)
        {
            if (milira == null || playerFaction == null || milira == playerFaction)
                return false;

            FactionRelation relation = milira.RelationWith(playerFaction, allowNull: true);
            if (relation != null && relation.kind != FactionRelationKind.Hostile)
                return false;

            milira.SetRelation(new FactionRelation(playerFaction, FactionRelationKind.Neutral)
            {
                baseGoodwill = 0
            });

            foreach (Map map in Find.Maps.OrderBy(candidate => candidate.uniqueID))
            {
                map.attackTargetsCache.Notify_FactionHostilityChanged(milira, playerFaction);
                map.attackTargetsCache.Notify_FactionHostilityChanged(playerFaction, milira);
            }

            return true;
        }

        internal static bool IsMiliraFaction(Faction faction)
        {
            return string.Equals(faction?.def?.defName, MiliraFactionDefName, StringComparison.Ordinal);
        }

        internal static bool IsEligiblePlayerFaction(Faction faction)
        {
            FactionDef def = faction?.def;
            if (def == null || !faction.IsPlayer)
                return false;

            return string.Equals(def.defName, MiliraPlayerFactionDefName, StringComparison.Ordinal) ||
                   string.Equals(def.categoryTag, KiiroPlayerCategoryTag, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Runs after a loaded singleplayer save is actually hosted. This matters because the usual
    /// RimWorld LoadedGame callback occurs before MP.IsInMultiplayer becomes true. The persisted
    /// version prevents later legitimate diplomacy changes from being reset on every load.
    /// </summary>
    public sealed class MiliraMultifactionRelationMigrationComponent : GameComponent
    {
        private const int CurrentMigrationVersion = 1;
        private const int PeriodicRecheckIntervalTicks = 1000;

        private int appliedMigrationVersion;
        private bool sessionAllowlistsPrepared;
        private int lastPeriodicRecheckTick = -1;

        public MiliraMultifactionRelationMigrationComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref appliedMigrationVersion,
                "mpMeowMiliraMultifactionRelationMigration",
                0);
        }

        public override void GameComponentTick()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
            if (!Patch_MiliraMultifactionRelations.TargetAvailable || !MP.IsInMultiplayer)
                return;

            if (!sessionAllowlistsPrepared)
            {
                Patch_MiliraMultifactionRelations.EnsureSessionAllowlists();
                sessionAllowlistsPrepared = true;
            }

            if (appliedMigrationVersion >= CurrentMigrationVersion)
            {
                // The one-time migration already ran on a previous load. Keep
                // the correction stable anyway: if a goodwill recalculation or a
                // newly created multiplayer faction re-introduces the asymmetric
                // permanent-enemy relation, re-normalize it deterministically.
            }
            else if (Patch_MiliraMultifactionRelations.PrepareSessionAndMigrateExistingSave())
            {
                appliedMigrationVersion = CurrentMigrationVersion;
            }

            if (Find.TickManager == null)
                return;

            int ticksGame = Find.TickManager.TicksGame;
            if (lastPeriodicRecheckTick >= 0 &&
                ticksGame - lastPeriodicRecheckTick < PeriodicRecheckIntervalTicks)
            {
                return;
            }
            lastPeriodicRecheckTick = ticksGame;

            Faction milira = Find.FactionManager?
                .AllFactionsListForReading?
                .FirstOrDefault(Patch_MiliraMultifactionRelations.IsMiliraFaction);
            if (milira == null)
                return;

            foreach (Faction playerFaction in Find.FactionManager.AllFactionsListForReading
                         .Where(Patch_MiliraMultifactionRelations.IsEligiblePlayerFaction)
                         .OrderBy(faction => faction.loadID))
            {
                Patch_MiliraMultifactionRelations.NormalizeBuggedHostility(
                    milira, playerFaction);
            }
        }
    }
}
