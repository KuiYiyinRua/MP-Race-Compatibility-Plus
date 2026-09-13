using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350CloneConfirmMp
    {
        private static bool applied;
        private static MethodInfo forbid, message;
        private static FieldInfo podField, activeBill;
        private static PropertyInfo corpseProperty;
        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || !ModsConfig.IsActive("secretarynexus.secretarynexusracemod")) return;
            applied = true;
            try
            {
                Type pod = Required("SEC_Building_SecretaryClonePod");
                Type recipe = Required("SEC_Def_CloneRecipe");
                Type utility = Required("SEC_Utility_Clone");
                Type closure = AccessTools.Inner(pod, "<>c__DisplayClass64_0") ?? throw new TypeLoadException("Clone confirmation closure");
                podField = AccessTools.DeclaredField(closure, "<>4__this");
                FieldInfo recipeField = AccessTools.DeclaredField(closure, "recipe");
                if (podField?.FieldType != pod || recipeField?.FieldType != recipe || !typeof(Thing).IsAssignableFrom(pod) || !typeof(Def).IsAssignableFrom(recipe))
                    throw new InvalidOperationException("Clone confirmation captures changed");
                activeBill = AccessTools.DeclaredField(pod, "activeBill") ?? throw new MissingFieldException(pod.FullName, "activeBill");
                corpseProperty = AccessTools.Property(pod, "Corpse");
                if (corpseProperty?.PropertyType != typeof(Corpse)) throw new MissingMemberException("Clone pod Corpse");
                MethodInfo create = AccessTools.DeclaredMethod(utility, "CreateResurrectionBill", new[] { pod, recipe, typeof(Corpse), typeof(bool) });
                MethodInfo start = AccessTools.DeclaredMethod(pod, "StartForming", Type.EmptyTypes);
                if (create == null || !create.IsStatic || create.ReturnType != typeof(void) || start == null || start.IsStatic || start.ReturnType != typeof(void))
                    throw new MissingMethodException("Clone action executor changed");
                var callbacks = new List<MethodInfo>();
                foreach (string name in new[] { "<GetGizmos>b__7", "<GetGizmos>b__8", "<GetGizmos>b__4" })
                {
                    MethodInfo callback = AccessTools.DeclaredMethod(closure, name, Type.EmptyTypes);
                    if (callback == null || callback.IsStatic || callback.ReturnType != typeof(void)) throw new MissingMethodException(closure.FullName, name);
                    var il = PatchProcessor.GetOriginalInstructions(callback);
                    if (il.Count(i => i.Calls(create)) != 1 || il.Count(i => i.Calls(start)) != 1)
                        throw new InvalidOperationException(name + ": complete confirmation body changed");
                    callbacks.Add(callback);
                }
                MethodInfo chosen = AccessTools.DeclaredMethod(pod, "OnTargetChosen", new[] { typeof(LocalTargetInfo) }) ?? throw new MissingMethodException("OnTargetChosen");
                forbid = AccessTools.DeclaredMethod(typeof(ForbidUtility), nameof(ForbidUtility.SetForbidden), new[] { typeof(Thing), typeof(bool), typeof(bool) });
                if (forbid == null || PatchProcessor.GetOriginalInstructions(chosen).Count(i => i.Calls(forbid)) != 1)
                    throw new InvalidOperationException("External corpse forbidden write changed");
                message = AccessTools.DeclaredMethod(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) });
                if (message == null || PatchProcessor.GetOriginalInstructions(chosen).Count(i => i.Calls(message)) != 3)
                    throw new InvalidOperationException("External corpse validation messages changed");
                // The three inside-pod confirmations remain one original command each.
                foreach (MethodInfo callback in callbacks)
                {
                    MP.RegisterSyncDelegate(pod, closure.Name, callback.Name, new[] { "<>4__this", "recipe" }, Type.EmptyTypes).CancelIfAnyFieldNull();
                    harmony.Patch(callback, prefix: new HarmonyMethod(typeof(Patch_Light350CloneConfirmMp), nameof(ValidInsideConfirmation)));
                }
                // External corpse options and the unconscious direct branch share this stable executor.
                // Calls from the synchronized inside-pod callback execute inline, without a second command.
                MP.RegisterSyncMethod(create);
                MP.RegisterSyncMethod(typeof(Patch_Light350CloneConfirmMp), nameof(SetCorpseForbidden));
                harmony.Patch(chosen, transpiler: new HarmonyMethod(typeof(Patch_Light350CloneConfirmMp), nameof(ExternalCorpseWrite)));
                Log.Message("[MP-MeowOnlineShop][Light350-B7] Secretary resurrection: 3 complete confirmations, shared bill executor and external corpse forbidden action installed.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B7] REQUIRED TARGET FAILED Secretary resurrection: " + e); }
        }
        private static Type Required(string name) => AccessTools.TypeByName("SEC_Assemblies." + name) ?? throw new TypeLoadException(name);
        private static bool ValidInsideConfirmation(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand) return true;
            object pod = podField.GetValue(__instance);
            return pod is Thing thing && !thing.Destroyed && activeBill.GetValue(pod) == null &&
                corpseProperty.GetValue(pod) is Corpse corpse && !corpse.Destroyed && corpse.InnerPawn != null;
        }
        private static IEnumerable<CodeInstruction> ExternalCorpseWrite(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0, messages = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(forbid))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Light350CloneConfirmMp), nameof(SetCorpseForbidden));
                    replaced++;
                }
                else if (instruction.Calls(message))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Light350CloneConfirmMp), nameof(LocalValidationMessage));
                    messages++;
                }
                yield return instruction;
            }
            if (replaced != 1 || messages != 3) throw new InvalidOperationException("External corpse action coverage changed");
        }
        private static void SetCorpseForbidden(Thing corpse, bool value, bool warnOnFail)
        {
            if (corpse == null || corpse.Destroyed) return;
            corpse.SetForbidden(value, warnOnFail);
        }
        private static void LocalValidationMessage(string text, MessageTypeDef type, bool historical)
        {
            Messages.Message(text, type, MP.IsInMultiplayer && MP.InInterface ? false : historical);
        }
    }
}
