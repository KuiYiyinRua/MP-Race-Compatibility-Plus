using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Synchronizes the real Custom Quest Framework option selection.
    /// The option's complete multicast action (CQF actions, required-item consumption,
    /// node transition, and close behavior) executes once inside the MP command.
    /// </summary>
    internal static class Patch_RigorMortisStoryDialogs
    {
        private const string LogTag = "[MP-MeowOnlineShop] RigorMortisStoryDialogs";
        private static readonly Type CqfWindowType = AccessTools.TypeByName("QuestEditor_Library.CQFDialogTreeWindow");
        private static readonly Type CqfOptionType = AccessTools.TypeByName("QuestEditor_Library.DialogElement_Option");
        private static readonly Type CqfTreeDefType = AccessTools.TypeByName("QuestEditor_Library.DialogTreeDef");
        private static readonly HashSet<string> RuntimeLogOnce = new HashSet<string>();

        private static readonly FieldInfo OptionTextField = AccessTools.Field(CqfOptionType, "text");
        private static readonly FieldInfo OptionDisabledField = AccessTools.Field(CqfOptionType, "disabled");
        private static readonly FieldInfo OptionDisableReasonField = AccessTools.Field(CqfOptionType, "disableReason");
        private static readonly FieldInfo OptionActionField = AccessTools.Field(CqfOptionType, "action");
        private static readonly FieldInfo OptionNextIndexField = AccessTools.Field(CqfOptionType, "nextIndex");

        private static readonly FieldInfo WindowInterviewerField = AccessTools.Field(CqfWindowType, "interviewer");
        private static readonly FieldInfo WindowIntervieweeField = AccessTools.Field(CqfWindowType, "interviewee");
        private static readonly FieldInfo WindowQuestField = AccessTools.Field(CqfWindowType, "quest");
        private static readonly FieldInfo WindowTreeField = AccessTools.Field(CqfWindowType, "tree");
        private static readonly FieldInfo WindowCurNodeField = AccessTools.Field(CqfWindowType, "curNode");
        private static readonly FieldInfo WindowOptionsField = AccessTools.Field(CqfWindowType, "options");
        private static readonly FieldInfo WindowNextOptionsField = AccessTools.Field(CqfWindowType, "nextOptions");

        private static ISyncMethod SyncSelectOptionMethod;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                Log.Warning($"{LogTag}: apply aborted, harmony is null.");
                return;
            }

            var draw = CqfOptionType?
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m =>
                    m.Name == "Draw" &&
                    m.GetParameters().Length == 2 &&
                    m.GetParameters()[0].ParameterType == typeof(float).MakeByRefType() &&
                    m.GetParameters()[1].ParameterType == typeof(Rect));
            var prefix = AccessTools.Method(typeof(Patch_RigorMortisStoryDialogs), nameof(DialogElementOptionDrawPrefix));
            var sync = AccessTools.Method(typeof(Patch_RigorMortisStoryDialogs), nameof(SyncSelectOption));
            var goToNode = AccessTools.Method(CqfWindowType, "GoToNode", new[] { typeof(int) });
            var goToNodePrefix = AccessTools.Method(typeof(Patch_RigorMortisStoryDialogs), nameof(GoToNodeRandPrefix));
            var goToNodePostfix = AccessTools.Method(typeof(Patch_RigorMortisStoryDialogs), nameof(GoToNodeRandPostfix));
            var goToNodeFinalizer = AccessTools.Method(typeof(Patch_RigorMortisStoryDialogs), nameof(GoToNodeRandFinalizer));

            if (CqfWindowType == null || CqfOptionType == null || CqfTreeDefType == null ||
                draw == null || prefix == null || sync == null ||
                goToNode == null || goToNodePrefix == null ||
                goToNodePostfix == null || goToNodeFinalizer == null ||
                OptionTextField == null || OptionDisabledField == null ||
                OptionDisableReasonField == null || OptionActionField == null ||
                OptionNextIndexField == null ||
                WindowInterviewerField == null || WindowIntervieweeField == null ||
                WindowQuestField == null || WindowTreeField == null ||
                WindowCurNodeField == null || WindowOptionsField == null ||
                WindowNextOptionsField == null)
            {
                Log.Warning(
                    $"{LogTag}: required CQF 1.6 symbols missing; " +
                    $"window={CqfWindowType != null} option={CqfOptionType != null} tree={CqfTreeDefType != null} " +
                    $"draw={draw != null} fields={AllFieldsResolved()}.");
                return;
            }

            try
            {
                SyncSelectOptionMethod = MP.RegisterSyncMethod(sync, null);
                harmony.Patch(draw, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                harmony.Patch(
                    goToNode,
                    prefix: new HarmonyMethod(goToNodePrefix) { priority = Priority.First },
                    postfix: new HarmonyMethod(goToNodePostfix) { priority = Priority.Last },
                    finalizer: new HarmonyMethod(goToNodeFinalizer) { priority = Priority.Last });
                Log.Message(
                    $"{LogTag}: CQF real-option sync ready; " +
                    $"draw={draw.DeclaringType?.FullName}.{draw.Name}, sync={sync.Name}, " +
                    $"goToNodeRandIsolation=true, fields=true.");
            }
            catch (Exception e)
            {
                Log.Warning($"{LogTag}: CQF real-option sync setup failed: {e}");
            }
        }

        public static bool DialogElementOptionDrawPrefix(object __instance, ref float y, Rect inRect)
        {
            if (__instance == null || !CqfOptionType.IsInstanceOfType(__instance))
                return true;

            string text = OptionTextField.GetValue(__instance) as string ?? string.Empty;
            bool disabled = OptionDisabledField.GetValue(__instance) is bool value && value;
            string reason = OptionDisableReasonField.GetValue(__instance) as string;
            float height = Text.CalcHeight(text, inRect.width);
            string label = text + (disabled ? $"({reason})" : null);

            bool clicked = Widgets.ButtonText(
                new Rect(inRect.x, y, inRect.width, height),
                label,
                false,
                !disabled,
                disabled ? Color.gray : Color.white,
                !disabled);
            y += height;

            if (!clicked || disabled)
                return false;

            var action = OptionActionField.GetValue(__instance) as Action;
            // A UI click is always an input boundary. SyncSelectOption invokes the
            // option action directly and never re-enters Draw, so treating a Draw
            // call observed during command playback as safe local execution only
            // bypasses the command stream and lets one peer advance the story alone.
            if (!MP.enabled || !MP.IsInMultiplayer)
            {
                action?.Invoke();
                return false;
            }

            if (SyncSelectOptionMethod == null)
            {
                LogRuntimeOnce("missing-sync", "option click blocked because SyncSelectOption is unavailable.", true);
                return false;
            }

            if (!TryBuildPayload(
                    __instance,
                    out var interviewer,
                    out var interviewee,
                    out var treeDefName,
                    out var questId,
                    out var nodeIndex,
                    out var optionIndex))
            {
                LogRuntimeOnce("payload-failed", "option click blocked because its CQF window/context could not be resolved.", true);
                return false;
            }

            try
            {
                Log.Message(
                    $"{LogTag}: request option tree={treeDefName} node={nodeIndex} option={optionIndex} " +
                    $"interviewer={interviewer?.thingIDNumber ?? 0} interviewee={interviewee?.thingIDNumber ?? 0} quest={questId}.");
                SyncSelectOptionMethod.DoSync(
                    null,
                    interviewer,
                    interviewee,
                    treeDefName,
                    questId,
                    nodeIndex,
                    optionIndex);
            }
            catch (Exception e)
            {
                Log.Warning($"{LogTag}: option sync dispatch failed: {e}");
            }

            return false;
        }

        public static void GoToNodeRandPrefix(object __instance, int index, ref bool __state)
        {
            __state = false;
            if (!MP.enabled || !MP.IsInMultiplayer)
                return;

            int interviewerId = 0;
            int treeHash = 0;
            try
            {
                interviewerId =
                    (WindowInterviewerField?.GetValue(__instance) as Thing)?.thingIDNumber ?? 0;
                string treeDefName =
                    (WindowTreeField?.GetValue(__instance) as Def)?.defName ?? string.Empty;
                treeHash = GenText.StableStringHash(treeDefName);
            }
            catch
            {
            }

            Rand.PushState(Gen.HashCombineInt(
                Gen.HashCombineInt(interviewerId, treeHash),
                index));
            __state = true;
        }

        public static void GoToNodeRandPostfix(ref bool __state)
        {
            if (!__state)
                return;
            Rand.PopState();
            __state = false;
        }

        public static Exception GoToNodeRandFinalizer(Exception __exception, ref bool __state)
        {
            if (__state)
            {
                Rand.PopState();
                __state = false;
            }
            return __exception;
        }

        public static void SyncSelectOption(
            Thing interviewer,
            Thing interviewee,
            string treeDefName,
            int questId,
            int nodeIndex,
            int optionIndex)
        {
            try
            {
                Quest quest = ResolveQuest(questId);
                object localWindow = FindMatchingWindow(
                    interviewer,
                    interviewee,
                    treeDefName,
                    nodeIndex);

                // Always execute the option through an identically reconstructed
                // transient window on every peer. Using the live window on the
                // initiating peer but a transient one elsewhere makes CQF closure
                // state and any construction-time work differ inside the command.
                object window = CreateTransientWindow(
                    interviewer,
                    interviewee,
                    treeDefName,
                    quest,
                    nodeIndex);

                if (window == null)
                {
                    Log.Warning($"{LogTag}: sync option failed; cannot reconstruct CQF tree {treeDefName} node={nodeIndex}.");
                    return;
                }

                var options = GetOptionsForNode(window);
                if (options == null || optionIndex < 0 || optionIndex >= options.Count)
                {
                    Log.Warning(
                        $"{LogTag}: sync option failed; invalid rendered option tree={treeDefName} " +
                        $"node={nodeIndex} option={optionIndex} count={options?.Count ?? -1}.");
                    return;
                }

                object option = options[optionIndex];
                if (OptionDisabledField.GetValue(option) is bool disabled && disabled)
                {
                    Log.Warning($"{LogTag}: sync option ignored because option is disabled: tree={treeDefName} node={nodeIndex} option={optionIndex}.");
                    return;
                }

                var action = OptionActionField.GetValue(option) as Action;
                if (action == null)
                {
                    Log.Warning($"{LogTag}: sync option failed; action is null: tree={treeDefName} node={nodeIndex} option={optionIndex}.");
                    return;
                }

                action.Invoke();

                // The reconstructed action performs all gameplay mutations and its
                // own transient transition identically. Advance only the peer-local
                // visible window without invoking that action a second time. CQF's
                // text/option construction may consume Verse.Rand, so isolate this
                // deliberately peer-local UI update from the synchronized Rand stack.
                if (localWindow != null)
                {
                    Rand.PushState(Gen.HashCombineInt(
                        Gen.HashCombineInt(interviewer?.thingIDNumber ?? 0, nodeIndex),
                        optionIndex));
                    try
                    {
                        object nextIndexValue = OptionNextIndexField.GetValue(option);
                        if (nextIndexValue != null)
                        {
                            int nextIndex = Convert.ToInt32(nextIndexValue);
                            AccessTools.Method(
                                    CqfWindowType,
                                    "GoToNode",
                                    new[] { typeof(int) })
                                ?.Invoke(localWindow, new object[] { nextIndex });
                        }
                        else
                        {
                            (localWindow as Window)?.Close();
                        }
                    }
                    finally
                    {
                        Rand.PopState();
                    }
                }

                Log.Message(
                    $"{LogTag}: applied option tree={treeDefName} node={nodeIndex} option={optionIndex} " +
                    $"quest={questId} reconstructed=true localWindow={localWindow != null}.");
            }
            catch (Exception e)
            {
                Log.Warning($"{LogTag}: SyncSelectOption failed: {e}");
                throw;
            }
        }

        // Kept for binary/save compatibility with the retired mirror window type.
        internal static void MarkMirrorWindowOpen(int sessionId, Dialog_MpRigorStory window)
        {
        }

        internal static void MarkMirrorWindowClosed(int sessionId, Dialog_MpRigorStory window)
        {
        }

        private static bool TryBuildPayload(
            object selectedOption,
            out Thing interviewer,
            out Thing interviewee,
            out string treeDefName,
            out int questId,
            out int nodeIndex,
            out int optionIndex)
        {
            interviewer = null;
            interviewee = null;
            treeDefName = null;
            questId = -1;
            nodeIndex = -1;
            optionIndex = -1;

            var windows = Find.WindowStack?.Windows;
            if (windows == null)
                return false;

            for (int wi = windows.Count - 1; wi >= 0; wi--)
            {
                var window = windows[wi];
                if (window == null || !CqfWindowType.IsInstanceOfType(window))
                    continue;

                var options = WindowOptionsField.GetValue(window) as IList;
                if (options == null)
                    continue;

                for (int oi = 0; oi < options.Count; oi++)
                {
                    if (!ReferenceEquals(options[oi], selectedOption))
                        continue;

                    interviewer = WindowInterviewerField.GetValue(window) as Thing;
                    interviewee = WindowIntervieweeField.GetValue(window) as Thing;
                    var quest = WindowQuestField.GetValue(window) as Quest;
                    var tree = WindowTreeField.GetValue(window) as Def;
                    var node = WindowCurNodeField.GetValue(window);

                    treeDefName = tree?.defName;
                    questId = quest?.id ?? -1;
                    nodeIndex = GetNodeIndex(node);
                    optionIndex = oi;
                    return !string.IsNullOrEmpty(treeDefName) && nodeIndex >= 0;
                }
            }

            return false;
        }

        private static object FindMatchingWindow(
            Thing interviewer,
            Thing interviewee,
            string treeDefName,
            int nodeIndex)
        {
            var windows = Find.WindowStack?.Windows;
            if (windows == null)
                return null;

            for (int i = windows.Count - 1; i >= 0; i--)
            {
                var window = windows[i];
                if (window == null || !CqfWindowType.IsInstanceOfType(window))
                    continue;
                if (!ReferenceEquals(WindowInterviewerField.GetValue(window), interviewer))
                    continue;
                if (!ReferenceEquals(WindowIntervieweeField.GetValue(window), interviewee))
                    continue;
                if (!string.Equals((WindowTreeField.GetValue(window) as Def)?.defName, treeDefName, StringComparison.Ordinal))
                    continue;
                if (GetNodeIndex(WindowCurNodeField.GetValue(window)) != nodeIndex)
                    continue;
                return window;
            }

            return null;
        }

        private static object CreateTransientWindow(
            Thing interviewer,
            Thing interviewee,
            string treeDefName,
            Quest quest,
            int nodeIndex)
        {
            object tree = ResolveTreeDef(treeDefName);
            if (tree == null)
                return null;

            var create = AccessTools.Method(
                tree.GetType(),
                "CreateCQFDialog",
                new[] { typeof(Thing), typeof(Thing), typeof(Quest) });
            object window = create?.Invoke(tree, new object[] { interviewer, interviewee, quest });
            if (window == null || !CqfWindowType.IsInstanceOfType(window))
                return null;

            if (nodeIndex != 0)
            {
                var goToNode = AccessTools.Method(CqfWindowType, "GoToNode", new[] { typeof(int) });
                goToNode?.Invoke(window, new object[] { nodeIndex });
            }

            return window;
        }

        private static IList GetOptionsForNode(object window)
        {
            var options = WindowOptionsField.GetValue(window) as IList;
            if (options != null && options.Count > 0)
                return options;
            return WindowNextOptionsField.GetValue(window) as IList;
        }

        private static object ResolveTreeDef(string defName)
        {
            if (CqfTreeDefType == null || string.IsNullOrEmpty(defName))
                return null;

            var databaseType = typeof(DefDatabase<>).MakeGenericType(CqfTreeDefType);
            var getNamed = AccessTools.Method(databaseType, "GetNamedSilentFail", new[] { typeof(string) });
            return getNamed?.Invoke(null, new object[] { defName });
        }

        private static Quest ResolveQuest(int questId)
        {
            if (questId < 0)
                return null;
            return Find.QuestManager?.QuestsListForReading?.FirstOrDefault(q => q != null && q.id == questId);
        }

        private static int GetNodeIndex(object node)
        {
            if (node == null)
                return -1;
            try
            {
                object value = AccessTools.Field(node.GetType(), "index")?.GetValue(node)
                               ?? AccessTools.Property(node.GetType(), "index")?.GetValue(node);
                return value == null ? -1 : Convert.ToInt32(value);
            }
            catch
            {
                return -1;
            }
        }

        private static bool AllFieldsResolved()
        {
            return OptionTextField != null && OptionDisabledField != null &&
                   OptionDisableReasonField != null && OptionActionField != null &&
                   OptionNextIndexField != null &&
                   WindowInterviewerField != null && WindowIntervieweeField != null &&
                   WindowQuestField != null && WindowTreeField != null &&
                   WindowCurNodeField != null && WindowOptionsField != null &&
                   WindowNextOptionsField != null;
        }

        private static void LogRuntimeOnce(string key, string message, bool warning)
        {
            if (!RuntimeLogOnce.Add(key))
                return;
            if (warning)
                Log.Warning($"{LogTag}: {message}");
            else
                Log.Message($"{LogTag}: {message}");
        }
    }
}
