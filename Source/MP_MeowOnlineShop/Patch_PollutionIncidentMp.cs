using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 原版 DLC 污染类事件（空气污染器、毒污、废污舱等）联机兜底：
    /// 在污染相关 <see cref="IncidentWorker.TryExecuteWorker"/> 内将 <c>Find.CurrentMap</c> 替换为基于
    /// <see cref="IncidentParms.target"/> 的地图解析，避免各玩家视角地图不一致导致落点/Rand 分叉。
    /// 三重 Rand 仍由 <see cref="Patch_QuestAndIdeologyMp"/> 统一包裹；本类不重复 Push/Pop Rand。
    /// </summary>
    internal static class Patch_PollutionIncidentMp
    {
        private const string HarmonyId = "mp.meowonlineshop.pollutionincident";
        private static readonly Harmony Harmony = new Harmony(HarmonyId);

        /// <summary>与 <see cref="Patch_QuestAndIdeologyMp.IsPollutionRelatedIncidentWorkerType"/> 保持一致。</summary>
        private static bool IsPollutionRelatedIncidentWorkerType(Type t)
        {
            if (t == null) return false;
            var fn = (t.FullName ?? t.Name ?? "").ToLowerInvariant();
            if (fn.Contains("pollution") || fn.Contains("toxifier") || fn.Contains("wastepack")) return true;
            if (fn.Contains("airpollution")) return true;
            return false;
        }

        public static void Apply()
        {
            if (!MP.enabled)
                return;

            try
            {
                TryPatchPollutionIncidentFindCurrentMapTranspilers();
                if (ModDebug.EnablePollutionIncidentTrace)
                    Log.Message("[MP-MeowOnlineShop] Patch_PollutionIncidentMp: Apply completed (Transpiler for Find.CurrentMap on pollution IncidentWorkers).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Patch_PollutionIncidentMp.Apply failed: {e}");
            }
        }

        /// <summary>
        /// Transpiler 替换目标：供 IL 调用，签名需与 <c>Find.get_CurrentMap()</c> 一致（无参返回 Map）。
        /// </summary>
        public static Map MapFromIncidentParmsForPollution(IncidentParms parms)
        {
            if (parms == null)
                return SingleMapFallback();

            try
            {
                var mapProp = AccessTools.Property(typeof(IncidentParms), "Map");
                if (mapProp != null && typeof(Map).IsAssignableFrom(mapProp.PropertyType) && mapProp.CanRead)
                {
                    var m = mapProp.GetValue(parms, null) as Map;
                    if (m != null)
                        return m;
                }
            }
            catch
            {
                // ignored
            }

            try
            {
                var target = parms.target;
                if (target == null)
                    return SingleMapFallback();

                if (target is Map map)
                    return map;

                if (target is Thing th)
                    return th.MapHeld;

                var t = target.GetType();
                foreach (var name in new[] { "Map", "map" })
                {
                    var p = AccessTools.Property(t, name);
                    if (p != null && typeof(Map).IsAssignableFrom(p.PropertyType) && p.CanRead)
                    {
                        var m = p.GetValue(target, null) as Map;
                        if (m != null)
                            return m;
                    }

                    var f = AccessTools.Field(t, name);
                    if (f != null && typeof(Map).IsAssignableFrom(f.FieldType))
                    {
                        var m = f.GetValue(target) as Map;
                        if (m != null)
                            return m;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return SingleMapFallback();
        }

        private static Map SingleMapFallback()
        {
            return Find.Maps != null && Find.Maps.Count == 1
                ? Find.Maps[0]
                : null;
        }

        private static void TryPatchPollutionIncidentFindCurrentMapTranspilers()
        {
            var transpiler = AccessTools.Method(typeof(Patch_PollutionIncidentMp), nameof(ReplaceFindCurrentMapWithParmsMapTranspiler));
            var tracePrefix = AccessTools.Method(typeof(Patch_PollutionIncidentMp), nameof(PollutionIncidentTryExecuteWorker_TracePrefix));
            if (transpiler == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Patch_PollutionIncidentMp: Transpiler method not found, skip.");
                return;
            }

            var findCurrentMapGetter = AccessTools.Property(typeof(Find), "CurrentMap")?.GetGetMethod(true);
            if (findCurrentMapGetter == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Patch_PollutionIncidentMp: Find.CurrentMap getter not found, skip.");
                return;
            }

            var patchedHandles = new HashSet<RuntimeMethodHandle>();
            var success = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null || asm.IsDynamic)
                    continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(x => x != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || !typeof(IncidentWorker).IsAssignableFrom(t))
                        continue;

                    if (!IsPollutionRelatedIncidentWorkerType(t))
                        continue;

                    var ns = t.Namespace ?? "";
                    if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.Ordinal) >= 0)
                        continue;

                    var m = AccessTools.Method(t, "TryExecuteWorker", new[] { typeof(IncidentParms) });
                    if (m == null || m.IsAbstract)
                        continue;

                    if (!patchedHandles.Add(m.MethodHandle))
                        continue;

                    try
                    {
                        var hmTranspiler = new HarmonyMethod(transpiler);
                        if (ModDebug.EnablePollutionIncidentTrace && tracePrefix != null)
                            Harmony.Patch(m, prefix: new HarmonyMethod(tracePrefix) { priority = Priority.Last }, transpiler: hmTranspiler);
                        else
                            Harmony.Patch(m, transpiler: hmTranspiler);
                        success++;

                        if (ModDebug.EnablePollutionIncidentTrace)
                            Log.Message($"[MP-MeowOnlineShop] Patch_PollutionIncidentMp: Transpiler applied to {t.FullName}.TryExecuteWorker");
                    }
                    catch (Exception ex)
                    {
                        patchedHandles.Remove(m.MethodHandle);
                        if (ModDebug.EnablePollutionIncidentTrace)
                            Log.Warning($"[MP-MeowOnlineShop] Patch_PollutionIncidentMp: patch {t.FullName}.TryExecuteWorker failed: {ex.Message}");
                    }
                }
            }

            Log.Message(
                $"[MP-MeowOnlineShop] Patch_PollutionIncidentMp: Find.CurrentMap→parms map Transpiler applied to {success} pollution-related IncidentWorker.TryExecuteWorker method(s).");
        }

        private static void PollutionIncidentTryExecuteWorker_TracePrefix(IncidentParms parms, IncidentWorker __instance)
        {
            if (!MP.IsInMultiplayer || !ModDebug.EnablePollutionIncidentTrace)
                return;
            try
            {
                var wn = __instance?.GetType().FullName ?? "?";
                var map = MapFromIncidentParmsForPollution(parms);
                Log.Message(
                    $"[MP-MeowOnlineShop] PollutionIncident trace: worker={wn} parmsMapIndex={map?.Index ?? -1} findCurrentMapIndex={Find.CurrentMap?.Index ?? -1}");
            }
            catch
            {
                // ignored
            }
        }

        private static IEnumerable<CodeInstruction> ReplaceFindCurrentMapWithParmsMapTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var findCurrentMapGetter = AccessTools.Property(typeof(Find), "CurrentMap")?.GetGetMethod(true);
            if (findCurrentMapGetter == null)
            {
                foreach (var i in instructions)
                    yield return i;
                yield break;
            }

            var replacer = AccessTools.Method(typeof(Patch_PollutionIncidentMp), nameof(MapFromIncidentParmsForPollution));
            foreach (var ci in instructions)
            {
                if (ci.Calls(findCurrentMapGetter))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, replacer);
                }
                else
                    yield return ci;
            }
        }
    }
}
