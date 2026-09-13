using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350RjwLoadTicksMp
    {
        private static bool installed;
        private static MethodInfo conditions;
        internal static void Apply(Harmony harmony)
        {
            if (installed || !MP.enabled || !ModsConfig.IsActive("rim.job.world")) return;
            try
            {
                Type driver = AccessTools.TypeByName("rjw.JobDriver_Sex") ?? throw new TypeLoadException("rjw.JobDriver_Sex");
                MethodInfo setup = AccessTools.DeclaredMethod(driver, "setup_ticks", Type.EmptyTypes);
                conditions = AccessTools.DeclaredMethod(driver, "ConditionsToAbortSex", Type.EmptyTypes);
                if (!typeof(JobDriver).IsAssignableFrom(driver) || setup == null || setup.ReturnType != typeof(void) || conditions == null || conditions.ReturnType != typeof(List<Func<bool>>))
                    throw new InvalidOperationException("RJW setup/conditions shape changed");
                var guards = new List<MethodInfo>();
                AddGuard(guards, "abscon.privacy.please", "Privacy_Please.HarmonyPatch_JobDriver_Sex_setup_ticks", driver.MakeByRefType());
                AddGuard(guards, "c0ffee.rimworld.animations", "Rimworld_Animations.HarmonyPatch_JobDriver_Sex", driver);
                harmony.Patch(setup, prefix: new HarmonyMethod(typeof(Patch_Light350RjwLoadTicksMp), nameof(SetupPrefix)) { priority = Priority.First });
                foreach (MethodInfo guard in guards)
                    harmony.Patch(guard, prefix: new HarmonyMethod(typeof(Patch_Light350RjwLoadTicksMp), nameof(InitializationPostfixPrefix)) { priority = Priority.First });
                installed = true;
                Log.Message("[MP-MeowOnlineShop][Light350-B12] RJW: load-time counters preserved, conditions rebuilt, initialization postfix guards=" + guards.Count + ".");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][Light350-B12] REQUIRED TARGET FAILED RJW load ticks: " + e);
            }
        }

        private static void AddGuard(List<MethodInfo> guards, string package, string typeName, Type argument)
        {
            if (!ModsConfig.IsActive(package)) return;
            Type type = AccessTools.TypeByName(typeName) ?? throw new TypeLoadException(typeName);
            MethodInfo postfix = AccessTools.DeclaredMethod(type, "Postfix", new[] { argument }) ?? throw new MissingMethodException(typeName, "Postfix");
            if (!postfix.IsStatic || postfix.ReturnType != typeof(void)) throw new InvalidOperationException("RJW initialization postfix changed");
            guards.Add(postfix);
        }

        private static bool PreserveLoadedState => MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.PostLoadInit;
        private static bool InitializationPostfixPrefix() => !PreserveLoadedState;

        private static bool SetupPrefix(JobDriver __instance)
        {
            if (!PreserveLoadedState) return true;
            // Do not run the virtual duration/orgasm initialization: downstream
            // postfixes can mutate hediffs or generate pawns before counters could
            // be restored. Only rebuild the original local failure delegates.
            var predicates = (List<Func<bool>>)conditions.Invoke(__instance, null);
            foreach (Func<bool> predicate in predicates) __instance.AddFailCondition(predicate);
            return false;
        }
    }
}
