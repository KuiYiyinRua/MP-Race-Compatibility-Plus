using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for Milira race flight status gizmos (<c>Milira.CompProperties_FlightControl</c> → <c>Milira.CompFlightControl</c>).
    /// Mirrors <see cref="Patch_MiliraShieldMode"/>: command click replay via sync + deterministic Rand during gizmo/init.
    /// </summary>
    internal static class Patch_MiliraFlightMode
    {
        internal const string HarmonyId = "mp.meowonlineshop.miliraflightmode";

        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;

        private const int SeedOffsetFlightGizmo = 0x71A3;
        private const int SeedOffsetFlightInit = 0x71B5;
        private const int WorldSeedOffset = 0x71C7;

        private static readonly string[] TargetCompTypeNames =
        {
            "Milira.CompFlightControl"
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
            "SwitchFly",
            "ToggleFly",
            "CycleFlightMode",
            "NextFlightMode",
            "AdvanceFlightMode",
            "SetFlightMode",
            "SwitchMode",
            "ToggleMode",
            "SetMode",
            "DoSwitchFly",
            "Notify_FlightModeChanged"
        };

        private static readonly FieldInfo CommandActionField = AccessTools.Field(typeof(Command_Action), "action");
        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();

        [ThreadStatic] private static Map _mapForRandPop;

        private static bool _syncSetModeMethodRegistered;
        private static int _registeredSyncMethods;
        private static int _patchedMethods;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            if (!_syncSetModeMethodRegistered)
            {
                try
                {
                    MP.RegisterSyncMethod(typeof(Patch_MiliraFlightMode), nameof(SyncSetFlightModeByPawnIds))
                        .SetContext(SyncContext.CurrentMap)
                        .CancelIfAnyArgNull();
                    _syncSetModeMethodRegistered = true;
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Milira flight mode patch: failed to register explicit mode sync method: {e.Message}");
                }
            }

            var randPrefix = AccessTools.Method(typeof(Patch_MiliraFlightMode), nameof(RandScopePrefix), new[] { typeof(object), typeof(MethodBase), typeof(int).MakeByRefType() });
            var randFinalizer = AccessTools.Method(typeof(Patch_MiliraFlightMode), nameof(RandScopeFinalizer), new[] { typeof(int) });
            var gizmoPostfix = AccessTools.Method(typeof(Patch_MiliraFlightMode), nameof(CaptureGizmoCommandsPostfix), new[] { typeof(object), typeof(IEnumerable<Gizmo>).MakeByRefType(), typeof(MethodBase) });

            var compTypes = DiscoverFlightCompTypes().ToList();
            if (compTypes.Count == 0)
            {
                Log.Message("[MP-MeowOnlineShop] Milira flight mode patch: CompFlightControl types not found, skipped.");
                return;
            }

            foreach (var compType in compTypes)
            {
                RegisterCompSyncMethods(compType);

                foreach (var methodName in GizmoMethodNames)
                    PatchCompMethod(harmony, compType, methodName, randPrefix, randFinalizer, gizmoPostfix);

                foreach (var methodName in InitMethodNames)
                    PatchCompMethod(harmony, compType, methodName, randPrefix, randFinalizer, null);
            }

            Log.Message($"[MP-MeowOnlineShop] Milira flight mode patch active: compTypes={compTypes.Count}, syncMethods={_registeredSyncMethods}, patchedMethods={_patchedMethods}.");
        }

        private static IEnumerable<Type> DiscoverFlightCompTypes()
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
                if (fullName.IndexOf("FlightControl", StringComparison.OrdinalIgnoreCase) >= 0
                    && (fullName.IndexOf("Milira", StringComparison.OrdinalIgnoreCase) >= 0
                        || fullName.IndexOf("Ancot", StringComparison.OrdinalIgnoreCase) >= 0))
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
                Log.Warning($"[MP-MeowOnlineShop] Milira flight mode patch: failed patch {compType.FullName}.{methodName}: {e.Message}");
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
                    return n.Contains("fly") || n.Contains("flight") || n.Contains("toggle") || n.Contains("switch") || n.Contains("mode");
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

        public static void SyncSetFlightModeByPawnIds(int mapIndex, List<int> pawnIds, int mode)
        {
            if (pawnIds == null || pawnIds.Count == 0 || mode < 0 || mode > 2)
                return;

            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            var compType = AccessTools.TypeByName("Milira.CompFlightControl");
            if (compType == null)
                return;

            var switchOnField = AccessTools.Field(compType, "switchOn");
            var onlyForMoveField = AccessTools.Field(compType, "onlyForMove");
            if (switchOnField == null || onlyForMoveField == null)
                return;

            // The original Milira menu mutates Find.Selector.SelectedObjects. Selection is local UI state,
            // so replaying that delegate produces a different pawn set on each peer. The command carries a
            // sorted, explicit pawn-id set instead and never reads selection while executing the sync command.
            foreach (int pawnId in pawnIds.Distinct().OrderBy(id => id))
            {
                var pawn = FindThingById(map, pawnId) as Pawn;
                if (pawn?.AllComps == null)
                    continue;

                var comp = pawn.AllComps.FirstOrDefault(c => c != null && compType.IsInstanceOfType(c));
                if (comp == null)
                    continue;

                bool switchOn = mode != 0;
                bool onlyForMove = mode == 1;
                switchOnField.SetValue(comp, switchOn);
                onlyForMoveField.SetValue(comp, onlyForMove);

                if (mode == 0)
                {
                    pawn.flight?.ForceLand();
                }
                else if (mode == 2 && pawn.CurJob != null)
                {
                    pawn.flight?.Notify_JobStarted(pawn.CurJob);
                }
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

            if (__originalMethod.GetParameters().Length != 0)
                return;

            var list = __result as IList<Gizmo> ?? __result.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is Command_Action command))
                    continue;

                int mapIndex = comp.parent.Map.Index;
                int fallbackPawnId = comp.parent.thingIDNumber;
                CommandActionField?.SetValue(command, (Action)(() => OpenSyncedFlightModeMenu(mapIndex, fallbackPawnId)));
            }

            __result = list;
        }

        private static void OpenSyncedFlightModeMenu(int mapIndex, int fallbackPawnId)
        {
            var map = Find.Maps?.FirstOrDefault(m => m != null && m.Index == mapIndex);
            if (map == null)
                return;

            var compType = AccessTools.TypeByName("Milira.CompFlightControl");
            var pawnIds = Find.Selector.SelectedObjects
                .OfType<Pawn>()
                .Where(p => p.Map == map && p.AllComps != null
                    && p.AllComps.Any(c => c != null && compType != null && compType.IsInstanceOfType(c)))
                .Select(p => p.thingIDNumber)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            if (pawnIds.Count == 0 && FindThingById(map, fallbackPawnId) is Pawn)
                pawnIds.Add(fallbackPawnId);
            if (pawnIds.Count == 0)
                return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(Translator.Translate("Milira.SwitchFly_Never"), () => SyncSetFlightModeByPawnIds(mapIndex, pawnIds, 0)),
                new FloatMenuOption(Translator.Translate("Milira.SwitchFly_OnlyForMove"), () => SyncSetFlightModeByPawnIds(mapIndex, pawnIds, 1)),
                new FloatMenuOption(Translator.Translate("Milira.SwitchFly_Always"), () => SyncSetFlightModeByPawnIds(mapIndex, pawnIds, 2))
            };
            Find.WindowStack.Add(new FloatMenu(options));
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
            int seedOffset = IsGizmoMethod(__originalMethod) ? SeedOffsetFlightGizmo : SeedOffsetFlightInit;
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
