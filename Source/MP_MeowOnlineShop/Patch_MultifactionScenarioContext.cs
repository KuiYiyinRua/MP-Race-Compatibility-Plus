using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_MultifactionScenarioContext
    {
        private sealed class Draft
        {
            internal Game Game;
            internal Scenario Scenario;
        }

        private sealed class Scope
        {
            internal Game Game;
            internal Scenario Previous;
        }

        private static readonly ConditionalWeakTable<Window, Draft> drafts = new ConditionalWeakTable<Window, Draft>();
        private static FieldInfo chosenScenario;

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled) return;
            try
            {
                var sidebar = AccessTools.TypeByName("Multiplayer.Client.FactionSidebar")
                    ?? throw new TypeLoadException("Multiplayer.Client.FactionSidebar");
                var open = AccessTools.DeclaredMethod(sidebar, "OpenConfigurationPages", Type.EmptyTypes)
                    ?? throw new MissingMethodException(sidebar.FullName, "OpenConfigurationPages");
                chosenScenario = AccessTools.DeclaredField(sidebar, "chosenScenario");
                if (chosenScenario == null || !chosenScenario.IsStatic || chosenScenario.FieldType != typeof(ScenarioDef))
                    throw new MissingFieldException(sidebar.FullName, "chosenScenario");

                var constructors = new List<ConstructorInfo>();
                var methods = new HashSet<MethodInfo>();
                foreach (string name in new[] { "Page_ChooseIdeo_Multifaction", "Page_ConfigureStartingPawns_Multifaction" })
                {
                    var type = AccessTools.TypeByName("Multiplayer.Client.Factions." + name)
                        ?? throw new TypeLoadException(name);
                    if (!typeof(Window).IsAssignableFrom(type)) throw new InvalidOperationException(name + " is not a window");
                    constructors.Add(AccessTools.DeclaredConstructor(type, Type.EmptyTypes)
                        ?? throw new MissingMethodException(name, ".ctor"));
                    // Include inherited page methods. The weak-table guard leaves
                    // ordinary new-game pages and all other windows untouched.
                    foreach (string methodName in new[] { "PreOpen", "PostOpen", "DoWindowContents", "PreClose", "PostClose",
                        "CanDoNext", "CanDoBack", "DoNext", "DoBack", "OnAcceptKeyPressed", "OnCancelKeyPressed" })
                    {
                        var method = AccessTools.Method(type, methodName)
                            ?? throw new MissingMethodException(name, methodName);
                        // Harmony requires the declaration, not an inherited
                        // MethodInfo whose ReflectedType is the derived MP page.
                        method = AccessTools.DeclaredMethod(method.DeclaringType, method.Name,
                            method.GetParameters().Select(p => p.ParameterType).ToArray())
                            ?? throw new MissingMethodException(name, methodName + " declaration");
                        methods.Add(method);
                    }
                }

                foreach (var ctor in constructors)
                    harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(Patch_MultifactionScenarioContext), nameof(RememberDraft)));
                foreach (var method in methods)
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(typeof(Patch_MultifactionScenarioContext), nameof(PagePrefix)) { priority = Priority.First },
                        finalizer: new HarmonyMethod(typeof(Patch_MultifactionScenarioContext), nameof(Restore)) { priority = Priority.Last });
                harmony.Patch(open,
                    prefix: new HarmonyMethod(typeof(Patch_MultifactionScenarioContext), nameof(OpenPrefix)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(Patch_MultifactionScenarioContext), nameof(Restore)) { priority = Priority.Last });
                Log.Message("[MP-MeowOnlineShop] Multifaction setup scenario is scoped to configuration calls; page targets=" + methods.Count + ".");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED TARGET FAILED multifaction scenario context: " + e);
            }
        }

        private static void RememberDraft(Window __instance)
        {
            if (!MP.IsInMultiplayer || Current.Game == null) return;
            var scenario = (chosenScenario.GetValue(null) as ScenarioDef)?.scenario;
            if (scenario == null) return;
            drafts.Add(__instance, new Draft { Game = Current.Game, Scenario = scenario });
        }

        private static void OpenPrefix(ref Scope __state)
        {
            if (!MP.IsInMultiplayer || Current.Game == null) return;
            // InitializeDataForGameConfigurationPages assigns the chosen scenario
            // directly to Current.Game. Keep it only through construction/PreOpen.
            __state = new Scope { Game = Current.Game, Previous = Current.Game.Scenario };
        }

        private static void PagePrefix(Window __instance, ref Scope __state)
        {
            if (!MP.IsInMultiplayer || !drafts.TryGetValue(__instance, out var draft) ||
                !ReferenceEquals(Current.Game, draft.Game)) return;
            __state = new Scope { Game = draft.Game, Previous = draft.Game.Scenario };
            draft.Game.Scenario = draft.Scenario;
        }

        private static Exception Restore(Exception __exception, Scope __state)
        {
            if (__state != null) __state.Game.Scenario = __state.Previous;
            return __exception;
        }
    }
}
