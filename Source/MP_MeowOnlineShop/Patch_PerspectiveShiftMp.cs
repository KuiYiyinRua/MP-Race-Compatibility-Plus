using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private static FieldInfo avatarPawnField;
        private static FieldInfo moveInputField;
        private static FieldInfo sprintField;
        private static FieldInfo walkField;
        private static FieldInfo physicsPositionField;
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

        private static int lastMoveX;
        private static int lastMoveZ;
        private static bool lastSprint;
        private static bool lastWalk;
        private static bool sentInput;
        private static int lastSentInputTick = -999999;

        [ThreadStatic] private static bool iteratingControlledAvatars;
        [ThreadStatic] private static bool clearingLocalView;
        [ThreadStatic] private static IntVec3? forcedMouseCell;
        [ThreadStatic] private static string executingCommandOwner;

        private static bool commandOwnerResolutionWarningLogged;
        private static bool localAvatarBindingLogged;
        private static bool localMovementInputLogged;

        public static bool Active { get; private set; }

        public static void Apply(Harmony harmony)
        {
            if (!ModsConfig.IsActive(PackageId))
                return;

            stateType = AccessTools.TypeByName("PerspectiveShift.State");
            avatarType = AccessTools.TypeByName("PerspectiveShift.Avatar");
            if (stateType == null || avatarType == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Perspective Shift detected, but its State/Avatar types were not found.");
                return;
            }

            stateAvatarField = AccessTools.Field(stateType, "Avatar");
            avatarPawnField = AccessTools.Field(avatarType, "pawn");
            moveInputField = AccessTools.Field(avatarType, "moveInput");
            sprintField = AccessTools.Field(avatarType, "isSprinting");
            walkField = AccessTools.Field(avatarType, "isWalking");
            physicsPositionField = AccessTools.Field(avatarType, "physicsPosition");
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

            MethodInfo setAvatar = AccessTools.Method(stateType, "SetAvatar", new[] { typeof(Pawn), typeof(bool) });
            MethodInfo clearAvatar = originalClearAvatarMethod;
            MethodInfo stateTick = AccessTools.Method(stateType, "Tick");
            MethodInfo isAvatar = AccessTools.Method(stateType, "IsAvatar", new[] { typeof(Pawn) });
            MethodInfo updatePhysics = AccessTools.Method(avatarType, "UpdatePhysics");

            if (stateAvatarField == null || avatarPawnConstructor == null || setAvatar == null ||
                clearAvatar == null || stateTick == null || isAvatar == null || updatePhysics == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Perspective Shift API shape is unsupported; compatibility patch skipped.");
                return;
            }

            harmony.Patch(setAvatar, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(SetAvatarPrefix)));
            harmony.Patch(clearAvatar, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(ClearAvatarPrefix)));
            harmony.Patch(stateTick, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(StateTickPrefix)));
            harmony.Patch(isAvatar, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(IsAvatarPrefix)));
            harmony.Patch(updatePhysics, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(UpdatePhysicsPrefix)));

            MethodInfo handleSelectorClick = AccessTools.Method(avatarType, "HandleSelectorClick");
            if (handleSelectorClick != null)
                harmony.Patch(handleSelectorClick, prefix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(HandleSelectorClickPrefix)));

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

            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncClaimAvatar));
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncReleaseAvatar));
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncSetMoveIntent));
            MP.RegisterSyncMethod(typeof(Patch_PerspectiveShiftMp), nameof(SyncAvatarMapClick));

            Active = true;
            Log.Message("[MP-MeowOnlineShop] Perspective Shift MP: multi-avatar registry, ordered movement input, and visual prediction enabled.");
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
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Pawn_PathFollower_DrawPath_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Pawn_RotationTracker_UpdateRotation_Patch", "Prefix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.Pawn_WorkSettings_GetPriority_Patch", "Postfix");
            PatchTargetPatchMethod(harmony, "PerspectiveShift.JobDriver_ManTurret_Patch", "Prefix");
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
                    postfix: new HarmonyMethod(typeof(Patch_PerspectiveShiftMp), nameof(EndJobPostfixContextPostfix)));
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

        public static void SyncClaimAvatar(string owner, Pawn pawn)
        {
            CurrentComponent()?.Claim(owner, pawn);
        }

        public static void SyncReleaseAvatar(string owner, int epoch)
        {
            CurrentComponent()?.Release(owner, epoch);
        }

        public static void SyncSetMoveIntent(string owner, Pawn pawn, int epoch, int moveX, int moveZ, bool sprint, bool walk)
        {
            CurrentComponent()?.SetMoveIntent(owner, pawn, epoch, moveX, moveZ, sprint, walk);
        }

        public static void SyncAvatarMapClick(string owner, Pawn pawn, int epoch, IntVec3 cell, int button, int clickCount)
        {
            PerspectiveShiftControlledAvatar state = CurrentComponent()?.ForOwner(owner);
            if (state == null || state.pawn != pawn || state.epoch != epoch || pawn == null || pawn.Dead)
                return;

            EnsureRuntimeAvatar(state);
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
                        handleFiringMethod?.Invoke(state.runtimeAvatar, null);
                    }
                    else if (handleLeftClickIntMethod != null)
                    {
                        isAvatarLeftClickField?.SetValue(null, true);
                        handleLeftClickIntMethod.Invoke(state.runtimeAvatar, null);
                    }
                }
                else if (button == 1)
                {
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
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
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
                stateAvatarField.SetValue(null, previousAvatar);
            }
        }

        public static bool SetAvatarPrefix(Pawn pawn)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            if (pawn != null)
            {
                string owner = CurrentCommandOwnerOrLocalPlayer();
                if (!string.IsNullOrEmpty(owner))
                    SyncClaimAvatar(owner, pawn);
            }
            return false;
        }

        public static bool ClearAvatarPrefix()
        {
            if (clearingLocalView)
                return true;
            if (!MP.IsInMultiplayer || !Active)
                return true;

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

        public static bool IsAvatarPrefix(Pawn pawn, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            __result = CurrentComponent()?.IsControlled(pawn) == true;
            return false;
        }

        public static bool StateTickPrefix()
        {
            if (!MP.IsInMultiplayer || !Active || iteratingControlledAvatars)
                return true;

            PerspectiveShiftMpComponent component = CurrentComponent();
            if (component == null || avatarTickMethod == null)
                return false;

            object localAvatar = stateAvatarField.GetValue(null);
            iteratingControlledAvatars = true;
            try
            {
                foreach (PerspectiveShiftControlledAvatar state in component.OrderedStates)
                {
                    if (state.pawn == null || state.pawn.Dead)
                        continue;

                    EnsureRuntimeAvatar(state);
                    CopyIntentToRuntime(state);
                    stateAvatarField.SetValue(null, state.runtimeAvatar);
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
                stateAvatarField.SetValue(null, localAvatar);
                iteratingControlledAvatars = false;
            }

            return false;
        }

        public static bool UpdatePhysicsPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            Pawn pawn = avatarPawnField?.GetValue(__instance) as Pawn;
            PerspectiveShiftControlledAvatar state = CurrentComponent()?.ForOwner(MP.PlayerName);
            if (pawn != null && state == null && MP.IsHosting && !MP.IsExecutingSyncCommand)
            {
                // Migrate a pre-existing single-player Perspective Shift avatar
                // when that save is first hosted. Only the host may seed it.
                SyncClaimAvatar(MP.PlayerName, pawn);
                return false;
            }
            if (pawn == null || state == null || state.pawn != pawn)
                return false;

            int moveX = 0;
            int moveZ = 0;
            if (KeyDown("PS_MoveLeft")) moveX--;
            if (KeyDown("PS_MoveRight")) moveX++;
            if (KeyDown("PS_MoveBack")) moveZ--;
            if (KeyDown("PS_MoveForward")) moveZ++;

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

            bool sprint = hasMoveInput && KeyDown("PS_Sprint");
            bool walk = hasMoveInput && !sprint && KeyDown("PS_Walk");

            if (hasMoveInput && cameraLockPositionField?.GetValue(null) != null)
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

                Log.Message(
                    $"[MP-MeowOnlineShop] Perspective Shift local movement input accepted: " +
                    $"owner={MP.PlayerName}, pawn={pawn.thingIDNumber}, input=({moveX},{moveZ}), " +
                    $"targetControlsFrozen={targetControlsFrozen}.");
            }

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (!sentInput || currentTick - lastSentInputTick >= 45 ||
                moveX != lastMoveX || moveZ != lastMoveZ ||
                sprint != lastSprint || walk != lastWalk)
            {
                lastMoveX = moveX;
                lastMoveZ = moveZ;
                lastSprint = sprint;
                lastWalk = walk;
                sentInput = true;
                lastSentInputTick = currentTick;
                SyncSetMoveIntent(MP.PlayerName, pawn, state.epoch, moveX, moveZ, sprint, walk);
            }

            UpdateVisualPrediction(__instance, pawn, moveX, moveZ, sprint, walk);
            return false;
        }

        public static bool HandleSelectorClickPrefix(object __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active || MP.IsExecutingSyncCommand)
                return true;

            Pawn pawn = avatarPawnField?.GetValue(__instance) as Pawn;
            PerspectiveShiftControlledAvatar state = CurrentComponent()?.ForOwner(MP.PlayerName);
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

            SyncAvatarMapClick(
                MP.PlayerName,
                pawn,
                state.epoch,
                UI.MouseCell(),
                currentEvent.button,
                currentEvent.clickCount);
            __result = true;
            return false;
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

            Pawn pawn = avatarPawnField?.GetValue(__instance) as Pawn;
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
                    reserver.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                }

                pawn.Map.reservationManager.ReleaseAllForTarget(storedItem);
                pawn.Map.physicalInteractionReservationManager.ReleaseAllForTarget(storedItem);
                pawn.carryTracker.TryStartCarry(
                    storedItem,
                    storedItem.stackCount,
                    reserve: true);
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
            if (!MP.IsInMultiplayer || !Active)
                return true;

            __result = true;
            return false;
        }

        public static bool ReplacePerspectiveShiftTweenerPrefix(object[] __args, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            object tweener = __args != null && __args.Length > 0 ? __args[0] : null;
            Pawn pawn = tweener == null ? null : AccessTools.Field(tweener.GetType(), "pawn")?.GetValue(tweener) as Pawn;
            object localAvatar = stateAvatarField?.GetValue(null);
            Pawn localPawn = localAvatar == null ? null : avatarPawnField?.GetValue(localAvatar) as Pawn;
            bool hasPrediction = localAvatar != null && physicsPositionField?.GetValue(localAvatar) != null;
            __result = pawn == null || pawn != localPawn || !hasPrediction;
            return false;
        }

        public static bool SkipInMultiplayerPrefix()
        {
            return !MP.IsInMultiplayer || !Active;
        }

        public static bool SuppressTargetBoolPatchPrefix(ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            // The patched method is itself a Harmony prefix. Returning true from it
            // tells Harmony to continue with the real vanilla method.
            __result = true;
            return false;
        }

        public static bool LocalAvatarOnlyTargetBoolPrefix(object[] __args, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !Active)
                return true;

            Pawn targetPawn = null;
            if (__args != null)
            {
                foreach (object arg in __args)
                {
                    if (arg is Pawn directPawn)
                    {
                        targetPawn = directPawn;
                        break;
                    }

                    if (arg != null)
                    {
                        FieldInfo pawnField = AccessTools.Field(arg.GetType(), "pawn");
                        if (pawnField?.GetValue(arg) is Pawn nestedPawn)
                        {
                            targetPawn = nestedPawn;
                            break;
                        }
                    }
                }
            }

            object localAvatar = stateAvatarField?.GetValue(null);
            Pawn localPawn = localAvatar == null ? null : avatarPawnField?.GetValue(localAvatar) as Pawn;
            if (targetPawn != null && targetPawn == localPawn)
                return true;

            // The target method is itself a Harmony prefix; true means the
            // underlying vanilla render method should continue.
            __result = true;
            return false;
        }

        public static bool EndJobPostfixContextPrefix(object[] __args, out object __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !Active)
                return true;

            Pawn_JobTracker tracker = __args?.OfType<Pawn_JobTracker>().FirstOrDefault();
            Pawn trackerPawn = tracker == null
                ? null
                : AccessTools.Field(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn;
            PerspectiveShiftControlledAvatar controlledState = CurrentComponent()?.ForPawn(trackerPawn);
            if (controlledState == null)
                return false;

            EnsureRuntimeAvatar(controlledState);
            CopyIntentToRuntime(controlledState);
            __state = stateAvatarField.GetValue(null);
            stateAvatarField.SetValue(null, controlledState.runtimeAvatar);
            return true;
        }

        public static void EndJobPostfixContextPostfix(object __state)
        {
            if (MP.IsInMultiplayer && Active && stateAvatarField != null)
                stateAvatarField.SetValue(null, __state);
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
            if (state != null && string.Equals(state.owner, MP.PlayerName, StringComparison.Ordinal))
            {
                EnsureRuntimeAvatar(state);
                cameraLockPositionField?.SetValue(null, null);
                isActiveCacheFrameField?.SetValue(null, -999);
                stateAvatarField?.SetValue(null, state.runtimeAvatar);
                sentInput = false;
                localMovementInputLogged = false;

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
            if (state != null)
                SetLocalAvatarIfOwned(state);
        }

        private static void ClearLocalViewOnly()
        {
            stateAvatarField?.SetValue(null, null);
            sentInput = false;
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
            return Current.Game?.GetComponent<PerspectiveShiftMpComponent>();
        }

        private static Pawn LocalAvatarPawn()
        {
            object localAvatar = stateAvatarField?.GetValue(null);
            return localAvatar == null ? null : avatarPawnField?.GetValue(localAvatar) as Pawn;
        }

        private static bool KeyDown(string defName)
        {
            KeyBindingDef key = DefDatabase<KeyBindingDef>.GetNamedSilentFail(defName);
            return key != null && key.IsDown;
        }

        private static void UpdateVisualPrediction(object avatar, Pawn pawn, int moveX, int moveZ, bool sprint, bool walk)
        {
            if (physicsPositionField == null || pawn.Map == null || !pawn.Spawned)
                return;

            Vector3 authoritative = pawn.Position.ToVector3ShiftedWithAltitude(pawn.def.Altitude);
            Vector3 predicted = (Vector3?)physicsPositionField.GetValue(avatar) ?? authoritative;
            Vector3 direction = new Vector3(moveX, 0f, moveZ);

            if (direction.sqrMagnitude > 0.01f)
            {
                direction.Normalize();
                float gait = sprint ? 1.35f : walk ? 0.65f : 1f;
                float cellsPerSecond = 60f / Mathf.Max(1f, pawn.TicksPerMoveCardinal);
                predicted += direction * cellsPerSecond * gait * Time.deltaTime;

                Vector3 offset = predicted - authoritative;
                offset.y = 0f;
                const float maxPredictionDistance = 1.25f;
                if (offset.magnitude > maxPredictionDistance)
                    predicted = authoritative + offset.normalized * maxPredictionDistance;
            }
            else
            {
                predicted = Vector3.Lerp(predicted, authoritative, 1f - Mathf.Exp(-12f * Time.deltaTime));
                if ((predicted - authoritative).sqrMagnitude < 0.0025f)
                    predicted = authoritative;
            }

            predicted.y = authoritative.y;
            physicsPositionField.SetValue(avatar, (Vector3?)predicted);
        }
    }
}
