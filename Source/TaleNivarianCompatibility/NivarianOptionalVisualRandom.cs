using System;
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
                finalizer: new HarmonyMethod(typeof(NivarianOptionalVisualRandom), nameof(End)));
            const string ns = "Nivarian.NivarianDrones.NivarianDroneComps.";
            words = Required(ns + "NivarianDroneComp_ThrowWords");
            voice = Required(ns + "NivarianDroneComp_Voice");
            var scheduler = Required(ns + "NivarianDroneStateTriggeredComp");
            PatchDrone(harmony, scheduler, "PostSpawnSetup", typeof(bool));
            PatchDrone(harmony, scheduler, "CompTick");
            // The public overload can be called outside the scheduler as well.
            PatchDrone(harmony, words, "ThrowText", Required("Nivarian.NivarianDrones.DroneState"));
            Log.Message("[TaleNivarianCompat] Optional icy fog and drone chatter random scopes installed; local preferences retained.");
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
        static void End(bool __state)
        {
            if (__state) Rand.PopState();
        }
    }
}
