using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Synchronizes God Hands turret-head gizmos, targeting callbacks, shell
    /// loading, refuel orders and module install/remove UI actions.
    /// </summary>
    internal static class Patch_GodHandsTurretCommands
    {
        private enum TurretToggleAction
        {
            HoldFire = 0,
            Sweep = 1,
            Lead = 2,
            SmartRetarget = 3,
            AirDefense = 4
        }

        private enum TurretActionKind
        {
            None = 0,
            ClearForced = 1
        }

        private sealed class ActionState
        {
            public IList Slots;
            public List<int> AirDefenseSlots = new List<int>();
            public List<int> MortarSlots = new List<int>();
            public List<int> ManualActivateSlots = new List<int>();
            public List<int> FuelSlots = new List<int>();
            public bool HasForced;
            public bool HasModules;
        }

        private static readonly FieldInfo CommandActionField = AccessTools.Field(typeof(Command_Action), "action");
        private static readonly FieldInfo CommandToggleField = AccessTools.Field(typeof(Command_Toggle), "toggleAction");
        private static readonly FieldInfo CommandTargetOnSelectedField =
            Patch_GodHands.CommandTargetType == null ? null : AccessTools.Field(Patch_GodHands.CommandTargetType, "onTargetSelected");
        private static readonly FieldInfo CommandTargetSlotField =
            Patch_GodHands.CommandTargetType == null ? null : AccessTools.Field(Patch_GodHands.CommandTargetType, "turretSlot");
        private static readonly FieldInfo SlotParentHeadField =
            Patch_GodHands.SlotType == null ? null : AccessTools.Field(Patch_GodHands.SlotType, "parentHead");

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            try
            {
                MethodInfo getWornGizmos = AccessTools.Method(Patch_GodHands.HeadType, "GetWornGizmos");
                MethodInfo gizmoPostfix = AccessTools.Method(typeof(Patch_GodHandsTurretCommands), nameof(CaptureGizmosPostfix));
                if (getWornGizmos != null && gizmoPostfix != null)
                    harmony.Patch(getWornGizmos, postfix: new HarmonyMethod(gizmoPostfix));
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] God Hands gizmo capture patch failed: {e.Message}");
            }

            TryPatchMethod(harmony, "TryOrderRefuel", nameof(RefuelOrderPrefix), null);
            TryPatchLoadShellClosures(harmony);

            TryPatchSlotMethod(harmony, "ToggleAirDefenseMode", nameof(ToggleAirDefensePrefix));
            TryPatchSlotMethod(harmony, "Activate", nameof(ActivatePrefix));
            TryPatchSlotMethod(harmony, "AddModule", nameof(AddModulePrefix));
            TryPatchSlotMethodWithArgs(harmony, "RemoveModule", new[] { typeof(Thing) }, nameof(RemoveModulePrefix));
        }

        private static void TryPatchMethod(Harmony harmony, string methodName, string prefixName, string postfixName)
        {
            try
            {
                MethodInfo method = AccessTools.Method(Patch_GodHands.HeadType, methodName);
                MethodInfo prefix = prefixName == null ? null : AccessTools.Method(typeof(Patch_GodHandsTurretCommands), prefixName);
                MethodInfo postfix = postfixName == null ? null : AccessTools.Method(typeof(Patch_GodHandsTurretCommands), postfixName);
                if (method == null || (prefix == null && postfix == null))
                    return;
                harmony.Patch(
                    method,
                    prefix: prefix == null ? null : new HarmonyMethod(prefix) { priority = Priority.First + 2 },
                    postfix: postfix == null ? null : new HarmonyMethod(postfix));
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] God Hands patch {methodName} failed: {e.Message}");
            }
        }

        private static void TryPatchSlotMethod(Harmony harmony, string methodName, string prefixName)
        {
            try
            {
                MethodInfo method = AccessTools.Method(Patch_GodHands.SlotType, methodName);
                MethodInfo prefix = AccessTools.Method(typeof(Patch_GodHandsTurretCommands), prefixName);
                if (method == null || prefix == null)
                    return;
                harmony.Patch(method, prefix: new HarmonyMethod(prefix) { priority = Priority.First + 2 });
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] God Hands slot patch {methodName} failed: {e.Message}");
            }
        }

        private static void TryPatchSlotMethodWithArgs(Harmony harmony, string methodName, Type[] args, string prefixName)
        {
            try
            {
                MethodInfo method = AccessTools.Method(Patch_GodHands.SlotType, methodName, args);
                MethodInfo prefix = AccessTools.Method(typeof(Patch_GodHandsTurretCommands), prefixName);
                if (method == null || prefix == null)
                    return;
                harmony.Patch(method, prefix: new HarmonyMethod(prefix) { priority = Priority.First + 2 });
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] God Hands slot patch {methodName} failed: {e.Message}");
            }
        }

        private static void TryPatchLoadShellClosures(Harmony harmony)
        {
            if (Patch_GodHands.HeadType == null)
                return;
            MethodInfo prefix = AccessTools.Method(typeof(Patch_GodHandsTurretCommands), nameof(LoadShellClosurePrefix));
            if (prefix == null)
                return;
            int patched = 0;
            foreach (Type nested in Patch_GodHands.HeadType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (MethodInfo method in nested.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.ReturnType != typeof(void) ||
                        method.GetParameters().Length != 0 ||
                        method.Name.IndexOf("TryOrderLoadShell", StringComparison.Ordinal) < 0)
                        continue;
                    try
                    {
                        harmony.Patch(method, prefix: new HarmonyMethod(prefix) { priority = Priority.First + 2 });
                        patched++;
                    }
                    catch
                    {
                    }
                }
            }
            if (patched > 0)
                Log.Message($"[MP-MeowOnlineShop] God Hands load-shell callbacks patched: {patched}.");
        }

        private static void CaptureGizmosPostfix(object __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!MP.IsInMultiplayer || __instance == null || __result == null ||
                !(__instance is Thing head) || head.Map == null || Patch_GodHands.HeadTurretSlotsField == null)
                return;

            var list = __result as IList<Gizmo> ?? __result.ToList();
            ActionState state = BuildActionState(head);
            int toggleCount = 0;
            int actionCount = 0;

            for (int i = 0; i < list.Count; i++)
            {
                Gizmo gizmo = list[i];
                if (gizmo is Command_Toggle toggle)
                {
                    if (!Patch_GodHands.IsGodHandClosure(toggle.toggleAction))
                        continue;
                    TurretToggleAction action;
                    int slotIndex = -1;
                    if (toggleCount < 4)
                    {
                        action = (TurretToggleAction)toggleCount;
                        toggleCount++;
                    }
                    else
                    {
                        int airIndex = toggleCount - 4;
                        toggleCount++;
                        if (airIndex < 0 || airIndex >= state.AirDefenseSlots.Count)
                            continue;
                        action = TurretToggleAction.AirDefense;
                        slotIndex = state.AirDefenseSlots[airIndex];
                    }
                    WrapToggle(toggle, head, action, slotIndex);
                }
                else if (gizmo is Command_Action commandAction)
                {
                    if (!Patch_GodHands.IsGodHandClosure(commandAction.action))
                        continue;
                    ResolveAction(state, actionCount, out TurretActionKind kind, out int slotIndex);
                    actionCount++;
                    if (kind == TurretActionKind.ClearForced)
                        WrapClearForced(commandAction, head);
                }
                else if (Patch_GodHands.CommandTargetType != null &&
                         Patch_GodHands.CommandTargetType.IsInstanceOfType(gizmo))
                {
                    WrapTarget(gizmo, head, state);
                }
            }

            __result = list;
        }

        private static ActionState BuildActionState(Thing head)
        {
            var state = new ActionState();
            var slots = Patch_GodHands.HeadTurretSlotsField.GetValue(head) as IList;
            state.Slots = slots;
            if (slots == null)
                return state;

            PropertyInfo isMortar = AccessTools.Property(Patch_GodHands.SlotType, "IsMortarType");
            PropertyInfo isManual = AccessTools.Property(Patch_GodHands.SlotType, "IsManualFireType");
            for (int i = 0; i < slots.Count; i++)
            {
                object slot = slots[i];
                if ((bool)(Patch_GodHands.SlotIsAirDefenseTurretField.GetValue(slot) ?? false))
                    state.AirDefenseSlots.Add(i);
                if ((bool)(isMortar?.GetValue(slot, null) ?? false))
                    state.MortarSlots.Add(i);
                if ((bool)(isManual?.GetValue(slot, null) ?? false) &&
                    (bool)(Patch_GodHands.SlotActivationRequiredField.GetValue(slot) ?? false) &&
                    !(bool)(Patch_GodHands.SlotIsActivatedField.GetValue(slot) ?? false))
                    state.ManualActivateSlots.Add(i);
                if ((bool)(Patch_GodHands.SlotFuelSystemEnabledField.GetValue(slot) ?? false) &&
                    (float)(Patch_GodHands.SlotFuelField.GetValue(slot) ?? 0f) <
                    (float)(Patch_GodHands.SlotFuelCapacityField.GetValue(slot) ?? 0f))
                    state.FuelSlots.Add(i);
                if (((LocalTargetInfo)Patch_GodHands.SlotForcedTargetField.GetValue(slot)).IsValid)
                    state.HasForced = true;
                IList modules = Patch_GodHands.SlotStoredModulesField.GetValue(slot) as IList;
                if (modules != null && modules.Count > 0)
                    state.HasModules = true;
            }
            return state;
        }

        private static void ResolveAction(ActionState state, int index, out TurretActionKind kind, out int slotIndex)
        {
            kind = TurretActionKind.None;
            slotIndex = -1;
            int cursor = 0;

            foreach (int mortar in state.MortarSlots)
            {
                if (cursor++ == index)
                {
                    slotIndex = mortar;
                    return;
                }
            }
            foreach (int activate in state.ManualActivateSlots)
            {
                if (cursor++ == index)
                {
                    slotIndex = activate;
                    return;
                }
            }
            if (state.HasForced && cursor++ == index)
            {
                kind = TurretActionKind.ClearForced;
                return;
            }
            foreach (int fuel in state.FuelSlots)
            {
                if (cursor++ == index)
                {
                    slotIndex = fuel;
                    return;
                }
            }
            if (state.Slots != null && state.Slots.Count > 1 && cursor++ == index)
                return;
            if (state.HasModules && cursor++ == index)
                return;
        }

        private static void WrapToggle(Command_Toggle toggle, Thing head, TurretToggleAction action, int slotIndex)
        {
            if (toggle == null || CommandToggleField == null)
                return;
            var original = CommandToggleField.GetValue(toggle) as Action;
            int headId = head.thingIDNumber;
            int mapIndex = head.Map.Index;
            CommandToggleField.SetValue(toggle, (Action)(() =>
            {
                if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                {
                    original?.Invoke();
                    return;
                }

                bool newValue;
                switch (action)
                {
                    case TurretToggleAction.HoldFire:
                        newValue = !(bool)(Patch_GodHands.HeadHoldFireField.GetValue(head) ?? false);
                        GodHandSync.SyncTurretHoldFire(mapIndex, headId, newValue);
                        break;
                    case TurretToggleAction.Sweep:
                        newValue = GetFirstSlotBool(head, Patch_GodHands.SlotSweepModeField, false);
                        GodHandSync.SyncTurretSweep(mapIndex, headId, !newValue);
                        break;
                    case TurretToggleAction.Lead:
                        newValue = GetFirstSlotBool(head, Patch_GodHands.SlotLeadTargetingField, true);
                        GodHandSync.SyncTurretLead(mapIndex, headId, !newValue);
                        break;
                    case TurretToggleAction.SmartRetarget:
                        newValue = GetFirstSlotBool(head, Patch_GodHands.SlotSmartRetargetField, true);
                        GodHandSync.SyncTurretSmartRetarget(mapIndex, headId, !newValue);
                        break;
                    case TurretToggleAction.AirDefense:
                        object slot = Patch_GodHands.GetHeadSlot(head, slotIndex);
                        if (slot != null)
                            GodHandSync.SyncTurretAirDefense(mapIndex, headId, slotIndex);
                        break;
                }
            }));
        }

        private static bool GetFirstSlotBool(Thing head, FieldInfo field, bool fallback)
        {
            object slot = Patch_GodHands.GetHeadSlot(head, 0);
            return slot == null ? fallback : (bool)(field.GetValue(slot) ?? fallback);
        }

        private static void WrapClearForced(Command_Action commandAction, Thing head)
        {
            if (commandAction == null || CommandActionField == null)
                return;
            var original = CommandActionField.GetValue(commandAction) as Action;
            int headId = head.thingIDNumber;
            int mapIndex = head.Map.Index;
            CommandActionField.SetValue(commandAction, (Action)(() =>
            {
                if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                {
                    original?.Invoke();
                    return;
                }
                GodHandSync.SyncTurretClearForced(mapIndex, headId);
            }));
        }

        private static void WrapTarget(object command, Thing head, ActionState state)
        {
            if (command == null || CommandTargetOnSelectedField == null || CommandTargetSlotField == null)
                return;
            var original = CommandTargetOnSelectedField.GetValue(command) as Action<LocalTargetInfo>;
            object slotRef = CommandTargetSlotField.GetValue(command);
            int slotIndex = FindSlotIndex(state.Slots, slotRef);
            int headId = head.thingIDNumber;
            int mapIndex = head.Map.Index;
            CommandTargetOnSelectedField.SetValue(command, (Action<LocalTargetInfo>)(target =>
            {
                if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                {
                    original?.Invoke(target);
                    return;
                }
                int kind = InferTargetKind(slotRef);
                int thingId = target.HasThing && target.Thing != null ? target.Thing.thingIDNumber : 0;
                GodHandSync.SyncTurretTarget(mapIndex, headId, slotIndex, kind, thingId, target.Cell.x, target.Cell.z);
            }));
        }

        private static int FindSlotIndex(IList slots, object slotRef)
        {
            if (slots == null || slotRef == null)
                return -1;
            for (int i = 0; i < slots.Count; i++)
            {
                if (ReferenceEquals(slots[i], slotRef))
                    return i;
            }
            return -1;
        }

        private static int InferTargetKind(object slot)
        {
            if (slot == null)
                return 0;
            if ((bool)(Patch_GodHands.SlotShellSystemEnabledField.GetValue(slot) ?? false))
                return 2;
            PropertyInfo isManual = AccessTools.Property(Patch_GodHands.SlotType, "IsManualFireType");
            if ((bool)(isManual?.GetValue(slot, null) ?? false))
                return 1;
            return 0;
        }

        private static bool LoadShellClosurePrefix(object __instance)
        {
            if (!ShouldInterceptUi() || __instance == null)
                return true;
            ThingDef shellDef = null;
            Thing shellThing = null;
            object slotRef = null;
            object headRef = null;
            FieldInfo[] fields = __instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (FieldInfo field in fields)
            {
                object value = field.GetValue(__instance);
                if (value is ThingDef def && shellDef == null)
                    shellDef = def;
                else if (value is Thing thing && shellThing == null && thing.def != null)
                    shellThing = thing;
                else if (Patch_GodHands.SlotType != null && Patch_GodHands.SlotType.IsInstanceOfType(value) && slotRef == null)
                    slotRef = value;
                else if (Patch_GodHands.HeadType != null && Patch_GodHands.HeadType.IsInstanceOfType(value) && headRef == null)
                    headRef = value;
            }
            if (shellDef == null || shellThing == null || slotRef == null || headRef == null)
                return true;
            if (!(headRef is Thing head) || head.Map == null)
                return true;
            int slotIndex = FindSlotIndex(Patch_GodHands.HeadTurretSlotsField.GetValue(head) as IList, slotRef);
            if (slotIndex < 0)
                return true;
            int capacity = (int)(Patch_GodHands.SlotShellCapacityField.GetValue(slotRef) ?? 1);
            int loaded = (int)(Patch_GodHands.SlotLoadedShellCountField.GetValue(slotRef) ?? 0);
            int needed = Math.Max(0, capacity - loaded);
            int count = Math.Min(needed, shellThing.stackCount);
            if (count <= 0)
                return false;
            GodHandSync.SyncTurretLoadShell(
                head.Map.Index,
                head.thingIDNumber,
                slotIndex,
                shellThing.thingIDNumber,
                shellDef.defName,
                count);
            return false;
        }

        private static bool RefuelOrderPrefix(object __instance, int slotIndex)
        {
            if (!ShouldInterceptUi() || !(__instance is Thing head) || head.Map == null)
                return true;
            GodHandSync.SyncTurretRefuelOrder(head.Map.Index, head.thingIDNumber, slotIndex);
            return false;
        }

        private static bool ToggleAirDefensePrefix(object __instance)
        {
            if (!ShouldInterceptUi())
                return true;
            return TrySendSlotCommand(__instance, (mapIndex, headId, slotIndex) =>
                GodHandSync.SyncTurretAirDefense(mapIndex, headId, slotIndex));
        }

        private static bool ActivatePrefix(object __instance)
        {
            if (!ShouldInterceptUi())
                return true;
            return TrySendSlotCommand(__instance, (mapIndex, headId, slotIndex) =>
                GodHandSync.SyncTurretActivate(mapIndex, headId, slotIndex));
        }

        private static bool AddModulePrefix(object __instance, Thing module)
        {
            if (!ShouldInterceptUi() || module == null)
                return true;
            return TrySendSlotCommand(__instance, (mapIndex, headId, slotIndex) =>
                GodHandSync.SyncTurretModuleAdd(mapIndex, headId, slotIndex, module.thingIDNumber));
        }

        private static bool RemoveModulePrefix(object __instance, Thing module)
        {
            if (!ShouldInterceptUi() || module == null)
                return true;
            return TrySendSlotCommand(__instance, (mapIndex, headId, slotIndex) =>
                GodHandSync.SyncTurretModuleRemove(mapIndex, headId, slotIndex, module.thingIDNumber));
        }

        private static bool TrySendSlotCommand(object slot, Action<int, int, int> send)
        {
            if (slot == null || SlotParentHeadField == null)
                return true;
            object parent = SlotParentHeadField.GetValue(slot);
            if (!(parent is Thing head) || head.Map == null)
                return true;
            object stackIndex = AccessTools.Field(Patch_GodHands.SlotType, "stackIndex")?.GetValue(slot);
            int slotIndex = stackIndex is int i ? i : -1;
            if (slotIndex < 0)
                return true;
            send(head.Map.Index, head.thingIDNumber, slotIndex);
            return false;
        }

        private static bool ShouldInterceptUi()
        {
            return MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;
        }
    }
}
