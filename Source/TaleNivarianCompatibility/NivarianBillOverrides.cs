using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianBillOverrides
    {
        internal static void Apply()
        {
            var forge = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompCryoForge")
                ?? throw new TypeLoadException("Nivarian CompCryoForge");
            // This override never calls the registered base RemoveQueue. It also resets
            // _inProgressBill/WorkDone and rebuilds hauling requirements.
            var remove = AccessTools.DeclaredMethod(forge, "RemoveQueue", new[] { typeof(int) })
                ?? throw new MissingMethodException(forge.FullName, "RemoveQueue(int)");
            MP.RegisterSyncMethod(remove);
            // The forge's drag/drop override also bypasses the registered base method.
            var move = AccessTools.DeclaredMethod(forge, "MoveQueue", new[] { typeof(int), typeof(int) })
                ?? throw new MissingMethodException(forge.FullName, "MoveQueue(int,int)");
            MP.RegisterSyncMethod(move);
            Log.Message("[TaleNivarianCompat] cryoforge RemoveQueue/MoveQueue overrides synchronized, including work state and hauling requirements.");
        }
    }
}
