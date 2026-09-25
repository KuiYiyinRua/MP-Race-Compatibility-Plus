using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Meow.TurretCombatSleep
{
    // Map/faction-owned membership view. Never caches a pawn's health/hostility
    // or a turret's combat decision, and never uses the currently viewed map.
    internal static class MapThreatManagement
    {
        private static readonly AccessTools.FieldRef<AttackTargetsCache, Dictionary<Faction, HashSet<IAttackTarget>>> Hostiles =
            AccessTools.FieldRefAccess<AttackTargetsCache, Dictionary<Faction, HashSet<IAttackTarget>>>("targetsHostileToFaction");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> Aggro =
            AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("pawnsInAggroMentalState");
        private static readonly AccessTools.FieldRef<AttackTargetsCache, HashSet<Pawn>> Factionless =
            AccessTools.FieldRefAccess<AttackTargetsCache, HashSet<Pawn>>("factionlessHumanlikes");
        private static readonly AccessTools.FieldRef<Dictionary<Faction, HashSet<IAttackTarget>>, int> DictionaryVersion = MakeVersion();
        private sealed class View
        {
            internal readonly Dictionary<Faction, Entry> Factions = new Dictionary<Faction, Entry>();
        }
        private sealed class Entry
        {

            internal Faction Faction;
            internal Dictionary<Faction, HashSet<IAttackTarget>> Dictionary;
            internal int Version;
            internal HashSet<IAttackTarget> Hostile;
        }
        private static readonly ConditionalWeakTable<AttackTargetsCache, View> Maps = new ConditionalWeakTable<AttackTargetsCache, View>();
        // Only an acceleration of locating the map view. Cold/hot paths read
        // exactly the same live sets, so save/rejoin/UI query history is irrelevant.
        [ThreadStatic] private static AttackTargetsCache lastCache;
        [ThreadStatic] private static Entry last;
        private static AccessTools.FieldRef<Dictionary<Faction, HashSet<IAttackTarget>>, int> MakeVersion()
        {
            var field = AccessTools.Field(typeof(Dictionary<Faction, HashSet<IAttackTarget>>), "_version") ??
                        AccessTools.Field(typeof(Dictionary<Faction, HashSet<IAttackTarget>>), "version");
            return field == null ? null : AccessTools.FieldRefAccess<Dictionary<Faction, HashSet<IAttackTarget>>, int>(field);
        }
        internal static bool Empty(Thing building)
        {
            var cache = building.Map.attackTargetsCache;
            if (cache == null) return false;
            var faction = building.Faction;
            var dictionary = Hostiles(cache);
            if (DictionaryVersion == null)
                return cache.TargetsHostileToFaction(faction).Count == 0 && Aggro(cache).Count == 0 && Factionless(cache).Count == 0;
            var e = last;
            if (!ReferenceEquals(lastCache, cache) || e == null || !ReferenceEquals(e.Faction, faction))
            {
                var view = Maps.GetValue(cache, _ => new View());
                if (!view.Factions.TryGetValue(faction, out e))
                    view.Factions.Add(faction, e = new Entry { Faction = faction });
                lastCache = cache; last = e;
            }
            int version = DictionaryVersion(dictionary);
            if (!ReferenceEquals(e.Dictionary, dictionary) || e.Version != version)
            {
                e.Dictionary = dictionary; e.Version = version;
                dictionary.TryGetValue(faction, out e.Hostile);
            }
            // Counts are LIVE on every call, including changes made directly by
            // other mods within the same tick. No delayed wake-up polling window.
            return (e.Hostile == null || e.Hostile.Count == 0) &&
                   Aggro(cache).Count == 0 && Factionless(cache).Count == 0;
        }
        internal static void Install(Harmony harmony)
        {
            foreach (var name in new[] { "RegisterTarget", "DeregisterTarget", "UpdateTarget",
                "Notify_FactionHostilityChanged", "Notify_FactionAdded", "Notify_FactionRemoved" })
            {
                var method = AccessTools.DeclaredMethod(typeof(AttackTargetsCache), name);
                if (method == null) throw new MissingMethodException(typeof(AttackTargetsCache).FullName, name);
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(MapThreatManagement), nameof(Changed)));
            }
            Log.Message("[Meow.TurretCombatSleep] Map threat membership events installed; live count/health/hostility wake gates");
        }
        private static void Changed(AttackTargetsCache __instance)
        {
            if (!Maps.TryGetValue(__instance, out var view)) return;
            foreach (var e in view.Factions.Values) e.Dictionary = null;
        }
    }
}
