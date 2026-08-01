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
        public int nextMovementJobTick;
        public Lord savedLord;
        public Building pendingMinifiedPickup;
        public Building_Door interactingDoor;
        public bool wasFullyRested;
        public bool passedOut;
        public Dictionary<string, bool> needsAlerted = new Dictionary<string, bool>();

        [Unsaved] public object runtimeAvatar;

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

        public void SetMoveIntent(string owner, Pawn pawn, int epoch, int moveX, int moveZ, bool sprint, bool walk)
        {
            PerspectiveShiftControlledAvatar state = ForOwner(owner);
            if (state == null || state.pawn != pawn || state.epoch != epoch)
                return;

            int normalizedX = Math.Max(-1, Math.Min(1, moveX));
            int normalizedZ = Math.Max(-1, Math.Min(1, moveZ));
            bool normalizedWalk = !sprint && walk;
            bool changed = state.moveX != normalizedX || state.moveZ != normalizedZ ||
                           state.sprint != sprint || state.walk != normalizedWalk;
            state.moveX = normalizedX;
            state.moveZ = normalizedZ;
            state.sprint = sprint;
            state.walk = normalizedWalk;
            state.lastInputTick = Find.TickManager?.TicksGame ?? 0;
            Patch_PerspectiveShiftMp.CopyIntentToRuntime(state);

            if (state.moveX == 0 && state.moveZ == 0)
                StopMovementJob(state);
            else
                EnsureMovementJob(state, forceNew: changed);
        }

        public override void GameComponentTick()
        {
            if (!MP.IsInMultiplayer || !Patch_PerspectiveShiftMp.Active)
                return;

            foreach (PerspectiveShiftControlledAvatar state in OrderedStates.ToList())
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
                    EnsureMovementJob(state, forceNew: false);
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            controlled.RemoveAll(s => s == null || s.pawn == null || string.IsNullOrEmpty(s.owner));
            foreach (PerspectiveShiftControlledAvatar state in OrderedStates)
                Patch_PerspectiveShiftMp.EnsureRuntimeAvatar(state);

            Patch_PerspectiveShiftMp.RestoreLocalAvatarFromRegistry();
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
            job.locomotionUrgency = state.sprint
                ? LocomotionUrgency.Sprint
                : state.walk ? LocomotionUrgency.Amble : LocomotionUrgency.Jog;

            if (pawn.jobs.TryTakeOrderedJob(job))
                state.movementJobId = job.loadID;
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
