using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    // Verified snapshot/iteration defects found while examining Desync325-328.
    // These are narrow safeguards, not a claim that all four upstream triggers
    // are uniquely established by the available rolling trace window.
    internal static class Patch_Desync325Boundaries
    {
        private static FieldInfo dispatchCounter, taskFinder, groupOffset, sourceOffset;
        private static readonly ConditionalWeakTable<object, CursorState> Cursors =
            new ConditionalWeakTable<object, CursorState>();
        private static readonly AccessTools.FieldRef<Hediff, List<Ability>> HediffAbilities =
            AccessTools.FieldRefAccess<Hediff, List<Ability>>("abilities");
        private static readonly MethodInfo ThingsAt = AccessTools.Method(typeof(ThingGrid),
            nameof(ThingGrid.ThingsListAt), new[] { typeof(IntVec3) });
        private sealed class CursorState { public int Dispatch, Group, Source; }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled) return;
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(DamageWorker), "ExplosionAffectCell");
                if (method == null || ThingsAt == null ||
                    PatchProcessor.GetOriginalInstructions(method).Count(i => Equals(i.operand, ThingsAt)) != 1)
                    throw new InvalidOperationException("Expected one explosion ThingsListAt call");
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(Patch_Desync325Boundaries), nameof(OrderExplosionTargets)));
                harmony.Patch(AccessTools.PropertyGetter(typeof(Hediff), nameof(Hediff.AllAbilitiesForReading)),
                    prefix: new HarmonyMethod(typeof(Patch_Desync325Boundaries), nameof(ReadHediffAbilities)) { priority = Priority.First });
                Log.Message("[MP-MeowOnlineShop][Desync325] READY explosion target ordering and Hediff UI ability guard.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Desync325] REQUIRED_TARGET_FAILED native boundaries: " + e); }

            if (!ModsConfig.IsActive("zuoyao.ravenrace")) return;
            try
            {
                Type manager = AccessTools.TypeByName("RavenRace.Features.Drone.Hauling.MapComponent_DroneManager");
                if (manager == null) throw new TypeLoadException("Raven haul manager");
                dispatchCounter = AccessTools.DeclaredField(manager, "dispatchCounter");
                taskFinder = AccessTools.DeclaredField(manager, "taskFinder");
                groupOffset = AccessTools.DeclaredField(taskFinder?.FieldType, "nextGroupOffset");
                sourceOffset = AccessTools.DeclaredField(taskFinder?.FieldType, "nextGeneralStorageSourceOffset");
                if (dispatchCounter?.FieldType != typeof(int) || groupOffset?.FieldType != typeof(int) ||
                    sourceOffset?.FieldType != typeof(int)) throw new MissingFieldException("Raven scheduler/cursor fields");
                var expose = AccessTools.DeclaredMethod(manager, "ExposeData", Type.EmptyTypes);
                if (expose == null) throw new MissingMethodException("Raven ExposeData");
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_Desync325Boundaries), nameof(ExposeDroneCursors)));
                Log.Message("[MP-MeowOnlineShop][Desync325] READY Raven dispatch countdown and both task search cursors serialized.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Desync325] REQUIRED_TARGET_FAILED Raven snapshot: " + e); }
        }

        private static IEnumerable<CodeInstruction> OrderExplosionTargets(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            if (body.Count(i => Equals(i.operand, ThingsAt)) != 1) throw new InvalidOperationException("Explosion target IL changed");
            foreach (var instruction in body)
            {
                if (Equals(instruction.operand, ThingsAt))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Desync325Boundaries), nameof(OrderedThingsAt));
                }
                yield return instruction;
            }
        }

        private static List<Thing> OrderedThingsAt(ThingGrid grid, IntVec3 cell)
        {
            var original = grid.ThingsListAt(cell);
            if (!MP.IsInMultiplayer || original.Count < 2) return original;
            // Do not reorder the shared grid or consume random state. Visual
            // Mote/Ethereal entries are ignored by the original damage worker.
            var ordered = new List<Thing>(original);
            ordered.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            return ordered;
        }

        private static bool ReadHediffAbilities(Hediff __instance, ref List<Ability> __result)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            // Normal simulation retains the original lazy initializer. A mod
            // calling this getter directly from UI cannot allocate an AbilityID.
            __result = HediffAbilities(__instance) ?? new List<Ability>();
            return false;
        }

        private static void ExposeDroneCursors(object __instance)
        {
            CursorState state = Cursors.GetOrCreateValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                object finder = taskFinder.GetValue(__instance);
                state.Dispatch = (int)dispatchCounter.GetValue(__instance);
                state.Group = finder == null ? 0 : (int)groupOffset.GetValue(finder);
                state.Source = finder == null ? 0 : (int)sourceOffset.GetValue(finder);
            }
            Scribe_Values.Look(ref state.Dispatch, "mpMeowRavenDispatchCountdown", 0);
            Scribe_Values.Look(ref state.Group, "mpMeowRavenNextGroup", 0);
            Scribe_Values.Look(ref state.Source, "mpMeowRavenNextStorageSource", 0);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            // Native PostLoadInit replaces taskFinder; restore cursors only
            // after that replacement. Caches are rebuilt by the native finder.
            dispatchCounter.SetValue(__instance, state.Dispatch);
            object restoredFinder = taskFinder.GetValue(__instance);
            if (restoredFinder == null) throw new InvalidOperationException("Raven taskFinder missing after load");
            groupOffset.SetValue(restoredFinder, state.Group);
            sourceOffset.SetValue(restoredFinder, state.Source);
        }
    }
}
