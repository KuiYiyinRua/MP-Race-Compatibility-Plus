using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Gameplay values belong to the shared save; local mod preferences remain local.
    // Only behavior switches are shared; visual and audio preferences remain local.
    public sealed class NivarianGameplaySwitches : GameComponent
    {
        static readonly string[] Names = {
            "EnableCryoSlowResistance",
            "CutosDoCookExplosion",
            "RemoveNegativeTraitsOnGenerate",
            "EnableNiraFeaturesWithoutStoryteller",
            "EnableGlobalDraftBinding",
            "EnableNivarianFriendlyFireGoodwillImmunity",
            "BeaconNoSpawnInterval",
            "NiraRankLocksStorytellerParams",
        };
        static FieldInfo settingsField;
        static FieldInfo[] fields;

        static ISyncMethod change;
        static UiState activeUi;
        readonly bool[] values = new bool[Names.Length];
        readonly bool available;

        public NivarianGameplaySwitches(Game game)
        {
            Resolve();
            if (settingsField == null) return; // Component also exists when this mod is inactive.
            available = true;
            var settings = settingsField.GetValue(null);
            if (settings != null)
                for (int i = 0; i < values.Length; i++) values[i] = (bool)fields[i].GetValue(settings);
        }

        static void Resolve()
        {
            if (fields != null) return;
            var mod = AccessTools.TypeByName("Nivarian.NivarianMod");
            if (mod == null) return;
            settingsField = AccessTools.Field(mod, "Settings") ?? throw new MissingFieldException(mod.FullName, "Settings");
            fields = Names.Select(n => AccessTools.Field(settingsField.FieldType, n)
                ?? throw new MissingFieldException(settingsField.FieldType.FullName, n)).ToArray();
            foreach (var field in fields) if (field.FieldType != typeof(bool)) throw new InvalidOperationException(field.Name + " is no longer bool");
        }

        public override void ExposeData()
        {
            // A Tale-only save must not seed zero-valued Nivarian settings when the race is later enabled.
            if (!available) return;
            // Missing keys import the host preference when an older save is first upgraded.
            for (int i = 0; i < values.Length; i++)
                Scribe_Values.Look(ref values[i], "meowShared_" + Names[i], values[i], true);
        }

        internal static void Apply(Harmony harmony)
        {
            Resolve();
            foreach (var target in new[] {
                new[] { "Nivarian.Hediff_IcyFronzen", "Tick" },
                new[] { "Nivarian.MapComp_StoveExplosionEff", "MapComponentTick" },
                new[] { "Nivarian.QuestNode_SpawnNivarianGroup", "CustomizePawn" },
                new[] { "Nivarian.Helper.NivarianHelper", "IsNiraStorytellerActive" },
                new[] { "Nivarian_Race.Code.UI.Nivarian_Command_Toggle_DraftBinding", "get_IsLocked" },
                new[] { "Nivarian_Race.Code.UI.Nivarian_Command_Toggle_DraftBinding", "GetLockedReason" },
                new[] { "Nivarian_Race.Code.UI.UplinkNiraMetricsTabPanel", "DrawIntSlider" },
                new[] { "Nivarian_Race.Code.UI.UplinkNiraMetricsTabPanel", "DrawFloatSlider" },
                new[] { "Nivarian_Race.Code.Patches.NivarianFriendlyFireGoodwillHelper", "ShouldSuppress" },
                new[] { "Nivarian_Race.Code.Patches.Patch_PawnGenerator_SanitizeNivarian+GeneratePawn_Postfix", "Postfix" },
                new[] { "Nivarian_Race.Code.GameComponent.GameComp_NivarianDraftSettings", "ApplySetting" },
                new[] { "Nivarian_Race.Code.Comps.BuildingComps.Comp_ContiniumBeacon", "CompTick" },
            })
            {
                var type = AccessTools.TypeByName(target[0]) ?? throw new TypeLoadException(target[0]);
                harmony.Patch(AccessTools.DeclaredMethod(type, target[1]) ?? throw new MissingMethodException(target[0], target[1]),
                    transpiler: new HarmonyMethod(typeof(NivarianGameplaySwitches), nameof(ReadShared)));
            }
            change = MP.RegisterSyncMethod(typeof(NivarianGameplaySwitches), nameof(Change));
            harmony.Patch(AccessTools.DeclaredMethod(settingsField.DeclaringType, "DoSettingsWindowContents"),
                prefix: new HarmonyMethod(typeof(NivarianGameplaySwitches), nameof(BeginUi)),
                finalizer: new HarmonyMethod(typeof(NivarianGameplaySwitches), nameof(EndUi)));
            harmony.Patch(AccessTools.DeclaredMethod(settingsField.FieldType, "ExposeData")
                ?? throw new MissingMethodException(settingsField.FieldType.FullName, "ExposeData"),
                prefix: new HarmonyMethod(typeof(NivarianGameplaySwitches), nameof(BeginSettingsSave)),
                finalizer: new HarmonyMethod(typeof(NivarianGameplaySwitches), nameof(EndSettingsSave)));
            Log.Message("[TaleNivarianCompat] 8 gameplay switches use shared save values and synchronized UI changes.");
        }

        static NivarianGameplaySwitches Shared => MP.IsInMultiplayer ? Current.Game?.GetComponent<NivarianGameplaySwitches>() : null;
        static bool Read(object localSettings, int index) => Shared is NivarianGameplaySwitches shared
            ? shared.values[index] : (bool)fields[index].GetValue(localSettings);

        static IEnumerable<CodeInstruction> ReadShared(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                int index = instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo f ? Array.IndexOf(fields, f) : -1;
                if (index < 0) { yield return instruction; continue; }
                var call = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NivarianGameplaySwitches), nameof(Read)));
                // Preserve branch targets and exception boundaries around the original field load.
                foreach (var block in instruction.blocks.Where(b => b.blockType == ExceptionBlockType.EndExceptionBlock).ToArray())
                { instruction.blocks.Remove(block); call.blocks.Add(block); }
                instruction.opcode = OpCodes.Ldc_I4; instruction.operand = index;
                yield return instruction; yield return call; replaced++;
            }
            int expected = 1;
            if (replaced != expected) throw new InvalidOperationException(__originalMethod.Name + " shared settings reads: " + replaced + ", expected " + expected);
        }

        sealed class UiState
        {
            internal object Settings;
            internal bool[] Local, Before;
            internal UiState Parent;
        }
        static void BeginUi(out UiState __state)
        {
            __state = null;
            var shared = Shared;
            if (!MP.InInterface || shared == null) return;
            var settings = settingsField.GetValue(null);
            __state = new UiState { Settings = settings, Local = fields.Select(f => (bool)f.GetValue(settings)).ToArray(), Before = (bool[])shared.values.Clone(), Parent = activeUi };
            activeUi = __state;
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(settings, __state.Before[i]);
        }
        static Exception EndUi(Exception __exception, UiState __state)
        {
            if (__state == null) return __exception;
            activeUi = __state.Parent;
            var indices = new List<int>(); var updates = new List<bool>();
            for (int i = 0; i < fields.Length; i++)
            {
                bool next = (bool)fields[i].GetValue(__state.Settings);
                fields[i].SetValue(__state.Settings, __state.Local[i]);
                if (next != __state.Before[i]) { indices.Add(i); updates.Add(next); }
            }
            if (__exception == null && indices.Count != 0) change.DoSync(null, indices.ToArray(), updates.ToArray());
            return __exception;
        }
        // ResetAll clears read notifications and calls ModSettings.Write while the UI is
        // still displaying shared values. Serialize local preferences, then restore the
        // pending UI delta even if the serializer throws. GameComponent saves are separate.
        static void BeginSettingsSave(object __instance, out bool[] __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving) return;
            UiState local = null;
            for (var frame = activeUi; frame != null; frame = frame.Parent)
                if (ReferenceEquals(frame.Settings, __instance)) local = frame;
            if (local == null) return;
            __state = fields.Select(f => (bool)f.GetValue(__instance)).ToArray();
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__instance, local.Local[i]);
        }
        static Exception EndSettingsSave(object __instance, Exception __exception, bool[] __state)
        {
            if (__state != null)
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__instance, __state[i]);
            return __exception;
        }
        static void Change(int[] indices, bool[] updates)
        {
            var shared = Shared;
            if (shared == null || indices == null || updates == null || indices.Length != updates.Length || indices.Length > Names.Length) return;
            // Validate the whole delta before changing any shared value.
            for (int i = 0; i < indices.Length; i++)
                if (indices[i] < 0 || indices[i] >= Names.Length) return;
            for (int i = 0; i < indices.Length; i++) shared.values[indices[i]] = updates[i];
        }
    }
}
