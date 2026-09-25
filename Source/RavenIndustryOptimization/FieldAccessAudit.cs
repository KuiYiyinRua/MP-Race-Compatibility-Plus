using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Meow.RavenIndustryOptimization
{
    internal static class FieldAccessAudit
    {
        private static readonly Dictionary<short, OpCode> opcodes = BuildOpcodes();
        private static Dictionary<short, OpCode> BuildOpcodes()
        {
            var result = new Dictionary<short, OpCode>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.GetValue(null) is OpCode op) result[op.Value] = op;
            return result;
        }

        internal static IEnumerable<MethodBase> Methods(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var method in type.GetMethods(flags)) yield return method;
            foreach (var constructor in type.GetConstructors(flags))
                if (!constructor.IsStatic) yield return constructor;
            if (type.TypeInitializer != null) yield return type.TypeInitializer;
        }

        internal static bool Touches(MethodBase method, FieldInfo target)
        {
            var body = method.GetMethodBody();
            if (body == null) return false;
            byte[] bytes = body.GetILAsByteArray();
            bool found = false;
            for (int offset = 0; offset < bytes.Length;)
            {
                short code = bytes[offset++];
                if (code == 0xfe) code = (short)(0xfe00 | bytes[offset++]);
                var op = opcodes[code];
                if (op.OperandType == OperandType.InlineField)
                {
                    int token = BitConverter.ToInt32(bytes, offset);
                    var field = method.Module.ResolveField(token, method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                    if (field.Module == target.Module && field.MetadataToken == target.MetadataToken)
                    {
                        if (op != OpCodes.Ldfld && op != OpCodes.Stfld)
                            throw new NotSupportedException("By-reference conveyor position in " + method);
                        found = true;
                    }
                }
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: offset++; break;
                    case OperandType.InlineVar: offset += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: offset += 8; break;
                    case OperandType.InlineSwitch: offset += 4 + BitConverter.ToInt32(bytes, offset) * 4; break;
                    default: offset += 4; break;
                }
            }
            return found;
        }
    }
}
