using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Store simulation options in the host's save/snapshot, not in each client's local mod settings.
    public sealed class NivarianAidSettings : GameComponent
    {
        static FieldInfo enabledField, allianceField;
        public bool Enabled = true;
        public bool RequireAlliance;

        public NivarianAidSettings(Game game)
        {
            var type = AccessTools.TypeByName("Nivarian.NivarianMod");
            var settings = type == null ? null : AccessTools.Field(type, "Settings")?.GetValue(null);
            if (settings == null) return;
            Enabled = (bool)AccessTools.Field(settings.GetType(), "EnableNivarianAid").GetValue(settings);
            RequireAlliance = (bool)AccessTools.Field(settings.GetType(), "RequireAllianceForNivarianAid").GetValue(settings);
        }

        public override void ExposeData()
        {
            // Constructor values import the host's preference when upgrading a save without these keys.
            Scribe_Values.Look(ref Enabled, "meowNivarianAidEnabled", Enabled, true);
            Scribe_Values.Look(ref RequireAlliance, "meowNivarianAidRequireAlliance", RequireAlliance, true);
        }

        internal static void Install(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.NivarianSettings") ?? throw new TypeLoadException("Nivarian.NivarianSettings");
            enabledField = AccessTools.Field(type, "EnableNivarianAid") ?? throw new MissingFieldException(type.FullName, "EnableNivarianAid");
            allianceField = AccessTools.Field(type, "RequireAllianceForNivarianAid") ?? throw new MissingFieldException(type.FullName, "RequireAllianceForNivarianAid");
            foreach (var pair in new[] {
                new[] { "IncidentWorker_NivarianAidBase", "MeetsAidModSettings" },
                new[] { "IncidentWorker_FrostDrakeArrival", "CanFireNowSub" } })
            {
                var worker = AccessTools.TypeByName("Nivarian_Race.Code.Incidents." + pair[0]);
                var method = worker == null ? null : AccessTools.DeclaredMethod(worker, pair[1]);
                if (method == null) throw new MissingMethodException(pair[0], pair[1]);
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NivarianAidSettings), nameof(ReadSessionSettings)));
            }
            NivarianAidSettingsUi.Apply(harmony);
        }

        static bool ReadEnabled(object settings) => MP.IsInMultiplayer && Current.Game != null
            ? Current.Game.GetComponent<NivarianAidSettings>().Enabled : (bool)enabledField.GetValue(settings);
        static bool ReadAlliance(object settings) => MP.IsInMultiplayer && Current.Game != null
            ? Current.Game.GetComponent<NivarianAidSettings>().RequireAlliance : (bool)allianceField.GetValue(settings);

        static IEnumerable<CodeInstruction> ReadSessionSettings(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field
                    && (field == enabledField || field == allianceField))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianAidSettings), field == enabledField ? nameof(ReadEnabled) : nameof(ReadAlliance));
                    count++;
                }
                yield return instruction;
            }
            int expected = __originalMethod.Name == "MeetsAidModSettings" ? 2 : 1;
            if (count != expected) throw new InvalidOperationException("Nivarian aid settings expected " + expected + " reads, found " + count);
        }
    }
}

