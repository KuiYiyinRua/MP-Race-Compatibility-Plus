using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TurretCombatSleep
{
    internal static class DeepSleepEntry
    {
        private sealed class Frame
        {
            internal Frame Previous;
            internal Building_Turret Turret;
            internal bool BaseDone, ReplayRaven;
        }
        [ThreadStatic] private static Frame current;
        [ThreadStatic] private static Frame spare;
        private static Action<Building_Turret> baseTick;
        private static MethodInfo ravenPrefix;
        private static Type ravenHub;
        private static Func<ThingWithComps, ThingComp> remote;
        private static Func<ThingComp, Building> hub;
        private static Func<Building, bool> operational;
        private static readonly HashSet<MethodBase> Enabled = new HashSet<MethodBase>();

        internal static void Install(Harmony harmony)
        {
            var method = AccessTools.DeclaredMethod(typeof(Building_Turret), "Tick", Type.EmptyTypes);
            if (method == null) throw new MissingMethodException("Building_Turret.Tick");
            var dm = new DynamicMethod("TurretSleep_BaseTick", typeof(void), new[] { typeof(Building_Turret) }, typeof(DeepSleepEntry).Module, true);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, method); il.Emit(OpCodes.Ret);
            baseTick = (Action<Building_Turret>)dm.CreateDelegate(typeof(Action<Building_Turret>));

            var ravenType = AccessTools.TypeByName("RavenRace.Features.DefenseHub.Harmony.Patch_DefenseHub_UniversalTurret");
            bool ravenSafe = ravenType == null;
            if (ravenType != null)
            {
                ravenPrefix = AccessTools.DeclaredMethod(ravenType, "Prefix_Tick", new[] { typeof(Building_TurretGun) });
                var remoteType = AccessTools.TypeByName("RavenRace.Features.DefenseHub.RemoteTurrets.CompRemoteTurret");
                ravenHub = AccessTools.TypeByName("RavenRace.Features.DefenseHub.Building_RavenDefenseHub");
                var status = ravenHub == null ? null : AccessTools.PropertyGetter(ravenHub, "RemoteTurretsOperational");
                if (ravenPrefix != null && remoteType != null && status != null &&
                    !PatchedByOthers(ravenPrefix) && !PatchedByOthers(status))
                {
                    harmony.Patch(status, transpiler: new HarmonyMethod(typeof(DeepSleepEntry), nameof(CachePowerLookup)));
                    var getter = AccessTools.Method(typeof(ComponentIndex), "Get").MakeGenericMethod(remoteType);
                    var read = new DynamicMethod("TurretSleep_RemoteComp", typeof(ThingComp), new[] { typeof(ThingWithComps) }, typeof(DeepSleepEntry).Module, true);
                    var readIl = read.GetILGenerator();
                    readIl.Emit(OpCodes.Ldarg_0); readIl.Emit(OpCodes.Call, getter); readIl.Emit(OpCodes.Ret);
                    remote = (Func<ThingWithComps, ThingComp>)read.CreateDelegate(typeof(Func<ThingWithComps, ThingComp>));
                    hub = TurretCombatSleep.FieldGetter<ThingComp, Building>(remoteType, "hub");
                    var getStatus = new DynamicMethod("TurretSleep_HubOperational", typeof(bool), new[] { typeof(Building) }, typeof(DeepSleepEntry).Module, true);
                    var si = getStatus.GetILGenerator();
                    si.Emit(OpCodes.Ldarg_0); si.Emit(OpCodes.Castclass, ravenHub); si.Emit(OpCodes.Call, status); si.Emit(OpCodes.Ret);
                    operational = (Func<Building, bool>)getStatus.CreateDelegate(typeof(Func<Building, bool>));
                    harmony.Patch(ravenPrefix, prefix: new HarmonyMethod(typeof(DeepSleepEntry), nameof(ReplayRaven)) { priority = Priority.First });
                    ravenSafe = true;
                }
            }
            foreach (var type in new[] { typeof(Building_TurretGun), AccessTools.TypeByName("AncotLibrary.Building_SpinTurretGun") })
            {
                if (type == null) continue;
                var tick = AccessTools.DeclaredMethod(type, "Tick", Type.EmptyTypes);
                if (tick == null) continue;
                bool native = type == typeof(Building_TurretGun);
                var patches = Harmony.GetPatchInfo(tick);
                // Unknown interception may depend on __runOriginal or cancel all
                // updates. Preserve that contract rather than guessing a shortcut.
                bool safe = (!native || ravenSafe) && (patches == null ||
                    (patches.Prefixes.All(p => p.PatchMethod == ravenPrefix) &&
                     patches.Postfixes.Count == 0 && patches.Finalizers.Count == 0 &&
                     patches.Transpilers.All(p => p.owner == TurretCombatSleep.HarmonyId)));
                if (!safe)
                {
                    Log.Message("[Meow.TurretCombatSleep] Deep entry retained original due to other Tick hooks: " + type.FullName);
                    continue;
                }
                harmony.Patch(tick,
                    prefix: new HarmonyMethod(typeof(DeepSleepEntry), nameof(Before)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(DeepSleepEntry), nameof(After)),
                    transpiler: new HarmonyMethod(typeof(DeepSleepEntry), nameof(ReplaceBase)) { priority = Priority.Last });
                Enabled.Add(tick);
                Log.Message("[Meow.TurretCombatSleep] DEEP_ENTRY " + type.FullName + " (base components preserved)");
            }
        }
        private static bool PatchedByOthers(MethodBase method)
        {
            var p = Harmony.GetPatchInfo(method);
            return p != null && p.Owners.Any();
        }
        private static bool Eligible(Building_Turret turret, MethodBase method)
            => method.DeclaringType == typeof(Building_TurretGun)
                ? TurretCombatSleep.ShouldSleep((Building_TurretGun)turret)
                : TurretCombatSleep.ShouldPauseCombat(turret);

        private static bool Before(Building_Turret __instance, MethodBase __originalMethod, out Frame __state)
        {
            __state = null;
            if (!Enabled.Contains(__originalMethod) || !MP.IsInMultiplayer || !Eligible(__instance, __originalMethod)) return true;
            bool native = __originalMethod.DeclaringType == typeof(Building_TurretGun);
            if (native && remote != null)
            {
                var comp = remote(__instance);
                var controller = comp == null ? null : hub(comp);
                // Inactive Raven hubs retain their original dormant notifications
                // and original cancellation of the entire base Tick.
                if (controller != null && ravenHub.IsInstanceOfType(controller) && !operational(controller)) return true;
            }
            var frame = spare ?? new Frame();
            spare = null;
            frame.Previous = current; frame.Turret = __instance;
            frame.BaseDone = false; frame.ReplayRaven = false;
            current = __state = frame;
            baseTick(__instance); // Fuel/charging/maintenance and ALL base comps, once.
            frame.BaseDone = true;
            if (Eligible(__instance, __originalMethod)) return false;
            // A component changed combat state: resume this same tick without
            // ticking components twice or re-evaluating the pre-base Raven gate.
            frame.ReplayRaven = native && ravenPrefix != null;
            return true;
        }
        private static bool ReplayRaven(Building_TurretGun __0, ref bool __result)
        {
            if (current == null || !current.ReplayRaven || current.Turret != __0) return true;
            current.ReplayRaven = false;
            __result = true;
            return false;
        }
        public static void TickBase(Building_Turret turret)
        {
            if (current != null && current.Turret == turret && current.BaseDone)
            {
                current.BaseDone = false;
                return;
            }
            baseTick(turret);
        }
        private static Exception After(Exception __exception, Frame __state)
        {
            if (__state == null) return __exception;
            current = __state.Previous;
            __state.Previous = null; __state.Turret = null;
            __state.BaseDone = false; __state.ReplayRaven = false;
            spare = __state;
            return __exception;
        }
        private static IEnumerable<CodeInstruction> CachePowerLookup(IEnumerable<CodeInstruction> input)
        {
            int matches = 0;
            var code = input.ToList();
            foreach (var c in code)
                if ((c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt) &&
                    c.operand is MethodInfo m && m.DeclaringType == typeof(ThingWithComps) &&
                    m.Name == "GetComp" && m.IsGenericMethod &&
                    m.GetGenericArguments().SequenceEqual(new[] { typeof(CompPowerTrader) }))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(ComponentIndex), "Get").MakeGenericMethod(typeof(CompPowerTrader));
                    matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Raven status expected one power component lookup, got " + matches);
            return code;
        }
        private static IEnumerable<CodeInstruction> ReplaceBase(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var target = AccessTools.DeclaredMethod(typeof(Building_Turret), "Tick", Type.EmptyTypes);
            int matches = 0;
            foreach (var c in code)
                if (c.Calls(target))
                {
                    c.opcode = OpCodes.Call;
                    c.operand = AccessTools.Method(typeof(DeepSleepEntry), nameof(TickBase));
                    matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Deep entry expected exactly one base Tick call, got " + matches);
            return code;
        }
    }
}
