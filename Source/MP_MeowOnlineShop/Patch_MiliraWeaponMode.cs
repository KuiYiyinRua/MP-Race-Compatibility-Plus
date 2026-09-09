using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for every weapon shipped by Milira Race.
    /// Handles weapon-mode command replay, Ancot's custom targeting override,
    /// and the direct-control firing boundary used by Perspective Shift.
    /// </summary>
    internal static class Patch_MiliraWeaponMode
    {
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;

        private const int SeedOffsetCompGizmo = 0x6B11;
        private const int SeedOffsetCompInit = 0x6B19;
        private const int WorldSeedOffset = 0x6C23;
        private const int SustainedFireSeedSalt = 0x53555346; // "SUSF"
        private const int SustainedFireWorldSeedOffset = 0x53555357; // "SUSW"
        private const string SustainedChargeVerbTypeName =
            "AncotLibrary.Verb_ChargeShootSustained";
        private const int MaxPerspectiveFireCooldownEntries = 1024;

        private static readonly Dictionary<long, int> PerspectiveFireCooldownUntil =
            new Dictionary<long, int>();

        private static readonly string[] TargetCompTypeNames =
        {
            "AncotLibrary.CompRangeWeaponVerbSwitch",
            "AncotLibrary.CompRangeWeaponVerbSwitch_EnergyPassive"
        };

        // Concrete, non-projectile weapon ThingDefs in Milira Race 1.6. Keeping
        // this allow-list exact prevents the Perspective Shift executor from
        // changing unrelated Ancot weapons supplied by other mods.
        private static readonly HashSet<string> MiliraWeaponDefNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Milian_BishopScepter",
                "Milian_PivotScepter",
                "Milian_ParticleLongRangeSniper",
                "Milian_KnightHalberd",
                "Milian_KnightSword",
                "Milian_KnightLance",
                "Milian_KnightHammer",
                "Milian_ParticleBeamGun",
                "Milian_PulsedBeamGun",
                "Milian_ParticleBeamBlaster",
                "Milian_RookBlade",
                "Milian_RookBladeII",
                "Milira_SwiftBlade",
                "Milira_Glaive",
                "Milira_SawBlade",
                "Milira_PoleBlade",
                "Milira_Lance",
                "Milira_Sickle",
                "Milira_Hammer",
                "Milira_TwoHandSword",
                "Milira_Spear",
                "Milira_SingleHandblade",
                "Milian_ParticleSMG",
                "Milian_ParticleDiffusionBlaster",
                "Milian_ParticleLMG",
                "Milira_PlasmaPistol",
                "Milira_PlasmaRifle",
                "Milira_PlasmaSMG",
                "Milira_PlasmaCannon",
                "Milira_PlasmaMG",
                "Milira_PlasmaPulseSniperRifle",
                "Milira_MagneticRailRifle",
                "Milira_HandRailGun",
                "Milira_RayPistol",
                "Milira_RayRifle",
                "Milira_ConvergentRaySniper",
                "Milira_LaserMG"
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
            "SwitchMode",
            "ToggleMode",
            "SwitchVerb",
            "ToggleVerb",
            "SetMode",
            "SetVerbMode",
            "TrySwitchMode",
            "DoSwitchMode"
        };

        private static readonly ConditionalWeakTable<Command, CommandMeta> CommandMetaTable = new ConditionalWeakTable<Command, CommandMeta>();
        private static readonly FieldInfo CommandActionField = AccessTools.Field(typeof(Command_Action), "action");
        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();

        [ThreadStatic] private static Map _mapForRandPop;
        [ThreadStatic] private static bool _executingSyncedCommand;
        [ThreadStatic] private static Map _sustainedFireMapForRandPop;

        private static bool _syncMethodRegistered;
        private static readonly HashSet<MethodBase> PatchedCommandProcessMethods = new HashSet<MethodBase>();
        private static bool _missingMethodLogged;
        private static int _registeredSyncMethods;
        private static int _patchedMethods;
        private static int _captureLogCount;
        private static int _syncReplayLogCount;
        private static bool _sustainedTargetSyncRegistered;
        private const int MaxTraceLogs = 8;

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

            if (!OptimizationGate.IsMiliraWeaponModeCompatEnabled)
            {
                OptimizationGate.LogOnce(
                    "milira.weapon.mode.disabled",
                    "[MP-MeowOnlineShop] Milira weapon-mode compat patch is disabled; " +
                    "Milira weapons keep vanilla firing/gizmo behavior.");
                return;
            }

            if (!_syncMethodRegistered)
            {
                try
                {
                    MP.RegisterSyncMethod(typeof(Patch_MiliraWeaponMode), nameof(SyncInvokeModeCommandByIndex))
                        .SetContext(SyncContext.CurrentMap)
                        .CancelIfAnyArgNull();
                    _syncMethodRegistered = true;
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Milira patch: failed to register sync replay method: {e.Message}");
                }
            }

            var randPrefix = AccessTools.Method(typeof(Patch_MiliraWeaponMode), nameof(RandScopePrefix), new[] { typeof(object), typeof(MethodBase), typeof(int).MakeByRefType() });
            var randFinalizer = AccessTools.Method(typeof(Patch_MiliraWeaponMode), nameof(RandScopeFinalizer), new[] { typeof(int) });
            var gizmoPostfix = AccessTools.Method(typeof(Patch_MiliraWeaponMode), nameof(CaptureGizmoCommandsPostfix), new[] { typeof(object), typeof(IEnumerable<Gizmo>).MakeByRefType(), typeof(MethodBase) });
            var commandPrefix = AccessTools.Method(typeof(Patch_MiliraWeaponMode), nameof(CommandProcessInputPrefix), new[] { typeof(Command) });

            foreach (string typeName in TargetCompTypeNames)
            {
                var compType = AccessTools.TypeByName(typeName);
                if (compType == null)
                    continue;

                RegisterCompSyncMethods(compType);

                PatchCompMethod(harmony, compType, "GetWeaponGizmos", randPrefix, randFinalizer, gizmoPostfix);
                PatchCompMethod(harmony, compType, "CompGetWornGizmosExtra", randPrefix, randFinalizer, gizmoPostfix);
                PatchCompMethod(harmony, compType, "CompGetGizmosExtra", randPrefix, randFinalizer, gizmoPostfix);
                PatchCompMethod(harmony, compType, "GetGizmos", randPrefix, randFinalizer, gizmoPostfix);

                foreach (string initMethodName in InitMethodNames)
                    PatchCompMethod(harmony, compType, initMethodName, randPrefix, randFinalizer, null);

                PatchCompMethod(harmony, compType, "Notify_VerbSwitch", randPrefix, randFinalizer, null);
                PatchCompMethod(harmony, compType, "Notify_SwitchPassive", randPrefix, randFinalizer, null);
            }

            RegisterSustainedTargetingOverride();
            PatchSustainedFireRand(harmony);

            if (commandPrefix != null)
            {
                foreach (Type t in GenTypes.AllTypes)
                {
                    if (t == null || t.IsAbstract || !typeof(Command).IsAssignableFrom(t))
                        continue;
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
                    }
                }
            }

            Log.Message(
                $"[MP-MeowOnlineShop] Milira all-weapons patch active: " +
                $"weaponDefs={MiliraWeaponDefNames.Count}, syncMethods={_registeredSyncMethods}, " +
                $"sustainedTargetSync={_sustainedTargetSyncRegistered}, patchedMethods={_patchedMethods}.");
        }

        private static void RegisterSustainedTargetingOverride()
        {
            Type sustainedVerb = AccessTools.TypeByName("AncotLibrary.Verb_ShootSustained");
            MethodInfo orderForceTarget = sustainedVerb == null
                ? null
                : AccessTools.Method(
                    sustainedVerb,
                    nameof(Verb.OrderForceTarget),
                    new[] { typeof(LocalTargetInfo) });

            // Multiplayer registers ITargetingSource overrides only from
            // Assembly-CSharp. Ancot's override stores the forced downed pawn
            // and current target, so it needs its own command boundary.
            if (orderForceTarget == null || orderForceTarget.DeclaringType != sustainedVerb)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira all-weapons patch: " +
                    "Ancot sustained-fire OrderForceTarget target was not resolved.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(sustainedVerb, nameof(Verb.OrderForceTarget))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _sustainedTargetSyncRegistered = true;
                _registeredSyncMethods++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira all-weapons patch: failed to register " +
                    $"Ancot sustained-fire targeting: {e.Message}");
            }
        }

        private static void PatchSustainedFireRand(Harmony harmony)
        {
            Type sustainedVerb = AccessTools.TypeByName(SustainedChargeVerbTypeName);
            if (sustainedVerb == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira sustained-charge verb Rand " +
                    "isolation target not resolved; skipped.");
                return;
            }

            MethodInfo tryCastShot = AccessTools.Method(
                sustainedVerb,
                "TryCastShot",
                Type.EmptyTypes);
            if (tryCastShot == null ||
                tryCastShot.DeclaringType != sustainedVerb)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira sustained-charge TryCastShot " +
                    "target not resolved or inherited; skipped.");
                return;
            }

            harmony.Patch(
                tryCastShot,
                prefix: new HarmonyMethod(
                    typeof(Patch_MiliraWeaponMode),
                    nameof(SustainedFireTryCastShotPrefix))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(
                    typeof(Patch_MiliraWeaponMode),
                    nameof(SustainedFireTryCastShotFinalizer))
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Milira sustained-charge firing Rand " +
                "isolation active.");
        }

        private static void SustainedFireTryCastShotPrefix(
            Verb __instance,
            ref int __state)
        {
            __state = 0;
            _sustainedFireMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Thing caster = __instance.caster;
            if (caster == null || caster.Map == null)
                return;

            int seed = Gen.HashCombineInt(
                SustainedFireSeedSalt,
                caster.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, caster.thingIDNumber);
            seed = Gen.HashCombineInt(seed, Find.TickManager.TicksGame);

            if (DeterministicRandScope.Begin(
                    caster.Map,
                    seed,
                    SustainedFireWorldSeedOffset,
                    ref __state,
                    out var mapForPop,
                    ignoreGate: true))
            {
                _sustainedFireMapForRandPop = mapForPop;
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception SustainedFireTryCastShotFinalizer(
            Exception __exception,
            int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _sustainedFireMapForRandPop);
            _sustainedFireMapForRandPop = null;
            return __exception;
        }

        /// <summary>
        /// Executes Perspective Shift firing for Milira weapons without
        /// re-entering its UI-oriented HandleFiring method. The caller already
        /// runs inside one ordered MP command and one deterministic Rand scope.
        /// </summary>
        internal static bool TryHandlePerspectiveShiftFire(Pawn pawn, IntVec3 cell)
        {
            if (!OptimizationGate.IsMiliraWeaponModeCompatEnabled)
                return false;

            Thing weapon = pawn?.equipment?.Primary;
            if (weapon?.def == null ||
                !MiliraWeaponDefNames.Contains(weapon.def.defName))
            {
                return false;
            }

            if (pawn.Map == null || !pawn.Spawned || pawn.Dead || pawn.Downed ||
                pawn.InMentalState || !cell.InBounds(pawn.Map))
            {
                return true;
            }

            // The original Avatar.HandleFiring blocks every shot while the pawn
            // is in a warmup or cooldown stance; without this gate the ordered
            // fire commands would keep calling TryStartCastOn during cooldown.
            if (pawn.stances?.curStance is Stance_Busy)
                return true;

            Thing targetThing = pawn.Map.thingGrid.ThingsListAt(cell)
                .Where(t => t != null && t != pawn &&
                            (t is Pawn || t.def.category == ThingCategory.Building ||
                             t.def.category == ThingCategory.Item))
                .OrderBy(t => t is Pawn ? 0 : 1)
                .ThenBy(t => t.thingIDNumber)
                .FirstOrDefault();
            LocalTargetInfo target = targetThing != null
                ? new LocalTargetInfo(targetThing)
                : new LocalTargetInfo(cell);

            Vector3 targetPos = targetThing != null
                ? targetThing.DrawPos
                : cell.ToVector3Shifted();
            Vector3 direction = targetPos - pawn.DrawPos;
            if (direction.sqrMagnitude > 0.01f)
            {
                float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                pawn.Rotation = Rot4.FromAngleFlat(angle);
            }

            if (pawn.Position.DistanceTo(cell) <= 1.42f && targetThing != null)
            {
                pawn.meleeVerbs?.TryMeleeAttack(targetThing, null, false);
                return true;
            }

            Verb verb = pawn.equipment.PrimaryEq?.PrimaryVerb;
            if (verb != null && !verb.verbProps.IsMeleeAttack &&
                verb.Available() && verb.CanHitTarget(target))
            {
                int pawnId = pawn.thingIDNumber;
                int weaponId = weapon.thingIDNumber;
                int now = Find.TickManager.TicksGame;
                if (now < GetPerspectiveFireCooldownUntil(pawnId, weaponId))
                    return true;
                if (verb.TryStartCastOn(target, false, true, false, false))
                    SetPerspectiveFireCooldown(
                        pawnId,
                        weaponId,
                        now + verb.verbProps.AdjustedCooldownTicks(verb, pawn));
            }

            return true;
        }

        private static long PerspectiveFireKey(int pawnId, int weaponId)
        {
            return ((long)pawnId << 32) ^ (uint)weaponId;
        }

        private static int GetPerspectiveFireCooldownUntil(int pawnId, int weaponId)
        {
            return PerspectiveFireCooldownUntil.TryGetValue(
                PerspectiveFireKey(pawnId, weaponId),
                out int until)
                ? until
                : 0;
        }

        private static void SetPerspectiveFireCooldown(int pawnId, int weaponId, int untilTick)
        {
            long key = PerspectiveFireKey(pawnId, weaponId);
            PerspectiveFireCooldownUntil[key] = untilTick;
            if (PerspectiveFireCooldownUntil.Count <= MaxPerspectiveFireCooldownEntries)
                return;

            int now = Find.TickManager.TicksGame;
            List<long> stale = null;
            foreach (KeyValuePair<long, int> pair in PerspectiveFireCooldownUntil)
            {
                if (pair.Value <= now)
                {
                    if (stale == null)
                        stale = new List<long>();
                    stale.Add(pair.Key);
                }
            }
            if (stale == null)
                return;
            for (int i = 0; i < stale.Count; i++)
                PerspectiveFireCooldownUntil.Remove(stale[i]);
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
                Log.Warning($"[MP-MeowOnlineShop] Milira patch: failed patch {compType.FullName}.{methodName}: {e.Message}");
            }
        }

        private static void RegisterCompSyncMethods(Type compType)
        {
            var registeredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var exactName in new[] { "Notify_VerbSwitch", "Notify_SwitchPassive", "<GetWeaponGizmos>b__17_0" })
            {
                var m = compType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(method => string.Equals(method.Name, exactName, StringComparison.Ordinal));
                if (m == null || !IsSyncMethodCandidate(m) || !registeredNames.Add(exactName))
                    continue;
                TryRegisterSyncMethod(compType, exactName);
            }

            foreach (string candidate in SyncMethodCandidates)
            {
                var method = compType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => string.Equals(m.Name, candidate, StringComparison.Ordinal));
                if (!IsSyncMethodCandidate(method) || !registeredNames.Add(candidate))
                    continue;
                TryRegisterSyncMethod(compType, candidate);
            }

            var heuristics = compType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(IsSyncMethodCandidate)
                .Where(m =>
                {
                    string n = m.Name.ToLowerInvariant();
                    return n.Contains("switch") || n.Contains("toggle") || n.Contains("mode") || n.Contains("verb");
                })
                .GroupBy(m => m.Name)
                .Where(g => g.Count() == 1)
                .Select(g => g.First().Name);

            foreach (string methodName in heuristics)
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
                // Some method names may exist only in certain versions or fail API validation.
            }
        }

        private static bool CommandProcessInputPrefix(Command __instance)
        {
            if (!MP.IsInMultiplayer || _executingSyncedCommand || __instance == null)
                return true;

            if (!CommandMetaTable.TryGetValue(__instance, out CommandMeta meta) || meta == null)
                return true;

            if (_captureLogCount < MaxTraceLogs)
            {
                _captureLogCount++;
                Log.Message($"[MP-MeowOnlineShop] Milira command sync dispatch: comp={meta.CompTypeName} source={meta.SourceMethodName} idx={meta.GizmoIndex} map={meta.MapIndex} thing={meta.ParentThingId}");
            }

            SyncInvokeModeCommandByIndex(meta.MapIndex, meta.ParentThingId, meta.CompTypeName, meta.SourceMethodName, meta.GizmoIndex);
            return false;
        }

        public static void SyncInvokeModeCommandByIndex(int mapIndex, int parentThingId, string compTypeName, string sourceMethodName, int gizmoIndex)
        {
            if (string.IsNullOrEmpty(compTypeName) || string.IsNullOrEmpty(sourceMethodName))
                return;

            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            Thing parent = FindThingById(map, parentThingId);
            if (!(parent is ThingWithComps twc))
                return;

            var compType = AccessTools.TypeByName(compTypeName);
            var comp = twc.AllComps?.FirstOrDefault(c => c != null && (compType == null ? c.GetType().FullName == compTypeName : compType.IsInstanceOfType(c)));
            if (comp == null)
                return;

            var sourceMethod = AccessTools.Method(comp.GetType(), sourceMethodName);
            if (sourceMethod == null)
            {
                if (!_missingMethodLogged)
                {
                    _missingMethodLogged = true;
                    Log.Warning($"[MP-MeowOnlineShop] Milira patch: source gizmo method missing: {comp.GetType().FullName}.{sourceMethodName}");
                }
                return;
            }

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
                if (_syncReplayLogCount < MaxTraceLogs)
                {
                    _syncReplayLogCount++;
                    Log.Message($"[MP-MeowOnlineShop] Milira command sync replay: comp={compTypeName} source={sourceMethodName} idx={gizmoIndex} map={mapIndex} thing={parentThingId} cmd={cmd.GetType().Name}");
                }
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
            for (int i = 0; i < allThings.Count; i++)
            {
                var thing = allThings[i];
                if (thing != null && thing.thingIDNumber == thingId)
                    return thing;
            }
            return null;
        }

        private static void CaptureGizmoCommandsPostfix(object __instance, ref IEnumerable<Gizmo> __result, MethodBase __originalMethod)
        {
            if (!MP.IsInMultiplayer || __instance == null || __result == null || __originalMethod == null)
                return;

            if (!(__instance is ThingComp comp) || comp.parent == null || comp.parent.Map == null)
                return;

            var list = __result as IList<Gizmo> ?? __result.ToList();
            for (int i = 0; i < list.Count; i++)
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

            if (!TryResolveMapAndThingId(__instance, out Map map, out int thingId))
                return;

            int typeHash = DeterministicTypeHash(__instance.GetType());
            int methodHash = DeterministicStringHash(__originalMethod?.Name ?? string.Empty);
            int baseSeed = Gen.HashCombineInt(Gen.HashCombineInt(map.Index, thingId), Gen.HashCombineInt(typeHash, methodHash));
            int seedOffset = IsGizmoMethod(__originalMethod) ? SeedOffsetCompGizmo : SeedOffsetCompInit;
            int seed = Gen.HashCombineInt(baseSeed, seedOffset);

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
            string name = method.Name ?? string.Empty;
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
            int hash = 0;
            for (int i = 0; i < value.Length; i++)
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
