using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RJWPE Voice Patch selects a replacement with Verse.Rand from an animation
    /// sound callback. Sound playback is local presentation state and can be
    /// skipped on an off-screen or muted peer, so its random draw must not
    /// advance Multiplayer's synchronized map Rand stream.
    /// </summary>
    internal static class Patch_RjwPeVoiceRandIsolation
    {
        private const string PackageId = "rim.job.world.pe";
        private const string VoiceHelperTypeName =
            "rjwpe.VoicePatch.Helpers.VoiceHelper";
        private const string VoiceDefTypeName = "Rimworld_Animations.VoiceDef";

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            var helperType = AccessTools.TypeByName(VoiceHelperTypeName);
            var voiceDefType = AccessTools.TypeByName(VoiceDefTypeName);
            var target = helperType == null || voiceDefType == null
                ? null
                : AccessTools.Method(
                    helperType,
                    "GetVoiceReplacement",
                    new[] { voiceDefType, typeof(Pawn) });

            if (target == null || !target.IsStatic ||
                target.ReturnType != voiceDefType)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJWPE-Voice] GetVoiceReplacement signature " +
                    "not found; cosmetic Rand isolation was not applied.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwPeVoiceRandIsolation),
                        nameof(Prefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwPeVoiceRandIsolation),
                        nameof(Finalizer)))
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop][RJWPE-Voice] cosmetic replacement selection " +
                "no longer consumes synchronized map Rand.");
        }

        private static void Prefix(ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;

            Rand.PushState();
            __state = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }
    }
}
