using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenHouseholdActions
    {
        internal static void Apply(Harmony harmony)
        {
            var methods = new List<MethodInfo>();
            Add(methods, "RavenRace.Features.MiscSmallFeatures.Bathtub.Building_RavenBathtub", "<GetGizmos>b__11_1");
            Add(methods, "RavenRace.Features.MiscSmallFeatures.SakeCup.Building_SakeCup", "<GetGizmos>b__11_1");
            Add(methods, "RavenRace.Features.MiscSmallFeatures.AVTelevision.CompTV_AV", "<CompGetGizmosExtra>b__5_1");
            const string concealment = "RavenRace.Features.DefenseSystem.Concealment.Building_Concealment";
            Add(methods, concealment, "EjectOccupant");
            Add(methods, concealment, "SwapGunToPawnWeapon", typeof(Pawn), typeof(Thing));
            foreach (var method in methods) MP.RegisterSyncMethod(method, null);
            harmony.Patch(methods[4], prefix: new HarmonyMethod(typeof(RavenHouseholdActions), nameof(ValidateSwap)));
            Log.Message("[MP-MeowOnlineShop] Raven household targets: 5/5; runtime coverage pending.");
        }
        private static void Add(List<MethodInfo> methods, string type, string name, params Type[] args)
        {
            var resolved = AccessTools.TypeByName(type) ?? throw new TypeLoadException(type);
            methods.Add(AccessTools.DeclaredMethod(resolved, name, args) ?? throw new MissingMethodException(type, name));
        }
        private static bool ValidateSwap(Thing __instance, Pawn __0, Thing __1)
        {
            if (!MP.IsInMultiplayer) return true;
            if (__0 == null || __instance.Destroyed || !__instance.Spawned ||
                !ReferenceEquals(AccessTools.Property(__instance.GetType(), "Occupant").GetValue(__instance), __0)) return false;
            return __1 == null || ReferenceEquals(__0.equipment?.Primary, __1) ||
                (__0.inventory?.innerContainer.Contains(__1) ?? false);
        }
    }
}
