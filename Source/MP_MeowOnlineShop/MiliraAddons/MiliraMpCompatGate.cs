using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat
{
    /// <summary>
    /// Avoids double-patching when the original Milira_MP reference mod
    /// (usamiseika.fixmod.miliramultiplayer) is active in the same loadout.
    /// The ported addon modules skip themselves so the reference owns those targets.
    /// </summary>
    internal static class MiliraMpCompatGate
    {
        private static bool? _referenceModActive;

        internal static bool ReferenceModActive
        {
            get
            {
                if (!_referenceModActive.HasValue)
                    _referenceModActive = ModsConfig.IsActive("usamiseika.fixmod.miliramultiplayer");
                return _referenceModActive.Value;
            }
        }
    }
}
