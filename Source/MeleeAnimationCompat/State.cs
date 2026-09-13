using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using AM;
using AM.AutoDuel;
using AM.Buildings;
using AM.Idle;
using AM.PawnData;
using AM.UniqueSkills;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    [HarmonyPatch(typeof(GameComp), nameof(GameComp.GetOrCreateData))]
    internal static class LocalDataPreview
    {
        [ThreadStatic] internal static PawnMeleeData ColumnData;
        private static bool Prefix(Pawn pawn, Dictionary<Pawn, PawnMeleeData> ___pawnMeleeData, ref PawnMeleeData __result)
        {
            if (!Bootstrap.Active || !MP.InInterface || pawn == null || pawn.Destroyed
                || ___pawnMeleeData.ContainsKey(pawn)) return true;
            // Opening a table must not start a cooldown clock on just one peer.
            __result = ColumnData?.Pawn == pawn ? ColumnData : new PawnMeleeData { Pawn = pawn };
            return false;
        }
    }

    [HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.MapComponentTick))]
    internal static class MapCooldowns
    {
        internal static readonly AccessTools.FieldRef<GameComp, List<PawnMeleeData>> AllData =
            AccessTools.FieldRefAccess<GameComp, List<PawnMeleeData>>("allMeleeData");

        private static void Prefix(AnimationManager __instance)
        {
            if (!Bootstrap.Active) return;
            foreach (var pawn in __instance.map.mapPawns.AllPawnsSpawned.OrderBy(p => p.thingIDNumber))
                GameComp.Current.GetOrCreateData(pawn);
            foreach (var data in AllData(GameComp.Current))
            {
                if (data.Pawn?.MapHeld != __instance.map) continue;
                data.TimeSinceExecuted += 1f / 60f;
                data.TimeSinceGrappled += 1f / 60f;
                data.TimeSinceFriendlyDueled += 1f / 60f;
            }
        }
    }

    [HarmonyPatch(typeof(GameComp), nameof(GameComp.GameComponentTick))]
    internal static class WorldCooldowns
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(GameComp), "allMeleeData");
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(WorldCooldowns), nameof(WorldData));
                }
                yield return instruction;
            }
        }

        private static List<PawnMeleeData> WorldData(GameComp comp) => !Bootstrap.Active
            ? MapCooldowns.AllData(comp)
            : MapCooldowns.AllData(comp).Where(d => d.Pawn?.MapHeld == null).ToList();
    }

    [HarmonyPatch(typeof(IdleControllerComp), nameof(IdleControllerComp.CompTick))]
    internal static class InitializeSkills
    {
        private static void Prefix(IdleControllerComp __instance, UniqueSkillInstance[] ___skills)
        {
            // GetSkills leaves skills null when disabled/ineligible. Avoid its
            // repeated eligibility path while disabled, and stop once populated.
            // Keep first eligible-tick initialization (including recruitment).
            if (___skills != null || !Bootstrap.Active
                || !MeleeSessionState.CurrentRules().EnableUniqueSkills) return;
            __instance.GetSkills();
        }
    }

    [HarmonyPatch(typeof(AnimRenderer), nameof(AnimRenderer.Register))]
    internal static class RestoreChannelCompletion
    {
        private static readonly FieldInfo Target = AccessTools.Field(typeof(ChanneledUniqueSkillInstance), "CurrentTarget");
        private static readonly MethodInfo End = AccessTools.Method(typeof(ChanneledUniqueSkillInstance), "OnEnd");

        private static void Postfix(AnimRenderer __instance, bool __result)
        {
            if (!Bootstrap.Active || !__result) return;
            foreach (var pawn in __instance.NonAnimatedPawns)
            {
                var skills = pawn?.GetComp<IdleControllerComp>()?.GetSkills();
                if (skills == null) continue;
                foreach (var skill in skills.OfType<ChanneledUniqueSkillInstance>())
                {
                    var target = Target.GetValue(skill) as Pawn;
                    if (target == null || !__instance.Pawns.Contains(target) || skill.Def.animation != __instance.Def) continue;
                    var callback = (Action<AnimRenderer>)Delegate.CreateDelegate(typeof(Action<AnimRenderer>), skill, End);
                    __instance.OnEndAction -= callback;
                    __instance.OnEndAction += callback;
                }
            }
        }
    }

    [HarmonyPatch(typeof(AutoFriendlyDuelMapComp), nameof(AutoFriendlyDuelMapComp.TryGetRandomDuelPartner))]
    internal static class OrderedDuelPartner
    {
        private static bool Prefix(AutoFriendlyDuelMapComp __instance, Pawn except, ref Pawn __result)
        {
            if (!Bootstrap.Active) return true;
            // Derive the candidate set from live simulation, avoiding the unsaved
            // one-second cache during a cold join.
            __result = __instance.map.mapPawns.AllPawnsSpawned
                .Where(p => p != except && (p.IsColonist || p.IsSlaveOfColony) && AutoFriendlyDuelMapComp.CanPawnDuel(p))
                .OrderBy(p => p.thingIDNumber).RandomElementWithFallback();
            return false;
        }
    }

    [HarmonyPatch(typeof(AutoFriendlyDuelMapComp), nameof(AutoFriendlyDuelMapComp.CanPawnMaybeDuel))]
    internal static class LiveDuelEligibility
    {
        private static bool Prefix(Pawn pawn, ref bool __result)
        {
            if (!Bootstrap.Active) return true;
            __result = pawn != null && (pawn.IsColonist || pawn.IsSlaveOfColony) && AutoFriendlyDuelMapComp.CanPawnDuel(pawn);
            return false;
        }
    }

    [HarmonyPatch(typeof(AutoFriendlyDuelMapComp), nameof(AutoFriendlyDuelMapComp.GetActiveDuelSpots))]
    internal static class OrderedActiveDuels
    {
        private static void Postfix(ref IEnumerable<ActiveDuelSpot> __result)
        {
            if (Bootstrap.Active) __result = __result.OrderBy(s => s.Spot.thingIDNumber);
        }
    }

    [HarmonyPatch(typeof(AutoFriendlyDuelMapComp), nameof(AutoFriendlyDuelMapComp.TryGetBestDuelSpotFor))]
    internal static class OrderedDuelSpot
    {
        private static bool Prefix(AutoFriendlyDuelMapComp __instance, Pawn a, Pawn b, ref Building_DuelSpot __result)
        {
            if (!Bootstrap.Active) return true;
            __result = __instance.DuelSpots.Where(s => !s.IsForbidden && !s.IsForbidden(a) && !s.IsForbidden(b) && !s.IsInUse(out _, out _))
                .OrderBy(s => s.Position.DistanceToSquared(a.Position) + s.Position.DistanceToSquared(b.Position))
                .ThenBy(s => s.thingIDNumber).FirstOrDefault();
            return false;
        }
    }
}
