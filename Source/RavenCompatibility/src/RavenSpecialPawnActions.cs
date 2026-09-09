using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Patches;
using RimWorld;
using Verse;
using Runtime = Multiplayer.Client.Multiplayer;

namespace MP_MeowOnlineShop
{
    internal static class RavenSpecialPawnActions
    {
        private static Type component, extension;
        private static MethodInfo give, reclaim, unlock;
        private static ISyncMethod summonCommand, reclaimCommand, unlockCommand;
        internal static void Apply(Harmony harmony)
        {
            component = AccessTools.TypeByName("RavenRace.Features.CustomPawn.Ui.RavrGameComp.GameComponent_SpecialPawnUnlocks");
            extension = AccessTools.TypeByName("RavenRace.Features.CustomPawn.Ui.RaveExtension.RaveCustomPawnUiData");
            var worker = AccessTools.TypeByName("RavenRace.Features.CustomPawn.Ui.SpecialPawnWorker.SpecialPawnWorker_ConsumeItem");
            var dialog = AccessTools.TypeByName("RavenRace.Features.CustomPawn.Ui.UiWindows.Dialog_SpecialPawnDetail");
            give = AccessTools.DeclaredMethod(component, "GivePawnToPlayer");
            reclaim = AccessTools.DeclaredMethod(component, "ReclaimPawn");
            unlock = AccessTools.DeclaredMethod(worker, "TryUnlockAndConsume");
            if (extension == null || give == null || reclaim == null || unlock == null) throw new MissingMemberException("Raven special pawn actions");
            summonCommand = MP.RegisterSyncMethod(typeof(RavenSpecialPawnActions), nameof(Summon)).SetContext(SyncContext.MapSelected);
            reclaimCommand = MP.RegisterSyncMethod(typeof(RavenSpecialPawnActions), nameof(Reclaim)).SetContext(SyncContext.MapSelected);
            unlockCommand = MP.RegisterSyncMethod(typeof(RavenSpecialPawnActions), nameof(Unlock)).SetContext(SyncContext.MapSelected);
            harmony.Patch(AccessTools.DeclaredMethod(dialog, "StartSummonTargeting"),
                prefix: new HarmonyMethod(typeof(RavenSpecialPawnActions), nameof(BeforeTargeting)));
            harmony.Patch(reclaim, prefix: new HarmonyMethod(typeof(RavenSpecialPawnActions), nameof(BeforeReclaim)));
            harmony.Patch(unlock, prefix: new HarmonyMethod(typeof(RavenSpecialPawnActions), nameof(BeforeUnlock)));
        }
        private static object Component => AccessTools.Property(component, "Instance").GetValue(null);
        private static object Extension(PawnKindDef kind) => kind?.modExtensions?.FirstOrDefault(e => e.GetType() == extension);
        private static Pawn StoredPawn(object owner, PawnKindDef kind) =>
            ((IDictionary)AccessTools.Field(component, "playerPawns").GetValue(owner))[kind] as Pawn;
        private static bool IsUnlocked(object owner, PawnKindDef kind) =>
            owner != null && (bool)AccessTools.Method(component, "IsUnlocked").Invoke(owner, new object[] { kind });
        private static bool BeforeTargeting(object __instance)
        {
            if (!MP.InInterface) return true;
            var map = Find.CurrentMap;
            var kind = (PawnKindDef)AccessTools.Field(__instance.GetType(), "kindDef").GetValue(__instance);
            if (map == null || !IsUnlocked(Component, kind)) return false;
            var parameters = new TargetingParameters
            {
                canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false,
                validator = target => target.Cell.IsValid && target.Cell.InBounds(map) && target.Cell.Standable(map)
            };
            Find.Targeter.BeginTargeting(parameters, target => summonCommand.DoSync(null, kind, target.Cell, map));
            return false;
        }
        private static bool BeforeReclaim(PawnKindDef __0, ref bool __result)
        {
            if (!MP.InInterface) return true;
            reclaimCommand.DoSync(null, __0); __result = false; return false;
        }
        private static bool BeforeUnlock(object __instance)
        {
            if (!MP.InInterface) return true;
            unlockCommand.DoSync(null, (PawnKindDef)AccessTools.Field(__instance.GetType(), "def").GetValue(__instance));
            return false;
        }
        private static void Unlock(PawnKindDef kind)
        {
            var data = Extension(kind);
            var worker = data == null ? null : AccessTools.Property(extension, "Worker").GetValue(data);
            if (worker != null && unlock.DeclaringType.IsInstanceOfType(worker)) unlock.Invoke(worker, null);
        }
        private static void Reclaim(PawnKindDef kind)
        {
            var owner = Component;
            var data = Extension(kind);
            if (data == null || !IsUnlocked(owner, kind)) return;
            var pawn = StoredPawn(owner, kind);
            var oldMap = pawn?.Map;
            if (oldMap == null) return;
            if ((bool)reclaim.Invoke(owner, new object[] { kind, data })) TimestampFixer.FixPawn(pawn, oldMap, null);
        }
        private static void Summon(PawnKindDef kind, IntVec3 cell, Map map)
        {
            var owner = Component;
            var data = Extension(kind);
            if (data == null || !IsUnlocked(owner, kind) || map == null || !cell.InBounds(map) || !cell.Standable(map)) return;
            var existing = StoredPawn(owner, kind);
            if (existing != null && (existing.Spawned || existing.Dead)) return;
            // Stored roster pawns use a GameComponent holder, bypassing MP's normal
            // WorldPawns transfer hooks. Avoid a second offset if native SpawnSetup
            // will already fix a just-removed legacy world pawn.
            var mp = existing?.AllComps.FirstOrDefault(c => AccessTools.Field(c.GetType(), "worldPawnRemoveTick") != null);
            bool nativeFix = mp != null && (int)AccessTools.Field(mp.GetType(), "worldPawnRemoveTick").GetValue(mp) == Runtime.AsyncWorldTime.worldTicks;
            var pawn = (Pawn)give.Invoke(owner, new object[] { kind, cell, map, data });
            if (pawn != null && existing != null && !nativeFix) TimestampFixer.FixPawn(pawn, null, map);
            if (!MP.IsExecutingSyncCommandIssuedBySelf) return;
            if (pawn != null) Messages.Message("RavenRace_CustomPawn_Dialog_SpecialPawnDetail_Message_2".Translate(pawn.LabelShortCap), MessageTypeDefOf.PositiveEvent, false);
            else Messages.Message("RavenRace_CustomPawn_Dialog_SpecialPawnDetail_Message_3".Translate(), MessageTypeDefOf.RejectInput, false);
        }
    }
}
