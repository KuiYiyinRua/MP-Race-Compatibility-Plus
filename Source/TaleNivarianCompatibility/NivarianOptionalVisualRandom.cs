using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianOptionalVisualRandom
    {
        static Type words, voice;
        internal static void Apply(Harmony harmony)
        {
            var fog = Required("Nivarian.HediffComp_NivarianVisual");
            harmony.Patch(AccessTools.DeclaredMethod(fog, "CompPostTick", new[] { typeof(float).MakeByRefType() })
                ?? throw new MissingMethodException(fog.FullName, "CompPostTick"),
                prefix: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(BeginFog)),
                transpiler: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(FogPauseBoundary)),
                finalizer: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(End)));
            const string ns = "Nivarian.NivarianDrones.NivarianDroneComps.";
            words = Required(ns + "NivarianDroneComp_ThrowWords");
            voice = Required(ns + "NivarianDroneComp_Voice");
            var scheduler = Required(ns + "NivarianDroneStateTriggeredComp");
            PatchDrone(harmony, scheduler, "PostSpawnSetup", typeof(bool));
            PatchDrone(harmony, scheduler, "CompTick");
            // The public overload can be called outside the scheduler as well.
            PatchDrone(harmony, words, "ThrowText", Required("Nivarian.NivarianDrones.DroneState"));
            if (ModsConfig.IsActive("keeptpa.NivarianDraconiture"))
            {
                // Despite the namespace, this worker belongs to Draconiture, not
                // DraconicMilitary. Its local speech setting and static rate limit
                // can skip word selection differently after a join. Both the retry
                // interval and selected text are cosmetic; isolate only this worker.
                var engineer = Required("NivarianRace.DraconicMilitary.EngineeringDroneThing");
                harmony.Patch(AccessTools.DeclaredMethod(engineer, "TickThrowWords", Type.EmptyTypes)
                    ?? throw new MissingMethodException(engineer.FullName, "TickThrowWords"),
                    prefix: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(BeginEngineer)),
                    finalizer: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(End)));
                Log.Message("[TaleNivarianCompat] Draconiture engineering drone speech Rand isolated: interval, retry and word selection; MVID=" + engineer.Module.ModuleVersionId);
            }
            Log.Message("[TaleNivarianCompat] Optional icy fog and drone chatter random scopes installed; local preferences retained.");
            Log.Message("[TaleNivarianCompat] Icy fog tick no longer reads the viewer map pause state during multiplayer simulation.");
        }
        static IEnumerable<CodeInstruction> FogPauseBoundary(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.Paused));
            var replacement = AccessTools.Method(typeof(NivarianOptionalVisualRandom), nameof(FogPaused));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one native icy fog pause query, found " + count);
        }
        static bool FogPaused(TickManager manager)
        {
            // A simulation PostTick already belongs to a map that MP is ticking.
            // MP's Paused getter instead reads Find.CurrentMap, which can be an
            // unrelated map paused by a trade or role-change session on one peer.
            return MP.IsInMultiplayer && !MP.InInterface ? false : manager.Paused;
        }
        static Type Required(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        static void PatchDrone(Harmony harmony, Type type, string name, params Type[] args)
        {
            harmony.Patch(AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name),
                prefix: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(BeginDrone)),
                finalizer: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(End)));
        }
        static void BeginFog(HediffComp __instance, out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState(Gen.HashCombineInt(__instance.Pawn.thingIDNumber, Find.TickManager.TicksGame));
        }
        static void BeginDrone(ThingComp __instance, out bool __state)
        {
            // This base class could acquire gameplay subclasses in another mod. Limit the scope
            // to the two verified cosmetic workers, including their token/retry scheduling.
            __state = MP.IsInMultiplayer && (__instance.GetType() == words || __instance.GetType() == voice);
            if (__state) Rand.PushState(Gen.HashCombineInt(__instance.parent.thingIDNumber, Find.TickManager.TicksGame));
        }
        static void BeginEngineer(Thing __instance, out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState(Gen.HashCombineInt(
                Gen.HashCombineInt(0x454E4757, __instance.thingIDNumber), Find.TickManager.TicksGame));
        }
        static void End(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }
}
