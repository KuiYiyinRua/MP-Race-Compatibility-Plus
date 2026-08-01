using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Synchronizes Ancot Library's float-unit sortie/follow commands.
    /// The original gizmo callbacks mutate persistent AI state directly from UI code.
    /// </summary>
    internal static class Patch_AncotCommandMode
    {
        private const string DeployTypeName = "AncotLibrary.CompApparelReloadable_DeployPawn";
        private const string PivotTypeName = "AncotLibrary.CompCommandPivot";
        private const string TerminalTypeName = "AncotLibrary.CompCommandTerminal";
        private const string SaveKey = "mpMeowAncotDeploySortie";

        private static Type _deployType;
        private static Type _pivotType;
        private static Type _terminalType;
        private static FieldInfo _deploySortieField;
        private static FieldInfo _pivotSortieField;
        private static FieldInfo _terminalSortieField;
        private static FieldInfo _terminalPivotField;
        private static FieldInfo _deploySpawnedPawnsField;
        private static PropertyInfo _pivotSpawnedPawnsProperty;
        private static PropertyInfo _deployPawnOwnerProperty;
        private static bool _syncRegistered;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            _deployType = AccessTools.TypeByName(DeployTypeName);
            _pivotType = AccessTools.TypeByName(PivotTypeName);
            _terminalType = AccessTools.TypeByName(TerminalTypeName);
            if (_terminalType == null || (_deployType == null && _pivotType == null))
                return;

            _deploySortieField = AccessTools.Field(_deployType, "sortie");
            _pivotSortieField = AccessTools.Field(_pivotType, "sortie");
            _terminalSortieField = AccessTools.Field(_terminalType, "sortie_Terminal");
            _terminalPivotField = AccessTools.Field(_terminalType, "pivot");
            _deploySpawnedPawnsField = AccessTools.Field(_deployType, "spawnedPawns");
            _pivotSpawnedPawnsProperty = AccessTools.Property(_pivotType, "spawnedPawns");
            _deployPawnOwnerProperty = AccessTools.Property(_deployType, "PawnOwner");

            if (_terminalSortieField == null || _terminalPivotField == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Ancot command mode patch skipped: terminal state fields were not found.");
                return;
            }

            if (!_syncRegistered)
            {
                MP.RegisterSyncMethod(typeof(Patch_AncotCommandMode), nameof(SyncSetSortie))
                    .CancelIfAnyArgNull();
                _syncRegistered = true;
            }

            int callbacks = 0;
            callbacks += PatchCallback(harmony, _deployType, "<CompGetWornGizmosExtra>b__", _deploySortieField);
            callbacks += PatchCallback(harmony, _pivotType, "<CompGetGizmosExtra>b__", _pivotSortieField);

            int persistence = 0;
            if (_deployType != null && _deploySortieField != null)
            {
                MethodInfo expose = AccessTools.Method(_deployType, "PostExposeData");
                MethodInfo postfix = AccessTools.Method(typeof(Patch_AncotCommandMode), nameof(DeployPostExposeDataPostfix));
                if (expose != null && postfix != null)
                {
                    harmony.Patch(expose, postfix: new HarmonyMethod(postfix));
                    persistence = 1;
                }
            }

            Log.Message($"[MP-MeowOnlineShop] Ancot command mode patch active: callbacks={callbacks}/2, persistence={persistence}/1.");
        }

        private static int PatchCallback(Harmony harmony, Type type, string namePrefix, FieldInfo sortieField)
        {
            if (type == null || sortieField == null)
                return 0;

            MethodInfo[] candidates = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith(namePrefix, StringComparison.Ordinal))
                .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
                .ToArray();

            if (candidates.Length != 1)
            {
                Log.Warning($"[MP-MeowOnlineShop] Ancot command mode patch: expected one callback {type.FullName}.{namePrefix}*, found {candidates.Length}; target left unpatched.");
                return 0;
            }

            MethodInfo prefix = AccessTools.Method(typeof(Patch_AncotCommandMode), nameof(CommandCallbackPrefix));
            harmony.Patch(candidates[0], prefix: new HarmonyMethod(prefix));
            return 1;
        }

        private static bool CommandCallbackPrefix(ThingComp __instance)
        {
            if (__instance == null || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;

            FieldInfo field = GetSortieField(__instance);
            if (field == null)
                return true;

            bool current = (bool)field.GetValue(__instance);
            SyncSetSortie(__instance, !current);
            return false;
        }

        public static void SyncSetSortie(ThingComp source, bool value)
        {
            if (source == null)
                return;

            FieldInfo sourceField = GetSortieField(source);
            if (sourceField == null)
                return;

            sourceField.SetValue(source, value);
            Pawn pivot = GetPivot(source);
            var updated = new HashSet<int>();

            foreach (Pawn pawn in GetSourcePawns(source))
                SetTerminalState(pawn, pivot, value, updated, requireMatchingPivot: false);

            // CompApparelReloadable_DeployPawn does not save spawnedPawns. Recover the
            // intended children from their serialized terminal.pivot reference after rejoin.
            if (pivot != null && Find.Maps != null)
            {
                foreach (Map map in Find.Maps.OrderBy(m => m.uniqueID))
                {
                    if (map?.mapPawns?.AllPawnsSpawned == null)
                        continue;
                    foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber))
                        SetTerminalState(pawn, pivot, value, updated, requireMatchingPivot: true);
                }
            }
        }

        private static FieldInfo GetSortieField(ThingComp source)
        {
            Type type = source.GetType();
            if (_deployType != null && _deployType.IsAssignableFrom(type))
                return _deploySortieField;
            if (_pivotType != null && _pivotType.IsAssignableFrom(type))
                return _pivotSortieField;
            return null;
        }

        private static Pawn GetPivot(ThingComp source)
        {
            if (_pivotType != null && _pivotType.IsInstanceOfType(source))
                return source.parent as Pawn;
            if (_deployType != null && _deployType.IsInstanceOfType(source))
                return _deployPawnOwnerProperty?.GetValue(source, null) as Pawn;
            return null;
        }

        private static IEnumerable<Pawn> GetSourcePawns(ThingComp source)
        {
            object value = null;
            try
            {
                if (_deployType != null && _deployType.IsInstanceOfType(source))
                    value = _deploySpawnedPawnsField?.GetValue(source);
                else if (_pivotType != null && _pivotType.IsInstanceOfType(source))
                    value = _pivotSpawnedPawnsProperty?.GetValue(source, null);
            }
            catch
            {
                yield break;
            }

            if (!(value is IEnumerable enumerable))
                yield break;
            foreach (object item in enumerable)
            {
                if (item is Pawn pawn)
                    yield return pawn;
            }
        }

        private static void SetTerminalState(Pawn pawn, Pawn pivot, bool value, HashSet<int> updated, bool requireMatchingPivot)
        {
            if (pawn == null || !updated.Add(pawn.thingIDNumber) || pawn.AllComps == null)
                return;

            ThingComp terminal = pawn.AllComps.FirstOrDefault(c => c != null && _terminalType.IsInstanceOfType(c));
            if (terminal == null)
                return;
            if (requireMatchingPivot && !ReferenceEquals(_terminalPivotField.GetValue(terminal), pivot))
                return;
            _terminalSortieField.SetValue(terminal, value);
        }

        private static void DeployPostExposeDataPostfix(ThingComp __instance)
        {
            if (__instance == null || _deploySortieField == null)
                return;

            bool value = (bool)_deploySortieField.GetValue(__instance);
            bool defaultValue = GetDeployDefaultSortie(__instance, value);
            Scribe_Values.Look(ref value, SaveKey, defaultValue, forceSave: true);
            _deploySortieField.SetValue(__instance, value);
        }

        private static bool GetDeployDefaultSortie(ThingComp source, bool fallback)
        {
            try
            {
                object props = source.props;
                FieldInfo field = props == null ? null : AccessTools.Field(props.GetType(), "sortie");
                return field == null ? fallback : (bool)field.GetValue(props);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
