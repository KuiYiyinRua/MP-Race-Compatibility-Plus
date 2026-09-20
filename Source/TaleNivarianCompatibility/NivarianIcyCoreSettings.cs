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
    // Keep the original component type name and keys so earlier shared saves still load.
    public sealed class NivarianIcyCoreSettings : GameComponent
    {
        static readonly string[] Names = {
            "IcyCoreBaseDecayInComfort", "IcyCoreFallPerDegreeAboveMax",
            "IcyCoreDeltaChangeBelowZero", "IcyCoreDeltaChangeBelowMeltMin",
            "IcyCoreSmoothFactorBelowZero", "IcyCoreSmoothFactorBelowMeltMin",
            "IcyCoreSmoothFactorInMeltRange", "IcyCoreSmoothFactorAboveMeltMax",
            "IcyCoreMaxPenaltyCap", "IcyCoreHediffSeverityMin", "IcyCoreHediffSeverityMax",
            "IcyCoreLuminisBonusThreshold", "IcyCoreInitialLevel",
            "MainStoryDifficulty", "BeaconRechargeRateMultiplier"
        };
        static FieldInfo settingsField;
        static FieldInfo[] fields;
        static float[] minima, maxima;
        static ISyncMethod change;
        static UiState activeUi;
        readonly float[] values = new float[Names.Length];
        readonly bool available;

        public NivarianIcyCoreSettings(Game game)
        {
            Resolve();
            if (settingsField == null) return; // Component also exists when this mod is inactive.
            available = true;
            var settings = settingsField.GetValue(null);
            if (settings != null)
                for (int i = 0; i < values.Length; i++) values[i] = (float)fields[i].GetValue(settings);
        }

        static void Resolve()
        {
            if (fields != null) return;
            var mod = AccessTools.TypeByName("Nivarian.NivarianMod");
            if (mod == null) return;
            settingsField = AccessTools.Field(mod, "Settings") ?? throw new MissingFieldException(mod.FullName, "Settings");
            fields = Names.Select(n => AccessTools.Field(settingsField.FieldType, n)
                ?? throw new MissingFieldException(settingsField.FieldType.FullName, n)).ToArray();
            minima = new float[Names.Length]; maxima = new float[Names.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(float)) throw new InvalidOperationException(Names[i] + " is no longer float");
                var range = fields[i].GetCustomAttributes(false).Single(a => a.GetType().FullName == "Nivarian.FloatRangeAttribute");
                minima[i] = (float)AccessTools.Property(range.GetType(), "Min").GetValue(range);
                maxima[i] = (float)AccessTools.Property(range.GetType(), "Max").GetValue(range);
            }
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
            var need = AccessTools.TypeByName("Nivarian.Need_IcyCore") ?? throw new TypeLoadException("Nivarian.Need_IcyCore");
            foreach (var name in new[] { "NeedInterval", "UpdateTemperatureHediff", "UpdateLuminisBonus", "SetInitialLevel" })
                harmony.Patch(AccessTools.DeclaredMethod(need, name) ?? throw new MissingMethodException(need.FullName, name),
                    transpiler: new HarmonyMethod(typeof(NivarianIcyCoreSettings), nameof(ReadShared)));
            foreach (var target in new[] {
                new[] { "Nivarian.NivarianDrones.NivarianContinuumAssaultCraftPawn", "ApplyDifficultyHealthScale" },
                new[] { "Nivarian_Race.Code.NivarianThing.ContinuumAssaultCraftRocket", "Start" },
                new[] { "Nivarian_Race.Code.NivarianProjectile.Proj_ContinuumAssaultCraft", "get_DamageAmount" },
                new[] { "Nivarian_Race.Code.Comps.ThingComps.Comp_AssaultCraftBossCombat", "ApplyLaserDamage" },
                new[] { "Nivarian_Race.Code.Comps.ThingComps.Comp_ContinuumBossShield", "get_ShieldMaxHits" },
                new[] { "Nivarian_Race.Code.Comps.BuildingComps.Comp_ContiniumBeacon", "get_DifficultyMultiplier" },
                new[] { "Nivarian_Race.Code.Comps.BuildingComps.Comp_ContiniumBeacon", "get_RechargeRateMultiplier" }
            })
            {
                var type = AccessTools.TypeByName(target[0]) ?? throw new TypeLoadException(target[0]);
                harmony.Patch(AccessTools.DeclaredMethod(type, target[1]) ?? throw new MissingMethodException(target[0], target[1]),
                    transpiler: new HarmonyMethod(typeof(NivarianIcyCoreSettings), nameof(ReadShared)));
            }
            change = MP.RegisterSyncMethod(typeof(NivarianIcyCoreSettings), nameof(Change));
            harmony.Patch(AccessTools.DeclaredMethod(settingsField.DeclaringType, "DoSettingsWindowContents"),
                prefix: new HarmonyMethod(typeof(NivarianIcyCoreSettings), nameof(BeginUi)),
                finalizer: new HarmonyMethod(typeof(NivarianIcyCoreSettings), nameof(EndUi)));
            harmony.Patch(AccessTools.DeclaredMethod(settingsField.FieldType, "ExposeData")
                ?? throw new MissingMethodException(settingsField.FieldType.FullName, "ExposeData"),
                prefix: new HarmonyMethod(typeof(NivarianIcyCoreSettings), nameof(BeginSettingsSave)),
                finalizer: new HarmonyMethod(typeof(NivarianIcyCoreSettings), nameof(EndSettingsSave)));
            Log.Message("[TaleNivarianCompat] 15 gameplay scalars (IcyCore, main-story difficulty, beacon recharge) use shared save values and synchronized UI changes.");
        }

        static NivarianIcyCoreSettings Shared => MP.IsInMultiplayer ? Current.Game?.GetComponent<NivarianIcyCoreSettings>() : null;
        static float Read(object localSettings, int index) => Shared is NivarianIcyCoreSettings shared
            ? shared.values[index] : (float)fields[index].GetValue(localSettings);

        static IEnumerable<CodeInstruction> ReadShared(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                int index = instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo f ? Array.IndexOf(fields, f) : -1;
                if (index < 0) { yield return instruction; continue; }
                var call = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NivarianIcyCoreSettings), nameof(Read)));
                // Preserve branch targets and exception boundaries around the original field load.
                foreach (var block in instruction.blocks.Where(b => b.blockType == ExceptionBlockType.EndExceptionBlock).ToArray())
                { instruction.blocks.Remove(block); call.blocks.Add(block); }
                instruction.opcode = OpCodes.Ldc_I4; instruction.operand = index;
                yield return instruction; yield return call; replaced++;
            }
            int expected = __originalMethod.Name == "NeedInterval" ? 13 : __originalMethod.Name == "UpdateTemperatureHediff" ? 2 : 1;
            if (replaced != expected) throw new InvalidOperationException(__originalMethod.Name + " shared settings reads: " + replaced + ", expected " + expected);
        }

        sealed class UiState
        {
            internal object Settings;
            internal float[] Local, Before;
            internal UiState Parent;
        }
        static void BeginUi(out UiState __state)
        {
            __state = null;
            var shared = Shared;
            if (!MP.InInterface || shared == null) return;
            var settings = settingsField.GetValue(null);
            __state = new UiState { Settings = settings, Local = fields.Select(f => (float)f.GetValue(settings)).ToArray(), Before = (float[])shared.values.Clone(), Parent = activeUi };
            activeUi = __state;
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(settings, __state.Before[i]);
        }
        static Exception EndUi(Exception __exception, UiState __state)
        {
            if (__state == null) return __exception;
            activeUi = __state.Parent;
            var indices = new List<int>(); var updates = new List<float>();
            for (int i = 0; i < fields.Length; i++)
            {
                float next = (float)fields[i].GetValue(__state.Settings);
                fields[i].SetValue(__state.Settings, __state.Local[i]);
                if (next != __state.Before[i]) { indices.Add(i); updates.Add(next); }
            }
            if (__exception == null && indices.Count != 0) change.DoSync(null, indices.ToArray(), updates.ToArray());
            return __exception;
        }
        // ResetAll clears read notifications and calls ModSettings.Write while the UI is
        // still displaying shared values. Serialize local preferences, then restore the
        // pending UI delta even if the serializer throws. GameComponent saves are separate.
        static void BeginSettingsSave(object __instance, out float[] __state)
        {
            __state = null;
            if (Scribe.mode != LoadSaveMode.Saving) return;
            UiState local = null;
            for (var frame = activeUi; frame != null; frame = frame.Parent)
                if (ReferenceEquals(frame.Settings, __instance)) local = frame;
            if (local == null) return;
            __state = fields.Select(f => (float)f.GetValue(__instance)).ToArray();
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__instance, local.Local[i]);
        }
        static Exception EndSettingsSave(object __instance, Exception __exception, float[] __state)
        {
            if (__state != null)
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(__instance, __state[i]);
            return __exception;
        }
        static void Change(int[] indices, float[] updates)
        {
            var shared = Shared;
            if (shared == null || indices == null || updates == null || indices.Length != updates.Length || indices.Length > Names.Length) return;
            // Validate the whole delta before changing any shared value.
            for (int i = 0; i < indices.Length; i++)
                if (indices[i] < 0 || indices[i] >= Names.Length || float.IsNaN(updates[i]) || float.IsInfinity(updates[i])
                    || updates[i] < minima[indices[i]] || updates[i] > maxima[indices[i]]) return;
            for (int i = 0; i < indices.Length; i++) shared.values[indices[i]] = updates[i];
        }
    }
}
