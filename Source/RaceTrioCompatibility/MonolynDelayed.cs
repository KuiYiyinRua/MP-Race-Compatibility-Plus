using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynDelayed
    {
        internal static bool Enabled;
        internal static readonly Dictionary<string, MethodInfo> Callbacks = new Dictionary<string, MethodInfo>();
        static MethodInfo mapTime;
        static PropertyInfo worldTime;
        static FieldInfo mapTicks, worldTicks;
        internal static void Apply(Harmony harmony)
        {
            Add("ASEL.CompPhoneBooth+<>c__DisplayClass37_0", "<ConsumeAllPawnS>b__1");
            Add("ASEL.CompPhoneBooth+<>c__DisplayClass41_0", "<ConsumeAllPawn>b__1");
            Add("ASEL.CallFromBeyond", "<PostStart>b__7_0");
            Add("ASEL.Building_MonolynProducer+<>c__DisplayClass40_0", "<ContainMaterials>b__0");
            Add("ASEL.CompAbilityEffect_Ingition+<>c__DisplayClass3_1", "<Apply>b__1");
            Add("ASEL.CompLightDistributor", "<PostSpawnSetup>b__19_0");
            Add("ASEL.CompComputationMonitor", "<PostSpawnSetup>b__17_0");
            Add("ASEL.CompTeleport+<>c__DisplayClass20_0", "<MoveBuilding>b__0");
            Add("ASEL.TrailProjectileNew", "<Tick>b__22_0");
            Add("ASEL.Gene_UnitaryCohesion", "<PostAdd>b__0_0");
            Add("ASEL.Gene_UnitaryCohesion", "<PostMake>b__2_0");
            var manager = AccessTools.TypeByName("ASEL.DelayedActionManager") ?? throw new TypeLoadException("ASEL.DelayedActionManager");
            var schedule = AccessTools.DeclaredMethod(manager, "Schedule", new[] { typeof(int), typeof(Action) })
                ?? throw new MissingMethodException(manager.FullName, "Schedule");
            mapTime = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Extensions"), "AsyncTime", new[] { typeof(Map) })
                ?? throw new MissingMethodException("Multiplayer.Client.Extensions.AsyncTime(Map)");
            worldTime = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "AsyncWorldTime")
                ?? throw new MissingMemberException("Multiplayer.Client.Multiplayer.AsyncWorldTime");
            mapTicks = AccessTools.Field(mapTime.ReturnType, "mapTicks") ?? throw new MissingFieldException("MP mapTicks");
            worldTicks = AccessTools.Field(worldTime.PropertyType, "worldTicks") ?? throw new MissingFieldException("MP worldTicks");
            Enabled = true;
            harmony.Patch(schedule, prefix: new HarmonyMethod(typeof(MonolynDelayed), nameof(Schedule)));
            Log.Message("[MonolynDelayed] resolved=11 callback sites; saved map/world queues; missing=0");
        }
        static void Add(string type, string method)
        {
            var t = AccessTools.TypeByName(type) ?? throw new TypeLoadException(type);
            var m = AccessTools.DeclaredMethod(t, method, Type.EmptyTypes) ?? throw new MissingMethodException(type, method);
            if (m.IsStatic || m.ReturnType != typeof(void)) throw new InvalidOperationException("Unexpected delayed callback " + m);
            Callbacks.Add(type + "|" + method, m);
        }
        static bool Schedule(int delayTicks, Action action)
        {
            if (!Enabled || action == null || !action.Method.DeclaringType.FullName.StartsWith("ASEL.", StringComparison.Ordinal)) return true;
            var entry = new MonolynPending(action);
            var map = entry.Map;
            entry.Tick = Clock(map) + Math.Max(0, delayTicks);
            if (map != null)
            {
                var queue = map.GetComponent<MonolynPendingMap>();
                if (queue.Actions == null) queue.Actions = new List<MonolynPending>();
                // PostSpawnSetup also schedules the introduction during loading.
                // Its original pending record already holds the exact due tick.
                if (entry.IsIntroduction && queue.Actions.Any(e => e.SameIntroduction(entry))) return false;
                queue.Actions.Add(entry);
            }
            else
            {
                var queue = Current.Game.GetComponent<MonolynPendingWorld>();
                if (queue.Actions == null) queue.Actions = new List<MonolynPending>();
                queue.Actions.Add(entry);
            }
            return false;
        }
        static int Clock(Map map)
        {
            if (!MP.IsInMultiplayer) return Find.TickManager.TicksGame;
            var clock = map == null ? worldTime.GetValue(null) : mapTime.Invoke(null, new object[] { map });
            if (clock != null) return (int)(map == null ? worldTicks : mapTicks).GetValue(clock);
            // Load-time PostSpawnSetup can precede restoration of MP clocks.
            if (Scribe.mode != LoadSaveMode.Inactive) return Find.TickManager.TicksGame;
            throw new InvalidOperationException("Missing Monolyn scheduling clock");
        }
        internal static void Tick(List<MonolynPending> actions)
        {
            for (int i = actions.Count - 1; i >= 0; i--)
            {
                var entry = actions[i];
                if (Find.TickManager.TicksGame < entry.Tick) continue;
                try { entry.Execute(); }
                catch (Exception e) { Log.Error("[MonolynDelayed] action failed: " + entry.Key + " " + e); }
                actions.RemoveAt(i);
            }
        }
        internal static object Get(object target, string field) => AccessTools.Field(target.GetType(), field).GetValue(target);
        internal static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
    }

    public sealed class MonolynPendingMap : MapComponent
    {
        internal List<MonolynPending> Actions = new List<MonolynPending>();
        public MonolynPendingMap(Map map) : base(map) { }
        public override void MapComponentTick() { if (MonolynDelayed.Enabled) MonolynDelayed.Tick(Actions); }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Actions, "meowMonolynPending", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Actions == null) Actions = new List<MonolynPending>();
        }
    }

    public sealed class MonolynPendingWorld : GameComponent
    {
        internal List<MonolynPending> Actions = new List<MonolynPending>();
        public MonolynPendingWorld(Game game) { }
        public override void GameComponentTick() { if (MonolynDelayed.Enabled) MonolynDelayed.Tick(Actions); }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Actions, "meowMonolynWorldPending", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Actions == null) Actions = new List<MonolynPending>();
        }
    }

    public sealed class MonolynPending : IExposable
    {
        internal int Tick;
        internal string Key;
        string kind, compType;
        Thing owner, other;
        Pawn pawn;
        Thing deepThing, cachedWeapon;
        Thing materialRef;
        bool materialDeep;
        MentalState contextMental, detachedMental;
        Gene contextGene, detachedGene;
        GeneDef geneDef;
        int abilityId, compIndex, ownerId;
        DamageDef damageOverride;
        float stoppingPower;
        List<MonolynPendingDamage> damages;
        Action action;
        internal Map Map { get; private set; }
        internal bool IsIntroduction => kind == "monitor";
        internal bool SameIntroduction(MonolynPending e) => IsIntroduction && e.IsIntroduction && ownerId == e.ownerId;
        public MonolynPending() { }
        internal MonolynPending(Action source)
        {
            action = source;
            Key = source.Method.DeclaringType.FullName + "|" + source.Method.Name;
            if (!MonolynDelayed.Callbacks.TryGetValue(Key, out var expected) || expected != source.Method)
                throw new InvalidOperationException("Unaudited Monolyn delayed callback " + Key);
            object context = source.Target;
            string type = source.Method.DeclaringType.FullName;
            if (type.StartsWith("ASEL.CompPhoneBooth+", StringComparison.Ordinal))
            {
                kind = "consume"; CaptureComp((ThingComp)MonolynDelayed.Get(context, "<>4__this"));
                pawn = (Pawn)MonolynDelayed.Get(context, "p");
            }
            else if (type.StartsWith("ASEL.Building_MonolynProducer+", StringComparison.Ordinal))
            {
                kind = "material"; owner = (Thing)MonolynDelayed.Get(context, "<>4__this");
                deepThing = (Thing)MonolynDelayed.Get(context, "material");
            }
            else if (type.StartsWith("ASEL.CompAbilityEffect_Ingition+", StringComparison.Ordinal))
            {
                kind = "ignition"; pawn = (Pawn)MonolynDelayed.Get(context, "pawn");
                var outer = MonolynDelayed.Get(context, "CS$<>8__locals1");
                var comp = (AbilityComp)MonolynDelayed.Get(outer, "<>4__this");
                owner = comp.parent.pawn; abilityId = comp.parent.Id; compIndex = comp.parent.comps.IndexOf(comp);
                compType = comp.GetType().FullName;
            }
            else if (type.StartsWith("ASEL.CompTeleport+", StringComparison.Ordinal))
            {
                kind = "teleport"; CaptureComp((ThingComp)MonolynDelayed.Get(context, "distributorC"));
                other = ((ThingComp)MonolynDelayed.Get(context, "receiver")).parent;
            }
            else if (type == "ASEL.CompComputationMonitor") { kind = "monitor"; CaptureComp((ThingComp)context); }
            else if (type == "ASEL.CompLightDistributor") { kind = "reconnect"; CaptureComp((ThingComp)context); }
            else if (type == "ASEL.CallFromBeyond") { kind = "mental"; contextMental = (MentalState)context; owner = contextMental.pawn; }
            else if (type == "ASEL.Gene_UnitaryCohesion") { kind = "gene"; contextGene = (Gene)context; owner = contextGene.pawn; geneDef = contextGene.def; }
            else if (type == "ASEL.TrailProjectileNew")
            {
                kind = "projectile"; deepThing = (Thing)context;
                cachedWeapon = (Thing)MonolynDelayed.Get(context, "cachedWeapon");
                damageOverride = (DamageDef)MonolynDelayed.Get(context, "damageDefOverride");
                stoppingPower = (float)MonolynDelayed.Get(context, "stoppingPower");
                var extras = (List<ExtraDamage>)MonolynDelayed.Get(context, "extraDamages");
                damages = extras?.Select(e => new MonolynPendingDamage(e)).ToList();
                Map = deepThing.Map;
            }
            else throw new InvalidOperationException("Missing capture strategy " + Key);
            if (Map == null) Map = owner?.MapHeld;
            ownerId = owner?.thingIDNumber ?? -1;
        }
        void CaptureComp(ThingComp comp)
        {
            owner = comp.parent; compType = comp.GetType().FullName; compIndex = comp.parent.AllComps.IndexOf(comp);
        }
        ThingComp Comp()
        {
            var comps = ((ThingWithComps)owner).AllComps;
            if (compIndex < 0 || compIndex >= comps.Count || comps[compIndex].GetType().FullName != compType)
                throw new InvalidOperationException("Delayed component identity changed: " + Key);
            return comps[compIndex];
        }
        object Closure(MethodInfo method) => Activator.CreateInstance(method.DeclaringType, true);
        Action Restore()
        {
            if (!MonolynDelayed.Callbacks.TryGetValue(Key, out var method)) throw new InvalidOperationException("Unknown saved callback " + Key);
            object context;
            switch (kind)
            {
                case "consume":
                    context = Closure(method); MonolynDelayed.Set(context, "<>4__this", Comp()); MonolynDelayed.Set(context, "p", pawn); break;
                case "material":
                    context = Closure(method); MonolynDelayed.Set(context, "<>4__this", owner); MonolynDelayed.Set(context, "material", deepThing); break;
                case "ignition":
                    var ability = ((Pawn)owner).abilities.AllAbilitiesForReading.FirstOrDefault(a => a.Id == abilityId);
                    AbilityComp ignition;
                    if (ability != null && compIndex >= 0 && compIndex < ability.comps.Count && ability.comps[compIndex].GetType().FullName == compType)
                        ignition = ability.comps[compIndex];
                    else
                    {
                        // Explode reads only parent.pawn from the ability context.
                        // Ability(Pawn) allocates no IDs and performs no Initialize.
                        // Preserve an already-cast explosion after its ability is removed.
                        ignition = (AbilityComp)Activator.CreateInstance(AccessTools.TypeByName(compType));
                        ignition.parent = new Ability((Pawn)owner);
                    }
                    var outerType = AccessTools.TypeByName("ASEL.CompAbilityEffect_Ingition+<>c__DisplayClass3_0");
                    var outer = Activator.CreateInstance(outerType, true);
                    MonolynDelayed.Set(outer, "caster", owner); MonolynDelayed.Set(outer, "<>4__this", ignition);
                    context = Closure(method); MonolynDelayed.Set(context, "pawn", pawn); MonolynDelayed.Set(context, "CS$<>8__locals1", outer); break;
                case "teleport":
                    context = Closure(method); MonolynDelayed.Set(context, "distributorC", Comp());
                    MonolynDelayed.Set(context, "receiver", ((ThingWithComps)other).AllComps.Single(c => c.GetType().FullName == "ASEL.CompLightReceiver")); break;
                case "monitor": case "reconnect": context = Comp(); break;
                case "mental": contextMental = detachedMental ?? ((Pawn)owner).MentalState; context = contextMental; break;
                case "gene": contextGene = detachedGene ?? ((Pawn)owner).genes.GenesListForReading.SingleOrDefault(g => g.def == geneDef && g.GetType() == method.DeclaringType); context = contextGene; break;
                case "projectile":
                    context = deepThing;
                    MonolynDelayed.Set(context, "cachedWeapon", cachedWeapon);
                    MonolynDelayed.Set(context, "damageDefOverride", damageOverride);
                    MonolynDelayed.Set(context, "stoppingPower", stoppingPower);
                    MonolynDelayed.Set(context, "extraDamages", damages?.Select(d => d.Restore()).ToList()); break;
                default: throw new InvalidOperationException("Unknown delayed capture kind " + kind);
            }
            if (context == null || !method.DeclaringType.IsInstanceOfType(context)) throw new InvalidOperationException("Missing saved callback context " + Key);
            return (Action)Delegate.CreateDelegate(typeof(Action), context, method);
        }
        internal void Execute()
        {
            if (!HasContext()) return;
            if (kind == "teleport" && (owner.Map == null || other.Map != owner.Map)) return;
            if (kind == "consume" && !pawn.Spawned) return;
            if (kind == "ignition" && (owner.Map == null || pawn.MapHeld != owner.Map)) return;
            if (kind == "projectile")
            {
                // Native lambda reads casterPawn.Map before JumpToTarget's own
                // null check. A removed launcher cannot start another flight.
                var caster = ((Projectile)deepThing).Launcher as Pawn;
                if (caster == null || caster.Destroyed || !caster.Spawned) return;
            }
            (action ?? (action = Restore()))?.Invoke();
        }
        bool HasContext()
        {
            if (kind == "projectile") return deepThing != null;
            if (owner == null || owner.Destroyed) return false;
            // A native delayed closure retains these objects in memory, but a
            // snapshot omits destroyed references. Never dereference a missing
            // receiver/pawn while reconstructing it during PostLoadInit.
            if (kind == "teleport") return other is ThingWithComps && !other.Destroyed;
            if (kind == "consume") return pawn != null && !pawn.Destroyed;
            if (kind == "ignition") return pawn != null && !pawn.Discarded;
            return true;
        }
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                if (kind == "material")
                {
                    // Save live game ownership by reference; retain detached stacks deeply.
                    materialDeep = deepThing != null && !deepThing.Spawned && deepThing.ParentHolder == null;
                    materialRef = materialDeep ? null : deepThing;
                }
                // Pending native callbacks retain their original context even if
                // its ability/gene/mental state is removed before snapshotting.
                if (kind == "mental") detachedMental = ((Pawn)owner).MentalState == contextMental ? null : contextMental;
                if (kind == "gene") detachedGene = ((Pawn)owner).genes.GenesListForReading.Contains(contextGene) ? null : contextGene;
            }
            Scribe_Values.Look(ref Tick, "tick"); Scribe_Values.Look(ref Key, "callback"); Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref ownerId, "ownerId", -1); Scribe_Values.Look(ref compType, "compType");
            Scribe_Values.Look(ref compIndex, "compIndex"); Scribe_Values.Look(ref abilityId, "abilityId");
            Scribe_References.Look(ref owner, "owner"); Scribe_References.Look(ref other, "other");
            // Ignition can still strike a dead pawn's owned corpse; keep that
            // native target instead of the default destroyed-reference nulling.
            Scribe_References.Look(ref pawn, "pawn", kind == "ignition" && pawn != null && !pawn.Discarded);
            Scribe_Values.Look(ref materialDeep, "materialDeep");
            Scribe_References.Look(ref materialRef, "materialRef");
            if (kind == "projectile" || kind == "material" && materialDeep) Scribe_Deep.Look(ref deepThing, "detachedThing");
            Scribe_Deep.Look(ref detachedMental, "detachedMental");
            Scribe_Deep.Look(ref detachedGene, "detachedGene");
            Scribe_References.Look(ref cachedWeapon, "cachedWeapon"); Scribe_Defs.Look(ref geneDef, "geneDef");
            Scribe_Defs.Look(ref damageOverride, "damageOverride"); Scribe_Values.Look(ref stoppingPower, "stoppingPower");
            Scribe_Collections.Look(ref damages, "extraDamages", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (kind == "material" && !materialDeep) deepThing = materialRef;
                if (detachedMental != null) detachedMental.pawn = owner as Pawn;
                if (detachedGene != null) detachedGene.pawn = owner as Pawn;
                if (HasContext()) action = Restore();
            }
        }
    }

    public sealed class MonolynPendingDamage : IExposable
    {
        DamageDef def; float amount, armorPenetration, chance;
        public MonolynPendingDamage() { }
        internal MonolynPendingDamage(ExtraDamage d) { def = d.def; amount = d.amount; armorPenetration = d.armorPenetration; chance = d.chance; }
        internal ExtraDamage Restore() => new ExtraDamage { def = def, amount = amount, armorPenetration = armorPenetration, chance = chance };
        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def"); Scribe_Values.Look(ref amount, "amount");
            Scribe_Values.Look(ref armorPenetration, "armorPenetration"); Scribe_Values.Look(ref chance, "chance");
        }
    }
}
