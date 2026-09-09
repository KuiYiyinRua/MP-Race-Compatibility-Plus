using System;
using System.Collections.Generic;
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
    /// NewRatkinPlus and Ratkin Underground store player-facing weapon/search
    /// toggles in saved comp fields that gizmo lambdas write directly. The
    /// booleans are registered as SyncFields. NewRatkin's wandering-caravan
    /// settlement dialog also mutates lords, factions, and the caravan Game
    /// Component from private window methods, so those are rewritten to sync
    /// methods.
    /// </summary>
    internal static class Patch_RatkinRaceMp
    {
        private const string NewRatkinPackageId = "solaris.ratkinracemod";
        private const string BfrToggleTypeName = "NewRatkin.Comp_BFRAmmoToggle";
        private const string PulseFireModeTypeName = "NewRatkin.Comp_PulseRifleFireMode";
        private const string CaravanComponentTypeName = "NewRatkin.GameComponent_WanderingCaravan";
        private const string CaravanSettlersWindowTypeName = "NewRatkin.Dialog_CaravanSettlers";

        private static bool _applied;
        private static Type _caravanComponentType;
        private static MethodInfo _onSettlerAcceptedMethod;
        private static MethodInfo _acceptPawnMethod;
        private static MethodInfo _acceptAllSettlersMethod;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                int registeredFields = 0;
                if (ModsConfig.IsActive(NewRatkinPackageId))
                {
                    registeredFields += TryRegisterField(BfrToggleTypeName, "isHEMode");
                    registeredFields += TryRegisterField(PulseFireModeTypeName, "isBurstMode");
                }

                int patchedDialog = 0;
                if (ModsConfig.IsActive(NewRatkinPackageId))
                    patchedDialog = ApplyCaravanSettlerDialog(harmony);

                Log.Message(
                    "[MP-MeowOnlineShop] Ratkin race MP patch active: " +
                    "sync fields=" + registeredFields + ", caravan dialog patches=" +
                    patchedDialog + "/2.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin race MP compat restore failed: " + e.Message);
            }
        }

        private static int TryRegisterField(string typeName, string fieldName)
        {
            Type type = AccessTools.TypeByName(typeName);
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            if (field == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin race field not resolved: " + typeName + "." + fieldName);
                return 0;
            }

            try
            {
                MP.RegisterSyncField(field);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin race sync field failed on " + fieldName + ": " + e.Message);
                return 0;
            }
        }

        private static int ApplyCaravanSettlerDialog(Harmony harmony)
        {
            _caravanComponentType = AccessTools.TypeByName(CaravanComponentTypeName);
            _onSettlerAcceptedMethod = _caravanComponentType == null
                ? null
                : AccessTools.Method(_caravanComponentType, "OnSettlerAccepted", new[] { typeof(Pawn) });

            Type windowType = AccessTools.TypeByName(CaravanSettlersWindowTypeName);
            _acceptPawnMethod = windowType == null
                ? null
                : AccessTools.DeclaredMethod(windowType, "AcceptPawn", new[] { typeof(Pawn) });
            _acceptAllSettlersMethod = windowType == null
                ? null
                : AccessTools.DeclaredMethod(windowType, "AcceptAllSettlers", new[] { typeof(List<Pawn>) });

            if (_caravanComponentType == null || _onSettlerAcceptedMethod == null ||
                _acceptPawnMethod == null || _acceptAllSettlersMethod == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin caravan settler dialog targets not resolved; dialog sync skipped.");
                return 0;
            }

            MethodInfo syncAccept = AccessTools.Method(
                typeof(Patch_RatkinRaceMp),
                nameof(SyncAcceptSettler),
                new[] { typeof(Pawn) });
            MethodInfo syncAcceptAll = AccessTools.Method(
                typeof(Patch_RatkinRaceMp),
                nameof(SyncAcceptAllSettlers),
                new[] { typeof(string) });
            if (syncAccept == null || syncAcceptAll == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(syncAccept, null);
                MP.RegisterSyncMethod(syncAcceptAll, null);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin caravan settler sync registration failed: " + e.Message);
                return 0;
            }

            MethodInfo acceptPrefix = AccessTools.Method(
                typeof(Patch_RatkinRaceMp),
                nameof(AcceptPawnPrefix));
            MethodInfo acceptAllPrefix = AccessTools.Method(
                typeof(Patch_RatkinRaceMp),
                nameof(AcceptAllSettlersPrefix));
            if (acceptPrefix == null || acceptAllPrefix == null)
                return 0;

            int patched = 0;
            try
            {
                harmony.Patch(_acceptPawnMethod, prefix: new HarmonyMethod(acceptPrefix));
                patched++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin AcceptPawn prefix failed: " + e.Message);
            }

            try
            {
                harmony.Patch(_acceptAllSettlersMethod, prefix: new HarmonyMethod(acceptAllPrefix));
                patched++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin AcceptAllSettlers prefix failed: " + e.Message);
            }

            return patched;
        }

        private static bool AcceptPawnPrefix(Pawn pawn)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;

            SyncAcceptSettler(pawn);
            return false;
        }

        private static bool AcceptAllSettlersPrefix(List<Pawn> pawns)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || pawns == null)
                return true;

            string ids = string.Join(
                ",",
                pawns.Select(p => p == null ? "0" : p.thingIDNumber.ToString()));
            SyncAcceptAllSettlers(ids);
            return false;
        }

        public static void SyncAcceptSettler(Pawn settler)
        {
            Map map = Find.CurrentMap;
            if (map == null || settler == null || settler.Destroyed || settler.Dead)
                return;

            object caravan = GetCaravanComponent();
            _onSettlerAcceptedMethod?.Invoke(caravan, new object[] { settler });

            Lord lord = LordUtility.GetLord(settler);
            lord?.Notify_PawnLost(settler, PawnLostCondition.LeftVoluntarily, null);
            settler.SetFaction(Faction.OfPlayer, null);

            Messages.Message(
                TranslatorFormattedStringExtensions.Translate(
                    "RK_WanderingCaravan_OneJoined",
                    settler.LabelShortCap),
                settler,
                MessageTypeDefOf.PositiveEvent,
                true);
        }

        public static void SyncAcceptAllSettlers(string pawnIds)
        {
            if (string.IsNullOrEmpty(pawnIds))
                return;

            List<Pawn> pawns = new List<Pawn>();
            string[] parts = pawnIds.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int id))
                {
                    Pawn pawn = FindPawnById(id);
                    if (pawn != null)
                        pawns.Add(pawn);
                }
            }

            Map currentMap = Find.CurrentMap;
            if (currentMap == null || pawns.Count == 0)
                return;

            object caravan = GetCaravanComponent();
            Lord lord = LordUtility.GetLord(pawns[0]);

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Destroyed || pawn.Dead)
                    continue;

                _onSettlerAcceptedMethod?.Invoke(caravan, new object[] { pawn });
                lord?.Notify_PawnLost(pawn, PawnLostCondition.LeftVoluntarily, null);
                pawn.SetFaction(Faction.OfPlayer, null);
            }

            Messages.Message(
                TranslatorFormattedStringExtensions.Translate(
                    "RK_WanderingCaravan_AllJoined",
                    pawns.Count),
                MessageTypeDefOf.PositiveEvent,
                true);
        }

        private static object GetCaravanComponent()
        {
            if (_caravanComponentType == null || Current.Game?.components == null)
                return null;

            for (int i = 0; i < Current.Game.components.Count; i++)
            {
                if (_caravanComponentType.IsInstanceOfType(Current.Game.components[i]))
                    return Current.Game.components[i];
            }

            return null;
        }

        private static Pawn FindPawnById(int pawnId)
        {
            if (Find.Maps != null)
            {
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    Map map = Find.Maps[m];
                    IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null)
                        continue;

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        if (pawns[i] != null && pawns[i].thingIDNumber == pawnId)
                            return pawns[i];
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                List<Pawn> worldPawns = Find.WorldPawns.AllPawnsAliveOrDead;
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    if (worldPawns[i] != null && worldPawns[i].thingIDNumber == pawnId)
                        return worldPawns[i];
                }
            }

            return null;
        }
    }
}
