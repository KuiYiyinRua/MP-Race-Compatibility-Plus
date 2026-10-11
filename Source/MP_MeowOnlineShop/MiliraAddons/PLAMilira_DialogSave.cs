using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_DialogSave
{
    static readonly string[] flags = { "flagS_A", "flagS_B", "flagS_C" };
    static Type type;
    static PLAMilira_DialogSave()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                type = AccessTools.TypeByName("PLAMilira.Hediff_PLAMiliraDialog") ?? throw new TypeLoadException("Hediff_PLAMiliraDialog");
                foreach (string name in flags) if (AccessTools.Field(type, name) == null) throw new MissingFieldException(name);
                new Harmony("Meow.PLAMilira.DialogSave").Patch(AccessTools.DeclaredMethod(type, "ExposeData") ?? throw new MissingMethodException("ExposeData"),
                    transpiler: new HarmonyMethod(typeof(PLAMilira_DialogSave), nameof(Labels)),
                    postfix: new HarmonyMethod(typeof(PLAMilira_DialogSave), nameof(Legacy)));
                Log.Message("[PLAMilira_DialogSave] REQUIRED_TARGETS_OK three distinct dialogue flags and legacy reader");
            }
            catch (Exception e) { Log.Error("[PLAMilira_DialogSave] REQUIRED_TARGET_FAILED " + e); }
        });
    }
    static IEnumerable<CodeInstruction> Labels(IEnumerable<CodeInstruction> source)
    {
        var result = new List<CodeInstruction>(); string label = null; int count = 0;
        foreach (var instruction in source)
        {
            if (instruction.opcode == OpCodes.Ldflda && instruction.operand is FieldInfo field)
                label = flags.Contains(field.Name) ? field.Name : null;
            if (label != null && instruction.opcode == OpCodes.Ldstr)
            {
                if ((string)instruction.operand != label.Replace("S_", "")) throw new InvalidOperationException("Unexpected native dialogue save label");
                instruction.operand = label; label = null; count++;
            }
            result.Add(instruction);
        }
        if (count != 3) throw new InvalidOperationException("Dialogue save label count=" + count);
        return result;
    }
    static void Legacy(object __instance)
    {
        bool version = Scribe.mode == LoadSaveMode.Saving;
        Scribe_Values.Look(ref version, "meowDistinctDialogueFlags", false);
        if (Scribe.mode != LoadSaveMode.LoadingVars || version) return;
        foreach (string name in flags)
        {
            // Old saves omitted false values and reused A/B/C labels. Preserve
            // the old loader's first-label interpretation when ambiguous.
            var node = Scribe.loader.curXmlParent[name] ?? Scribe.loader.curXmlParent[name.Replace("S_", "")];
            if (node != null && bool.TryParse(node.InnerText, out bool value)) AccessTools.Field(type, name).SetValue(__instance, value);
        }
    }
}
