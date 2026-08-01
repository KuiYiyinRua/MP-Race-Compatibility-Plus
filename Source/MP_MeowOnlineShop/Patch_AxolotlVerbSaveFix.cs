using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Axolotl 武器 Verb 保存修复：
    /// 在保存前清理 VerbTracker.allVerbs 的重复项，避免
    /// "DebugLoadIDsSavingErrorsChecker ... already deepsaved" 导致联机 join point 保存失败。
    /// </summary>
    internal static class Patch_AxolotlVerbSaveFix
    {
        private static readonly Type VerbTrackerType = AccessTools.TypeByName("Verse.VerbTracker");
        private static readonly MethodInfo ExposeDataMethod = AccessTools.Method(VerbTrackerType, "ExposeData");
        private static readonly FieldInfo AllVerbsField = AccessTools.Field(VerbTrackerType, "allVerbs");
        private static readonly FieldInfo DirectOwnerField = AccessTools.Field(VerbTrackerType, "directOwner");

        private static bool _patched;
        private static int _runtimeLogCount;
        private const int MaxRuntimeLogs = 16;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || _patched)
                return;

            if (VerbTrackerType == null || ExposeDataMethod == null || AllVerbsField == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Axolotl verb save fix skipped: signatures not resolved.");
                return;
            }

            try
            {
                var prefix = AccessTools.Method(typeof(Patch_AxolotlVerbSaveFix), nameof(ExposeDataPrefix));
                harmony.Patch(ExposeDataMethod, prefix: new HarmonyMethod(prefix));
                _patched = true;
                Log.Message("[MP-MeowOnlineShop] Axolotl verb save fix patch active.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl verb save fix patch failed: {e.Message}");
            }
        }

        private static void ExposeDataPrefix(object __instance)
        {
            if (__instance == null || Scribe.mode != LoadSaveMode.Saving)
                return;

            if (!ShouldProcessTracker(__instance))
                return;

            if (!(AllVerbsField.GetValue(__instance) is IList list) || list.Count <= 1)
                return;

            var seenRefs = new HashSet<object>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var dupIndices = new List<int>();

            for (int i = 0; i < list.Count; i++)
            {
                var verbObj = list[i];
                if (verbObj == null)
                {
                    dupIndices.Add(i);
                    continue;
                }

                var uniqueId = TryGetUniqueLoadId(verbObj);
                bool duplicateByRef = !seenRefs.Add(verbObj);
                bool duplicateById = !string.IsNullOrEmpty(uniqueId) && !seenIds.Add(uniqueId);
                if (duplicateByRef || duplicateById)
                    dupIndices.Add(i);
            }

            if (dupIndices.Count == 0)
                return;

            for (int i = dupIndices.Count - 1; i >= 0; i--)
                list.RemoveAt(dupIndices[i]);

            RuntimeLog($"Removed duplicate verbs before save: count={dupIndices.Count}, owner={DescribeOwner(__instance)}");
        }

        private static bool ShouldProcessTracker(object trackerObj)
        {
            object ownerObj = null;
            try
            {
                ownerObj = DirectOwnerField?.GetValue(trackerObj);
            }
            catch
            {
                return false;
            }

            if (ownerObj == null)
                return false;

            var ownerTypeName = ownerObj.GetType().FullName ?? string.Empty;
            if (ownerTypeName.IndexOf("CompEquippable", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            object parentObj = null;
            try
            {
                parentObj = AccessTools.Field(ownerObj.GetType(), "parent")?.GetValue(ownerObj)
                            ?? AccessTools.Property(ownerObj.GetType(), "parent")?.GetValue(ownerObj, null);
            }
            catch
            {
                // ignored
            }

            var defName = TryGetDefName(parentObj);
            return !string.IsNullOrEmpty(defName)
                   && defName.IndexOf("Axolotl_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string DescribeOwner(object trackerObj)
        {
            try
            {
                var ownerObj = DirectOwnerField?.GetValue(trackerObj);
                var parentObj = AccessTools.Field(ownerObj?.GetType(), "parent")?.GetValue(ownerObj)
                                ?? AccessTools.Property(ownerObj?.GetType(), "parent")?.GetValue(ownerObj, null);
                return TryGetDefName(parentObj) ?? ownerObj?.GetType().Name ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

        private static string TryGetUniqueLoadId(object verbObj)
        {
            try
            {
                var method = AccessTools.Method(verbObj.GetType(), "GetUniqueLoadID");
                return method?.Invoke(verbObj, null) as string;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetDefName(object thingObj)
        {
            if (thingObj == null)
                return null;

            try
            {
                var defObj = AccessTools.Property(thingObj.GetType(), "def")?.GetValue(thingObj, null)
                             ?? AccessTools.Field(thingObj.GetType(), "def")?.GetValue(thingObj);
                if (defObj == null)
                    return null;

                return AccessTools.Property(defObj.GetType(), "defName")?.GetValue(defObj, null) as string
                       ?? AccessTools.Field(defObj.GetType(), "defName")?.GetValue(defObj) as string;
            }
            catch
            {
                return null;
            }
        }

        private static void RuntimeLog(string msg)
        {
            if (_runtimeLogCount >= MaxRuntimeLogs)
                return;
            _runtimeLogCount++;
            Log.Message("[MP-MeowOnlineShop] Axolotl verb save fix: " + msg);
        }
    }
}
