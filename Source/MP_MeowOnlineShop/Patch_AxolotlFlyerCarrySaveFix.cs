using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Axolotl.PawnFlyerMode 保存修复：
    /// 避免 carriedThing 在不属于 innerContainer 时被 Scribe_References 写出，触发 not deep-saved。
    /// </summary>
    internal static class Patch_AxolotlFlyerCarrySaveFix
    {
        private static readonly Type FlyerType = AccessTools.TypeByName("Axolotl.PawnFlyerMode");
        private static readonly FieldInfo CarriedThingField = AccessTools.Field(FlyerType, "carriedThing");
        private static readonly FieldInfo InnerContainerField = AccessTools.Field(FlyerType, "innerContainer");
        private static readonly MethodInfo ExposeDataMethod = AccessTools.Method(FlyerType, "ExposeData");
        private static readonly MethodInfo RespawnPawnMethod = AccessTools.Method(FlyerType, "RespawnPawn");

        private static bool _patched;
        private static int _runtimeLogCount;
        private const int MaxRuntimeLogs = 8;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || _patched)
                return;

            if (FlyerType == null || CarriedThingField == null || InnerContainerField == null || ExposeDataMethod == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Axolotl flyer carry-save fix skipped: signatures not resolved.");
                return;
            }

            try
            {
                var prefix = AccessTools.Method(typeof(Patch_AxolotlFlyerCarrySaveFix), nameof(ExposeDataPrefix));
                var postfix = AccessTools.Method(typeof(Patch_AxolotlFlyerCarrySaveFix), nameof(ExposeDataPostfix));
                harmony.Patch(ExposeDataMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));

                if (RespawnPawnMethod != null)
                {
                    var respawnPostfix = AccessTools.Method(typeof(Patch_AxolotlFlyerCarrySaveFix), nameof(RespawnPawnPostfix));
                    harmony.Patch(RespawnPawnMethod, postfix: new HarmonyMethod(respawnPostfix));
                }

                _patched = true;
                Log.Message("[MP-MeowOnlineShop] Axolotl flyer carry-save fix patch active.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl flyer carry-save fix patch failed: {e.Message}");
            }
        }

        private static void ExposeDataPrefix(object __instance, out Thing __state)
        {
            __state = null;
            if (__instance == null || Scribe.mode != LoadSaveMode.Saving)
                return;

            Thing carriedThing;
            try
            {
                carriedThing = CarriedThingField.GetValue(__instance) as Thing;
            }
            catch
            {
                return;
            }

            if (carriedThing == null)
                return;

            object innerContainer = null;
            try
            {
                innerContainer = InnerContainerField.GetValue(__instance);
            }
            catch
            {
                // ignored
            }

            bool shouldStrip = carriedThing.Destroyed || !IsThingInContainer(innerContainer, carriedThing);
            if (!shouldStrip)
                return;

            __state = carriedThing;
            CarriedThingField.SetValue(__instance, null);
            RuntimeLog($"Stripped stale carriedThing during save: {carriedThing.LabelCap}");
        }

        private static void ExposeDataPostfix(object __instance, Thing __state)
        {
            if (__instance == null || __state == null || Scribe.mode != LoadSaveMode.Saving)
                return;

            try
            {
                CarriedThingField.SetValue(__instance, __state);
            }
            catch
            {
                // ignored
            }
        }

        // 落地后若 carriedThing 已不在 innerContainer，清空残留字段，减少后续保存窗口写出旧引用。
        private static void RespawnPawnPostfix(object __instance)
        {
            if (__instance == null)
                return;

            try
            {
                var carriedThing = CarriedThingField.GetValue(__instance) as Thing;
                if (carriedThing == null)
                    return;

                var innerContainer = InnerContainerField.GetValue(__instance);
                if (IsThingInContainer(innerContainer, carriedThing))
                    return;

                CarriedThingField.SetValue(__instance, null);
                RuntimeLog("Cleared carriedThing residue after RespawnPawn.");
            }
            catch
            {
                // ignored
            }
        }

        private static bool IsThingInContainer(object containerObj, Thing thing)
        {
            if (containerObj == null || thing == null)
                return false;

            if (containerObj is ThingOwner<Thing> owner)
                return owner.Contains(thing);

            if (containerObj is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (ReferenceEquals(item, thing))
                        return true;
                }
            }

            return false;
        }

        private static void RuntimeLog(string msg)
        {
            if (_runtimeLogCount >= MaxRuntimeLogs)
                return;
            _runtimeLogCount++;
            Log.Message("[MP-MeowOnlineShop] Axolotl flyer carry-save fix: " + msg);
        }
    }
}
