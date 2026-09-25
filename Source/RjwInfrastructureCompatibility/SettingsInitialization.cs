using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Prepatcher;

namespace Meow.RjwInfrastructure
{
    public static class SettingsInitialization
    {
        // Mod constructors run on the loading worker. Unity font resources may
        // only be loaded from the UI thread. Prepatcher runs before these types
        // initialize, regardless of the compatibility mod's load-order position.
        [FreePatchAll]
        public static bool DeferFontMeasurement(ModuleDefinition module)
        {
            if (module.Assembly.Name.Name != "RJW") return false;
            var type = module.GetType("rjw.RJWHookupSettings");
            if (type == null) throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: settings type missing");
            var init = type.Methods.Single(m => m.Name == ".cctor");
            var draw = type.Methods.Single(m => m.Name == "DoWindowContents" && m.Parameters.Count == 1);
            var height = type.Fields.Single(f => f.Name == "sectionHeight" && f.FieldType.FullName == "System.Single");
            var instructions = init.Body.Instructions;
            var calls = instructions.Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference m &&
                m.DeclaringType.FullName == "Verse.Text" && m.Name == "get_LineHeight").ToList();
            // Idempotence: another prepatching pass may have already moved it.
            var drawInstructions = draw.Body.Instructions;
            if (calls.Count == 0 && drawInstructions.Count > 7 &&
                drawInstructions[0].OpCode == OpCodes.Ldsfld &&
                drawInstructions[0].Operand is FieldReference existing && existing.FullName == height.FullName &&
                drawInstructions[1].OpCode == OpCodes.Ldc_R4 && Equals(drawInstructions[1].Operand, 0f) &&
                drawInstructions[2].OpCode == OpCodes.Bgt_Un && drawInstructions[6].OpCode == OpCodes.Stsfld) return false;
            if (calls.Count != 1) throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: font initializer shape changed");
            var call = calls[0]; int index = instructions.IndexOf(call);
            if (index + 3 >= instructions.Count || instructions[index+1].OpCode != OpCodes.Ldc_R4 ||
                !Equals(instructions[index+1].Operand, 200f) || instructions[index+2].OpCode != OpCodes.Mul ||
                instructions[index+3].OpCode != OpCodes.Stsfld ||
                !(instructions[index+3].Operand is FieldReference target) || target.FullName != height.FullName)
                throw new InvalidOperationException("INFRA REQUIRED_TARGET_FAILURE: font field assignment changed");
            var getter = (MethodReference)call.Operand;
            call.OpCode = OpCodes.Ldc_R4; call.Operand = 0f;
            var il = draw.Body.GetILProcessor(); var first = draw.Body.Instructions[0];
            foreach (var added in new[] {
                Instruction.Create(OpCodes.Ldsfld, height), Instruction.Create(OpCodes.Ldc_R4, 0f),
                Instruction.Create(OpCodes.Bgt_Un, first), Instruction.Create(OpCodes.Call, getter),
                Instruction.Create(OpCodes.Ldc_R4, 200f), Instruction.Create(OpCodes.Mul), Instruction.Create(OpCodes.Stsfld, height) })
                il.InsertBefore(first, added);
            Console.WriteLine("[Meow.RjwInfrastructure] Settings font measurement deferred to UI thread.");
            return true;
        }
    }
}
