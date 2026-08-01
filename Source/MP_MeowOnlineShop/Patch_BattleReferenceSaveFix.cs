using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 修复联机存档阶段 Pawn_RecordsTracker.battleActive 可能引用未被 deep-save 的 Battle，
    /// 导致保存警告与重载时 CrossRef 失败的问题。
    /// </summary>
    internal static class Patch_BattleReferenceSaveFix
    {
        private static readonly FieldInfo BattleActiveField = AccessTools.Field(typeof(Pawn_RecordsTracker), "battleActive");
        private static bool _patched;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || _patched || BattleActiveField == null)
                return;

            try
            {
                var exposeData = AccessTools.Method(typeof(Pawn_RecordsTracker), "ExposeData");
                if (exposeData == null)
                    return;

                var prefix = AccessTools.Method(typeof(Patch_BattleReferenceSaveFix), nameof(ExposeData_Prefix));
                var postfix = AccessTools.Method(typeof(Patch_BattleReferenceSaveFix), nameof(ExposeData_Postfix));
                if (prefix == null || postfix == null)
                    return;

                harmony.Patch(exposeData, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                _patched = true;
                Log.Message("[MP-MeowOnlineShop] Patched Pawn_RecordsTracker.ExposeData (battleActive save fix).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] battleActive save fix patch failed: {e.Message}");
            }
        }

        private static void ExposeData_Prefix(Pawn_RecordsTracker __instance, out Battle __state)
        {
            __state = null;

            if (__instance == null || BattleActiveField == null || Scribe.mode != LoadSaveMode.Saving)
                return;

            var battle = BattleActiveField.GetValue(__instance) as Battle;
            if (battle == null)
                return;

            __state = battle;
            BattleActiveField.SetValue(__instance, null);
        }

        private static void ExposeData_Postfix(Pawn_RecordsTracker __instance, Battle __state)
        {
            if (__instance == null || BattleActiveField == null || Scribe.mode != LoadSaveMode.Saving || __state == null)
                return;

            BattleActiveField.SetValue(__instance, __state);
        }
    }
}
