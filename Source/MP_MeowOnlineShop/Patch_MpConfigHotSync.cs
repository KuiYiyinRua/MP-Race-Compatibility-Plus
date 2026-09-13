using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Classifies host config mismatches before touching any local state.
    /// Configs that can be proven in memory and on disk are hot-applied with a
    /// rollback guard; startup-bound and unverifiable configs are left for the
    /// native Fix and Restart flow. Automatic continuation is only allowed when
    /// every changed config was verified hot, so a partial or stale runtime is
    /// never silently joined.
    /// </summary>
    internal static class Patch_MpConfigHotSync
    {
        private const string EnableArg = "mpmeowhotcfg";
        private const string DiagnosticArg = "mpmeowhotcfgdiag";
        private const string HugsLibId = "unlimitedhugs.hugslib";
        private const string HugsLibSettingsFile = "ModSettings";
        private const string LoadingProgressId = "ilyvion.loadingprogress";
        // These packages expose only presentation preferences.  They must be
        // ignored on both peers before Multiplayer snapshots config files;
        // importing one peer's preferred visuals is neither necessary nor
        // desirable for deterministic simulation.
        private static readonly string[] LocalOnlyConfigModIds =
        {
            "crashm.colorcodedmoodbar.11",
            "ab.hatweaker",
            "mlie.showmeyourhands",
            "stm.shellabackgrounds"
        };
        // These settings affect simulation or startup Def construction.  They
        // are deliberately never added to the local-only filter.  Diagnostic
        // output for them is gated so normal joins do not leak config contents
        // or add noisy logging.
        private static readonly HashSet<string> GameplayConfigModIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "hentailoliteam.axolotl",
                "smashphil.vehicleframework",
                "ferny.perspectiveshift"
            };
        // XML Extensions used to be classified as startup-bound because its
        // XmlMod settings feed def-time patch operations.  XmlModBaseSettings
        // is a normal Verse.ModSettings and XmlMod inherits the standard
        // WriteSettings path, so the generic hot-sync reload below can apply
        // and verify it in place.  Only add a package when it genuinely
        // cannot reload its settings without a restart.
        private static readonly HashSet<string> StartupBoundConfigModIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Installed TickList patches cannot be changed by config hot reload.
                "local.mp.meowonlineshop.sellslingshot"
            };

        private static readonly HashSet<string> WrittenThisProcess =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<object> ProcessedWindows =
            new HashSet<object>(ReferenceEqualityComparer.Instance);
        private static readonly HashSet<string> LoggedDiagnosticPhases =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool _applied;
        private static bool _disabledLogged;
        private static MethodInfo _joinDataWindowCloseMethod;
        private static bool _joinDataWindowMembersValidated;
        private static MethodInfo _getSettingsFilenameMethod;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
                return;

            _applied = true;

            if (!IsEnabled())
            {
                if (!_disabledLogged)
                {
                    _disabledLogged = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] MP config hot sync disabled by " +
                        $"{EnableArg}=false; config mismatches stay on the native " +
                        "Multiplayer temp-config + restart flow.");
                }
                return;
            }

            ApplyIgnoredConfigFilters();

            if (IsDiagnosticEnabled())
            {
                Type syncConfigsType = AccessTools.TypeByName("Multiplayer.Client.Util.SyncConfigs");
                Log.Message(
                    "[MP-MeowOnlineShop][ConfigDiag] phase=startup, restartChildProcess=" +
                    GetStaticFieldOrProperty<bool>(syncConfigsType, "Applicable"));
            }

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
                var postfix = AccessTools.Method(
                    typeof(Patch_MpConfigHotSync),
                    nameof(JoinDataWindow_PostOpen_Postfix));
                _joinDataWindowCloseMethod = AccessTools.Method(
                    joinDataWindowType,
                    "Close",
                    new[] { typeof(bool) });
                if (postOpen == null || postfix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] MP config hot sync: PostOpen patch target not resolved.");
                    return;
                }

                harmony.Patch(postOpen, postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
                Log.Message(
                    "[MP-MeowOnlineShop] MP host-config hot sync active: verifiable " +
                    "config-only mismatches are imported, reloaded and verified before " +
                    "world download; unverifiable items fall back to restart and block " +
                    "automatic continuation.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] MP config hot sync patch failed: " + e);
            }
        }

        private static bool IsEnabled()
        {
            if (!GenCommandLine.TryGetCommandLineArg(EnableArg, out string value))
                return true;
            return !bool.TryParse(value, out bool enabled) || enabled;
        }

        private static bool IsDiagnosticEnabled()
        {
            if (!GenCommandLine.TryGetCommandLineArg(DiagnosticArg, out string value))
                return false;
            return bool.TryParse(value, out bool enabled) && enabled;
        }

        private static void ApplyIgnoredConfigFilters()
        {
            try
            {
                Type syncConfigsType = AccessTools.TypeByName("Multiplayer.Client.Util.SyncConfigs");
                FieldInfo ignoredField = AccessTools.Field(syncConfigsType, "ignoredConfigsModIds");
                string[] ignored = ignoredField?.GetValue(null) as string[];
                if (ignored == null)
                    return;

                string[] additions = new[] { LoadingProgressId }
                    .Concat(LocalOnlyConfigModIds)
                    .Where(addition => !ignored.Any(id =>
                        string.Equals(id, addition, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (additions.Length == 0)
                    return;

                ignoredField.SetValue(null, ignored.Concat(additions).ToArray());
                Log.Message(
                    "[MP-MeowOnlineShop] Multiplayer config comparison ignores local-only " +
                    "presentation settings: " + string.Join(", ", additions) + ".");
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Failed to add the local-only config filter: " +
                    exception.Message);
            }
        }

        private static void JoinDataWindow_PostOpen_Postfix(object __instance)
        {
            if (__instance == null)
                return;

            // Multiplayer's native Fix and Restart flow has already replaced
            // GetSettingsFilename with its temp-config override in this child
            // process.  Do not reopen a second transaction against that view:
            // the native child is authoritative and should join normally.
            Type syncConfigsType = AccessTools.TypeByName("Multiplayer.Client.Util.SyncConfigs");
            if (GetStaticFieldOrProperty<bool>(syncConfigsType, "Applicable"))
            {
                if (IsDiagnosticEnabled())
                {
                    Log.Message(
                        "[MP-MeowOnlineShop][ConfigDiag] phase=restart-child-skip, " +
                        "restartChildProcess=True");
                }
                return;
            }

            if (!ProcessedWindows.Add(__instance))
                return;

            try
            {
                if (!TryResolveAutoHotSyncContext(__instance, out var remote, out var connectAnyway))
                    return;

                var localOnlyConfigs = GetLocalOnlyConfigPaths(
                    GetFieldOrProperty<object>(__instance, "configsRoot"),
                    remote);

                var result = TryApplyRemoteConfigs(remote, localOnlyConfigs);

                foreach (string line in result.AuditLines)
                    Log.Message("[MP-MeowOnlineShop] MP config hot sync item: " + line);

                if (!result.SafeToAutoConnect)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] MP config hot sync cannot safely continue: " +
                        "automatic join is suppressed and already-applied changes were " +
                        "rolled back; use the native Fix and Restart flow or review the " +
                        "config tab manually. " + result.Summary);
                    return;
                }

                try { Patch_MultifactionTpsOptimize.NotifyModSettingsUpdated(); }
                catch { }

                Log.Message(
                    "[MP-MeowOnlineShop] MP config hot sync verified; continuing join. " +
                    result.Summary);

                try
                {
                    connectAnyway.Invoke();
                    _joinDataWindowCloseMethod?.Invoke(__instance, new object[] { false });
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] MP config hot sync: auto-continue failed, " +
                        "fallback to manual action: " + e.Message);
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
                if (joinDataWindowType.GetField(name, flags) == null &&
                    joinDataWindowType.GetProperty(name, flags) == null)
                {
                    Log.Warning(
                        $"[MP-MeowOnlineShop] MP config hot sync: JoinDataWindow missing required member '{name}'.");
                    return false;
                }
            }

            _joinDataWindowMembersValidated = true;
            return true;
        }

        private static bool TryResolveAutoHotSyncContext(
            object joinDataWindow,
            out object remote,
            out Action connectAnyway)
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

        private static HotSyncResult TryApplyRemoteConfigs(
            object remote,
            IEnumerable<string> localOnlyConfigs)
        {
            var result = new HotSyncResult();
            var remoteConfigs = GetFieldOrProperty<IEnumerable>(remote, "remoteModConfigs");
            if (remoteConfigs == null)
            {
                result.Summary = "remoteModConfigs missing";
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var records = new List<RemoteConfigRecord>();
            foreach (var cfg in remoteConfigs)
            {
                if (cfg == null)
                    continue;

                string modId = GetFieldOrProperty<string>(cfg, "ModId");
                string fileName = GetFieldOrProperty<string>(cfg, "FileName");
                string contents = GetFieldOrProperty<string>(cfg, "Contents");

                if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(fileName))
                {
                    result.AddItem("(missing)", "(missing)", ItemOutcome.Rejected,
                        "remote record missing ModId/FileName");
                    continue;
                }

                string key = modId + "/" + fileName;
                if (!seen.Add(key))
                    continue;

                records.Add(new RemoteConfigRecord(key, modId, fileName, contents ?? string.Empty));
            }

            // Do not import one config and then leave it live when a later
            // item requires the native restart path.  In particular, a
            // startup-bound XML Extensions setting must be discovered before
            // any ModSettings instance or settings file is touched.
            if (!PreflightRemoteConfigs(result, records, localOnlyConfigs))
            {
                result.Finish();
                return result;
            }

            var transaction = new HotSyncTransaction();
            foreach (RemoteConfigRecord record in records)
            {
                ProcessRemoteConfig(
                    result,
                    transaction,
                    record.Key,
                    record.ModId,
                    record.FileName,
                    record.HostContents);
            }

            result.Finish();
            if (!result.SafeToAutoConnect)
            {
                transaction.Rollback();
                result.MarkRolledBack(transaction.RollbackCount);
            }

            return result;
        }

        private static bool PreflightRemoteConfigs(
            HotSyncResult result,
            IEnumerable<RemoteConfigRecord> records,
            IEnumerable<string> localOnlyConfigs)
        {
            bool safe = true;
            foreach (RemoteConfigRecord record in records)
            {
                if (Encoding.UTF8.GetByteCount(record.HostContents) > 8 * 1024 * 1024)
                {
                    result.AddItem(
                        record.ModId,
                        record.FileName,
                        ItemOutcome.Rejected,
                        "host config content exceeds the 8 MB safety limit");
                    safe = false;
                    continue;
                }

                string path = ResolveSettingsPath(record.ModId, record.FileName);
                LogGameplayConfigDiagnostic("preflight", record, path, null, null);
                if (string.IsNullOrEmpty(path))
                {
                    result.AddItem(
                        record.ModId,
                        record.FileName,
                        ItemOutcome.Rejected,
                        "path validation failed; mod/file name not accepted");
                    safe = false;
                    continue;
                }

                if (StartupBoundConfigModIds.Contains(record.ModId))
                {
                    result.AddItem(
                        record.ModId,
                        record.FileName,
                        ItemOutcome.RestartRequired,
                        "startup-bound config requires Multiplayer native Fix and Restart");
                    safe = false;
                }

                byte[] localBytes = ReadFileBytesOrNull(path);
                bool alreadyStaged = WrittenThisProcess.Contains(record.Key);
                bool hostMatchesLocal = localBytes != null &&
                    ByteArrayEquals(localBytes, Encoding.UTF8.GetBytes(record.HostContents));
                if (alreadyStaged && hostMatchesLocal)
                {
                    result.AddItem(
                        record.ModId,
                        record.FileName,
                        ItemOutcome.Unchanged,
                        "config was already staged and verified in this process");
                }
            }

            foreach (string localOnly in localOnlyConfigs ?? Enumerable.Empty<string>())
            {
                int separator = localOnly?.IndexOf('/') ?? -1;
                if (separator <= 0 || separator >= localOnly.Length - 1)
                {
                    result.AddItem("(unknown)", localOnly ?? string.Empty, ItemOutcome.Rejected,
                        "malformed local-only config key");
                    safe = false;
                    continue;
                }

                string modId = localOnly.Substring(0, separator);
                string fileName = localOnly.Substring(separator + 1);
                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.RestartRequired,
                    "client-only config is not present on host; native reset/restart required");
                safe = false;
            }

            return safe;
        }

        private static void ProcessRemoteConfig(
            HotSyncResult result,
            HotSyncTransaction transaction,
            string key,
            string modId,
            string fileName,
            string hostContents)
        {
            if (Encoding.UTF8.GetByteCount(hostContents) > 8 * 1024 * 1024)
            {
                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.Rejected,
                    "host config content exceeds the 8 MB safety limit");
                return;
            }

            string path = ResolveSettingsPath(modId, fileName);
            if (string.IsNullOrEmpty(path))
            {
                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.Rejected,
                    "path validation failed; mod/file name not accepted");
                return;
            }

            var record = new RemoteConfigRecord(key, modId, fileName, hostContents);
            string runtimeBefore = GetRuntimeSettingsFingerprint(modId, fileName);

            if (StartupBoundConfigModIds.Contains(modId))
            {
                // Preflight is expected to reject this whole batch before any
                // state changes.  Keep a fail-closed guard in the executor.
                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.RestartRequired,
                    "startup-bound config requires Multiplayer native Fix and Restart");
                return;
            }

            byte[] originalBytes = ReadFileBytesOrNull(path);
            bool contentMatches = originalBytes != null &&
                                  string.Equals(
                                      Encoding.UTF8.GetString(originalBytes),
                                      hostContents,
                                      StringComparison.Ordinal);

            if (contentMatches && !WrittenThisProcess.Contains(key))
            {
                result.AddItem(modId, fileName, ItemOutcome.Unchanged, "local file already matches host");
                return;
            }

            if (contentMatches && WrittenThisProcess.Contains(key))
            {
                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.Unchanged,
                    "file matches after a previous verified hot sync in this process");
                return;
            }

            if (originalBytes != null &&
                !contentMatches &&
                XmlSemanticallyEqual(
                    Encoding.UTF8.GetString(originalBytes),
                    hostContents))
            {
                if (TryWriteFileAtomic(path, hostContents, out string canonicalizeMessage))
                {
                    transaction.TrackFile(path, originalBytes);
                    LogGameplayConfigDiagnostic(
                        "canonicalized",
                        record,
                        path,
                        runtimeBefore,
                        GetRuntimeSettingsFingerprint(modId, fileName));
                    result.AddItem(
                        modId,
                        fileName,
                        ItemOutcome.HotApplied,
                        "XML values already match; canonicalized local bytes to host text");
                    return;
                }

                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.Failed,
                    "semantically equal but byte canonicalization failed: " + canonicalizeMessage);
                return;
            }

            string stagingPath = Path.Combine(
                GenFilePaths.SaveDataFolderPath,
                "MPMeowHotSync",
                GenText.SanitizeFilename(
                    Guid.NewGuid().ToString("N") + "-" + modId + "-" + fileName + ".xml"));

            if (!TryWriteFileAtomic(stagingPath, hostContents, out string stagingMessage))
            {
                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.Failed,
                    "staging write failed: " + stagingMessage);
                return;
            }

            bool isHugsLib = string.Equals(modId, HugsLibId, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(fileName, HugsLibSettingsFile, StringComparison.OrdinalIgnoreCase);

            RuntimeSnapshot snapshot;
            if (isHugsLib)
                snapshot = HugsLibSnapshot.Capture();
            else
                snapshot = StandardSettingsSnapshot.Capture(modId, fileName);

            bool applied;
            bool verifiedDiff;
            string reloadMessage;
            if (isHugsLib)
            {
                applied = TryReloadHugsLibSettings(
                    stagingPath,
                    out verifiedDiff,
                    out reloadMessage);
            }
            else
            {
                applied = TryReloadStandardModSettings(
                    modId,
                    fileName,
                    stagingPath,
                    out verifiedDiff,
                    out reloadMessage);
            }

            bool requireDiff = !contentMatches;
            bool hot = applied && (!requireDiff || verifiedDiff);
            if (!hot)
            {
                snapshot.Rollback();
                TryDeleteFile(stagingPath);

                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.RestartRequired,
                    applied
                        ? "runtime loaded but field verification was inconclusive (" +
                          reloadMessage + "); native restart required"
                        : "runtime reload rejected: " + reloadMessage);
                return;
            }

            if (!TryWriteFileAtomic(path, hostContents, out string canonicalMessage))
            {
                snapshot.Rollback();
                TryDeleteFile(stagingPath);
                result.AddItem(
                    modId,
                    fileName,
                    ItemOutcome.Failed,
                    "runtime applied but canonical host file write failed: " + canonicalMessage);
                return;
            }

            bool wasWrittenThisProcess = WrittenThisProcess.Contains(key);
            WrittenThisProcess.Add(key);
            TryDeleteFile(stagingPath);
            transaction.TrackRuntime(
                snapshot,
                path,
                originalBytes,
                key,
                wasWrittenThisProcess);
            LogGameplayConfigDiagnostic(
                "hot-applied",
                record,
                path,
                runtimeBefore,
                GetRuntimeSettingsFingerprint(modId, fileName));
            result.AddItem(
                modId,
                fileName,
                ItemOutcome.HotApplied,
                "reloaded and verified (" + reloadMessage + ")");
        }

        // Diagnostics are intentionally limited to the three settings whose
        // values may alter gameplay.  They record hashes and stable field
        // fingerprints, never the XML contents themselves.
        private static void LogGameplayConfigDiagnostic(
            string phase,
            RemoteConfigRecord record,
            string path,
            string runtimeBefore,
            string runtimeAfter)
        {
            if (!IsDiagnosticEnabled() || record == null ||
                !GameplayConfigModIds.Contains(record.ModId))
                return;

            string diagnosticKey = phase + ":" + record.Key;
            if (!LoggedDiagnosticPhases.Add(diagnosticKey))
                return;

            string localHash = Sha256Hex(ReadFileBytesOrNull(path));
            string hostHash = Sha256Hex(Encoding.UTF8.GetBytes(record.HostContents));
            string before = runtimeBefore ??
                GetRuntimeSettingsFingerprint(record.ModId, record.FileName);
            string after = runtimeAfter ??
                GetRuntimeSettingsFingerprint(record.ModId, record.FileName);
            Type syncConfigsType = AccessTools.TypeByName("Multiplayer.Client.Util.SyncConfigs");
            bool restartChildProcess = GetStaticFieldOrProperty<bool>(syncConfigsType, "Applicable");

            Log.Message(
                "[MP-MeowOnlineShop][ConfigDiag] phase=" + phase +
                ", key=" + record.Key +
                ", localXmlSha256=" + localHash +
                ", hostXmlSha256=" + hostHash +
                ", fieldBefore=" + before +
                ", fieldAfter=" + after +
                ", restartChildProcess=" + restartChildProcess);
        }

        private static string GetRuntimeSettingsFingerprint(string modId, string fileName)
        {
            try
            {
                var mod = FindRunningModContentPack(modId);
                IEnumerable<object> running = GetRunningModInstances();
                if (mod == null || running == null)
                    return "unavailable";

                var fields = new List<string>();
                foreach (object modInstance in running)
                {
                    if (modInstance == null)
                        continue;
                    Type modType = modInstance.GetType();
                    if (!mod.assemblies.loadedAssemblies.Contains(modType.Assembly) ||
                        !string.Equals(modType.Name, fileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var settings = GetModSettingsObject(modInstance) as ModSettings;
                    if (settings == null)
                        continue;

                    foreach (FieldInfo field in GetConcreteSettingsFields(settings.GetType())
                                 .OrderBy(value => value.Name, StringComparer.Ordinal))
                    {
                        fields.Add(
                            settings.GetType().FullName + "." + field.Name + "=" +
                            StableDiagnosticValue(field.GetValue(settings)));
                    }
                }

                string joined = string.Join("|", fields);
                return "count=" + fields.Count + ";sha256=" +
                    Sha256Hex(Encoding.UTF8.GetBytes(joined));
            }
            catch (Exception e)
            {
                return "unavailable:" + e.GetType().Name;
            }
        }

        private static string StableDiagnosticValue(object value)
        {
            if (value == null)
                return "null";
            if (value is string text)
                return "str:" + Sha256Hex(Encoding.UTF8.GetBytes(text));
            if (value is bool || value is char || value.GetType().IsEnum ||
                value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            if (value is IEnumerable enumerable)
            {
                var parts = new List<string>();
                int count = 0;
                foreach (object item in enumerable)
                {
                    if (count++ == 64)
                    {
                        parts.Add("...");
                        break;
                    }
                    parts.Add(StableDiagnosticValue(item));
                }
                return "seq[" + string.Join(",", parts) + "]";
            }
            return "object:" + value.GetType().FullName;
        }

        private static string Sha256Hex(byte[] bytes)
        {
            if (bytes == null)
                return "missing";
            using (SHA256 sha256 = SHA256.Create())
            {
                return BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }

        private static string ResolveSettingsPath(string modId, string fileName)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(fileName))
                return null;

            if (!IsSafeSettingsFileName(fileName))
                return null;

            if (string.Equals(modId, HugsLibId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(fileName, HugsLibSettingsFile, StringComparison.OrdinalIgnoreCase))
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
                    return IsUnderSaveData(overridePath) ? Normalize(overridePath) : null;

                return Path.Combine(GenFilePaths.SaveDataFolderPath, "HugsLib", "ModSettings.xml");
            }

            var mod = FindRunningModContentPack(modId);
            if (mod == null)
                return null;

            var running = GetRunningModInstances();
            if (running == null)
                return null;

            bool instanceMatch = false;
            foreach (var modInstance in running)
            {
                if (modInstance == null)
                    continue;

                Type modType = modInstance.GetType();
                if (!string.Equals(modType.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!mod.assemblies.loadedAssemblies.Contains(modType.Assembly))
                    continue;
                if (GetModSettingsObject(modInstance) == null)
                    continue;

                instanceMatch = true;
                break;
            }

            if (!instanceMatch)
                return null;

            string resolved = InvokeGetSettingsFilename(mod.FolderName, fileName);
            if (string.IsNullOrEmpty(resolved))
                return null;
            return IsUnderSaveData(resolved) ? Normalize(resolved) : null;
        }

        private static bool IsSafeSettingsFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) ||
                fileName.Length > 120 ||
                string.Equals(fileName, ".", StringComparison.Ordinal) ||
                string.Equals(fileName, "..", StringComparison.Ordinal))
            {
                return false;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < fileName.Length; i++)
            {
                char c = fileName[i];
                if (c == Path.DirectorySeparatorChar ||
                    c == Path.AltDirectorySeparatorChar ||
                    c == ':')
                {
                    return false;
                }
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c == invalid[j])
                        return false;
                }
            }

            return true;
        }

        private static bool IsUnderSaveData(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                string root = Normalize(GenFilePaths.SaveDataFolderPath);
                string candidate = Normalize(path);
                return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                       candidate.StartsWith(
                           root.TrimEnd(Path.DirectorySeparatorChar) +
                           Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string Normalize(string path)
        {
            return Path.GetFullPath(path);
        }

        private static bool TryReloadStandardModSettings(
            string modId,
            string fileName,
            string path,
            out bool verifiedDiff,
            out string message)
        {
            verifiedDiff = false;
            message = "no matching mod instance";
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
            bool anyDiff = false;
            string lastError = null;
            foreach (var modInstance in running)
            {
                if (modInstance == null)
                    continue;

                Type modType = modInstance.GetType();
                if (!mod.assemblies.loadedAssemblies.Contains(modType.Assembly))
                    continue;
                if (!string.Equals(modType.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var settingsObj = GetModSettingsObject(modInstance);
                if (settingsObj == null)
                    continue;

                matched++;
                if (TryReloadSingleModInstance(
                        modInstance,
                        settingsObj,
                        path,
                        out bool singleDiff,
                        out string oneMsg))
                {
                    success++;
                    anyDiff |= singleDiff;
                }
                else
                {
                    message = oneMsg;
                    lastError = oneMsg;
                }
            }

            if (matched == 0)
            {
                message = "no runtime modSettings instance matched";
                return false;
            }

            if (success == matched)
            {
                verifiedDiff = anyDiff;
                message = $"reloaded={success}/{matched}, verifiedFieldDiff={anyDiff}";
                return true;
            }

            message = lastError ?? message;
            return false;
        }

        private static bool TryReloadSingleModInstance(
            object modInstance,
            object settingsObj,
            string path,
            out bool verifiedDiff,
            out string message)
        {
            verifiedDiff = false;
            message = "unknown";
            if (modInstance == null || settingsObj == null)
            {
                message = "mod instance or settings object missing";
                return false;
            }

            var modSettings = settingsObj as ModSettings;
            if (modSettings == null)
            {
                message = "runtime settings object is not Verse.ModSettings";
                return false;
            }

            MethodInfo writeSettings = AccessTools.Method(
                modInstance.GetType(),
                "WriteSettings",
                Type.EmptyTypes);
            if (writeSettings == null)
            {
                message = "WriteSettings callback not found; native restart required";
                return false;
            }

            bool loadingInitialized = false;
            bool enteredSettingsNode = false;
            var before = new List<KeyValuePair<FieldInfo, object>>();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    message = "settings file missing after import";
                    return false;
                }

                foreach (FieldInfo field in GetConcreteSettingsFields(modSettings.GetType()))
                    before.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(modSettings)));

                Scribe.loader.InitLoading(path);
                loadingInitialized = true;
                if (!Scribe.EnterNode("ModSettings"))
                {
                    message = "SettingsBlock/ModSettings node missing";
                    return false;
                }
                enteredSettingsNode = true;

                modSettings.ExposeData();
                Scribe.ExitNode();
                enteredSettingsNode = false;
                Scribe.loader.FinalizeLoading();
                loadingInitialized = false;

                try
                {
                    writeSettings.Invoke(modInstance, null);
                }
                catch (Exception callbackException)
                {
                    message = "WriteSettings callback threw: " +
                              callbackException.GetBaseException().Message;
                    return false;
                }

                foreach (KeyValuePair<FieldInfo, object> pair in before)
                {
                    object current = pair.Key.GetValue(modSettings);
                    if (!ValuesEqual(pair.Value, current))
                    {
                        verifiedDiff = true;
                        break;
                    }
                }

                message = "reloaded in place; WriteSettings callback invoked";
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

        private static bool TryReloadHugsLibSettings(
            string path,
            out bool verifiedDiff,
            out string message)
        {
            verifiedDiff = false;
            message = "HugsLib SettingsManager unavailable";
            try
            {
                XDocument document = XDocument.Load(path);
                XElement root = document.Root;
                if (root == null)
                {
                    message = "HugsLib settings XML has no root";
                    return false;
                }

                Type controllerType = AccessTools.TypeByName("HugsLib.HugsLibController");
                object manager = GetStaticFieldOrProperty<object>(
                    controllerType,
                    "SettingsManager");
                if (manager == null)
                    return false;

                IEnumerable packs = GetFieldOrProperty<IEnumerable>(
                    manager,
                    "ModSettingsPacks");
                if (packs == null)
                {
                    message = "HugsLib ModSettingsPacks unavailable";
                    return false;
                }

                var remotePacks = root.Elements()
                    .GroupBy(element => element.Name.LocalName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Last(),
                        StringComparer.Ordinal);

                int applied = 0;
                int reset = 0;
                int failed = 0;
                bool anyChanged = false;
                foreach (object pack in packs)
                {
                    if (pack == null)
                        continue;

                    string packModId = GetFieldOrProperty<string>(pack, "ModId");
                    remotePacks.TryGetValue(packModId ?? string.Empty, out XElement remotePack);
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
                            string beforeValue = GetFieldOrProperty<string>(handle, "StringValue") ?? string.Empty;
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
                                if (!string.Equals(
                                        beforeValue,
                                        remoteValue.Value,
                                        StringComparison.Ordinal))
                                {
                                    anyChanged = true;
                                }
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
                                string afterReset = GetFieldOrProperty<string>(handle, "StringValue") ?? string.Empty;
                                if (!string.Equals(
                                        beforeValue,
                                        afterReset,
                                        StringComparison.Ordinal))
                                {
                                    anyChanged = true;
                                }
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
                if (failed != 0)
                    return false;

                verifiedDiff = anyChanged;
                return true;
            }
            catch (Exception e)
            {
                message = e.GetBaseException().Message;
                return false;
            }
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
                    // Some mods keep every ModSettings value in static fields.
                    // Treat those as part of the runtime snapshot and reload
                    // verification; otherwise a failed later item could leave
                    // a gameplay setting changed with no rollback path.
                    if (!field.IsInitOnly && !field.IsLiteral)
                        yield return field;
                }
            }
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left == null || right == null)
                return left == right;

            if (left is IEnumerable leftEnumerable && right is IEnumerable rightEnumerable)
            {
                if (!(left is string) && !(right is string))
                {
                    if (left is IDictionary leftDict && right is IDictionary rightDict)
                    {
                        if (leftDict.Count != rightDict.Count)
                            return false;
                        foreach (DictionaryEntry entry in leftDict)
                        {
                            if (!rightDict.Contains(entry.Key))
                                return false;
                            if (!ValuesEqual(entry.Value, rightDict[entry.Key]))
                                return false;
                        }
                        return true;
                    }

                    var leftList = leftEnumerable.Cast<object>().ToList();
                    var rightList = rightEnumerable.Cast<object>().ToList();
                    if (leftList.Count != rightList.Count)
                        return false;
                    for (int i = 0; i < leftList.Count; i++)
                    {
                        if (!ValuesEqual(leftList[i], rightList[i]))
                            return false;
                    }
                    return true;
                }
            }

            return Equals(left, right);
        }

        private static bool XmlSemanticallyEqual(string left, string right)
        {
            if (string.Equals(left, right, StringComparison.Ordinal))
                return true;

            try
            {
                return ElementsEqual(
                    XDocument.Parse(left).Root,
                    XDocument.Parse(right).Root);
            }
            catch
            {
                return false;
            }
        }

        private static bool ElementsEqual(XElement left, XElement right)
        {
            if (left == null || right == null)
                return left == right;
            if (!string.Equals(left.Name.LocalName, right.Name.LocalName, StringComparison.Ordinal))
                return false;

            var leftChildren = left.Elements().ToList();
            var rightChildren = right.Elements().ToList();
            if (leftChildren.Count != rightChildren.Count)
                return false;

            for (int i = 0; i < leftChildren.Count; i++)
            {
                if (!ElementsEqual(leftChildren[i], rightChildren[i]))
                    return false;
            }

            string leftText = left.Value.Trim();
            string rightText = right.Value.Trim();
            return string.Equals(leftText, rightText, StringComparison.Ordinal);
        }

        private static byte[] ReadFileBytesOrNull(string path)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        private static bool ByteArrayEquals(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static bool TryRestoreFileBytesAtomic(string path, byte[] contents)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string tempPath = path + ".mp-hot-sync.rollback.tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                if (contents == null)
                {
                    TryDeleteFile(path);
                    return true;
                }

                File.WriteAllBytes(tempPath, contents);
                if (File.Exists(path))
                    File.Replace(tempPath, path, null);
                else
                    File.Move(tempPath, path);
                return true;
            }
            catch
            {
                TryDeleteFile(tempPath);
                return false;
            }
        }

        private static bool TryWriteFileAtomic(
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
                TryDeleteFile(tempPath);
                message = e.Message;
                return false;
            }
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

            var valuesProp = holder.GetType().GetProperty(
                "Values",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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

        private static string InvokeGetSettingsFilename(string folderName, string handleName)
        {
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(handleName))
                return null;

            if (_getSettingsFilenameMethod == null)
            {
                _getSettingsFilenameMethod = typeof(LoadedModManager)
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                    {
                        if (!string.Equals(m.Name, "GetSettingsFilename", StringComparison.Ordinal))
                            return false;
                        ParameterInfo[] p = m.GetParameters();
                        return p.Length == 2 &&
                               p[0].ParameterType == typeof(string) &&
                               p[1].ParameterType == typeof(string);
                    });
            }

            if (_getSettingsFilenameMethod == null)
                return null;

            try
            {
                return _getSettingsFilenameMethod.Invoke(
                    null,
                    new object[] { folderName, handleName }) as string;
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

        private enum ItemOutcome
        {
            HotApplied,
            Unchanged,
            RestartRequired,
            Failed,
            Rejected
        }

        private sealed class RemoteConfigRecord
        {
            public RemoteConfigRecord(string key, string modId, string fileName, string hostContents)
            {
                Key = key;
                ModId = modId;
                FileName = fileName;
                HostContents = hostContents;
            }

            public string Key { get; }
            public string ModId { get; }
            public string FileName { get; }
            public string HostContents { get; }
        }

        private sealed class HotSyncTransaction
        {
            private readonly List<CommittedConfig> _commits = new List<CommittedConfig>();
            public int RollbackCount { get; private set; }

            public void TrackFile(string path, byte[] originalBytes)
            {
                _commits.Add(new CommittedConfig(null, path, originalBytes, null, true));
            }

            public void TrackRuntime(
                RuntimeSnapshot snapshot,
                string path,
                byte[] originalBytes,
                string key,
                bool wasWrittenThisProcess)
            {
                _commits.Add(new CommittedConfig(
                    snapshot,
                    path,
                    originalBytes,
                    key,
                    wasWrittenThisProcess));
            }

            public void Rollback()
            {
                for (int i = _commits.Count - 1; i >= 0; i--)
                {
                    CommittedConfig commit = _commits[i];
                    try { commit.Snapshot?.Rollback(); }
                    catch { }
                    TryRestoreFileBytesAtomic(commit.Path, commit.OriginalBytes);
                    if (!commit.WasWrittenThisProcess && !string.IsNullOrEmpty(commit.Key))
                        WrittenThisProcess.Remove(commit.Key);
                    RollbackCount++;
                }
                _commits.Clear();
            }

            private sealed class CommittedConfig
            {
                public CommittedConfig(
                    RuntimeSnapshot snapshot,
                    string path,
                    byte[] originalBytes,
                    string key,
                    bool wasWrittenThisProcess)
                {
                    Snapshot = snapshot;
                    Path = path;
                    OriginalBytes = originalBytes;
                    Key = key;
                    WasWrittenThisProcess = wasWrittenThisProcess;
                }

                public RuntimeSnapshot Snapshot { get; }
                public string Path { get; }
                public byte[] OriginalBytes { get; }
                public string Key { get; }
                public bool WasWrittenThisProcess { get; }
            }
        }

        private sealed class HotSyncResult
        {
            private readonly List<string> _audit = new List<string>();
            private readonly List<KeyValuePair<string, string>> _changed =
                new List<KeyValuePair<string, string>>();
            private int _hot;
            private int _unchanged;
            private int _restart;
            private int _failed;
            private int _rejected;

            public IReadOnlyList<string> AuditLines => _audit;
            public bool SafeToAutoConnect { get; private set; }
            public string Summary { get; set; }

            public void AddItem(string modId, string fileName, ItemOutcome outcome, string reason)
            {
                string key = modId + "/" + fileName;
                switch (outcome)
                {
                    case ItemOutcome.HotApplied:
                        _hot++;
                        break;
                    case ItemOutcome.Unchanged:
                        _unchanged++;
                        break;
                    case ItemOutcome.RestartRequired:
                        _restart++;
                        _changed.Add(new KeyValuePair<string, string>(key, reason));
                        break;
                    case ItemOutcome.Failed:
                        _failed++;
                        _changed.Add(new KeyValuePair<string, string>(key, reason));
                        break;
                    case ItemOutcome.Rejected:
                        _rejected++;
                        _changed.Add(new KeyValuePair<string, string>(key, reason));
                        break;
                }

                _audit.Add(
                    $"{key}: outcome={outcome}, reason={reason}");
            }

            public void Finish()
            {
                SafeToAutoConnect =
                    _restart == 0 &&
                    _failed == 0 &&
                    _rejected == 0 &&
                    _hot > 0;

                Summary =
                    $"hot={_hot}, unchanged={_unchanged}, restartRequired={_restart}, " +
                    $"failed={_failed}, rejected={_rejected}, " +
                    $"changed=[{string.Join(", ", _changed.Select(pair => pair.Key + " (" + pair.Value + ")"))}]";
            }

            public void MarkRolledBack(int count)
            {
                Summary = (Summary ?? string.Empty) + ", transactionRolledBack=" + count;
            }
        }

        private abstract class RuntimeSnapshot
        {
            public abstract void Rollback();
        }

        private sealed class StandardSettingsSnapshot : RuntimeSnapshot
        {
            private readonly List<FieldState> _fields = new List<FieldState>();

            public static StandardSettingsSnapshot Capture(string modId, string fileName)
            {
                var snapshot = new StandardSettingsSnapshot();
                var mod = FindRunningModContentPack(modId);
                if (mod == null)
                    return snapshot;

                IEnumerable<object> running = GetRunningModInstances();
                if (running == null)
                    return snapshot;

                foreach (var modInstance in running)
                {
                    if (modInstance == null)
                        continue;
                    Type modType = modInstance.GetType();
                    if (!mod.assemblies.loadedAssemblies.Contains(modType.Assembly))
                        continue;
                    if (!string.Equals(modType.Name, fileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var settingsObj = GetModSettingsObject(modInstance) as ModSettings;
                    if (settingsObj == null)
                        continue;

                    foreach (FieldInfo field in GetConcreteSettingsFields(settingsObj.GetType()))
                    {
                        snapshot._fields.Add(
                            new FieldState(
                                settingsObj,
                                field,
                                SnapshotFieldValue(field.GetValue(settingsObj))));
                    }
                }

                return snapshot;
            }

            public override void Rollback()
            {
                foreach (FieldState state in _fields)
                {
                    try
                    {
                        state.Field.SetValue(state.SettingsObject, state.Value);
                    }
                    catch
                    {
                    }
                }
            }

            private static object SnapshotFieldValue(object value)
            {
                if (value is Array array)
                    return array.Clone();

                if (value is IList list)
                {
                    try
                    {
                        var clone = Activator.CreateInstance(value.GetType()) as IList;
                        if (clone != null)
                        {
                            foreach (object item in list)
                                clone.Add(item);
                            return clone;
                        }
                    }
                    catch
                    {
                    }
                }

                return value;
            }

            private sealed class FieldState
            {
                public FieldState(object settingsObject, FieldInfo field, object value)
                {
                    SettingsObject = settingsObject;
                    Field = field;
                    Value = value;
                }

                public object SettingsObject { get; }
                public FieldInfo Field { get; }
                public object Value { get; }
            }
        }

        private sealed class HugsLibSnapshot : RuntimeSnapshot
        {
            private readonly List<HandleState> _handles = new List<HandleState>();
            private object _manager;

            public static HugsLibSnapshot Capture()
            {
                var snapshot = new HugsLibSnapshot();
                Type controllerType = AccessTools.TypeByName("HugsLib.HugsLibController");
                snapshot._manager = GetStaticFieldOrProperty<object>(
                    controllerType,
                    "SettingsManager");
                if (snapshot._manager == null)
                    return snapshot;

                IEnumerable packs = GetFieldOrProperty<IEnumerable>(
                    snapshot._manager,
                    "ModSettingsPacks");
                if (packs == null)
                    return snapshot;

                foreach (object pack in packs)
                {
                    if (pack == null)
                        continue;
                    IEnumerable handles = GetFieldOrProperty<IEnumerable>(pack, "Handles");
                    if (handles == null)
                        continue;

                    foreach (object handle in handles)
                    {
                        if (handle == null)
                            continue;
                        snapshot._handles.Add(
                            new HandleState(
                                handle,
                                GetFieldOrProperty<string>(handle, "StringValue") ?? string.Empty,
                                GetFieldOrProperty<bool>(handle, "HasUnsavedChanges")));
                    }
                }

                return snapshot;
            }

            public override void Rollback()
            {
                foreach (HandleState state in _handles)
                {
                    try
                    {
                        SetFieldOrProperty(state.Handle, "StringValue", state.StringValue);
                        SetFieldOrProperty(state.Handle, "HasUnsavedChanges", state.HasUnsavedChanges);
                    }
                    catch
                    {
                    }
                }

                if (_manager != null)
                {
                    try
                    {
                        MethodInfo saveChanges = AccessTools.Method(
                            _manager.GetType(),
                            "SaveChanges",
                            Type.EmptyTypes);
                        saveChanges?.Invoke(_manager, null);
                    }
                    catch
                    {
                    }
                }
            }

            private sealed class HandleState
            {
                public HandleState(object handle, string stringValue, bool hasUnsavedChanges)
                {
                    Handle = handle;
                    StringValue = stringValue;
                    HasUnsavedChanges = hasUnsavedChanges;
                }

                public object Handle { get; }
                public string StringValue { get; }
                public bool HasUnsavedChanges { get; }
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance =
                new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
#pragma warning disable 618
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
#pragma warning restore 618
            }
        }
    }
}
