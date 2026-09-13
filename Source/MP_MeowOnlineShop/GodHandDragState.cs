using System;
using System.Collections.Generic;
using System.Linq;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    // Snapshot-owned gameplay state. Local input state is deliberately excluded.
    public sealed class GodHandDragState : GameComponent
    {
        public bool Friendly = true, Hostile = true, Neutral = true, ForceIngest = true;
        private float nextPolicyCheck;
        private int pendingPolicy = -1;
        public GodHandDragState(Game game)
        {
            Friendly = Patch_GodHands.GetSettingsValue("canGrabFriendly", true);
            Hostile = Patch_GodHands.GetSettingsValue("canGrabHostile", true);
            Neutral = Patch_GodHands.GetSettingsValue("canGrabNeutral", true);
            ForceIngest = Patch_GodHands.GetSettingsValue("godHandEnableForceIngest", true);
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref Friendly, "godHandFriendly", true);
            Scribe_Values.Look(ref Hostile, "godHandHostile", true);
            Scribe_Values.Look(ref Neutral, "godHandNeutral", true);
            Scribe_Values.Look(ref ForceIngest, "godHandIngest", true);
            GodHandSync.ExposeDragSessions();
        }
        public override void GameComponentUpdate()
        {
            if (!MP.IsInMultiplayer || !Patch_GodHands.TargetFound) return;
            GodHandSync.UpdateLocalDragState();
            if (!MP.IsHosting || Time.realtimeSinceStartup < nextPolicyCheck) return;
            nextPolicyCheck = Time.realtimeSinceStartup + 0.25f;
            int desired = (Patch_GodHands.GetSettingsValue("canGrabFriendly", true) ? 1 : 0)
                | (Patch_GodHands.GetSettingsValue("canGrabHostile", true) ? 2 : 0)
                | (Patch_GodHands.GetSettingsValue("canGrabNeutral", true) ? 4 : 0)
                | (Patch_GodHands.GetSettingsValue("godHandEnableForceIngest", true) ? 8 : 0);
            int current = (Friendly ? 1 : 0) | (Hostile ? 2 : 0) | (Neutral ? 4 : 0) | (ForceIngest ? 8 : 0);
            if (desired == current) { pendingPolicy = -1; return; }
            if (desired == pendingPolicy) return;
            if (GodHandSync.PolicySync?.DoSync(null, desired) == true)
                pendingPolicy = desired;
        }
    }

    internal static partial class GodHandSync
    {
        internal static ISyncMethod PolicySync, DisconnectSync, CancelSync;
        private sealed class LocalDrag
        {
            public object Controller;
            public bool Starting, Releasing, Rejected;
        }
        private static readonly Dictionary<SessionKey, LocalDrag> LocalDrags = new Dictionary<SessionKey, LocalDrag>();
        private static readonly Dictionary<SessionKey, IntVec3> LocalDragCells = new Dictionary<SessionKey, IntVec3>();
        private static List<GodHandDragSession> savedDragSessions;

        private sealed partial class GodHandDragSession
        {
            private List<int> pawnKeys, thingKeys, offsetKeys;
            private List<Pawn> pawnValues;
            private List<Thing> thingValues;
            private List<Vector3> offsetValues;
            public void ExposeData()
            {
                Scribe_Values.Look(ref MapIndex, "map");
                Scribe_Values.Look(ref PlayerId, "player");
                Scribe_Values.Look(ref StartCell, "start");
                Scribe_Values.Look(ref RangeMode, "range");
                Scribe_Values.Look(ref IsPawnGrab, "pawnGrab");
                Scribe_Collections.Look(ref Pawns, "pawns", LookMode.Value, LookMode.Reference, ref pawnKeys, ref pawnValues);
                // Grabbed items are despawned and have no other save owner.
                Scribe_Collections.Look(ref Things, "things", LookMode.Value, LookMode.Deep, ref thingKeys, ref thingValues);
                Scribe_Collections.Look(ref Offsets, "offsets", LookMode.Value, LookMode.Value, ref offsetKeys, ref offsetValues);
                Scribe_Values.Look(ref WeaponThingId, "weapon");
                Scribe_Values.Look(ref FixedWeaponPos, "weaponPos");
                Scribe_Values.Look(ref LastDragTick, "lastTick", -999);
                Scribe_Values.Look(ref LastDragCell, "lastCell");
                Scribe_Values.Look(ref WaitForRelease, "waitRelease");
                Scribe_Values.Look(ref BurstLeft, "burst");
                Scribe_Values.Look(ref NextShotTick, "nextShot");
                Scribe_Values.Look(ref CooldownUntilTick, "cooldown");
                Scribe_TargetInfo.Look(ref BurstTarget, "burstTarget");
            }
        }

        internal static void ExposeDragSessions()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                savedDragSessions = GodHandSessions.OrderBy(p => p.Key.MapIndex).ThenBy(p => p.Key.PlayerId).Select(p => p.Value).ToList();
            Scribe_Collections.Look(ref savedDragSessions, "godHandDragSessions", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                GodHandSessions.Clear();
                if (savedDragSessions != null)
                    foreach (var session in savedDragSessions)
                        GodHandSessions[new SessionKey(session.MapIndex, session.PlayerId)] = session;
                ResetLocalDragState();
            }
        }

        public static void SyncDragPolicy(int flags)
        {
            var state = Current.Game?.GetComponent<GodHandDragState>();
            if (state == null) return;
            state.Friendly = (flags & 1) != 0;
            state.Hostile = (flags & 2) != 0;
            state.Neutral = (flags & 4) != 0;
            state.ForceIngest = (flags & 8) != 0;
        }
        public static void SyncDisconnectPlayer(Map map, int player)
        {
            if (map == null) return;
            SyncGodHandForceReleaseAt(player, map.uniqueID, IntVec3.Invalid.x, IntVec3.Invalid.z);
            SyncWrenchForceRelease(player, map.uniqueID);
        }
        public static void SyncCancelDrag(Map map, int player)
        {
            if (map != null)
                SyncGodHandForceReleaseAt(player, map.uniqueID, IntVec3.Invalid.x, IntVec3.Invalid.z);
        }
        private static bool CanGrabPawn(Pawn pawn)
        {
            var state = Current.Game?.GetComponent<GodHandDragState>();
            if (state == null) return false;
            if (pawn.HostileTo(Faction.OfPlayer)) return state.Hostile;
            if (pawn.Faction == Faction.OfPlayer || pawn.Faction?.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally)
                return state.Friendly;
            return state.Neutral;
        }
        internal static bool BeginLocalDrag(object controller, int player, int map)
        {
            var key = new SessionKey(map, player);
            if (LocalDrags.ContainsKey(key)) return false;
            LocalDrags[key] = new LocalDrag { Controller = controller, Starting = true };
            return true;
        }
        internal static bool BeginLocalRelease(int player, int map)
        {
            var key = new SessionKey(map, player);
            if (!LocalDrags.TryGetValue(key, out var local))
                LocalDrags[key] = local = new LocalDrag();
            if (local.Releasing) return false;
            local.Releasing = true;
            return true;
        }
        private static void AcknowledgeLocalStart(int player, int map)
        {
            var key = new SessionKey(map, player);
            if (!LocalDrags.TryGetValue(key, out var local)) return;
            local.Starting = false;
            local.Rejected = !GodHandSessions.ContainsKey(key);
        }
        private static void CompleteLocalDrag(int player, int map)
        {
            if (player != Patch_GodHands.GetLocalPlayerId()) return;
            var key = new SessionKey(map, player);
            LocalDragCells.Remove(key);
            ClearLocalPreview();
            // Retain the original controller until its fields have been cleared.
            TrySyncLocalGodHandController(null, map, player);
            LocalDrags.Remove(key);
        }
        private static void ResetLocalDragState()
        {
            LocalDrags.Clear(); LocalDragCells.Clear();
        }
        internal static void UpdateLocalDragState()
        {
            foreach (var pair in LocalDrags.ToArray())
            {
                var key = pair.Key;
                var local = pair.Value;
                if (local.Rejected && !UnityInputCompat.GetMouseButton(0))
                { LocalDrags.Remove(key); continue; }
                if (local.Releasing || local.Rejected) continue;
                var selected = FindSelectedDesignator(Patch_GodHands.DesignatorGodHandType);
                object selectedController = selected == null ? null : HarmonyLib.AccessTools.Field(Patch_GodHands.DesignatorGodHandType, "controller")?.GetValue(selected);
                if (Find.CurrentMap?.uniqueID == key.MapIndex && selectedController == local.Controller) continue;
                // A serialized Map selects the old map's command queue even after
                // the local user has switched to a different map.
                Map map = Patch_GodHands.FindMap(key.MapIndex);
                if (map == null) { CompleteLocalDrag(key.PlayerId, key.MapIndex); continue; }
                if (CancelSync?.DoSync(null, map, key.PlayerId) == true)
                    local.Releasing = true;
            }
        }
    }
}
