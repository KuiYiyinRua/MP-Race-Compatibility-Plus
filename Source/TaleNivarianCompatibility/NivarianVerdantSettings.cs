using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // CompProperties copies the setting during Def construction. Preserve the host's
    // resolved per-Def values, including explicit XML overrides, without mutating Defs.
    public sealed class NivarianVerdantSettings : GameComponent
    {
        static FieldInfo growthField;
        static Dictionary<CompProperties, string> keys;
        Dictionary<string, float> growth;

        public NivarianVerdantSettings(Game game)
        {
            Resolve();
            if (keys != null) Capture();
        }

        static void Resolve()
        {
            if (keys != null) return;
            var type = AccessTools.TypeByName("Nivarian.CompProperties_VerdantBlessing");
            if (type == null) return; // Royalty content is optional.
            growthField = AccessTools.Field(type, "totalGrowthAmountPerDay")
                ?? throw new MissingFieldException(type.FullName, "totalGrowthAmountPerDay");
            keys = new Dictionary<CompProperties, string>();
            foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal))
            {
                if (def.comps == null) continue;
                for (int i = 0; i < def.comps.Count; i++)
                    if (type.IsInstanceOfType(def.comps[i]) && !keys.ContainsKey(def.comps[i]))
                        keys.Add(def.comps[i], def.defName + ":" + i.ToString(CultureInfo.InvariantCulture));
            }
        }

        void Capture()
        {
            growth = new Dictionary<string, float>();
            foreach (var pair in keys.OrderBy(p => p.Value, StringComparer.Ordinal))
                growth.Add(pair.Value, (float)growthField.GetValue(pair.Key));
        }

        public override void ExposeData()
        {
            if (keys == null) return;
            Scribe_Collections.Look(ref growth, "meowResolvedVerdantGrowth", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && growth == null) Capture();
        }

        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.ThingComp_VerdantBlessing");
            if (type == null) return;
            Resolve();
            foreach (var name in new[] { "get_EffectiveGrowthAmountPerDay", "PostSpawnSetup", "PostExposeData" })
                harmony.Patch(AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name),
                    transpiler: new HarmonyMethod(typeof(NivarianVerdantSettings), nameof(ReadHostGrowth)));
            Log.Message("[TaleNivarianCompat] Verdant growth uses saved resolved Def values; existing per-thing growth remains unchanged.");
        }

        static float Read(CompProperties properties)
        {
            if (MP.IsInMultiplayer && Current.Game != null && keys.TryGetValue(properties, out var key))
            {
                var state = Current.Game.GetComponent<NivarianVerdantSettings>();
                if (state?.growth != null && state.growth.TryGetValue(key, out float value)) return value;
            }
            return (float)growthField.GetValue(properties);
        }

        static IEnumerable<CodeInstruction> ReadHostGrowth(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, growthField))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianVerdantSettings), nameof(Read));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException(__originalMethod.Name + " expected one resolved Verdant growth read, found " + count);
        }
    }
}
