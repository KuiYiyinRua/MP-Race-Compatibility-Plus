using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Synchronizes Cinders of the Embergarden's secondary-verb weapon mode
    /// button (for example Rocket/Minigun). The mod's Command_Action points
    /// directly at a private parameterless SwitchVerb method, so the click
    /// otherwise mutates the equipped verb only on the local peer.
    /// </summary>
    internal static class Patch_EmbergardenWeaponSwitchMp
    {
        private const string CompTypeName = "Embergarden.CompSecondaryVerb";

        private static Type _compType;
        private static MethodInfo _switchVerbMethod;
        private static bool _syncRegistered;
        private static int _dispatchLogCount;
        private static int _replayLogCount;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            _compType = AccessTools.TypeByName(CompTypeName);
            _switchVerbMethod = _compType == null
                ? null
                : AccessTools.Method(_compType, "SwitchVerb", Type.EmptyTypes);

            if (_compType == null || _switchVerbMethod == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Embergarden weapon mode sync skipped " +
                    "(CompSecondaryVerb.SwitchVerb target was not resolved).");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(
                        typeof(Patch_EmbergardenWeaponSwitchMp),
                        nameof(SyncSwitchVerb))
                    .SetContext(SyncContext.CurrentMap)
                    .CancelIfAnyArgNull();
                _syncRegistered = true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Embergarden weapon mode sync registration " +
                    "failed: " + e.Message);
                return;
            }

            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_EmbergardenWeaponSwitchMp),
                nameof(SwitchVerbPrefix));
            if (prefix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Embergarden weapon mode sync skipped " +
                    "(prefix target was not resolved).");
                return;
            }

            harmony.Patch(
                _switchVerbMethod,
                prefix: new HarmonyMethod(prefix)
                {
                    priority = Priority.First
                });

            Log.Message(
                "[MP-MeowOnlineShop] Embergarden weapon mode sync active: " +
                "Embergarden.CompSecondaryVerb.SwitchVerb -> SyncSwitchVerb(ThingComp), " +
                "context=CurrentMap.");
        }

        private static bool SwitchVerbPrefix(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer ||
                MP.IsExecutingSyncCommand ||
                !_syncRegistered ||
                __instance == null ||
                _compType == null ||
                !_compType.IsInstanceOfType(__instance))
            {
                return true;
            }

            if (_dispatchLogCount < 8)
            {
                _dispatchLogCount++;
                Log.Message(
                    "[MP-MeowOnlineShop] Embergarden weapon mode click dispatched " +
                    "through multiplayer: parent=" +
                    (__instance.parent?.thingIDNumber ?? 0) + ".");
            }

            SyncSwitchVerb(__instance);
            return false;
        }

        public static void SyncSwitchVerb(ThingComp comp)
        {
            if (comp == null ||
                _switchVerbMethod == null ||
                _compType == null ||
                !_compType.IsInstanceOfType(comp))
            {
                return;
            }

            if (_replayLogCount < 8)
            {
                _replayLogCount++;
                Log.Message(
                    "[MP-MeowOnlineShop] Embergarden weapon mode replay: parent=" +
                    (comp.parent?.thingIDNumber ?? 0) + ".");
            }

            try
            {
                // During sync-command replay SwitchVerbPrefix allows the
                // original method through, preserving the mod's full state
                // transition (verb props, selected flag, burst cache, sound).
                _switchVerbMethod.Invoke(comp, null);
            }
            catch (Exception e)
            {
                Log.Error(
                    "[MP-MeowOnlineShop] Embergarden weapon mode replay failed: " +
                    e.GetBaseException().Message);
            }
        }
    }
}
