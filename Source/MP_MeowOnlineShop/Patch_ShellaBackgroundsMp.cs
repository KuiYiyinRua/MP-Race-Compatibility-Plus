using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Shella Backgrounds replaces the entry background from two local UI hooks:
    /// a MainMenuDrawer.Init postfix and a UI_BackgroundMain.DoOverlay postfix.
    /// Multiplayer owns a prefix on the same DoOverlay method and Shella's own
    /// apply path is a one-shot latch (gameLoading/initialized). Depending on
    /// mod order and entry-scene timing that latch can already be consumed, so
    /// the selected texture is never re-applied and the vanilla background plus
    /// Multiplayer's main-menu animation stay visible. This shim re-applies the
    /// selected Shella texture from stable local UI points after all mods load.
    /// It is cosmetic only and does not touch simulation state.
    /// </summary>
    internal static class Patch_ShellaBackgroundsMp
    {
        private const string SettingsHandlerTypeName = "ShellaBackgrounds.SettingsHandler";
        private const string SettingsTypeName = "ShellaBackgrounds.ShellaBackgroundSettings";
        private const string DefTypeName = "ShellaBackgrounds.Shella_BackgroundDef";
        private const string UtilityTypeName = "ShellaBackgrounds.BackgroundsUtility";

        private static bool resolved;
        private static bool usable;
        private static bool failureLogged;
        private static bool randomApplied;

        private static Type settingsHandlerType;
        private static FieldInfo settingsField;
        private static FieldInfo currentBackgroundField;
        private static FieldInfo imagePathsField;
        private static PropertyInfo currentBackgroundDefProperty;
        private static PropertyInfo backgroundImageProperty;
        private static MethodInfo getBackgroundImageFromSetting;
        private static MethodInfo getBackgroundImagePath;

        private static string fallbackName;
        private static bool fallbackTried;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !ModsConfig.IsActive("stm.ShellaBackgrounds"))
                return;

            try
            {
                MethodInfo mainMenuInit = AccessTools.Method(
                    typeof(MainMenuDrawer),
                    nameof(MainMenuDrawer.Init));
                MethodInfo backgroundOnGui = AccessTools.Method(
                    typeof(UI_BackgroundMain),
                    nameof(UI_BackgroundMain.BackgroundOnGUI));
                if (mainMenuInit == null || backgroundOnGui == null)
                {
                    LogFailure(
                        "vanilla MainMenuDrawer.Init or UI_BackgroundMain.BackgroundOnGUI was not resolved");
                    return;
                }

                harmony.Patch(
                    mainMenuInit,
                    postfix: new HarmonyMethod(
                        typeof(Patch_ShellaBackgroundsMp),
                        nameof(MainMenuInitPostfix)));
                harmony.Patch(
                    backgroundOnGui,
                    prefix: new HarmonyMethod(
                        typeof(Patch_ShellaBackgroundsMp),
                        nameof(BackgroundOnGuiPrefix)),
                    postfix: new HarmonyMethod(
                        typeof(Patch_ShellaBackgroundsMp),
                        nameof(BackgroundOnGuiPostfix)));

                Log.Message(
                    "[MP-MeowOnlineShop] Shella Backgrounds MP UI hooks installed; " +
                    "the late-loaded API will be resolved when the main menu initializes.");
            }
            catch (Exception e)
            {
                LogFailure(e.Message);
            }
        }

        private static bool ResolveShellaApi()
        {
            if (resolved)
                return usable;

            resolved = true;
            try
            {
                // Resolve all companion types from the assembly that actually
                // supplied SettingsHandler.  AccessTools.TypeByName can return
                // null here during the early compatibility bootstrap even
                // though RimWorld has already loaded Shella's assembly.
                settingsHandlerType = FindShellaType(SettingsHandlerTypeName);
                Assembly shellaAssembly = settingsHandlerType?.Assembly;
                Type settingsType = shellaAssembly?.GetType(SettingsTypeName, false) ??
                                    FindShellaType(SettingsTypeName);
                Type defType = shellaAssembly?.GetType(DefTypeName, false) ??
                               FindShellaType(DefTypeName);
                Type utilityType = shellaAssembly?.GetType(UtilityTypeName, false) ??
                                   FindShellaType(UtilityTypeName);

                settingsField = settingsHandlerType == null
                    ? null
                    : AccessTools.Field(settingsHandlerType, "Settings");
                settingsType = settingsField?.FieldType ?? settingsType;
                currentBackgroundField = settingsType == null
                    ? null
                    : AccessTools.Field(settingsType, "CurrentBackground");
                imagePathsField = settingsType == null
                    ? null
                    : AccessTools.Field(settingsType, "ImagePaths");
                currentBackgroundDefProperty = settingsType == null
                    ? null
                    : AccessTools.Property(settingsType, "CurrentBackgroundDef");
                backgroundImageProperty = defType == null
                    ? null
                    : AccessTools.Property(defType, "BackgroundImage");
                getBackgroundImageFromSetting = utilityType == null
                    ? null
                    : AccessTools.Method(utilityType, "GetBackgroundImageFromSetting", Type.EmptyTypes);
                getBackgroundImagePath = utilityType == null
                    ? null
                    : AccessTools.Method(utilityType, "GetBackgroundImagePath");

                usable = settingsHandlerType != null &&
                         settingsField != null &&
                         currentBackgroundField != null;
                if (usable)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Shella Backgrounds late-loaded API resolved; " +
                        "selected background re-application is active.");
                }
                else
                {
                    LogFailure(
                        "Shella Backgrounds API shape is unsupported; compatibility shim skipped " +
                        $"(handler={settingsHandlerType != null}, settingsField={settingsField != null}, " +
                        $"currentBackground={currentBackgroundField != null}).");
                }
            }
            catch (Exception e)
            {
                LogFailure(e.Message);
            }

            return usable;
        }

        private static Type FindShellaType(string fullName)
        {
            Type resolvedType = AccessTools.TypeByName(fullName);
            if (resolvedType != null)
                return resolvedType;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    resolvedType = assembly.GetType(fullName, false);
                    if (resolvedType != null)
                        return resolvedType;
                }
                catch
                {
                    // A broken unrelated assembly must not abort optional UI
                    // compatibility discovery.
                }
            }

            return null;
        }

        private static void MainMenuInitPostfix()
        {
            if (!ResolveShellaApi())
                return;

            ApplyToCurrentBackground();
        }

        private static void BackgroundOnGuiPrefix(UI_BackgroundMain __instance)
        {
            if (__instance == null)
                return;

            Texture2D texture = TryGetShellaTexture();
            if (texture != null)
                __instance.overrideBGImage = texture;
        }

        private static void BackgroundOnGuiPostfix(UI_BackgroundMain __instance)
        {
            if (__instance == null)
                return;

            Texture2D texture = TryGetShellaTexture();
            if (texture != null)
                __instance.overrideBGImage = texture;
        }

        private static void ApplyToCurrentBackground()
        {
            try
            {
                if (UIMenuBackgroundManager.background is UI_BackgroundMain bg)
                {
                    Texture2D texture = TryGetShellaTexture();
                    if (texture != null)
                        bg.overrideBGImage = texture;
                }
            }
            catch (Exception e)
            {
                LogFailure(e.Message);
            }
        }

        private static Texture2D TryGetShellaTexture()
        {
            if (!usable && !ResolveShellaApi())
                return null;

            try
            {
                object settings = settingsField.GetValue(null);
                if (settings == null)
                    return null;

                if (getBackgroundImageFromSetting != null)
                {
                    Texture2D fromSetting =
                        getBackgroundImageFromSetting.Invoke(null, null) as Texture2D;
                    if (fromSetting != null)
                        return fromSetting;
                }

                string current = currentBackgroundField.GetValue(settings) as string;
                if (!string.IsNullOrEmpty(current))
                {
                    if (current != fallbackName)
                    {
                        fallbackName = current;
                        fallbackTried = false;
                    }

                    object def = currentBackgroundDefProperty?.GetValue(settings);
                    if (def != null)
                    {
                        Texture2D defTexture =
                            backgroundImageProperty?.GetValue(def) as Texture2D;
                        if (defTexture != null)
                            return defTexture;
                    }

                    if (!fallbackTried)
                    {
                        fallbackTried = true;
                        IDictionary paths = imagePathsField?.GetValue(settings) as IDictionary;
                        if (paths != null && paths.Contains(current))
                        {
                            string path = paths[current] as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                Texture2D pathTexture =
                                    ContentFinder<Texture2D>.Get(path, true);
                                if (pathTexture != null)
                                    return pathTexture;
                            }
                        }
                    }
                }

                if (Prefs.RandomBackgroundImage && !randomApplied)
                {
                    randomApplied = true;
                    if (getBackgroundImagePath != null)
                    {
                        object[] args = { null };
                        string path = getBackgroundImagePath.Invoke(null, args) as string;
                        if (!string.IsNullOrEmpty(path))
                        {
                            Texture2D randomTexture =
                                ContentFinder<Texture2D>.Get(path, true);
                            if (randomTexture != null)
                                return randomTexture;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogFailure(e.Message);
            }

            return null;
        }

        private static void LogFailure(string message)
        {
            if (failureLogged)
                return;

            failureLogged = true;
            Log.Warning("[MP-MeowOnlineShop] Shella Backgrounds MP UI compat: " + message);
        }
    }
}
