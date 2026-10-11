using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_ReadingCallbacks
{
    static Type compType, itemType, listType;
    static MethodInfo open, next;
    static FieldInfo dialogNumber, dialogs, content, optionText;
    static PLAMilira_ReadingCallbacks()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                compType = AccessTools.TypeByName("PLAMilira.CompReadDiaLog_Quest") ?? throw new TypeLoadException("CompReadDiaLog_Quest");
                itemType = AccessTools.TypeByName("AncotLibrary.CustomDiaLog_Nested") ?? throw new TypeLoadException("CustomDiaLog_Nested");
                listType = typeof(List<>).MakeGenericType(itemType);
                open = AccessTools.DeclaredMethod(compType, "OpenNestedDialog", new[] { typeof(int), listType }) ?? throw new MissingMethodException("OpenNestedDialog");
                var closure = compType.GetNestedType("<>c__DisplayClass4_0", BindingFlags.NonPublic) ?? throw new TypeLoadException("Reading callback closure");
                next = AccessTools.DeclaredMethod(closure, "<OpenNestedDialog>b__0", Type.EmptyTypes) ?? throw new MissingMethodException("Reading next callback");
                dialogNumber = AccessTools.DeclaredField(closure, "dialogNumber"); dialogs = AccessTools.DeclaredField(closure, "dialogs");
                content = AccessTools.Field(itemType, "content"); optionText = AccessTools.Field(itemType, "optionText");
                if (dialogNumber?.FieldType != typeof(int) || dialogs?.FieldType != listType || content?.FieldType != typeof(string) || optionText?.FieldType != typeof(string)) throw new MissingFieldException("Reading callback payload");
                new Harmony("Meow.PLAMilira.ReadingCallbacks").Patch(open, transpiler: new HarmonyMethod(typeof(PLAMilira_ReadingCallbacks), nameof(WrapActions)));
                MiliraDialogDelegates.Register(AccessTools.DeclaredMethod(typeof(SavedNext), nameof(SavedNext.Invoke), Type.EmptyTypes));
                Log.Message("[PLAMilira_ReadingCallbacks] REQUIRED_TARGETS_OK native reading page callbacks");
            }
            catch (Exception e) { Log.Error("[PLAMilira_ReadingCallbacks] REQUIRED_TARGET_FAILED " + e); }
        });
    }

    static IEnumerable<CodeInstruction> WrapActions(IEnumerable<CodeInstruction> instructions)
    {
        int count = 0;
        var action = AccessTools.Field(typeof(DiaOption), nameof(DiaOption.action));
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, action))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PLAMilira_ReadingCallbacks), nameof(Wrap)));
                count++;
            }
            yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException("Reading action sites=" + count);
    }

    static Action Wrap(Action original)
    {
        if (!MP.IsInMultiplayer || original?.Method != next) return original;
        var saved = new SavedNext { nextIndex = (int)dialogNumber.GetValue(original.Target) + 1 };
        saved.Capture(original, (IList)dialogs.GetValue(original.Target));
        return saved.Invoke;
    }

    public abstract class LivePage
    {
        protected Action live;
        protected IList source;
        protected List<string> contents, labels;
        internal void Capture(Action action, IList pages) { live = action; source = pages; }
    }

    public sealed class SavedNext : LivePage, IExposable
    {
        public int nextIndex;
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && source != null)
            {
                contents = new List<string>(); labels = new List<string>();
                foreach (object page in source) { contents.Add((string)content.GetValue(page)); labels.Add((string)optionText.GetValue(page)); }
            }
            Scribe_Values.Look(ref nextIndex, "nextIndex");
            Scribe_Collections.Look(ref contents, "contents", LookMode.Value);
            Scribe_Collections.Look(ref labels, "labels", LookMode.Value);
        }
        public void Invoke()
        {
            if (live != null) { live(); return; }
            if (contents == null || labels == null || contents.Count != labels.Count) throw new InvalidOperationException("Reading pages were not restored");
            var pages = (IList)Activator.CreateInstance(listType);
            for (int i = 0; i < contents.Count; i++)
            {
                var page = Activator.CreateInstance(itemType);
                content.SetValue(page, contents[i]); optionText.SetValue(page, labels[i]); pages.Add(page);
            }
            // Verified native OpenNestedDialog reads no component/parent state.
            // The original usable item can already have been consumed; only the
            // saved pages and index are needed to run the same paging behavior.
            open.Invoke(Activator.CreateInstance(compType), new object[] { nextIndex, pages });
        }
    }
}
