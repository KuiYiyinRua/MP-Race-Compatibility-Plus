using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Meow.TurretCombatSleep
{
    // Cache object lookup only. List mutation invalidates even same-count replacement.
    internal static class ComponentIndex
    {
        private static readonly AccessTools.FieldRef<List<ThingComp>, int> Version = CreateVersion();
        private static AccessTools.FieldRef<List<ThingComp>, int> CreateVersion()
        {
            var f = AccessTools.Field(typeof(List<ThingComp>), "_version");
            return f == null ? null : AccessTools.FieldRefAccess<List<ThingComp>, int>(f);
        }
        private sealed class Entry<T> where T : ThingComp
        {
            internal List<ThingComp> List;
            internal int Version;
            internal T Value;
        }
        private static class Cache<T> where T : ThingComp
        {
            internal static readonly ConditionalWeakTable<ThingWithComps, Entry<T>> Values =
                new ConditionalWeakTable<ThingWithComps, Entry<T>>();
        }
        internal static T Get<T>(ThingWithComps thing) where T : ThingComp
        {
            if (thing == null) return null;
            if (Version == null) return thing.GetComp<T>();
            var list = thing.AllComps;
            if (list == null) return thing.GetComp<T>();
            var e = Cache<T>.Values.GetValue(thing, _ => new Entry<T>());
            int version = Version(list);
            if (!ReferenceEquals(e.List, list) || e.Version != version)
            {
                e.Value = thing.GetComp<T>();
                e.List = list;
                e.Version = version;
            }
            return e.Value;
        }
    }
}
