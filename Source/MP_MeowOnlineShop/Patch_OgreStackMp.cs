using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// OgreStack rewrites `ThingDef.stackLimit` and calls
    /// `ResourceCounter.ResetDefs()` both at startup and whenever the mod
    /// settings window changes values. In multiplayer, a settings change on
    /// one peer would rewrite the DefDatabase and resource counters on that
    /// peer only, producing a permanent simulation divergence.
    ///
    /// Startup application is safe because every peer loads the same mod
    /// config and the same Defs. The patch therefore suppresses only the
    /// runtime re-modification while connected to Multiplayer, leaving startup
    /// behavior unchanged.
    /// </summary>
    internal static class Patch_OgreStackMp
    {
        private const string PackageId = "ogre.ogrestack";
        private const string ModTypeName = "OgreStack.OgreStackMod";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type modType = AccessTools.TypeByName(ModTypeName);
            MethodInfo modifyStackSizes = modType == null
                ? null
                : AccessTools.Method(modType, "ModifyStackSizes", Type.EmptyTypes);
            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_OgreStackMp),
                nameof(ModifyStackSizesPrefix));

            if (modifyStackSizes == null || prefix == null)
            {
                Log.Warning("[MP-MeowOnlineShop] OgreStack target resolution failed; patch skipped.");
                return;
            }

            try
            {
                harmony.Patch(modifyStackSizes, prefix: new HarmonyMethod(prefix));
                Log.Message("[MP-MeowOnlineShop] OgreStack MP patch active: runtime stack re-modification suppressed in multiplayer.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] OgreStack patch failed: " + e.Message);
            }
        }

        private static bool ModifyStackSizesPrefix()
        {
            return !MP.IsInMultiplayer;
        }
    }
}
