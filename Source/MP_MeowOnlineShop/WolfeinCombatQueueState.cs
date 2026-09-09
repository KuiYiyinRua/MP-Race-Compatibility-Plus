using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    // The source mod keeps delayed damage in static lists without ExposeData.
    // Store the actual pending action data in the snapshot, including its verb reference.
    public sealed class WolfeinCombatQueueState : GameComponent
    {
        [ThreadStatic] internal static bool tickingMap;
        private List<WolfeinCombatQueueRecord> combos = new List<WolfeinCombatQueueRecord>();
        private List<WolfeinCombatQueueRecord> executions = new List<WolfeinCombatQueueRecord>();

        public WolfeinCombatQueueState(Game game)
        {
            Queue("MultiMeleeComboComponent")?.Clear();
            Queue("TachiExecutionComponent")?.Clear();
        }

        internal static IList Queue(string type)
        {
            Type resolved = AccessTools.TypeByName("BlackScience." + type);
            return resolved == null ? null : AccessTools.Field(resolved, "queue")?.GetValue(null) as IList;
        }

        internal static bool WorldTickPrefix() => !MP.IsInMultiplayer || tickingMap;

        public override void ExposeData()
        {
            base.ExposeData();
            if (!ModsConfig.IsActive("wolfeinexpand.blackscience")) return;
            ExposeQueue("MultiMeleeComboComponent", "mpWolfeinCombos", ref combos);
            ExposeQueue("TachiExecutionComponent", "mpWolfeinExecutions", ref executions);
        }

        private static void ExposeQueue(string type, string label, ref List<WolfeinCombatQueueRecord> records)
        {
            IList queue = Queue(type);
            if (queue == null) return;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                records = new List<WolfeinCombatQueueRecord>();
                foreach (object item in queue) records.Add(new WolfeinCombatQueueRecord(item));
            }
            Scribe_Collections.Look(ref records, label, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                queue.Clear();
                if (records != null)
                    foreach (var record in records)
                        if (record?.entry != null) queue.Add(record.entry);
            }
        }
    }

    // Enqueue uses the caster map's TicksGame. Execute on that same map's clock and Rand context.
    public sealed class WolfeinCombatMapTick : MapComponent
    {
        public WolfeinCombatMapTick(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            if (!MP.IsInMultiplayer || !ModsConfig.IsActive("wolfeinexpand.blackscience")) return;
            TickQueue("MultiMeleeComboComponent");
            TickQueue("TachiExecutionComponent");
        }
        private void TickQueue(string name)
        {
            IList queue = WolfeinCombatQueueState.Queue(name);
            if (queue == null || queue.Count == 0) return;
            var original = new List<object>();
            var selected = new List<object>();
            foreach (object entry in queue)
            {
                original.Add(entry);
                if ((Map)AccessTools.Field(entry.GetType(), "map").GetValue(entry) == map) selected.Add(entry);
            }
            if (selected.Count == 0) return;
            GameComponent component = Current.Game.components.Find(c => c.GetType().FullName == "BlackScience." + name);
            if (component == null) return;
            queue.Clear(); foreach (object entry in selected) queue.Add(entry);
            WolfeinCombatQueueState.tickingMap = true;
            try { component.GameComponentTick(); }
            finally
            {
                WolfeinCombatQueueState.tickingMap = false;
                var survivors = new HashSet<object>();
                var added = new List<object>();
                foreach (object entry in queue)
                {
                    survivors.Add(entry);
                    if (!original.Contains(entry)) added.Add(entry);
                }
                queue.Clear();
                foreach (object entry in original)
                    if (!selected.Contains(entry) || survivors.Contains(entry)) queue.Add(entry);
                foreach (object entry in added) queue.Add(entry);
            }
        }
    }

    public sealed class WolfeinCombatQueueRecord : IExposable
    {
        internal object entry;
        private bool execution;
        public WolfeinCombatQueueRecord() { }
        internal WolfeinCombatQueueRecord(object item) { entry = item; execution = item.GetType().Name == "PendingTachiExecution"; }
        private T Get<T>(string field) => (T)AccessTools.Field(entry.GetType(), field).GetValue(entry);
        private void Set(string field, object value) => AccessTools.Field(entry.GetType(), field).SetValue(entry, value);
        private void Value<T>(string name, T defaultValue = default(T))
        {
            T value = Get<T>(name);
            Scribe_Values.Look(ref value, name, defaultValue);
            Set(name, value);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref execution, "execution");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                entry = Activator.CreateInstance(AccessTools.TypeByName("BlackScience." + (execution ? "PendingTachiExecution" : "PendingComboHit")));
            Pawn caster = Get<Pawn>("caster");
            Map map = Get<Map>("map");
            Scribe_References.Look(ref caster, "caster");
            Scribe_References.Look(ref map, "map");
            Set("caster", caster); Set("map", map);
            if (execution)
            {
                int phase = Convert.ToInt32(AccessTools.Field(entry.GetType(), "phase").GetValue(entry));
                Scribe_Values.Look(ref phase, "phase");
                Set("phase", Enum.ToObject(AccessTools.Field(entry.GetType(), "phase").FieldType, phase));
                Value<int>("phaseEndTick"); Value<int>("executionEndTick"); Value<int>("ultimateEndTick");
                Value<float>("damageAmount"); Value<int>("damageHits"); Value<float>("executionAnimSpeedMultiplier", 1f);
                List<Pawn> targets = Get<List<Pawn>>("targets");
                Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
                Set("targets", targets);
                FleckDef fleck = Get<FleckDef>("executionFleck");
                HediffDef hediff = Get<HediffDef>("ultimateHediff");
                Scribe_Defs.Look(ref fleck, "executionFleck"); Scribe_Defs.Look(ref hediff, "ultimateHediff");
                Set("executionFleck", fleck); Set("ultimateHediff", hediff);
            }
            else
            {
                Value<int>("executeTick"); Value<float>("damageMultiplier");
                Value<bool>("requireSameTarget"); Value<bool>("independentHit"); Value<bool>("independentDodge");
                LocalTargetInfo target = Get<LocalTargetInfo>("target");
                Scribe_TargetInfo.Look(ref target, "target"); Set("target", target);
                Verb_MeleeAttackDamage verb = Get<Verb_MeleeAttackDamage>("verb");
                Scribe_References.Look(ref verb, "verb"); Set("verb", verb);
            }
        }
    }
}
