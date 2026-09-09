using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    // Gameplay settings belong to the host snapshot. Local settings windows keep their disk preferences.
    public sealed class WolfeinSessionSettings : GameComponent
    {
        private bool apparelRestricted = true;
        private int minimumDays = 5, maximumDays = 10;
        private static bool hostHookInstalled;
        private static FieldInfo ApparelField => AccessTools.Field(AccessTools.TypeByName("Wolfein.WolfeinSettings"), "raceRestrictedApparel");
        public WolfeinSessionSettings(Game game) { Capture(); }
        private void Capture()
        {
            apparelRestricted = (bool?)ApparelField?.GetValue(null) ?? true;
            object settings = AccessTools.Field(AccessTools.TypeByName("WolfeinAllegiance.WolfeinAllegianceMod"), "Settings")?.GetValue(null);
            minimumDays = Math.Max(1, settings == null ? 5 : (int)AccessTools.Field(settings.GetType(), "questMinIntervalDays").GetValue(settings));
            maximumDays = Math.Max(minimumDays, settings == null ? 10 : (int)AccessTools.Field(settings.GetType(), "questMaxIntervalDays").GetValue(settings));
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref apparelRestricted, "wolfeinApparelRestricted", apparelRestricted, true);
            Scribe_Values.Look(ref minimumDays, "wolfeinMinimumQuestDays", minimumDays, true);
            Scribe_Values.Look(ref maximumDays, "wolfeinMaximumQuestDays", maximumDays, true);
        }
        private static WolfeinSessionSettings Rules => MP.IsInMultiplayer ? Current.Game?.GetComponent<WolfeinSessionSettings>() : null;
        private static void CaptureHost() => Current.Game?.GetComponent<WolfeinSessionSettings>().Capture();
        private static void InstallHostHook(Harmony harmony)
        {
            if (hostHookInstalled) return;
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.HostUtil"), "SetupGameFromSingleplayer"),
                prefix: new HarmonyMethod(typeof(WolfeinSessionSettings), nameof(CaptureHost)));
            hostHookInstalled = true;
        }
        internal static void ApplyBase(Harmony harmony)
        {
            InstallHostHook(harmony);
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Wolfein.Wolfein_RaceRestrictionSettings_Patch"), "Prefix"),
                transpiler: new HarmonyMethod(typeof(WolfeinSessionSettings), nameof(ApparelReads)));
        }
        internal static void ApplyAllegiance(Harmony harmony)
        {
            InstallHostHook(harmony);
            Type type = AccessTools.TypeByName("WolfeinAllegiance.Patch_ResistanceQuestFiring");
            harmony.Patch(AccessTools.PropertyGetter(type, "MinIntervalDays"), prefix: new HarmonyMethod(typeof(WolfeinSessionSettings), nameof(Minimum)));
            harmony.Patch(AccessTools.PropertyGetter(type, "MaxIntervalDays"), prefix: new HarmonyMethod(typeof(WolfeinSessionSettings), nameof(Maximum)));
        }
        private static bool Minimum(ref int __result) { var rules = Rules; if (rules == null) return true; __result = rules.minimumDays; return false; }
        private static bool Maximum(ref int __result) { var rules = Rules; if (rules == null) return true; __result = rules.maximumDays; return false; }
        public static bool ApparelRestriction() => Rules?.apparelRestricted ?? ((bool?)ApparelField?.GetValue(null) ?? true);
        private static IEnumerable<CodeInstruction> ApparelReads(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.LoadsField(ApparelField))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(WolfeinSessionSettings), nameof(ApparelRestriction));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Wolfein apparel setting read changed: " + count);
        }
    }
}
