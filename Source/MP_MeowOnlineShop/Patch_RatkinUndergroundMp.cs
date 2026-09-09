using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Ratkin Underground's radio dialog mutates a saved GameComponent, the
    /// radio comp, faction relations, quests, incidents, and world objects
    /// from local dialog buttons. `RKU_DialogueManager.TriggerDialogueEvents`
    /// and `ExecuteDialogueEvent` are stable executors, so they are patched to
    /// send sync commands. A SyncWorker reconstructs a replay-only
    /// Dialog_RKU_Radio without running its constructor's random/startup
    /// event queue.
    /// </summary>
    internal static class Patch_RatkinUndergroundMp
    {
        private const string PackageId = "rku.ratkinunderground";
        private const string RadioCompTypeName = "RatkinUnderground.Comp_RKU_Radio";
        private const string DrillingVehicleTurretTypeName = "RatkinUnderground.RKU_DrillingVehicleWithTurret";
        private const string DialogTypeName = "RatkinUnderground.Dialog_RKU_Radio";
        private const string DialogueManagerTypeName = "RatkinUnderground.RKU_DialogueManager";
        private const string DialogueEventDefTypeName = "RatkinUnderground.RKU_DialogueEventDef";

        private static bool _applied;
        private static Type _dialogType;
        private static Type _dialogueManagerType;
        private static Type _dialogueEventDefType;
        private static FieldInfo _radioField;
        private static FieldInfo _displayBuilderField;
        private static FieldInfo _fullMessageField;
        private static FieldInfo _currentCharIndexField;
        private static FieldInfo _tickCounterField;
        private static FieldInfo _isTypingField;
        private static FieldInfo _scrollPositionField;
        private static FieldInfo _dialogueEventDefNowField;
        private static FieldInfo _dialogueTextField;
        private static MethodInfo _addMessageMethod;
        private static MethodInfo _triggerDialogueEventsMethod;
        private static MethodInfo _executeDialogueEventMethod;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Ratkin Underground sync skipped (target mod not active).");
                    return;
                }

                int registeredFields = 0;
                Type radioCompType = AccessTools.TypeByName(RadioCompTypeName);
                FieldInfo isSearchJob = radioCompType == null
                    ? null
                    : AccessTools.Field(radioCompType, "isSearchJob");
                if (isSearchJob != null)
                {
                    try
                    {
                        MP.RegisterSyncField(isSearchJob);
                        registeredFields++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Ratkin Underground isSearchJob sync field failed: " + e.Message);
                    }
                }

                Type drillingVehicleTurret = AccessTools.TypeByName(DrillingVehicleTurretTypeName);
                FieldInfo holdFire = drillingVehicleTurret == null
                    ? null
                    : AccessTools.Field(drillingVehicleTurret, "holdFire");
                if (holdFire != null)
                {
                    try
                    {
                        MP.RegisterSyncField(holdFire);
                        registeredFields++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Ratkin Underground drilling vehicle holdFire sync field failed: " + e.Message);
                    }
                }

                _dialogType = AccessTools.TypeByName(DialogTypeName);
                _dialogueManagerType = AccessTools.TypeByName(DialogueManagerTypeName);
                _dialogueEventDefType = AccessTools.TypeByName(DialogueEventDefTypeName);
                _radioField = _dialogType == null ? null : AccessTools.Field(_dialogType, "radio");
                _displayBuilderField = _dialogType == null ? null : AccessTools.Field(_dialogType, "displayBuilder");
                _fullMessageField = _dialogType == null ? null : AccessTools.Field(_dialogType, "fullMessage");
                _currentCharIndexField = _dialogType == null ? null : AccessTools.Field(_dialogType, "currentCharIndex");
                _tickCounterField = _dialogType == null ? null : AccessTools.Field(_dialogType, "tickCounter");
                _isTypingField = _dialogType == null ? null : AccessTools.Field(_dialogType, "isTyping");
                _scrollPositionField = _dialogType == null ? null : AccessTools.Field(_dialogType, "scrollPosition");
                _dialogueEventDefNowField = _dialogType == null
                    ? null
                    : AccessTools.Field(_dialogType, "dialogueEventDefNow");
                _dialogueTextField = _dialogueEventDefType == null
                    ? null
                    : AccessTools.Field(_dialogueEventDefType, "dialogueText");
                _addMessageMethod = _dialogType == null
                    ? null
                    : AccessTools.Method(_dialogType, "AddMessage", new[] { typeof(string) });
                _triggerDialogueEventsMethod = _dialogueManagerType == null
                    ? null
                    : AccessTools.Method(
                        _dialogueManagerType,
                        "TriggerDialogueEvents",
                        new[] { _dialogType, typeof(string) });
                _executeDialogueEventMethod = _dialogueManagerType == null
                    ? null
                    : AccessTools.Method(
                        _dialogueManagerType,
                        "ExecuteDialogueEvent",
                        new[] { _dialogueEventDefType, _dialogType });

                if (_dialogType == null || _dialogueManagerType == null ||
                    _dialogueEventDefType == null || _radioField == null ||
                    _displayBuilderField == null || _fullMessageField == null ||
                    _currentCharIndexField == null || _tickCounterField == null ||
                    _isTypingField == null || _dialogueEventDefNowField == null ||
                    _dialogueTextField == null || _addMessageMethod == null ||
                    _triggerDialogueEventsMethod == null ||
                    _executeDialogueEventMethod == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin Underground dialogue targets not resolved; only sync fields remain active.");
                    return;
                }

                try
                {
                    MP.RegisterSyncWorker<object>(
                        DialogSyncer,
                        _dialogType,
                        false,
                        false);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin Underground dialog SyncWorker failed: " + e.Message);
                    return;
                }

                MethodInfo syncTrigger = AccessTools.Method(
                    typeof(Patch_RatkinUndergroundMp),
                    nameof(SyncTriggerDialogueEvents),
                    new[] { typeof(int), typeof(string) });
                MethodInfo syncExecute = AccessTools.Method(
                    typeof(Patch_RatkinUndergroundMp),
                    nameof(SyncExecuteDialogueEvent),
                    new[] { typeof(string), typeof(int) });
                if (syncTrigger == null || syncExecute == null)
                    return;

                try
                {
                    MP.RegisterSyncMethod(syncTrigger, null);
                    MP.RegisterSyncMethod(syncExecute, null);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Ratkin Underground dialogue sync registration failed: " + e.Message);
                    return;
                }

                MethodInfo triggerPrefix = AccessTools.Method(
                    typeof(Patch_RatkinUndergroundMp),
                    nameof(TriggerDialogueEventsPrefix));
                MethodInfo executePrefix = AccessTools.Method(
                    typeof(Patch_RatkinUndergroundMp),
                    nameof(ExecuteDialogueEventPrefix));

                if (triggerPrefix == null || executePrefix == null)
                    return;

                harmony.Patch(
                    _triggerDialogueEventsMethod,
                    prefix: new HarmonyMethod(triggerPrefix));
                harmony.Patch(
                    _executeDialogueEventMethod,
                    prefix: new HarmonyMethod(executePrefix));

                Log.Message(
                    "[MP-MeowOnlineShop] Ratkin Underground MP patch active: " +
                    "sync fields=" + registeredFields + ", dialogue sync=2 methods.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Ratkin Underground MP compat restore failed: " + e.Message);
            }
        }

        private static bool TriggerDialogueEventsPrefix(object __0, object __1)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;

            Thing radio = GetRadio(__0);
            if (radio == null)
                return true;

            string triggerType = __1 as string ?? "";
            SyncTriggerDialogueEvents(radio.thingIDNumber, triggerType);
            return false;
        }

        private static bool ExecuteDialogueEventPrefix(object __0, object __1)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;

            Thing radio = GetRadio(__1);
            string defName = (__0 as Def)?.defName;
            if (radio == null || string.IsNullOrEmpty(defName))
                return true;

            SyncExecuteDialogueEvent(defName, radio.thingIDNumber);
            return false;
        }

        public static void SyncTriggerDialogueEvents(int radioThingId, string triggerType)
        {
            Thing radio = FindThingById(radioThingId);
            object dialog = CreateReplayDialog(radio);
            if (dialog == null || _triggerDialogueEventsMethod == null)
                return;

            _triggerDialogueEventsMethod.Invoke(null, new object[] { dialog, triggerType ?? "" });
            RefreshOpenDialog(radio, dialog);
        }

        public static void SyncExecuteDialogueEvent(string defName, int radioThingId)
        {
            if (string.IsNullOrEmpty(defName))
                return;

            Thing radio = FindThingById(radioThingId);
            object dialog = CreateReplayDialog(radio);
            object dialogueEvent = GetDialogueEventDef(defName);
            if (dialog == null || dialogueEvent == null || _executeDialogueEventMethod == null)
                return;

            _executeDialogueEventMethod.Invoke(null, new object[] { dialogueEvent, dialog });
            RefreshOpenDialog(radio, dialog);
        }

        private static void RefreshOpenDialog(Thing radio, object replayDialog)
        {
            if (radio == null || replayDialog == null || _dialogType == null ||
                _dialogueEventDefNowField == null || _dialogueTextField == null ||
                _addMessageMethod == null)
            {
                return;
            }

            string text = null;
            object dialogueEvent = _dialogueEventDefNowField.GetValue(replayDialog);
            if (dialogueEvent != null)
                text = _dialogueTextField.GetValue(dialogueEvent) as string;
            if (string.IsNullOrEmpty(text))
                return;

            foreach (Window window in Find.WindowStack.Windows)
            {
                if (window == null || !_dialogType.IsInstanceOfType(window))
                    continue;

                if (ReferenceEquals(GetRadio(window), radio))
                {
                    try
                    {
                        _addMessageMethod.Invoke(window, new object[] { text });
                    }
                    catch
                    {
                        // UI refresh is best-effort; simulation replay already succeeded.
                    }
                }
            }
        }

        private static Thing GetRadio(object dialog)
        {
            if (dialog == null || _radioField == null)
                return null;
            return _radioField.GetValue(dialog) as Thing;
        }

        private static object CreateReplayDialog(Thing radio)
        {
            if (radio == null || _dialogType == null)
                return null;

            object dialog = FormatterServices.GetUninitializedObject(_dialogType);
            _radioField?.SetValue(dialog, radio);
            _displayBuilderField?.SetValue(dialog, new StringBuilder());
            _fullMessageField?.SetValue(dialog, "");
            _currentCharIndexField?.SetValue(dialog, 0);
            _tickCounterField?.SetValue(dialog, 0);
            _isTypingField?.SetValue(dialog, false);
            return dialog;
        }

        private static object GetDialogueEventDef(string defName)
        {
            Type defDatabaseType = typeof(DefDatabase<>).MakeGenericType(_dialogueEventDefType);
            MethodInfo getNamed = AccessTools.Method(
                defDatabaseType,
                "GetNamedSilentFail",
                new[] { typeof(string) });
            return getNamed?.Invoke(null, new object[] { defName });
        }

        private static Thing FindThingById(int thingId)
        {
            if (Find.Maps != null)
            {
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    Map map = Find.Maps[m];
                    if (map?.listerThings?.AllThings == null)
                        continue;

                    for (int i = 0; i < map.listerThings.AllThings.Count; i++)
                    {
                        Thing thing = map.listerThings.AllThings[i];
                        if (thing != null && thing.thingIDNumber == thingId)
                            return thing;
                    }
                }
            }

            return null;
        }

        private static void DialogSyncer(SyncWorker sync, ref object inst)
        {
            if (sync.isWriting)
            {
                string id = null;
                Thing radio = inst == null ? null : _radioField?.GetValue(inst) as Thing;
                if (radio != null)
                    id = radio.thingIDNumber.ToString();
                sync.Bind(ref id);
                return;
            }

            string loadId = null;
            sync.Bind(ref loadId);
            if (int.TryParse(loadId, out int thingId))
                inst = CreateReplayDialog(FindThingById(thingId));
        }
    }
}
