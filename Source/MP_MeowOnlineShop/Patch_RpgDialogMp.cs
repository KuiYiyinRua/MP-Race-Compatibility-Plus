using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RPG Dialog may automatically open a newly received ChoiceLetter from a
    /// local UI postfix. In Multiplayer that can create a PersistentDialog on
    /// one peer only and consume a synchronized unique ID. Keep letter/quest
    /// creation and manual opening intact, but suppress local auto-open in MP.
    /// </summary>
    internal static class Patch_RpgDialogMp
    {
        private const string PatchTypeName =
            "RPGDialog.LetterStack_ReceiveLetter_Patch";

        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName(PatchTypeName);
            if (type == null)
                return;

            var target = AccessTools.Method(
                type, "Postfix", new[] { typeof(Letter) });
            var prefix = AccessTools.Method(
                typeof(Patch_RpgDialogMp), nameof(AutoOpenPostfixPrefix));

            if (target == null || prefix == null || !target.IsStatic ||
                target.ReturnType != typeof(void) ||
                target.GetParameters().Length != 1)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] RPG Dialog ReceiveLetter postfix " +
                    "signature drift; multiplayer auto-open guard was not installed.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix)
            {
                priority = Priority.First
            });

            Log.Message(
                "[MP-MeowOnlineShop] RPG Dialog MP guard active: newly received " +
                "ChoiceLetters are not auto-opened in multiplayer; manual opening remains.");
        }

        private static bool AutoOpenPostfixPrefix()
        {
            return !MP.IsInMultiplayer;
        }
    }
}
