using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Shared, saveable simulation state for Perspective Shift avatars.
    /// The target mod's static State.Avatar remains a local view/UI pointer.
    /// </summary>
    public sealed class PerspectiveShiftControlledAvatar : IExposable
    {
        public string owner;
        public Pawn pawn;
        public int epoch;
        public int moveX;
        public int moveZ;
        public bool sprint;
        public bool walk;
        public int movementJobId = -1;
        public int lastInputTick = -1;
        public int lastAppliedInputSequence;
        public int nextMovementJobTick;
        public Lord savedLord;
        public Building pendingMinifiedPickup;
        public Building_Door interactingDoor;
        public bool wasFullyRested;
        public bool passedOut;
        public Dictionary<string, bool> needsAlerted = new Dictionary<string, bool>();

        [Unsaved] public object runtimeAvatar;
        [Unsaved] public int movementDiagnosticTick = -1;
        [Unsaved] public IntVec3 movementDiagnosticStart = IntVec3.Invalid;

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                Patch_PerspectiveShiftMp.CaptureRuntimeState(this);

            Scribe_Values.Look(ref owner, "owner");
            Scribe_References.Look(ref pawn, "pawn", true);
            Scribe_Values.Look(ref epoch, "epoch");
            Scribe_Values.Look(ref moveX, "moveX");
            Scribe_Values.Look(ref moveZ, "moveZ");
            Scribe_Values.Look(ref sprint, "sprint");
            Scribe_Values.Look(ref walk, "walk");
            Scribe_Values.Look(ref movementJobId, "movementJobId", -1);
            Scribe_Values.Look(ref lastInputTick, "lastInputTick", -1);
            Scribe_Values.Look(ref lastAppliedInputSequence, "lastAppliedInputSequence");
            Scribe_Values.Look(ref nextMovementJobTick, "nextMovementJobTick");
            Scribe_References.Look(ref savedLord, "savedLord");
            Scribe_References.Look(ref pendingMinifiedPickup, "pendingMinifiedPickup");
            Scribe_References.Look(ref interactingDoor, "interactingDoor");
            Scribe_Values.Look(ref wasFullyRested, "wasFullyRested");
            Scribe_Values.Look(ref passedOut, "passedOut");
            Scribe_Collections.Look(ref needsAlerted, "needsAlerted", LookMode.Value, LookMode.Value);
            if (needsAlerted == null)
                needsAlerted = new Dictionary<string, bool>();
        }
    }

    public sealed class PerspectiveShiftMpComponent : GameComponent
    {
        private List<PerspectiveShiftControlledAvatar> controlled = new List<PerspectiveShiftControlledAvatar>();
        [Unsaved] internal bool hadOwnershipRecordsAtLoad;

        public PerspectiveShiftMpComponent(Game game)
        {
        }

        public IEnumerable<PerspectiveShiftControlledAvatar> OrderedStates =>
            controlled
                .Where(s => s != null && s.pawn != null && !string.IsNullOrEmpty(s.owner))
                .OrderBy(s => s.pawn.thingIDNumber)
                .ThenBy(s => s.owner, StringComparer.Ordinal);

        public PerspectiveShiftControlledAvatar ForOwner(string owner)
        {
            return string.IsNullOrEmpty(owner)
                ? null
                : controlled.FirstOrDefault(s => s != null && string.Equals(s.owner, owner, StringComparison.Ordinal));
        }

        public PerspectiveShiftControlledAvatar ForPawn(Pawn pawn)
        {
            return pawn == null ? null : controlled.FirstOrDefault(s => s != null && s.pawn == pawn);
        }

        public bool IsControlled(Pawn pawn)
        {
            return ForPawn(pawn) != null;
        }

        public void Claim(string owner, Pawn pawn)
        {
            if (string.IsNullOrEmpty(owner) || pawn == null || pawn.Destroyed)
                return;

            PerspectiveShiftControlledAvatar occupied = ForPawn(pawn);
            if (occupied != null && !string.Equals(occupied.owner, owner, StringComparison.Ordinal))
            {
                if (MP.PlayerName == owner)
                    Messages.Message("该角色已由其他玩家控制。", pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }

            PerspectiveShiftControlledAvatar previous = ForOwner(owner);
            if (previous != null && previous.pawn == pawn)
            {
                Patch_PerspectiveShiftMp.EnsureRuntimeAvatar(previous);
                Patch_PerspectiveShiftMp.SetLocalAvatarIfOwned(previous);
                return;
            }

            int nextEpoch = previous == null ? 1 : previous.epoch + 1;
            if (previous != null)
                ReleaseState(previous, restoreLord: true);

            var state = new PerspectiveShiftControlledAvatar
            {
                owner = owner,
                pawn = pawn,
                epoch = nextEpoch,
                lastInputTick = Find.TickManager?.TicksGame ?? 0
            };
            controlled.Add(state);

            PreparePawnForControl(state);
            Patch_PerspectiveShiftMp.EnsureRuntimeAvatar(state);
            Patch_PerspectiveShiftMp.SetLocalAvatarIfOwned(state);
        }

        public void Release(string owner, int epoch)
        {
            PerspectiveShiftControlledAvatar state = ForOwner(owner);
            if (state == null || (epoch >= 0 && state.epoch != epoch))
                return;

            ReleaseState(state, restoreLord: true);
        }

        public void SetMoveIntent(
            string owner,
            Pawn pawn,
            int epoch,
            int inputSequence,
            int moveX,
            int moveZ,
            bool sprint,
            bool walk)
        {
            PerspectiveShiftControlledAvatar state = ForOwner(owner);
            if (state == null || state.pawn != pawn || state.epoch != epoch)
                return;
            if (inputSequence <= state.lastAppliedInputSequence)
                return;

            int normalizedX = Math.Max(-1, Math.Min(1, moveX));
            int normalizedZ = Math.Max(-1, Math.Min(1, moveZ));
            bool normalizedWalk = !sprint && walk;
            bool directionChanged = state.moveX != normalizedX || state.moveZ != normalizedZ;
            bool gaitChanged = state.sprint != sprint || state.walk != normalizedWalk;
            state.moveX = normalizedX;
            state.moveZ = normalizedZ;
            state.sprint = sprint;
            state.walk = normalizedWalk;
            state.lastAppliedInputSequence = inputSequence;
            state.lastInputTick = Find.TickManager?.TicksGame ?? 0;
            Patch_PerspectiveShiftMp.CopyIntentToRuntime(state);
            Patch_PerspectiveShiftMp.NotifyMoveIntentApplied(
                state,
                inputSequence);

            if (state.moveX == 0 && state.moveZ == 0)
                StopMovementJob(state);
            else
            {
                // Changing sprint/walk used to tear down and recreate the Goto
                // job even though its route and destination were unchanged. That
                // introduces a visible pause on every modifier transition. The
                // command is already replayed identically on every peer, so the
                // active compatibility-owned job can safely adopt the new gait.
                if (gaitChanged && IsMovementJob(state))
                    state.pawn.CurJob.locomotionUrgency = MovementUrgency(state);
                bool retargeted = directionChanged &&
                                  TryRetargetMovementJob(state);
                EnsureMovementJob(
                    state,
                    forceNew: directionChanged && !retargeted);
            }
        }

        public override void GameComponentTick()
        {
            if (!MP.IsInMultiplayer || !Patch_PerspectiveShiftMp.Active)
                return;

            if (MpRuntimeInfo.TryGetAsyncTimeActive(out bool asyncTime) && asyncTime)
                return;

            TickStates(OrderedStates.ToList());
        }

        public void TickForMap(Map map)
        {
            if (map == null || !MP.IsInMultiplayer || !Patch_PerspectiveShiftMp.Active)
                return;

            TickStates(OrderedStates.Where(state => state.pawn?.Map == map).ToList());
        }

        private void TickStates(IEnumerable<PerspectiveShiftControlledAvatar> states)
        {
            foreach (PerspectiveShiftControlledAvatar state in states)
            {
                Pawn pawn = state.pawn;
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    ReleaseState(state, restoreLord: false);
                    continue;
                }

                Patch_PerspectiveShiftMp.EnsureRuntimeAvatar(state);
                Patch_PerspectiveShiftMp.CopyIntentToRuntime(state);

                int now = Find.TickManager?.TicksGame ?? 0;
                if ((state.moveX != 0 || state.moveZ != 0) &&
                    state.lastInputTick >= 0 && now - state.lastInputTick > 120)
                {
                    state.moveX = 0;
                    state.moveZ = 0;
                    state.sprint = false;
                    state.walk = false;
                    StopMovementJob(state);
                    Patch_PerspectiveShiftMp.CopyIntentToRuntime(state);
                    continue;
                }

                if (state.moveX != 0 || state.moveZ != 0)
                {
                    EnsureMovementJob(state, forceNew: false);
                    if (state.movementDiagnosticTick >= 0 && now >= state.movementDiagnosticTick)
                    {
                        state.movementDiagnosticTick = -1;
                        if (ModDebug.EnablePerspectiveShiftTrace)
                        {
                            Log.Message(
                                $"[MP-MeowOnlineShop] Perspective Shift movement checkpoint: " +
                                $"owner={state.owner}, pawn={pawn.thingIDNumber}, " +
                                $"position={state.movementDiagnosticStart}->{pawn.Position}, " +
                                $"job={pawn.CurJob?.def?.defName ?? "<null>"}#{pawn.CurJob?.loadID ?? -1}, " +
                                $"trackedJob={state.movementJobId}, moving={pawn.pather.Moving}.");
                        }
                    }
                }
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            NormalizeLoadedRegistry();
            foreach (PerspectiveShiftControlledAvatar state in OrderedStates)
                Patch_PerspectiveShiftMp.EnsureRuntimeAvatar(state);

            Patch_PerspectiveShiftMp.RestoreLocalAvatarFromRegistry();
        }

        private void NormalizeLoadedRegistry()
        {
            hadOwnershipRecordsAtLoad = controlled.Any(state => state != null);
            var valid = controlled
                .Where(state =>
                    state != null && state.pawn != null &&
                    !state.pawn.Destroyed &&
                    !string.IsNullOrEmpty(state.owner))
                .ToList();
            var duplicateOwners = new HashSet<string>(
                valid.GroupBy(state => state.owner, StringComparer.Ordinal)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.Ordinal);
            var duplicatePawns = new HashSet<Pawn>(
                valid.GroupBy(state => state.pawn)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key));

            List<PerspectiveShiftControlledAvatar> rejected = controlled
                .Where(state => state != null &&
                    (state.pawn == null || state.pawn.Destroyed ||
                     string.IsNullOrEmpty(state.owner) ||
                     duplicateOwners.Contains(state.owner) ||
                     duplicatePawns.Contains(state.pawn)))
                .Distinct()
                .OrderBy(state => state.owner, StringComparer.Ordinal)
                .ThenBy(state => state.pawn?.thingIDNumber ?? -1)
                .ToList();
            foreach (PerspectiveShiftControlledAvatar state in rejected)
            {
                state.moveX = 0;
                state.moveZ = 0;
                state.sprint = false;
                state.walk = false;
                if (state.pawn != null && !state.pawn.Destroyed)
                    StopMovementJob(state);
            }

            // Ambiguous ownership is never guessed. Dropping every side of a
            // duplicate makes affected players explicitly reclaim a pawn after
            // joining instead of binding one peer to somebody else's avatar.
            controlled = valid
                .Where(state =>
                    !duplicateOwners.Contains(state.owner) &&
                    !duplicatePawns.Contains(state.pawn))
                .OrderBy(state => state.pawn.thingIDNumber)
                .ThenBy(state => state.owner, StringComparer.Ordinal)
                .ToList();

            if (rejected.Count > 0)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Perspective Shift removed ambiguous saved " +
                    $"avatar ownership while joining: records={rejected.Count}, " +
                    $"owners={duplicateOwners.Count}, pawns={duplicatePawns.Count}. " +
                    "No local Avatar will be bound until an explicit synchronized claim.");
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref controlled, "mp_perspective_shift_controlled", LookMode.Deep);
            if (controlled == null)
                controlled = new List<PerspectiveShiftControlledAvatar>();
        }

        private void PreparePawnForControl(PerspectiveShiftControlledAvatar state)
        {
            Pawn pawn = state.pawn;
            if (pawn.jobs != null && pawn.Spawned)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                pawn.pather?.StopDead();

                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = 60;
                wait.checkOverrideOnExpire = true;
                pawn.jobs.TryTakeOrderedJob(wait);
            }

            Lord lord = pawn.GetLord();
            if (lord != null)
            {
                state.savedLord = lord;
                lord.Notify_PawnLost(pawn, PawnLostCondition.Undefined);
            }
        }

        private void ReleaseState(PerspectiveShiftControlledAvatar state, bool restoreLord)
        {
            StopMovementJob(state);

            Pawn pawn = state.pawn;
            if (pawn?.drafter != null && pawn.Drafted)
                pawn.drafter.Drafted = false;

            if (restoreLord && pawn != null && state.savedLord?.lordManager != null &&
                state.savedLord.lordManager.lords.Contains(state.savedLord))
            {
                state.savedLord.AddPawn(pawn);
                state.savedLord.CurLordToil?.UpdateAllDuties();
            }

            controlled.Remove(state);
            Patch_PerspectiveShiftMp.ClearLocalAvatarIfOwned(state.owner);
        }

        private static bool IsMovementJob(PerspectiveShiftControlledAvatar state)
        {
            return state?.pawn?.CurJob != null && state.movementJobId >= 0 &&
                   state.pawn.CurJob.loadID == state.movementJobId;
        }

        private static void StopMovementJob(PerspectiveShiftControlledAvatar state)
        {
            if (IsMovementJob(state))
                state.pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            state.movementJobId = -1;
        }

        private static bool TryRetargetMovementJob(
            PerspectiveShiftControlledAvatar state)
        {
            if (!IsMovementJob(state) || state.pawn?.pather == null)
                return false;

            Pawn pawn = state.pawn;
            IntVec3 destination = FindDestination(
                pawn,
                state.moveX,
                state.moveZ);
            if (!destination.IsValid || destination == pawn.Position)
                return false;

            // Keep the same Job/loadID when WASD direction changes. Ending and
            // recreating the Goto job made quick W->W+D transitions visibly
            // stop the authoritative pawn. Updating targetA and restarting only
            // the path request is deterministic because this runs inside the
            // ordered movement-intent command on every peer.
            pawn.CurJob.targetA = destination;
            pawn.CurJob.locomotionUrgency = MovementUrgency(state);
            pawn.pather.StartPath(destination, PathEndMode.OnCell);
            state.nextMovementJobTick =
                (Find.TickManager?.TicksGame ?? 0) + 10;
            return pawn.pather.Moving;
        }

        private static void EnsureMovementJob(PerspectiveShiftControlledAvatar state, bool forceNew)
        {
            Pawn pawn = state.pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Downed ||
                pawn.InMentalState || pawn.jobs == null || pawn.pather == null)
                return;

            if (!forceNew && IsMovementJob(state) && pawn.pather.Moving)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (!forceNew && now < state.nextMovementJobTick)
                return;
            state.nextMovementJobTick = now + 10;

            IntVec3 destination = FindDestination(pawn, state.moveX, state.moveZ);
            if (!destination.IsValid || destination == pawn.Position)
                return;

            if (forceNew && pawn.CurJob != null &&
                (IsMovementJob(state) ||
                 pawn.CurJob.def == JobDefOf.Wait ||
                 pawn.CurJob.def == JobDefOf.Wait_Combat))
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                state.movementJobId = -1;
            }

            Job job = JobMaker.MakeJob(JobDefOf.Goto, destination);
            job.playerForced = true;
            job.expiryInterval = 240;
            job.checkOverrideOnExpire = true;
            job.locomotionUrgency = MovementUrgency(state);

            bool accepted = pawn.jobs.TryTakeOrderedJob(job);
            if (accepted)
            {
                state.movementJobId = job.loadID;
                state.movementDiagnosticStart = pawn.Position;
                state.movementDiagnosticTick = now + 30;
            }

            if (forceNew)
            {
                if (ModDebug.EnablePerspectiveShiftTrace)
                {
                    Log.Message(
                        $"[MP-MeowOnlineShop] Perspective Shift movement job request: " +
                        $"owner={state.owner}, pawn={pawn.thingIDNumber}, from={pawn.Position}, " +
                        $"destination={destination}, input=({state.moveX},{state.moveZ}), " +
                        $"accepted={accepted}, currentJob={pawn.CurJob?.def?.defName ?? "<null>"}" +
                        $"#{pawn.CurJob?.loadID ?? -1}, trackedJob={state.movementJobId}.");
                }
            }
        }

        private static LocomotionUrgency MovementUrgency(
            PerspectiveShiftControlledAvatar state)
        {
            return state.sprint
                ? LocomotionUrgency.Sprint
                : state.walk ? LocomotionUrgency.Amble : LocomotionUrgency.Jog;
        }

        private static IntVec3 FindDestination(Pawn pawn, int moveX, int moveZ)
        {
            int dx = Math.Max(-1, Math.Min(1, moveX));
            int dz = Math.Max(-1, Math.Min(1, moveZ));
            if (dx == 0 && dz == 0)
                return IntVec3.Invalid;

            for (int distance = 8; distance >= 1; distance--)
            {
                IntVec3 cell = pawn.Position + new IntVec3(dx * distance, 0, dz * distance);
                if (cell.InBounds(pawn.Map) && cell.WalkableBy(pawn.Map, pawn))
                    return cell;
            }

            return IntVec3.Invalid;
        }
    }
}
