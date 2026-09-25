using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Meow.TurretCombatSleep
{
    // A derived index, not a cached combat decision. Rebuilding it must give the
    // same answer after load/rejoin, independently of tick rate and viewed map.
    internal static class SharedThreatIndex
    {
        private static readonly ConditionalWeakTable<AttackTargetsCache, MapIndex> Maps = new ConditionalWeakTable<AttackTargetsCache, MapIndex>();
        private static readonly ConditionalWeakTable<Thing, Binding> Bindings = new ConditionalWeakTable<Thing, Binding>();
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> Aggro = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("pawnsInAggroMentalState");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> Factionless = AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("factionlessHumanlikes");

        private static class SetVersion<T>
        {
            internal static readonly AccessTools.FieldRef<HashSet<T>, int> Read = Create();
            private static AccessTools.FieldRef<HashSet<T>, int> Create()
            {
                var field = AccessTools.Field(typeof(HashSet<T>), "_version") ?? AccessTools.Field(typeof(HashSet<T>), "m_version");
                return field != null && field.FieldType == typeof(int)
                    ? AccessTools.FieldRefAccess<HashSet<T>, int>(field) : null;
            }
        }

        private sealed class MapIndex
        {
            internal readonly Dictionary<Faction, FactionIndex> Factions = new Dictionary<Faction, FactionIndex>();
        }

        private sealed class Binding
        {
            internal AttackTargetsCache Cache;
            internal Faction Faction;
            internal FactionIndex Index;
        }

        private sealed class FactionIndex
        {
            private HashSet<IAttackTarget> hostile;
            private HashSet<Pawn> aggro, factionless;
            private int hostileVersion, aggroVersion, factionlessVersion;
            private readonly List<IAttackTarget> candidates = new List<IAttackTarget>();

            internal bool Quiet(AttackTargetsCache cache, Faction faction, Thing building)
            {
                var h = cache.TargetsHostileToFaction(faction);
                var a = Aggro(cache);
                var f = Factionless(cache);
                int hv = SetVersion<IAttackTarget>.Read(h), av = SetVersion<Pawn>.Read(a), fv = SetVersion<Pawn>.Read(f);
                if (!ReferenceEquals(h, hostile) || !ReferenceEquals(a, aggro) || !ReferenceEquals(f, factionless) ||
                    hv != hostileVersion || av != aggroVersion || fv != factionlessVersion)
                {
                    // HashSet mutation versions also detect same-count replacement.
                    // Preserve source order and duplicates: mod hostility predicates
                    // may have patched behavior. No sorting or extra predicate calls.
                    candidates.Clear();
                    foreach (var target in h) candidates.Add(target);
                    foreach (var pawn in a) candidates.Add(pawn);
                    foreach (var pawn in f) candidates.Add(pawn);
                    hostile = h; aggro = a; factionless = f;
                    hostileVersion = hv; aggroVersion = av; factionlessVersion = fv;
                }
                for (int i = 0; i < candidates.Count; i++)
                    if (Threatens(candidates[i]?.Thing, building)) return false;
                return true;
            }
        }

        internal static bool Quiet(Thing building)
        {
            var cache = building.Map.attackTargetsCache;
            if (cache == null) return false;
            // The common peaceful case needs only three count reads; no cache
            // allocation, collection version reads, or per-turret state lookup.
            if (MapThreatManagement.Empty(building))
                return true;
            // Unsupported runtime collection layout: use the original live walk.
            if (SetVersion<IAttackTarget>.Read == null || SetVersion<Pawn>.Read == null)
                return LiveQuiet(cache, building);
            var binding = Bindings.GetValue(building, _ => new Binding());
            var faction = building.Faction;
            if (!ReferenceEquals(binding.Cache, cache) || !ReferenceEquals(binding.Faction, faction))
            {
                var map = Maps.GetValue(cache, _ => new MapIndex());
                if (!map.Factions.TryGetValue(faction, out var index))
                    map.Factions.Add(faction, index = new FactionIndex());
                binding.Cache = cache;
                binding.Faction = faction;
                binding.Index = index;
            }
            return binding.Index.Quiet(cache, faction, building);
        }

        private static bool LiveQuiet(AttackTargetsCache cache, Thing building)
        {
            foreach (var target in cache.TargetsHostileToFaction(building.Faction))
                if (Threatens(target?.Thing, building)) return false;
            foreach (var pawn in Aggro(cache)) if (Threatens(pawn, building)) return false;
            foreach (var pawn in Factionless(cache)) if (Threatens(pawn, building)) return false;
            return true;
        }

        private static bool Threatens(Thing candidate, Thing building)
        {
            if (candidate == null || !candidate.Spawned || candidate.Destroyed || candidate.Map != building.Map) return false;
            if (candidate is Pawn pawn && (pawn.Dead || pawn.Downed)) return false;
            return candidate.HostileTo(building);
        }
    }
}
