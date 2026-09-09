using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_WolfeinBlackScienceMp
    {
        private const string Ns = "BlackScience.";
        private static readonly ConditionalWeakTable<Building, Selection> Selections = new ConditionalWeakTable<Building, Selection>();
        private sealed class Selection { internal int tile = -1; }
        private static FieldInfo tileField, cellField;
        private static MethodInfo executeLaunch;
        private static ISyncMethod launch;

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || !ModsConfig.IsActive("wolfeinexpand.blackscience")) return;
            Register("CompBlackSciencePrisonerPod", "TryOrderPrisonerEnterPod", typeof(Pawn));
            Register("CompBlackSciencePrisonerPod", "CancelLoad", typeof(Pawn));
            Register("CompBlackSciencePrisonerPod", "<CompGetGizmosExtra>b__25_3");
            Register("CompBlackSciencePrisonerPod", "<CompGetGizmosExtra>b__25_4");
            Register("CompFinalJudgementAttackMode", "SetMode", typeof(int));
            Register("CompTurretGun", "<CompGetGizmosExtra>b__45_0");
            Register("CompTurretGun", "<CompGetGizmosExtra>b__45_1", typeof(LocalTargetInfo));
            Register("Building_TianMenShenGong", "EjectLoadedPawns");
            foreach (string name in new[] { "MultiMeleeComboComponent", "TachiExecutionComponent" })
                harmony.Patch(Required(name, "GameComponentTick"), prefix: new HarmonyMethod(typeof(WolfeinCombatQueueState), nameof(WolfeinCombatQueueState.WorldTickPrefix)));
            harmony.Patch(Required("CompSuperShield", "Draw"),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinBlackScienceMp), nameof(DrawPrefix)),
                finalizer: new HarmonyMethod(typeof(Patch_WolfeinBlackScienceMp), nameof(DrawFinalizer)));
            Type turret = AccessTools.TypeByName(Ns + "Building_TianMenShenGong");
            tileField = AccessTools.Field(turret, "pendingWorldTargetTile");
            cellField = AccessTools.Field(turret, "pendingMapTargetCell");
            executeLaunch = Required("Building_TianMenShenGong", "ExecuteLaunch");
            launch = MP.RegisterSyncMethod(typeof(Patch_WolfeinBlackScienceMp), nameof(Launch));
            harmony.Patch(Required("Building_TianMenShenGong", "<StartMapTargeting>b__61_0", typeof(LocalTargetInfo)),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinBlackScienceMp), nameof(MapTargetPrefix)));
            harmony.Patch(Required("Building_TianMenShenGong", "<StartWorldTargeting>b__62_0", typeof(GlobalTargetInfo)),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinBlackScienceMp), nameof(WorldSelectionPrefix)),
                finalizer: new HarmonyMethod(typeof(Patch_WolfeinBlackScienceMp), nameof(WorldSelectionFinalizer)));
            harmony.Patch(Required("Building_TianMenShenGong", "<StartWorldTargeting>b__62_4", typeof(LocalTargetInfo)),
                prefix: new HarmonyMethod(typeof(Patch_WolfeinBlackScienceMp), nameof(WorldCellPrefix)));
            Log.Message("[MP-MeowOnlineShop] Wolfein Black Science actions and map/world launch context installed.");
        }

        private static MethodInfo Required(string type, string method, params Type[] args)
        {
            Type resolved = AccessTools.TypeByName(Ns + type);
            return (resolved == null ? null : AccessTools.DeclaredMethod(resolved, method, args))
                ?? throw new MissingMethodException(Ns + type, method);
        }

        private static void Register(string type, string method, params Type[] args)
        {
            MP.RegisterSyncMethod(Required(type, method, args), null);
        }

        private static bool MapTargetPrefix(Building __instance, LocalTargetInfo __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !MP.InInterface) return true;
            if (__0.IsValid) launch.DoSync(null, __instance, -1, __0.Cell);
            return false;
        }

        private static bool WorldCellPrefix(Building __instance, LocalTargetInfo __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !MP.InInterface) return true;
            if (__0.IsValid && Selections.TryGetValue(__instance, out Selection selection) && selection.tile >= 0)
                launch.DoSync(null, __instance, selection.tile, __0.Cell);
            return false;
        }

        private sealed class PreviousSelection
        {
            internal int tile;
            internal IntVec3 cell;
        }

        private static void WorldSelectionPrefix(Building __instance, out PreviousSelection __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand || !MP.InInterface) return;
            __state = new PreviousSelection { tile = (int)tileField.GetValue(__instance), cell = (IntVec3)cellField.GetValue(__instance) };
        }

        private static Exception WorldSelectionFinalizer(Building __instance, PreviousSelection __state, Exception __exception)
        {
            if (__state != null)
            {
                // The first world click opens a local map targeter, but its original callback also writes saved fields.
                if (__exception == null) Selections.GetOrCreateValue(__instance).tile = (int)tileField.GetValue(__instance);
                tileField.SetValue(__instance, __state.tile);
                cellField.SetValue(__instance, __state.cell);
            }
            return __exception;
        }

        public static void Launch(Building turret, int tile, IntVec3 cell)
        {
            if (turret == null || !turret.Spawned || !cell.IsValid) return;
            if (AccessTools.Field(turret.GetType(), "launchState").GetValue(turret).ToString() != "AwaitingTarget") return;
            Map targetMap = tile < 0 ? turret.Map : Find.WorldObjects.MapParentAt(tile)?.Map;
            if (targetMap == null || !cell.InBounds(targetMap)) return;
            tileField.SetValue(turret, tile);
            cellField.SetValue(turret, cell);
            executeLaunch.Invoke(turret, null);
        }

        private static void DrawPrefix(out bool __state)
        {
            __state = MP.IsInMultiplayer;
            if (__state) Rand.PushState();
        }

        private static Exception DrawFinalizer(bool __state, Exception __exception)
        {
            if (__state) Rand.PopState();
            return __exception;
        }
    }
}
