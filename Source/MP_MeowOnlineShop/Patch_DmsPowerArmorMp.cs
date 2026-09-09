using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// DMS Power Armor Expanded (`ziri.deadmanswitch.powerarmorexpanded`)
    /// contains two player-facing mutation paths:
    ///
    /// - CompApparelGiveHediff.Notify_Used adds a hediff and starts a saved
    ///   cooldown from a worn-apparel gizmo.
    /// - CompCommandRelayApparel opens a float menu that reassigns the
    ///   Overseer direct relation. Multiplayer has no generic sync for
    ///   Pawn_RelationsTracker.AddDirectRelation/RemoveDirectRelation, so only
    ///   the Overseer relation writes are converted into one replay command.
    /// </summary>
    internal static class Patch_DmsPowerArmorMp
    {
        private const string PackageId = "ziri.deadmanswitch.powerarmorexpanded";
        private const string GiveHediffCompTypeName = "DMS_PowerArmor_Expand.CompApparelGiveHediff";
        private const string NotifyUsedMethodName = "Notify_Used";

        private static readonly PropertyInfo RelationsPawnProperty =
            AccessTools.Property(typeof(Pawn_RelationsTracker), "pawn");
        private static readonly FieldInfo RelationsPawnField =
            AccessTools.Field(typeof(Pawn_RelationsTracker), "pawn");
        private static ISyncMethod _syncOverseerRelation;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type giveHediffType = AccessTools.TypeByName(GiveHediffCompTypeName);
            MethodInfo notifyUsed = giveHediffType == null
                ? null
                : AccessTools.Method(giveHediffType, NotifyUsedMethodName, Type.EmptyTypes);
            if (notifyUsed != null)
            {
                try
                {
                    MP.RegisterSyncMethod(notifyUsed, null).SetContext(SyncContext.CurrentMap);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] DMS Power Armor give-hediff sync registration failed: " + e.Message);
                }
            }

            PatchOverseerRelations(harmony);

            Log.Message("[MP-MeowOnlineShop] DMS Power Armor MP patch active: apparel hediff and overseer relay sync.");
        }

        private static void PatchOverseerRelations(Harmony harmony)
        {
            MethodInfo addRelation = AccessTools.Method(
                typeof(Pawn_RelationsTracker),
                "AddDirectRelation",
                new[] { typeof(PawnRelationDef), typeof(Pawn) });
            MethodInfo removeRelation = AccessTools.Method(
                typeof(Pawn_RelationsTracker),
                "RemoveDirectRelation",
                new[] { typeof(PawnRelationDef), typeof(Pawn) });
            MethodInfo addPrefix = AccessTools.Method(
                typeof(Patch_DmsPowerArmorMp),
                nameof(AddDirectRelationPrefix));
            MethodInfo removePrefix = AccessTools.Method(
                typeof(Patch_DmsPowerArmorMp),
                nameof(RemoveDirectRelationPrefix));

            if (addRelation == null || removeRelation == null || addPrefix == null || removePrefix == null)
                return;

            try
            {
                _syncOverseerRelation = MP.RegisterSyncMethod(
                        typeof(Patch_DmsPowerArmorMp),
                        nameof(SyncChangeOverseerRelation))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS Power Armor overseer sync registration failed: " + e.Message);
                return;
            }

            try
            {
                harmony.Patch(addRelation, prefix: new HarmonyMethod(addPrefix));
                harmony.Patch(removeRelation, prefix: new HarmonyMethod(removePrefix));
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS Power Armor overseer relation patch failed: " + e.Message);
            }
        }

        private static bool AddDirectRelationPrefix(
            Pawn_RelationsTracker __instance,
            PawnRelationDef def,
            Pawn otherPawn)
        {
            return ShouldSyncOverseerRelation(__instance, def, otherPawn)
                ? TrySyncOverseerRelation(__instance, otherPawn, add: true)
                : true;
        }

        private static bool RemoveDirectRelationPrefix(
            Pawn_RelationsTracker __instance,
            PawnRelationDef def,
            Pawn otherPawn)
        {
            return ShouldSyncOverseerRelation(__instance, def, otherPawn)
                ? TrySyncOverseerRelation(__instance, otherPawn, add: false)
                : true;
        }

        private static bool ShouldSyncOverseerRelation(
            Pawn_RelationsTracker tracker,
            PawnRelationDef def,
            Pawn otherPawn)
        {
            return MP.IsInMultiplayer && !MP.IsExecutingSyncCommand && MP.InInterface &&
                   _syncOverseerRelation != null && tracker != null &&
                   def == PawnRelationDefOf.Overseer && otherPawn != null &&
                   GetTrackerPawn(tracker) is Pawn pawn && pawn.Map != null;
        }

        private static bool TrySyncOverseerRelation(Pawn_RelationsTracker tracker, Pawn otherPawn, bool add)
        {
            try
            {
                Pawn pawn = GetTrackerPawn(tracker);
                if (pawn?.Map == null)
                    return true;

                _syncOverseerRelation.DoSync(
                    null,
                    pawn.Map.Index,
                    pawn.thingIDNumber,
                    otherPawn.thingIDNumber,
                    add);
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] DMS Power Armor overseer sync send failed: " + e.Message);
                return true;
            }
        }

        public static void SyncChangeOverseerRelation(int mapIndex, int trackerPawnId, int otherPawnId, bool add)
        {
            Map map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            Pawn trackerPawn = FindPawnById(map, trackerPawnId);
            Pawn otherPawn = FindPawnById(map, otherPawnId);
            if (trackerPawn?.relations == null || otherPawn == null)
                return;

            if (add)
                trackerPawn.relations.AddDirectRelation(PawnRelationDefOf.Overseer, otherPawn);
            else
                trackerPawn.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, otherPawn);
        }

        private static Pawn FindPawnById(Map map, int thingId)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null)
                return null;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn != null && pawn.thingIDNumber == thingId)
                    return pawn;
            }

            return null;
        }

        private static Pawn GetTrackerPawn(Pawn_RelationsTracker tracker)
        {
            if (tracker == null)
                return null;

            try
            {
                object value = RelationsPawnProperty?.GetValue(tracker) ?? RelationsPawnField?.GetValue(tracker);
                return value as Pawn;
            }
            catch
            {
                return null;
            }
        }
    }
}
