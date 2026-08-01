using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 配置不匹配时曾尝试直接覆盖客户端设置并热重载。该行为会绕过 Multiplayer
    /// 自己的临时配置 + 重启隔离流程，因此默认禁用并保留原版 JoinDataWindow 流程。
    /// </summary>
    internal static class Patch_MpConfigHotSync
    {
        private const string HugsLibId = "unlimitedhugs.hugslib";
        private const string HugsLibSettingsFile = "ModSettings";
        private const string LoadingProgressId = "ilyvion.loadingprogress";
        private static readonly HashSet<string> StartupBoundConfigModIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "imranfish.xmlextensions"
            };
        private static readonly HashSet<string> StartupBoundConfigsWrittenThisProcess =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _applied;
        private static MethodInfo _joinDataWindowCloseMethod;
        private static bool _joinDataWindowMembersValidated;
        private static readonly HashSet<int> ProcessedWindows = new HashSet<int>();

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;

            _applied = true;
            ApplyIgnoredConfigFilters();

            try
            {
                var joinDataWindowType = AccessTools.TypeByName("Multiplayer.Client.JoinDataWindow");
                if (joinDataWindowType == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] MP config hot sync: JoinDataWindow type not found, patch skipped.");
                    return;
                }

                if (!ValidateJoinDataWindowShape(joinDataWindowType))
                {
                    Log.Warning("[MP-MeowOnlineShop] MP config hot sync: JoinDataWindow member shape mismatch, patch skipped.");
                    return;
                }

                var postOpen = AccessTools.Method(joinDataWindowType, "PostOpen");
                var postfix = AccessTools.Method(typeof(Patch_MpConfigHotSync), nameof(JoinDataWindow_PostOpen_Postfix));
                _joinDataWindowCloseMethod = AccessTools.Method(joinDataWindowType, "Close", new[] { typeof(bool) });
                if (postOpen == null || postfix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] MP config hot sync: PostOpen patch target not resolved.");
                    return;
                }

                harmony.Patch(postOpen, postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
                Log.Message(
                    "[MP-MeowOnlineShop] MP host-config hot sync active: config-only mismatches " +
                    "are imported and reloaded before world download; restart is not forced.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MP config hot sync patch failed: " + e);
            }
        }

        private static void ApplyIgnoredConfigFilters()
        {
            try
            {
                Type syncConfigsType = AccessTools.TypeByName("Multiplayer.Client.Util.SyncConfigs");
                FieldInfo ignoredField = AccessTools.Field(syncConfigsType, "ignoredConfigsModIds");
                string[] ignored = ignoredField?.GetValue(null) as string[];
                if (ignored == null || ignored.Any(id =>
                        string.Equals(id, LoadingProgressId, StringComparison.OrdinalIgnoreCase)))
                    return;

                ignoredField.SetValue(null, ignored.Concat(new[] { LoadingProgressId }).ToArray());
                Log.Message(
                    "[MP-MeowOnlineShop] Multiplayer config comparison ignores Loading Progress; " +
                    "its startup timing cache is local-only and rewrites itself during every load.");
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Failed to add the Loading Progress local-only config filter: " +
                    exception.Message);
            }
        }

        private static void JoinDataWindow_PostOpen_Postfix(object __instance)
        {
            if (__instance == null)
                return;

            int hash = __instance.GetHashCode();
            if (ProcessedWindows.Contains(hash))
                return;
            ProcessedWindows.Add(hash);

            try
            {
                if (!TryResolveAutoHotSyncContext(__instance, out var remote, out var connectAnyway))
                    return;

                bool filesApplied;
                var localOnlyConfigs = GetLocalOnlyConfigPaths(
                    GetFieldOrProperty<object>(__instance, "configsRoot"),
                    remote);
                TryApplyRemoteConfigs(
                    remote,
                    localOnlyConfigs,
                    out var result,
                    out filesApplied);

                // 本模组存在运行态开关应用逻辑，热重载后主动触发一次对齐。
                try { Patch_MultifactionTpsOptimize.NotifyModSettingsUpdated(); }
                catch { }

                if (!filesApplied)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] MP config hot sync could not safely apply every " +
                        "host config in memory and on disk. Automatic continuation was " +
                        "suppressed; Connect Anyway remains a manual choice and no restart " +
                        "is forced. " +
                        result);
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] MP config hot sync finished; continuing join " +
                    "without restart. " + result);
                try
                {
                    connectAnyway.Invoke();
                    _joinDataWindowCloseMethod?.Invoke(__instance, new object[] { false });
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] MP config hot sync: auto-continue failed, fallback to manual action: " + e.Message);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MP config hot sync runtime error: " + e);
            }
        }

        private static bool ValidateJoinDataWindowShape(Type joinDataWindowType)
        {
            if (_joinDataWindowMembersValidated)
                return true;
            if (joinDataWindowType == null)
                return false;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            string[] requiredMembers =
            {
                "connectAnywayDisabled",
                "connectAnywayCallback",
                "remote",
                "modListDiff",
                "filesRoot",
                "configsRoot"
            };

            for (int i = 0; i < requiredMembers.Length; i++)
            {
                string name = requiredMembers[i];
                if (joinDataWindowType.GetField(name, flags) == null && joinDataWindowType.GetProperty(name, flags) == null)
                {
                    Log.Warning($"[MP-MeowOnlineShop] MP config hot sync: JoinDataWindow missing required member '{name}'.");
                    return false;
                }
            }

            _joinDataWindowMembersValidated = true;
            return true;
        }

        private static bool TryResolveAutoHotSyncContext(object joinDataWindow, out object remote, out Action connectAnyway)
        {
            remote = null;
            connectAnyway = null;

            if (IsConnectAnywayDisabled(joinDataWindow))
                return false;

            connectAnyway = GetFieldOrProperty<Action>(joinDataWindow, "connectAnywayCallback");
            if (connectAnyway == null)
                return false;

            remote = GetFieldOrProperty<object>(joinDataWindow, "remote");
            if (remote == null)
                return false;

            bool hasConfigs = GetFieldOrProperty<bool>(remote, "hasConfigs");
            if (!hasConfigs)
                return false;

            var modListDiff = GetFieldOrProperty<object>(joinDataWindow, "modListDiff");
            bool modListMatch = string.Equals(modListDiff?.ToString(), "None", StringComparison.Ordinal);
            if (!modListMatch)
                return false;

            bool filesMismatch = NodeHasAnyChildren(GetFieldOrProperty<object>(joinDataWindow, "filesRoot"));
            if (filesMismatch)
                return false;

            bool configsMismatch = NodeHasAnyChildren(GetFieldOrProperty<object>(joinDataWindow, "configsRoot"));
            if (!configsMismatch)
                return false;

            return true;
        }

        private static bool IsConnectAnywayDisabled(object joinDataWindow)
        {
            var value = GetFieldOrProperty<object>(joinDataWindow, "connectAnywayDisabled");
            if (value == null)
                return false;
            if (value is bool boolValue)
                return boolValue;
            if (value is string str)
                return !string.IsNullOrEmpty(str);
            return true;
        }

        private static void TryApplyRemoteConfigs(
            object remote,
            IEnumerable<string> localOnlyConfigs,
            out string result,
            out bool filesApplied)
        {
            int written = 0;
            int writeFailed = 0;
            int reloaded = 0;
            int fileOnly = 0;
            int unchanged = 0;
            int startupBound = 0;
            int resetToHostDefaults = 0;
            int resetFailed = 0;
            var changedKeys = new List<string>();
            filesApplied = false;

            var remoteModConfigs = GetFieldOrProperty<IEnumerable>(remote, "remoteModConfigs");
            if (remoteModConfigs == null)
            {
                result = "remoteModConfigs missing";
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cfg in remoteModConfigs)
            {
                if (cfg == null)
                    continue;

                string modId = GetFieldOrProperty<string>(cfg, "ModId");
                string fileName = GetFieldOrProperty<string>(cfg, "FileName");
                string contents = GetFieldOrProperty<string>(cfg, "Contents");
                if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(fileName))
                {
                    writeFailed++;
                    continue;
                }

                string key = modId + "/" + fileName;
                if (!seen.Add(key))
                    continue;

                string path = ResolveSettingsPath(modId, fileName);
                if (string.IsNullOrEmpty(path))
                {
                    Log.Warning($"[MP-MeowOnlineShop] MP config hot sync: resolve path failed for {key}.");
                    writeFailed++;
                    continue;
                }

                string hostContents = contents ?? string.Empty;
                bool contentMatches = false;
                try
                {
                    contentMatches =
                        File.Exists(path) &&
                        string.Equals(
                            File.ReadAllText(path),
                            hostContents,
                            StringComparison.Ordinal);
                }
                catch (Exception e)
                {
                    Log.Warning(
                        $"[MP-MeowOnlineShop] MP config hot sync could not compare " +
                        $"{key}: {e.Message}");
                }

                bool requiresRestartBecausePreviouslyWritten =
                    StartupBoundConfigsWrittenThisProcess.Contains(key);
                if (contentMatches && !requiresRestartBecausePreviouslyWritten)
                {
                    unchanged++;
                    continue;
                }

                if (!changedKeys.Contains(key))
                    changedKeys.Add(key);

                if (StartupBoundConfigModIds.Contains(modId))
                {
                    startupBound++;
                    fileOnly++;

                    if (!contentMatches)
                    {
                        if (TryWriteConfigAtomically(
                                path,
                                hostContents,
                                out string startupWriteMessage))
                        {
                            written++;
                            StartupBoundConfigsWrittenThisProcess.Add(key);
                        }
                        else
                        {
                            writeFailed++;
                            Log.Warning(
                                $"[MP-MeowOnlineShop] MP config hot sync could not stage " +
                                $"startup-bound host config {key}: {startupWriteMessage}");
                        }
                    }

                    Log.Warning(
                        $"[MP-MeowOnlineShop] MP config hot sync staged but did not " +
                        $"hot-apply startup-bound config {key}. XML Extensions already " +
                        "ran its XML patch operations during startup, so reloading only " +
                        "the settings object would leave generated Def data stale. " +
                        "Automatic join is blocked for this process; no restart is forced.");
                    continue;
                }

                // Load from a staging file first. If runtime application fails,
                // leave the active client file mismatched so a reconnect in the
                // same process cannot silently bypass the warning with stale
                // in-memory settings.
                string stagingPath = path + ".mp-runtime-import.xml";
                if (!TryWriteConfigAtomically(
                        stagingPath,
                        hostContents,
                        out string stagingWriteMessage))
                {
                    Log.Warning(
                        $"[MP-MeowOnlineShop] MP config hot sync: staging write failed for {key}, " +
                        $"path={stagingPath}, err={stagingWriteMessage}");
                    writeFailed++;
                    continue;
                }

                bool runtimeApplied;
                string reloadMsg;
                try
                {
                    runtimeApplied = TryReloadRuntimeSettings(
                        modId,
                        fileName,
                        stagingPath,
                        out reloadMsg);
                }
                finally
                {
                    TryDeleteFile(stagingPath);
                }

                if (!runtimeApplied)
                {
                    fileOnly++;
                    Log.Warning(
                        $"[MP-MeowOnlineShop] MP config hot sync rejected {key}: " +
                        $"runtime application was incomplete ({reloadMsg}). The active " +
                        "client config was not replaced and automatic join is blocked.");
                    continue;
                }
                reloaded++;

                // WriteSettings/SaveChanges callbacks commonly reserialize XML
                // with local formatting or field order. Multiplayer compares the
                // complete config text, so restore the host's canonical payload
                // after callbacks have updated runtime state.
                if (!TryWriteConfigAtomically(
                        path,
                        hostContents,
                        out string canonicalWriteMessage))
                {
                    writeFailed++;
                    Log.Warning(
                        $"[MP-MeowOnlineShop] MP config hot sync could not restore " +
                        $"canonical host text for {key}: {canonicalWriteMessage}");
                }
                else
                {
                    written++;
                }
            }

            if (localOnlyConfigs != null)
            {
                foreach (string localOnly in localOnlyConfigs)
                {
                    int separator = localOnly?.IndexOf('/') ?? -1;
                    if (separator <= 0 || separator >= localOnly.Length - 1)
                    {
                        resetFailed++;
                        continue;
                    }

                    string modId = localOnly.Substring(0, separator);
                    string fileName = localOnly.Substring(separator + 1);
                    if (TryResetLocalOnlyConfig(modId, fileName, out string resetMessage))
                    {
                        resetToHostDefaults++;
                    }
                    else
                    {
                        resetFailed++;
                        Log.Warning(
                            $"[MP-MeowOnlineShop] MP config hot sync could not reset " +
                            $"client-only config {localOnly} to host defaults: {resetMessage}");
                    }
                }
            }

            result =
                $"written={written}, writeFailed={writeFailed}, " +
                $"runtimeApplied={reloaded}, fileOnly={fileOnly}, unchanged={unchanged}, " +
                $"startupBound={startupBound}, resetToHostDefaults={resetToHostDefaults}, " +
                $"resetFailed={resetFailed}, changed=[{string.Join(", ", changedKeys)}]";
            filesApplied =
                (written > 0 || resetToHostDefaults > 0) &&
                writeFailed == 0 &&
                fileOnly == 0 &&
                resetFailed == 0;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static bool TryWriteConfigAtomically(
            string path,
            string contents,
            out string message)
        {
            message = "unknown";
            if (string.IsNullOrEmpty(path))
            {
                message = "path missing";
                return false;
            }

            string tempPath = path + ".mp-hot-sync.tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(tempPath, contents ?? string.Empty);
                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);

                message = "ok";
                return true;
            }
            catch (Exception e)
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                message = e.Message;
                return false;
            }
        }

        private static bool TryReloadRuntimeSettings(
            string modId,
            string fileName,
            string path,
            out string message)
        {
            message = "no matching mod instance";
            if (string.Equals(modId, HugsLibId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fileName, HugsLibSettingsFile, StringComparison.OrdinalIgnoreCase))
            {
                return TryReloadHugsLibSettings(path, out message);
            }

            var mod = FindRunningModContentPack(modId);
            if (mod == null)
                return false;

            var running = GetRunningModInstances();
            if (running == null)
            {
                message = "running mod instances collection unavailable";
                return false;
            }

            int matched = 0;
            int success = 0;
            foreach (var modInstance in running)
            {
                if (modInstance == null)
                    continue;
                var modType = modInstance.GetType();
                if (!mod.assemblies.loadedAssemblies.Contains(modType.Assembly))
                    continue;

                var settingsObj = GetModSettingsObject(modInstance);
                if (settingsObj == null)
                    continue;
                if (!string.Equals(modType.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                matched++;
                if (TryReloadSingleModInstance(modInstance, path, out var oneMsg))
                {
                    success++;
                }
                else
                {
                    message = oneMsg;
                }
            }

            if (matched == 0)
            {
                message = "no runtime modSettings instance matched";
                return false;
            }

            if (success > 0)
            {
                message = $"reloaded={success}/{matched}";
                return true;
            }

            return false;
        }

        private static bool TryReloadSingleModInstance(
            object modInstance,
            string path,
            out string message)
        {
            message = "unknown";
            if (modInstance == null)
            {
                message = "mod instance missing";
                return false;
            }

            var oldSettings = GetModSettingsObject(modInstance);
            if (oldSettings == null)
            {
                message = "modSettings missing";
                return false;
            }

            var modSettings = oldSettings as ModSettings;
            if (modSettings == null)
            {
                message = "runtime settings object is not Verse.ModSettings";
                return false;
            }

            bool loadingInitialized = false;
            bool enteredSettingsNode = false;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    message = "settings file missing after import";
                    return false;
                }

                Scribe.loader.InitLoading(path);
                loadingInitialized = true;
                if (!Scribe.EnterNode("ModSettings"))
                {
                    message = "SettingsBlock/ModSettings node missing";
                    return false;
                }
                enteredSettingsNode = true;

                // Load directly into the existing object. Mods frequently retain a
                // static reference to this instance, so replacing Mod.modSettings
                // would leave their simulation code reading stale values.
                modSettings.ExposeData();
                Scribe.ExitNode();
                enteredSettingsNode = false;
                Scribe.loader.FinalizeLoading();
                loadingInitialized = false;

                MethodInfo writeSettings = AccessTools.Method(
                    modInstance.GetType(),
                    "WriteSettings",
                    Type.EmptyTypes);
                writeSettings?.Invoke(modInstance, null);

                message = writeSettings == null
                    ? "reloaded in place; WriteSettings callback not found"
                    : "reloaded in place; WriteSettings callback invoked";
                return true;
            }
            catch (Exception e)
            {
                message = e.GetBaseException().Message;
                return false;
            }
            finally
            {
                if (enteredSettingsNode)
                {
                    try { Scribe.ExitNode(); }
                    catch { }
                }
                if (loadingInitialized)
                {
                    try { Scribe.loader.FinalizeLoading(); }
                    catch { }
                }
            }
        }

        private static bool TryReloadHugsLibSettings(string path, out string message)
        {
            try
            {
                XDocument document = XDocument.Load(path);
                XElement root = document.Root;
                if (root == null)
                {
                    message = "HugsLib settings XML has no root";
                    return false;
                }

                return TryApplyHugsLibSettingsRoot(root, out message);
            }
            catch (Exception e)
            {
                message = e.GetBaseException().Message;
                return false;
            }
        }

        private static bool TryApplyHugsLibSettingsRoot(
            XElement root,
            out string message)
        {
            message = "HugsLib SettingsManager unavailable";
            try
            {
                Type controllerType = AccessTools.TypeByName("HugsLib.HugsLibController");
                object manager = GetStaticFieldOrProperty<object>(
                    controllerType,
                    "SettingsManager");
                if (manager == null)
                    return false;

                var remotePacks = root.Elements()
                    .GroupBy(element => element.Name.LocalName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last(),
                        StringComparer.Ordinal);

                IEnumerable packs = GetFieldOrProperty<IEnumerable>(
                    manager,
                    "ModSettingsPacks");
                if (packs == null)
                {
                    message = "HugsLib ModSettingsPacks unavailable";
                    return false;
                }

                int applied = 0;
                int reset = 0;
                int failed = 0;
                foreach (object pack in packs)
                {
                    if (pack == null)
                        continue;

                    string modId = GetFieldOrProperty<string>(pack, "ModId");
                    remotePacks.TryGetValue(modId ?? string.Empty, out XElement remotePack);
                    IEnumerable handles = GetFieldOrProperty<IEnumerable>(pack, "Handles");
                    if (handles == null)
                        continue;

                    foreach (object handle in handles)
                    {
                        if (handle == null ||
                            GetFieldOrProperty<bool>(handle, "Unsaved"))
                        {
                            continue;
                        }

                        string name = GetFieldOrProperty<string>(handle, "Name");
                        XElement remoteValue = remotePack?
                            .Elements()
                            .FirstOrDefault(element =>
                                string.Equals(
                                    element.Name.LocalName,
                                    name,
                                    StringComparison.Ordinal));

                        try
                        {
                            if (remoteValue != null)
                            {
                                if (!SetFieldOrProperty(
                                        handle,
                                        "StringValue",
                                        remoteValue.Value))
                                {
                                    failed++;
                                    continue;
                                }
                                applied++;
                            }
                            else
                            {
                                MethodInfo resetMethod = AccessTools.Method(
                                    handle.GetType(),
                                    "ResetToDefault",
                                    Type.EmptyTypes);
                                if (resetMethod == null)
                                {
                                    failed++;
                                    continue;
                                }
                                resetMethod.Invoke(handle, null);
                                reset++;
                            }
                        }
                        catch
                        {
                            failed++;
                        }
                    }
                }

                MethodInfo saveChanges = AccessTools.Method(
                    manager.GetType(),
                    "SaveChanges",
                    Type.EmptyTypes);
                saveChanges?.Invoke(manager, null);

                message =
                    $"HugsLib handles applied={applied}, reset={reset}, " +
                    $"failed={failed}, callbacks={(saveChanges != null ? "invoked" : "missing")}";
                return failed == 0;
            }
            catch (Exception e)
            {
                message = e.GetBaseException().Message;
                return false;
            }
        }

        private static bool TryResetLocalOnlyConfig(
            string modId,
            string fileName,
            out string message)
        {
            string path = ResolveSettingsPath(modId, fileName);
            if (string.IsNullOrEmpty(path))
            {
                message = "settings path could not be resolved";
                return false;
            }

            string backupPath = path + ".mp-client-backup";
            try
            {
                if (File.Exists(path))
                    File.Copy(path, backupPath, true);

                bool reset;
                if (string.Equals(modId, HugsLibId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(fileName, HugsLibSettingsFile, StringComparison.OrdinalIgnoreCase))
                {
                    reset = TryApplyHugsLibSettingsRoot(
                        new XElement("settings"),
                        out message);
                }
                else
                {
                    reset = TryResetStandardModSettings(
                        modId,
                        fileName,
                        out message);
                }

                if (!reset)
                    return false;

                if (File.Exists(path))
                    File.Delete(path);

                message += $"; previous client config backed up to {backupPath}";
                return true;
            }
            catch (Exception e)
            {
                message = e.GetBaseException().Message;
                return false;
            }
        }

        private static bool TryResetStandardModSettings(
            string modId,
            string fileName,
            out string message)
        {
            message = "no runtime modSettings instance matched";
            ModContentPack mod = FindRunningModContentPack(modId);
            IEnumerable<object> running = GetRunningModInstances();
            if (mod == null || running == null)
                return false;

            int matched = 0;
            int reset = 0;
            foreach (object modInstance in running)
            {
                if (modInstance == null)
                    continue;

                Type modType = modInstance.GetType();
                if (!mod.assemblies.loadedAssemblies.Contains(modType.Assembly) ||
                    !string.Equals(modType.Name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ModSettings current = GetModSettingsObject(modInstance) as ModSettings;
                if (current == null)
                    continue;

                matched++;
                var snapshots = new List<KeyValuePair<FieldInfo, object>>();
                try
                {
                    object defaults = Activator.CreateInstance(
                        current.GetType(),
                        true);
                    if (defaults == null)
                    {
                        message = "default ModSettings instance could not be created";
                        continue;
                    }

                    foreach (FieldInfo field in GetConcreteSettingsFields(current.GetType()))
                    {
                        snapshots.Add(
                            new KeyValuePair<FieldInfo, object>(
                                field,
                                field.GetValue(current)));
                        field.SetValue(current, field.GetValue(defaults));
                    }

                    MethodInfo writeSettings = AccessTools.Method(
                        modType,
                        "WriteSettings",
                        Type.EmptyTypes);
                    writeSettings?.Invoke(modInstance, null);
                    reset++;
                }
                catch (Exception e)
                {
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        try
                        {
                            snapshots[i].Key.SetValue(
                                current,
                                snapshots[i].Value);
                        }
                        catch
                        {
                        }
                    }
                    message = e.GetBaseException().Message;
                }
            }

            if (matched == 0 || reset != matched)
                return false;

            message = $"reset runtime settings to defaults={reset}/{matched}";
            return true;
        }

        private static IEnumerable<FieldInfo> GetConcreteSettingsFields(Type type)
        {
            for (Type current = type;
                 current != null && current != typeof(ModSettings);
                 current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (!field.IsStatic && !field.IsInitOnly && !field.IsLiteral)
                        yield return field;
                }
            }
        }

        private static ModContentPack FindRunningModContentPack(string modId)
        {
            if (string.IsNullOrEmpty(modId) || LoadedModManager.RunningMods == null)
                return null;

            foreach (var mod in LoadedModManager.RunningMods)
            {
                if (mod == null)
                    continue;
                if (string.Equals(mod.PackageIdPlayerFacing, modId, StringComparison.OrdinalIgnoreCase))
                    return mod;
            }

            return null;
        }

        private static string ResolveSettingsPath(string modId, string fileName)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(fileName))
                return null;

            if (string.Equals(modId, HugsLibId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fileName, HugsLibSettingsFile, StringComparison.OrdinalIgnoreCase))
            {
                Type overrideType = AccessTools.TypeByName(
                    "Multiplayer.Client.Util.HugsLib_OverrideConfigsPatch");
                bool overrideActive = GetStaticFieldOrProperty<bool>(
                    overrideType,
                    "HugsLibConfigIsOverriden");
                string overridePath = GetStaticFieldOrProperty<string>(
                    overrideType,
                    "HugsLibConfigOverridePath");
                if (overrideActive && !string.IsNullOrEmpty(overridePath))
                    return overridePath;

                return Path.Combine(GenFilePaths.SaveDataFolderPath, "HugsLib", "ModSettings.xml");
            }

            var mod = FindRunningModContentPack(modId);
            if (mod != null)
                return InvokeGetSettingsFilename(mod.FolderName, fileName);

            return null;
        }

        private static IEnumerable<object> GetRunningModInstances()
        {
            var holder = GetStaticFieldOrProperty<object>(typeof(LoadedModManager), "runningModClasses")
                         ?? GetStaticFieldOrProperty<object>(typeof(LoadedModManager), "RunningModClasses");
            if (holder == null)
                return null;

            if (holder is IDictionary dictionary)
            {
                var list = new List<object>();
                foreach (DictionaryEntry entry in dictionary)
                    list.Add(entry.Value);
                return list;
            }

            var valuesProp = holder.GetType().GetProperty("Values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var values = valuesProp?.GetValue(holder, null) as IEnumerable;
            if (values == null)
                return null;

            var result = new List<object>();
            foreach (var v in values)
                result.Add(v);
            return result;
        }

        private static object GetModSettingsObject(object modInstance)
        {
            if (modInstance == null)
                return null;

            return GetFieldOrProperty<object>(modInstance, "modSettings")
                   ?? GetFieldOrProperty<object>(modInstance, "settings")
                   ?? GetFieldOrProperty<object>(modInstance, "_settings");
        }

        private static bool SetModSettingsObject(object modInstance, object value)
        {
            if (modInstance == null)
                return false;

            return SetFieldOrProperty(modInstance, "modSettings", value)
                   || SetFieldOrProperty(modInstance, "settings", value)
                   || SetFieldOrProperty(modInstance, "_settings", value);
        }

        private static string InvokeGetSettingsFilename(string folderName, string handleName)
        {
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(handleName))
                return null;

            var method = typeof(LoadedModManager)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "GetSettingsFilename", StringComparison.Ordinal))
                        return false;
                    var p = m.GetParameters();
                    return p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(string);
                });
            if (method == null)
                return null;

            try
            {
                return method.Invoke(null, new object[] { folderName, handleName }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static bool NodeHasAnyChildren(object node)
        {
            if (node == null)
                return false;
            var children = GetFieldOrProperty<object>(node, "children");
            if (children == null)
                return false;

            if (children is ICollection collection)
                return collection.Count > 0;
            if (children is IEnumerable enumerable)
            {
                foreach (var _ in enumerable)
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> GetLocalOnlyConfigPaths(
            object configsRoot,
            object remote)
        {
            var differingPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            object paths = GetFieldOrProperty<object>(configsRoot, "paths");
            if (paths is IEnumerable pathEnumerable)
            {
                foreach (object path in pathEnumerable)
                {
                    if (path is string text && !string.IsNullOrEmpty(text))
                        differingPaths.Add(text);
                }
            }

            var remoteKeys = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            IEnumerable remoteConfigs = GetFieldOrProperty<IEnumerable>(
                remote,
                "remoteModConfigs");
            if (remoteConfigs != null)
            {
                foreach (object config in remoteConfigs)
                {
                    string modId = GetFieldOrProperty<string>(config, "ModId");
                    string fileName = GetFieldOrProperty<string>(config, "FileName");
                    if (!string.IsNullOrEmpty(modId) &&
                        !string.IsNullOrEmpty(fileName))
                    {
                        remoteKeys.Add(modId + "/" + fileName);
                    }
                }
            }

            differingPaths.ExceptWith(remoteKeys);
            return differingPaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool SetFieldOrProperty(object instance, string name, object value)
        {
            if (instance == null || string.IsNullOrEmpty(name))
                return false;

            var flags = BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, flags);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return true;
                }

                var property = type.GetProperty(name, flags);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
            }

            return false;
        }

        private static T GetFieldOrProperty<T>(object instance, string name)
        {
            if (instance == null || string.IsNullOrEmpty(name))
                return default(T);

            var flags = BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, flags);
                if (field != null)
                {
                    object value = field.GetValue(instance);
                    if (value is T t)
                        return t;
                    return default(T);
                }

                var property = type.GetProperty(name, flags);
                if (property != null)
                {
                    object value = property.GetValue(instance, null);
                    if (value is T t)
                        return t;
                    return default(T);
                }
            }

            return default(T);
        }

        private static T GetStaticFieldOrProperty<T>(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return default(T);

            var flags = BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, flags);
                if (field != null)
                {
                    object value = field.GetValue(null);
                    if (value is T t)
                        return t;
                    return default(T);
                }

                var property = current.GetProperty(name, flags);
                if (property != null)
                {
                    object value = property.GetValue(null, null);
                    if (value is T t)
                        return t;
                    return default(T);
                }
            }

            return default(T);
        }
    }
}
