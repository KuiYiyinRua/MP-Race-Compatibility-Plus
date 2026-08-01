using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-130: retrieving the Milira leftover-supply cache desynced map 1.
    /// `TaleOfMilira_TaleOfMilira_TheMiliraSupply` is a world object whose
    /// `CaravanArrivalAction_VisitTheMiliraSupply.Arrived` -> `Notify_CaravanArrived`
    /// runs inside the synchronized world tick on both peers (the outcome and its
    /// initial letter are deterministic), but the resulting `Dialog_NodeTree`
    /// "leave" `DiaOption.action` calls `Find.LetterStack.ReceiveLetter` only on
    /// the peer that clicks it. The bundle shows that click at tick 1400665 only
    /// on the client, followed by the first map-1 Rand/TickList divergence at
    /// tick 1400678 and the desync at 1400703.
    ///
    /// Fix: after every `Dialog_NodeTree` is constructed, replace any option
    /// action that belongs to the Milira-supply compiler closure with a wrapper
    /// that dispatches a synchronized command. The command replays the peer-local
    /// original action on every peer (creating the letter and letter ID exactly
    /// once per command) and closes that peer's dialog. Singleplayer and
    /// non-Milira dialogs are untouched.
    /// </summary>
    internal static class Patch_MiliraSupplyMp
    {
        private const string SupplyTypeName = "TheTaleofMilira.TaleOfMilira_TheMiliraSupply";
        private const string LogTag = "[MP-MeowOnlineShop] MiliraSupplyMp";

        private static bool _applied;
        private static Type _supplyType;
        private static FieldInfo _dialogCurNodeField;
        private static ISyncMethod _syncLeaveMethod;
        private static bool _loggedDispatchFailure;

        private sealed class MiliraSupplyDialog
        {
            internal Dialog_NodeTree Dialog;
            internal Caravan Caravan;
            internal string OutcomeKey;
            internal Action Original;
        }

        private static readonly Dictionary<int, MiliraSupplyDialog> DialogsBySupplyId =
            new Dictionary<int, MiliraSupplyDialog>();

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            _supplyType = AccessTools.TypeByName(SupplyTypeName);
            if (_supplyType == null)
            {
                Log.Message(
                    LogTag + " skipped: The Tale of Milira is not active.");
                return;
            }

            try
            {
                Type dialogType = AccessTools.TypeByName("Verse.Dialog_NodeTree");
                _dialogCurNodeField = dialogType == null
                    ? null
                    : AccessTools.Field(dialogType, "curNode");
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_MiliraSupplyMp),
                    nameof(DialogNodeTreePostfix));

                if (dialogType == null || _dialogCurNodeField == null ||
                    postfix == null)
                {
                    Log.Warning(
                        LogTag + " target resolution failed: " +
                        $"dialog={dialogType != null} curNode={_dialogCurNodeField != null} " +
                        $"postfix={postfix != null}; the supply dialog can still desync.");
                    return;
                }

                _syncLeaveMethod = MP.RegisterSyncMethod(
                    typeof(Patch_MiliraSupplyMp),
                    nameof(SyncMiliraSupplyLeave));

                int patchedCtors = 0;
                foreach (ConstructorInfo ctor in dialogType.GetConstructors(
                             BindingFlags.Instance | BindingFlags.Public |
                             BindingFlags.NonPublic))
                {
                    harmony.Patch(
                        ctor,
                        postfix: new HarmonyMethod(postfix)
                        {
                            priority = Priority.Last
                        });
                    patchedCtors++;
                }

                Log.Message(
                    LogTag + " dialog sync active: " +
                    $"ctors patched={patchedCtors}, sync registered={_syncLeaveMethod != null}.");
            }
            catch (Exception e)
            {
                Log.Warning(LogTag + " apply failed: " + e);
            }
        }

        private static void DialogNodeTreePostfix(Dialog_NodeTree __instance)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                __instance == null || _supplyType == null ||
                _syncLeaveMethod == null || _dialogCurNodeField == null)
            {
                return;
            }

            try
            {
                DiaNode node = _dialogCurNodeField.GetValue(__instance) as DiaNode;
                if (node?.options == null)
                    return;

                foreach (DiaOption option in node.options)
                {
                    if (option?.action == null)
                        continue;
                    WrapSupplyOption(__instance, option);
                }
            }
            catch
            {
                // UI wrapping must never break dialog construction.
            }
        }

        private static void WrapSupplyOption(
            Dialog_NodeTree dialog,
            DiaOption option)
        {
            Action original = option.action;
            object closure = original.Target;
            Type closureType = closure?.GetType();
            if (closureType == null || closureType.DeclaringType != _supplyType)
                return;

            string methodName = original.Method.Name;
            if (!methodName.StartsWith("<Outcome_", StringComparison.Ordinal) ||
                methodName.IndexOf(">b__", StringComparison.Ordinal) < 0)
            {
                return;
            }

            FieldInfo supplyField = closureType
                .GetFields(BindingFlags.Instance | BindingFlags.Public |
                           BindingFlags.NonPublic)
                .FirstOrDefault(f => _supplyType.IsAssignableFrom(f.FieldType));
            FieldInfo caravanField = closureType
                .GetFields(BindingFlags.Instance | BindingFlags.Public |
                           BindingFlags.NonPublic)
                .FirstOrDefault(f => typeof(Caravan).IsAssignableFrom(f.FieldType));
            if (supplyField == null || caravanField == null)
                return;

            WorldObject supply = supplyField.GetValue(closure) as WorldObject;
            Caravan caravan = caravanField.GetValue(closure) as Caravan;
            if (supply == null)
                return;

            int start = "<Outcome_".Length;
            int end = methodName.IndexOf(">b__", start, StringComparison.Ordinal);
            if (end < 0)
                return;
            string outcomeKey = methodName.Substring(start, end - start);

            int supplyId = supply.ID;
            DialogsBySupplyId[supplyId] = new MiliraSupplyDialog
            {
                Dialog = dialog,
                Caravan = caravan,
                OutcomeKey = outcomeKey,
                Original = original
            };

            option.action = () =>
            {
                if (!MP.enabled || !MP.IsInMultiplayer ||
                    _syncLeaveMethod == null)
                {
                    original();
                    return;
                }

                try
                {
                    _syncLeaveMethod.DoSync(
                        null,
                        supplyId,
                        caravan?.ID ?? -1,
                        outcomeKey);
                }
                catch (Exception e)
                {
                    if (!_loggedDispatchFailure)
                    {
                        _loggedDispatchFailure = true;
                        Log.Warning(
                            LogTag + " dispatch failed; the supply letter is " +
                            "blocked locally to avoid a one-sided letter ID: " + e);
                    }
                }
            };
        }

        private static void SyncMiliraSupplyLeave(
            int supplyId,
            int caravanId,
            string outcomeKey)
        {
            try
            {
                if (DialogsBySupplyId.TryGetValue(supplyId, out MiliraSupplyDialog entry))
                {
                    DialogsBySupplyId.Remove(supplyId);
                    Action original = entry.Original;
                    Dialog_NodeTree dialog = entry.Dialog;
                    original?.Invoke();
                    dialog?.Close(doCloseSound: false);
                    return;
                }

                // Fallback: the local dialog is already gone, but the letter must
                // still be created on every peer with the same translated text.
                Caravan caravan = Find.World?.worldObjects?.Caravans
                    .FirstOrDefault(c => c.ID == caravanId);
                if (caravan == null || string.IsNullOrEmpty(outcomeKey))
                    return;

                Find.LetterStack?.ReceiveLetter(
                    ("LetterLabelTheTaleOfMiliraTheMiliraSupply_" + outcomeKey +
                     "_leave").Translate(),
                    ("LetterTheTaleOfMiliraTheMiliraSupply_" + outcomeKey +
                     "_leave").Translate(),
                    LetterDefOf.PositiveEvent,
                    caravan,
                    caravan.Faction);
            }
            catch (Exception e)
            {
                Log.Warning(LogTag + " replay failed: " + e);
            }
        }
    }
}
