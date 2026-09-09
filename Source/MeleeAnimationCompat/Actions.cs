using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AM;
using AM.Buildings;
using AM.Controller;
using AM.Controller.Reports;
using AM.Controller.Requests;
using AM.Grappling;
using AM.Idle;
using AM.UI;
using AM.UniqueSkills;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    internal static class Actions
    {
        internal static ISyncMethod ExecuteCommand, GrappleCommand, SkillCommand, VisibilityCommand;
        [ThreadStatic] internal static bool ReplayingExecution;
        internal static readonly MethodInfo ExecuteOriginal = AccessTools.DeclaredMethod(
            typeof(DraftedFloatMenuOptionsUI), "ExecutionEnabledOnClick",
            new[] { typeof(Pawn), typeof(Pawn), typeof(ExecutionAttemptReport), typeof(ExecutionAttemptRequest) });

        internal static void Register()
        {
            if (ExecuteOriginal == null) throw new MissingMethodException("ExecutionEnabledOnClick");
            ExecuteCommand = MP.RegisterSyncMethod(typeof(Actions), nameof(Execute));
            GrappleCommand = MP.RegisterSyncMethod(typeof(Actions), nameof(Grapple));
            SkillCommand = MP.RegisterSyncMethod(typeof(Actions), nameof(Skill));
            VisibilityCommand = MP.RegisterSyncMethod(typeof(Actions), nameof(SetSpotHidden));
        }
        private static void SetSpotHidden(Building_DuelSpot spot,bool hidden)
        {
            if(spot!=null && spot.Spawned) spot.IsHidden=hidden;
        }

        internal static bool ValidPair(Pawn a, Pawn b) => a != null && b != null && a != b
            && a.Spawned && b.Spawned && a.Map == b.Map && !a.Dead && !a.Downed;

        internal static bool ConfirmFriendly(Pawn target)
        {
            if (!Core.Settings.WarnOfFriendlyExecution || target.HostileTo(Faction.OfPlayer)
                || target.RaceProps.Animal || Input.GetKey(KeyCode.LeftShift)) return true;
            Messages.Message("Tried to execute friendly: hold the Shift key when selecting to confirm!",
                MessageTypeDefOf.RejectInput, false);
            return false;
        }

        private static void Execute(Pawn attacker, Pawn target, bool canLasso, bool canWalk, AnimDef[] onlyAnimations)
        {
            if (!ValidPair(attacker, target)) return;
            if (onlyAnimations != null)
            {
                // Skill-menu eligibility must be checked again after command latency.
                // The generic execution report deliberately bypasses weapon filtering
                // for OnlyTheseAnimations, so it cannot validate a special skill.
                foreach (var def in DefDatabase<UniqueSkillDef>.AllDefsListForReading
                    .Where(d => d.type == SkillType.Execution && onlyAnimations.Contains(d.animation)))
                {
                    var skill = attacker.GetComp<IdleControllerComp>()?.GetSkills()?.FirstOrDefault(s => s?.Def == def);
                    if (skill == null || !skill.IsEnabledForPawn(out _) || skill.CanTriggerOn(target) != null) return;
                }
            }
            ulong mask = SpaceChecker.MakeOccupiedMask(attacker.Map, attacker.Position, out uint small);
            var request = new ExecutionAttemptRequest
            {
                Executioner = attacker, Target = target, CanUseLasso = canLasso, CanWalk = canWalk,
                OccupiedMask = mask, SmallOccupiedMask = small,
                EastCell = !mask.GetBit(1, 0), WestCell = !mask.GetBit(-1, 0),
                OnlyTheseAnimations = onlyAnimations
            };
            // Original callback revalidates against the current simulation and applies
            // verbs, jobs, animation selection, outcome and cooldown as one command.
            ReplayingExecution = true;
            try { ExecuteOriginal.Invoke(null, new object[] { target, attacker, default(ExecutionAttemptReport), request }); }
            finally { ReplayingExecution = false; }
        }

        private static void Grapple(Pawn attacker, Pawn target, int picking, IntVec3? destination)
        {
            if (!ValidPair(attacker, target)) return;
            SpaceChecker.MakeOccupiedMask(attacker.Map, attacker.Position, out uint small);
            var report = new ActionController().GetGrappleReport(new GrappleAttemptRequest
            {
                Grappler = attacker, Target = target, OccupiedMask = small,
                DestinationCell = destination, GrappleSpotPickingBehaviour = (GrappleSpotPickingBehaviour)picking
            });
            if (report.CanGrapple && JobDriver_GrapplePawn.GiveJob(attacker, target, report.DestinationCell, true, default))
                attacker.GetMeleeData().TimeSinceGrappled = 0;
        }

        private static void Skill(Pawn attacker, Pawn target, UniqueSkillDef def)
        {
            if (!ValidPair(attacker, target)) return;
            var skill = attacker.GetComp<IdleControllerComp>()?.GetSkills()?.FirstOrDefault(s => s?.Def == def);
            if (skill != null && skill.IsEnabledForPawn(out _) && skill.CanTriggerOn(target) == null)
                skill.TryTrigger(target);
        }
    }

    [HarmonyPatch(typeof(Building_DuelSpot),nameof(Building_DuelSpot.GetGizmos))]
    internal static class DuelVisibilityClick
    {
        private static void Postfix(Building_DuelSpot __instance,ref IEnumerable<Gizmo> __result)
        {
            if(Bootstrap.Active) __result=Wrap(__instance,__result);
        }
        private static IEnumerable<Gizmo> Wrap(Building_DuelSpot spot,IEnumerable<Gizmo> gizmos)
        {
            foreach(var gizmo in gizmos)
            {
                if(gizmo is Command_Toggle toggle && toggle.defaultLabel=="AM.Gizmos.DuelSpot.ToggleVisibility".Trs())
                    toggle.toggleAction=()=>Actions.VisibilityCommand.DoSync(null,spot,!spot.IsHidden);
                yield return gizmo;
            }
        }
    }

    [HarmonyPatch]
    internal static class ExecuteClick
    {
        private static MethodBase TargetMethod() => Actions.ExecuteOriginal;
        private static bool Prefix(Pawn target, Pawn attacker, ExecutionAttemptRequest request)
        {
            if (!Bootstrap.Active || Actions.ReplayingExecution || !MP.InInterface) return true;
            if (Actions.ValidPair(attacker, target) && Actions.ConfirmFriendly(target))
                Actions.ExecuteCommand.DoSync(null, attacker, target, request.CanUseLasso, request.CanWalk, request.OnlyTheseAnimations?.ToArray());
            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) });
            var replacement = AccessTools.Method(typeof(ExecuteClick), nameof(ConfirmedKey));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original)) instruction.operand = replacement;
                yield return instruction;
            }
        }

        private static bool ConfirmedKey(KeyCode key) => Actions.ReplayingExecution || Input.GetKey(key);
    }

    [HarmonyPatch(typeof(DraftedFloatMenuOptionsUI), "GetEnabledLassoOption")]
    internal static class GrappleClick
    {
        private static void Postfix(GrappleAttemptRequest request, Pawn grappler, Pawn target, FloatMenuOption __result)
        {
            if (!Bootstrap.Active) return;
            __result.action = () => Actions.GrappleCommand.DoSync(null, grappler, target,
                (int)request.GrappleSpotPickingBehaviour, request.DestinationCell);
        }
    }

    [HarmonyPatch(typeof(ChanneledUniqueSkillInstance), nameof(ChanneledUniqueSkillInstance.TryTrigger))]
    internal static class ChannelClick
    {
        private static bool Prefix(ChanneledUniqueSkillInstance __instance, in LocalTargetInfo target, ref bool __result)
        {
            if (!Bootstrap.Active || !MP.InInterface) return true;
            Actions.SkillCommand.DoSync(null, __instance.Pawn, target.Pawn, __instance.Def);
            __result = true;
            return false;
        }
    }
}
