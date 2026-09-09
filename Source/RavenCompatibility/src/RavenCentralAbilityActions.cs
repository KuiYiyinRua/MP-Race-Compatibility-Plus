using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenCentralAbilityActions
    {
        private static Type systemType;
        [ThreadStatic] private static bool drawingUnlock;
        [ThreadStatic] private static bool queuedUnlock;
        internal static void Apply(Harmony harmony)
        {
            systemType = AccessTools.TypeByName("RavenRace.Features.CentralHub.Abilities.GameComponent_RavenCentralAbilitySystem")
                ?? throw new TypeLoadException("Raven ability actions");
            foreach (string name in new[] { "TryUnlockNextLevel", "TryAssignAbility", "TryClearAbilitySlot" })
                harmony.Patch(AccessTools.DeclaredMethod(systemType, name) ?? throw new MissingMethodException(systemType.FullName, name),
                    prefix: new HarmonyMethod(typeof(RavenCentralAbilityActions), nameof(BeforeAction)));
            var drawer = AccessTools.TypeByName("RavenRace.Features.CentralHub.UI.Abilities.RavenCentralAbilityDetailDrawer");
            harmony.Patch(AccessTools.DeclaredMethod(drawer, "DrawUnlockButton"),
                prefix: new HarmonyMethod(typeof(RavenCentralAbilityActions), nameof(BeforeDraw)),
                transpiler: new HarmonyMethod(typeof(RavenCentralAbilityActions), nameof(ReplaceMessage)),
                finalizer: new HarmonyMethod(typeof(RavenCentralAbilityActions), nameof(AfterDraw)));
            MP.RegisterSyncMethod(typeof(RavenCentralAbilityActions), nameof(Execute));
        }
        private static IList Slots(GameComponent system) => (IList)AccessTools.Field(systemType, "abilitySlots").GetValue(system);

        private static bool BeforeAction(GameComponent __instance, MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (!MP.InInterface) return true;
            __result = false;
            switch (__originalMethod.Name)
            {
                case "TryUnlockNextLevel":
                    Execute(__instance, (Def)__args[0], -1, 0);
                    __args[1] = null;
                    queuedUnlock = drawingUnlock;
                    break;
                case "TryAssignAbility":
                    Execute(__instance, (Def)__args[0], (int)__args[1], 1);
                    __args[2] = null;
                    break;
                case "TryClearAbilitySlot":
                    int index = (int)__args[0];
                    var slots = Slots(__instance);
                    if (index >= 0 && index < slots.Count) Execute(__instance, slots[index] as Def, index, 2);
                    __args[1] = null;
                    break;
            }
            return false;
        }

        private static void Execute(GameComponent system, Def ability, int slot, int operation)
        {
            if (system == null || system.GetType() != systemType) return;
            string method;
            object[] args;
            switch (operation)
            {
                case 0: method = "TryUnlockNextLevel"; args = new object[] { ability, null }; break;
                case 1: method = "TryAssignAbility"; args = new object[] { ability, slot, null }; break;
                case 2:
                    var slots = Slots(system);
                    if (slot < 0 || slot >= slots.Count || !ReferenceEquals(slots[slot], ability)) return;
                    method = "TryClearAbilitySlot"; args = new object[] { slot, null }; break;
                default: return;
            }
            bool success = (bool)AccessTools.DeclaredMethod(systemType, method).Invoke(system, args);
            if (!success)
            {
                if (args[args.Length - 1] is string reason && !string.IsNullOrEmpty(reason))
                    Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            }
            else if (operation == 0)
            {
                int level = (int)AccessTools.DeclaredMethod(systemType, "AbilityLevel").Invoke(system, new object[] { ability });
                Messages.Message("Raven_CentralAbility_UnlockedMessage".Translate(ability.LabelCap, level), MessageTypeDefOf.PositiveEvent, false);
            }
        }

        private static void BeforeDraw(out bool[] __state)
        {
            __state = new[] { drawingUnlock, queuedUnlock };
            drawingUnlock = true;
            queuedUnlock = false;
        }
        private static void AfterDraw(bool[] __state)
        {
            drawingUnlock = __state[0];
            queuedUnlock = __state[1];
        }
        private static IEnumerable<CodeInstruction> ReplaceMessage(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Messages), "Message", new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) });
            var replacement = AccessTools.DeclaredMethod(typeof(RavenCentralAbilityActions), nameof(ShowMessage));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original)) { instruction.operand = replacement; replaced++; }
                yield return instruction;
            }
            if (replaced != 2) throw new InvalidOperationException("Raven unlock feedback call-site mismatch: " + replaced);
        }
        private static void ShowMessage(string text, MessageTypeDef type, bool historical)
        {
            if (MP.InInterface && drawingUnlock && queuedUnlock)
                Messages.Message("联机请求已提交，等待同步结果。", MessageTypeDefOf.NeutralEvent, false);
            else Messages.Message(text, type, historical);
        }
    }
}
