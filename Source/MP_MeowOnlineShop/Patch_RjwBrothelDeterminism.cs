using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Brothel Colony selects a client from map- and caravan-derived lists with
    /// RandomElement.  The candidate membership is deterministic, but its
    /// enumeration order is not guaranteed after a cross-PC save load.  Sorting
    /// the Pawn candidates by durable Thing ID before the existing Rand draw
    /// preserves one draw while making the selected client identical.
    /// </summary>
    internal static class Patch_RjwBrothelDeterminism
    {
        private const string PackageId = "calamabanana.rjw.brothelcolony";
        private const string AnchorTypeName = "BrothelColony.JobGiver_WhoreInvitingVisitors";

        private static readonly MethodInfo StablePawnRandomElementMethod =
            AccessTools.Method(typeof(Patch_RjwBrothelDeterminism), nameof(StablePawnRandomElement));
        private static int _replacementCount;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type anchor = AccessTools.TypeByName(AnchorTypeName);
            Assembly assembly = anchor?.Assembly;
            if (assembly == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Brothel] runtime anchor was not found; stable client selection was not applied.");
                return;
            }

            _replacementCount = 0;
            int patchedMethods = 0;
            var transpiler = new HarmonyMethod(AccessTools.Method(
                typeof(Patch_RjwBrothelDeterminism),
                nameof(StablePawnRandomElementTranspiler)));
            foreach (Type type in GetLoadableTypes(assembly))
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null ||
                        !CallsPawnRandomElement(method))
                        continue;
                    harmony.Patch(method, transpiler: transpiler);
                    patchedMethods++;
                }
            }

            if (patchedMethods == 0)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Brothel] no Pawn RandomElement callers matched the installed assembly.");
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop][RJW-Brothel] stabilized Pawn RandomElement callers=" +
                patchedMethods + ".");
        }

        private static IEnumerable<CodeInstruction> StablePawnRandomElementTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo called = instruction.operand as MethodInfo;
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    IsPawnRandomElement(called))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = StablePawnRandomElementMethod;
                    _replacementCount++;
                }
                yield return instruction;
            }
        }

        private static Pawn StablePawnRandomElement(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
                return null;
            // ToList deliberately preserves the original source's empty-list
            // behavior through RandomElement after the deterministic ordering.
            return pawns.OrderBy(pawn => pawn?.thingIDNumber ?? int.MinValue).RandomElement();
        }

        private static bool CallsPawnRandomElement(MethodInfo method)
        {
            byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
            if (bytes == null)
                return false;
            for (int i = 0; i + 4 < bytes.Length; i++)
            {
                if (bytes[i] != OpCodes.Call.Value && bytes[i] != OpCodes.Callvirt.Value)
                    continue;
                try
                {
                    MethodInfo called = method.Module.ResolveMethod(
                        BitConverter.ToInt32(bytes, i + 1), GetTypeArguments(method),
                        method.IsGenericMethod ? method.GetGenericArguments() : null) as MethodInfo;
                    if (IsPawnRandomElement(called))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static bool IsPawnRandomElement(MethodInfo method)
        {
            return method != null && method.Name == "RandomElement" &&
                   method.ReturnType == typeof(Pawn) && method.GetParameters().Length == 1;
        }

        private static Type[] GetTypeArguments(MethodBase method)
        {
            return method.DeclaringType != null && method.DeclaringType.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : null;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { return exception.Types.Where(type => type != null); }
        }
    }
}
