using System;
using System.Linq;
using System.Reflection;
using Verse;
namespace Meow.TaleNivarianCompatibility
{
    // MP PopFaction restores global-faction managers, not necessarily the map's
    // incoming managers. Save references separately at every temporary map scope.
    internal sealed class SavedMapManagers
    {
        static readonly FieldInfo[] fields = new[] { "designationManager", "areaManager", "zoneManager", "planManager", "haulDestinationManager", "listerHaulables", "resourceCounter", "listerFilthInHomeArea", "listerMergeables" }
            .Select(n => AccessToolsField(n)).ToArray();
        static FieldInfo AccessToolsField(string name) => HarmonyLib.AccessTools.Field(typeof(Map), name) ?? throw new MissingFieldException(typeof(Map).FullName, name);
        readonly Map map;
        readonly object[] values;
        internal SavedMapManagers(Map map) { this.map = map; values = fields.Select(f => f.GetValue(map)).ToArray(); }
        internal void Restore() { for (int i = 0; i < fields.Length; i++) fields[i].SetValue(map, values[i]); }
    }
}
