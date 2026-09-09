using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Almost There! Fork (`duz.almosttherefork`) exposes a saved caravan
    /// night-rest mode through a Command_Toggle on CompNightRestControl. The
    /// toggle lambda changes the `AlmostThere` property on the clicking
    /// peer; Caravan_PostAdd_Patch later reads it during deterministic world
    /// simulation.
    ///
    /// Multiplayer's official compatibility package only covers the older
    /// `roolo.AlmostThere` and `Chad.Almostthere1.5` package IDs. Sync the
    /// actual toggle action, so consecutive clicks advance the shared mode
    /// in command order. A bare SyncField registration does not watch writes.
    /// PostAdd initialization and save/load remain outside this UI boundary.
    /// </summary>
    internal static class Patch_AlmostThereMp
    {
        private const string PackageId = "duz.almosttherefork";
        private const string CompTypeName = "CaravanDontRest.CompNightRestControl";
        // Reference: rwmt/Multiplayer-Compatibility contributors, AlmostThere.cs
        // (original mod by Roolo), MIT; action-sync approach, not copied code:
        // https://github.com/rwmt/Multiplayer-Compatibility/blob/bcffd46671bd326f1b978d421423e3c5b68305b8/Source/Mods/AlmostThere.cs
        // Fork author: Duztamva, https://steamcommunity.com/sharedfiles/filedetails/?id=3515165298
        // https://github.com/duztamva/Almost-There-Fork-1.5-/tree/b89a9c41dbe8d9797a2d8826921ba42bb73ab860
        // Both installed RW 1.6 variants (AT1.6 / AT1.6Vehicle) bind this
        // instance method to Command_Toggle.toggleAction; ordinal 0 is isActive.
        private const string ToggleMethodName = "<GetGizmos>b__5_1";
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type compType = AccessTools.TypeByName(CompTypeName);
            MethodInfo toggle = compType == null ? null :
                AccessTools.DeclaredMethod(compType, ToggleMethodName, Type.EmptyTypes);
            if (toggle == null || toggle.IsStatic || toggle.ReturnType != typeof(void) ||
                !toggle.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                Log.Warning("[MP-MeowOnlineShop] Almost There fork target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(toggle, null);
                _applied = true;
                Log.Message("[MP-MeowOnlineShop] Almost There fork MP action registered: " +
                    compType.FullName + "::" + toggle.Name);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Almost There fork toggle registration failed: " + e.Message);
            }
        }
    }
}
