using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace MP_MeowOnlineShop
{
    public sealed class WolfeinAllegianceState : GameComponent
    {
        internal static Type Type(string name) => AccessTools.TypeByName("WolfeinAllegiance." + name);
        internal static FieldInfo Field(string type, string field) => AccessTools.Field(Type(type), field);
        private List<WolfeinAllegianceRecord> records = new List<WolfeinAllegianceRecord>();
        private List<int> npcIds;
        private List<Pawn> departing;
        private bool waiting;
        private float departureCountdown = -1000f;
        private string departureCredits;
        private static readonly FieldInfo Countdown = AccessTools.Field(typeof(ShipCountdown), "timeLeft");
        private static readonly FieldInfo Credits = AccessTools.Field(typeof(ShipCountdown), "customLaunchString");
        public WolfeinAllegianceState(Game game)
        {
            if (Type("ArmyShuttleTracker") == null) return;
            if ((bool)Field("VictoryShuttleTracker", "waitingForFade").GetValue(null))
            {
                Countdown.SetValue(null, -1000f);
                Credits.SetValue(null, null);
            }
            ((IList)Field("ArmyShuttleTracker", "groups").GetValue(null)).Clear();
            ((IDictionary)Field("QuestShuttleTracker", "tracked").GetValue(null)).Clear();
            ((IDictionary)Field("PickupShuttleBoardingTracker", "pending").GetValue(null)).Clear();
            ((IList)Field("VictoryShuttleTracker", "tracked").GetValue(null)).Clear();
            ((HashSet<int>)Field("NpcShuttleTracker", "npcShuttleIds").GetValue(null)).Clear();
            Field("VictoryShuttleTracker", "pendingDestroyColonists").SetValue(null, null);
            Field("VictoryShuttleTracker", "waitingForFade").SetValue(null, false);
            // These quest parts rebuild their registries in ExposeData(PostLoadInit).
            foreach (string name in new[] { "QuestPart_VIPMoodMonitor", "QuestPart_ThingPlacedMonitor", "QuestPart_EndingReinforcement", "QuestPart_EnemyReinforcement" })
                ((IList)Field(name, "activeMonitors").GetValue(null)).Clear();
            ((IList)Field("QuestPart_TriggerRaid_NightWaiters", "waiters").GetValue(null)).Clear();
            ((IList)Field("QuestPart_RecurringRaid", "activeRaiders").GetValue(null)).Clear();
        }

        public override void ExposeData()
        {
            if (!ModsConfig.IsActive("leopoko.wolfeinallegiance")) return;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                records = new List<WolfeinAllegianceRecord>();
                foreach (object entry in (IList)Field("ArmyShuttleTracker", "groups").GetValue(null))
                    records.Add(new WolfeinAllegianceRecord("army", 0, entry));
                foreach (DictionaryEntry entry in (IDictionary)Field("QuestShuttleTracker", "tracked").GetValue(null))
                    records.Add(new WolfeinAllegianceRecord("restriction", (int)entry.Key, entry.Value));
                foreach (DictionaryEntry entry in (IDictionary)Field("PickupShuttleBoardingTracker", "pending").GetValue(null))
                    records.Add(new WolfeinAllegianceRecord("boarding", (int)entry.Key, entry.Value));
                foreach (object entry in (IList)Field("VictoryShuttleTracker", "tracked").GetValue(null))
                    records.Add(new WolfeinAllegianceRecord("victory", 0, entry));
                npcIds = ((HashSet<int>)Field("NpcShuttleTracker", "npcShuttleIds").GetValue(null)).OrderBy(i => i).ToList();
                departing = (List<Pawn>)Field("VictoryShuttleTracker", "pendingDestroyColonists").GetValue(null);
                waiting = (bool)Field("VictoryShuttleTracker", "waitingForFade").GetValue(null);
                departureCountdown = waiting ? (float)Countdown.GetValue(null) : -1000f;
                departureCredits = waiting ? (string)Credits.GetValue(null) : null;
            }
            Scribe_Collections.Look(ref records, "mpWolfeinShuttleState", LookMode.Deep);
            Scribe_Collections.Look(ref npcIds, "mpWolfeinNpcShuttles", LookMode.Value);
            Scribe_Collections.Look(ref departing, "mpWolfeinDepartingColonists", LookMode.Reference);
            Scribe_Values.Look(ref waiting, "mpWolfeinWaitingForFade");
            Scribe_Values.Look(ref departureCountdown, "mpWolfeinDepartureCountdown", -1000f);
            Scribe_Values.Look(ref departureCredits, "mpWolfeinDepartureCredits");
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            var army = (IList)Field("ArmyShuttleTracker", "groups").GetValue(null);
            var restrictions = (IDictionary)Field("QuestShuttleTracker", "tracked").GetValue(null);
            var boarding = (IDictionary)Field("PickupShuttleBoardingTracker", "pending").GetValue(null);
            var victory = (IList)Field("VictoryShuttleTracker", "tracked").GetValue(null);
            army.Clear(); restrictions.Clear(); boarding.Clear(); victory.Clear();
            if (records != null) foreach (var record in records)
            {
                object entry = record.Restore();
                switch (record.kind)
                {
                    case "army": army.Add(entry); break;
                    case "restriction": restrictions[record.id] = entry; break;
                    case "boarding": boarding[record.id] = entry; break;
                    case "victory": victory.Add(entry); break;
                }
            }
            var ids = (HashSet<int>)Field("NpcShuttleTracker", "npcShuttleIds").GetValue(null);
            ids.Clear(); if (npcIds != null) ids.UnionWith(npcIds);
            Field("VictoryShuttleTracker", "pendingDestroyColonists").SetValue(null, departing);
            Field("VictoryShuttleTracker", "waitingForFade").SetValue(null, waiting);
            if (waiting)
            {
                Countdown.SetValue(null, departureCountdown);
                Credits.SetValue(null, departureCredits);
                AccessTools.Field(typeof(ShipCountdown), "shipRoot").SetValue(null, null);
            }
        }
    }

    public sealed class WolfeinAllegianceRecord : IExposable
    {
        internal string kind;
        internal int id;
        private List<Pawn> pawns;
        private Faction faction;
        private Map map;
        private Lord lord;
        private IntVec3 rally;
        private string factory;
        private bool colonists, children, prisoners, slaves;
        private Thing shuttle;
        private Quest quest;
        private string tag;
        private int side;
        public WolfeinAllegianceRecord() { }
        private static T Get<T>(object entry, string field) => (T)AccessTools.Field(entry.GetType(), field).GetValue(entry);
        private static void Set(object entry, string field, object value) => AccessTools.Field(entry.GetType(), field).SetValue(entry, value);
        internal WolfeinAllegianceRecord(string kind, int id, object entry)
        {
            this.kind = kind; this.id = id;
            if (kind == "army" || kind == "boarding")
            {
                pawns = Get<IEnumerable<Pawn>>(entry, "pawns").ToList();
                faction = Get<Faction>(entry, "faction");
            }
            if (kind == "army")
            {
                map = Get<Map>(entry, "map"); lord = Get<Lord>(entry, "lord"); rally = Get<IntVec3>(entry, "rallyPoint");
                Delegate maker = Get<Delegate>(entry, "lordJobMaker");
                // These are the three factory bodies in the audited assembly. All capture only the group's faction.
                factory = maker == null ? null : maker.Method.Name;
                if (factory != null && !factory.Contains("ExecuteShuttleHack") && !factory.Contains("SpawnEnemyReinforcement") && !factory.Contains("ExecuteShuttleRaid"))
                    throw new InvalidOperationException("Unsupported Wolfein army factory: " + factory);
            }
            if (kind == "restriction")
            {
                colonists = Get<bool>(entry, "acceptColonists"); children = Get<bool>(entry, "acceptChildren");
                prisoners = Get<bool>(entry, "acceptColonyPrisoners"); slaves = Get<bool>(entry, "allowSlaves");
            }
            if (kind == "victory")
            {
                shuttle = Get<Thing>(entry, "shuttle"); quest = Get<Quest>(entry, "quest"); tag = Get<string>(entry, "questTag");
                side = Convert.ToInt32(Get<object>(entry, "endingSide"));
            }
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref id, "id");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Reference);
            Scribe_References.Look(ref faction, "faction"); Scribe_References.Look(ref map, "map"); Scribe_References.Look(ref lord, "lord");
            Scribe_Values.Look(ref rally, "rally"); Scribe_Values.Look(ref factory, "factory");
            Scribe_Values.Look(ref colonists, "colonists"); Scribe_Values.Look(ref children, "children");
            Scribe_Values.Look(ref prisoners, "prisoners"); Scribe_Values.Look(ref slaves, "slaves");
            Scribe_References.Look(ref shuttle, "shuttle"); Scribe_References.Look(ref quest, "quest");
            Scribe_Values.Look(ref tag, "tag"); Scribe_Values.Look(ref side, "side");
        }
        internal object Restore()
        {
            string type = kind == "army" ? "ArmyShuttleTracker+TrackedGroup" : kind == "boarding" ? "PickupShuttleBoardingTracker+PendingBoarding" :
                kind == "restriction" ? "ShuttleRestrictions" : "VictoryShuttleTracker+VictoryShuttleInfo";
            object entry = Activator.CreateInstance(WolfeinAllegianceState.Type(type), true);
            if (kind == "army" || kind == "boarding")
            {
                Set(entry, "pawns", kind == "army" ? (object)new HashSet<Pawn>(pawns ?? new List<Pawn>()) : pawns ?? new List<Pawn>());
                Set(entry, "faction", faction);
            }
            if (kind == "army")
            {
                Set(entry, "map", map); Set(entry, "lord", lord); Set(entry, "rallyPoint", rally);
                if (factory != null)
                {
                    Func<IntVec3, LordJob> maker = factory.Contains("ExecuteShuttleHack") ? (Func<IntVec3, LordJob>)ExecuteShuttleHack :
                        factory.Contains("SpawnEnemyReinforcement") ? SpawnEnemyReinforcement : ExecuteShuttleRaid;
                    Set(entry, "lordJobMaker", maker);
                }
            }
            if (kind == "restriction")
            {
                Set(entry, "acceptColonists", colonists); Set(entry, "acceptChildren", children);
                Set(entry, "acceptColonyPrisoners", prisoners); Set(entry, "allowSlaves", slaves);
            }
            if (kind == "victory")
            {
                Set(entry, "shuttle", shuttle); Set(entry, "quest", quest); Set(entry, "questTag", tag);
                Set(entry, "endingSide", Enum.ToObject(WolfeinAllegianceState.Type("AllegianceSide"), side));
            }
            return entry;
        }
        private LordJob ExecuteShuttleHack(IntVec3 cell) => new LordJob_AssaultColony(faction, true, true, false, false, true, false, false);
        private LordJob SpawnEnemyReinforcement(IntVec3 cell) => new LordJob_AssaultColony(faction, false, false, false, false, true, false, false);
        private LordJob ExecuteShuttleRaid(IntVec3 cell) => new LordJob_AssaultColony(faction, false, true, false, false, true, false, false);
    }
}
