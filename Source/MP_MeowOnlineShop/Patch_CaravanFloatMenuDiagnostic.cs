using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-127: a synced caravan float-menu command threw
    /// `NullReferenceException` inside
    /// `CaravanArrivalActionUtility.<GetFloatMenuOptions>b__0` during replay on
    /// the client, and the world desynced 44 ticks later. The concrete
    /// `CaravanArrivalAction` type is erased from the generic stack, so this
    /// bounded diagnostic records it from the closure's generic argument.
    ///
    /// This patch is diagnostic only: it does not change any simulation
    /// behavior. It patches every `WorldObject.GetFloatMenuOptions(Caravan)`
    /// override and logs each unique concrete caravan arrival action type once
    /// per session so the next desync bundle identifies the failing mod path.
    /// </summary>
    internal static class Patch_CaravanFloatMenuDiagnostic
    {
        private static readonly HashSet<string> LoggedActionTypes =
            new HashSet<string>();

        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_CaravanFloatMenuDiagnostic),
                    nameof(GetFloatMenuOptionsPostfix));
                if (postfix == null)
                    return;

                int patched = 0;
                foreach (Type type in AppDomain.CurrentDomain.GetAssemblies()
                             .SelectMany(asm =>
                             {
                                 try
                                 {
                                     return asm.GetTypes();
                                 }
                                 catch
                                 {
                                     return Type.EmptyTypes;
                                 }
                             })
                             .Where(t => t != null &&
                                         !t.IsAbstract &&
                                         typeof(WorldObject).IsAssignableFrom(t)))
                {
                    MethodInfo method = AccessTools.Method(
                        type,
                        "GetFloatMenuOptions",
                        new[] { typeof(Caravan) });
                    if (method == null || method.IsAbstract)
                        continue;

                    harmony.Patch(
                        method,
                        postfix: new HarmonyMethod(postfix)
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }

                if (patched == 0)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Caravan float-menu diagnostic skipped " +
                        "(no WorldObject.GetFloatMenuOptions overrides resolved).");
                    return;
                }

                Log.Message(
                    "[MP-MeowOnlineShop] Caravan float-menu action-type diagnostic " +
                    $"active (overrides patched={patched}).");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Caravan float-menu diagnostic failed: " +
                    e.Message);
            }
        }

        private static void GetFloatMenuOptionsPostfix(
            IEnumerable<FloatMenuOption> __result)
        {
            if (!MP.IsInMultiplayer || __result == null)
                return;

            try
            {
                foreach (FloatMenuOption option in __result)
                {
                    if (option?.action == null)
                        continue;

                    Type actionType = TryGetConcreteArrivalActionType(option);
                    if (actionType == null)
                        continue;

                    string fullName = actionType.FullName ?? actionType.Name;
                    if (!LoggedActionTypes.Add(fullName))
                        continue;

                    Log.Message(
                        "[MP-MeowOnlineShop][CaravanMenu] concrete caravan arrival " +
                        $"action type={fullName}.");
                }
            }
            catch
            {
                // Diagnostic only; never let this affect UI.
            }
        }

        private static Type TryGetConcreteArrivalActionType(FloatMenuOption option)
        {
            object closure = option.action.Target;
            if (closure == null)
                return null;

            Type closureType = closure.GetType();
            if (!closureType.IsGenericType ||
                !closureType.GetGenericTypeDefinition().FullName.Contains(
                    "CaravanArrivalActionUtility"))
            {
                return null;
            }

            // DisplayClass0_1 wraps the inner action in its "action" field;
            // DisplayClass0_0 is the inner action itself. Both are generic
            // over the concrete CaravanArrivalAction type.
            if (closureType.Name.Contains("DisplayClass0_1"))
            {
                try
                {
                    FieldInfo innerField = AccessTools.Field(closureType, "action");
                    object inner = innerField?.GetValue(closure);
                    closure = inner;
                    closureType = closure?.GetType();
                }
                catch
                {
                    return null;
                }
            }

            if (closureType == null || !closureType.IsGenericType)
                return null;

            Type[] args = closureType.GetGenericArguments();
            return args.Length == 1 ? args[0] : null;
        }
    }
}
