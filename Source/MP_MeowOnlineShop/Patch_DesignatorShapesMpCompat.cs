using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Designator Shapes 联机兼容：
    /// 在 MP 命令回放期间，屏蔽其 HistoryManager.AddEntry 写入，避免把非建造流程的 designation 记录进本地撤销栈。
    /// </summary>
    internal static class Patch_DesignatorShapesMpCompat
    {
        private static readonly Type HistoryManagerType = AccessTools.TypeByName("Merthsoft.DesignatorShapes.HistoryManager");
        private static readonly Type MultiplayerRuntimeType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
        private static readonly FieldInfo MultiplayerExecutingCmdsField = MultiplayerRuntimeType != null
            ? AccessTools.Field(MultiplayerRuntimeType, "ExecutingCmds")
            : null;
        private static readonly PropertyInfo MultiplayerExecutingCmdsProperty = MultiplayerRuntimeType != null
            ? AccessTools.Property(MultiplayerRuntimeType, "ExecutingCmds")
            : null;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || HistoryManagerType == null)
                return;

            bool patched = false;
            patched |= TryPatchAddEntry(harmony, new[] { typeof(Designation) }, nameof(AddEntryDesignation_Prefix));
            patched |= TryPatchAddEntry(harmony, new[] { typeof(Blueprint) }, nameof(AddEntryBlueprint_Prefix));
            patched |= TryPatchAddEntry(harmony, new[] { typeof(Designation), typeof(Blueprint) }, nameof(AddEntryDesignationBlueprint_Prefix));

            if (patched)
                Log.Message("[MP-MeowOnlineShop] DesignatorShapes MP compat enabled: skip HistoryManager.AddEntry during command replay.");
        }

        private static bool TryPatchAddEntry(Harmony harmony, Type[] argTypes, string prefixName)
        {
            try
            {
                var target = AccessTools.Method(HistoryManagerType, "AddEntry", argTypes);
                var prefix = AccessTools.Method(typeof(Patch_DesignatorShapesMpCompat), prefixName);
                if (target == null || prefix == null)
                    return false;

                harmony.Patch(target, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                return true;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] DesignatorShapes compat patch failed ({prefixName}): {e.Message}");
                return false;
            }
        }

        private static bool ShouldSkipHistoryEntry()
        {
            if (!MP.enabled || !MP.IsInMultiplayer)
                return false;

            return IsExecutingMultiplayerCommand();
        }

        private static bool IsExecutingMultiplayerCommand()
        {
            try
            {
                if (MultiplayerExecutingCmdsField != null && MultiplayerExecutingCmdsField.FieldType == typeof(bool))
                    return (bool)MultiplayerExecutingCmdsField.GetValue(null);
                if (MultiplayerExecutingCmdsProperty != null && MultiplayerExecutingCmdsProperty.PropertyType == typeof(bool))
                    return (bool)MultiplayerExecutingCmdsProperty.GetValue(null);
            }
            catch
            {
                // 反射失败时保守地不拦截，避免影响原行为。
            }

            return false;
        }

        private static bool AddEntryDesignation_Prefix(Designation des) => !ShouldSkipHistoryEntry();
        private static bool AddEntryBlueprint_Prefix(Blueprint bp) => !ShouldSkipHistoryEntry();
        private static bool AddEntryDesignationBlueprint_Prefix(Designation des, Blueprint bp) => !ShouldSkipHistoryEntry();
    }
}
