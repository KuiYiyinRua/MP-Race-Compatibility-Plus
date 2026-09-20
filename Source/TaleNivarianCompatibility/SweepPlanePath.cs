using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class SweepPlanePath
    {
        static FieldInfo initialized, offset;
        static MethodInfo initialize;
        internal static void Apply(Harmony harmony)
        {
            if (!ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
            var type = AccessTools.TypeByName("PLAMilira.Map_Sweep_Plane")
                ?? throw new TypeLoadException("PLAMilira.Map_Sweep_Plane");
            initialized = AccessTools.Field(type, "OnceFlag") ?? throw new MissingFieldException(type.FullName, "OnceFlag");
            offset = AccessTools.Field(type, "RandNew") ?? throw new MissingFieldException(type.FullName, "RandNew");
            initialize = AccessTools.DeclaredMethod(type, "RandFactor") ?? throw new MissingMethodException(type.FullName, "RandFactor");
            harmony.Patch(initialize, prefix: Hook(nameof(BeginRandom)), finalizer: Hook(nameof(EndRandom)));
            harmony.Patch(AccessTools.DeclaredMethod(type, "BPos") ?? throw new MissingMethodException(type.FullName, "BPos"),
                prefix: Hook(nameof(BeginPreview)), finalizer: Hook(nameof(EndPreview)));
            harmony.Patch(AccessTools.DeclaredMethod(type, "Launch") ?? throw new MissingMethodException(type.FullName, "Launch"),
                postfix: Hook(nameof(AfterLaunch)));
            Log.Message("[TaleNivarianCompat] sweep plane path initialized deterministically; drawing cannot initialize simulation state.");
        }
        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(SweepPlanePath), name);
        static void BeginRandom(Thing __instance, out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState(Gen.HashCombineInt(0x53574545, __instance.thingIDNumber));
        }
        static void EndRandom(bool __state) { if (__state) Rand.PopState(); }
        internal sealed class Preview { internal bool Initialized; internal Vector3 Offset; }
        static void BeginPreview(Thing __instance, out Preview __state)
        {
            __state = MP.IsInMultiplayer && MP.InInterface
                ? new Preview { Initialized = (bool)initialized.GetValue(__instance), Offset = (Vector3)offset.GetValue(__instance) }
                : null;
        }
        static void EndPreview(Thing __instance, Preview __state)
        {
            if (__state == null) return;
            initialized.SetValue(__instance, __state.Initialized);
            offset.SetValue(__instance, __state.Offset);
        }
        static void AfterLaunch(Thing __instance)
        {
            if (MP.IsInMultiplayer && !MP.InInterface && !(bool)initialized.GetValue(__instance))
                initialize.Invoke(__instance, null);
        }
    }
}
