using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    internal static class Patch_PerspectiveShiftMp
    {
        private const string PackageId = "ferny.PerspectiveShift";

        private static Type stateType;
        private static Type avatarType;
        private static FieldInfo stateAvatarField;
        private static FieldInfo seekAtWillPawnsField;
        private static FieldInfo avatarPawnField;
        private static FieldInfo moveInputField;
        private static FieldInfo sprintField;
        private static FieldInfo walkField;
        private static FieldInfo physicsPositionField;
        private static FieldInfo aimAngleField;
        private static PropertyInfo controlsFrozenProperty;
        private static ConstructorInfo avatarPawnConstructor;
        private static MethodInfo avatarTickMethod;
        private static MethodInfo originalClearAvatarMethod;
        private static MethodInfo handleLeftClickIntMethod;
        private static MethodInfo handleFiringMethod;
        private static FieldInfo isAvatarLeftClickField;
        private static FieldInfo savedLordField;
        private static FieldInfo pendingMinifiedPickupField;
        private static FieldInfo interactingDoorField;
        private static FieldInfo wasFullyRestedField;
        private static FieldInfo passedOutField;
        private static FieldInfo needsAlertedField;
        private static FieldInfo cameraLockPositionField;
        private static FieldInfo isActiveCacheFrameField;
        private static FieldInfo asyncTickingMapField;
        private static AccessTools.FieldRef<object, Pawn> _avatarPawnRef;
        private static AccessTools.FieldRef<object, Pawn> _tweenerPawnRef;
        private static AccessTools.FieldRef<object, Pawn> _jobTrackerPawnRef;
        private static AccessTools.FieldRef<object, Pawn> _jobDriverPawnRef;
        private static AccessTools.FieldRef<object, IntVec3> _shootSourceOffsetRef;
        private static Func<object> _stateAvatarGetter;
        private static Action<object> _stateAvatarSetter;
        private static Func<object, object> _physicsPositionGetter;
        private static Game _componentCacheGame;
        private static PerspectiveShiftMpComponent _componentCache;
        private static bool _componentCacheValid;
        private static readonly Dictionary<Type, AccessTools.FieldRef<object, Pawn>> NestedPawnRefCache =
            new Dictionary<Type, AccessTools.FieldRef<object, Pawn>>();
        private static readonly object NestedPawnRefCacheLock = new object();

        private static int lastMoveX;
        private static int lastMoveZ;
        private static bool lastSprint;
        private static bool lastWalk;
        private static bool sentInput;
        private static float lastSentInputRealtime = -999f;
        private static int lastSentInputGameTick;
        private const float InputHeartbeatSeconds = 0.5f;

        [ThreadStatic] private static bool iteratingControlledAvatars;
        [ThreadStatic] private static bool clearingLocalView;
        [ThreadStatic] private static IntVec3? forcedMouseCell;
        [ThreadStatic] private static string executingCommandOwner;
        [ThreadStatic] private static int _localAvatarCacheFrame;
        [ThreadStatic] private static object _localAvatarCacheObject;
        [ThreadStatic] private static Pawn _localAvatarCachePawn;
        [ThreadStatic] private static int _ownerStateCacheFrame;
        [ThreadStatic] private static string _ownerStateCacheOwner;
        [ThreadStatic] private static PerspectiveShiftControlledAvatar _ownerStateCache;

        private const int SeekSeedSalt = 0x53454B57; // "SEKW"
        private const int SeekWorldSeedOffset = 0x53454B52; // "SEKR"
        private const string SeekGiverTypeName = "PerspectiveShift.JobGiver_SeekAtWill";
        private static bool _seekRandIsolationInstalled;

        private sealed class SeekRandContext
        {
            public int state;
            public Map map;
        }

        private sealed class AvatarContext
        {
            public object previous;
        }

        private struct RenderLeanContext
        {
            public PawnLeaner leaner;
            public IntVec3 shootSourceOffset;
        }

        private static bool commandOwnerResolutionWarningLogged;
        private static bool commandOwnerMismatchWarningLogged;
        private static bool localAvatarBindingLogged;
        private static bool localAvatarRecoveryLogged;
        private static bool localMovementInputLogged;
        private static bool incompleteMapClickWarningLogged;

        private static bool heldMoveForward;
        private static bool heldMoveBack;
        private static bool heldMoveLeft;
        private static bool heldMoveRight;
        private static bool heldSprint;
        private static bool heldWalk;
        private static readonly bool FireDiagnostics =
            string.Equals(
                Environment.GetEnvironmentVariable("MP_PS_FIRE_DIAG"),
                "1",
                StringComparison.OrdinalIgnoreCase);
        private static int _fireDiagnosticCount;

        private static void LogFireDiagnostic(string message)
        {
            if (!FireDiagnostics)
                return;
            if (System.Threading.Interlocked.Increment(ref _fireDiagnosticCount) > 20)
                return;
            Log.Message("[MP-MeowOnlineShop][PS-Fire] " + message);
        }

        private sealed class PendingMoveInput
        {
            public int sequence;
            public float sentRealtime;
            public int moveX;
            public int moveZ;
            public bool sprint;
            public bool walk;
        }

        private sealed class LocalPredictionState
        {
            public string owner;
            public int pawnId = -1;
            public int epoch = -1;
            public int mapId = -1;
            public int nextSequence = 1;
            public int lastAcknowledgedSequence;
            public float smoothedCommandDelay = 0.12f;
            public bool hasDelaySample;
            public readonly List<PendingMoveInput> pending =
                new List<PendingMoveInput>();
            public readonly List<PendingMoveInput> inputHistory =
                new List<PendingMoveInput>();
            public int historyBaseMoveX;
            public int historyBaseMoveZ;
            public bool historyBaseSprint;
            public bool historyBaseWalk;
            public Vector3 predictedPosition;
            public Vector3 predictionVelocity;
            public bool hasPredictedPosition;
            public Vector3 cameraPosition;
            public Vector3 cameraVelocity;
            public bool hasCameraPosition;

            public bool Matches(PerspectiveShiftControlledAvatar state)
            {
                return state != null && state.pawn != null &&
                       string.Equals(owner, state.owner, StringComparison.Ordinal) &&
                       pawnId == state.pawn.thingIDNumber && epoch == state.epoch &&
                       mapId == (state.pawn.Map?.uniqueID ?? -1);
            }

            public void Bind(PerspectiveShiftControlledAvatar state, bool resetVisuals)
            {
                if (state == null || state.pawn == null)
                {
                    Clear();
                    return;
                }

                bool changed = !Matches(state);
                if (changed)
                {
                    owner = state.owner;
                    pawnId = state.pawn.thingIDNumber;
                    epoch = state.epoch;
                    mapId = state.pawn.Map?.uniqueID ?? -1;
                    pending.Clear();
                    inputHistory.Clear();
                    lastAcknowledgedSequence = state.lastAppliedInputSequence;
                    historyBaseMoveX = state.moveX;
                    historyBaseMoveZ = state.moveZ;
                    historyBaseSprint = state.sprint;
                    historyBaseWalk = state.walk;
                    smoothedCommandDelay = 0.12f;
                    hasDelaySample = false;
                    resetVisuals = true;
                }

                nextSequence = Math.Max(
                    nextSequence,
                    state.lastAppliedInputSequence + 1);
                if (resetVisuals)
                {
                    hasPredictedPosition = false;
                    predictionVelocity = Vector3.zero;
                    hasCameraPosition = false;
                    cameraVelocity = Vector3.zero;
                }
            }

            public int Queue(
                PerspectiveShiftControlledAvatar state,
                int moveX,
                int moveZ,
                bool sprint,
                bool walk,
                float sentRealtime)
            {
                Bind(state, resetVisuals: false);
                int sequence = nextSequence++;
                var input = new PendingMoveInput
                {
                    sequence = sequence,
                    sentRealtime = sentRealtime,
                    moveX = moveX,
                    moveZ = moveZ,
                    sprint = sprint,
                    walk = walk
                };
                pending.Add(input);
                inputHistory.Add(input);
                if (pending.Count > 96)
                    pending.RemoveAt(0);
                if (inputHistory.Count > 192)
                {
                    PendingMoveInput removed = inputHistory[0];
                    historyBaseMoveX = removed.moveX;
                    historyBaseMoveZ = removed.moveZ;
                    historyBaseSprint = removed.sprint;
                    historyBaseWalk = removed.walk;
                    inputHistory.RemoveAt(0);
                }
                return sequence;
            }

            public void Acknowledge(
                int sequence,
                float now)
            {
                if (sequence <= lastAcknowledgedSequence)
                    return;

                PendingMoveInput acknowledged = pending
                    .FirstOrDefault(input => input.sequence == sequence);
                if (acknowledged != null)
                {
                    float sample = Mathf.Clamp(
                        now - acknowledged.sentRealtime,
                        0.01f,
                        2f);
                    smoothedCommandDelay = hasDelaySample
                        ? Mathf.Lerp(smoothedCommandDelay, sample, 0.2f)
                        : sample;
                    hasDelaySample = true;
                }

                pending.RemoveAll(input => input.sequence <= sequence);
                lastAcknowledgedSequence = sequence;
                nextSequence = Math.Max(nextSequence, sequence + 1);
            }

            public float PredictionHorizon
            {
                get
                {
                    // While acknowledgements stall, their previous average
                    // understates the current delay. Extend visual replay to
                    // the oldest outstanding input, keeping the existing cap.
                    float outstandingAge = pending.Count == 0 ? 0f :
                        Mathf.Max(0f, Time.realtimeSinceStartup - pending[0].sentRealtime);
                    return Mathf.Clamp(Mathf.Max(smoothedCommandDelay, outstandingAge), 0.05f, 1f);
                }
            }

            public void Clear()
            {
                owner = null;
                pawnId = -1;
                epoch = -1;
                mapId = -1;
                nextSequence = 1;
                lastAcknowledgedSequence = 0;
                smoothedCommandDelay = 0.12f;
                hasDelaySample = false;
                pending.Clear();
                inputHistory.Clear();
                historyBaseMoveX = 0;
                historyBaseMoveZ = 0;
                historyBaseSprint = false;
                historyBaseWalk = false;
                hasPredictedPosition = false;
                predictionVelocity = Vector3.zero;
                hasCameraPosition = false;
                cameraVelocity = Vector3.zero;
            }
        }

        private struct CameraPredictionSwap
        {
            public bool active;
            public Vector3? previousPhysicsPosition;
        }

        private static readonly LocalPredictionState localPrediction =
            new LocalPredictionState();

        public static bool Active { get; private set; }

        public static void Apply(Harmony harmony)
        {
            bool configuredActive = ModsConfig.IsActive(PackageId);
            stateType = AccessTools.TypeByName("PerspectiveShift.State");
            avatarType = AccessTools.TypeByName("PerspectiveShift.Avatar");
            if (!configuredActive && stateType == null && avatarType == null)
                return;

            if (!configuredActive)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Perspective Shift types are loaded although ModsConfig.IsActive " +
                    "did not report its package ID; enabling compatibility from the loaded API shape.");
            }

            if (stateType == null || avatarType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Perspective Shift detected, but its State/Avatar types were not found.");
                return;
            }

            stateAvatarField = AccessTools.Field(stateType, "Avatar");
            seekAtWillPawnsField = AccessTools.Field(stateType, "seekAtWillPawns");
            avatarPawnField = AccessTools.Field(avatarType, "pawn");
            moveInputField = AccessTools.Field(avatarType, "moveInput");
            sprintField = AccessTools.Field(avatarType, "isSprinting");
            walkField = AccessTools.Field(avatarType, "isWalking");
            physicsPositionField = AccessTools.Field(avatarType, "physicsPosition");
            aimAngleField = AccessTools.Field(avatarType, "aimAngle");
            controlsFrozenProperty = AccessTools.Property(stateType, "ControlsFrozen");
            avatarPawnConstructor = AccessTools.Constructor(avatarType, new[] { typeof(Pawn) });
            avatarTickMethod = AccessTools.Method(avatarType, "Tick");
            originalClearAvatarMethod = AccessTools.Method(stateType, "ClearAvatar");
            handleLeftClickIntMethod = AccessTools.Method(avatarType, "HandleLeftClickInt");
            handleFiringMethod = AccessTools.Method(avatarType, "HandleFiring");
            isAvatarLeftClickField = AccessTools.Field(avatarType, "IsAvatarLeftClick");
            savedLordField = AccessTools.Field(avatarType, "savedLord");
            pendingMinifiedPickupField = AccessTools.Field(avatarType, "pendingMinifiedPickup");
            interactingDoorField = AccessTools.Field(avatarType, "interactingDoor");
            wasFullyRestedField = AccessTools.Field(avatarType, "wasFullyRested");
            passedOutField = AccessTools.Field(avatarType, "passedOut");
            needsAlertedField = AccessTools.Field(avatarType, "needsAlerted");
            cameraLockPositionField = AccessTools.Field(stateType, "CameraLockPosition");
            isActiveCacheFrameField = AccessTools.Field(stateType, "_isActiveCacheFrame");
            Type asyncTimeType = AccessTools.TypeByName("Multiplayer.Client.AsyncTimeComp");
            asyncTickingMapField = asyncTimeType == null
                ? null
                : AccessTools.Field(asyncTimeType, "tickingMap");
            _avatarPawnRef = TryGetInstanceFieldRef<Pawn>(avatarType, "pawn");
            _stateAvatarGetter = TryCompileStaticFieldGetter(stateAvatarField);
            _stateAvatarSetter = TryCompileStaticFieldSetter(stateAvatarField);
            _physicsPositionGetter = TryCompileInstanceFieldGetter(avatarType, physicsPositionField);
            _jobTrackerPawnRef = TryGetInstanceFieldRef<Pawn>(typeof(Pawn_JobTracker), "pawn");
            _jobDriverPawnRef = TryGetInstanceFieldRef<Pawn>(typeof(JobDriver), "pawn");
            _shootSourceOffsetRef = TryGetInstanceFieldRef<IntVec3>(typeof(PawnLeaner), "shootSourceOffset");

            MethodInfo setAvatar = AccessTools.Method(stateType, "SetAvatar", new[] { typeof(Pawn), typeof(bool) });
            MethodInfo clearAvatar = originalClearAvatarMethod;
            MethodInfo revokeControl = AccessTools.Method(stateType, "RevokeControl",
                new[] { typeof(Pawn), typeof(DamageInfo?), typeof(Hediff) });
            MethodInfo stateUpdate = AccessTools.Method(stateType, "Update");
            MethodInfo stateTick = AccessTools.Method(stateType, "Tick");
            MethodInfo stateOnGui = AccessTools.Method(stateType, "OnGUI");
            MethodInfo isAvatar = AccessTools.Method(stateType, "IsAvatar", new[] { typeof(Pawn) });
            MethodInfo updatePhysics = AccessTools.Method(avatarType, "UpdatePhysics");
            MethodInfo updateCamera = AccessTools.Method(avatarType, "UpdateCamera");
            MethodInfo renderPawn = AccessTools.Method(avatarType, "RenderPawn");

            if (stateAvatarField == null || avatarPawnConstructor == null || setAvatar == null ||
                clearAvatar == null || revokeControl == null || stateUpdate == null || stateTick == null || stateOnGui == null ||
                isAvatar == null || updatePhysics == null || renderPawn == null || _shootSourceOffsetRef == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Perspective Shift API shape is unsupported; compatibility patch skipped.");
                return;
            }

            harmony.Patch(setAvatar, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(SetAvatarPrefix)));
            harmony.Patch(clearAvatar, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(ClearAvatarPrefix)));
            harmony.Patch(revokeControl, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(RevokeControlPrefix)));
            harmony.Patch(stateUpdate, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(StateUpdatePrefix)));
            harmony.Patch(stateTick, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(StateTickPrefix)));
            harmony.Patch(AccessTools.Method(typeof(Map), nameof(Map.MapPostTick)),
                postfix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(AvatarMapPostTick)));
            harmony.Patch(stateOnGui, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(StateOnGuiPrefix)));
            harmony.Patch(isAvatar, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(IsAvatarPrefix)));
            MethodInfo draftedSetter = AccessTools.PropertySetter(
                typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted));
            if (draftedSetter != null)
                harmony.Patch(draftedSetter,
                    prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(CaptureDraftedState)),
                    postfix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(ReleaseAvatarOnUndraft)));
            else
                Log.Warning("[MP-MeowOnlineShop] Perspective Shift undraft release target not resolved.");
            harmony.Patch(updatePhysics, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(UpdatePhysicsPrefix)));
            harmony.Patch(renderPawn,
                prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(RenderPawnPrefix)),
                finalizer: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(RenderPawnFinalizer)));
            if (updateCamera != null)
            {
                harmony.Patch(
                    updateCamera,
                    prefix: new HarmonyMethod(
                        typeof(Patch_PerspectiveShiftMp),
                        nameof(UpdateCameraPrefix)),
                    postfix: new HarmonyMethod(
                        typeof(Patch_PerspectiveShiftMp),
                        nameof(UpdateCameraPostfix)));
            }

            MethodInfo handleSelectorClick = AccessTools.Method(avatarType, "HandleSelectorClick");
            if (handleSelectorClick != null)
                harmony.Patch(handleSelectorClick, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(HandleSelectorClickPrefix)));

            if (handleFiringMethod != null)
                harmony.Patch(handleFiringMethod, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(HandleFiringPrefix)));

            MethodInfo warmupTimeGetter = AccessTools.PropertyGetter(typeof(Verb), nameof(Verb.WarmupTime));
            if (warmupTimeGetter != null)
                harmony.Patch(warmupTimeGetter, postfix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(WarmupTimePostfix)));

            MethodInfo tryHandleStorageBuilding = AccessTools.Method(
                avatarType,
                "TryHandleStorageBuilding",
                new[] { typeof(Thing) });
            if (tryHandleStorageBuilding != null)
            {
                harmony.Patch(
                    tryHandleStorageBuilding,
                    prefix: new HarmonyMethod(
                        typeof(Patch_PerspectiveShiftMp),
                        nameof(TryHandleStorageBuildingPrefix)));
            }

            MethodInfo mouseCell = AccessTools.Method(typeof(UI), nameof(UI.MouseCell));
            if (mouseCell != null)
                harmony.Patch(mouseCell, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(MouseCellPrefix)));

            PatchExecutingCommandOwnerContext(harmony);
            PatchCameraLockCallbacks(harmony);
            DisableSimulationUseOfPredictedPosition(harmony);
            NeutralizeSingletonSimulationPatches(harmony);

            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncClaimAvatar))
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncClaimAvatarWithMode))
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncReleaseAvatar))
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncSetMoveIntent))
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncAvatarMapClick))
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncFireAvatar))
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncAimAndFireAvatar))
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncToggleSeekAtWill))
                .SetContext(SyncContext.None);

            PatchSeekAtWillSync(harmony);

            Active = true;
            Log.Message(
                "[MP-MeowOnlineShop] Perspective Shift firing patch targets: " +
                $"handleFiring={handleFiringMethod != null}, " +
                $"handleSelectorClick={handleSelectorClick != null}, " +
                $"handleLeftClickInt={handleLeftClickIntMethod != null}, " +
                $"warmupPostfix={warmupTimeGetter != null}, " +
                "syncFireAvatar=registered");
            Log.Message(
                "[MP-MeowOnlineShop] Perspective Shift MP: multi-avatar registry, " +
                "ordered sequence-acknowledged movement, replay prediction, and " +
                "independent camera smoothing enabled.");
        }

        private static void PatchExecutingCommandOwnerContext(Harmony harmony)
        {
            int patched = 0;
            string[] executorTypes =
            {
                "Multiplayer.Client.AsyncTimeComp",
                "Multiplayer.Client.AsyncTime.AsyncWorldTimeComp"
            };

            foreach (string typeName in executorTypes)
            {
                Type executorType = AccessTools.TypeByName(typeName);
                MethodInfo execute = executorType == null
                    ? null
                    : AccessTools.GetDeclaredMethods(executorType)
                        .FirstOrDefault(method =>
                            method.Name == "ExecuteCmd" &&
                            method.GetParameters().Length == 1 &&
                            method.GetParameters()[0].ParameterType.FullName ==
                                "Multiplayer.Common.ScheduledCommand");
                if (execute == null)
                    continue;

                harmony.Patch(
                    execute,
                    prefix: new HarmonyMethod(
                        typeof(Patch_PerspectiveShiftMp),
                        nameof(ExecutingCommandOwnerPrefix)),
                    finalizer: new HarmonyMethod(
                        typeof(Patch_PerspectiveShiftMp),
                        nameof(ExecutingCommandOwnerFinalizer)));
                patched++;
            }

            if (patched != executorTypes.Length)
            {
                Log.Warning(
                    $"[MP-MeowOnlineShop] Perspective Shift MP: command-owner context resolved " +
                    $"{patched}/{executorTypes.Length} Multiplayer executors. Avatar claim/release " +
                    "will fail closed when the issuing player cannot be identified.");
            }
            else
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Perspective Shift MP: synchronized command issuer context enabled.");
            }
        }

        private static void PatchCameraLockCallbacks(Harmony harmony)
        {
            string[] patchTypes =
            {
                "PerspectiveShift.CameraDriver_PanToMapLoc_Patch",
                "PerspectiveShift.CameraDriver_JumpToCurrentMapLoc_Patch"
            };

            foreach (string typeName in patchTypes)
            {
                Type patchType = AccessTools.TypeByName(typeName);
                MethodInfo postfix = patchType == null ? null : AccessTools.Method(patchType, "Postfix");
                if (postfix != null)
                {
                    harmony.Patch(
                        postfix,
                        prefix: new HarmonyMethod(
                            typeof(Patch_PerspectiveShiftMp),
                            nameof(CameraLockCallbackPrefix)));
                }
            }
        }

        public static bool CameraLockCallbackPrefix()
        {
            // Multiplayer restores map/camera context after a synchronized command.
            // Perspective Shift mistakes that internal jump for an intentional local
            // camera lock, which leaves the view pinned after taking control.
            return !MP.IsInMultiplayer || !Active || !MP.IsExecutingSyncCommand;
        }

        public static void StateOnGuiPrefix()
        {
            if (!MP.IsInMultiplayer || !Active)
                return;

            EnsureLocalAvatarView("OnGUI");
            if (LocalAvatarPawn() == null)
            {
                ClearHeldMovementKeys();
                return;
            }

            Event current = Event.current;
            if (current == null ||
                (current.type != EventType.KeyDown && current.type != EventType.KeyUp))
                return;

            bool held = current.type == EventType.KeyDown;
            KeyCode code = current.keyCode;
            if (BindingMatches("PS_MoveForward", code)) heldMoveForward = held;
            if (BindingMatches("PS_MoveBack", code)) heldMoveBack = held;
            if (BindingMatches("PS_MoveLeft", code)) heldMoveLeft = held;
            if (BindingMatches("PS_MoveRight", code)) heldMoveRight = held;
            if (BindingMatches("PS_Sprint", code)) heldSprint = held;
            if (BindingMatches("PS_Walk", code)) heldWalk = held;
        }

        public static void ExecutingCommandOwnerPrefix(object[] __args, out string __state)
        {
            __state = executingCommandOwner;
            executingCommandOwner = ResolveCommandOwner(__args != null && __args.Length > 0
                ? __args[0]
                : null);
        }

        public static Exception ExecutingCommandOwnerFinalizer(Exception __exception, string __state)
        {
            executingCommandOwner = __state;
            return __exception;
        }

        private static string ResolveCommandOwner(object command)
        {
            if (command == null)
                return null;

            object playerIdValue = AccessTools.Field(command.GetType(), "playerId")?.GetValue(command)
                                   ?? AccessTools.Property(command.GetType(), "playerId")?.GetValue(command, null);
            if (!(playerIdValue is int playerId))
                return null;

            Type multiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
            if (multiplayerType == null)
                return null;

            object session = AccessTools.Field(multiplayerType, "session")?.GetValue(null)
                             ?? AccessTools.Property(multiplayerType, "session")?.GetValue(null, null);
            object playersObject = session == null
                ? null
                : AccessTools.Field(session.GetType(), "players")?.GetValue(session)
                  ?? AccessTools.Property(session.GetType(), "players")?.GetValue(session, null);

            if (!(playersObject is IEnumerable players))
                return null;

            foreach (object player in players)
            {
                if (player == null)
                    continue;

                object idValue = AccessTools.Field(player.GetType(), "id")?.GetValue(player)
                                 ?? AccessTools.Property(player.GetType(), "Id")?.GetValue(player, null);
                if (!(idValue is int id) || id != playerId)
                    continue;

                return AccessTools.Field(player.GetType(), "username")?.GetValue(player) as string
                       ?? AccessTools.Property(player.GetType(), "Username")?.GetValue(player, null) as string;
            }

            return null;
        }

        private static void DisableSimulationUseOfPredictedPosition(Harmony harmony)
        {
            Type patchType = AccessTools.TypeByName("PerspectiveShift.CellFinder_TryFindBestPawnStandCell_Patch");
            MethodInfo targetPrefix = patchType == null ? null : AccessTools.Method(patchType, "Prefix");
            if (targetPrefix != null)
            {
                harmony.Patch(
                    targetPrefix,
                    prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(BypassPerspectiveShiftCellFinderPrefix))
                    {
                        priority = Priority.First
                    });
            }

            Type tweenerPatchType = AccessTools.TypeByName("PerspectiveShift.PawnTweener_PreDrawPosCalculation_Patch");
            MethodInfo tweenerPrefix = tweenerPatchType == null ? null : AccessTools.Method(tweenerPatchType, "Prefix");
            Type tweenerType = AccessTools.TypeByName("PerspectiveShift.PawnTweener");
            _tweenerPawnRef = TryGetInstanceFieldRef<Pawn>(tweenerType, "pawn");
            if (tweenerPrefix != null)
            {
                harmony.Patch(
                    tweenerPrefix,
                    prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(ReplacePerspectiveShiftTweenerPrefix))
                    {
                        priority = Priority.First
                    });
            }
        }

        private static void NeutralizeSingletonSimulationPatches(Harmony harmony)
        {
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Need_Food_FoodFallPerTickAssumingCategory_Patch", "Postfix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Need_Rest_RestFallPerTick_Patch", "Postfix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Pawn_PathFollower_Moving_Patch", "Postfix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.PawnLeaner_ShouldLean_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Pawn_PathFollower_DrawPath_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Pawn_RotationTracker_UpdateRotation_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Pawn_WorkSettings_GetPriority_Patch", "Postfix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.JobDriver_ManTurret_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.JobDriver_Wait_CheckForAutoAttack_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.GenClosest_MaxDistance_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.HaulAIUtility_PawnCanAutomaticallyHaul_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Verb_WarmupTime_Patch", "Postfix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.WorkGiver_DoBill_JobOnThing_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.MedicalCareUtility_AllowsMedicine_Patch", "Postfix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.DrawLinesBetweenTargets_Patch", "Prefix");
            PatchTargetBoolPrefixForLocalAvatar(
                harmony,
                "PerspectiveShift.PawnLeaner_LeanOffset_Patch",
                "Prefix");
            PatchTargetBoolPrefixForLocalAvatar(
                harmony,
                "PerspectiveShift.PawnRenderUtility_DrawEquipmentAndApparelExtras_Patch",
                "Prefix");

            MethodInfo handlePather = AccessTools.Method(avatarType, "HandlePather");
            if (handlePather != null)
                harmony.Patch(handlePather, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(SkipInMultiplayerPrefix)));
            MethodInfo autoPickup = AccessTools.Method(avatarType, "TryAutoPickupMinifiedItem");
            if (autoPickup != null)
                harmony.Patch(autoPickup, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(SkipInMultiplayerPrefix)));

            Type endJobPatch = AccessTools.TypeByName("PerspectiveShift.Pawn_JobTracker_EndCurrentJob_Patch");
            MethodInfo endJobPostfix = endJobPatch == null ? null : AccessTools.Method(endJobPatch, "Postfix");
            if (endJobPostfix != null)
            {
                harmony.Patch(
                    endJobPostfix,
                    prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(EndJobPostfixContextPrefix)),
                    finalizer: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(EndJobPostfixContextFinalizer)));
            }

            MethodInfo checkForAutoAttack = AccessTools.Method(
                typeof(JobDriver_Wait),
                "CheckForAutoAttack");
            if (checkForAutoAttack != null)
            {
                harmony.Patch(
                    checkForAutoAttack,
                    prefix: new HarmonyMethod(
                        typeof(Patch_PerspectiveShiftMp),
                        nameof(ControlledWaitAutoAttackPrefix))
                    {
                        priority = Priority.First
                    });
            }
        }

        private static void PatchTargetPatchMethod(Harmony harmony, string typeName, string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
            if (method != null)
            {
                string prefixName = method.ReturnType == typeof(bool)
                    ? nameof(SuppressTargetBoolPatchPrefix)
                    : nameof(SkipInMultiplayerPrefix);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), prefixName));
            }
        }

        private static void PatchTargetBoolPrefixForLocalAvatar(
            Harmony harmony,
            string typeName,
            string methodName)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
            if (method != null)
            {
                harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(
                        typeof(Patch_PerspectiveShiftMp),
                        nameof(LocalAvatarOnlyTargetBoolPrefix)));
            }
        }

        private static void PatchSeekAtWillSync(Harmony harmony)
        {
            if (harmony == null || stateType == null || seekAtWillPawnsField == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Perspective Shift seek-at-will sync " +
                    "targets unresolved; skipped.");
                return;
            }

            MethodInfo getGizmos = AccessTools.Method(
                typeof(Pawn),
                nameof(Pawn.GetGizmos),
                Type.EmptyTypes);
            MethodInfo gizmoPostfix = AccessTools.Method(
                typeof(Patch_PerspectiveShiftMp),
                nameof(SeekAtWillGizmoPostfix));
            if (getGizmos != null && gizmoPostfix != null)
            {
                harmony.Patch(
                    getGizmos,
                    postfix: new HarmonyMethod(gizmoPostfix)
                    {
                        priority = Priority.Last
                    });
            }

            MethodInfo shouldSeek = AccessTools.Method(
                stateType,
                "ShouldSeekEnemy",
                new[] { typeof(Pawn) });
            MethodInfo shouldSeekPrefix = AccessTools.Method(
                typeof(Patch_PerspectiveShiftMp),
                nameof(ShouldSeekEnemyPrefix));
            if (shouldSeek != null && shouldSeekPrefix != null)
            {
                harmony.Patch(
                    shouldSeek,
                    prefix: new HarmonyMethod(shouldSeekPrefix)
                    {
                        priority = Priority.First
                    });
            }

            PatchSeekAtWillRandIsolation(harmony);

            Log.Message(
                "[MP-MeowOnlineShop] Perspective Shift seek-at-will sync active.");
        }

        private static void PatchSeekAtWillRandIsolation(Harmony harmony)
        {
            if (harmony == null || _seekRandIsolationInstalled)
                return;

            Type seekGiverType = AccessTools.TypeByName(SeekGiverTypeName);
            MethodInfo tryGiveJob = seekGiverType == null
                ? null
                : AccessTools.Method(seekGiverType, "TryGiveJob", new[] { typeof(Pawn) });
            if (tryGiveJob == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Perspective Shift seek-at-will job " +
                    "Rand isolation target not resolved; skipped.");
                return;
            }

            _seekRandIsolationInstalled = true;
            harmony.Patch(
                tryGiveJob,
                prefix: new HarmonyMethod(
                    typeof(Patch_PerspectiveShiftMp),
                    nameof(SeekAtWillTryGiveJobPrefix))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(
                    typeof(Patch_PerspectiveShiftMp),
                    nameof(SeekAtWillTryGiveJobFinalizer))
                {
                    priority = Priority.Last
                });

            Log.Message(
                "[MP-MeowOnlineShop] Perspective Shift seek-at-will job Rand " +
                "isolation active.");
        }

        private static void SeekAtWillTryGiveJobPrefix(
            Pawn pawn,
            out SeekRandContext __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || pawn == null || pawn.Map == null)
                return;

            int seed = Gen.HashCombineInt(SeekSeedSalt, pawn.Map.uniqueID);
            seed = Gen.HashCombineInt(seed, pawn.thingIDNumber);
            seed = Gen.HashCombineInt(seed, Find.TickManager.TicksGame);

            var context = new SeekRandContext();
            if (DeterministicRandScope.Begin(
                    pawn.Map,
                    seed,
                    SeekWorldSeedOffset,
                    ref context.state,
                    out context.map,
                    ignoreGate: true))
            {
                __state = context;
            }
        }

        private static Exception SeekAtWillTryGiveJobFinalizer(
            Exception __exception,
            SeekRandContext __state)
        {
            if (__state != null)
                DeterministicRandScope.End(__state.state, __state.map);
            return __exception;
        }

        public static void SyncToggleSeekAtWill(string owner, Pawn pawn)
        {
            if (!ValidateSynchronizedOwner(owner, nameof(SyncToggleSeekAtWill)))
                return;

            PerspectiveShiftMpComponent comp = CurrentComponent();
            if (comp == null || pawn == null)
                return;

            comp.ToggleSeekAtWill(pawn);
            SyncStaticSeekAtWillFromComponent();
        }

        public static void SyncStaticSeekAtWillFromComponent()
        {
            if (seekAtWillPawnsField == null)
                return;

            PerspectiveShiftMpComponent comp = CurrentComponent();
            HashSet<int> set =
                seekAtWillPawnsField.GetValue(null) as HashSet<int> ??
                new HashSet<int>();
            set.Clear();
            if (comp?.seekAtWillPawnIds != null)
            {
                foreach (int id in comp.seekAtWillPawnIds)
                    set.Add(id);
            }
            seekAtWillPawnsField.SetValue(null, set);
        }

        public static IEnumerable<Gizmo> SeekAtWillGizmoPostfix(
            IEnumerable<Gizmo> gizmos,
            Pawn __instance)
        {
            if (!MP.IsInMultiplayer || !Active || gizmos == null ||
                seekAtWillPawnsField == null)
            {
                if (gizmos == null)
                    yield break;
                foreach (Gizmo gizmo in gizmos)
                    yield return gizmo;
                yield break;
            }

            TaggedString seekLabel = Translator.Translate("PS_SeekAtWill");
            foreach (Gizmo gizmo in gizmos)
            {
                if (gizmo is Command_Toggle toggle &&
                    !MP.IsExecutingSyncCommand &&
                    toggle.defaultLabel == seekLabel)
                {
                    Pawn pawn = __instance;
                    toggle.toggleAction = () => SyncToggleSeekAtWill(
                        MP.PlayerName,
                        pawn);
                    toggle.isActive = () =>
                        CurrentComponent()?.IsSeekAtWill(pawn) ?? false;
                }
                yield return gizmo;
            }
        }

        public static bool ShouldSeekEnemyPrefix(Pawn pawn, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active || pawn == null)
                return true;

            PerspectiveShiftMpComponent comp = CurrentComponent();
            if (comp == null)
                return true;

            __result = comp.IsSeekAtWill(pawn);
            return false;
        }

        public static void SyncClaimAvatar(string owner, Pawn pawn)
        {
            if (!ValidateSynchronizedOwner(owner, nameof(SyncClaimAvatar)))
                return;
            CurrentComponent()?.Claim(owner, pawn);
            InvalidateControlledCache();
        }

        public static void SyncClaimAvatarWithMode(string owner, Pawn pawn, bool retainOnDowned)
        {
            if (!ValidateSynchronizedOwner(owner, nameof(SyncClaimAvatarWithMode)))
                return;
            PerspectiveShiftMpComponent component = CurrentComponent();
            component?.Claim(owner, pawn);
            PerspectiveShiftControlledAvatar state = component?.ForOwner(owner);
            if (state != null && state.pawn == pawn)
                state.retainOnDowned = retainOnDowned;
            InvalidateControlledCache();
        }

        private static bool LocalRetainOnDowned()
        {
            // Capture only at local input; never read local mode in simulation.
            if (MP.IsExecutingSyncCommand || !MP.InInterface)
                return CurrentComponent()?.ForOwner(executingCommandOwner)?.retainOnDowned ?? false;
            return string.Equals(AccessTools.Field(stateType, "CurrentMode")?.GetValue(null)?.ToString(),
                "Authentic", StringComparison.Ordinal);
        }

        public static void SyncReleaseAvatar(string owner, int epoch)
        {
            if (!ValidateSynchronizedOwner(owner, nameof(SyncReleaseAvatar)))
                return;
            CurrentComponent()?.Release(owner, epoch);
            InvalidateControlledCache();
        }

        public static void SyncSetMoveIntent(
            string owner,
            Pawn pawn,
            int epoch,
            int inputSequence,
            int moveX,
            int moveZ,
            bool sprint,
            bool walk)
        {
            if (!ValidateSynchronizedOwner(owner, nameof(SyncSetMoveIntent)))
                return;

            PerspectiveShiftMpComponent component = CurrentComponent();
            PerspectiveShiftControlledAvatar state = component?.EnsureState(
                owner,
                pawn,
                epoch);
            if (component == null || state == null)
                return;

            component.SetMoveIntent(
                owner,
                pawn,
                state.epoch,
                inputSequence,
                moveX,
                moveZ,
                sprint,
                walk);
        }

        public static void NotifyMoveIntentApplied(
            PerspectiveShiftControlledAvatar state,
            int inputSequence)
        {
            if (!MP.IsInMultiplayer || !IsExclusiveLocalOwnership(state) ||
                !localPrediction.Matches(state))
            {
                return;
            }

            // The ordered command returning to its originating client is the
            // strongest available acknowledgement: it includes transport,
            // server scheduling, and the client's command-queue delay.
            localPrediction.Acknowledge(
                inputSequence,
                Time.realtimeSinceStartup);
        }

        public static void SyncAvatarMapClick(string owner, Pawn pawn, int epoch, IntVec3 cell, int button, int clickCount)
        {
            if (!ValidateSynchronizedOwner(owner, nameof(SyncAvatarMapClick)))
                return;

            PerspectiveShiftControlledAvatar state = CurrentComponent()?.EnsureState(
                owner,
                pawn,
                epoch);
            if (state == null || state.pawn != pawn || state.epoch != epoch || pawn == null || pawn.Dead)
                return;

            EnsureRuntimeAvatar(state);
            if (!Active || stateAvatarField == null || state.runtimeAvatar == null)
            {
                if (!incompleteMapClickWarningLogged)
                {
                    incompleteMapClickWarningLogged = true;
                    Log.Error(
                        "[MP-MeowOnlineShop] Perspective Shift synchronized map click was rejected " +
                        "because compatibility initialization is incomplete.");
                }
                return;
            }

            object previousAvatar = stateAvatarField.GetValue(null);
            Event previousEvent = Event.current;
            forcedMouseCell = cell;
            stateAvatarField.SetValue(null, state.runtimeAvatar);
            try
            {
                Event.current = new Event
                {
                    type = EventType.MouseDown,
                    button = button,
                    clickCount = Math.Max(1, clickCount)
                };

                if (button == 0)
                {
                    if (pawn.Drafted)
                    {
                        SyncFireAvatar(owner, pawn, epoch, cell);
                    }
                    else if (handleLeftClickIntMethod != null)
                    {
                        isAvatarLeftClickField?.SetValue(null, true);
                        handleLeftClickIntMethod.Invoke(state.runtimeAvatar, null);
                    }
                }
                else if (button == 1)
                {
                    state.pendingFire = false;
                    state.pendingFireTarget = LocalTargetInfo.Invalid;
                    Thing carried = pawn.carryTracker?.CarriedThing;
                    if (!pawn.Drafted && carried != null && pawn.inventory != null &&
                        !(carried is Pawn) && !(carried is Corpse))
                    {
                        pawn.carryTracker.innerContainer.TryTransferToContainer(
                            carried,
                            pawn.inventory.innerContainer,
                            carried.stackCount);
                    }
                    else if (pawn.jobs?.curJob != null && pawn.jobs.curJob.def.playerInterruptible)
                    {
                        pawn.jobs.EndCurrentJob(
                            JobCondition.InterruptForced,
                            startNewJob: false);
                    }
                }
            }
            catch (TargetInvocationException e)
            {
                Log.Error("[MP-MeowOnlineShop] Perspective Shift synchronized map click failed: " +
                          (e.InnerException ?? e));
            }
            finally
            {
                isAvatarLeftClickField?.SetValue(null, false);
                forcedMouseCell = null;
                Event.current = previousEvent;
                RestoreLocalAvatarContext(previousAvatar);
            }
        }

        public static void SyncFireAvatar(string owner, Pawn pawn, int epoch, IntVec3 cell)
        {
            LocalTargetInfo target = pawn?.Map != null && cell.InBounds(pawn.Map)
                ? ResolvePerspectiveFireTarget(pawn, cell) : LocalTargetInfo.Invalid;
            SyncAimAndFireAvatar(owner, pawn, epoch, target);
        }

        public static void SyncAimAndFireAvatar(string owner, Pawn pawn, int epoch, LocalTargetInfo aimedTarget)
        {
            if (!ValidateSynchronizedOwner(owner, nameof(SyncAimAndFireAvatar)))
                return;
            PerspectiveShiftControlledAvatar state = CurrentComponent()?.EnsureState(owner, pawn, epoch);
            if (state == null)
                return;
            // The newest intent supersedes the old one even if its target
            // disappeared in transit; never execute an older buffered aim.
            state.pendingFire = false;
            state.pendingFireTarget = LocalTargetInfo.Invalid;
            if (pawn.Map == null || !aimedTarget.IsValid || !aimedTarget.Cell.InBounds(pawn.Map))
                return;
            // One bounded intent, not an unbounded backlog of delayed shots.
            // Capture target identity at input time so latency cannot select a
            // different pawn that later walks into the clicked cell. MP's
            // LocalTargetInfo serializer preserves thing-vs-cell identity:
            // an unresolved Thing becomes Invalid, never a ground shot.
            state.pendingFire = true;
            state.pendingFireTarget = aimedTarget;
            state.pendingFireUntil = Find.TickManager.TicksGame + 60;
            ProcessPendingFire(state);
        }

        internal static void ProcessPendingFire(PerspectiveShiftControlledAvatar state)
        {
            if (!state.pendingFire)
                return;
            Pawn pawn = state.pawn;
            if (!state.pendingFireTarget.IsValid || pawn == null || pawn.Dead || pawn.Downed || !pawn.Spawned ||
                pawn.InMentalState || !pawn.Drafted || pawn.CurJob?.ability != null ||
                Find.TickManager.TicksGame > state.pendingFireUntil)
            {
                state.pendingFire = false;
                state.pendingFireTarget = LocalTargetInfo.Invalid;
                return;
            }
            if (pawn.stances?.curStance is Stance_Busy)
                return;
            LocalTargetInfo intent = state.pendingFireTarget;
            Thing target = intent.Thing;
            IntVec3 cell = intent.Cell;
            state.pendingFire = false;
            state.pendingFireTarget = LocalTargetInfo.Invalid;
            if (target != null && (target.Destroyed || !target.Spawned || target.Map != pawn.Map))
                return;
            ExecuteFireAvatar(state.owner, pawn, state.epoch, cell, target);
        }

        private static void ExecuteFireAvatar(string owner, Pawn pawn, int epoch, IntVec3 cell, Thing aimedTarget)
        {
            LogFireDiagnostic(
                $"enter owner={owner} pawn={(pawn != null ? pawn.thingIDNumber.ToString() : "null")} " +
                $"cell={cell} drafted={(pawn != null && pawn.Drafted)}");
            PerspectiveShiftControlledAvatar state = CurrentComponent()?.EnsureState(
                owner,
                pawn,
                epoch);
            if (state == null || state.pawn != pawn || state.epoch != epoch ||
                pawn == null || pawn.Dead || pawn.Downed || pawn.InMentalState ||
                pawn.Map == null || !cell.InBounds(pawn.Map) || !pawn.Drafted)
            {
                LogFireDiagnostic($"state-rejected pawn={(pawn != null ? pawn.thingIDNumber.ToString() : "null")}");
                return;
            }

            if (pawn.WorkTagIsDisabled((WorkTags)8))
            {
                LogFireDiagnostic("violence-disabled");
                return;
            }

            // Replayed input must obey the same busy/ability gate as the
            // original HandleFiring, including delayed commands during bursts.
            if (pawn.stances?.curStance is Stance_Busy || pawn.CurJob?.ability != null)
                return;

            LocalTargetInfo target = aimedTarget != null
                ? new LocalTargetInfo(aimedTarget) : new LocalTargetInfo(cell);
            cell = target.Cell;
            LogFireDiagnostic(
                $"target cell={cell} thing={(target.Thing != null ? target.Thing.thingIDNumber.ToString() : "cell")} " +
                $"dist={pawn.Position.DistanceTo(cell):F2}");
            int randState = 0;
            Map randMap;
            int seed = Gen.HashCombineInt(0x50534649, pawn.thingIDNumber);
            seed = Gen.HashCombineInt(seed, state.epoch);
            seed = Gen.HashCombineInt(seed, cell.x);
            seed = Gen.HashCombineInt(seed, cell.z);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);
            DeterministicRandScope.Begin(
                pawn.Map,
                seed,
                0x13579B,
                ref randState,
                out randMap,
                ignoreGate: true);
            try
            {
                Vector3 targetPos = target.Thing != null
                    ? target.Thing.Position.ToVector3Shifted()
                    : cell.ToVector3Shifted();
                Vector3 direction = targetPos - pawn.Position.ToVector3Shifted();
                if (direction.sqrMagnitude > 0.01f)
                {
                    pawn.Rotation = Rot4.FromAngleFlat(
                        Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
                }

                if (pawn.Position.DistanceTo(cell) <= 1.42f && target.Thing != null)
                {
                    bool meleeResult = pawn.meleeVerbs?.TryMeleeAttack(target.Thing, null, false) ?? false;
                    LogFireDiagnostic(
                        $"melee result={meleeResult} stance={pawn.stances?.curStance?.GetType().Name ?? "null"}");
                    return;
                }

                Verb verb = ResolveActiveVerb(pawn);
                LogFireDiagnostic(
                    $"ranged verb={(verb != null ? verb.GetType().Name : "null")} " +
                    $"canHit={verb != null && verb.CanHitTarget(target)}");
                if (verb == null || !verb.Available() || verb.verbProps.IsMeleeAttack ||
                    pawn.WorkTagIsDisabled((WorkTags)524288) ||
                    !verb.CanHitTarget(target))
                    return;
                verb.TryStartCastOn(target, false, true, false, false);
            }
            finally
            {
                DeterministicRandScope.End(randState, randMap);
            }
        }

        private static LocalTargetInfo ResolvePerspectiveFireTarget(Pawn pawn, IntVec3 cell)
        {
            List<Thing> things = cell.GetThingList(pawn.Map)
                .Where(t => t != null).OrderBy(t => t.thingIDNumber).ToList();
            Thing target = things.FirstOrDefault(t => t is Pawn && t != pawn)
                ?? things.FirstOrDefault(t =>
                    t != null &&
                    ((int)t.def.category == (int)ThingCategory.Building ||
                     (int)t.def.category == (int)ThingCategory.Plant));
            return target != null ? new LocalTargetInfo(target) : new LocalTargetInfo(cell);
        }

        private static Verb ResolveActiveVerb(Pawn pawn)
        {
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (verb == null || verb.verbProps.IsMeleeAttack)
            {
                verb = pawn.VerbTracker?.AllVerbs?
                    .FirstOrDefault(candidate => candidate is Verb_MeleeAttack && candidate.Available());
            }
            return verb;
        }

        public static bool SetAvatarPrefix(Pawn pawn)
        {
            InvalidateLocalAvatarCache();
            InvalidateControlledCache();
            if (!MP.IsInMultiplayer || !Active)
                return true;

            if (pawn != null)
            {
                string owner = CurrentCommandOwnerOrLocalPlayer();
                if (!string.IsNullOrEmpty(owner))
                    SyncClaimAvatarWithMode(owner, pawn, LocalRetainOnDowned());
            }
            return false;
        }

        public static bool ClearAvatarPrefix()
        {
            InvalidateLocalAvatarCache();
            InvalidateControlledCache();
            if (clearingLocalView)
                return true;
            if (!MP.IsInMultiplayer || !Active)
                return true;
            if (!MP.InInterface && !MP.IsExecutingSyncCommand)
                return false;

            // Automatic health revocation has a pawn-based executor. A tick
            // or world cleanup must never infer authority from this peer's UI.
            if (!MP.InInterface && !MP.IsExecutingSyncCommand)
            {
                if (Current.ProgramState != ProgramState.Playing)
                    ClearLocalViewOnly();
                return false;
            }

            string owner = CurrentCommandOwnerOrLocalPlayer();
            if (string.IsNullOrEmpty(owner))
                return false;

            PerspectiveShiftControlledAvatar state = CurrentComponent()?.ForOwner(owner);
            if (state != null)
                SyncReleaseAvatar(owner, state.epoch);
            else if (string.Equals(owner, MP.PlayerName, StringComparison.Ordinal))
                ClearLocalViewOnly();
            return false;
        }

        public static bool RevokeControlPrefix(Pawn pawn, DamageInfo? dinfo, Hediff hediff)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;
            PerspectiveShiftMpComponent component = CurrentComponent();
            PerspectiveShiftControlledAvatar state = component?.ForPawn(pawn);
            if (state == null)
                return false;

            bool localOwner = string.Equals(state.owner, MP.PlayerName, StringComparison.Ordinal);
            bool authenticDeath = state.retainOnDowned && (pawn.Dead || pawn.IsKidnapped());
            component.RevokeForHealth(pawn);
            InvalidateControlledCache();
            // Original revocation checks the local Avatar then calls a UI
            // release command during simulation. Never run that branch.
            if (localOwner)
            {
                Game notificationGame = Current.Game;
                bool showLostControl = !state.retainOnDowned;
                localHealthNotifications.Enqueue(() =>
                {
                    if (!ReferenceEquals(Current.Game, notificationGame))
                        return;
                    if (authenticDeath)
                    {
                        AccessTools.Field(stateType, "pendingDeathMenu")?.SetValue(null, true);
                        Type dialogType = AccessTools.TypeByName("PerspectiveShift.Dialog_YouDied");
                        if (dialogType != null)
                            Find.WindowStack.Add((Window)Activator.CreateInstance(dialogType,
                                new object[] { pawn, dinfo, hediff }));
                    }
                    else if (showLostControl)
                        Messages.Message("PS_LostControl".Translate(), MessageTypeDefOf.NegativeEvent, false);
                });
            }
            return false;
        }

        private static string CurrentCommandOwnerOrLocalPlayer()
        {
            if (!MP.IsExecutingSyncCommand)
                return MP.PlayerName;

            if (!string.IsNullOrEmpty(executingCommandOwner))
                return executingCommandOwner;

            if (!commandOwnerResolutionWarningLogged)
            {
                commandOwnerResolutionWarningLogged = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Perspective Shift MP: could not resolve the issuing player " +
                    "for a synchronized avatar command; the action was ignored to prevent assigning " +
                    "control to the host or another peer.");
            }

            return null;
        }

        private static bool ValidateSynchronizedOwner(
            string suppliedOwner,
            string action)
        {
            string actualOwner = CurrentCommandOwnerOrLocalPlayer();
            if (!string.IsNullOrEmpty(actualOwner) &&
                string.Equals(suppliedOwner, actualOwner, StringComparison.Ordinal))
            {
                return true;
            }

            if (!commandOwnerMismatchWarningLogged)
            {
                commandOwnerMismatchWarningLogged = true;
                Log.Warning(
                    "[MP-MeowOnlineShop] Perspective Shift rejected a synchronized " +
                    $"avatar action whose serialized owner did not match its issuer: " +
                    $"action={action}, supplied={suppliedOwner ?? "<null>"}, " +
                    $"issuer={actualOwner ?? "<unresolved>"}. Ownership remains unchanged.");
            }
            return false;
        }

        public static bool IsAvatarPrefix(Pawn pawn, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            __result = CurrentComponent()?.IsControlled(pawn) == true;
            return false;
        }

        private static readonly Queue<Action> localHealthNotifications = new Queue<Action>();

        public static void StateUpdatePrefix()
        {
            EnsureLocalAvatarView("Update");
            if (MP.IsInMultiplayer && (!MP.InInterface || MP.IsExecutingSyncCommand))
                return;
            while (localHealthNotifications.Count > 0)
            {
                Action notification = localHealthNotifications.Dequeue();
                // UI feedback must not allocate shared message IDs/history or
                // consume simulation randomness inside a health callback.
                Rand.PushState();
                try { notification(); }
                finally { Rand.PopState(); }
            }
        }

        public static bool StateTickPrefix()
        {
            // MP calls DoSingleTick for world time; map simulation has its own
            // Tick path even with async time disabled. Never tick a map pawn here.
            return !MP.IsInMultiplayer || !Active;
        }

        public static void AvatarMapPostTick(Map __instance)
        {
            if (!MP.IsInMultiplayer || !Active || !HasControlledAvatars() ||
                iteratingControlledAvatars ||
                !ReferenceEquals(asyncTickingMapField?.GetValue(null), __instance))
                return;
            PerspectiveShiftMpComponent component = CurrentComponent();
            if (component == null || avatarTickMethod == null)
                return;

            object localAvatar = stateAvatarField.GetValue(null);
            iteratingControlledAvatars = true;
            try
            {
                component.TickForMap(__instance);
                foreach (PerspectiveShiftControlledAvatar state in component.OrderedStates)
                {
                    if (state.pawn == null || state.pawn.Dead)
                        continue;
                    if (state.pawn.Map != __instance)
                        continue;

                    EnsureRuntimeAvatar(state);
                    CopyIntentToRuntime(state);
                    stateAvatarField.SetValue(null, state.runtimeAvatar);
                    ProcessPendingFire(state);
                    avatarTickMethod.Invoke(state.runtimeAvatar, null);
                }
            }
            catch (TargetInvocationException e)
            {
                Log.Error("[MP-MeowOnlineShop] Perspective Shift controlled-avatar tick failed: " +
                          (e.InnerException ?? e));
            }
            finally
            {
                RestoreLocalAvatarContext(localAvatar);
                iteratingControlledAvatars = false;
            }
        }

        public static bool UpdatePhysicsPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            Pawn pawn = AvatarPawnOf(__instance);
            PerspectiveShiftControlledAvatar state = CurrentOwnerState();
            if (pawn != null && state == null && MayPreserveHostMigrationAvatar(__instance))
            {
                // Migrate a pre-existing single-player Perspective Shift avatar
                // when that save is first hosted. Only the host may seed it.
                SyncClaimAvatarWithMode(MP.PlayerName, pawn, LocalRetainOnDowned());
                return false;
            }
            if (pawn == null || state == null || state.pawn != pawn)
                return false;

            int moveX = 0;
            int moveZ = 0;
            if (!Application.isFocused)
                ClearHeldMovementKeys();

            if (KeyDown("PS_MoveLeft", heldMoveLeft)) moveX--;
            if (KeyDown("PS_MoveRight", heldMoveRight)) moveX++;
            if (KeyDown("PS_MoveBack", heldMoveBack)) moveZ--;
            if (KeyDown("PS_MoveForward", heldMoveForward)) moveZ++;

            bool hasMoveInput = moveX != 0 || moveZ != 0;
            bool hardBlocked = WorldRendererUtility.WorldSelected ||
                               Find.WindowStack.AnySearchWidgetFocused ||
                               (pawn.CurJob != null && !pawn.CurJob.def.playerInterruptible);
            if (hardBlocked)
            {
                moveX = 0;
                moveZ = 0;
                hasMoveInput = false;
            }

            bool sprint = hasMoveInput && KeyDown("PS_Sprint", heldSprint);
            bool walk = hasMoveInput && !sprint && KeyDown("PS_Walk", heldWalk);

            if ((hasMoveInput || pawn.pather?.Moving == true) &&
                cameraLockPositionField?.GetValue(null) != null)
                cameraLockPositionField.SetValue(null, null);

            if (hasMoveInput && !localMovementInputLogged)
            {
                localMovementInputLogged = true;
                bool targetControlsFrozen = false;
                try
                {
                    targetControlsFrozen = controlsFrozenProperty != null &&
                                           (bool)controlsFrozenProperty.GetValue(null, null);
                }
                catch
                {
                }

                if (ModDebug.EnablePerspectiveShiftTrace)
                {
                    Log.Message(
                        $"[MP-MeowOnlineShop] Perspective Shift local movement input accepted: " +
                        $"owner={MP.PlayerName}, pawn={pawn.thingIDNumber}, input=({moveX},{moveZ}), " +
                        $"targetControlsFrozen={targetControlsFrozen}.");
                }
            }

            float realtime = Time.realtimeSinceStartup;
            int inputGameTick = Find.TickManager.TicksGame;
            if (!sentInput || realtime - lastSentInputRealtime >= InputHeartbeatSeconds ||
                inputGameTick - lastSentInputGameTick >= 30 ||
                moveX != lastMoveX || moveZ != lastMoveZ ||
                sprint != lastSprint || walk != lastWalk)
            {
                lastMoveX = moveX;
                lastMoveZ = moveZ;
                lastSprint = sprint;
                lastWalk = walk;
                sentInput = true;
                lastSentInputRealtime = realtime;
                lastSentInputGameTick = inputGameTick;
                int inputSequence = localPrediction.Queue(
                    state,
                    moveX,
                    moveZ,
                    sprint,
                    walk,
                    realtime);
                SyncSetMoveIntent(
                    MP.PlayerName,
                    pawn,
                    state.epoch,
                    inputSequence,
                    moveX,
                    moveZ,
                    sprint,
                    walk);
            }

            UpdateVisualPrediction(__instance, state, pawn, moveX, moveZ, sprint, walk);
            return false;
        }

        private static void UpdateCameraPrefix(
            object __instance,
            out CameraPredictionSwap __state)
        {
            __state = default(CameraPredictionSwap);
            if (!MP.IsInMultiplayer || !Active ||
                !HasControlledAvatars() || physicsPositionField == null)
                return;

            Pawn pawn = AvatarPawnOf(__instance);
            PerspectiveShiftControlledAvatar state =
                CurrentComponent()?.ForOwner(MP.PlayerName);
            if (!IsExclusiveLocalOwnership(state) || state.pawn != pawn ||
                !localPrediction.Matches(state))
            {
                return;
            }

            Vector3 bodyPosition =
                (Vector3?)physicsPositionField.GetValue(__instance) ??
                AuthoritativePathPosition(pawn);
            float deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            if (!localPrediction.hasCameraPosition ||
                (localPrediction.cameraPosition - bodyPosition).sqrMagnitude > 16f)
            {
                localPrediction.cameraPosition = bodyPosition;
                localPrediction.cameraVelocity = Vector3.zero;
                localPrediction.hasCameraPosition = true;
            }
            else
            {
                float latencyFactor = Mathf.InverseLerp(
                    0.1f,
                    0.75f,
                    localPrediction.PredictionHorizon);
                localPrediction.cameraPosition = Vector3.SmoothDamp(
                    localPrediction.cameraPosition,
                    bodyPosition,
                    ref localPrediction.cameraVelocity,
                    Mathf.Lerp(0.1f, 0.22f, latencyFactor),
                    10f,
                    deltaTime);
            }

            __state.active = true;
            __state.previousPhysicsPosition =
                (Vector3?)physicsPositionField.GetValue(__instance);
            physicsPositionField.SetValue(
                __instance,
                (Vector3?)localPrediction.cameraPosition);
        }

        private static void UpdateCameraPostfix(
            object __instance,
            CameraPredictionSwap __state)
        {
            if (__state.active && physicsPositionField != null)
            {
                physicsPositionField.SetValue(
                    __instance,
                    __state.previousPhysicsPosition);
            }
        }

        public static bool HandleSelectorClickPrefix(object __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active ||
                !HasControlledAvatars() || MP.IsExecutingSyncCommand)
                return true;

            Pawn pawn = AvatarPawnOf(__instance);
            PerspectiveShiftControlledAvatar state = CurrentOwnerState();
            Event currentEvent = Event.current;
            if (pawn == null || state == null || state.pawn != pawn || currentEvent == null ||
                currentEvent.type != EventType.MouseDown || (currentEvent.button != 0 && currentEvent.button != 1))
            {
                __result = false;
                return false;
            }

            if (Find.Targeter.IsTargeting || Find.TickManager.Paused || pawn.Downed || pawn.InMentalState)
            {
                __result = false;
                return false;
            }

            IntVec3 clickCell = UI.MouseCell();
            if (currentEvent.button == 0 && pawn.Drafted && pawn.Map != null && clickCell.InBounds(pawn.Map))
            {
                SyncAimAndFireAvatar(MP.PlayerName, pawn, state.epoch,
                    ResolvePerspectiveFireTarget(pawn, clickCell));
            }
            else
                SyncAvatarMapClick(
                MP.PlayerName,
                pawn,
                state.epoch,
                clickCell,
                currentEvent.button,
                currentEvent.clickCount);
            __result = true;
            return false;
        }

        public static bool HandleFiringPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !Active ||
                !HasControlledAvatars() || MP.IsExecutingSyncCommand)
                return true;

            Pawn pawn = AvatarPawnOf(__instance);
            PerspectiveShiftControlledAvatar state = CurrentOwnerState();
            if (pawn == null || state == null || state.pawn != pawn || !pawn.Drafted ||
                pawn.Downed || pawn.InMentalState || Find.Targeter.IsTargeting || Find.TickManager.Paused)
                return false;

            IntVec3 targetCell = UI.MouseCell();
            if (pawn.Map == null || !targetCell.InBounds(pawn.Map))
                return false;

            // Keep mouse aiming responsive without changing simulation state locally.
            // Rotation and the actual verb cast are performed only by the ordered command.
            if (aimAngleField != null)
            {
                Vector3 origin = (Vector3?)physicsPositionField?.GetValue(__instance) ?? pawn.DrawPos;
                Vector3 toTarget = targetCell.ToVector3Shifted() - origin;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    float angle = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                    if (angle < 0f)
                        angle += 360f;
                    aimAngleField.SetValue(__instance, angle);
                }
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            bool targetChanged = state.lastFireCell != targetCell;
            if (!targetChanged && now - state.lastFireTick < 5)
                return false;

            // Custom weapon Available/CanHitTarget implementations may mutate
            // caches or consume Rand. Evaluate them only in the replayed command.

            state.lastFireCell = targetCell;
            state.lastFireTick = now;
            SyncAimAndFireAvatar(MP.PlayerName, pawn, state.epoch,
                ResolvePerspectiveFireTarget(pawn, targetCell));
            return false;
        }

        public static void WarmupTimePostfix(Verb __instance, ref float __result)
        {
            if (!MP.IsInMultiplayer || !Active ||
                !HasControlledAvatars() || __result <= 0f || __instance == null ||
                __instance.EquipmentCompSource == null || !__instance.CasterIsPawn ||
                __instance is IAbilityVerb)
                return;

            // Perspective Shift's original patch reads a per-client setting. In MP,
            // apply the direct-control behavior from shared state on every peer.
            if (CurrentComponent()?.IsControlled(__instance.CasterPawn) == true)
                __result = 0f;
        }

        public static bool MouseCellPrefix(ref IntVec3 __result)
        {
            if (!MP.IsInMultiplayer || !Active || !forcedMouseCell.HasValue)
                return true;

            __result = forcedMouseCell.Value;
            return false;
        }

        public static bool TryHandleStorageBuildingPrefix(
            object __instance,
            Thing thing,
            ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active || !(thing is Building_Storage storage))
                return true;

            Pawn pawn = AvatarPawnOf(__instance);
            if (pawn == null || pawn.Map == null || storage.Map != pawn.Map)
            {
                __result = false;
                return false;
            }

            // Preserve the target mod's ordered-job paths for designated
            // deconstruction/uninstallation and its simple one-slot behavior.
            if (pawn.Map.designationManager.DesignationOn(
                    storage,
                    DesignationDefOf.Deconstruct) != null ||
                pawn.Map.designationManager.DesignationOn(
                    storage,
                    DesignationDefOf.Uninstall) != null ||
                InstallBlueprintUtility.ExistingBlueprintFor(storage) != null ||
                storage.AllSlotCellsList().Count * storage.def.building.maxItemsInCell <= 1)
            {
                return true;
            }

            Thing carried = pawn.carryTracker?.CarriedThing;
            if (carried != null)
            {
                if (storage.Accepts(carried))
                {
                    IntVec3 destination = storage.AllSlotCellsList()
                        .Where(cell => cell.InBounds(pawn.Map))
                        .OrderBy(cell => cell.x)
                        .ThenBy(cell => cell.z)
                        .Select(cell => (IntVec3?)cell)
                        .FirstOrDefault(
                            cell => StoreUtility.IsGoodStoreCell(
                                cell.Value,
                                pawn.Map,
                                carried,
                                pawn,
                                pawn.Faction)) ?? IntVec3.Invalid;
                    if (destination.IsValid)
                    {
                        pawn.carryTracker.TryDropCarriedThing(
                            destination,
                            ThingPlaceMode.Direct,
                            out _);
                    }
                }

                __result = true;
                return false;
            }

            Thing storedItem = storage.AllSlotCellsList()
                .Where(cell => cell.InBounds(pawn.Map))
                .SelectMany(cell => cell.GetThingList(pawn.Map))
                .Where(candidate =>
                    candidate != null &&
                    candidate.def.category == ThingCategory.Item)
                .OrderBy(candidate => candidate.thingIDNumber)
                .FirstOrDefault();
            if (storedItem != null)
            {
                var reservers = new HashSet<Pawn>();
                pawn.Map.reservationManager.ReserversOf(storedItem, reservers);
                foreach (Pawn reserver in reservers
                             .Where(candidate => candidate != null && candidate != pawn)
                             .OrderBy(candidate => candidate.thingIDNumber))
                {
                    reserver.jobs?.EndCurrentJob(
                        JobCondition.InterruptForced,
                        startNewJob: false);
                }

                pawn.Map.reservationManager.ReleaseAllForTarget(storedItem);
                pawn.Map.physicalInteractionReservationManager.ReleaseAllForTarget(storedItem);
                pawn.carryTracker.TryStartCarry(
                    storedItem,
                    storedItem.stackCount,
                    reserve: false);
            }
            else if (LocalAvatarPawn() == pawn)
            {
                Messages.Message(
                    "Perspective Shift multiplayer: the drag-and-drop storage window is disabled; " +
                    "carry an item and click the storage to deposit it.",
                    MessageTypeDefOf.RejectInput,
                    false);
            }

            __result = true;
            return false;
        }

        public static bool BypassPerspectiveShiftCellFinderPrefix(ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active || !HasControlledAvatars())
                return true;

            __result = true;
            return false;
        }

        public static bool ReplacePerspectiveShiftTweenerPrefix(object __0, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active || !HasControlledAvatars())
                return true;

            object tweener = __0;
            Pawn pawn = tweener == null
                ? null
                : _tweenerPawnRef != null
                    ? _tweenerPawnRef(tweener)
                    : AccessTools.Field(tweener.GetType(), "pawn")?.GetValue(tweener) as Pawn;
            object localAvatar = LocalAvatarObject();
            Pawn localPawn = AvatarPawnOf(localAvatar);
            bool hasPrediction = localAvatar != null &&
                                 (_physicsPositionGetter != null
                                     ? _physicsPositionGetter(localAvatar)
                                     : physicsPositionField?.GetValue(localAvatar)) != null;
            __result = pawn == null || pawn != localPawn || !hasPrediction;
            return false;
        }

        public static bool SkipInMultiplayerPrefix()
        {
            return !MP.IsInMultiplayer || !Active || !HasControlledAvatars();
        }

        public static bool SuppressTargetBoolPatchPrefix(ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active || !HasControlledAvatars())
                return true;

            // The patched method is itself a Harmony prefix. Returning true from it
            // tells Harmony to continue with the real vanilla method.
            __result = true;
            return false;
        }

        public static bool ControlledWaitAutoAttackPrefix(JobDriver_Wait __instance)
        {
            if (!MP.IsInMultiplayer || !Active || __instance == null)
                return true;

            Pawn pawn = _jobDriverPawnRef != null
                ? _jobDriverPawnRef(__instance)
                : AccessTools.Field(typeof(JobDriver), "pawn")?.GetValue(__instance) as Pawn;
            if (pawn == null)
                return true;

            // Perspective Shift's original prefix reads its local Avatar singleton.
            // Use the synchronized ownership registry instead, so every peer makes
            // the same decision before vanilla CheckForAutoAttack consumes Rand.
            return CurrentComponent()?.IsControlled(pawn) != true;
        }

        public static bool LocalAvatarOnlyTargetBoolPrefix(object[] __args, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            // DrawPos is also the real projectile origin. Only rendering may
            // substitute the owning client's smoothed lean for vanilla state.
            if (!MP.InInterface || MP.IsExecutingSyncCommand || !HasControlledAvatars())
            {
                __result = true;
                return false;
            }

            Pawn targetPawn = null;
            if (__args != null)
            {
                foreach (object arg in __args)
                {
                    targetPawn = ResolvePawnArg(arg);
                    if (targetPawn != null)
                        break;
                }
            }

            object localAvatar = LocalAvatarObject();
            Pawn localPawn = AvatarPawnOf(localAvatar);
            if (targetPawn != null && targetPawn == localPawn)
                return true;

            // The target method is itself a Harmony prefix; true means the
            // underlying vanilla render method should continue.
            __result = true;
            return false;
        }

        private static void RenderPawnPrefix(object __instance, out RenderLeanContext __state)
        {
            __state = default;
            if (!MP.IsInMultiplayer || !Active)
                return;
            PawnLeaner leaner = AvatarPawnOf(__instance)?.Drawer?.leaner;
            if (leaner == null)
                return;
            __state.leaner = leaner;
            __state.shootSourceOffset = _shootSourceOffsetRef(leaner);
        }

        private static Exception RenderPawnFinalizer(Exception __exception, RenderLeanContext __state)
        {
            // RenderPawn writes this simulation field from local LeanTarget.
            // Retain its visual smoothing, but restore the offset established
            // by Notify_WarmingCastAlongLine even when rendering throws.
            if (__state.leaner != null)
                _shootSourceOffsetRef(__state.leaner) = __state.shootSourceOffset;
            return __exception;
        }

        public static bool EndJobPostfixContextPrefix(object[] __args, out object __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !Active || !HasControlledAvatars())
                return true;

            Pawn_JobTracker tracker = __args?.OfType<Pawn_JobTracker>().FirstOrDefault();
            Pawn trackerPawn = tracker == null
                ? null
                : _jobTrackerPawnRef != null
                    ? _jobTrackerPawnRef(tracker)
                    : AccessTools.Field(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn;
            PerspectiveShiftControlledAvatar controlledState = CurrentComponent()?.ForPawn(trackerPawn);
            if (controlledState == null)
                return false;

            EnsureRuntimeAvatar(controlledState);
            CopyIntentToRuntime(controlledState);
            __state = new AvatarContext { previous = stateAvatarField.GetValue(null) };
            stateAvatarField.SetValue(null, controlledState.runtimeAvatar);
            return true;
        }

        public static Exception EndJobPostfixContextFinalizer(Exception __exception, object __state)
        {
            // Restore the caller's simulation context, not this client's UI
            // avatar. The finalizer also runs when InteractWith throws.
            if (__state is AvatarContext context)
            {
                stateAvatarField.SetValue(null, context.previous);
                InvalidateLocalAvatarCache();
            }
            return __exception;
        }

        public static void EnsureRuntimeAvatar(PerspectiveShiftControlledAvatar state)
        {
            if (state == null || state.pawn == null || avatarPawnConstructor == null)
                return;

            if (state.runtimeAvatar == null || avatarPawnField.GetValue(state.runtimeAvatar) != state.pawn)
            {
                state.runtimeAvatar = avatarPawnConstructor.Invoke(new object[] { state.pawn });
                ApplyRuntimeState(state);
            }
        }

        public static void CaptureRuntimeState(PerspectiveShiftControlledAvatar state)
        {
            if (state?.runtimeAvatar == null)
                return;

            state.savedLord = savedLordField?.GetValue(state.runtimeAvatar) as Verse.AI.Group.Lord ?? state.savedLord;
            state.pendingMinifiedPickup = pendingMinifiedPickupField?.GetValue(state.runtimeAvatar) as Building;
            state.interactingDoor = interactingDoorField?.GetValue(state.runtimeAvatar) as Building_Door;
            state.wasFullyRested = ReadBool(wasFullyRestedField, state.runtimeAvatar, state.wasFullyRested);
            state.passedOut = ReadBool(passedOutField, state.runtimeAvatar, state.passedOut);
            var alerts = needsAlertedField?.GetValue(state.runtimeAvatar) as Dictionary<string, bool>;
            state.needsAlerted = alerts == null
                ? new Dictionary<string, bool>()
                : new Dictionary<string, bool>(alerts, StringComparer.Ordinal);
        }

        private static void ApplyRuntimeState(PerspectiveShiftControlledAvatar state)
        {
            if (state?.runtimeAvatar == null)
                return;

            savedLordField?.SetValue(state.runtimeAvatar, state.savedLord);
            pendingMinifiedPickupField?.SetValue(state.runtimeAvatar, state.pendingMinifiedPickup);
            interactingDoorField?.SetValue(state.runtimeAvatar, state.interactingDoor);
            wasFullyRestedField?.SetValue(state.runtimeAvatar, state.wasFullyRested);
            passedOutField?.SetValue(state.runtimeAvatar, state.passedOut);
            needsAlertedField?.SetValue(
                state.runtimeAvatar,
                state.needsAlerted == null
                    ? new Dictionary<string, bool>()
                    : new Dictionary<string, bool>(state.needsAlerted, StringComparer.Ordinal));
        }

        private static bool ReadBool(FieldInfo field, object instance, bool fallback)
        {
            object value = field?.GetValue(instance);
            return value is bool result ? result : fallback;
        }

        public static void CopyIntentToRuntime(PerspectiveShiftControlledAvatar state)
        {
            if (state?.runtimeAvatar == null)
                return;

            moveInputField?.SetValue(state.runtimeAvatar, new Vector3(state.moveX, 0f, state.moveZ).normalized);
            sprintField?.SetValue(state.runtimeAvatar, state.sprint);
            walkField?.SetValue(state.runtimeAvatar, state.walk);
        }

        public static void SetLocalAvatarIfOwned(PerspectiveShiftControlledAvatar state)
        {
            if (IsExclusiveLocalOwnership(state))
            {
                EnsureRuntimeAvatar(state);
                localPrediction.Bind(state, resetVisuals: true);
                cameraLockPositionField?.SetValue(null, null);
                isActiveCacheFrameField?.SetValue(null, -999);
                stateAvatarField?.SetValue(null, state.runtimeAvatar);
                sentInput = false;
                localMovementInputLogged = false;
                ClearHeldMovementKeys();

                if (!localAvatarBindingLogged)
                {
                    localAvatarBindingLogged = true;
                    Log.Message(
                        $"[MP-MeowOnlineShop] Perspective Shift local avatar bound: " +
                        $"owner={state.owner}, pawn={state.pawn?.thingIDNumber ?? -1}, " +
                        $"map={state.pawn?.Map?.uniqueID ?? -1}.");
                }
            }
        }

        public static void ClearLocalAvatarIfOwned(string owner)
        {
            if (string.Equals(owner, MP.PlayerName, StringComparison.Ordinal))
                ClearLocalViewOnly();
        }

        public static void RestoreLocalAvatarFromRegistry()
        {
            PerspectiveShiftControlledAvatar state = CurrentComponent()?.ForOwner(MP.PlayerName);
            if (IsExclusiveLocalOwnership(state))
                SetLocalAvatarIfOwned(state);
            else
            {
                object unownedAvatar = stateAvatarField?.GetValue(null);
                if (unownedAvatar != null &&
                    !MayPreserveHostMigrationAvatar(unownedAvatar))
                {
                    ClearLocalViewOnly();
                }
            }
        }

        private static void EnsureLocalAvatarView(string source)
        {
            if (!MP.IsInMultiplayer || !Active ||
                !HasControlledAvatars() ||
                stateAvatarField == null || avatarPawnField == null)
                return;

            PerspectiveShiftControlledAvatar state = CurrentComponent()?.ForOwner(MP.PlayerName);
            if (!IsExclusiveLocalOwnership(state))
            {
                object unownedAvatar = stateAvatarField.GetValue(null);
                if (unownedAvatar != null &&
                    !MayPreserveHostMigrationAvatar(unownedAvatar))
                {
                    ClearLocalViewOnly();
                }
                return;
            }

            EnsureRuntimeAvatar(state);
            object current = stateAvatarField.GetValue(null);
            if (current == state.runtimeAvatar && avatarPawnField.GetValue(current) == state.pawn)
                return;

            stateAvatarField.SetValue(null, state.runtimeAvatar);
            cameraLockPositionField?.SetValue(null, null);
            isActiveCacheFrameField?.SetValue(null, -999);
            sentInput = false;

            if (!localAvatarRecoveryLogged)
            {
                localAvatarRecoveryLogged = true;
                Log.Warning(
                    $"[MP-MeowOnlineShop] Perspective Shift restored a missing local avatar view " +
                    $"from the shared owner registry: source={source}, owner={state.owner}, " +
                    $"pawn={state.pawn.thingIDNumber}, map={state.pawn.Map?.uniqueID ?? -1}.");
            }
        }

        private static void RestoreLocalAvatarContext(object previousAvatar)
        {
            InvalidateLocalAvatarCache();
            InvalidateControlledCache();
            PerspectiveShiftControlledAvatar localState = CurrentComponent()?.ForOwner(MP.PlayerName);
            if (IsExclusiveLocalOwnership(localState))
            {
                EnsureRuntimeAvatar(localState);
                stateAvatarField?.SetValue(null, localState.runtimeAvatar);
                return;
            }

            // Joining clients must never inherit the host's serialized/static
            // Perspective Shift singleton. Preserve an unregistered Avatar only
            // on the hosting peer, briefly, so UpdatePhysics can migrate a save
            // that was switched from single-player to Multiplayer.
            if (MayPreserveHostMigrationAvatar(previousAvatar))
                stateAvatarField.SetValue(null, previousAvatar);
            else
                stateAvatarField?.SetValue(null, null);
        }

        private static bool MayPreserveHostMigrationAvatar(object avatar)
        {
            if (!MP.IsHosting || MP.IsExecutingSyncCommand || avatar == null ||
                avatarPawnField == null)
            {
                return false;
            }

            Pawn pawn = avatarPawnField.GetValue(avatar) as Pawn;
            PerspectiveShiftMpComponent component = CurrentComponent();
            return pawn != null && component != null &&
                   component.CanMigrateSinglePlayerAvatar && !pawn.Dead && !pawn.Downed &&
                   component.ForPawn(pawn) == null;
        }

        private static bool IsExclusiveLocalOwnership(
            PerspectiveShiftControlledAvatar state)
        {
            if (state == null || state.pawn == null ||
                !string.Equals(state.owner, MP.PlayerName, StringComparison.Ordinal))
            {
                return false;
            }

            PerspectiveShiftMpComponent component = CurrentComponent();
            return component != null &&
                   ReferenceEquals(component.ForOwner(MP.PlayerName), state) &&
                   ReferenceEquals(component.ForPawn(state.pawn), state);
        }

        private static void ClearLocalViewOnly()
        {
            InvalidateLocalAvatarCache();
            InvalidateControlledCache();
            if (_stateAvatarSetter != null)
                _stateAvatarSetter(null);
            else
                stateAvatarField?.SetValue(null, null);
            sentInput = false;
            localPrediction.Clear();
            ClearHeldMovementKeys();
            Cursor.visible = true;

            // With Avatar already null, the target cleanup only restores its local camera/UI.
            try
            {
                clearingLocalView = true;
                originalClearAvatarMethod?.Invoke(null, null);
            }
            catch
            {
            }
            finally
            {
                clearingLocalView = false;
            }
        }

        private static PerspectiveShiftMpComponent CurrentComponent()
        {
            Game game = Current.Game;
            List<GameComponent> components = game?.components;
            // Loading replaces Game.components without replacing Current.Game.
            // Validate the cached slot, including in-place list replacement/removal,
            // so simulation cannot keep a constructor-time, empty owner registry.
            if (!_componentCacheValid || !ReferenceEquals(game, _componentCacheGame) ||
                _componentCache == null || components == null ||
                _componentCacheIndex < 0 || _componentCacheIndex >= components.Count ||
                !ReferenceEquals(components[_componentCacheIndex], _componentCache))
            {
                _componentCache = null;
                _componentCacheIndex = -1;
                if (components != null)
                {
                    for (int i = 0; i < components.Count; i++)
                    {
                        if (components[i] is PerspectiveShiftMpComponent component)
                        {
                            _componentCache = component;
                            _componentCacheIndex = i;
                            break;
                        }
                    }
                }
                _componentCacheGame = game;
                _componentCacheValid = true;
                InvalidateControlledCache();
            }

            return _componentCache;
        }

        private static PerspectiveShiftControlledAvatar CurrentOwnerState()
        {
            int frame = Time.frameCount;
            string owner = MP.PlayerName;
            if (_ownerStateCacheFrame == frame &&
                string.Equals(_ownerStateCacheOwner, owner, StringComparison.Ordinal))
            {
                return _ownerStateCache;
            }

            _ownerStateCacheFrame = frame;
            _ownerStateCacheOwner = owner;
            _ownerStateCache = CurrentComponent()?.ForOwner(owner);
            return _ownerStateCache;
        }

        private static AccessTools.FieldRef<object, T> TryGetInstanceFieldRef<T>(
            Type type,
            string name)
        {
            if (type == null)
                return null;
            try
            {
                return AccessTools.FieldRefAccess<T>(type, name);
            }
            catch
            {
                return null;
            }
        }

        private static Func<object> TryCompileStaticFieldGetter(FieldInfo field)
        {
            if (field == null)
                return null;
            try
            {
                var body = Expression.Convert(
                    Expression.Field(null, field),
                    typeof(object));
                return Expression.Lambda<Func<object>>(body).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Action<object> TryCompileStaticFieldSetter(FieldInfo field)
        {
            if (field == null)
                return null;
            try
            {
                var value = Expression.Parameter(typeof(object), "value");
                var body = Expression.Assign(
                    Expression.Field(null, field),
                    Expression.Convert(value, field.FieldType));
                return Expression.Lambda<Action<object>>(body, value).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Func<object, object> TryCompileInstanceFieldGetter(
            Type type,
            FieldInfo field)
        {
            if (type == null || field == null)
                return null;
            try
            {
                var instance = Expression.Parameter(typeof(object), "instance");
                var body = Expression.Convert(
                    Expression.Field(Expression.Convert(instance, type), field),
                    typeof(object));
                return Expression.Lambda<Func<object, object>>(body, instance).Compile();
            }
            catch
            {
                return null;
            }
        }

        private static Pawn ResolvePawnArg(object arg)
        {
            if (arg is Pawn pawn)
                return pawn;
            if (arg == null)
                return null;

            var accessor = GetNestedPawnRef(arg.GetType());
            if (accessor == null)
                return null;
            try
            {
                return accessor(arg);
            }
            catch
            {
                return null;
            }
        }

        private static AccessTools.FieldRef<object, Pawn> GetNestedPawnRef(Type type)
        {
            lock (NestedPawnRefCacheLock)
            {
                AccessTools.FieldRef<object, Pawn> cached;
                if (NestedPawnRefCache.TryGetValue(type, out cached))
                    return cached;

                AccessTools.FieldRef<object, Pawn> accessor = null;
                try
                {
                    accessor = AccessTools.FieldRefAccess<Pawn>(type, "pawn");
                }
                catch
                {
                    accessor = null;
                }

                NestedPawnRefCache[type] = accessor;
                if (NestedPawnRefCache.Count > 256)
                    NestedPawnRefCache.Clear();
                return accessor;
            }
        }

        private static Pawn LocalAvatarPawn()
        {
            LocalAvatarObject();
            return _localAvatarCacheFrame == Time.frameCount
                ? _localAvatarCachePawn
                : null;
        }

        private static object LocalAvatarObject()
        {
            int frame = Time.frameCount;
            if (_localAvatarCacheFrame == frame &&
                _localAvatarCacheObject != null)
            {
                return _localAvatarCacheObject;
            }

            object avatar = _stateAvatarGetter != null
                ? _stateAvatarGetter()
                : stateAvatarField?.GetValue(null);
            _localAvatarCacheFrame = frame;
            _localAvatarCacheObject = avatar;
            _localAvatarCachePawn = AvatarPawnOf(avatar);
            return avatar;
        }

        private static Pawn AvatarPawnOf(object avatar)
        {
            if (avatar == null)
                return null;
            return _avatarPawnRef != null
                ? _avatarPawnRef(avatar)
                : avatarPawnField?.GetValue(avatar) as Pawn;
        }

        private static void InvalidateLocalAvatarCache()
        {
            _localAvatarCacheFrame = -1;
            _localAvatarCacheObject = null;
            _localAvatarCachePawn = null;
        }

        private static bool HasControlledAvatars()
        {
            // This gates AI/combat, not just rendering. Death/release can occur
            // between ticks in one frame, and peers render at different rates.
            return CurrentComponent()?.HasControlledAvatars == true;
        }

        private static void InvalidateControlledCache()
        {
            _ownerStateCacheFrame = -1;
            _ownerStateCache = null;
        }

        private static bool KeyDown(string defName, bool eventHeld)
        {
            KeyBindingDef key = DefDatabase<KeyBindingDef>.GetNamedSilentFail(defName);
            return eventHeld || (key != null && key.IsDown);
        }

        private static bool BindingMatches(string defName, KeyCode code)
        {
            if (code == KeyCode.None)
                return false;

            KeyBindingDef key = DefDatabase<KeyBindingDef>.GetNamedSilentFail(defName);
            if (key == null)
                return false;

            return code == key.MainKey ||
                   code == key.defaultKeyCodeA ||
                   code == key.defaultKeyCodeB;
        }

        private static void ClearHeldMovementKeys()
        {
            heldMoveForward = false;
            heldMoveBack = false;
            heldMoveLeft = false;
            heldMoveRight = false;
            heldSprint = false;
            heldWalk = false;
        }

        private static void UpdateVisualPrediction(
            object avatar,
            PerspectiveShiftControlledAvatar state,
            Pawn pawn,
            int moveX,
            int moveZ,
            bool sprint,
            bool walk)
        {
            if (physicsPositionField == null || pawn.Map == null || !pawn.Spawned)
                return;

            localPrediction.Bind(state, resetVisuals: false);
            float deltaTime = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            Vector3 authoritative = AuthoritativePathPosition(pawn);
            Vector3 inputDirection = new Vector3(moveX, 0f, moveZ);
            bool ownsMovementJob = state != null && pawn.CurJob != null &&
                                   state.movementJobId >= 0 &&
                                   pawn.CurJob.loadID == state.movementJobId;
            Vector3 pathDirection = CurrentPathDirection(pawn);
            bool authorityMoving = ownsMovementJob && pawn.pather?.Moving == true &&
                                   pathDirection.sqrMagnitude > 0.01f;
            if (inputDirection.sqrMagnitude > 0.01f)
                inputDirection.Normalize();

            Vector3 movementDirection = authorityMoving
                ? pathDirection
                : inputDirection;
            float cellsPerSecond = PredictionSpeed(
                pawn,
                authorityMoving,
                sprint,
                walk);
            float now = Time.realtimeSinceStartup;
            Vector3 replayOffset = ReplayPendingInputs(
                pawn,
                now,
                localPrediction.PredictionHorizon,
                authorityMoving);

            Vector3 desired = authoritative + replayOffset;

            Vector3 desiredOffset = desired - authoritative;
            desiredOffset.y = 0f;
            float maximumLead = Mathf.Clamp(
                cellsPerSecond * localPrediction.PredictionHorizon,
                0.5f,
                2.25f);
            if (desiredOffset.magnitude > maximumLead)
                desired = authoritative + desiredOffset.normalized * maximumLead;
            desired = ClampPredictionToWalkable(pawn, authoritative, desired);

            if (!localPrediction.hasPredictedPosition)
            {
                localPrediction.predictedPosition = authoritative;
                localPrediction.predictionVelocity = Vector3.zero;
                localPrediction.hasPredictedPosition = true;
            }

            Vector3 correction = desired - localPrediction.predictedPosition;
            correction.y = 0f;
            if (correction.sqrMagnitude > 9f)
            {
                localPrediction.predictedPosition = desired;
                localPrediction.predictionVelocity = Vector3.zero;
            }
            else
            {
                bool correctingBackwards =
                    (movementDirection.sqrMagnitude > 0.01f &&
                     Vector3.Dot(correction, movementDirection) < -0.05f) ||
                    (inputDirection.sqrMagnitude <= 0.01f &&
                     correction.sqrMagnitude > 0.0025f);
                float latencyFactor = Mathf.InverseLerp(
                    0.1f,
                    0.75f,
                    localPrediction.PredictionHorizon);
                float smoothTime = correctingBackwards
                    ? Mathf.Lerp(0.22f, 0.4f, latencyFactor)
                    : 0.075f;
                float maximumCorrectionSpeed = correctingBackwards
                    ? Mathf.Lerp(4f, 2.25f, latencyFactor)
                    : 12f;
                localPrediction.predictedPosition = Vector3.SmoothDamp(
                    localPrediction.predictedPosition,
                    desired,
                    ref localPrediction.predictionVelocity,
                    smoothTime,
                    maximumCorrectionSpeed,
                    deltaTime);
            }

            if (inputDirection.sqrMagnitude <= 0.01f &&
                localPrediction.pending.Count == 0 &&
                (localPrediction.predictedPosition - authoritative).sqrMagnitude < 0.0016f)
            {
                localPrediction.predictedPosition = authoritative;
                localPrediction.predictionVelocity = Vector3.zero;
            }

            localPrediction.predictedPosition.y = authoritative.y;
            physicsPositionField.SetValue(
                avatar,
                (Vector3?)localPrediction.predictedPosition);
        }

        private static Vector3 ReplayPendingInputs(
            Pawn pawn,
            float now,
            float horizon,
            bool authorityMoving)
        {
            float windowStart = now - horizon;
            Vector3 offset = Vector3.zero;
            float cursor = windowStart;
            List<PendingMoveInput> history = localPrediction.inputHistory;

            // Retain one transition at or before the replay window as its
            // baseline. Acknowledging a command must not erase its recent
            // visual history, otherwise every direction-change acknowledgement
            // would rotate the prediction target abruptly.
            while (history.Count > 1 &&
                   history[1].sentRealtime <= windowStart)
            {
                PendingMoveInput removed = history[0];
                localPrediction.historyBaseMoveX = removed.moveX;
                localPrediction.historyBaseMoveZ = removed.moveZ;
                localPrediction.historyBaseSprint = removed.sprint;
                localPrediction.historyBaseWalk = removed.walk;
                history.RemoveAt(0);
            }

            int activeMoveX = localPrediction.historyBaseMoveX;
            int activeMoveZ = localPrediction.historyBaseMoveZ;
            bool activeSprint = localPrediction.historyBaseSprint;
            bool activeWalk = localPrediction.historyBaseWalk;
            for (int i = 0; i < history.Count; i++)
            {
                PendingMoveInput input = history[i];
                if (input.sentRealtime <= windowStart)
                {
                    activeMoveX = input.moveX;
                    activeMoveZ = input.moveZ;
                    activeSprint = input.sprint;
                    activeWalk = input.walk;
                    continue;
                }
                if (input.sentRealtime > now)
                    break;

                AccumulatePredictionSegment(
                    pawn,
                    ref offset,
                    activeMoveX,
                    activeMoveZ,
                    activeSprint,
                    activeWalk,
                    input.sentRealtime - cursor,
                    authorityMoving);
                cursor = input.sentRealtime;
                activeMoveX = input.moveX;
                activeMoveZ = input.moveZ;
                activeSprint = input.sprint;
                activeWalk = input.walk;
            }

            AccumulatePredictionSegment(
                pawn,
                ref offset,
                activeMoveX,
                activeMoveZ,
                activeSprint,
                activeWalk,
                now - cursor,
                authorityMoving);
            return offset;
        }

        private static void AccumulatePredictionSegment(
            Pawn pawn,
            ref Vector3 offset,
            int moveX,
            int moveZ,
            bool sprint,
            bool walk,
            float duration,
            bool authorityMoving)
        {
            if (duration <= 0f)
                return;

            Vector3 direction = new Vector3(moveX, 0f, moveZ);
            if (direction.sqrMagnitude <= 0.01f)
                return;
            direction.Normalize();
            float speed = PredictionSpeed(
                pawn,
                authorityMoving,
                sprint,
                walk);
            offset += direction * speed * duration;
        }

        private static Vector3 ClampPredictionToWalkable(
            Pawn pawn,
            Vector3 origin,
            Vector3 desired)
        {
            Vector3 delta = desired - origin;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= 0.01f)
                return origin;

            Vector3 direction = delta / distance;
            Vector3 accepted = origin;
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.2f));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 candidate = origin + direction *
                    (distance * i / steps);
                candidate.y = origin.y;
                if (!PredictionCellIsWalkable(pawn, candidate))
                    break;
                accepted = candidate;
            }
            return accepted;
        }

        private static Vector3 AuthoritativePathPosition(Pawn pawn)
        {
            Vector3 current = pawn.Position.ToVector3ShiftedWithAltitude(
                pawn.def.Altitude);
            Pawn_PathFollower pather = pawn.pather;
            if (pather == null || !pather.Moving || !pather.nextCell.IsValid ||
                pather.nextCell == pawn.Position || pather.nextCellCostTotal <= 0f)
            {
                return current;
            }

            float progress = Mathf.Clamp01(
                1f - pather.nextCellCostLeft / pather.nextCellCostTotal);
            Vector3 next = pather.nextCell.ToVector3ShiftedWithAltitude(
                pawn.def.Altitude);
            return Vector3.Lerp(current, next, progress);
        }

        private static Vector3 CurrentPathDirection(Pawn pawn)
        {
            Pawn_PathFollower pather = pawn.pather;
            if (pather == null || !pather.Moving || !pather.nextCell.IsValid ||
                pather.nextCell == pawn.Position)
            {
                return Vector3.zero;
            }

            Vector3 direction = (pather.nextCell - pawn.Position).ToVector3();
            direction.y = 0f;
            return direction.sqrMagnitude > 0.01f
                ? direction.normalized
                : Vector3.zero;
        }

        private static float PredictionSpeed(
            Pawn pawn,
            bool authorityMoving,
            bool sprint,
            bool walk)
        {
            Pawn_PathFollower pather = pawn.pather;
            if (authorityMoving && pather != null &&
                pather.nextCellCostTotal > 0f && pather.nextCell.IsValid)
            {
                Vector3 segment = (pather.nextCell - pawn.Position).ToVector3();
                segment.y = 0f;
                float segmentLength = Mathf.Max(1f, segment.magnitude);
                return 60f * segmentLength / pather.nextCellCostTotal;
            }

            // Match RimWorld's LocomotionUrgency cost multipliers while waiting
            // for the first authoritative path segment: Sprint=0.75 cost and
            // Amble=3x cost. The previous 0.65 walk factor substantially
            // over-predicted an Amble job and guaranteed repeated pullbacks.
            float gait = sprint ? (1f / 0.75f) : walk ? (1f / 3f) : 1f;
            return 60f / Mathf.Max(1f, pawn.TicksPerMoveCardinal) * gait;
        }

        private static bool PredictionCellIsWalkable(Pawn pawn, Vector3 position)
        {
            IntVec3 cell = position.ToIntVec3();
            return cell.InBounds(pawn.Map) && cell.WalkableBy(pawn.Map, pawn);
        }

        public static void CaptureDraftedState(Pawn_DraftController __instance, out bool __state)
        {
            __state = __instance.Drafted;
        }

        public static void ReleaseAvatarOnUndraft(
            Pawn_DraftController __instance, bool value, bool __state)
        {
            // MP already synchronizes the vanilla Drafted setter. A UI-side
            // invocation may be intercepted before the command is replayed;
            // releasing the lease there would mutate only the issuing peer.
            if (!MP.IsInMultiplayer || !Active || !MP.IsExecutingSyncCommand ||
                !__state || value || __instance.Drafted)
                return;

            Pawn pawn = __instance.pawn;
            PerspectiveShiftMpComponent component = CurrentComponent();
            PerspectiveShiftControlledAvatar state = component?.ForPawn(pawn);
            if (state == null)
                return;

            component.Release(state.owner, state.epoch);
            InvalidateControlledCache();
        }

        private static int _componentCacheIndex;
    }
}
