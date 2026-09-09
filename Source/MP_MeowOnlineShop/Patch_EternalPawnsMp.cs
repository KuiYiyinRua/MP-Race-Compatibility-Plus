using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Eternal Pawns (`helldan.eternalpawns`) keeps its veteran database in
    /// WorldPopulationManager dictionaries/hash sets. Window_PawnMemory and the
    /// settings dialog mutate those collections in place, so SyncField watching
    /// (which compares collection references) cannot detect the writes. This
    /// patch snapshots the affected state before each UI draw, rolls back any
    /// local mutation after the draw, and replays one deterministic command.
    /// </summary>
    internal static class Patch_EternalPawnsMp
    {
        private const string PackageId = "helldan.eternalpawns";
        private const string ManagerTypeName = "FinitePopulationVeterans.WorldPopulationManager";
        private const string MemoryWindowTypeName = "FinitePopulationVeterans.Window_PawnMemory";

        private static bool _applied;
        private static Type _managerType;
        private static MethodInfo _getComponentMethod;
        private static FieldInfo _pinsField;
        private static FieldInfo _notesField;
        private static FieldInfo _poolField;
        private static FieldInfo _cacheField;
        private static FieldInfo _addTicksField;
        private static FieldInfo _missionField;
        private static Type _dialogType;
        private static FieldInfo _dialogTextField;
        private static ISyncMethod _syncUpdateMemory;
        private static ISyncMethod _syncClearDatabase;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Eternal Pawns sync skipped (target mod not active).");
                    return;
                }

                _managerType = AccessTools.TypeByName(ManagerTypeName);
                Type windowType = AccessTools.TypeByName(MemoryWindowTypeName);
                _dialogType = AccessTools.TypeByName("Verse.Dialog_MessageBox")
                              ?? AccessTools.TypeByName("RimWorld.Dialog_MessageBox");
                _getComponentMethod = AccessTools.Method(typeof(World), "GetComponent", Type.EmptyTypes);

                if (_managerType == null || windowType == null || _getComponentMethod == null ||
                    _dialogType == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Eternal Pawns target resolution failed; patch skipped.");
                    return;
                }

                _pinsField = AccessTools.Field(_managerType, "manualVeteranPins");
                _notesField = AccessTools.Field(_managerType, "pawnNotes");
                _poolField = AccessTools.Field(_managerType, "veteranPool");
                _cacheField = AccessTools.Field(_managerType, "allVeteranIdsCache");
                _addTicksField = AccessTools.Field(_managerType, "veteranAddTicks");
                _missionField = AccessTools.Field(_managerType, "veteransOnMission");
                _dialogTextField = AccessTools.Field(_dialogType, "text");

                if (_pinsField == null || _notesField == null || _poolField == null ||
                    _cacheField == null || _addTicksField == null || _missionField == null ||
                    _dialogTextField == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Eternal Pawns field resolution failed; patch skipped.");
                    return;
                }

                int registered = 0;
                registered += TryRegisterSyncMethod(
                    AccessTools.Method(
                        typeof(Patch_EternalPawnsMp),
                        nameof(SyncUpdateMemory),
                        new[] { typeof(int), typeof(bool), typeof(string) }),
                    ref _syncUpdateMemory);
                registered += TryRegisterSyncMethod(
                    AccessTools.Method(
                        typeof(Patch_EternalPawnsMp),
                        nameof(SyncClearDatabase),
                        Type.EmptyTypes),
                    ref _syncClearDatabase);

                if (registered == 0)
                {
                    Log.Warning("[MP-MeowOnlineShop] Eternal Pawns sync registration failed; patch skipped.");
                    return;
                }

                MethodInfo memoryDraw = AccessTools.Method(windowType, "DoWindowContents", new[] { typeof(Rect) });
                MethodInfo dialogDraw = AccessTools.Method(_dialogType, "DoWindowContents", new[] { typeof(Rect) });

                if (memoryDraw != null)
                {
                    harmony.Patch(
                        memoryDraw,
                        prefix: new HarmonyMethod(
                            AccessTools.Method(typeof(Patch_EternalPawnsMp), nameof(MemoryWindowPrefix))),
                        postfix: new HarmonyMethod(
                            AccessTools.Method(typeof(Patch_EternalPawnsMp), nameof(MemoryWindowPostfix))));
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Eternal Pawns memory window target not resolved; UI sync skipped.");
                }

                if (dialogDraw != null)
                {
                    harmony.Patch(
                        dialogDraw,
                        prefix: new HarmonyMethod(
                            AccessTools.Method(typeof(Patch_EternalPawnsMp), nameof(ClearDbPrefix))),
                        postfix: new HarmonyMethod(
                            AccessTools.Method(typeof(Patch_EternalPawnsMp), nameof(ClearDbPostfix))));
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Eternal Pawns dialog target not resolved; clear-database sync skipped.");
                }

                Log.Message("[MP-MeowOnlineShop] Eternal Pawns MP patch active: " + registered + " sync methods.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Eternal Pawns MP compat restore failed: " + e.Message);
            }
        }

        private static int TryRegisterSyncMethod(MethodInfo method, ref ISyncMethod target)
        {
            if (method == null)
                return 0;

            try
            {
                target = MP.RegisterSyncMethod(method, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Eternal Pawns sync register failed on " + method.Name + ": " + e.Message);
                return 0;
            }
        }

        private static object GetManager()
        {
            if (_getComponentMethod == null || _managerType == null || Find.World == null)
                return null;

            try
            {
                MethodInfo generic = _getComponentMethod.MakeGenericMethod(_managerType);
                return generic.Invoke(Find.World, null);
            }
            catch
            {
                return null;
            }
        }

        public static void SyncUpdateMemory(int thingID, bool pinned, string note)
        {
            object manager = GetManager();
            if (manager == null || _pinsField == null || _notesField == null)
                return;

            var pins = (HashSet<int>)_pinsField.GetValue(manager);
            if (pinned)
                pins.Add(thingID);
            else
                pins.Remove(thingID);

            var notes = (Dictionary<int, string>)_notesField.GetValue(manager);
            if (string.IsNullOrWhiteSpace(note))
                notes.Remove(thingID);
            else
                notes[thingID] = note;
        }

        public static void SyncClearDatabase()
        {
            object manager = GetManager();
            if (manager == null)
                return;

            ClearCollection(_poolField.GetValue(manager));
            ClearCollection(_cacheField.GetValue(manager));
            ClearCollection(_addTicksField.GetValue(manager));
            ClearCollection(_missionField.GetValue(manager));
        }

        private static void MemoryWindowPrefix(int ___thingID, ref MemoryState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return;

            object manager = GetManager();
            if (manager == null || _pinsField == null || _notesField == null)
                return;

            var pins = (HashSet<int>)_pinsField.GetValue(manager);
            var notes = (Dictionary<int, string>)_notesField.GetValue(manager);
            __state = new MemoryState
            {
                OldPinned = pins.Contains(___thingID),
                OldNote = notes.TryGetValue(___thingID, out string note) ? note : null
            };
        }

        private static void MemoryWindowPostfix(int ___thingID, MemoryState __state)
        {
            if (__state == null)
                return;

            object manager = GetManager();
            if (manager == null || _pinsField == null || _notesField == null)
                return;

            var pins = (HashSet<int>)_pinsField.GetValue(manager);
            var notes = (Dictionary<int, string>)_notesField.GetValue(manager);
            bool newPinned = pins.Contains(___thingID);
            string newNote = notes.TryGetValue(___thingID, out string value) ? value : null;
            bool pinChanged = newPinned != __state.OldPinned;
            bool noteChanged = !string.Equals(newNote, __state.OldNote, StringComparison.Ordinal);

            if (!pinChanged && !noteChanged)
                return;

            if (__state.OldPinned)
                pins.Add(___thingID);
            else
                pins.Remove(___thingID);

            if (__state.OldNote == null)
                notes.Remove(___thingID);
            else
                notes[___thingID] = __state.OldNote;

            try
            {
                _syncUpdateMemory?.DoSync(null, ___thingID, newPinned, newNote ?? string.Empty);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Eternal Pawns memory sync failed: " + e.Message);
            }
        }

        private static void ClearDbPrefix(object __instance, ref ClearDbState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || __instance == null ||
                _dialogTextField == null)
                return;

            string text = _dialogTextField.GetValue(__instance) as string;
            if (!string.Equals(
                    text,
                    Translator.Translate("FP_ClearDatabaseConfirm").ToString(),
                    StringComparison.Ordinal))
                return;

            object manager = GetManager();
            if (manager == null || _poolField == null || _cacheField == null ||
                _addTicksField == null || _missionField == null)
                return;

            __state = new ClearDbState
            {
                Manager = manager,
                PoolSnapshot = SnapshotCollection(_poolField.GetValue(manager)),
                CacheSnapshot = SnapshotCollection(_cacheField.GetValue(manager)),
                AddTicksSnapshot = SnapshotCollection(_addTicksField.GetValue(manager)),
                MissionSnapshot = SnapshotCollection(_missionField.GetValue(manager)),
                PoolCount = CollectionCount(_poolField.GetValue(manager)),
                CacheCount = CollectionCount(_cacheField.GetValue(manager)),
                AddTicksCount = CollectionCount(_addTicksField.GetValue(manager)),
                MissionCount = CollectionCount(_missionField.GetValue(manager))
            };
        }

        private static void ClearDbPostfix(ClearDbState __state)
        {
            if (__state == null || __state.Manager == null)
                return;

            bool changed =
                CollectionCount(_poolField.GetValue(__state.Manager)) != __state.PoolCount ||
                CollectionCount(_cacheField.GetValue(__state.Manager)) != __state.CacheCount ||
                CollectionCount(_addTicksField.GetValue(__state.Manager)) != __state.AddTicksCount ||
                CollectionCount(_missionField.GetValue(__state.Manager)) != __state.MissionCount;

            if (!changed)
                return;

            RestoreCollection(_poolField.GetValue(__state.Manager), __state.PoolSnapshot);
            RestoreCollection(_cacheField.GetValue(__state.Manager), __state.CacheSnapshot);
            RestoreCollection(_addTicksField.GetValue(__state.Manager), __state.AddTicksSnapshot);
            RestoreCollection(_missionField.GetValue(__state.Manager), __state.MissionSnapshot);

            try
            {
                _syncClearDatabase?.DoSync(null);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Eternal Pawns clear-database sync failed: " + e.Message);
            }
        }

        private static int CollectionCount(object collection)
        {
            if (collection == null)
                return 0;
            if (collection is ICollection nonGeneric)
                return nonGeneric.Count;
            PropertyInfo count = collection.GetType().GetProperty("Count");
            return count == null ? 0 : (int)count.GetValue(collection, null);
        }

        private static void ClearCollection(object collection)
        {
            if (collection == null)
                return;
            MethodInfo clear = collection.GetType().GetMethod("Clear");
            clear?.Invoke(collection, null);
        }

        private static List<object> SnapshotCollection(object collection)
        {
            var snapshot = new List<object>();
            if (collection == null)
                return snapshot;

            if (collection is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    snapshot.Add(entry);
                return snapshot;
            }

            foreach (object item in (IEnumerable)collection)
                snapshot.Add(item);
            return snapshot;
        }

        private static void RestoreCollection(object collection, List<object> snapshot)
        {
            if (collection == null || snapshot == null)
                return;

            Type type = collection.GetType();
            MethodInfo clear = type.GetMethod("Clear");
            clear?.Invoke(collection, null);

            if (collection is IDictionary dictionary)
            {
                Type[] genericArgs = type.GetGenericArguments();
                MethodInfo add = genericArgs.Length == 2 ? type.GetMethod("Add", genericArgs) : null;
                if (add == null)
                    return;
                foreach (object item in snapshot)
                {
                    var entry = (DictionaryEntry)item;
                    add.Invoke(collection, new[] { entry.Key, entry.Value });
                }
                return;
            }

            MethodInfo addMethod = type.GetMethod("Add");
            if (addMethod == null)
                return;
            foreach (object item in snapshot)
                addMethod.Invoke(collection, new[] { item });
        }

        private sealed class MemoryState
        {
            public bool OldPinned;
            public string OldNote;
        }

        private sealed class ClearDbState
        {
            public object Manager;
            public List<object> PoolSnapshot;
            public List<object> CacheSnapshot;
            public List<object> AddTicksSnapshot;
            public List<object> MissionSnapshot;
            public int PoolCount;
            public int CacheCount;
            public int AddTicksCount;
            public int MissionCount;
        }
    }
}
