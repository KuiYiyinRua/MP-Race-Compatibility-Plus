using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Saveable records for successful role-change rituals.  Vanilla keeps the
    /// role's selected pawn in the ideology, but a client-side post-load or
    /// activity recache can clear it before the multifaction context is ready.
    /// These records preserve only ritual-produced assignments, never general
    /// role UI state.
    /// </summary>
    public sealed class RitualRoleAssignmentPersistenceComponent : GameComponent
    {
        private List<RitualRoleAssignmentRecord> records = new List<RitualRoleAssignmentRecord>();

        public RitualRoleAssignmentPersistenceComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "mpMeowRitualRoleAssignments", LookMode.Deep);
            if (records == null)
                records = new List<RitualRoleAssignmentRecord>();
        }

        public void Record(Precept_Role role, Pawn pawn)
        {
            if (role == null || pawn == null)
                return;

            Forget(role);
            records.Add(new RitualRoleAssignmentRecord(role, pawn));
        }

        public bool ShouldRetain(Precept_Role role, Pawn pawn)
        {
            RitualRoleAssignmentRecord record = FindRecord(role);
            if (record == null || record.pawn != pawn)
                return false;

            if (!record.IsStillValid(role))
            {
                records.Remove(record);
                return false;
            }

            return true;
        }

        public void Restore(Precept_Role role)
        {
            RitualRoleAssignmentRecord record = FindRecord(role);
            if (record == null)
                return;

            if (!record.IsStillValid(role))
            {
                records.Remove(record);
                return;
            }

            if (!role.IsAssigned(record.pawn))
                role.Assign(record.pawn, addThoughts: false);
        }

        public void Forget(Precept_Role role)
        {
            if (role == null)
                return;

            records.RemoveAll(record => record?.Matches(role) == true);
        }

        private RitualRoleAssignmentRecord FindRecord(Precept_Role role)
        {
            return role == null
                ? null
                : records.FirstOrDefault(record => record?.Matches(role) == true);
        }
    }

    public sealed class RitualRoleAssignmentRecord : IExposable
    {
        public Ideo ideo;
        public PreceptDef roleDef;
        public int roleIndex = -1;
        public Pawn pawn;

        public RitualRoleAssignmentRecord()
        {
        }

        public RitualRoleAssignmentRecord(Precept_Role role, Pawn assignedPawn)
        {
            ideo = role.ideo;
            roleDef = role.def;
            roleIndex = ideo?.RolesListForReading?.IndexOf(role) ?? -1;
            pawn = assignedPawn;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref ideo, "ideo");
            Scribe_Defs.Look(ref roleDef, "roleDef");
            Scribe_Values.Look(ref roleIndex, "roleIndex", -1);
            Scribe_References.Look(ref pawn, "pawn", true);
        }

        public bool Matches(Precept_Role role)
        {
            return role != null && role.ideo == ideo && role.def == roleDef &&
                roleIndex >= 0 && ideo?.RolesListForReading != null &&
                roleIndex < ideo.RolesListForReading.Count &&
                ideo.RolesListForReading[roleIndex] == role;
        }

        public bool IsStillValid(Precept_Role role)
        {
            // Ritual-produced assignments are retained while the pawn is still
            // a free non-slave colonist of the role's ideology. Role
            // requirements are not re-checked here: the persistence patch
            // makes these records pass Precept_Role.ValidatePawn, which is the
            // validity check used by Assign and RecacheActivity.
            return Matches(role) && pawn != null && !pawn.Destroyed && !pawn.Dead &&
                pawn.Faction != null && pawn.Faction.IsPlayer && pawn.HostFaction == null &&
                pawn.RaceProps.Humanlike && !pawn.IsSlave && pawn.Ideo == ideo;
        }
    }
}
