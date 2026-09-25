using System;
using HarmonyLib;
using Multiplayer.API;

namespace Meow.DesyncBatchCompatibility
{
    internal static class ExpandableProjectileRate
    {
        internal static void Apply(Harmony harmony)
        {
            var type = Bootstrap.Type("VEF.Weapons.ExpandableProjectile");
            var getter = AccessTools.DeclaredPropertyGetter(type, "UpdateRateTicks");
            if (getter == null || getter.ReturnType != typeof(int))
                throw new MissingMethodException(type.FullName, "get_UpdateRateTicks");
            harmony.Patch(getter, prefix: new HarmonyMethod(typeof(ExpandableProjectileRate), nameof(Rate)));
        }

        // VEF bypasses MP's VTR: the viewed map gets 1, other maps get a larger
        // interval. curDuration advances once per invocation, not by delta.
        // Desync-164/165/169..171: collision/fade/destruction diverge accordingly.
        internal static bool Rate(ref int __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = 1;
            return false;
        }
    }
}
