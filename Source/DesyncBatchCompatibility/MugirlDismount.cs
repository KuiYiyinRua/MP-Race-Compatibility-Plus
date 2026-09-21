using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class MugirlDismount
    {
        internal static void Apply(Harmony harmony)
        {
            var type = Bootstrap.Type("Mugirl.Comp_MugirlMount");
            var method = Bootstrap.Method(type, "TryDismount", typeof(IntVec3?), typeof(bool));
            if (!typeof(ThingComp).IsAssignableFrom(type) || method.ReturnType != typeof(bool))
                throw new InvalidOperationException("Mugirl dismount signature changed");

            // Both the rider and carrier gizmos reach this method. MP serializes
            // ThingComp by its parent/type and supports Nullable<IntVec3>. Its
            // SyncTemplates bypass interception while ticking/replaying, so
            // automatic safety dismounts still execute synchronously in the tick.
            // The UI callbacks discard the queued call's default bool return.
            MP.RegisterSyncMethod(method, null).SetContext(SyncContext.CurrentMap);
        }
    }
}
