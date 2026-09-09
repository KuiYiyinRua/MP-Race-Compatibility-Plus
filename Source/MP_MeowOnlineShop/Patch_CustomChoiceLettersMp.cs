using System;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Oberonia and Sylvie add custom ChoiceLetter subclasses whose accept and
    /// refuse options mutate simulation directly. Multiplayer only registers
    /// built-in choice letters, so the two action lambdas in each custom
    /// Choices getter are registered with the official lambda-in-getter API.
    /// The remaining "view pawn info" style options are local UI only.
    /// </summary>
    internal static class Patch_CustomChoiceLettersMp
    {
        private static int _registered;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            RegisterLetter(
                "oark.oberoniaaurea.framework",
                "OberoniaAurea.ChoiceLetter_AcceptJoinerViewInfo",
                "Choices",
                0,
                useGetter: true);
            RegisterLetter(
                "oark.oberoniaaurea.framework",
                "OberoniaAurea.ChoiceLetter_AcceptJoinerViewInfo",
                "Choices",
                1,
                useGetter: true);
            RegisterLetter(
                "aifeng.sylvierace",
                "SylvieMod.ChoiceLetter_SylvieOffer",
                "CreateBuyOption",
                0,
                useGetter: false);
            RegisterLetter(
                "aifeng.sylvierace",
                "SylvieMod.ChoiceLetter_SylvieOffer",
                "CreateRefuseOption",
                0,
                useGetter: false);

            if (_registered > 0)
                Log.Message("[MP-MeowOnlineShop] Custom ChoiceLetter MP patch active: " + _registered + " lambdas.");
        }

        private static void RegisterLetter(
            string packageId,
            string typeName,
            string parentMethod,
            int ordinal,
            bool useGetter)
        {
            if (!ModsConfig.IsActive(packageId))
                return;

            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
                return;

            try
            {
                if (useGetter)
                    MP.RegisterSyncMethodLambdaInGetter(type, parentMethod, ordinal);
                else
                    MP.RegisterSyncMethodLambda(type, parentMethod, ordinal);
                _registered++;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Custom ChoiceLetter lambda registration failed on " +
                    typeName + "::" + parentMethod + "#" + ordinal + ": " + e.Message);
            }
        }
    }
}
