using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Maru Trap adds its own auto-rearm Command_Toggle to Building_Trap. The
    /// toggle writes the saved `autoRearm` field directly. Sync the action
    /// itself; registering a SyncField alone does not observe that write.
    /// SpawnSetup and save/load are not UI actions and remain untouched.
    /// </summary>
    internal static class Patch_MaruTrapMp
    {
        private const string PackageId = "vamv.maruracemod";
        private const string TrapTypeName = "MaruTrap.Building_Trap";
        // Action-sync reference: rwmt/Multiplayer-Compatibility contributors,
        // AlmostThere.cs (Roolo's mod), MIT. Independently adapted to Maru:
        // https://github.com/rwmt/Multiplayer-Compatibility/blob/bcffd46671bd326f1b978d421423e3c5b68305b8/Source/Mods/AlmostThere.cs
        // Target author: VAMV, https://steamcommunity.com/sharedfiles/filedetails/?id=2817638066
        // Verified against installed RW 1.6 MaruTrap.dll: ordinal 1 is the
        // toggleAction, ordinal 0 only reads autoRearm for isActive.
        private const string ToggleMethodName = "<GetGizmos>b__15_1";
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type trapType = AccessTools.TypeByName(TrapTypeName);
            MethodInfo toggle = trapType == null ? null :
                AccessTools.DeclaredMethod(trapType, ToggleMethodName, Type.EmptyTypes);
            if (toggle == null || toggle.IsStatic || toggle.ReturnType != typeof(void) ||
                !toggle.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                Log.Warning("[MP-MeowOnlineShop] Maru Trap target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(toggle, null);
                _applied = true;
                Log.Message("[MP-MeowOnlineShop] Maru Trap MP action registered: " +
                    trapType.FullName + "::" + toggle.Name);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Maru Trap toggle registration failed: " + e.Message);
            }
        }
    }
}
