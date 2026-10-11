using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

internal static class MiliraDialogDelegates
{
    static readonly HashSet<MethodInfo> allowed = new HashSet<MethodInfo>();
    static bool patched;

    internal static void Register(MethodInfo method)
    {
        if (method == null || method.IsGenericMethod || method.GetParameters().Length != 0 ||
            (method.ReturnType != typeof(void) && method.ReturnType != typeof(DiaNode)))
            throw new InvalidOperationException("Invalid Milira persistent callback target");
        if (!patched)
        {
            var gate = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.DelegateSerialization"),
                "CheckMethodAllowed", new[] { typeof(MethodInfo) }) ?? throw new MissingMethodException("MP persistent delegate gate");
            new Harmony("Meow.Milira.DialogDelegates").Patch(gate,
                prefix: new HarmonyMethod(typeof(MiliraDialogDelegates), nameof(AllowExact)));
            patched = true;
        }
        allowed.Add(method);
    }

    static bool AllowExact(MethodInfo method, ref MethodInfo __result)
    {
        if (method == null || !allowed.Contains(method)) return true;
        __result = method;
        return false;
    }
}
