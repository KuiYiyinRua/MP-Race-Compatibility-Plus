using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TurretCombatSleep
{
    // One synchronous power sum, one option index. Never cache power output,
    // operational status, occupants, fuel, or a value across simulation ticks.
    internal static class RavenPowerLookup
    {
        private sealed class Scope
        {
            internal object Extension;
            internal readonly Dictionary<ThingDef, object> Options = new Dictionary<ThingDef, object>();
            internal bool Ready;
        }
        private struct Frame
        {
            internal Scope Previous;
            internal bool Entered;
        }
        [ThreadStatic] private static Scope current;
        [ThreadStatic] private static Scope spare;
        private static Func<object, IList> ReadOptions;
        private static Func<object, ThingDef> ReadDef;
        private static MethodInfo FindOption;
        private static MethodInfo Lookup;

        internal static void Install(Harmony harmony)
        {
            var hub = AccessTools.TypeByName("RavenRace.Features.DefenseHub.Building_RavenDefenseHub");
            var utility = AccessTools.TypeByName("RavenRace.Features.DefenseHub.DefenseHubUIUtility");
            var extension = AccessTools.TypeByName("RavenRace.Features.DefenseHub.DefenseHubExtension");
            if (hub == null || utility == null || extension == null) return;
            try
            {
                FindOption = AccessTools.DeclaredMethod(utility, "FindTurretOption", new[] { typeof(ThingDef), extension });
                var getOptions = AccessTools.DeclaredMethod(utility, "GetTurretOptions", new[] { extension });
                var sum = AccessTools.PropertyGetter(hub, "ManagedTurretPowerConsumption");
                if (FindOption == null || getOptions == null || sum == null) throw new MissingMethodException("Raven power lookup targets");
                // Do not bypass another mod's custom filtering or lookup semantics.
                if (Harmony.GetPatchInfo(FindOption)?.Owners.Any() == true || Harmony.GetPatchInfo(getOptions)?.Owners.Any() == true)
                {
                    Log.Warning("[Meow.TurretCombatSleep] Raven power lookup retained: another mod patches option selection");
                    return;
                }
                ReadOptions = TurretCombatSleep.FieldGetter<object, IList>(extension, "turretOptions");
                ReadDef = TurretCombatSleep.FieldGetter<object, ThingDef>(FindOption.ReturnType, "turretDef");
                Lookup = AccessTools.Method(typeof(RavenPowerLookup), nameof(Find)).MakeGenericMethod(extension, FindOption.ReturnType);
                var holder = typeof(Original<,>).MakeGenericType(extension, FindOption.ReturnType);
                var delegateType = typeof(Func<,,>).MakeGenericType(typeof(ThingDef), extension, FindOption.ReturnType);
                holder.GetField("Find", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Delegate.CreateDelegate(delegateType, FindOption));
                harmony.Patch(sum, prefix: new HarmonyMethod(typeof(RavenPowerLookup), nameof(Begin)),
                    finalizer: new HarmonyMethod(typeof(RavenPowerLookup), nameof(End)),
                    transpiler: new HarmonyMethod(typeof(RavenPowerLookup), nameof(Rewrite)));
                Log.Message("[Meow.TurretCombatSleep] Raven power sum shares one option index; live power values preserved");
            }
            catch (Exception e) { Log.Error("[Meow.TurretCombatSleep] REQUIRED_TARGET_FAILED Raven power lookup " + e); }
        }

        private static class Original<TExtension, TOption>
        {
            internal static Func<ThingDef, TExtension, TOption> Find = null; // Assigned once through the closed generic holder.
        }

        private static void Begin(out Frame __state)
        {
            __state = new Frame { Previous = current, Entered = true };
            if (!MP.IsInMultiplayer) { current = null; return; }
            current = spare ?? new Scope();
            spare = null;
            current.Ready = false;
        }

        private static Exception End(Exception __exception, Frame __state)
        {
            if (!__state.Entered) return __exception;
            if (current != null)
            {
                current.Options.Clear();
                current.Extension = null;
                current.Ready = false;
                spare = current;
            }
            current = __state.Previous;
            return __exception;
        }

        private static TOption Find<TExtension, TOption>(ThingDef def, TExtension extension) where TOption : class
        {
            var scope = current;
            if (scope == null) return Original<TExtension, TOption>.Find(def, extension);
            if (!scope.Ready || !ReferenceEquals(scope.Extension, extension))
            {
                scope.Options.Clear();
                scope.Extension = extension;
                var options = extension == null ? null : ReadOptions(extension);
                if (options != null)
                    for (int i = 0; i < options.Count; i++)
                    {
                        var option = options[i];
                        if (option == null) continue;
                        var key = ReadDef(option);
                        // Match GetTurretOptions filtering and first-match semantics.
                        if (key != null && !scope.Options.ContainsKey(key)) scope.Options.Add(key, option);
                    }
                scope.Ready = true;
            }
            return def != null && scope.Options.TryGetValue(def, out var value) ? (TOption)value : null;
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int count = 0;
            foreach (var instruction in code)
                if (instruction.Calls(FindOption))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = Lookup;
                    count++;
                }
            if (count != 1) throw new InvalidOperationException("Expected one FindTurretOption call in power sum, got " + count);
            return code;
        }
    }
}
