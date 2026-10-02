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
        private static readonly Dictionary<int, bool> flights = new Dictionary<int, bool>();
        private static bool? arrivalOwnedLocally;

        internal sealed class FlightState
        {
            internal Thing Shuttle;
            internal bool HadPrevious;
            internal bool Previous;
        }

        internal sealed class FlightArrivalState
        {
            internal bool? Previous;
            internal readonly List<int> ShuttleIds = new List<int>();
        }

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
                harmony.Patch(AccessTools.Method(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch),
                        new[] { typeof(PlanetTile), typeof(TransportersArrivalAction) }),
                    prefix: Patch(nameof(BeforeMapLaunch)), finalizer: Patch(nameof(AfterLaunch)));
                harmony.Patch(AccessTools.Method(typeof(CaravanShuttleUtility), nameof(CaravanShuttleUtility.LaunchShuttle),
                        new[] { typeof(Caravan), typeof(PlanetTile), typeof(TransportersArrivalAction) }),
                    prefix: Patch(nameof(BeforeWorldLaunch)), finalizer: Patch(nameof(AfterLaunch)),
                    transpiler: Patch(nameof(LaunchCameraTranspiler)));
                harmony.Patch(AccessTools.Method(typeof(TravellingTransporters), "Arrived"),
                    prefix: Patch(nameof(BeforeFlightArrival)), finalizer: Patch(nameof(AfterFlightArrival)));
                harmony.Patch(AccessTools.Method(typeof(TransportersArrivalAction_Trade), "Arrived",
                        new[] { typeof(List<ActiveTransporterInfo>), typeof(PlanetTile) }),
                    transpiler: Patch(nameof(ArrivalCameraTranspiler)));
                Log.Message("[MP-MeowOnlineShop] Trade auto-open routing ready: settlement, arrival and caravan encounter; manual viewing unchanged.");
                Log.Message("[MP-MeowOnlineShop] Shuttle camera routing ready: map/world launch ownership and delayed trade arrival.");
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

        internal static IEnumerable<CodeInstruction> LaunchCameraTranspiler(IEnumerable<CodeInstruction> instructions) =>
            ReplaceCalls(instructions, AccessTools.Method(typeof(CameraJumper), nameof(CameraJumper.TryJump),
                    new[] { typeof(GlobalTargetInfo), typeof(CameraJumper.MovementMode) }),
                nameof(JumpForLaunch), 1);

        private static void JumpForLaunch(GlobalTargetInfo target, CameraJumper.MovementMode mode)
        {
            if (!Enabled || OwnsAutomaticOpen)
                CameraJumper.TryJump(target, mode);
        }

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
            flights.Clear();
        }

        private static FlightState RememberLaunch(Thing shuttle)
        {
            if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand ||
                Scribe.mode != LoadSaveMode.Inactive || shuttle == null) return null;
            ResetForSession();
            var state = new FlightState { Shuttle = shuttle };
            state.HadPrevious = flights.TryGetValue(shuttle.thingIDNumber, out state.Previous);
            // Stable shuttle IDs survive skyfaller creation, world flight and save reload.
            // This is local UI ownership, never serialized into simulation state.
            flights[shuttle.thingIDNumber] = MP.IsExecutingSyncCommandIssuedBySelf;
            return state;
        }

        private static void BeforeMapLaunch(CompLaunchable __instance, out FlightState __state) =>
            __state = RememberLaunch(__instance.parent.HasComp<CompShuttle>() ? __instance.parent : null);

        private static void BeforeWorldLaunch(Caravan caravan, out FlightState __state) =>
            __state = RememberLaunch(caravan.Shuttle);

        private static Exception AfterLaunch(Exception __exception, FlightState __state)
        {
            // Failed/rejected launches must not replace the previous local owner.
            if (__state != null && (__exception != null || __state.Shuttle.Spawned || __state.Shuttle.IsInCaravan()))
            {
                if (__state.HadPrevious) flights[__state.Shuttle.thingIDNumber] = __state.Previous;
                else flights.Remove(__state.Shuttle.thingIDNumber);
            }
            return __exception;
        }

        private static void BeforeFlightArrival(List<ActiveTransporterInfo> ___transporters, out FlightArrivalState __state)
        {
            __state = new FlightArrivalState { Previous = arrivalOwnedLocally };
            if (!MP.IsInMultiplayer) return;
            ResetForSession();
            bool mine = true;
            foreach (var transporter in ___transporters)
            {
                var shuttle = transporter.GetShuttle();
                if (shuttle == null) continue;
                __state.ShuttleIds.Add(shuttle.thingIDNumber);
                mine &= flights.TryGetValue(shuttle.thingIDNumber, out bool owner) && owner;
            }
            // Unknown ownership (e.g. cold rejoin) must not move everybody's camera.
            arrivalOwnedLocally = __state.ShuttleIds.Count > 0 && mine;
        }

        private static Exception AfterFlightArrival(Exception __exception, FlightArrivalState __state)
        {
            foreach (int id in __state.ShuttleIds) flights.Remove(id);
            arrivalOwnedLocally = __state.Previous;
            return __exception;
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
