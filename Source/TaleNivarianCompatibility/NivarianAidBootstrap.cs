using System;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Aid must remain synchronized even if an unrelated race/visual initializer fails.
    // Do not force either of the general Bootstrap constructors from this owner.
    [StaticConstructorOnStartup]
    internal static class NivarianAidBootstrap
    {
        static NivarianAidBootstrap()
        {
            if (!MP.enabled || !ModsConfig.IsActive("keeptpa.NivarianRace") ||
                !MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("nivarian")) return;
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        static void Install()
        {
            var harmony = new Harmony("meow.tale-nivarian.aid");
            // Install the local-target -> map-command boundary first. Later failures
            // must never restore the native UI callback that generates pawns locally.
            InstallPart("reinforcement targeting", () => NivarianReinforcementTarget.Apply(harmony));
            InstallPart("aid decisions", () =>
            {
                var letter = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianAid");
                var accept = NivarianUpdateActions.RequiredMethod(letter, "ExecuteAccept");
                if (Harmony.GetPatchInfo(accept)?.Owners.Contains("meow.trio.nivarian-aid-events") != true)
                    NivarianAidEvents.Apply(harmony);
            });
            InstallPart("letter clock", () => NivarianLetterClock.Apply(harmony));
        }

        static void InstallPart(string name, Action install)
        {
            try
            {
                install();
                Log.Message("[NivarianAidBootstrap] installed " + name);
            }
            catch (Exception exception)
            {
                Log.Error("[NivarianAidBootstrap] REQUIRED_TARGET_FAILED " + name + ": " + exception);
            }
        }
    }
}
