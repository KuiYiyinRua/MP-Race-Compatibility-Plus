using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynSwapTarget
    {
        static FieldInfo target;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.CompAbilityEffect_SwapWeapon") ?? throw new TypeLoadException("ASEL.CompAbilityEffect_SwapWeapon");
            target = AccessTools.DeclaredField(type, "t");
            if (target?.FieldType != typeof(LocalTargetInfo)) throw new MissingFieldException(type.FullName, "t");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(AbilityComp), nameof(AbilityComp.PostExposeData), Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(MonolynSwapTarget), nameof(Save)));
            Log.Message("[MonolynSwapTarget] resolved=native nullable/cell/Thing retained AI target; missing=0");
        }
        static void Save(AbilityComp __instance)
        {
            if (!MP.IsInMultiplayer || !target.DeclaringType.IsInstanceOfType(__instance)) return;
            var value = (LocalTargetInfo)target.GetValue(__instance);
            // AICanTargetNow only replaces t when currently aiming, or when t
            // is null. Otherwise the native old target still controls distance
            // and weapon choice. Keep that identity, rather than recomputing it.
            MonolynRetainedRoots.Look(ref value, "meowSwapWeaponRetainedTarget");
            target.SetValue(__instance, value);
        }
    }
}
