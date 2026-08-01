using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;
using UnityRandom = UnityEngine.Random;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for VoiceroidAsAnimal (hatena.VoiceroidAsAnimal / workshop 2073559411).
    /// Root cause (Kotodama): Command_KotodamaSystem FloatMenu calls
    /// <c>CompVAAVoiceroidSkill.SetKotodamaInt</c> only on the clicking client → world/global stat bookkeeping diverges.
    /// Fix: register <c>SetKotodamaInt(int)</c> as sync method and route local calls through <see cref="ISyncMethod.DoSync"/>.
    /// Tame/train: register plausible instance sync methods on non-<see cref="JobDriver"/> types; wrap
    /// <c>VoiceroidAsAnimal.JobDriver_VAATame</c> / <c>JobDriver_VAATrain</c> declared methods (incl. <c>MakeNewToils</c>)
    /// with deterministic <see cref="Rand"/> push/pop in MP;
    /// normalize <c>VAA_Tame</c>/<c>VAA_Train</c> ordered jobs before replication.
    /// Secondary: <c>VAA_WorldManager.MechBandwidthCountCheck</c> / <c>ShieldCountCheck</c> use <c>GenCollection.RandomElement</c>
    /// while trimming pawns → non-deterministic choice → desync; replace with deterministic ordering via transpiler.
    /// VoiceroidPasses / CoeFont letters: wrap incident <c>TryExecuteWorker</c> in <see cref="Rand.PushState"/> for aligned draws;
    /// register sync on VAA <see cref="ChoiceLetter"/> accept-style callbacks discovered by reflection.
    /// Verb skills: register <c>Verb.OrderForceTarget(LocalTargetInfo)</c> on VAA verb types (same idea as <see cref="Patch_MiliraFly"/>).
    /// Extended: reflect-register skill <see cref="ThingComp"/> / 九尾系 <see cref="Verb"/> sync methods; <see cref="JobDriver.DriverTick"/> Rand for
    /// VAA UseSkill / UseNineTail / UseZunko / UseVerbSkill; extra Rand on skill JobDriver methods; NineTail Verb Try/Cast/Shoot paths;
    /// all VAA <see cref="IncidentWorker.TryExecuteWorker"/> Rand; <see cref="VAA_WorldManager"/> RandomElement&lt;Pawn&gt; on Check/Count/Trim/Band methods.
    /// </summary>
    internal static class Patch_VoiceroidAsAnimal
    {
        private const int MaxInfoLogs = 8;
        private static int _infoLogCount;
        private static ISyncMethod _syncSetKotodamaInt;
        private static JobDef _cachedUseVerbOnThingStatic;
        private static JobDef _cachedVaaTame;
        private static JobDef _cachedVaaTrain;

        private const int MaxTameInfoLogs = 10;
        private static int _tameInfoLogCount;

        private const int MaxSkillInfoLogs = 12;
        private static int _skillInfoLogCount;
        private static bool _loggedPatchSummary;

        private static int _diagSyncExplicitRegistered;
        private static int _diagSyncReflectRegistered;
        private static int _diagJobDriverRandPatches;
        private static int _diagVerbRandPatches;
        private static int _diagIncidentRandPatches;
        private static int _diagWorldManagerTranspilerPatches;
        private static int _diagHediffRandPatches;
        private static int _diagThinkNodeRandPatches;

        /// <summary>Current method being transpiled (for one-shot diagnostics).</summary>
        private static string _transpilerTargetLabel;

        private const string VoiceroidPassesIncidentDefName = "VoiceroidPasses";
        private const string VoiceroidPassesWorkerTypeName = "VoiceroidAsAnimal.IncidentWorker_KotonohaSistersPasses";
        private const string VaaIncidentModExtensionTypeName = "VoiceroidAsAnimal.VAAIncidentDefModExtension";
        private static ISyncMethod _syncTriggerVoiceroidPassesByMap;
        private static Type _cachedVoiceroidPassesWorkerType;
        private static Type _cachedVaaIncidentModExtensionType;
        private static MethodInfo _cachedDefGetModExtensionGeneric;
        private static Type _cachedMultiplayerClientType;
        private static PropertyInfo _cachedMultiplayerShouldSyncProperty;
        private static readonly HashSet<string> _vaaPassDiagOnceKeys = new HashSet<string>();
        private static readonly Dictionary<Def, List<PawnKindDef>> _vaaPassPawnKindsBaseline = new Dictionary<Def, List<PawnKindDef>>();
        private const int StateVaaPassUnityRand = 1 << 8;
        [ThreadStatic]
        private static Map _vaaPassCanFireMapForPop;
        [ThreadStatic]
        private static Map _vaaPassExecuteMapForPop;
        [ThreadStatic]
        private static int? _vaaPassForcedExecuteSeed;
        [ThreadStatic]
        private static UnityEngine.Random.State _vaaPassUnityRandomSavedState;
        [ThreadStatic]
        private static bool _vaaPassUnityRandomCaptured;

        private static readonly HashSet<string> _loggedNoRandomReplacement = new HashSet<string>();
        private static readonly HashSet<string> _tameDiagOnceKeys = new HashSet<string>();

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            try
            {
                // 驯服/训练：不依赖 Comp，优先应用，避免缺组件时整段补丁被跳过。
                RegisterVaaTameAndTrainSyncMethods();
                TryPatchVaaJobDriverDriverTickRand(harmony);
                TryPatchVaaJobDriverRand(harmony);
                TryPatchVaaAbilitySkillJobDriverExtraRand(harmony);
                RegisterVaaExplicitSkillSyncMethods();

                var compType = AccessTools.TypeByName("VoiceroidAsAnimal.CompVAAVoiceroidSkill");
                if (compType != null)
                {
                    var setKotodamaMethod = AccessTools.Method(compType, "SetKotodamaInt", new[] { typeof(int) });
                    if (setKotodamaMethod != null)
                    {
                        _syncSetKotodamaInt = MP.RegisterSyncMethod(setKotodamaMethod, null);
                        _diagSyncExplicitRegistered++;
                        Info("Registered SyncMethod CompVAAVoiceroidSkill.SetKotodamaInt(int) via MethodInfo.");
                    }
                    else
                    {
                        _syncSetKotodamaInt = MP.RegisterSyncMethod(compType, "SetKotodamaInt");
                        _diagSyncExplicitRegistered++;
                        Info("Registered SyncMethod CompVAAVoiceroidSkill.SetKotodamaInt (fallback by name).");
                    }

                    if (setKotodamaMethod != null)
                    {
                        var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(SetKotodamaInt_Prefix));
                        harmony.Patch(setKotodamaMethod, prefix: new HarmonyMethod(prefix) { priority = 1 });
                    }
                }
                else
                {
                    Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: CompVAAVoiceroidSkill not found, Kotodama sync skipped.");
                }

                // 注意：CompGetGizmosExtra 为迭代器（yield），Harmony Prefix/Finalizer 对 __state 不可靠，Rand 稳定由 Patch_WorldRandStabilizer 等区域补丁承担。

                RegisterVaaVerbOrderForceTargetSyncMethods();
                RegisterVaaSkillAbilitySyncMethods();
                RegisterVaaChoiceLetterSyncMethods();
                TryPatchTryTakeOrderedJobRepair(harmony);
                TryPatchVoiceroidPassesIncidentRand(harmony);
                TryPatchVaaAbilityVerbRand(harmony);
                TryPatchVaaHediffCompRand(harmony);
                TryPatchVaaNineTailThinkNodeRand(harmony);

                PatchVaaWorldManagerRandomElementMethods(harmony);

                LogPatchSummary();
                Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal multiplayer patches applied.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP patch failed: {e}");
            }
        }

        private static void Info(string msg)
        {
            if (_infoLogCount >= MaxInfoLogs)
                return;
            _infoLogCount++;
            Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: " + msg);
        }

        private static void TameInfoLog(string msg)
        {
            if (_tameInfoLogCount >= MaxTameInfoLogs)
                return;
            _tameInfoLogCount++;
            Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP (tame): " + msg);
        }

        private static void TameDiagOnce(string key, string msg)
        {
            if (!ModDebug.EnableVoiceroidTameTrace)
                return;
            if (!_tameDiagOnceKeys.Add(key))
                return;
            Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP (tame diag): " + msg);
        }

        private static bool VaaVerboseTraceEnabled =>
            ModDebug.EnableVoiceroidTameTrace || ModDebug.EnableQuestIdeologyTrace;

        private static void LogPatchSummary()
        {
            if (_loggedPatchSummary)
                return;
            _loggedPatchSummary = true;
            Log.Message(
                "[MP-MeowOnlineShop] VoiceroidAsAnimal MP summary: " +
                $"syncExplicit={_diagSyncExplicitRegistered}, syncReflect={_diagSyncReflectRegistered}, " +
                $"jobRand={_diagJobDriverRandPatches}, verbRand={_diagVerbRandPatches}, " +
                $"hediffRand={_diagHediffRandPatches}, thinkNodeRand={_diagThinkNodeRandPatches}, " +
                $"incidentRand={_diagIncidentRandPatches}, worldManagerTranspiler={_diagWorldManagerTranspilerPatches}.");
        }

        /// <summary>
        /// 反射注册：名称含 Tame/Train/Teach 的实例方法（排除 <see cref="JobDriver"/>），签名需可被 MP 序列化。
        /// </summary>
        private static void RegisterVaaTameAndTrainSyncMethods()
        {
            var asm = GetVoiceroidAsAnimalAssembly();
            if (asm == null)
            {
                Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: assembly not loaded, tame/train SyncMethod scan skipped.");
                return;
            }

            const int maxRegister = 48;
            var registered = 0;
            var seenMethods = new HashSet<string>(StringComparer.Ordinal);

            foreach (var t in GetTypesSafe(asm))
            {
                if (t == null || t.IsAbstract || t.IsInterface)
                    continue;
                var ns = t.Namespace ?? "";
                if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.Ordinal) < 0)
                    continue;
                if (typeof(JobDriver).IsAssignableFrom(t))
                    continue;

                var typeInteresting = NameHasTameTrainOrTeach(t.Name);
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (registered >= maxRegister)
                        break;
                    if (m.IsSpecialName)
                        continue;
                    if (m.Name == "MakeNewToils" || m.Name == "CreateNewToils")
                        continue;

                    if (!typeInteresting
                        && m.Name.IndexOf("Tame", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Train", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Teach", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (!IsPlausibleSyncMethodSignature(m))
                        continue;

                    var key = m.MetadataToken + "@" + t.AssemblyQualifiedName;
                    if (!seenMethods.Add(key))
                        continue;

                    try
                    {
                        MP.RegisterSyncMethod(m, null);
                        registered++;
                        _diagSyncReflectRegistered++;
                        TameInfoLog($"RegisterSyncMethod {t.Name}.{m.Name}({ParamTypesShort(m)})");
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: RegisterSyncMethod {t.FullName}.{m.Name} failed: {e.Message}");
                    }
                }
            }

            if (registered == 0)
                TameDiagOnce("no_tame_sync", "No Tame/Train/Teach SyncMethod candidates registered (names/signatures may differ).");
        }

        /// <summary>
        /// 显式优先注册：九尾/分身/技能组件的常见入口；反射扫描仍作为兜底。
        /// </summary>
        private static void RegisterVaaExplicitSkillSyncMethods()
        {
            var explicitTypeNames = new[]
            {
                "VoiceroidAsAnimal.CompVAANineTail",
                "VoiceroidAsAnimal.CompVAAItakoSkill",
                "VoiceroidAsAnimal.CompVAAVoiceroidSkill",
                "VoiceroidAsAnimal.Verb_NineTailShoot",
                "VoiceroidAsAnimal.Verb_NineTailAttackDamage",
                "VoiceroidAsAnimal.Verb_MeleeNineTailCurse",
                "VoiceroidAsAnimal.Verb_VAAShoot",
                "VoiceroidAsAnimal.Verb_VAAMeikars"
            };

            var preferredNames = new[]
            {
                "AllowNineTail", "ReleaseNineTailPower", "SummonBunshin", "UseNineTailSkill",
                "UseSkill", "Use", "Activate", "Trigger", "Confirm", "Select", "Set",
                "TryCast", "TryStart", "TryUse", "Toggle", "Switch", "Transform", "Curse"
            };

            var registered = 0;
            foreach (var typeName in explicitTypeNames)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null || t.IsAbstract || t.IsInterface || typeof(JobDriver).IsAssignableFrom(t))
                    continue;

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName || m.Name == "OrderForceTarget" || m.Name == "SetKotodamaInt")
                        continue;
                    if (!IsPlausibleSyncMethodSignature(m))
                        continue;
                    if (!preferredNames.Any(k => m.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    try
                    {
                        MP.RegisterSyncMethod(m, null);
                        registered++;
                        _diagSyncExplicitRegistered++;
                        if (VaaVerboseTraceEnabled)
                            SkillInfoLog($"RegisterSyncMethod (explicit) {t.Name}.{m.Name}({ParamTypesShort(m)})");
                    }
                    catch (Exception e)
                    {
                        if (VaaVerboseTraceEnabled)
                            Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: explicit skill sync {t.FullName}.{m.Name} failed: {e.Message}");
                    }
                }
            }

            if (registered == 0 && VaaVerboseTraceEnabled)
                SkillInfoLog("No explicit skill SyncMethod matched (fallback scan still enabled).");
        }

        private static void SkillInfoLog(string msg)
        {
            if (_skillInfoLogCount >= MaxSkillInfoLogs)
                return;
            _skillInfoLogCount++;
            Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP (skill): " + msg);
        }

        /// <summary>
        /// 技能/能力：<see cref="ThingComp"/> 与 <see cref="Verb"/> 上玩家可能仅在本地调用的入口，注册为 SyncMethod。
        /// <see cref="JobDriver"/> 不在此注册（由模拟 tick + Rand 包裹处理）。
        /// </summary>
        private static void RegisterVaaSkillAbilitySyncMethods()
        {
            var asm = GetVoiceroidAsAnimalAssembly();
            if (asm == null)
            {
                Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: assembly not loaded, skill SyncMethod scan skipped.");
                return;
            }

            const int maxRegister = 96;
            var registered = 0;
            var seenMethods = new HashSet<string>(StringComparer.Ordinal);

            foreach (var t in GetTypesSafe(asm))
            {
                if (t == null || t.IsAbstract || t.IsInterface)
                    continue;
                var ns = t.Namespace ?? "";
                if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.Ordinal) < 0)
                    continue;
                if (typeof(JobDriver).IsAssignableFrom(t))
                    continue;

                if (!TypeIsSkillAbilitySyncTarget(t))
                    continue;

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (registered >= maxRegister)
                        break;
                    if (m.IsSpecialName)
                        continue;
                    if (m.Name == "MakeNewToils" || m.Name == "CreateNewToils")
                        continue;
                    if (m.Name == "OrderForceTarget")
                        continue;
                    if (m.Name == "SetKotodamaInt")
                        continue;

                    if (!NameIsSkillAbilitySyncMethodName(m.Name))
                        continue;
                    if (!IsPlausibleSyncMethodSignature(m))
                        continue;

                    var key = m.MetadataToken + "@" + t.AssemblyQualifiedName;
                    if (!seenMethods.Add(key))
                        continue;

                    try
                    {
                        MP.RegisterSyncMethod(m, null);
                        registered++;
                        _diagSyncReflectRegistered++;
                        SkillInfoLog($"RegisterSyncMethod {t.Name}.{m.Name}({ParamTypesShort(m)})");
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: RegisterSyncMethod skill {t.FullName}.{m.Name} failed: {e.Message}");
                    }
                }
            }

            if (registered == 0)
                Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: no skill/ability SyncMethod candidates registered (optional).");
        }

        /// <summary>技能 Comp（名称含 Skill/VAA…）或 VAA <see cref="Verb"/>。</summary>
        private static bool TypeIsSkillAbilitySyncTarget(Type t)
        {
            if (typeof(ThingComp).IsAssignableFrom(t))
            {
                var name = t.Name ?? "";
                if (name.IndexOf("Skill", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (name.IndexOf("CompVAA", StringComparison.Ordinal) >= 0 && name.IndexOf("Voiceroid", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (name.IndexOf("NineTail", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Bunshin", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                return false;
            }

            if (typeof(Verb).IsAssignableFrom(t) && !t.IsAbstract)
            {
                var name = t.Name ?? "";
                if (name.IndexOf("Verb_", StringComparison.Ordinal) < 0)
                    return false;
                return name.IndexOf("Skill", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Nine", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Tail", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Tuina", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Meikar", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("BeatFire", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Zunko", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Bunshin", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Curse", StringComparison.OrdinalIgnoreCase) >= 0
                       || name.IndexOf("Fox", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return false;
        }

        private static bool NameIsSkillAbilitySyncMethodName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal))
                return false;

            if (name.IndexOf("Order", StringComparison.OrdinalIgnoreCase) >= 0
                && name.IndexOf("OrderForceTarget", StringComparison.Ordinal) < 0)
                return true;
            if (name.IndexOf("TryCast", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Cast", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Broadcast", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
            if (name.IndexOf("Activate", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("UseSkill", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.StartsWith("Use", StringComparison.Ordinal) && name.Length > 3)
                return true;
            if (name.StartsWith("Set", StringComparison.Ordinal) && name.Length > 3)
                return true;
            if (name.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Select", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Summon", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Release", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Transform", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Curse", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static string ParamTypesShort(MethodInfo m)
        {
            return string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name));
        }

        private static bool NameHasTameTrainOrTeach(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.IndexOf("Tame", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Teach", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static bool IsPlausibleSyncMethodSignature(MethodInfo m)
        {
            if (m.IsStatic)
                return false;
            if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool))
                return false;
            if (m.GetParameters().Length > 4)
                return false;
            foreach (var p in m.GetParameters())
            {
                if (p.ParameterType.IsByRef)
                    return false;
                if (!IsMpSyncSafeParameterType(p.ParameterType))
                    return false;
            }

            return true;
        }

        private static bool IsMpSyncSafeParameterType(Type pt)
        {
            if (pt == typeof(int) || pt == typeof(float) || pt == typeof(bool) || pt == typeof(byte) || pt == typeof(long) || pt == typeof(string))
                return true;
            if (pt.IsEnum)
                return true;
            if (typeof(Thing).IsAssignableFrom(pt))
                return true;
            if (typeof(ThingDef).IsAssignableFrom(pt))
                return true;
            if (typeof(Def).IsAssignableFrom(pt) && !pt.IsAbstract)
                return true;
            if (pt == typeof(LocalTargetInfo) || pt == typeof(TargetInfo) || pt == typeof(IntVec3))
                return true;
            if (pt == typeof(Job))
                return true;
            if (pt.FullName == "Verse.GlobalTargetInfo")
                return true;
            return false;
        }

        private static IEnumerable<Type> GetTypesSafe(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(x => x != null);
            }
        }

        /// <summary>
        /// 每 tick 包裹 VAA 驯服/训练 <see cref="JobDriver"/> 的 Rand，避免交互/结算随机序列在联机分叉。
        /// （不对 <c>MakeNewToils</c> 迭代器包裹：Prefix/Finalizer 会在枚举器创建后即 Pop，无法覆盖 Toil 执行。）
        /// </summary>
        private static void TryPatchVaaJobDriverDriverTickRand(Harmony harmony)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(typeof(JobDriver), "DriverTick")
                        ?? AccessTools.Method(typeof(JobDriver), "DriverTick", Type.EmptyTypes);
                if (m == null)
                {
                    TameDiagOnce("no_driver_tick", "JobDriver.DriverTick() not found, VAA per-tick Rand wrap skipped.");
                    return;
                }

                var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VaaJobDriverDriverTickRand_Prefix));
                var finalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VaaJobDriverDriverTickRand_Finalizer));
                harmony.Patch(m,
                    prefix: new HarmonyMethod(prefix) { priority = -50 },
                    finalizer: new HarmonyMethod(finalizer) { priority = 50 });
                _diagJobDriverRandPatches++;
                TameInfoLog("Patched JobDriver.DriverTick (conditional Rand for VAA tame/train/ability).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: JobDriver.DriverTick Rand patch failed: {e.Message}");
            }
        }

        private static void VaaJobDriverDriverTickRand_Prefix(JobDriver __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;
            if (!IsVaaJobDriverNeedingPerTickRand(__instance))
                return;

            Rand.PushState(JobDriverRandSeed(__instance));
            __state = true;
        }

        private static Exception VaaJobDriverDriverTickRand_Finalizer(ref bool __state, Exception __exception)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        /// <summary>驯服/训练与 VAA 主动技能 JobDriver：每 tick 统一 Rand 序列。</summary>
        private static bool IsVaaJobDriverNeedingPerTickRand(JobDriver jd)
        {
            if (jd == null)
                return false;
            var n = jd.GetType().FullName ?? "";
            if (n == "VoiceroidAsAnimal.JobDriver_VAATame" || n == "VoiceroidAsAnimal.JobDriver_VAATrain")
                return true;
            return IsVaaSkillAbilityJobDriverTypeName(n);
        }

        /// <summary>
        /// VAA 技能类 JobDriver（UseSkill / 九尾 / ずん子 / VerbSkill 等）。
        /// </summary>
        private static bool IsVaaSkillAbilityJobDriverTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return false;
            if (fullName.IndexOf("VoiceroidAsAnimal.JobDriver_", StringComparison.Ordinal) < 0)
                return false;
            if (fullName.IndexOf("UseSkill", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName.IndexOf("UseNineTail", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName.IndexOf("UseZunko", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName == "VoiceroidAsAnimal.JobDriver_UseVerbSkill")
                return true;
            if (fullName.IndexOf("UseVerb", StringComparison.OrdinalIgnoreCase) >= 0 && fullName.IndexOf("Skill", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName.IndexOf("VAAControlMech", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName.IndexOf("VAALesson", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName.IndexOf("Bunshin", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName.IndexOf("NineTail", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fullName.IndexOf("Itako", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>
        /// 对 VAA 驯服/训练 JobDriver 中带 Tame/Train/Interact 的声明方法额外包裹 Rand（不含 <c>MakeNewToils</c>）。
        /// </summary>
        private static void TryPatchVaaJobDriverRand(Harmony harmony)
        {
            foreach (var typeName in new[] { "VoiceroidAsAnimal.JobDriver_VAATame", "VoiceroidAsAnimal.JobDriver_VAATrain" })
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null)
                {
                    TameDiagOnce("missing_" + typeName, $"Type not found: {typeName}");
                    continue;
                }

                if (!typeof(JobDriver).IsAssignableFrom(t))
                    continue;

                var candidates = new HashSet<MethodInfo>();
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == "MakeNewToils")
                        continue;

                    if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool))
                        continue;

                    if (m.Name.IndexOf("Tame", StringComparison.OrdinalIgnoreCase) >= 0
                        || m.Name.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0
                        || m.Name.IndexOf("Interact", StringComparison.OrdinalIgnoreCase) >= 0)
                        candidates.Add(m);
                }

                if (candidates.Count == 0)
                {
                    TameDiagOnce("no_extra_candidates_" + typeName, $"No extra Rand-wrap method names for {typeName} (DriverTick wrap still applies).");
                    continue;
                }

                var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(JobDriverRandWrap_Prefix));
                var finalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(JobDriverRandWrap_Finalizer));
                foreach (var m in candidates)
                {
                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(prefix) { priority = -100 },
                            finalizer: new HarmonyMethod(finalizer) { priority = 100 });
                        _diagJobDriverRandPatches++;
                        TameInfoLog($"Rand wrap (extra): {t.Name}.{m.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: Rand wrap {t.Name}.{m.Name} failed: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 对 VAA 技能类 <see cref="JobDriver"/> 中带 Skill/Cast/Interact/Verb 等声明方法额外包裹 Rand（不含 <c>MakeNewToils</c>）。
        /// </summary>
        private static void TryPatchVaaAbilitySkillJobDriverExtraRand(Harmony harmony)
        {
            var asm = GetVoiceroidAsAnimalAssembly();
            if (asm == null)
                return;

            var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(JobDriverRandWrap_Prefix));
            var finalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(JobDriverRandWrap_Finalizer));

            foreach (var t in GetTypesSafe(asm))
            {
                if (t == null || t.IsAbstract || !typeof(JobDriver).IsAssignableFrom(t))
                    continue;
                if (!IsVaaSkillAbilityJobDriverTypeName(t.FullName ?? ""))
                    continue;

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == "MakeNewToils" || m.Name == "CreateNewToils")
                        continue;
                    if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool))
                        continue;

                    if (m.Name.IndexOf("Skill", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Cast", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Interact", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Verb", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Use", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Tail", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Zunko", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(prefix) { priority = -100 },
                            finalizer: new HarmonyMethod(finalizer) { priority = 100 });
                        _diagJobDriverRandPatches++;
                        TameInfoLog($"Rand wrap (ability JobDriver): {t.Name}.{m.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: Rand wrap ability {t.Name}.{m.Name} failed: {ex.Message}");
                    }
                }
            }
        }

        private static void JobDriverRandWrap_Prefix(JobDriver __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;

            Rand.PushState(JobDriverRandSeed(__instance));
            __state = true;
        }

        private static Exception JobDriverRandWrap_Finalizer(ref bool __state, Exception __exception)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        /// <summary>
        /// 对类型全名做跨进程稳定哈希（与 <see cref="Patch_WorldRandStabilizer"/> 一致）。
        /// 勿用 <c>string.GetHashCode()</c>：在 .NET 中不保证跨主机/客户端一致，会导致联机 Rand.PushState 种子分叉。
        /// </summary>
        private static int DeterministicTypeNameHash(Type type)
        {
            if (type == null) return 0;
            var name = type.FullName ?? type.Name ?? "";
            var hash = 0;
            foreach (var c in name)
                hash = Gen.HashCombineInt(hash, c);
            return hash;
        }

        private static int JobDriverRandSeed(JobDriver jd)
        {
            // 优先 pawn/job/def/类型等存档稳定量；命令回放时 TicksGame 可能与主机不一致，故仅在非回放路径混入 tick。
            int seed = 0x20735594;
            var pawn = jd?.pawn;
            if (pawn != null)
                seed = Gen.HashCombineInt(seed, pawn.thingIDNumber);
            var job = jd?.job;
            if (job != null)
            {
                if (job.targetA.IsValid && job.targetA.HasThing)
                    seed = Gen.HashCombineInt(seed, job.targetA.Thing.thingIDNumber);
                if (job.def != null)
                    seed = Gen.HashCombineInt(seed, job.def.shortHash);
            }

            seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(jd?.GetType()));
            if (!MP.IsExecutingSyncCommand)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (tick >= 0)
                    seed = Gen.HashCombineInt(seed, tick);
            }

            return seed;
        }

        /// <summary>Local UI calls → sync host/clients; replay runs with <see cref="MP.IsExecutingSyncCommand"/>.</summary>
        private static bool SetKotodamaInt_Prefix(ThingComp __instance, int num)
        {
            if (!MP.IsInMultiplayer)
                return true;
            if (MP.IsExecutingSyncCommand)
                return true;
            if (_syncSetKotodamaInt == null)
                return true;

            try
            {
                _syncSetKotodamaInt.DoSync(__instance, num);
                return false;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: SetKotodamaInt DoSync failed, falling back to local execution: {e.Message}");
                return true;
            }
        }

        /// <summary>
        /// 事件信件「是/Yes」等常在仅点击端执行；对 VoiceroidAsAnimal 命名空间下 <see cref="ChoiceLetter"/> 子类中无参 void 且名称含 Accept/Confirm 等的方法注册同步。
        /// </summary>
        private static void RegisterVaaChoiceLetterSyncMethods()
        {
            var asm = GetVoiceroidAsAnimalAssembly();
            if (asm == null)
            {
                Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: assembly VoiceroidAsAnimal not loaded, letter SyncMethod registration skipped.");
                return;
            }

            var letterBase = typeof(ChoiceLetter);
            var registered = 0;
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(x => x != null).ToArray();
            }

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || !letterBase.IsAssignableFrom(t))
                    continue;
                var ns = t.Namespace ?? "";
                if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.Ordinal) < 0)
                    continue;

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.ReturnType != typeof(void) || m.GetParameters().Length != 0)
                        continue;
                    if (m.IsSpecialName)
                        continue;
                    var n = m.Name;
                    if (n.IndexOf("Accept", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Execute", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Chosen", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Yes", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Ok", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Decline", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Refuse", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Reject", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Dismiss", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("No", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Proceed", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Complete", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Respond", StringComparison.OrdinalIgnoreCase) < 0
                        && string.Compare(n, "No", StringComparison.OrdinalIgnoreCase) != 0)
                        continue;

                    try
                    {
                        MP.RegisterSyncMethod(m, null);
                        registered++;
                        _diagSyncReflectRegistered++;
                        Info($"Registered SyncMethod {t.Name}.{m.Name}() for ChoiceLetter.");
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: RegisterSyncMethod {t.FullName}.{m.Name} failed: {e.Message}");
                    }
                }
            }

            if (registered == 0)
                Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: no ChoiceLetter accept methods matched for SyncMethod (names may differ).");
        }

        private static Assembly GetVoiceroidAsAnimalAssembly()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null) continue;
                if (string.Equals(asm.GetName().Name, "VoiceroidAsAnimal", StringComparison.OrdinalIgnoreCase))
                    return asm;
            }

            return null;
        }

        /// <summary>
        /// 仅对 VoiceroidPasses 事件打补丁：
        /// 1) 启动时解析并输出目标方法；2) 阻止界面阶段本地直接触发，统一走 Sync；3) CanFire/TryExecute 双点位稳定 Rand。
        /// </summary>
        private static void TryPatchVoiceroidPassesIncidentRand(Harmony harmony)
        {
            try
            {
                _cachedVoiceroidPassesWorkerType = AccessTools.TypeByName(VoiceroidPassesWorkerTypeName);
                if (_cachedVoiceroidPassesWorkerType == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: IncidentWorker_KotonohaSistersPasses not found, VoiceroidPasses patch skipped.");
                    return;
                }

                var canFire = AccessTools.Method(_cachedVoiceroidPassesWorkerType, "CanFireNowSub", new[] { typeof(IncidentParms) });
                var tryExecute = AccessTools.Method(_cachedVoiceroidPassesWorkerType, "TryExecuteWorker", new[] { typeof(IncidentParms) });
                if (canFire == null || tryExecute == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: VoiceroidPasses target methods unresolved (CanFireNowSub/TryExecuteWorker).");
                    return;
                }

                _syncTriggerVoiceroidPassesByMap = MP.RegisterSyncMethod(typeof(Patch_VoiceroidAsAnimal), nameof(SyncTriggerVoiceroidPassesByMap));
                _diagSyncExplicitRegistered++;
                Info("Registered SyncMethod Patch_VoiceroidAsAnimal.SyncTriggerVoiceroidPassesByMap(Map,int).");

                var canFirePrefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VoiceroidPassesCanFire_Prefix));
                var canFireFinalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VoiceroidPassesCanFire_Finalizer));
                harmony.Patch(canFire, prefix: new HarmonyMethod(canFirePrefix), finalizer: new HarmonyMethod(canFireFinalizer));
                _diagIncidentRandPatches++;
                Info("Patched IncidentWorker_KotonohaSistersPasses.CanFireNowSub (Rand scope + pawnKinds reset).");

                var executePrefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VoiceroidPassesTryExecute_Prefix));
                var executeFinalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VoiceroidPassesTryExecute_Finalizer));
                harmony.Patch(tryExecute, prefix: new HarmonyMethod(executePrefix), finalizer: new HarmonyMethod(executeFinalizer));
                _diagIncidentRandPatches++;
                Info("Patched IncidentWorker_KotonohaSistersPasses.TryExecuteWorker (host-authoritative sync + Rand scope).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: TryPatchVoiceroidPassesIncidentRand failed: {e.Message}");
            }
        }

        private static void VoiceroidPassesCanFire_Prefix(IncidentParms parms, IncidentWorker __instance, ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || !IsVoiceroidPassesWorker(__instance))
                return;

            ResetVoiceroidPassesPawnKinds(__instance);
            var map = MapFromIncidentTarget(parms?.target) ?? SingleLoadedMap();
            var seed = IncidentSeedForWorker(parms, __instance, 0x21);
            if (DeterministicRandScope.Begin(map, seed, 0x2A3B, ref __state, out _vaaPassCanFireMapForPop))
                VaaPassDiagOnce("canfire_hit", "VoiceroidPasses CanFireNowSub entered with deterministic triple Rand scope.");
            TryPushVaaPassUnityRandom(seed, ref __state);
        }

        private static Exception VoiceroidPassesCanFire_Finalizer(ref int __state, Exception __exception)
        {
            PopVaaPassUnityRandomIfNeeded(__state);
            DeterministicRandScope.End(__state, _vaaPassCanFireMapForPop);
            _vaaPassCanFireMapForPop = null;
            __state = 0;
            return __exception;
        }

        private static bool VoiceroidPassesTryExecute_Prefix(IncidentParms parms, IncidentWorker __instance, ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || !IsVoiceroidPassesWorker(__instance))
                return true;

            ResetVoiceroidPassesPawnKinds(__instance);
            if (IsMultiplayerShouldSyncNow() && !MP.IsExecutingSyncCommand)
            {
                var map = MapFromIncidentTarget(parms?.target) ?? SingleLoadedMap();
                if (_syncTriggerVoiceroidPassesByMap != null && map != null)
                {
                    _syncTriggerVoiceroidPassesByMap.DoSync(map, IncidentSeedForWorker(parms, __instance, 0x22));
                    VaaPassDiagOnce("redirect_sync", "VoiceroidPasses local trigger redirected to sync command (no client-local execution).");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: VoiceroidPasses local trigger blocked (sync target map unavailable).");
                }

                return false;
            }

            var execSeed = _vaaPassForcedExecuteSeed ?? IncidentSeedForWorker(parms, __instance, 0x22);
            var mapForScope = MapFromIncidentTarget(parms?.target) ?? SingleLoadedMap();
            if (DeterministicRandScope.Begin(mapForScope, execSeed, 0x2A3B, ref __state, out _vaaPassExecuteMapForPop))
                VaaPassDiagOnce("execute_hit", "VoiceroidPasses TryExecuteWorker entered with deterministic triple Rand scope.");
            TryPushVaaPassUnityRandom(execSeed, ref __state);
            return true;
        }

        private static Exception VoiceroidPassesTryExecute_Finalizer(ref int __state, Exception __exception)
        {
            PopVaaPassUnityRandomIfNeeded(__state);
            DeterministicRandScope.End(__state, _vaaPassExecuteMapForPop);
            _vaaPassExecuteMapForPop = null;
            __state = 0;
            return __exception;
        }

        private static void SyncTriggerVoiceroidPassesByMap(Map map, int executeSeed)
        {
            if (map == null)
                return;

            var def = DefDatabase<IncidentDef>.GetNamedSilentFail(VoiceroidPassesIncidentDefName);
            if (def?.Worker == null)
                return;

            var prevSeed = _vaaPassForcedExecuteSeed;
            _vaaPassForcedExecuteSeed = executeSeed;
            try
            {
                var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
                parms.target = map;
                def.Worker.TryExecute(parms);
                VaaPassDiagOnce("sync_execute", "VoiceroidPasses executed via sync command.");
            }
            finally
            {
                _vaaPassForcedExecuteSeed = prevSeed;
            }
        }

        private static Map SingleLoadedMap()
        {
            return Find.Maps != null && Find.Maps.Count == 1
                ? Find.Maps[0]
                : null;
        }

        private static bool IsVoiceroidPassesWorker(IncidentWorker worker)
        {
            if (worker == null)
                return false;
            if (_cachedVoiceroidPassesWorkerType == null)
                _cachedVoiceroidPassesWorkerType = AccessTools.TypeByName(VoiceroidPassesWorkerTypeName);
            if (_cachedVoiceroidPassesWorkerType == null || !_cachedVoiceroidPassesWorkerType.IsInstanceOfType(worker))
                return false;
            return string.Equals(worker.def?.defName, VoiceroidPassesIncidentDefName, StringComparison.Ordinal);
        }

        private static bool IsMultiplayerShouldSyncNow()
        {
            if (!MP.IsInMultiplayer)
                return false;

            try
            {
                if (_cachedMultiplayerShouldSyncProperty == null)
                {
                    _cachedMultiplayerClientType = _cachedMultiplayerClientType ?? AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
                    _cachedMultiplayerShouldSyncProperty = AccessTools.Property(_cachedMultiplayerClientType, "ShouldSync");
                }

                if (_cachedMultiplayerShouldSyncProperty == null)
                    return false;

                var value = _cachedMultiplayerShouldSyncProperty.GetValue(null, null);
                return value is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private static void VaaPassDiagOnce(string key, string msg)
        {
            if (!VaaVerboseTraceEnabled)
                return;
            if (!_vaaPassDiagOnceKeys.Add(key))
                return;
            Log.Message("[MP-MeowOnlineShop] VoiceroidAsAnimal MP (incident): " + msg);
        }

        private static void ResetVoiceroidPassesPawnKinds(IncidentWorker worker)
        {
            try
            {
                var def = worker?.def;
                if (def == null || !string.Equals(def.defName, VoiceroidPassesIncidentDefName, StringComparison.Ordinal))
                    return;

                _cachedVaaIncidentModExtensionType = _cachedVaaIncidentModExtensionType ?? AccessTools.TypeByName(VaaIncidentModExtensionTypeName);
                if (_cachedVaaIncidentModExtensionType == null)
                    return;

                if (_cachedDefGetModExtensionGeneric == null)
                {
                    _cachedDefGetModExtensionGeneric = typeof(Def)
                        .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .FirstOrDefault(m => m.Name == "GetModExtension" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                }
                if (_cachedDefGetModExtensionGeneric == null)
                    return;

                var getExt = _cachedDefGetModExtensionGeneric.MakeGenericMethod(_cachedVaaIncidentModExtensionType);
                var extObj = getExt.Invoke(def, null);
                if (extObj == null)
                    return;

                var field = AccessTools.Field(_cachedVaaIncidentModExtensionType, "pawnKinds");
                if (field == null)
                    return;

                if (!(field.GetValue(extObj) is IList current))
                    return;

                if (!_vaaPassPawnKindsBaseline.TryGetValue(def, out var baseline))
                {
                    baseline = current.Cast<object>().OfType<PawnKindDef>().ToList();
                    _vaaPassPawnKindsBaseline[def] = baseline;
                    VaaPassDiagOnce("cache_baseline", $"Cached VoiceroidPasses pawnKinds baseline: {baseline.Count} entries.");
                }

                field.SetValue(extObj, new List<PawnKindDef>(baseline));
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: reset VoiceroidPasses pawnKinds failed: {e.Message}");
            }
        }

        private static int IncidentSeedForWorker(IncidentParms parms, IncidentWorker worker, int phaseSalt)
        {
            var seed = IncidentSeed(parms, phaseSalt);
            if (worker != null)
                seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(worker.GetType()));
            return seed;
        }

        private static void TryPushVaaPassUnityRandom(int seed, ref int state)
        {
            if ((state & StateVaaPassUnityRand) != 0)
                return;
            try
            {
                _vaaPassUnityRandomSavedState = UnityRandom.state;
                _vaaPassUnityRandomCaptured = true;
                UnityRandom.InitState(seed + 0x4F17);
                state |= StateVaaPassUnityRand;
            }
            catch
            {
                _vaaPassUnityRandomCaptured = false;
            }
        }

        private static void PopVaaPassUnityRandomIfNeeded(int state)
        {
            if ((state & StateVaaPassUnityRand) == 0 || !_vaaPassUnityRandomCaptured)
                return;
            try
            {
                UnityRandom.state = _vaaPassUnityRandomSavedState;
            }
            catch
            {
                // ignore
            }
            finally
            {
                _vaaPassUnityRandomCaptured = false;
            }
        }

        /// <summary>
        /// <see cref="IIncidentTarget"/> 在部分版本无 <c>Map</c> 属性；按常见实现解析为 <see cref="Map"/>。
        /// </summary>
        private static Map MapFromIncidentTarget(IIncidentTarget target)
        {
            if (target == null) return null;
            if (target is Map m) return m;
            if (target is Thing th) return th.MapHeld;
            return null;
        }

        private static int IncidentSeed(IncidentParms parms, int phaseSalt)
        {
            var seed = Find.TickManager.TicksAbs;
            var map = MapFromIncidentTarget(parms?.target);
            if (map != null)
                seed = Gen.HashCombine(seed, map.uniqueID);
            else
            {
                var any = Find.AnyPlayerHomeMap;
                if (any != null)
                    seed = Gen.HashCombine(seed, any.uniqueID);
            }

            //稳定标签，避免与其它事件在同一 Tick/Map 上撞种子
            seed = Gen.HashCombine(seed, Gen.HashCombine(0x20735594, phaseSalt));
            return seed;
        }

        private static void RegisterVaaVerbOrderForceTargetSyncMethods()
        {
            var targetParam = new[] { typeof(LocalTargetInfo) };
            var registeredDeclaringTypes = new HashSet<Type>();

            foreach (var typeName in new[]
                     {
                         "VoiceroidAsAnimal.Verb_VAAMeikars",
                         "VoiceroidAsAnimal.Verb_VAAShoot",
                         "VoiceroidAsAnimal.Verb_VAATuinaFire",
                         "VoiceroidAsAnimal.Verb_VAABeatFire"
                     })
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) continue;
                foreach (var verbType in DiscoverAssignableVerbTypes(t))
                    TryRegisterVerbOrderForceTarget(verbType, targetParam, registeredDeclaringTypes);
            }

            // 米莉亚人像等技能可能继承自其它 Verb 基类：扫描程序集中全部 VAA Verb_* 并注册 OrderForceTarget。
            var asm = GetVoiceroidAsAnimalAssembly();
            if (asm != null)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(x => x != null).ToArray();
                }

                foreach (var vt in types)
                {
                    if (vt == null || vt.IsAbstract || !typeof(Verb).IsAssignableFrom(vt))
                        continue;
                    var ns = vt.Namespace ?? "";
                    if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.Ordinal) < 0)
                        continue;
                    if ((vt.Name ?? "").IndexOf("Verb_", StringComparison.Ordinal) < 0)
                        continue;
                    TryRegisterVerbOrderForceTarget(vt, targetParam, registeredDeclaringTypes);
                }
            }
        }

        private static void TryRegisterVerbOrderForceTarget(Type verbType, Type[] targetParam, HashSet<Type> registeredDeclaringTypes)
        {
            var m = AccessTools.Method(verbType, "OrderForceTarget", targetParam);
            if (m == null) return;
            var decl = m.DeclaringType;
            if (decl == null || !registeredDeclaringTypes.Add(decl)) return;
            try
            {
                MP.RegisterSyncMethod(m, null);
                _diagSyncReflectRegistered++;
                Info($"Registered SyncMethod {decl.FullName}.OrderForceTarget(LocalTargetInfo).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: RegisterSyncMethod {decl.FullName} failed: {e.Message}");
            }
        }

        private static IEnumerable<Type> DiscoverAssignableVerbTypes(Type root)
        {
            if (root == null) yield break;
            var seen = new HashSet<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null) continue;
                string an = asm.GetName().Name ?? "";
                if (an.IndexOf("Voiceroid", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || !typeof(Verb).IsAssignableFrom(t))
                        continue;
                    if (!root.IsAssignableFrom(t))
                        continue;
                    if (seen.Add(t))
                        yield return t;
                }
            }
        }

        /// <summary>
        /// 九尾等技能 Verb：对 Try*/Cast*/Shoot*/Burst* 等声明方法包裹 Rand，避免战斗随机序列分叉。
        /// </summary>
        private static void TryPatchVaaAbilityVerbRand(Harmony harmony)
        {
            var asm = GetVoiceroidAsAnimalAssembly();
            if (asm == null)
                return;

            var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VerbRandWrap_Prefix));
            var finalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(VerbRandWrap_Finalizer));

            foreach (var t in GetTypesSafe(asm))
            {
                if (t == null || t.IsAbstract || !typeof(Verb).IsAssignableFrom(t))
                    continue;
                var ns = t.Namespace ?? "";
                if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.Ordinal) < 0)
                    continue;
                var tn = t.Name ?? "";
                if (tn.IndexOf("NineTail", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Verb_Nine", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Bunshin", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Curse", StringComparison.OrdinalIgnoreCase) < 0
                    && tn.IndexOf("Fox", StringComparison.OrdinalIgnoreCase) < 0
                    && string.Compare(tn, "Verb_NineTailShoot", StringComparison.Ordinal) != 0)
                    continue;

                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (m.IsSpecialName)
                        continue;
                    if (m.Name.StartsWith("get_", StringComparison.Ordinal) || m.Name.StartsWith("set_", StringComparison.Ordinal))
                        continue;
                    if (m.GetParameters().Length > 8)
                        continue;

                    if (m.Name.IndexOf("Try", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Cast", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Burst", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Hit", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Warm", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Use", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Impact", StringComparison.OrdinalIgnoreCase) < 0
                        && m.Name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(prefix) { priority = -80 },
                            finalizer: new HarmonyMethod(finalizer) { priority = 80 });
                        _diagVerbRandPatches++;
                        SkillInfoLog($"Rand wrap (NineTail Verb): {t.Name}.{m.Name}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: Rand wrap verb {t.Name}.{m.Name} failed: {ex.Message}");
                    }
                }
            }
        }

        private static void VerbRandWrap_Prefix(Verb __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;

            Rand.PushState(VerbRandSeed(__instance));
            __state = true;
        }

        private static Exception VerbRandWrap_Finalizer(ref bool __state, Exception __exception)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        private static int VerbRandSeed(Verb verb)
        {
            var seed = 0x20735596;
            if (verb == null)
                return seed;

            try
            {
                var pawn = verb.CasterPawn;
                if (pawn != null)
                    seed = Gen.HashCombineInt(seed, pawn.thingIDNumber);
            }
            catch
            {
                // ignore
            }

            try
            {
                if (verb.EquipmentSource != null)
                    seed = Gen.HashCombineInt(seed, verb.EquipmentSource.thingIDNumber);
            }
            catch
            {
                // ignore
            }

            seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(verb.GetType()));
            if (!MP.IsExecutingSyncCommand)
            {
                var tick = Find.TickManager?.TicksGame ?? 0;
                if (tick >= 0)
                    seed = Gen.HashCombineInt(seed, tick);
            }

            return seed;
        }

        /// <summary>
        /// 九尾相关 HediffComp 常在 Tick 中叠加伤害/状态，统一 Rand 序列以避免跨端分叉。
        /// </summary>
        private static void TryPatchVaaHediffCompRand(Harmony harmony)
        {
            var asm = GetVoiceroidAsAnimalAssembly();
            if (asm == null)
                return;

            var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(HediffCompRandWrap_Prefix));
            var finalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(HediffCompRandWrap_Finalizer));
            if (prefix == null || finalizer == null)
                return;

            foreach (var t in GetTypesSafe(asm))
            {
                if (t == null || t.IsAbstract || !typeof(HediffComp).IsAssignableFrom(t))
                    continue;
                var ns = t.Namespace ?? "";
                if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.Ordinal) < 0)
                    continue;

                foreach (var name in new[] { "CompPostTick", "CompPostTickRare", "CompPostPostRemoved" })
                {
                    var m = AccessTools.Method(t, name, Type.EmptyTypes);
                    if (m == null)
                        continue;
                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(prefix) { priority = -80 },
                            finalizer: new HarmonyMethod(finalizer) { priority = 80 });
                        _diagHediffRandPatches++;
                        if (VaaVerboseTraceEnabled)
                            SkillInfoLog($"Rand wrap (HediffComp): {t.Name}.{name}");
                    }
                    catch (Exception e)
                    {
                        if (VaaVerboseTraceEnabled)
                            Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: Rand wrap HediffComp {t.Name}.{name} failed: {e.Message}");
                    }
                }
            }
        }

        private static void HediffCompRandWrap_Prefix(HediffComp __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;
            Rand.PushState(HediffCompRandSeed(__instance));
            __state = true;
        }

        private static Exception HediffCompRandWrap_Finalizer(ref bool __state, Exception __exception)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        private static int HediffCompRandSeed(HediffComp comp)
        {
            var seed = 0x207355A1;
            var hediff = comp?.parent as HediffWithComps;
            if (hediff?.pawn != null)
                seed = Gen.HashCombineInt(seed, hediff.pawn.thingIDNumber);
            if (hediff?.def != null)
                seed = Gen.HashCombineInt(seed, hediff.def.shortHash);
            seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(comp?.GetType()));
            if (!MP.IsExecutingSyncCommand)
            {
                var tick = Find.TickManager?.TicksGame ?? 0;
                if (tick >= 0)
                    seed = Gen.HashCombineInt(seed, tick);
            }

            return seed;
        }

        /// <summary>
        /// NineTail 燃烧反应节点可能驱动随机逃跑 Job；补丁仅限该节点类，避免过度覆盖。
        /// </summary>
        private static void TryPatchVaaNineTailThinkNodeRand(Harmony harmony)
        {
            var t = AccessTools.TypeByName("VoiceroidAsAnimal.ThinkNode_ConditionalBurningByNineTail");
            if (t == null)
                return;

            var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(ThinkNodeRandWrap_Prefix));
            var finalizer = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(ThinkNodeRandWrap_Finalizer));
            if (prefix == null || finalizer == null)
                return;

            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName)
                    continue;
                if (m.Name.IndexOf("Try", StringComparison.OrdinalIgnoreCase) < 0
                    && m.Name.IndexOf("GiveJob", StringComparison.OrdinalIgnoreCase) < 0
                    && m.Name.IndexOf("Issue", StringComparison.OrdinalIgnoreCase) < 0
                    && m.Name.IndexOf("Satisfied", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (m.GetParameters().Length > 3)
                    continue;
                try
                {
                    harmony.Patch(m,
                        prefix: new HarmonyMethod(prefix) { priority = -70 },
                        finalizer: new HarmonyMethod(finalizer) { priority = 70 });
                    _diagThinkNodeRandPatches++;
                    if (VaaVerboseTraceEnabled)
                        SkillInfoLog($"Rand wrap (ThinkNode): {t.Name}.{m.Name}");
                }
                catch (Exception e)
                {
                    if (VaaVerboseTraceEnabled)
                        Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: Rand wrap ThinkNode {t.Name}.{m.Name} failed: {e.Message}");
                }
            }
        }

        private static void ThinkNodeRandWrap_Prefix(object __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;
            var seed = 0x207355A2;
            seed = Gen.HashCombineInt(seed, DeterministicTypeNameHash(__instance?.GetType()));
            if (!MP.IsExecutingSyncCommand)
            {
                var tick = Find.TickManager?.TicksGame ?? 0;
                if (tick >= 0)
                    seed = Gen.HashCombineInt(seed, tick);
            }

            Rand.PushState(seed);
            __state = true;
        }

        private static Exception ThinkNodeRandWrap_Finalizer(ref bool __state, Exception __exception)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        /// <summary>
        /// <see cref="VAA_WorldManager"/> 中除已知方法外，对名称含 Check/Count/Trim/Band 的实例方法做 RandomElement&lt;Pawn&gt; 确定性替换。
        /// </summary>
        private static void PatchVaaWorldManagerRandomElementMethods(Harmony harmony)
        {
            var wmType = AccessTools.TypeByName("VoiceroidAsAnimal.VAA_WorldManager");
            if (wmType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: VAA_WorldManager not found, RandomElement patches skipped.");
                return;
            }

            var patchedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var explicitName in new[] { "MechBandwidthCountCheck", "ShieldCountCheck" })
            {
                if (patchedNames.Add(explicitName))
                    PatchRandomElementPawn(harmony, wmType, explicitName);
            }

            foreach (var m in wmType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.GetParameters().Length > 12)
                    continue;
                var mn = m.Name ?? "";
                if (mn.IndexOf("Check", StringComparison.OrdinalIgnoreCase) < 0
                    && mn.IndexOf("Count", StringComparison.OrdinalIgnoreCase) < 0
                    && mn.IndexOf("Trim", StringComparison.OrdinalIgnoreCase) < 0
                    && mn.IndexOf("Band", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (!patchedNames.Add(mn))
                    continue;

                PatchRandomElementPawn(harmony, wmType, mn);
            }
        }

        private static void TryPatchTryTakeOrderedJobRepair(Harmony harmony)
        {
            try
            {
                var trackerType = typeof(Pawn_JobTracker);
                foreach (var m in trackerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "TryTakeOrderedJob") continue;
                    var parameters = m.GetParameters();
                    if (parameters.Length == 0 || parameters[0].ParameterType != typeof(Job))
                        continue;

                    var prefix = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(TryTakeOrderedJob_Prefix));
                    harmony.Patch(m, prefix: new HarmonyMethod(prefix) { priority = 5 });
                    Info($"Patched {m.DeclaringType?.FullName}.TryTakeOrderedJob for VAA verb/tame repair.");
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: TryTakeOrderedJob repair failed: {e.Message}");
            }
        }

        private static JobDef GetUseVerbOnThingStaticDef()
        {
            if (_cachedUseVerbOnThingStatic != null)
                return _cachedUseVerbOnThingStatic;
            try
            {
                _cachedUseVerbOnThingStatic = JobDefOf.UseVerbOnThingStatic;
            }
            catch
            {
                _cachedUseVerbOnThingStatic = DefDatabase<JobDef>.GetNamedSilentFail("UseVerbOnThingStatic");
            }
            return _cachedUseVerbOnThingStatic;
        }

        private static JobDef GetCachedVaaTameDef()
        {
            if (_cachedVaaTame == null)
                _cachedVaaTame = DefDatabase<JobDef>.GetNamedSilentFail("VAA_Tame");
            return _cachedVaaTame;
        }

        private static JobDef GetCachedVaaTrainDef()
        {
            if (_cachedVaaTrain == null)
                _cachedVaaTrain = DefDatabase<JobDef>.GetNamedSilentFail("VAA_Train");
            return _cachedVaaTrain;
        }

        private static Pawn GetPawnFromJobTracker(Pawn_JobTracker tracker)
        {
            if (tracker == null) return null;
            try
            {
                return AccessTools.Property(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn
                       ?? AccessTools.Field(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn;
            }
            catch { return null; }
        }

        private static void TryTakeOrderedJob_Prefix(Job job, Pawn_JobTracker __instance)
        {
            if (!MP.IsInMultiplayer || job == null)
                return;

            TryRepairVaaTameTrainOrderedJob(job, __instance);

            var def = GetUseVerbOnThingStaticDef();
            if (def == null || job.def != def)
                return;
            if (job.verbToUse != null)
                return;

            var pawn = GetPawnFromJobTracker(__instance);
            if (pawn?.VerbTracker?.AllVerbs == null)
                return;

            var verb = ResolveVaaVerbForUseVerbStatic(pawn, job);
            if (verb == null)
                return;

            job.verbToUse = verb;
            if (_infoLogCount < MaxInfoLogs)
                Info($"Repaired UseVerbOnThingStatic.verbToUse → {verb.GetType().Name} for {pawn.LabelShort}.");
        }

        /// <summary>
        /// 联机复制 <see cref="Job"/> 前规范化 VAA 驯服/训练指令，减少字段默认值分叉。
        /// </summary>
        private static void TryRepairVaaTameTrainOrderedJob(Job job, Pawn_JobTracker tracker)
        {
            var tame = GetCachedVaaTameDef();
            var train = GetCachedVaaTrainDef();
            if (tame == null && train == null)
                return;
            if (job.def != tame && job.def != train)
                return;

            var pawn = GetPawnFromJobTracker(tracker);
            if (pawn == null)
                return;

            var repaired = false;
            if (job.count < 0)
            {
                job.count = 0;
                repaired = true;
            }

            // 部分版本/路径下 billgiver 等引用在序列化后丢失；驯服通常不需要，但若存在空引用字段则清空。
            TryNullOutBrokenBillGiverOnJob(job, ref repaired);

            if (repaired && ModDebug.EnableVoiceroidTameTrace)
                Log.Message($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: repaired ordered job {job.def?.defName} for {pawn.LabelShort}.");

            if (!job.targetA.IsValid && ModDebug.EnableVoiceroidTameTrace)
                TameDiagOnce("invalid_target_" + job.def?.defName, $"Ordered {job.def?.defName} has invalid TargetA for {pawn.LabelShort} (cannot auto-fix).");
        }

        private static void TryNullOutBrokenBillGiverOnJob(Job job, ref bool repaired)
        {
            if (job == null) return;
            try
            {
                var f = AccessTools.Field(typeof(Job), "billGiver");
                if (f == null) return;
                var val = f.GetValue(job);
                if (val is Thing th && !th.Spawned && th.Destroyed)
                {
                    f.SetValue(job, null);
                    repaired = true;
                }
            }
            catch
            {
                // ignore
            }
        }

        private static Verb ResolveVaaVerbForUseVerbStatic(Pawn pawn, Job job)
        {
            var candidates = pawn.VerbTracker.AllVerbs
                .Where(v => v != null && IsVoiceroidAsAnimalVerbType(v.GetType()))
                .ToList();
            if (candidates.Count == 0)
                return null;
            if (candidates.Count == 1)
                return candidates[0];

            candidates.Sort((x, y) => string.Compare(x?.GetUniqueLoadID(), y?.GetUniqueLoadID(), StringComparison.Ordinal));
            return candidates[0];
        }

        private static bool IsVoiceroidAsAnimalVerbType(Type t)
        {
            if (t == null) return false;
            string ns = t.Namespace ?? "";
            if (ns.IndexOf("VoiceroidAsAnimal", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            string name = t.Name ?? "";
            return name.IndexOf("Verb_", StringComparison.Ordinal) >= 0;
        }

        /// <summary>Deterministic replacement for <c>GenCollection.RandomElement&lt;Pawn&gt;</c> in VAA world checks.</summary>
        public static Pawn DeterministicRandomElementPawn(IEnumerable<Pawn> source)
        {
            if (source == null)
                return null;
            var list = source.ToList();
            if (list.Count == 0)
                return null;
            list.Sort((a, b) => (a?.thingIDNumber ?? 0).CompareTo(b?.thingIDNumber ?? 0));
            return list[0];
        }

        /// <summary>Deterministic <c>TryRandomElement&lt;Pawn&gt;</c> for transpiler replacement.</summary>
        public static bool DeterministicTryRandomElementPawn(IEnumerable<Pawn> source, out Pawn value)
        {
            value = DeterministicRandomElementPawn(source);
            return value != null;
        }

        private static Dictionary<MethodInfo, MethodInfo> BuildPawnRandomReplacementMap()
        {
            var map = new Dictionary<MethodInfo, MethodInfo>();
            var mDeterministicElement = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(DeterministicRandomElementPawn));
            var mDeterministicTry = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(DeterministicTryRandomElementPawn));

            if (mDeterministicElement == null || mDeterministicTry == null)
                return map;

            foreach (var m in typeof(GenCollection).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!m.IsGenericMethodDefinition)
                    continue;
                if (m.Name != "RandomElement" && m.Name != "TryRandomElement")
                    continue;

                MethodInfo concrete;
                try
                {
                    concrete = m.MakeGenericMethod(typeof(Pawn));
                }
                catch
                {
                    continue;
                }

                var ps = concrete.GetParameters();
                if (m.Name == "RandomElement" && ps.Length == 1)
                {
                    var p0 = ps[0].ParameterType;
                    if (p0 == typeof(IEnumerable<Pawn>) || typeof(IEnumerable<Pawn>).IsAssignableFrom(p0))
                        map[concrete] = mDeterministicElement;
                }
                else if (m.Name == "TryRandomElement" && ps.Length == 2 && ps[1].ParameterType.IsByRef)
                {
                    var p0 = ps[0].ParameterType;
                    if (p0 == typeof(IEnumerable<Pawn>) || typeof(IEnumerable<Pawn>).IsAssignableFrom(p0))
                        map[concrete] = mDeterministicTry;
                }
            }

            return map;
        }

        private static void PatchRandomElementPawn(Harmony harmony, Type declaringType, string methodName)
        {
            try
            {
                var method = AccessTools.Method(declaringType, methodName);
                if (method == null)
                {
                    Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: {declaringType.Name}.{methodName} not found.");
                    return;
                }

                _transpilerTargetLabel = $"{declaringType.Name}.{methodName}";
                var transpiler = AccessTools.Method(typeof(Patch_VoiceroidAsAnimal), nameof(RandomElementPawn_Transpiler));
                harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                _diagWorldManagerTranspilerPatches++;
                Info($"Patched {declaringType.Name}.{methodName} (deterministic RandomElement/TryRandomElement<Pawn>).");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: transpiler for {declaringType?.Name}.{methodName} failed: {e.Message}");
            }
        }

        private static IEnumerable<CodeInstruction> RandomElementPawn_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var replacementMap = BuildPawnRandomReplacementMap();
            if (replacementMap.Count == 0)
            {
                Log.Warning("[MP-MeowOnlineShop] VoiceroidAsAnimal MP: RandomElement transpiler could not build replacement map.");
                foreach (var instr in instructions)
                    yield return instr;
                yield break;
            }

            var label = _transpilerTargetLabel ?? "Unknown";
            var replaced = 0;
            foreach (var instr in instructions)
            {
                var emit = instr;
                foreach (var kv in replacementMap)
                {
                    if (!instr.Calls(kv.Key))
                        continue;
                    emit = new CodeInstruction(OpCodes.Call, kv.Value);
                    replaced++;
                    break;
                }

                yield return emit;
            }

            if (replaced == 0 && _loggedNoRandomReplacement.Add(label))
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: transpiler for {label} replaced 0 RandomElement/TryRandomElement<Pawn> calls (IL may differ).");
            else if (replaced > 0)
                Log.Message($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP: transpiler {label} replaced {replaced} call(s).");
        }
    }

    /*
     * 联机验证建议（VoiceroidAsAnimal 驯服/训练）：
     * 1) 主机 + 客户端各一人，开启 VoiceroidAsAnimal 与 Multiplayer；可选将 ModDebug.EnableVoiceroidTameTrace 设为 true 查看驯服补丁日志。
     * 2) 生成或等待地图上出现“新增”Voiceroid 动物，对其实施驯服直至成功。
     * 3) 确认双方不断线、无 Rand/desync 提示；成功后检查动物阵营与训练栏一致。
     * 4) 对同一动物执行一次 VAA_Train 相关训练，确认双方状态一致。
     * 5) 回归：Kotodama（言灵）按钮、VAA 技能 Verb 指向、事件信件确认仍可用。
     * 6) 主动技能 Job（VAA_UseSkill / 九尾 / ずん子等）多次施放；九尾远程；其它 Voiceroid 事件；VAA_WorldManager 带宽/护盾等检查。
     */

    internal static class Patch_VoiceroidAsAnimalBootstrap
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Patch_VoiceroidAsAnimal.Apply(harmony);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] VoiceroidAsAnimal MP bootstrap failed: {e.Message}");
            }
        }
    }
}
