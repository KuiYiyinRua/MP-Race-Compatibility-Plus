using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Axolotl 修炼阅读联机兼容：
    /// 1) 将阅读 Job 内的 Rand.Range(1,4) 替换为确定性随机，避免 map/world 随机状态分叉；
    /// 2) 稳定化地图功法书枚举顺序，降低 UI 选书与工作分配在双端出现不同顺序的风险。
    /// </summary>
    internal static class Patch_AxolotlCultivationRead
    {
        private const string ItabTypeName = "Axolotl.ITab_MoeLotl_Cultivation";
        private const string TargetJobDriverTypeName = "Axolotl.JobDriver_ReadMoeLotlQiSkillBook";
        private const string CompCultivationTypeName = "Axolotl.Comp_Cultivation";
        private const string MoeLotlQiSkillUtilityTypeName = "Axolotl.MoeLotlQiSkillUtility";
        private const int SeedOffsetReadRotation = 0x6F31;
        private const int TargetReadSeedOffset = 0x54415247; // "TARG"
        private const int TargetReadSeedWorldOffset = 0x54415257; // "TARW"

        private static readonly MethodInfo RandRangeIntMethod = AccessTools.Method(typeof(Rand), nameof(Rand.Range), new[] { typeof(int), typeof(int) });
        private static readonly MethodInfo DeterministicRangeMethod = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(DeterministicReadRotationRange));
        private static readonly MethodInfo SyncTargetReadBookMethod = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(SyncSetTargetReadBook));
        private static readonly MethodInfo SyncToggleInstalledSkillMethod = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(SyncToggleInstalledSkill));
        private static readonly MethodInfo ReplaceTargetReadBookAssignmentMethod = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(SetTargetReadBookMaybeSync));
        private static readonly MethodInfo ReplaceInstallSkillMethod = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(InstallSkillMaybeSync));
        private static readonly MethodInfo ReplaceUninstallSkillMethod = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(UninstallSkillMaybeSync));

        private static bool _syncMethodRegistered;
        private static MethodInfo _installSkillMethod;
        private static MethodInfo _uninstallSkillMethod;
        private static FieldInfo _allLearnedSkillsField;
        private static FieldInfo _skillInstallListField;
        private static FieldInfo _skillDefField;
        private static int _toggleTraceCount;
        private const int MaxToggleTraceCount = 60;

        [ThreadStatic] private static int _scopeDepth;
        [ThreadStatic] private static int _scopeSeed;
        [ThreadStatic] private static int _scopeCounter;
        [ThreadStatic] private static bool _executingTargetReadSync;
        [ThreadStatic] private static bool _executingSkillToggleSync;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null) return;

            TryRegisterSyncMethods();

            try { ApplyTargetReadBookSyncPatch(harmony); }
            catch (Exception e) { Log.Warning($"[MP-MeowOnlineShop] Axolotl cultivation target-read sync patch failed: {e.Message}"); }

            try { ApplyReadBookRandPatch(harmony); }
            catch (Exception e) { Log.Warning($"[MP-MeowOnlineShop] Axolotl cultivation Rand patch failed: {e.Message}"); }

            try { ApplyBookListOrderPatch(harmony); }
            catch (Exception e) { Log.Warning($"[MP-MeowOnlineShop] Axolotl cultivation list-order patch failed: {e.Message}"); }

            try { ApplyCompTickGuardPatch(harmony); }
            catch (Exception e) { Log.Warning($"[MP-MeowOnlineShop] Axolotl cultivation tick guard patch failed: {e.Message}"); }
        }

        private static void ApplyCompTickGuardPatch(Harmony harmony)
        {
            Type compType = AccessTools.TypeByName(CompCultivationTypeName);
            Type skillType = AccessTools.TypeByName("Axolotl.MoeLotlQiSkill");
            MethodInfo compTick = compType == null
                ? null
                : AccessTools.Method(compType, "CompTick", Type.EmptyTypes);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_AxolotlCultivationRead),
                nameof(CompTickGuardPrefix));

            _allLearnedSkillsField = AccessTools.Field(compType, "AllLearnedSkills");
            _skillInstallListField = AccessTools.Field(compType, "SkillInstallList");
            _skillDefField = AccessTools.Field(skillType, "def");

            if (compTick == null || prefix == null ||
                _allLearnedSkillsField == null ||
                _skillInstallListField == null || _skillDefField == null)
            {
                return;
            }

            harmony.Patch(
                compTick,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });

            Log.Message(
                "[MP-MeowOnlineShop] Axolotl cultivation tick guard active: " +
                "stale SkillInstallList entries are reconciled before CompTick.");
        }

        private static bool CompTickGuardPrefix(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null ||
                _allLearnedSkillsField == null ||
                _skillInstallListField == null || _skillDefField == null)
            {
                return true;
            }

            try
            {
                if (!(_allLearnedSkillsField.GetValue(__instance) is IList learned) ||
                    !(_skillInstallListField.GetValue(__instance) is IList installs))
                {
                    return true;
                }

                if (learned.Count == 0 || installs.Count == 0)
                    return true;

                var learnedDefs = new HashSet<object>();
                for (int i = 0; i < learned.Count; i++)
                {
                    object skill = learned[i];
                    object def = skill == null
                        ? null
                        : _skillDefField.GetValue(skill);
                    if (def != null)
                        learnedDefs.Add(def);
                }

                for (int i = installs.Count - 1; i >= 0; i--)
                {
                    if (installs[i] == null ||
                        !learnedDefs.Contains(installs[i]))
                    {
                        installs.RemoveAt(i);
                    }
                }
            }
            catch
            {
                // Reconcile is best-effort; vanilla CompTick still runs.
            }

            return true;
        }

        private static void TryRegisterSyncMethods()
        {
            if (_syncMethodRegistered || !MP.enabled || SyncTargetReadBookMethod == null)
                return;

            try
            {
                MP.RegisterSyncMethod(SyncTargetReadBookMethod, null);
                if (SyncToggleInstalledSkillMethod != null)
                    MP.RegisterSyncMethod(SyncToggleInstalledSkillMethod, null);
                _syncMethodRegistered = true;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl cultivation sync method registration failed: {e.Message}");
            }
        }

        private static void ApplyTargetReadBookSyncPatch(Harmony harmony)
        {
            var itabType = AccessTools.TypeByName(ItabTypeName);
            var compType = AccessTools.TypeByName(CompCultivationTypeName);
            var targetField = AccessTools.Field(compType, "TargetReadBook");
            if (itabType == null || targetField == null || ReplaceTargetReadBookAssignmentMethod == null)
                return;

            int patched = 0;
            var methods = EnumerateMethodsForTypeAndNested(itabType)
                .Where(m => (m.Name?.IndexOf("FillTab", StringComparison.Ordinal) ?? -1) >= 0)
                .ToList();

            var transpiler = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(TargetReadBookAssignmentTranspiler));
            if (transpiler == null)
                return;

            foreach (var method in methods)
            {
                try
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                    patched++;
                }
                catch
                {
                }
            }

            if (patched > 0)
                Log.Message($"[MP-MeowOnlineShop] Axolotl cultivation target-read sync patch active: methods={patched}.");
        }

        public static IEnumerable<CodeInstruction> TargetReadBookAssignmentTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var compType = AccessTools.TypeByName(CompCultivationTypeName);
            var targetField = AccessTools.Field(compType, "TargetReadBook");
            if (targetField == null || ReplaceTargetReadBookAssignmentMethod == null || ReplaceInstallSkillMethod == null || ReplaceUninstallSkillMethod == null)
            {
                foreach (var code in instructions) yield return code;
                yield break;
            }

            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Stfld && Equals(code.operand, targetField))
                {
                    yield return new CodeInstruction(OpCodes.Call, ReplaceTargetReadBookAssignmentMethod);
                    continue;
                }

                if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo calledMethod)
                {
                    if (IsInstallSkillCall(calledMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Call, ReplaceInstallSkillMethod);
                        continue;
                    }

                    if (IsUninstallSkillCall(calledMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Call, ReplaceUninstallSkillMethod);
                        continue;
                    }
                }

                yield return code;
            }
        }

        public static void InstallSkillMaybeSync(Pawn pawn, object skillObj)
        {
            ToggleInstalledSkillMaybeSync(pawn, skillObj, true);
        }

        public static void UninstallSkillMaybeSync(Pawn pawn, object skillObj)
        {
            ToggleInstalledSkillMaybeSync(pawn, skillObj, false);
        }

        public static void ToggleInstalledSkillMaybeSync(Pawn pawn, object skillObj, bool enable)
        {
            if (pawn == null || skillObj == null)
                return;

            if (!MP.IsInMultiplayer || _executingSkillToggleSync)
            {
                ApplyToggleInstalledSkill(pawn, skillObj, enable);
                return;
            }

            var map = pawn.Map;
            if (map == null)
            {
                ApplyToggleInstalledSkill(pawn, skillObj, enable);
                return;
            }

            var skillDefName = TryGetSkillDefName(skillObj);
            if (string.IsNullOrEmpty(skillDefName))
            {
                ApplyToggleInstalledSkill(pawn, skillObj, enable);
                return;
            }

            TraceSkillToggle("send", pawn, skillDefName, enable, map?.Index ?? -1, "syncOnly");
            SyncToggleInstalledSkill(map.Index, pawn.thingIDNumber, skillDefName, enable);
        }

        public static void SyncToggleInstalledSkill(int mapIndex, int pawnThingId, string skillDefName, bool enable)
        {
            if (string.IsNullOrEmpty(skillDefName))
                return;

            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            var pawn = map.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
            if (pawn == null)
                return;

            var skillObj = TryResolveSkillObject(pawn, skillDefName);
            if (skillObj == null)
                return;

            try
            {
                _executingSkillToggleSync = true;
                TraceSkillToggle("replay-before", pawn, skillDefName, enable, mapIndex, null);
                ApplyToggleInstalledSkill(pawn, skillObj, enable);
                TraceSkillToggle("replay-after", pawn, skillDefName, enable, mapIndex, null);
            }
            finally
            {
                _executingSkillToggleSync = false;
            }
        }

        private static void ApplyToggleInstalledSkill(Pawn pawn, object skillObj, bool enable)
        {
            if (pawn == null || skillObj == null)
                return;

            var skillDef = TryGetSkillDef(skillObj);
            if (skillDef == null)
                return;

            bool installed = IsSkillInstalled(pawn, skillDef);
            if (enable)
            {
                if (installed)
                    return;
                EnsureSkillMethodsResolved();
                _installSkillMethod?.Invoke(null, new[] { pawn, skillObj });
            }
            else
            {
                if (!installed)
                    return;
                EnsureSkillMethodsResolved();
                _uninstallSkillMethod?.Invoke(null, new[] { pawn, skillObj });
            }
        }

        private static bool IsInstallSkillCall(MethodInfo method)
        {
            return IsSkillUtilityCall(method, "Action_InstallSkill");
        }

        private static bool IsUninstallSkillCall(MethodInfo method)
        {
            return IsSkillUtilityCall(method, "Action_UninstallSkill");
        }

        private static bool IsSkillUtilityCall(MethodInfo method, string methodName)
        {
            if (method == null)
                return false;
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                return false;
            if (!string.Equals(method.DeclaringType?.FullName, MoeLotlQiSkillUtilityTypeName, StringComparison.Ordinal))
                return false;

            var parameters = method.GetParameters();
            return parameters.Length == 2 && parameters[0].ParameterType == typeof(Pawn);
        }

        private static void EnsureSkillMethodsResolved()
        {
            if (_installSkillMethod != null && _uninstallSkillMethod != null)
                return;

            var utilityType = AccessTools.TypeByName(MoeLotlQiSkillUtilityTypeName);
            if (utilityType == null)
                return;

            if (_installSkillMethod == null)
                _installSkillMethod = utilityType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => IsSkillUtilityCall(m, "Action_InstallSkill"));

            if (_uninstallSkillMethod == null)
                _uninstallSkillMethod = utilityType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => IsSkillUtilityCall(m, "Action_UninstallSkill"));
        }

        private static bool IsSkillInstalled(Pawn pawn, object skillDefObj)
        {
            var comp = TryGetCompByTypeName(pawn, CompCultivationTypeName);
            if (comp == null)
                return false;

            var installListObj = AccessTools.Field(comp.GetType(), "SkillInstallList")?.GetValue(comp) as System.Collections.IEnumerable;
            if (installListObj == null)
                return false;

            foreach (var installedDef in installListObj)
            {
                if (ReferenceEquals(installedDef, skillDefObj))
                    return true;
                if (string.Equals(TryGetDefName(installedDef), TryGetDefName(skillDefObj), StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static object TryResolveSkillObject(Pawn pawn, string skillDefName)
        {
            var comp = TryGetCompByTypeName(pawn, CompCultivationTypeName);
            if (comp == null)
                return null;

            var allLearnedSkills = AccessTools.Field(comp.GetType(), "AllLearnedSkills")?.GetValue(comp) as System.Collections.IEnumerable;
            if (allLearnedSkills == null)
                return null;

            foreach (var skillObj in allLearnedSkills)
            {
                var defObj = TryGetSkillDef(skillObj);
                if (string.Equals(TryGetDefName(defObj), skillDefName, StringComparison.Ordinal))
                    return skillObj;
            }

            return null;
        }

        private static string TryGetSkillDefName(object skillObj)
        {
            return TryGetDefName(TryGetSkillDef(skillObj));
        }

        private static object TryGetSkillDef(object skillObj)
        {
            if (skillObj == null)
                return null;

            var type = skillObj.GetType();
            return AccessTools.Field(type, "def")?.GetValue(skillObj)
                   ?? AccessTools.Property(type, "def")?.GetValue(skillObj, null);
        }

        private static string TryGetDefName(object defObj)
        {
            if (defObj == null)
                return null;

            var type = defObj.GetType();
            return AccessTools.Property(type, "defName")?.GetValue(defObj, null) as string
                   ?? AccessTools.Field(type, "defName")?.GetValue(defObj) as string;
        }

        private static void TraceSkillToggle(string stage, Pawn pawn, string skillDefName, bool enable, int mapIndex, string detail)
        {
            if (!ModDebug.EnableAxolotlCultivationToggleTrace || _toggleTraceCount >= MaxToggleTraceCount)
                return;
            _toggleTraceCount++;

            string state = BuildCultivationStateSummary(pawn);
            int tick = Find.TickManager?.TicksGame ?? -1;
            Log.Message("[MP-MeowOnlineShop] Axolotl cultivation toggle " +
                        $"stage={stage} tick={tick} map={mapIndex} pawnId={pawn?.thingIDNumber ?? 0} " +
                        $"pawn={pawn?.LabelShort ?? "null"} skill={skillDefName ?? "null"} enable={enable} " +
                        $"detail={detail ?? "none"} state={state}");
        }

        private static string BuildCultivationStateSummary(Pawn pawn)
        {
            if (pawn == null)
                return "pawn=null";

            var comp = TryGetCompByTypeName(pawn, CompCultivationTypeName);
            if (comp == null)
                return "compCultivation=null";

            var compType = comp.GetType();
            int installCount = CountEnumerable(AccessTools.Field(compType, "SkillInstallList")?.GetValue(comp) as System.Collections.IEnumerable);
            int learnedCount = CountEnumerable(AccessTools.Field(compType, "AllLearnedSkills")?.GetValue(comp) as System.Collections.IEnumerable);
            int progressCount = CountEnumerable(AccessTools.Field(compType, "KV_SkillLearningProgress")?.GetValue(comp) as System.Collections.IEnumerable);

            float lotlQiGain = ReadFloatProperty(comp, "LotlQiGainOffsets");
            float lotlQiMax = ReadFloatProperty(comp, "LotlQiMaxOffsets");
            float shieldMax = ReadFloatProperty(comp, "LotlQiShieldMaxOffsets");
            float shieldReset = ReadFloatProperty(comp, "LotlQiShieldResetFactors");

            return $"install={installCount},learned={learnedCount},progress={progressCount}," +
                   $"qiGain={lotlQiGain:F3},qiMax={lotlQiMax:F3},shieldMax={shieldMax:F3},shieldReset={shieldReset:F3}";
        }

        private static int CountEnumerable(System.Collections.IEnumerable enumerable)
        {
            if (enumerable == null)
                return 0;

            int count = 0;
            foreach (var _ in enumerable)
                count++;
            return count;
        }

        private static float ReadFloatProperty(object obj, string propertyName)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName))
                return 0f;

            try
            {
                var prop = AccessTools.Property(obj.GetType(), propertyName);
                if (prop == null)
                    return 0f;

                var value = prop.GetValue(obj, null);
                if (value is float f)
                    return f;
                if (value is double d)
                    return (float)d;
                if (value is int i)
                    return i;
                return 0f;
            }
            catch
            {
                return 0f;
            }
        }

        public static void SetTargetReadBookMaybeSync(object compObj, ThingDef targetReadBookDef)
        {
            if (compObj == null)
                return;

            if (!MP.IsInMultiplayer || _executingTargetReadSync)
            {
                ApplyTargetReadBookAndInterrupt(compObj, targetReadBookDef);
                return;
            }

            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map))
            {
                ApplyTargetReadBookAndInterrupt(compObj, targetReadBookDef);
                return;
            }

            // 不再在 UI 回调里先行执行本地副作用：ApplyTargetReadBookAndInterrupt 里的
            // EndCurrentJob 会触发换工作（ThinkNode_PrioritySorter）并消费 map/world
            // 随机，若只在发起端执行一次必然分叉（Desync-526 的 host/local trace 正是
            // 一端在命令里消费随机、另一端在地图 tick 里消费随机）。改为只发送同步命令，
            // 由命令回放在所有端（含发起端）统一执行一次。
            SyncSetTargetReadBook(map.Index, pawn.thingIDNumber, targetReadBookDef?.defName);
        }

        public static void SyncSetTargetReadBook(int mapIndex, int pawnThingId, string targetReadBookDefName)
        {
            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            var pawn = map.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
            if (pawn == null)
                return;

            var comp = TryGetCompByTypeName(pawn, CompCultivationTypeName);
            if (comp == null)
                return;

            ThingDef targetDef = null;
            if (!string.IsNullOrEmpty(targetReadBookDefName))
                targetDef = DefDatabase<ThingDef>.GetNamedSilentFail(targetReadBookDefName);

            try
            {
                _executingTargetReadSync = true;
                ApplyTargetReadBookAndInterrupt(comp, targetDef);
            }
            finally
            {
                _executingTargetReadSync = false;
            }
        }

        private static bool TryResolvePawnAndMap(object compObj, out Pawn pawn, out Map map)
        {
            pawn = null;
            map = null;

            var compType = compObj.GetType();
            var getPawnProperty = AccessTools.Property(compType, "GetPawn");
            pawn = getPawnProperty?.GetValue(compObj, null) as Pawn;
            map = pawn?.Map;
            return pawn != null && map != null;
        }

        private static object TryGetCompByTypeName(Pawn pawn, string compTypeName)
        {
            if (pawn?.AllComps == null || string.IsNullOrEmpty(compTypeName))
                return null;

            var compType = AccessTools.TypeByName(compTypeName);
            return pawn.AllComps.FirstOrDefault(c =>
                c != null && (compType == null
                    ? string.Equals(c.GetType().FullName, compTypeName, StringComparison.Ordinal)
                    : compType.IsInstanceOfType(c)));
        }

        private static void ApplyTargetReadBookAndInterrupt(object compObj, ThingDef targetReadBookDef)
        {
            var compType = compObj.GetType();
            var field = AccessTools.Field(compType, "TargetReadBook");
            if (field == null)
                return;

            field.SetValue(compObj, targetReadBookDef);

            var pawn = AccessTools.Property(compType, "GetPawn")?.GetValue(compObj, null) as Pawn;
            if (pawn?.jobs == null)
                return;

            try
            {
                var jobDef = pawn.CurJobDef;
                if (jobDef != null && string.Equals(jobDef.defName, "Axolotl_ReadMoeLotlQiSkillBooks", StringComparison.Ordinal))
                {
                    // EndCurrentJob -> TryFindAndStartJob -> DetermineNextJob 会经
                    // ThinkNode_PrioritySorter 消费 map/world 随机（Desync-526 首个分叉
                    // trace 正是 SyncSetTargetReadBook 命令里的 Rand.Range）。在同步命令
                    // 内该消费必须确定化：包进确定性种子作用域，不触碰共享随机流。
                    int state = 0;
                    Map mapForPop = null;
                    bool scoped = false;
                    if (MP.IsInMultiplayer)
                    {
                        int seed = BuildTargetReadSeed(pawn, targetReadBookDef);
                        scoped = DeterministicRandScope.Begin(
                            pawn.Map,
                            seed,
                            TargetReadSeedWorldOffset,
                            ref state,
                            out mapForPop,
                            ignoreGate: true);
                    }

                    try
                    {
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
                    }
                    finally
                    {
                        if (scoped)
                            DeterministicRandScope.End(state, mapForPop);
                    }
                }
            }
            catch
            {
            }
        }

        private static void ApplyReadBookRandPatch(Harmony harmony)
        {
            var jobDriverType = AccessTools.TypeByName(TargetJobDriverTypeName);
            if (jobDriverType == null || RandRangeIntMethod == null || DeterministicRangeMethod == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Axolotl cultivation patch skipped: target type or Rand methods missing.");
                return;
            }

            var prefix = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(ReadRandScopePrefix));
            var finalizer = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(ReadRandScopeFinalizer));
            var transpiler = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(ReadRandReplaceTranspiler));
            if (prefix == null || finalizer == null || transpiler == null)
                return;

            int patched = 0;
            var targetMethods = EnumerateMethodsForTypeAndNested(jobDriverType)
                .Where(m => (m.Name?.IndexOf("ReadBook", StringComparison.Ordinal) ?? -1) >= 0)
                .ToList();

            foreach (var method in targetMethods)
            {
                try
                {
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(prefix),
                        transpiler: new HarmonyMethod(transpiler),
                        finalizer: new HarmonyMethod(finalizer));
                    patched++;
                }
                catch
                {
                    // 某些编译器生成方法可能不可补丁，忽略并继续。
                }
            }

            Log.Message($"[MP-MeowOnlineShop] Axolotl cultivation Rand patch active: candidateMethods={patched}.");
        }

        private static IEnumerable<MethodBase> EnumerateMethodsForTypeAndNested(Type type)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var method in type.GetMethods(flags))
            {
                if (method == null || method.IsAbstract) continue;
                if (method.GetMethodBody() == null) continue;
                yield return method;
            }

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var method in nested.GetMethods(flags))
                {
                    if (method == null || method.IsAbstract) continue;
                    if (method.GetMethodBody() == null) continue;
                    yield return method;
                }
            }
        }

        public static void ReadRandScopePrefix(object __instance, MethodBase __originalMethod, ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            var driver = TryResolveTargetDriver(__instance);
            if (driver == null)
                return;

            if (_scopeDepth == 0)
            {
                _scopeSeed = BuildDeterministicSeed(driver, __originalMethod);
                _scopeCounter = 0;
            }

            _scopeDepth++;
            __state = 1;
        }

        public static Exception ReadRandScopeFinalizer(Exception __exception, int __state)
        {
            if (__state == 0)
                return __exception;

            if (_scopeDepth > 0)
                _scopeDepth--;

            if (_scopeDepth == 0)
            {
                _scopeSeed = 0;
                _scopeCounter = 0;
            }

            return __exception;
        }

        public static IEnumerable<CodeInstruction> ReadRandReplaceTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var code in instructions)
            {
                if (code.Calls(RandRangeIntMethod))
                    yield return new CodeInstruction(OpCodes.Call, DeterministicRangeMethod);
                else
                    yield return code;
            }
        }

        private static int BuildTargetReadSeed(Pawn pawn, ThingDef targetReadBookDef)
        {
            int seed = TargetReadSeedOffset;
            var map = pawn?.Map;
            seed = Gen.HashCombineInt(seed, map?.Index ?? -1);
            seed = Gen.HashCombineInt(seed, pawn?.thingIDNumber ?? 0);
            seed = Gen.HashCombineInt(seed, DeterministicStringHash(targetReadBookDef?.defName));
            return seed;
        }

        public static int DeterministicReadRotationRange(int minInclusive, int maxExclusive)
        {
            if (!MP.IsInMultiplayer || _scopeDepth <= 0)
                return Rand.Range(minInclusive, maxExclusive);

            int width = maxExclusive - minInclusive;
            if (width <= 0)
                return minInclusive;

            int hash = Gen.HashCombineInt(_scopeSeed, _scopeCounter++);
            if (hash == int.MinValue) hash = 0;
            int offset = Math.Abs(hash) % width;
            return minInclusive + offset;
        }

        private static JobDriver TryResolveTargetDriver(object instance)
        {
            if (instance is JobDriver directDriver && IsTargetDriverType(directDriver.GetType()))
                return directDriver;

            var type = instance.GetType();
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (!typeof(JobDriver).IsAssignableFrom(field.FieldType))
                    continue;
                var value = field.GetValue(instance) as JobDriver;
                if (value != null && IsTargetDriverType(value.GetType()))
                    return value;
            }

            return null;
        }

        private static bool IsTargetDriverType(Type type)
        {
            return type != null && string.Equals(type.FullName, TargetJobDriverTypeName, StringComparison.Ordinal);
        }

        private static int BuildDeterministicSeed(JobDriver driver, MethodBase originalMethod)
        {
            int seed = SeedOffsetReadRotation;
            var pawn = driver.pawn;
            var map = pawn?.Map;
            var bookThing = driver.job?.GetTarget(TargetIndex.A).Thing;

            seed = Gen.HashCombineInt(seed, map?.Index ?? -1);
            seed = Gen.HashCombineInt(seed, pawn?.thingIDNumber ?? 0);
            seed = Gen.HashCombineInt(seed, bookThing?.thingIDNumber ?? 0);
            seed = Gen.HashCombineInt(seed, DeterministicStringHash(bookThing?.def?.defName));
            seed = Gen.HashCombineInt(seed, DeterministicStringHash(originalMethod?.DeclaringType?.FullName));
            seed = Gen.HashCombineInt(seed, DeterministicStringHash(originalMethod?.Name));
            return seed;
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            int hash = 0;
            for (int i = 0; i < value.Length; i++)
                hash = Gen.HashCombineInt(hash, value[i]);
            return hash;
        }

        private static void ApplyBookListOrderPatch(Harmony harmony)
        {
            var workGiverType = AccessTools.TypeByName("Axolotl.WorkGiver_ReadMoeLotlQiSkillBook");
            if (workGiverType == null)
                return;

            var method = AccessTools.Method(workGiverType, "GetAllMoeLotlQiSkillBooksOnMap", new[] { typeof(Map) });
            var postfix = AccessTools.Method(typeof(Patch_AxolotlCultivationRead), nameof(SortBookListPostfix));
            if (method == null || postfix == null)
                return;

            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Message("[MP-MeowOnlineShop] Axolotl cultivation book list order patch active.");
        }

        public static void SortBookListPostfix(ref IEnumerable<Thing> __result)
        {
            if (!MP.IsInMultiplayer || __result == null)
                return;

            __result = new List<Thing>(__result)
                .FindAll(t => t != null)
                .OrderBy(t => t.def?.defName ?? string.Empty)
                .ThenBy(t => t.thingIDNumber)
                .ToList();
        }
    }
}
