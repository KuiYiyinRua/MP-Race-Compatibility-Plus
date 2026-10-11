using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    public sealed class MonolynView : IExposable
    {
        public int Player, Remaining;
        public CellRect Rect;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Player, "player");
            Scribe_Values.Look(ref Remaining, "remaining");
            Scribe_Values.Look(ref Rect, "rect");
        }
    }

    public sealed class MonolynSightMap : MapComponent
    {
        public int Clock;
        public List<MonolynView> Views = new List<MonolynView>();
        public MonolynSightMap(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            if (!MP.IsInMultiplayer || MonolynSight.UpdateView == null) return;
            Clock++;
            foreach (var view in Views) view.Remaining--;
            Views.RemoveAll(v => v.Remaining <= 0);
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref Clock, "meowMonolynViewClock");
            Scribe_Collections.Look(ref Views, "meowMonolynViews", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Views == null) Views = new List<MonolynView>();
        }
        public bool Observed(IntVec3 cell) => Views.Any(v => v.Remaining > 0 && v.Rect.Contains(cell));
    }

    internal static class MonolynSight
    {
        internal static ISyncMethod UpdateView;
        internal static PropertyInfo IsReplay, Simulating;
        static Type sightType;
        static FieldInfo sub;
        static PropertyInfo activity;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.CompSightSign") ?? throw new TypeLoadException("ASEL.CompSightSign");
            var visible = AccessTools.DeclaredMethod(type, "IsVisibleToCamera", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "IsVisibleToCamera");
            var pawnVisible = AccessTools.DeclaredMethod(type, "IsVisibleToCameraP", new[] { typeof(Pawn) }) ?? throw new MissingMethodException(type.FullName, "IsVisibleToCameraP");
            sightType = type;
            sub = AccessTools.Field(type, "Sub") ?? throw new MissingFieldException(type.FullName, "Sub");
            if (sub.FieldType != typeof(bool)) throw new InvalidOperationException("Sight Sub field type changed");
            activity = AccessTools.Property(type, "Activity") ?? throw new MissingMemberException(type.FullName, "Activity");
            var worker = AccessTools.TypeByName("ASEL.SignActivityWorker") ?? throw new TypeLoadException("ASEL.SignActivityWorker");
            var rate = AccessTools.DeclaredMethod(worker, "GetChangeRatePerDay", new[] { typeof(ThingWithComps) }) ?? throw new MissingMethodException(worker.FullName, "GetChangeRatePerDay");
            harmony.Patch(rate, prefix: new HarmonyMethod(typeof(MonolynSight), nameof(ActivityRate)));
            IsReplay = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "IsReplay") ?? throw new MissingMemberException("MP IsReplay");
            Simulating = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.TickPatch"), "Simulating") ?? throw new MissingMemberException("MP Simulating");
            UpdateView = MP.RegisterSyncMethod(typeof(MonolynSight), nameof(SetView));
            harmony.Patch(visible, prefix: new HarmonyMethod(typeof(MonolynSight), nameof(Visible)));
            harmony.Patch(pawnVisible, prefix: new HarmonyMethod(typeof(MonolynSight), nameof(PawnVisible)));
            Log.Message("[MonolynSight] resolved=shared observer rectangles, sight activation, victim visibility; missing=0");
        }

        static bool ActivityRate(ThingWithComps __0, ref float __result)
        {
            if (!MP.IsInMultiplayer) return true;
            var state = __0?.Map?.GetComponent<MonolynSightMap>();
            __result = -5f;
            if (state == null || !state.Views.Any(v => v.Remaining > 0)) return false;
            if (state.Observed(__0.Position)) { __result = 35f; return false; }
            var comp = __0.AllComps.First(c => sightType.IsInstanceOfType(c));
            bool active = ((RimWorld.CompActivity)activity.GetValue(comp)).IsActive;
            __result = !active ? -25f : !(bool)sub.GetValue(comp) ? -5f : 0f;
            return false;
        }

        // This changes only saved observer records, without ticking a map or using
        // its random stream. Keep it in the global command queue: MP reconstructs
        // map queues separately on joining and can reorder same-tick map commands.
        static void SetView(int mapId, int player, int minX, int minZ, int maxX, int maxZ)
        {
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            if (map == null || player < 0 || !Find.Maps.Contains(map)) return;
            var state = map.GetComponent<MonolynSightMap>();
            state.Views.RemoveAll(v => v.Player == player);
            if (maxX < minX || maxZ < minZ) return;
            var rect = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
            rect.ClipInsideMap(map);
            if (!rect.IsEmpty) state.Views.Add(new MonolynView { Player = player, Remaining = 180, Rect = rect });
        }

        static bool Visible(ThingComp __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            var parent = __instance.parent;
            __result = parent?.Map != null && parent.Map.GetComponent<MonolynSightMap>().Observed(parent.Position);
            return false;
        }

        static bool PawnVisible(ThingComp __instance, Pawn p, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = p?.Map != null && p.Map == __instance.parent.Map && p.Faction != __instance.parent.Faction &&
                p.Map.GetComponent<MonolynSightMap>().Observed(p.Position);
            return false;
        }
    }

    public sealed class MonolynSightInput : GameComponent
    {
        sealed class LastView { public int Clock; public CellRect Rect; }
        readonly Dictionary<Map, LastView> sent = new Dictionary<Map, LastView>();
        int previousPlayer = -1;
        float nextSend;
        public MonolynSightInput(Game game) { }
        public override void GameComponentUpdate()
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MonolynSight.UpdateView == null ||
                (bool)MonolynSight.IsReplay.GetValue(null) || (bool)MonolynSight.Simulating.GetValue(null)) return;
            if (UnityEngine.Time.realtimeSinceStartup < nextSend) return;
            nextSend = UnityEngine.Time.realtimeSinceStartup + 0.1f;
            var matches = MP.GetPlayers().Where(p => p.Username == MP.PlayerName).ToList();
            if (matches.Count != 1) return;
            int player = matches[0].Id;
            if (previousPlayer != player) { sent.Clear(); previousPlayer = player; }
            foreach (var stale in sent.Keys.Where(m => !Find.Maps.Contains(m)).ToList()) sent.Remove(stale);
            foreach (var map in Find.Maps)
            {
                var state = map.GetComponent<MonolynSightMap>();
                var rect = map == Find.CurrentMap ? Find.CameraDriver.CurrentViewRect : CellRect.Empty;
                rect.ClipInsideMap(map);
                bool hadSent = sent.TryGetValue(map, out var prior);
                bool changed = hadSent ? !rect.Equals(prior.Rect) : !rect.IsEmpty || state.Views.Any(v => v.Player == player);
                if (!changed && (rect.IsEmpty || hadSent && unchecked(state.Clock - prior.Clock) < 60)) continue;
                if (MonolynSight.UpdateView.DoSync(null, map.uniqueID, player, rect.minX, rect.minZ, rect.maxX, rect.maxZ))
                    sent[map] = new LastView { Clock = state.Clock, Rect = rect };
            }
        }
    }
}
