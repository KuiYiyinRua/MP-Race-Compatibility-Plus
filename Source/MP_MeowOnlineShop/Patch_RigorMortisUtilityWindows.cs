using System;
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
    /// Converts Rigor Mortis utility-window edits into commit-on-command state.
    /// The original Fufu window writes into a spawned Thing every GUI frame and the
    /// blood window writes into CompTaoist only on local close; both are unsafe in MP.
    /// </summary>
    internal static class Patch_RigorMortisUtilityWindows
    {
        private const string LogTag = "[MP-MeowOnlineShop] RigorMortisUtilityWindows";

        private static readonly Type FufuType = AccessTools.TypeByName("RigorMortis.Fufu");
        private static readonly Type FufuWindowType = AccessTools.TypeByName("RigorMortis.FufuSizeWindow");
        private static readonly Type BloodWindowType = AccessTools.TypeByName("RigorMortis.BloodUseWindow");
        private static readonly Type CompTaoistType = AccessTools.TypeByName("RigorMortis.CompTaoist");

        private static readonly FieldInfo FufuWindowThingField = AccessTools.Field(FufuWindowType, "fufu");
        private static readonly FieldInfo FufuWindowTitleField = AccessTools.Field(FufuWindowType, "title");
        private static readonly FieldInfo FufuSizeField = AccessTools.Field(FufuType, "sizeFixed");
        private static readonly FieldInfo FufuSizeFakeField = AccessTools.Field(FufuType, "sizeFixedFake");
        private static readonly FieldInfo FufuOffsetField = AccessTools.Field(FufuType, "offsetFixed");

        private static readonly FieldInfo BloodWindowCompField = AccessTools.Field(BloodWindowType, "comp");
        private static readonly FieldInfo BloodWindowFieldNameField = AccessTools.Field(BloodWindowType, "fieldName");
        private static readonly FieldInfo BloodWindowNowField = AccessTools.Field(BloodWindowType, "now");

        private static readonly Dictionary<object, FufuDraft> FufuDrafts =
            new Dictionary<object, FufuDraft>();

        private static ISyncMethod SyncFufuMethod;
        private static ISyncMethod SyncBloodMethod;

        private sealed class FufuDraft
        {
            internal int Size;
            internal int Offset;
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            try
            {
                SyncFufuMethod = MP.RegisterSyncMethod(
                    AccessTools.Method(typeof(Patch_RigorMortisUtilityWindows), nameof(SyncFufu)),
                    null);
                SyncBloodMethod = MP.RegisterSyncMethod(
                    AccessTools.Method(typeof(Patch_RigorMortisUtilityWindows), nameof(SyncBloodUse)),
                    null);

                MethodInfo fufuDraw = AccessTools.Method(FufuWindowType, "DoWindowContents", new[] { typeof(Rect) });
                MethodInfo bloodClose = AccessTools.Method(BloodWindowType, "Close", new[] { typeof(bool) });
                if (fufuDraw != null)
                    harmony.Patch(
                        fufuDraw,
                        prefix: new HarmonyMethod(
                            typeof(Patch_RigorMortisUtilityWindows),
                            nameof(FufuDrawPrefix))
                        { priority = Priority.First });
                if (bloodClose != null)
                    harmony.Patch(
                        bloodClose,
                        prefix: new HarmonyMethod(
                            typeof(Patch_RigorMortisUtilityWindows),
                            nameof(BloodClosePrefix))
                        { priority = Priority.First });

                Log.Message(
                    $"{LogTag}: ready fufuDraw={fufuDraw != null} " +
                    $"bloodClose={bloodClose != null} fields={SymbolsReady()}.");
            }
            catch (Exception e)
            {
                Log.Warning($"{LogTag}: setup failed: {e}");
            }
        }

        public static bool FufuDrawPrefix(object __instance, Rect inRect)
        {
            if (!MP.enabled || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;
            if (__instance == null || !SymbolsReady())
                return true;

            var fufu = FufuWindowThingField.GetValue(__instance) as Thing;
            if (fufu == null)
                return true;

            if (!FufuDrafts.TryGetValue(__instance, out FufuDraft draft))
            {
                draft = new FufuDraft
                {
                    Size = (int)FufuSizeFakeField.GetValue(fufu),
                    Offset = (int)FufuOffsetField.GetValue(fufu)
                };
                FufuDrafts.Add(__instance, draft);
            }

            Rect contentRect = inRect.ContractedBy(10f);
            var listing = new Listing_Standard();
            listing.Begin(contentRect);
            Text.Font = GameFont.Medium;
            listing.Label(FufuWindowTitleField.GetValue(__instance) as string ?? string.Empty);
            Text.Font = GameFont.Small;
            listing.Label("Fufu.ChangeSizeDescription".Translate((draft.Size / 10f).ToString("F1")));
            int newSize = (int)listing.Slider(draft.Size, 1f, 20f);
            listing.Label("Fufu.ChangeOffset".Translate((draft.Offset / 10f).ToString("F1")));
            int newOffset = (int)listing.Slider(draft.Offset, -10f, 10f);
            listing.End();

            Rect closeRect = new Rect(
                contentRect.x,
                inRect.height - 25f,
                contentRect.width,
                Text.LineHeight);
            Widgets.DrawHighlightIfMouseover(closeRect);
            Widgets.Label(closeRect, "CloseButton".Translate());
            bool close = Widgets.ButtonInvisible(closeRect);

            if (newSize != draft.Size || newOffset != draft.Offset)
            {
                draft.Size = newSize;
                draft.Offset = newOffset;
                SyncFufuMethod?.DoSync(null, fufu, newSize, newOffset);
            }

            if (close)
                ((Window)__instance).Close();
            return false;
        }

        public static bool BloodClosePrefix(object __instance)
        {
            if (!MP.enabled || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;
            if (__instance == null || SyncBloodMethod == null)
                return true;

            var comp = BloodWindowCompField?.GetValue(__instance) as ThingComp;
            string fieldName = BloodWindowFieldNameField?.GetValue(__instance) as string;
            if (comp?.parent == null || string.IsNullOrEmpty(fieldName) ||
                !(BloodWindowNowField?.GetValue(__instance) is int now))
                return true;

            SyncBloodMethod.DoSync(null, comp.parent, fieldName, now);
            return false;
        }

        public static void SyncFufu(Thing fufu, int sizeFixedFake, int offsetFixed)
        {
            if (fufu == null || !FufuType.IsInstanceOfType(fufu))
                return;

            int size = Mathf.Clamp(sizeFixedFake, 1, 20);
            int offset = Mathf.Clamp(offsetFixed, -10, 10);
            FufuSizeFakeField.SetValue(fufu, size);
            FufuSizeField.SetValue(fufu, size / 10f);
            FufuOffsetField.SetValue(fufu, offset);

            foreach (object window in FufuDrafts.Keys.ToList())
            {
                if (ReferenceEquals(FufuWindowThingField.GetValue(window), fufu))
                {
                    FufuDrafts[window].Size = size;
                    FufuDrafts[window].Offset = offset;
                }
            }
        }

        public static void SyncBloodUse(Thing parent, string fieldName, int value)
        {
            if (parent == null || CompTaoistType == null || string.IsNullOrEmpty(fieldName))
                return;

            ThingComp comp = (parent as ThingWithComps)?.AllComps?
                .FirstOrDefault(c => c != null && CompTaoistType.IsInstanceOfType(c));
            FieldInfo field = AccessTools.Field(CompTaoistType, fieldName);
            if (comp == null || field == null || field.FieldType != typeof(int))
                return;

            field.SetValue(comp, Math.Max(1, value));

            var windows = Find.WindowStack?.Windows;
            if (windows == null)
                return;
            foreach (Window window in windows.ToList())
            {
                if (!BloodWindowType.IsInstanceOfType(window) ||
                    !ReferenceEquals(BloodWindowCompField.GetValue(window), comp) ||
                    !string.Equals(
                        BloodWindowFieldNameField.GetValue(window) as string,
                        fieldName,
                        StringComparison.Ordinal))
                    continue;

                BloodWindowNowField.SetValue(window, value);
                window.Close();
            }
        }

        private static bool SymbolsReady()
        {
            return FufuType != null && FufuWindowType != null && BloodWindowType != null &&
                   FufuWindowThingField != null && FufuWindowTitleField != null &&
                   FufuSizeField != null && FufuSizeFakeField != null && FufuOffsetField != null &&
                   BloodWindowCompField != null && BloodWindowFieldNameField != null &&
                   BloodWindowNowField != null;
        }
    }
}
