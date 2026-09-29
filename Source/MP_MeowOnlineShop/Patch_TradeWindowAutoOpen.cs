using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    // All state below is local presentation state. Never gate TryCreate, AddSession,
    // stock generation, trade execution, or serialization on the local issuer.
    internal static class Patch_TradeWindowAutoOpen
    {
        private static MethodInfo openWindow;
        private static FieldInfo traderField;
        private static FieldInfo sessionField;
        private static FieldInfo settlementField;
        private static Type tradingWindowType;
        private static object trackedSession;
        private static readonly Dictionary<int, RouteOwner> routes = new Dictionary<int, RouteOwner>();
        private static bool? arrivalOwnedLocally;

        internal sealed class RouteOwner
        {
            internal int SettlementId;
            internal bool Mine;
        }

        internal sealed class PathState
        {
            internal int CaravanId;
            internal RouteOwner Previous;
        }

        private static bool Enabled => MP.IsInMultiplayer &&
            MpMeowOnlineShopMod.Settings?.tradeWindowOnlyForInitiator == true;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                var trade = AccessTools.TypeByName("Multiplayer.Client.MpTradeSession");
                openWindow = AccessTools.Method(trade, "OpenWindow", new[] { typeof(bool) });
                traderField = AccessTools.Field(trade, "trader");
                sessionField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "session");
                settlementField = AccessTools.Field(typeof(CaravanArrivalAction_Trade), "settlement");
                tradingWindowType = AccessTools.TypeByName("Multiplayer.Client.TradingWindow");
                var ctor = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.DialogTradeCtorPatch"), "Prefix",
                    new[] { typeof(Pawn), typeof(ITrader), typeof(bool) });
                var encounter = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.NodeTreeDialogSync"),
                    "SyncDialogOptionByIndex", new[] { typeof(int) });
                if (openWindow == null || traderField == null || sessionField == null || settlementField == null ||
                    tradingWindowType == null || ctor == null || encounter == null)
                    throw new MissingMemberException("Multiplayer trade presentation targets changed");

                harmony.Patch(ctor, transpiler: Patch(nameof(TradeOpenTranspiler)));
                harmony.Patch(encounter, transpiler: Patch(nameof(EncounterOpenTranspiler)));
                harmony.Patch(AccessTools.Method(typeof(Caravan_PathFollower), "StartPath",
                        new[] { typeof(PlanetTile), typeof(CaravanArrivalAction), typeof(bool), typeof(bool) }),
                    prefix: Patch(nameof(BeforeStartPath)), postfix: Patch(nameof(AfterStartPath)),
                    finalizer: Patch(nameof(StartPathFinalizer)));
                harmony.Patch(AccessTools.Method(typeof(CaravanArrivalAction_Trade), "Arrived", new[] { typeof(Caravan) }),
                    prefix: Patch(nameof(BeforeArrival)), finalizer: Patch(nameof(AfterArrival)),
                    transpiler: Patch(nameof(ArrivalCameraTranspiler)));
                Log.Message("[MP-MeowOnlineShop] Trade auto-open routing ready: settlement, arrival and caravan encounter; manual viewing unchanged.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] REQUIRED_TARGET_FAILURE trade auto-open routing: " + e);
            }
        }

        private static HarmonyMethod Patch(string name) => new HarmonyMethod(typeof(Patch_TradeWindowAutoOpen), name);

        private static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions,
            MethodInfo target, string replacement, int expected)
        {
            var code = instructions.ToList();
            int count = code.Count(i => i.Calls(target));
            if (count != expected)
                throw new InvalidOperationException(replacement + ": expected " + expected + " calls, found " + count);
            foreach (var instruction in code)
            {
                if (instruction.Calls(target))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Patch_TradeWindowAutoOpen), replacement);
                }
            }
            return code;
        }

        internal static IEnumerable<CodeInstruction> TradeOpenTranspiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, openWindow, nameof(OpenAutomatically), 2);

        internal static IEnumerable<CodeInstruction> EncounterOpenTranspiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, AccessTools.Method(typeof(WindowStack), nameof(WindowStack.Add)),
                nameof(AddEncounterWindow), 1);

        internal static IEnumerable<CodeInstruction> ArrivalCameraTranspiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TryJumpAndSelect),
                    new[] { typeof(GlobalTargetInfo), typeof(CameraJumper.MovementMode) }),
                nameof(JumpForArrival), 1);

        private static bool OwnsAutomaticOpen => arrivalOwnedLocally ??
            (MP.IsExecutingSyncCommand && MP.IsExecutingSyncCommandIssuedBySelf);

        private static void OpenAutomatically(object trade, bool sound)
        {
            // The map/job branch already uses MP's tradeJobStartedByMe marker.
            if (Enabled && traderField.GetValue(trade) is Settlement && !OwnsAutomaticOpen)
                return;
            openWindow.Invoke(trade, new object[] { sound });
        }

        private static void AddEncounterWindow(WindowStack stack, Window window)
        {
            if (Enabled && tradingWindowType.IsInstanceOfType(window) && !OwnsAutomaticOpen)
                return;
            stack.Add(window);
        }

        private static void JumpForArrival(GlobalTargetInfo target, CameraJumper.MovementMode mode)
        {
            if (!Enabled || OwnsAutomaticOpen)
                CameraJumper.TryJumpAndSelect(target, mode);
        }

        private static void ResetForSession()
        {
            object session = sessionField.GetValue(null);
            if (ReferenceEquals(session, trackedSession)) return;
            trackedSession = session;
            routes.Clear();
        }

        private static void BeforeStartPath(Caravan ___caravan, CaravanArrivalAction arrivalAction, out PathState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand || Scribe.mode != LoadSaveMode.Inactive) return;
            ResetForSession();
            routes.TryGetValue(___caravan.ID, out var previous);
            __state = new PathState { CaravanId = ___caravan.ID, Previous = previous };
            // Record before StartPath: an already-arrived caravan calls Arrived inline.
            // Track even while disabled so changing the UI preference mid-route works.
            if (arrivalAction is CaravanArrivalAction_Trade trade && settlementField.GetValue(trade) is Settlement settlement)
                routes[___caravan.ID] = new RouteOwner { SettlementId = settlement.ID, Mine = MP.IsExecutingSyncCommandIssuedBySelf };
            else
                routes.Remove(___caravan.ID);
        }

        private static void RestorePath(PathState state)
        {
            if (state == null) return;
            if (state.Previous == null) routes.Remove(state.CaravanId);
            else routes[state.CaravanId] = state.Previous;
        }

        private static void AfterStartPath(bool __result, PathState __state)
        {
            if (!__result) RestorePath(__state);
        }

        private static Exception StartPathFinalizer(Exception __exception, PathState __state)
        {
            if (__exception != null) RestorePath(__state);
            return __exception;
        }

        private static void BeforeArrival(CaravanArrivalAction_Trade __instance, Caravan caravan, out bool? __state)
        {
            __state = arrivalOwnedLocally;
            if (!MP.IsInMultiplayer) return;
            ResetForSession();
            var settlement = settlementField.GetValue(__instance) as Settlement;
            arrivalOwnedLocally = MP.IsExecutingSyncCommand
                ? MP.IsExecutingSyncCommandIssuedBySelf
                : routes.TryGetValue(caravan.ID, out var owner) && owner.SettlementId == settlement?.ID && owner.Mine;
            routes.Remove(caravan.ID);
        }

        private static Exception AfterArrival(Exception __exception, bool? __state)
        {
            arrivalOwnedLocally = __state;
            return __exception;
        }
    }
}
