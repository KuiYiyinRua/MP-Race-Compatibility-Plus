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
    /// Multiplayer serializes PersistentDialog with its owning map, so PersistentDialog.Click is
    /// normally queued on that map. ChoiceLetter_AcceptJoiner and ChoiceLetter_AcceptCreepJoiner
    /// options mutate quests, world pawns, the letter stack, and the departing creep-joiner lord.
    /// Replaying the click by option index is fragile under async time: if the letter's current
    /// node has fewer options on one peer (for example because its pawn is no longer spawned),
    /// the replay throws inside the map command and only the other peer mutates state. Route the
    /// durable signal and optional letter cleanup through a primitive-only global command; the signal must not depend on a peer-local Letter reference.
    /// </summary>
    internal static class Patch_AcceptJoinerWorldCommand
    {
        private const int MaxWarnings = 8;

        private static Type _persistentDialogType;
        private static Type _multiplayerMapCompType;
        private static MethodInfo _clickMethod;
        private static PropertyInfo _dialogProperty;
        private static FieldInfo _currentNodeField;
        private static FieldInfo _mapField;
        private static FieldInfo _idField;
        private static FieldInfo _dialogsField;

        private static FieldInfo _letterIdField;
        private static FieldInfo _joinerAcceptSignalField;
        private static FieldInfo _joinerRejectSignalField;
        private static FieldInfo _creepAcceptSignalField;
        private static FieldInfo _creepCaptureSignalField;
        private static FieldInfo _creepRejectSignalField;
        private static FieldInfo _creepPawnField;

        private static int _warningCount;

        public static void Apply(Harmony harmony)
        {
            if (!MP.enabled)
                return;

            _persistentDialogType =
                AccessTools.TypeByName("Multiplayer.Client.PersistentDialog") ??
                AccessTools.TypeByName("Multiplayer.Client.Persistent.PersistentDialog");
            _multiplayerMapCompType = AccessTools.TypeByName("Multiplayer.Client.MultiplayerMapComp")
                ?? ResolveTypeBySimpleName("MultiplayerMapComp");
            _clickMethod = _persistentDialogType == null
                ? null
                : AccessTools.Method(_persistentDialogType, "Click", new[] { typeof(int), typeof(int) });
            _dialogProperty = _persistentDialogType == null
                ? null
                : AccessTools.Property(_persistentDialogType, "Dialog");
            _currentNodeField = AccessTools.Field(typeof(Dialog_NodeTree), "curNode");
            _mapField = _persistentDialogType == null
                ? null
                : AccessTools.Field(_persistentDialogType, "map");
            _idField = _persistentDialogType == null
                ? null
                : AccessTools.Field(_persistentDialogType, "id");
            _dialogsField = _multiplayerMapCompType == null
                ? null
                : AccessTools.Field(_multiplayerMapCompType, "mapDialogs");

            _letterIdField = AccessTools.Field(typeof(Letter), "ID");
            _joinerAcceptSignalField = AccessTools.Field(typeof(ChoiceLetter_AcceptJoiner), "signalAccept");
            _joinerRejectSignalField = AccessTools.Field(typeof(ChoiceLetter_AcceptJoiner), "signalReject");
            _creepAcceptSignalField = AccessTools.Field(typeof(ChoiceLetter_AcceptCreepJoiner), "signalAccept");
            _creepCaptureSignalField = AccessTools.Field(typeof(ChoiceLetter_AcceptCreepJoiner), "signalCapture");
            _creepRejectSignalField = AccessTools.Field(typeof(ChoiceLetter_AcceptCreepJoiner), "signalReject");
            _creepPawnField = AccessTools.Field(typeof(ChoiceLetter_AcceptCreepJoiner), "pawn");

            if (_clickMethod == null || _dialogProperty == null || _currentNodeField == null ||
                _mapField == null || _idField == null || _multiplayerMapCompType == null ||
                _dialogsField == null || _letterIdField == null ||
                _joinerAcceptSignalField == null || _joinerRejectSignalField == null ||
                _creepAcceptSignalField == null || _creepCaptureSignalField == null ||
                _creepRejectSignalField == null || _creepPawnField == null)
            {
                Warn(ResolutionFailureDetail());
                return;
            }

            MP.RegisterSyncMethod(typeof(Patch_AcceptJoinerWorldCommand), nameof(SyncAcceptJoinerSignalGlobal))
                .SetContext(SyncContext.None);
            harmony.Patch(
                _clickMethod,
                prefix: new HarmonyMethod(typeof(Patch_AcceptJoinerWorldCommand), nameof(PersistentDialogClickPrefix))
                {
                    priority = Priority.First
                });

            Log.Message("[MP-MeowOnlineShop] AcceptJoiner/CreepJoiner persistent-dialog clicks routed to global command queue.");
        }

        private static bool PersistentDialogClickPrefix(object __instance, int __0, int __1)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || __instance == null)
                return true;

            if (!TryResolveLetterAction(
                    __instance,
                    __1,
                    out object letter,
                    out string signal,
                    out bool removeLetter,
                    out bool requirePawnSpawned,
                    out bool pawnWasSpawned))
            {
                return true;
            }

            Map map = _mapField.GetValue(__instance) as Map;
            if (map == null)
            {
                Warn("AcceptJoiner dialog had no owning map; falling back to Multiplayer's normal dialog sync.");
                return true;
            }

            int dialogId = (int)_idField.GetValue(__instance);
            int letterId = (int)_letterIdField.GetValue(letter);
            SyncAcceptJoinerSignalGlobal(
                map.uniqueID,
                dialogId,
                letterId,
                signal,
                removeLetter,
                requirePawnSpawned,
                pawnWasSpawned);
            return false;
        }

        private static bool TryResolveLetterAction(
            object session,
            int optionIndex,
            out object letter,
            out string signal,
            out bool removeLetter,
            out bool requirePawnSpawned,
            out bool pawnWasSpawned)
        {
            letter = null;
            signal = null;
            removeLetter = false;
            requirePawnSpawned = false;
            pawnWasSpawned = false;

            Dialog_NodeTree dialog = _dialogProperty.GetValue(session, null) as Dialog_NodeTree;
            DiaNode node = dialog == null ? null : _currentNodeField.GetValue(dialog) as DiaNode;
            if (node?.options == null || optionIndex < 0 || optionIndex >= node.options.Count)
                return false;

            Action action = node.options[optionIndex]?.action;
            object target = action?.Target;
            if (target is ChoiceLetter_AcceptJoiner)
            {
                if (optionIndex == 0)
                {
                    signal = ReadSignal(target, _joinerAcceptSignalField);
                    removeLetter = true;
                    requirePawnSpawned = false;
                }
                else if (optionIndex == 1)
                {
                    signal = ReadSignal(target, _joinerRejectSignalField);
                    removeLetter = true;
                    requirePawnSpawned = false;
                }
                else
                {
                    return false;
                }

                letter = target;
                return !string.IsNullOrEmpty(signal);
            }

            if (target is ChoiceLetter_AcceptCreepJoiner)
            {
                if (optionIndex == 0)
                {
                    signal = ReadSignal(target, _creepAcceptSignalField);
                    removeLetter = true;
                    requirePawnSpawned = true;
                }
                else if (optionIndex == 1)
                {
                    signal = ReadSignal(target, _creepCaptureSignalField);
                    removeLetter = false;
                    requirePawnSpawned = true;
                }
                else if (optionIndex == 2)
                {
                    signal = ReadSignal(target, _creepRejectSignalField);
                    removeLetter = true;
                    requirePawnSpawned = true;
                }
                else
                {
                    return false;
                }

                pawnWasSpawned = ReadCreepPawnSpawned(target);
                letter = target;
                return !string.IsNullOrEmpty(signal);
            }

            return false;
        }

        private static bool ReadCreepPawnSpawned(object letter)
        {
            try
            {
                Pawn pawn = _creepPawnField?.GetValue(letter) as Pawn;
                return pawn != null && pawn.Spawned;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadSignal(object letter, FieldInfo field)
        {
            try
            {
                return field?.GetValue(letter) as string;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// All arguments are primitives/strings, deliberately leaving the Multiplayer
        /// serialization context without a map so the command is scheduled as Global. The letter id is only used for optional cleanup; the click-time pawn-spawn result is synchronized.
        /// </summary>
        private static void SyncAcceptJoinerSignalGlobal(
            int mapUniqueId,
            int dialogId,
            int letterId,
            string signal,
            bool removeLetter,
            bool requirePawnSpawned,
            bool pawnWasSpawned)
        {
            Letter letter = FindLetterById(letterId);
            if (letter == null)
            {
                Warn($"AcceptJoiner global replay could not resolve letter id={letterId} map={mapUniqueId} dialog={dialogId}; continuing with the durable signal.");
            }

            if (requirePawnSpawned && !pawnWasSpawned)
            {
                Warn($"AcceptJoiner global replay skipped letter id={letterId}: creep joiner pawn was not spawned when clicked.");
                return;
            }

            if (!string.IsNullOrEmpty(signal))
                Find.SignalManager.SendSignal(new Signal(signal));

            if (removeLetter && letter != null)
                Find.LetterStack.RemoveLetter(letter);

            CloseDialog(mapUniqueId, dialogId);
        }

        private static void CloseDialog(int mapUniqueId, int dialogId)
        {
            Map map = null;
            object session = null;
            try
            {
                map = Find.Maps?.FirstOrDefault(candidate => candidate != null && candidate.uniqueID == mapUniqueId);
                session = map == null ? null : ResolveDialogSession(map, dialogId);
                Dialog_NodeTree dialog = session == null
                    ? null
                    : _dialogProperty.GetValue(session, null) as Dialog_NodeTree;
                if (dialog != null && Find.WindowStack.IsOpen(dialog))
                    dialog.Close();
            }
            catch
            {
                // dialog close is presentation-only; never fail the replay
            }
            finally
            {
                // A global replay can run while this peer is on another map, so the
                // dialog may not be in the local WindowStack. Always remove the
                // persistent session or Multiplayer's ForceShowDialogs will reopen it.
                RemoveDialogSession(map, session);
            }
        }

        private static void RemoveDialogSession(Map map, object session)
        {
            if (map == null || session == null || _multiplayerMapCompType == null || _dialogsField == null)
                return;

            try
            {
                MapComponent comp = map.components?.FirstOrDefault(
                    candidate => candidate != null && _multiplayerMapCompType.IsInstanceOfType(candidate));
                if (comp == null)
                    return;

                if (_dialogsField.GetValue(comp) is System.Collections.IList sessions)
                    sessions.Remove(session);
            }
            catch
            {
                // Cleanup is best effort and must not change the signal replay result.
            }
        }

        private static object ResolveDialogSession(Map map, int dialogId)
        {
            MapComponent comp = map.components?.FirstOrDefault(
                candidate => candidate != null && _multiplayerMapCompType.IsInstanceOfType(candidate));
            System.Collections.IEnumerable sessions = comp == null
                ? null
                : _dialogsField.GetValue(comp) as System.Collections.IEnumerable;
            if (sessions == null)
                return null;

            foreach (object session in sessions)
            {
                if (session != null && (int)_idField.GetValue(session) == dialogId)
                    return session;
            }

            return null;
        }

        private static Letter FindLetterById(int letterId)
        {
            LetterStack stack = Find.LetterStack;
            List<Letter> letters = stack?.LettersListForReading;
            if (letters == null)
                return null;

            for (int i = 0; i < letters.Count; i++)
            {
                Letter letter = letters[i];
                if (letter != null && letter.ID == letterId)
                    return letter;
            }

            return null;
        }

        private static Type ResolveTypeBySimpleName(string simpleName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null || asm.IsDynamic)
                    continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (Type t in types)
                {
                    if (t == null)
                        continue;
                    if (t.Name == simpleName ||
                        (t.FullName != null &&
                         t.FullName.EndsWith("." + simpleName, StringComparison.Ordinal)))
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        private static string ResolutionFailureDetail()
        {
            return
                "AcceptJoiner world-command target resolution failed; patch was not installed. " +
                $"persistentDialog={_persistentDialogType != null}, mapComp={_multiplayerMapCompType != null}, " +
                $"click={_clickMethod != null}, dialogProperty={_dialogProperty != null}, curNode={_currentNodeField != null}, " +
                $"map={_mapField != null}, id={_idField != null}, dialogs={_dialogsField != null}, " +
                $"letterId={_letterIdField != null}, " +
                $"joinerSignals={_joinerAcceptSignalField != null && _joinerRejectSignalField != null}, " +
                $"creepSignals={_creepAcceptSignalField != null && _creepCaptureSignalField != null && _creepRejectSignalField != null && _creepPawnField != null}.";
        }

        private static void Warn(string message)
        {
            if (_warningCount++ < MaxWarnings)
                Log.Warning("[MP-MeowOnlineShop] " + message);
        }
    }
}
