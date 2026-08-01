using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for Milira/Ancot shield gizmos (e.g. CompPhysicalShield).
    /// Ensures command clicks are replayed via sync and wraps Rand state deterministically.
    /// </summary>
    internal static class Patch_MiliraShieldMode
    {
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;

        private const int SeedOffsetShieldGizmo = 0x7145;
        private const int SeedOffsetShieldInit = 0x7157;
        private const int WorldSeedOffset = 0x7191;

        private static readonly string[] TargetCompTypeNames =
        {
            "AncotLibrary.CompPhysicalShield",
            "Milira.CompPhysicalShield"
        };

        private static readonly string[] GizmoMethodNames =
        {
            "CompGetWornGizmosExtra",
            "CompGetGizmosExtra",
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
            "SwitchMode",
            "ToggleMode",
            "RaiseShield",
            "LowerShield",
            "SetShieldUp",
            "SetShieldState",
            "SetMode",
            "ToggleShield",
            "DoToggleShield",
            "Notify_ShieldModeChanged"
        };

        private static readonly ConditionalWeakTable<Command, CommandMeta> CommandMetaTable = new ConditionalWeakTable<Command, CommandMeta>();
        private static readonly HashSet<MethodBase> PatchedCommandProcessMethods = new HashSet<MethodBase>();
        private static readonly FieldInfo CommandActionField = AccessTools.Field(typeof(Command_Action), "action");
        private static readonly FieldInfo CommandToggleActionField = AccessTools.Field(typeof(Command_Toggle), "toggleAction");
        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();

        [ThreadStatic] private static Map _mapForRandPop;
        [ThreadStatic] private static bool _executingSyncedCommand;

        private static bool _syncReplayMethodRegistered;
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
                    MP.RegisterSyncMethod(typeof(Patch_MiliraShieldMode), nameof(SyncInvokeShieldCommandByIndex))
                        .SetContext(SyncContext.CurrentMap)
                        .CancelIfAnyArgNull();
                    _syncReplayMethodRegistered = true;
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Milira shield patch: failed to register sync replay method: {e.Message}");
                }
            }

            var randPrefix = AccessTools.Method(typeof(Patch_MiliraShieldMode), nameof(RandScopePrefix), new[] { typeof(object), typeof(MethodBase), typeof(int).MakeByRefType() });
            var randFinalizer = AccessTools.Method(typeof(Patch_MiliraShieldMode), nameof(RandScopeFinalizer), new[] { typeof(int) });
            var gizmoPostfix = AccessTools.Method(typeof(Patch_MiliraShieldMode), nameof(CaptureGizmoCommandsPostfix), new[] { typeof(object), typeof(IEnumerable<Gizmo>).MakeByRefType(), typeof(MethodBase) });
            var commandPrefix = AccessTools.Method(typeof(Patch_MiliraShieldMode), nameof(CommandProcessInputPrefix), new[] { typeof(Command) });

            var compTypes = DiscoverShieldCompTypes().ToList();
            foreach (var compType in compTypes)
            {
                RegisterCompSyncMethods(compType);

                foreach (var methodName in GizmoMethodNames)
                    PatchCompMethod(harmony, compType, methodName, randPrefix, randFinalizer, gizmoPostfix);

                foreach (var methodName in InitMethodNames)
                    PatchCompMethod(harmony, compType, methodName, randPrefix, randFinalizer, null);
            }

            if (commandPrefix != null)
                PatchShieldRelatedCommandProcessInput(harmony, commandPrefix);

            Log.Message($"[MP-MeowOnlineShop] Milira shield patch active: compTypes={compTypes.Count}, syncMethods={_registeredSyncMethods}, patchedMethods={_patchedMethods}.");
        }

        /// <summary>
        /// 仅对常见 <see cref="Command"/> 子类上<strong>自行声明</strong>的 <c>ProcessInput</c> 打补丁，
        /// 避免对 <c>GenTypes.AllTypes</c> 全量扫描导致与其它 Mod 的 Command 补丁冲突。
        /// 盾牌 Gizmo 点击通常走 <see cref="Command_Action"/> / <see cref="Command_Toggle"/> / Target 系。
        /// </summary>
        private static void PatchShieldRelatedCommandProcessInput(Harmony harmony, MethodInfo commandPrefix)
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

        private static IEnumerable<Type> DiscoverShieldCompTypes()
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

                string fullName = t.FullName ?? t.Name ?? string.Empty;
                if (fullName.IndexOf("PhysicalShield", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    set.Add(t);
                    continue;
                }

                // 仅放宽到 Ancot 系的盾牌组件，避免覆盖过广。
                if (fullName.IndexOf("Ancot", StringComparison.OrdinalIgnoreCase) >= 0
                    && fullName.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0
                    && fullName.IndexOf("Comp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    set.Add(t);
                }
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
                Log.Warning($"[MP-MeowOnlineShop] Milira shield patch: failed patch {compType.FullName}.{methodName}: {e.Message}");
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
                    string n = (m.Name ?? string.Empty).ToLowerInvariant();
                    return n.Contains("shield") || n.Contains("toggle") || n.Contains("switch") || n.Contains("mode")
                           || n.Contains("raise") || n.Contains("lower");
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

        private static bool CommandProcessInputPrefix(Command __instance)
        {
            if (!MP.IsInMultiplayer || _executingSyncedCommand || __instance == null)
                return true;
            if (!CommandMetaTable.TryGetValue(__instance, out CommandMeta meta) || meta == null)
                return true;

            SyncInvokeShieldCommandByIndex(meta.MapIndex, meta.ParentThingId, meta.CompTypeName, meta.SourceMethodName, meta.GizmoIndex);
            return false;
        }

        public static void SyncInvokeShieldCommandByIndex(int mapIndex, int parentThingId, string compTypeName, string sourceMethodName, int gizmoIndex)
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

                // Ancot CompPhysicalShield exposes its holdShield mutation through a Command_Toggle
                // compiler-generated delegate. Invoke that delegate directly during sync replay instead
                // of routing through GUI ProcessInput/Event state.
                if (cmd is Command_Toggle commandToggle)
                {
                    var toggleAction = CommandToggleActionField?.GetValue(commandToggle) as Action;
                    if (toggleAction != null)
                    {
                        toggleAction.Invoke();
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

                // Worn apparel, equipment and inventory items are not direct map lister entries.
                // Search each spawned root holder so an Apparel-hosted CompPhysicalShield can be
                // resolved identically on every peer from its stable thing ID.
                if (!(thing is IThingHolder holder))
                    continue;

                var heldThings = ThingOwnerUtility.GetAllThingsRecursively(holder);
                for (int j = 0; j < heldThings.Count; j++)
                {
                    var heldThing = heldThings[j];
                    if (heldThing != null && heldThing.thingIDNumber == thingId)
                        return heldThing;
                }
            }

            return null;
        }

        private static void CaptureGizmoCommandsPostfix(object __instance, ref IEnumerable<Gizmo> __result, MethodBase __originalMethod)
        {
            if (!MP.IsInMultiplayer || __instance == null || __result == null || __originalMethod == null)
                return;
            if (!(__instance is ThingComp comp) || comp.parent == null || comp.parent.MapHeld == null)
                return;

            // 仅处理无参 gizmo 生产方法，保证可在同步时重放。
            if (__originalMethod.GetParameters().Length != 0)
                return;

            var list = __result as IList<Gizmo> ?? __result.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Command command))
                    continue;

                var meta = new CommandMeta
                {
                    MapIndex = comp.parent.MapHeld.Index,
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
            int seedOffset = IsGizmoMethod(__originalMethod) ? SeedOffsetShieldGizmo : SeedOffsetShieldInit;
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
                map = comp.parent?.MapHeld;
                thingId = comp.parent?.thingIDNumber ?? 0;
                return map != null && thingId != 0;
            }

            if (instance is Thing thing)
            {
                map = thing.MapHeld;
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
