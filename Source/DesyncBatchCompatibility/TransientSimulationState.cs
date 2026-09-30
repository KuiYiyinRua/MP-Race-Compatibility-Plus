using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    // SimpleTrailer defs are explicitly unsaved, unselectable visual movers.
    // A snapshot cannot carry their membership or the control center's pending
    // visual queue. Neither their IDs nor their draws may advance simulation.
    internal static class NivarianTrailBoundary
    {
        static FieldInfo localOverride;
        internal struct State { internal bool active, previous; }
        static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
        {
            "Nivarian_SimpleTrailer_Purple", "Nivarian_SimpleTrailer_Green",
            "Nivarian_SimpleTrailer_Blue", "Nivarian_SimpleTrailer_Pink", "Nivarian_SimpleTrailer_Red"
        };

        internal static void Apply(Harmony harmony)
        {
            localOverride = Bootstrap.Field(Bootstrap.Type("Multiplayer.Client.Patches.UniqueIdsPatch"),
                "useLocalIdsOverride", typeof(bool));
            var mover = Bootstrap.Type("Nivarian_Race.Code.NivarianThing.ProgrammableMoverThing");
            foreach (var def in DefDatabase<ThingDef>.AllDefs.Where(d => Names.Contains(d.defName)))
                if (def.isSaveable || def.thingClass != mover)
                    throw new InvalidOperationException("SimpleTrailer is no longer a purely visual mover: " + def.defName);

            var end = new HarmonyMethod(typeof(NivarianTrailBoundary), nameof(End));
            harmony.Patch(Bootstrap.Method(typeof(ThingMaker), "MakeThing", typeof(ThingDef), typeof(ThingDef)),
                prefix: new HarmonyMethod(typeof(NivarianTrailBoundary), nameof(BeforeSpawn)), finalizer: end);
            int count = 0;
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(GenSpawn)).Where(m => m.Name == "Spawn"))
            {
                var args = method.GetParameters();
                if (args.Length == 0 || (args[0].ParameterType != typeof(Thing) && args[0].ParameterType != typeof(ThingDef))) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NivarianTrailBoundary), nameof(BeforeSpawn)), finalizer: end);
                count++;
            }
            if (count == 0) throw new MissingMethodException("GenSpawn.Spawn");
            harmony.Patch(Bootstrap.Method(Bootstrap.Type("Nivarian_Race.Code.NivarianThing.Base.ProgrammableThing"), "Tick"),
                prefix: new HarmonyMethod(typeof(NivarianTrailBoundary), nameof(BeforeTick)), finalizer: end);
            var center = Bootstrap.Type("Nivarian_Race.Code.Comps.BuildingComps.CompNiraControlCenter");
            foreach (var method in new[] {
                Bootstrap.Method(center, "EnqueueControlTrail", typeof(Thing), typeof(string), typeof(bool)),
                Bootstrap.Method(center, "TickPendingTrails"),
                Bootstrap.Method(center, "SpawnControlTrail", typeof(ThingDef), typeof(Thing), typeof(bool)) })
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(NivarianTrailBoundary), nameof(BeforeVisual)), finalizer: end);
            harmony.Patch(Bootstrap.Method(Bootstrap.Type("Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister"), "SpawnAbsorbTrail"),
                prefix: new HarmonyMethod(typeof(NivarianTrailBoundary), nameof(BeforeVisual)), finalizer: end);
            Log.Message("[DesyncBatchCompat] SimpleTrailer local ID/Rand boundaries: spawnOverloads=" + count + "; gameplay movers unchanged.");
        }

        internal static bool IsTrail(ThingDef def) => def != null && !def.isSaveable && Names.Contains(def.defName)
            && def.thingClass?.FullName == "Nivarian_Race.Code.NivarianThing.ProgrammableMoverThing";
        static void BeforeSpawn(object __0, out State __state) => Begin(IsTrail(__0 as ThingDef ?? (__0 as Thing)?.def), out __state);
        static void BeforeTick(Thing __instance, out State __state) => Begin(IsTrail(__instance.def), out __state);
        static void BeforeVisual(out State __state) => Begin(true, out __state);
        internal static void Begin(bool visual, out State state)
        {
            state = default;
            if (!visual || !MP.IsInMultiplayer) return;
            state.previous = (bool)localOverride.GetValue(null);
            Rand.PushState();
            try { localOverride.SetValue(null, true); state.active = true; }
            catch { Rand.PopState(); throw; }
        }
        internal static void End(State __state)
        {
            if (!__state.active) return;
            try { localOverride.SetValue(null, __state.previous); }
            finally { Rand.PopState(); }
        }
    }

    // The upstream generator adds tag-selected apparel to PawnKindDef's shared
    // list. That process-local mutation survives on the host but is absent on a
    // cold peer; it also accumulates duplicate equipment on repeated generation.
    internal static class MilianApparelBoundary
    {
        internal static void Apply(Harmony harmony)
        {
            var target = Bootstrap.Method(Bootstrap.Type("Milira.Milira_MilianPawnGenerator_Patch"), "Postfix",
                typeof(Pawn).MakeByRefType(), typeof(PawnGenerationRequest));
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(MilianApparelBoundary), nameof(CloneRead)));
        }
        internal static IEnumerable<CodeInstruction> CloneRead(IEnumerable<CodeInstruction> instructions)
        {
            var field = Bootstrap.Field(typeof(PawnKindDef), "apparelRequired", typeof(List<ThingDef>));
            int count = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MilianApparelBoundary), nameof(ForGeneration)));
                    count++;
                }
            }
            if (count != 1) throw new InvalidOperationException("Expected one Milian apparelRequired read, found " + count);
        }
        internal static List<ThingDef> ForGeneration(List<ThingDef> source) =>
            MP.IsInMultiplayer && source != null ? new List<ThingDef>(source) : source;
    }
}
