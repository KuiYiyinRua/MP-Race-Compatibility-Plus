using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for Kiiro Story: Events Expanded
    /// (Ancot.KiiroStoryEventsExpanded, assembly Kiiro_Event).
    /// </summary>
    internal static class Patch_KiiroStoryEventsMp
    {
        private const string DialogTypeName = "Kiiro_Event.Dialog_TradeFestivalDate";
        private const string GameComponentTypeName = "Kiiro_Event.KiiroEventGameComponent_OverallControl";
        private const string ResourceRequestTypeName = "Kiiro_Event.ResourceRequestComp";
        private const string FishCatcherTypeName = "Kiiro_Event.CompFishCatcher";

        private static bool _applied;
        private static Type _gameComponentType;
        private static FieldInfo _tradeFestivalDateField;
        private static PropertyInfo _gameComponentProperty;

        private sealed class TradeDateDialogState
        {
            internal int Date;
            internal HashSet<Letter> Letters;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;

            _applied = true;

            Type dialogType = AccessTools.TypeByName(DialogTypeName);
            Type resourceRequestType = AccessTools.TypeByName(ResourceRequestTypeName);
            Type fishCatcherType = AccessTools.TypeByName(FishCatcherTypeName);
            _gameComponentType = AccessTools.TypeByName(GameComponentTypeName);

            if (dialogType == null && resourceRequestType == null && fishCatcherType == null && _gameComponentType == null)
            {
                Log.Message("[MP-MeowOnlineShop] Kiiro Story Events MP: target assembly not active; patch skipped.");
                return;
            }

            int resolved = 0;
            int expected = 3;

            try
            {
                if (TryPatchTradeFestivalDateDialog(harmony, dialogType))
                    resolved++;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Kiiro Story Events MP: trade festival date patch failed: " + e);
            }

            try
            {
                MethodInfo fulfill = resourceRequestType?.GetMethod(
                    "Fulfill",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Caravan) },
                    null);

                if (fulfill != null)
                {
                    MP.RegisterSyncMethod(fulfill, null)
                        .SetContext(SyncContext.WorldSelected)
                        .CancelIfAnyArgNull();
                    resolved++;
                    Log.Message("[MP-MeowOnlineShop] Kiiro Story Events MP: registered ResourceRequestComp.Fulfill(Caravan).");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Kiiro Story Events MP: ResourceRequestComp.Fulfill(Caravan) not resolved.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Kiiro Story Events MP: resource request sync registration failed: " + e);
            }

            try
            {
                MethodInfo toggle = FindGeneratedToggleMethod(fishCatcherType, "CompGetGizmosExtra");
                if (toggle != null)
                {
                    MP.RegisterSyncMethod(toggle, null).SetContext(SyncContext.MapSelected);
                    resolved++;
                    Log.Message("[MP-MeowOnlineShop] Kiiro Story Events MP: registered fish catcher toggle " + toggle.Name + ".");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] Kiiro Story Events MP: fish catcher toggle method not resolved.");
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Kiiro Story Events MP: fish catcher toggle registration failed: " + e);
            }

            Log.Message($"[MP-MeowOnlineShop] Kiiro Story Events MP targets resolved={resolved}/{expected}.");
        }

        private static bool TryPatchTradeFestivalDateDialog(Harmony harmony, Type dialogType)
        {
            if (dialogType == null || _gameComponentType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Kiiro Story Events MP: date dialog or game component type not resolved.");
                return false;
            }

            _tradeFestivalDateField = AccessTools.Field(_gameComponentType, "tradeFestivalDate");
            _gameComponentProperty = AccessTools.Property(_gameComponentType, "GC");
            MethodInfo doWindowContents = AccessTools.Method(dialogType, "DoWindowContents", new[] { typeof(Rect) });
            MethodInfo prefix = AccessTools.Method(typeof(Patch_KiiroStoryEventsMp), nameof(TradeFestivalDateDialog_Prefix));
            MethodInfo postfix = AccessTools.Method(typeof(Patch_KiiroStoryEventsMp), nameof(TradeFestivalDateDialog_Postfix));

            if (_tradeFestivalDateField == null || _gameComponentProperty == null || doWindowContents == null || prefix == null || postfix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Kiiro Story Events MP: date dialog members not resolved "
                    + $"(field={_tradeFestivalDateField != null}, gc={_gameComponentProperty != null}, window={doWindowContents != null}).");
                return false;
            }

            MP.RegisterSyncMethod(typeof(Patch_KiiroStoryEventsMp), nameof(SyncSetTradeFestivalDate));
            harmony.Patch(doWindowContents, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            Log.Message("[MP-MeowOnlineShop] Kiiro Story Events MP: patched Dialog_TradeFestivalDate.DoWindowContents.");
            return true;
        }

        private static MethodInfo FindGeneratedToggleMethod(Type type, string parentMethod)
        {
            if (type == null)
                return null;

            string prefix = "<" + parentMethod + ">b__";
            return type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal)
                            && m.Name.EndsWith("_0", StringComparison.Ordinal)
                            && m.ReturnType == typeof(void)
                            && m.GetParameters().Length == 0)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static void TradeFestivalDateDialog_Prefix(out TradeDateDialogState __state)
        {
            __state = new TradeDateDialogState
            {
                Date = GetTradeFestivalDate(),
                Letters = Find.LetterStack == null
                    ? new HashSet<Letter>()
                    : new HashSet<Letter>(Find.LetterStack.LettersListForReading)
            };
        }

        private static void TradeFestivalDateDialog_Postfix(TradeDateDialogState __state)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || __state == null)
                return;

            int newValue = GetTradeFestivalDate();
            if (newValue == __state.Date)
                return;

            // The original button handler also inserts a letter before writing
            // the date. Undo every letter created during this GUI call so no
            // interface-only state survives on the clicking peer.
            if (Find.LetterStack != null)
            {
                foreach (Letter letter in Find.LetterStack.LettersListForReading
                             .Where(letter => !__state.Letters.Contains(letter))
                             .ToList())
                    Find.LetterStack.RemoveLetter(letter);
            }

            SetTradeFestivalDate(__state.Date);
            SyncSetTradeFestivalDate(newValue);
        }

        private static void SyncSetTradeFestivalDate(int value)
        {
            Quadrum quadrum = (Quadrum)(value / 15);
            int day = value % 15;
            Find.LetterStack?.ReceiveLetter(
                "Kiiro.TradeFestivalSetUp_LetterTitle".Translate(),
                "Kiiro.TradeFestivalSetUp_LetterDesc".Translate(
                    QuadrumUtility.Label(quadrum),
                    (day + 1).ToString()),
                LetterDefOf.NeutralEvent);
            SetTradeFestivalDate(value);
        }

        private static int GetTradeFestivalDate()
        {
            object component = GetGameComponent();
            if (component == null || _tradeFestivalDateField == null)
                return -1;

            object value = _tradeFestivalDateField.GetValue(component);
            return value is int intValue ? intValue : -1;
        }

        private static void SetTradeFestivalDate(int value)
        {
            object component = GetGameComponent();
            if (component != null && _tradeFestivalDateField != null)
                _tradeFestivalDateField.SetValue(component, value);
        }

        private static object GetGameComponent()
        {
            if (_gameComponentProperty == null)
                return null;

            try
            {
                return _gameComponentProperty.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
