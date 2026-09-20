using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AM;
using AM.Buildings;
using AM.ColumnWorkers;
using AM.PawnData;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        internal const string Id = "meow.multiplayer.meleeanimation";
        internal static bool Active => MP.IsInMultiplayer;

        static Bootstrap()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("melee")) return;
            if (!MP.enabled) return;
            try
            {
                MP.RegisterSyncWorker<PawnMeleeData>(SyncMeleeData);
                MP.RegisterSyncMethod(typeof(Building_DuelSpot), "TryStartDuel").CancelIfAnyArgNull();
                MP.RegisterSyncField(typeof(PawnMeleeData), nameof(PawnMeleeData.AutoExecute));
                MP.RegisterSyncField(typeof(PawnMeleeData), nameof(PawnMeleeData.AutoGrapple));
                Actions.Register();
                var harmony=new Harmony(Id);
                VisibilityThread.RepairPathScope(harmony);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[MeleeAnimationMP] 1.0.0 synchronization and deterministic simulation patches registered.");
            }
            catch (Exception e)
            {
                Log.Error("[MeleeAnimationMP] REQUIRED_TARGET_FAILURE " + e);
                throw;
            }
        }

        private static void SyncMeleeData(SyncWorker sync, ref PawnMeleeData data)
        {
            Pawn pawn = data?.Pawn;
            sync.Bind(ref pawn);
            if (!sync.isWriting) data = GameComp.Current.GetOrCreateData(pawn);
        }
    }

    [HarmonyPatch(typeof(PawnColumnWorker_Base), nameof(PawnColumnWorker_Base.DoCell))]
    internal static class AutoOptions
    {
        private static void Prefix(Pawn pawn, out bool __state)
        {
            __state = Bootstrap.Active && pawn.RaceProps.Humanlike && pawn.RaceProps.ToolUser;
            if (!__state) return;
            var data = GameComp.Current.GetOrCreateData(pawn);
            LocalDataPreview.ColumnData = data;
            MP.WatchBegin();
            MP.Watch(typeof(PawnMeleeData), nameof(PawnMeleeData.AutoExecute), data);
            MP.Watch(typeof(PawnMeleeData), nameof(PawnMeleeData.AutoGrapple), data);
        }

        private static void Finalizer(bool __state)
        {
            if (!__state) return;
            try { MP.WatchEnd(); }
            finally { LocalDataPreview.ColumnData = null; }
        }
    }
}
