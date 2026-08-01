using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for Axolotl weapon mode gizmo switching (CompAxolotlEnergy).
    /// Replays command clicks via sync and wraps Rand deterministically around gizmo/init paths.
    /// </summary>
    internal static class Patch_AxolotlWeaponMode
    {
        internal const string HarmonyId = "mp.meowonlineshop.axolotlweaponmode";

        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;

        private const int SeedOffsetGizmo = 0x73A1;
        private const int SeedOffsetInit = 0x73B7;
        private const int WorldSeedOffset = 0x73D3;

        private static readonly string[] TargetCompTypeNames =
        {
            "Axolotl.CompAxolotlEnergy"
        };

        private static readonly string[] GizmoMethodNames =
        {
            "CompGetGizmosExtra",
            "CompGetWornGizmosExtra",
            "GetGizmos"
        };

        private static readonly string[] InitMethodNames =
        {
            "PostPostMake",
            "Initialize",
            "PostSpawnSetup",
            "Notify_Equipped"
        };

        private static readonly string[] SyncMethodCandidates =
        {
            "AutoCloseLotlQiWeaponMode",
            "SwitchMode",
            "ToggleMode",
            "SetMode",
            "SetWeaponMode",
            "SetIsChangeLotiWeaponMode"
        };

        private static readonly ConditionalWeakTable<Command, CommandMeta> CommandMetaTable = new ConditionalWeakTable<Command, CommandMeta>();
        private static readonly HashSet<MethodBase> PatchedCommandProcessMethods = new HashSet<MethodBase>();
        private static readonly FieldInfo CommandActionField = AccessTools.Field(typeof(Command_Action), "action");
        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();
        private static readonly MethodInfo SetModeMaybeSyncMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(SetModeMaybeSync));
        private static readonly MethodInfo SyncSetWeaponModeMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(SyncSetWeaponMode));
        private static readonly MethodInfo LotlQiGizmoTranspilerMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(LotlQiOverviewGizmoTranspiler), new[] { typeof(IEnumerable<CodeInstruction>) });
        private static readonly MethodInfo MaybeSyncSetAutoSaveCrystalMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(MaybeSyncSetAutoSaveCrystal));
        private static readonly MethodInfo MaybeSyncAddEnergyCrystalMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(MaybeSyncAddEnergyCrystal));
        private static readonly MethodInfo MaybeSyncReduceEnergyCrystalMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(MaybeSyncReduceEnergyCrystal));
        private static readonly MethodInfo MaybeSyncSetAutoResetShieldMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(MaybeSyncSetAutoResetShield));
        private static readonly MethodInfo MaybeSyncShieldResetMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(MaybeSyncShieldReset));
        private static readonly MethodInfo MaybeSyncSetTargetShieldAndAutoResetMethod = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(MaybeSyncSetTargetShieldAndMaybeAutoReset));

        [ThreadStatic] private static Map _mapForRandPop;
        [ThreadStatic] private static bool _executingSyncedCommand;
        [ThreadStatic] private static bool _executingModeSync;
        [ThreadStatic] private static bool _executingLotlQiSync;

        private static bool _syncReplayMethodRegistered;
        private static bool _syncSetModeMethodRegistered;
        private static bool _lotlQiSyncMethodsRegistered;
        private static int _registeredSyncMethods;
        private static int _patchedMethods;

        private sealed class CommandMeta
        {
            public int MapIndex;
            public int ParentThingId;
            public string CompTypeName;
            public string SourceMethodName;
            public int GizmoIndex;
        }

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            if (!_syncReplayMethodRegistered)
            {
                try
                {
                    MP.RegisterSyncMethod(typeof(Patch_AxolotlWeaponMode), nameof(SyncInvokeAxolotlModeCommandByIndex))
                        .SetContext(SyncContext.CurrentMap)
                        .CancelIfAnyArgNull();
                    _syncReplayMethodRegistered = true;
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Axolotl weapon mode patch: failed to register sync replay method: {e.Message}");
                }
            }

            if (!_syncSetModeMethodRegistered && SyncSetWeaponModeMethod != null)
            {
                try
                {
                    MP.RegisterSyncMethod(SyncSetWeaponModeMethod, null);
                    _syncSetModeMethodRegistered = true;
                    _registeredSyncMethods++;
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Axolotl weapon mode patch: failed to register set-mode sync method: {e.Message}");
                }
            }

            RegisterLotlQiSyncMethods();

            var randPrefix = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(RandScopePrefix), new[] { typeof(object), typeof(MethodBase), typeof(int).MakeByRefType() });
            var randFinalizer = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(RandScopeFinalizer), new[] { typeof(int) });
            var modeTranspiler = AccessTools.Method(typeof(Patch_AxolotlWeaponMode), nameof(ModeToggleTranspiler), new[] { typeof(IEnumerable<CodeInstruction>) });

            var compTypes = DiscoverAxolotlCompTypes().ToList();
            foreach (var compType in compTypes)
            {
                RegisterCompSyncMethods(compType);

                foreach (var methodName in GizmoMethodNames)
                    PatchCompMethod(harmony, compType, methodName, randPrefix, randFinalizer, null);

                foreach (var methodName in InitMethodNames)
                    PatchCompMethod(harmony, compType, methodName, randPrefix, randFinalizer, null);

                ApplyModeToggleSyncPatch(harmony, compType, modeTranspiler);
            }

            ApplyLotlQiOverviewSyncPatch(harmony);

            Log.Message($"[MP-MeowOnlineShop] Axolotl weapon mode patch active: compTypes={compTypes.Count}, syncMethods={_registeredSyncMethods}, patchedMethods={_patchedMethods}.");
        }

        private static void RegisterLotlQiSyncMethods()
        {
            if (_lotlQiSyncMethodsRegistered)
                return;

            try
            {
                MP.RegisterSyncMethod(typeof(Patch_AxolotlWeaponMode), nameof(SyncSetAutoSaveCrystal));
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl LotlQi sync register failed: {nameof(SyncSetAutoSaveCrystal)}: {e.Message}");
            }

            try
            {
                MP.RegisterSyncMethod(typeof(Patch_AxolotlWeaponMode), nameof(SyncAddEnergyCrystal));
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl LotlQi sync register failed: {nameof(SyncAddEnergyCrystal)}: {e.Message}");
            }

            try
            {
                MP.RegisterSyncMethod(typeof(Patch_AxolotlWeaponMode), nameof(SyncReduceEnergyCrystal));
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl LotlQi sync register failed: {nameof(SyncReduceEnergyCrystal)}: {e.Message}");
            }

            try
            {
                MP.RegisterSyncMethod(typeof(Patch_AxolotlWeaponMode), nameof(SyncSetAutoResetShield));
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl LotlQi sync register failed: {nameof(SyncSetAutoResetShield)}: {e.Message}");
            }

            try
            {
                MP.RegisterSyncMethod(typeof(Patch_AxolotlWeaponMode), nameof(SyncShieldReset));
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl LotlQi sync register failed: {nameof(SyncShieldReset)}: {e.Message}");
            }

            try
            {
                MP.RegisterSyncMethod(typeof(Patch_AxolotlWeaponMode), nameof(SyncSetTargetShieldAndMaybeAutoReset));
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl LotlQi sync register failed: {nameof(SyncSetTargetShieldAndMaybeAutoReset)}: {e.Message}");
            }

            _lotlQiSyncMethodsRegistered = true;
        }

        private static void ApplyLotlQiOverviewSyncPatch(Harmony harmony)
        {
            if (harmony == null || LotlQiGizmoTranspilerMethod == null)
                return;

            var gizmoType = AccessTools.TypeByName("Axolotl.Gizmo_LotlQiOverView");
            if (gizmoType == null)
                return;

            var gizmoOnGui = AccessTools.Method(gizmoType, "GizmoOnGUI");
            if (gizmoOnGui == null)
                return;

            try
            {
                harmony.Patch(gizmoOnGui, transpiler: new HarmonyMethod(LotlQiGizmoTranspilerMethod));
                _patchedMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl LotlQi patch: failed to transpile {gizmoType.FullName}.GizmoOnGUI: {e.Message}");
            }
        }

        private static void ApplyModeToggleSyncPatch(Harmony harmony, Type compType, MethodInfo modeTranspiler)
        {
            if (harmony == null || compType == null || modeTranspiler == null)
                return;

            foreach (var method in compType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method == null || method.IsAbstract || method.GetMethodBody() == null)
                    continue;
                if ((method.Name?.IndexOf("GetLotlQiModeWeaponGizimos", StringComparison.Ordinal) ?? -1) < 0)
                    continue;

                try
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(modeTranspiler));
                    _patchedMethods++;
                }
                catch
                {
                }
            }
        }

        private static void PatchAxolotlRelatedCommandProcessInput(Harmony harmony, MethodInfo commandPrefix)
        {
            if (harmony == null || commandPrefix == null)
                return;

            var candidateTypes = new List<Type>();
            foreach (var typeName in new[]
                     {
                         "Verse.Command",
                         "Verse.Command_Action",
                         "Verse.Command_Toggle",
                         "Verse.Command_Target",
                         "RimWorld.Command_Target"
                     })
            {
                var t = AccessTools.TypeByName(typeName);
                if (t != null && typeof(Command).IsAssignableFrom(t))
                    candidateTypes.Add(t);
            }

            foreach (var t in candidateTypes)
            {
                var processInput = AccessTools.Method(t, "ProcessInput");
                if (processInput == null || processInput.DeclaringType != t)
                    continue;
                if (PatchedCommandProcessMethods.Contains(processInput))
                    continue;

                try
                {
                    harmony.Patch(processInput, prefix: new HarmonyMethod(commandPrefix));
                    PatchedCommandProcessMethods.Add(processInput);
                    _patchedMethods++;
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static IEnumerable<Type> DiscoverAxolotlCompTypes()
        {
            var set = new HashSet<Type>();

            foreach (var typeName in TargetCompTypeNames)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t != null && typeof(ThingComp).IsAssignableFrom(t))
                    set.Add(t);
            }

            foreach (var t in GenTypes.AllTypes)
            {
                if (t == null || t.IsAbstract || !typeof(ThingComp).IsAssignableFrom(t))
                    continue;

                var fullName = t.FullName ?? t.Name ?? string.Empty;
                if (fullName.IndexOf("CompAxolotlEnergy", StringComparison.OrdinalIgnoreCase) >= 0)
                    set.Add(t);
            }

            return set;
        }

        private static void PatchCompMethod(Harmony harmony, Type compType, string methodName, MethodInfo prefix, MethodInfo finalizer, MethodInfo postfix)
        {
            try
            {
                var method = compType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.Ordinal));
                if (method == null)
                    return;

                var hmPrefix = prefix != null ? new HarmonyMethod(prefix) : null;
                var hmFinalizer = finalizer != null ? new HarmonyMethod(finalizer) : null;
                var hmPostfix = postfix != null ? new HarmonyMethod(postfix) : null;

                harmony.Patch(method, prefix: hmPrefix, postfix: hmPostfix, finalizer: hmFinalizer);
                _patchedMethods++;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl weapon mode patch: failed patch {compType.FullName}.{methodName}: {e.Message}");
            }
        }

        private static void RegisterCompSyncMethods(Type compType)
        {
            var registeredNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var exactName in SyncMethodCandidates)
            {
                var m = compType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(method => string.Equals(method.Name, exactName, StringComparison.Ordinal));
                if (!IsSyncMethodCandidate(m) || !registeredNames.Add(exactName))
                    continue;
                TryRegisterSyncMethod(compType, exactName);
            }

            var heuristicNames = compType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(IsSyncMethodCandidate)
                .Where(m =>
                {
                    var n = (m.Name ?? string.Empty).ToLowerInvariant();
                    return n.Contains("switch") || n.Contains("toggle") || n.Contains("mode") || n.Contains("weaponmode") || n.Contains("loti");
                })
                .GroupBy(m => m.Name)
                .Where(g => g.Count() == 1)
                .Select(g => g.First().Name);

            foreach (var methodName in heuristicNames)
            {
                if (!registeredNames.Add(methodName))
                    continue;
                TryRegisterSyncMethod(compType, methodName);
            }
        }

        private static bool IsSyncMethodCandidate(MethodInfo method)
        {
            if (method == null || method.IsStatic || method.IsAbstract || method.IsSpecialName)
                return false;
            if (method.ReturnType != typeof(void))
                return false;

            var parameters = method.GetParameters();
            if (parameters.Length == 0)
                return true;

            if (parameters.Length == 1)
            {
                var p = parameters[0].ParameterType;
                return p == typeof(bool) || p == typeof(int) || p.IsEnum;
            }

            return false;
        }

        private static void TryRegisterSyncMethod(Type compType, string methodName)
        {
            try
            {
                MP.RegisterSyncMethod(compType, methodName);
                _registeredSyncMethods++;
            }
            catch
            {
            }
        }

        public static IEnumerable<CodeInstruction> ModeToggleTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            if (SetModeMaybeSyncMethod == null)
            {
                foreach (var code in instructions) yield return code;
                yield break;
            }

            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Callvirt && code.operand is MethodInfo mi
                    && string.Equals(mi.Name, "set_IsChangeLotiWeaponMode", StringComparison.Ordinal))
                {
                    yield return new CodeInstruction(OpCodes.Call, SetModeMaybeSyncMethod);
                }
                else
                {
                    yield return code;
                }
            }
        }

        public static void SetModeMaybeSync(object compObj, bool enabled)
        {
            if (compObj == null)
                return;

            if (!MP.IsInMultiplayer || _executingModeSync)
            {
                ApplyMode(compObj, enabled);
                return;
            }

            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map))
            {
                ApplyMode(compObj, enabled);
                return;
            }

            ApplyMode(compObj, enabled);
            SyncSetWeaponMode(map.Index, pawn.thingIDNumber, enabled);
        }

        public static void SyncSetWeaponMode(int mapIndex, int pawnThingId, bool enabled)
        {
            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            var pawn = map.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
            if (pawn == null)
                return;

            var comp = pawn.AllComps?.FirstOrDefault(c => c != null && (c.GetType().FullName?.IndexOf("CompAxolotlEnergy", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (comp == null)
                return;

            try
            {
                _executingModeSync = true;
                ApplyMode(comp, enabled);
            }
            finally
            {
                _executingModeSync = false;
            }
        }

        public static void MaybeSyncSetAutoSaveCrystal(object compObj, bool enabled)
        {
            if (compObj == null)
                return;

            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map) || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _executingLotlQiSync)
            {
                SetBoolProperty(compObj, "IsAutoSaveCrystal", enabled);
                return;
            }

            SyncSetAutoSaveCrystal(map.Index, pawn.thingIDNumber, enabled);
        }

        public static void MaybeSyncAddEnergyCrystal(object compObj, bool isCostEnergy)
        {
            if (compObj == null)
                return;

            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map) || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _executingLotlQiSync)
            {
                InvokeCompMethod(compObj, "Action_AddEnergyCrystal", isCostEnergy);
                return;
            }

            SyncAddEnergyCrystal(map.Index, pawn.thingIDNumber, isCostEnergy);
        }

        public static void MaybeSyncReduceEnergyCrystal(object compObj, bool isAddEnergy)
        {
            if (compObj == null)
                return;

            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map) || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _executingLotlQiSync)
            {
                InvokeCompMethod(compObj, "Action_ReduceEnergyCrystal", isAddEnergy);
                return;
            }

            SyncReduceEnergyCrystal(map.Index, pawn.thingIDNumber, isAddEnergy);
        }

        public static void MaybeSyncSetAutoResetShield(object compObj, bool enabled)
        {
            if (compObj == null)
                return;

            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map) || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _executingLotlQiSync)
            {
                SetBoolProperty(compObj, "IsAutoResetShield", enabled);
                return;
            }

            SyncSetAutoResetShield(map.Index, pawn.thingIDNumber, enabled);
        }

        public static void MaybeSyncShieldReset(object compObj)
        {
            if (compObj == null)
                return;

            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map) || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _executingLotlQiSync)
            {
                InvokeCompMethod(compObj, "Action_ShieldReSet");
                return;
            }

            SyncShieldReset(map.Index, pawn.thingIDNumber);
        }

        public static void MaybeSyncSetTargetShieldAndMaybeAutoReset(object compObj)
        {
            if (compObj == null)
                return;

            float targetShieldCount = GetFloatProperty(compObj, "TargetShieldCount", 0.3f);
            if (!TryResolvePawnAndMap(compObj, out var pawn, out var map) || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand || _executingLotlQiSync)
            {
                InvokeCompMethod(compObj, "AutoResetShield");
                return;
            }

            SyncSetTargetShieldAndMaybeAutoReset(map.Index, pawn.thingIDNumber, targetShieldCount, true);
        }

        public static void SyncSetAutoSaveCrystal(int mapIndex, int pawnThingId, bool enabled)
        {
            ExecuteOnAxolotlEnergyComp(mapIndex, pawnThingId, comp => SetBoolProperty(comp, "IsAutoSaveCrystal", enabled));
        }

        public static void SyncAddEnergyCrystal(int mapIndex, int pawnThingId, bool isCostEnergy)
        {
            ExecuteOnAxolotlEnergyComp(mapIndex, pawnThingId, comp => InvokeCompMethod(comp, "Action_AddEnergyCrystal", isCostEnergy));
        }

        public static void SyncReduceEnergyCrystal(int mapIndex, int pawnThingId, bool isAddEnergy)
        {
            ExecuteOnAxolotlEnergyComp(mapIndex, pawnThingId, comp => InvokeCompMethod(comp, "Action_ReduceEnergyCrystal", isAddEnergy));
        }

        public static void SyncSetAutoResetShield(int mapIndex, int pawnThingId, bool enabled)
        {
            ExecuteOnAxolotlEnergyComp(mapIndex, pawnThingId, comp => SetBoolProperty(comp, "IsAutoResetShield", enabled));
        }

        public static void SyncShieldReset(int mapIndex, int pawnThingId)
        {
            ExecuteOnAxolotlEnergyComp(mapIndex, pawnThingId, comp => InvokeCompMethod(comp, "Action_ShieldReSet"));
        }

        public static void SyncSetTargetShieldAndMaybeAutoReset(int mapIndex, int pawnThingId, float targetShieldCount, bool runAutoReset)
        {
            ExecuteOnAxolotlEnergyComp(mapIndex, pawnThingId, comp =>
            {
                SetFloatProperty(comp, "TargetShieldCount", targetShieldCount);
                if (runAutoReset)
                    InvokeCompMethod(comp, "AutoResetShield");
            });
        }

        private static bool TryResolvePawnAndMap(object compObj, out Pawn pawn, out Map map)
        {
            pawn = null;
            map = null;

            if (compObj is ThingComp tc && tc.parent is Pawn p)
            {
                pawn = p;
                map = p.Map;
                return map != null;
            }

            var getPawn = AccessTools.Property(compObj.GetType(), "GetPawn");
            pawn = getPawn?.GetValue(compObj, null) as Pawn;
            map = pawn?.Map;
            return map != null;
        }

        private static void ApplyMode(object compObj, bool enabled)
        {
            var prop = AccessTools.Property(compObj.GetType(), "IsChangeLotiWeaponMode");
            if (prop != null && prop.CanWrite)
                prop.SetValue(compObj, enabled, null);
        }

        public static IEnumerable<CodeInstruction> LotlQiOverviewGizmoTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var code in instructions)
            {
                if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo mi)
                {
                    if (IsCompMethod(mi, "set_IsAutoSaveCrystal", 1))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = MaybeSyncSetAutoSaveCrystalMethod;
                    }
                    else if (IsCompMethod(mi, "Action_AddEnergyCrystal", 1))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = MaybeSyncAddEnergyCrystalMethod;
                    }
                    else if (IsCompMethod(mi, "Action_ReduceEnergyCrystal", 1))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = MaybeSyncReduceEnergyCrystalMethod;
                    }
                    else if (IsCompMethod(mi, "set_IsAutoResetShield", 1))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = MaybeSyncSetAutoResetShieldMethod;
                    }
                    else if (IsCompMethod(mi, "Action_ShieldReSet", 0))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = MaybeSyncShieldResetMethod;
                    }
                    else if (IsCompMethod(mi, "AutoResetShield", 0))
                    {
                        code.opcode = OpCodes.Call;
                        code.operand = MaybeSyncSetTargetShieldAndAutoResetMethod;
                    }
                }

                yield return code;
            }
        }

        private static bool IsCompMethod(MethodInfo method, string methodName, int paramCount)
        {
            if (method == null || !string.Equals(method.Name, methodName, StringComparison.Ordinal))
                return false;

            var declaring = method.DeclaringType?.FullName ?? string.Empty;
            if (declaring.IndexOf("CompAxolotlEnergy", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            return method.GetParameters().Length == paramCount;
        }

        private static bool CommandProcessInputPrefix(Command __instance)
        {
            if (!MP.IsInMultiplayer || _executingSyncedCommand || __instance == null)
                return true;
            if (!CommandMetaTable.TryGetValue(__instance, out CommandMeta meta) || meta == null)
                return true;

            SyncInvokeAxolotlModeCommandByIndex(meta.MapIndex, meta.ParentThingId, meta.CompTypeName, meta.SourceMethodName, meta.GizmoIndex);
            return false;
        }

        public static void SyncInvokeAxolotlModeCommandByIndex(int mapIndex, int parentThingId, string compTypeName, string sourceMethodName, int gizmoIndex)
        {
            if (string.IsNullOrEmpty(compTypeName) || string.IsNullOrEmpty(sourceMethodName))
                return;

            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            var parent = FindThingById(map, parentThingId) as ThingWithComps;
            if (parent?.AllComps == null)
                return;

            var compType = AccessTools.TypeByName(compTypeName);
            var comp = parent.AllComps.FirstOrDefault(c => c != null && (compType == null ? c.GetType().FullName == compTypeName : compType.IsInstanceOfType(c)));
            if (comp == null)
                return;

            var sourceMethod = AccessTools.Method(comp.GetType(), sourceMethodName);
            if (sourceMethod == null || sourceMethod.GetParameters().Length != 0)
                return;

            IEnumerable<Gizmo> gizmos = null;
            try
            {
                gizmos = sourceMethod.Invoke(comp, null) as IEnumerable<Gizmo>;
            }
            catch
            {
                return;
            }

            if (gizmos == null)
                return;

            var list = gizmos as IList<Gizmo> ?? gizmos.ToList();
            if (gizmoIndex < 0 || gizmoIndex >= list.Count)
                return;
            if (!(list[gizmoIndex] is Command cmd))
                return;

            try
            {
                _executingSyncedCommand = true;
                if (cmd is Command_Action commandAction)
                {
                    var action = CommandActionField?.GetValue(commandAction) as Action;
                    if (action != null)
                    {
                        action.Invoke();
                        return;
                    }
                }

                var processInput = AccessTools.Method(cmd.GetType(), "ProcessInput") ?? AccessTools.Method(typeof(Command), "ProcessInput");
                processInput?.Invoke(cmd, new object[] { null });
            }
            finally
            {
                _executingSyncedCommand = false;
            }
        }

        private static Thing FindThingById(Map map, int thingId)
        {
            if (map?.listerThings?.AllThings == null)
                return null;

            var allThings = map.listerThings.AllThings;
            for (var i = 0; i < allThings.Count; i++)
            {
                var thing = allThings[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;
            }

            return null;
        }

        private static void ExecuteOnAxolotlEnergyComp(int mapIndex, int pawnThingId, Action<object> action)
        {
            if (action == null)
                return;

            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            var pawn = map.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
            var comp = pawn?.AllComps?.FirstOrDefault(c => c != null && (c.GetType().FullName?.IndexOf("CompAxolotlEnergy", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            if (comp == null)
                return;

            try
            {
                _executingLotlQiSync = true;
                action(comp);
            }
            finally
            {
                _executingLotlQiSync = false;
            }
        }

        private static void InvokeCompMethod(object compObj, string methodName, params object[] args)
        {
            if (compObj == null || string.IsNullOrEmpty(methodName))
                return;

            var argTypes = args == null ? Type.EmptyTypes : args.Select(a => a?.GetType() ?? typeof(object)).ToArray();
            var method = AccessTools.Method(compObj.GetType(), methodName, argTypes) ?? AccessTools.Method(compObj.GetType(), methodName);
            if (method == null)
                return;

            method.Invoke(compObj, args);
        }

        private static void SetBoolProperty(object compObj, string propertyName, bool value)
        {
            if (compObj == null || string.IsNullOrEmpty(propertyName))
                return;

            var prop = AccessTools.Property(compObj.GetType(), propertyName);
            if (prop != null && prop.CanWrite)
                prop.SetValue(compObj, value, null);
        }

        private static void SetFloatProperty(object compObj, string propertyName, float value)
        {
            if (compObj == null || string.IsNullOrEmpty(propertyName))
                return;

            var prop = AccessTools.Property(compObj.GetType(), propertyName);
            if (prop != null && prop.CanWrite)
                prop.SetValue(compObj, value, null);
        }

        private static float GetFloatProperty(object compObj, string propertyName, float fallback)
        {
            if (compObj == null || string.IsNullOrEmpty(propertyName))
                return fallback;

            var prop = AccessTools.Property(compObj.GetType(), propertyName);
            if (prop == null || !prop.CanRead)
                return fallback;

            var value = prop.GetValue(compObj, null);
            if (value is float f)
                return f;
            if (value is double d)
                return (float)d;
            if (value is int i)
                return i;
            return fallback;
        }

        private static void CaptureGizmoCommandsPostfix(object __instance, ref IEnumerable<Gizmo> __result, MethodBase __originalMethod)
        {
            if (!MP.IsInMultiplayer || __instance == null || __result == null || __originalMethod == null)
                return;
            if (!(__instance is ThingComp comp) || comp.parent == null || comp.parent.Map == null)
                return;
            if (__originalMethod.GetParameters().Length != 0)
                return;

            var list = __result as IList<Gizmo> ?? __result.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Command command))
                    continue;

                var meta = new CommandMeta
                {
                    MapIndex = comp.parent.Map.Index,
                    ParentThingId = comp.parent.thingIDNumber,
                    CompTypeName = comp.GetType().FullName ?? comp.GetType().Name,
                    SourceMethodName = __originalMethod.Name,
                    GizmoIndex = i
                };

                CommandMetaTable.Remove(command);
                CommandMetaTable.Add(command, meta);
            }

            __result = list;
        }

        private static void RandScopePrefix(object __instance, MethodBase __originalMethod, ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;

            if (!MP.IsInMultiplayer || __instance == null)
                return;
            if (!TryResolveMapAndThingId(__instance, out var map, out var thingId))
                return;

            var typeHash = DeterministicTypeHash(__instance.GetType());
            var methodHash = DeterministicStringHash(__originalMethod?.Name ?? string.Empty);
            var baseSeed = Gen.HashCombineInt(Gen.HashCombineInt(map.Index, thingId), Gen.HashCombineInt(typeHash, methodHash));
            var seedOffset = IsGizmoMethod(__originalMethod) ? SeedOffsetGizmo : SeedOffsetInit;
            var seed = Gen.HashCombineInt(baseSeed, seedOffset);

            Rand.PushState(seed);
            __state |= StateStaticRand;

            if (PushMapRand(map, seed))
            {
                _mapForRandPop = map;
                __state |= StateMapRand;
            }

            if (PushWorldRand(seed + WorldSeedOffset))
                __state |= StateWorldRand;
        }

        private static void RandScopeFinalizer(int __state)
        {
            if (__state == 0)
                return;

            try
            {
                if ((__state & StateWorldRand) != 0)
                    PopWorldRand();
                if ((__state & StateMapRand) != 0 && _mapForRandPop != null)
                {
                    PopMapRand(_mapForRandPop);
                    _mapForRandPop = null;
                }
                if ((__state & StateStaticRand) != 0)
                    Rand.PopState();
            }
            catch
            {
            }
        }

        private static bool IsGizmoMethod(MethodBase method)
        {
            if (method == null)
                return false;
            var name = method.Name ?? string.Empty;
            return name.IndexOf("Gizmo", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryResolveMapAndThingId(object instance, out Map map, out int thingId)
        {
            map = null;
            thingId = 0;

            if (instance is ThingComp comp)
            {
                map = comp.parent?.Map;
                thingId = comp.parent?.thingIDNumber ?? 0;
                return map != null && thingId != 0;
            }

            if (instance is Thing thing)
            {
                map = thing.Map;
                thingId = thing.thingIDNumber;
                return map != null && thingId != 0;
            }

            return false;
        }

        private static int DeterministicTypeHash(Type type)
        {
            if (type == null)
                return 0;
            return DeterministicStringHash(type.FullName ?? type.Name ?? string.Empty);
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            var hash = 0;
            for (var i = 0; i < value.Length; i++)
                hash = Gen.HashCombineInt(hash, value[i]);
            return hash;
        }

        private static Func<object> TryGetWorldRandGetter()
        {
            try
            {
                return () =>
                {
                    var world = Find.World;
                    if (world == null)
                        return null;
                    var t = world.GetType();
                    return AccessTools.Property(t, "Rand")?.GetValue(world)
                           ?? AccessTools.Property(t, "rand")?.GetValue(world)
                           ?? AccessTools.Field(t, "Rand")?.GetValue(world)
                           ?? AccessTools.Field(t, "rand")?.GetValue(world);
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool PushMapRand(Map map, int seed)
        {
            if (map == null)
                return false;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Property(t, "rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null)
                    return false;
                var pushMethod = mapRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null)
                    return false;
                pushMethod.Invoke(mapRand, new object[] { seed });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PopMapRand(Map map)
        {
            if (map == null)
                return;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Property(t, "rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "Rand")?.GetValue(map)
                              ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null)
                    return;
                mapRand.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(mapRand, null);
            }
            catch
            {
            }
        }

        private static bool PushWorldRand(int seed)
        {
            if (WorldRandGetter == null)
                return false;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null)
                    return false;
                var pushMethod = worldRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (pushMethod == null)
                    return false;
                pushMethod.Invoke(worldRand, new object[] { seed });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PopWorldRand()
        {
            if (WorldRandGetter == null)
                return;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null)
                    return;
                worldRand.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(worldRand, null);
            }
            catch
            {
            }
        }
    }
}
