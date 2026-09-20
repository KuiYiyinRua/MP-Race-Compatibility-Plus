using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianMetricsRefresh
    {
        static MethodInfo getMapComp, setFaction;
        static FieldInfo[] mapManagers;
        internal sealed class SavedMap { internal Map Map; internal object[] Managers; }
        internal sealed class Scope { internal readonly List<SavedMap> Maps = new List<SavedMap>(); }

        internal static void Apply(Harmony harmony)
        {
            var metrics = AccessTools.TypeByName("Nivarian.GameComp_NivarianNiraMetrics")
                ?? throw new TypeLoadException("Nivarian metrics");
            var refresh = AccessTools.DeclaredMethod(metrics, "RefreshMetrics", Type.EmptyTypes)
                ?? throw new MissingMethodException(metrics.FullName, "RefreshMetrics");
            var extensions = AccessTools.TypeByName("Multiplayer.Client.Extensions")
                ?? throw new TypeLoadException("MP map extensions");
            getMapComp = AccessTools.DeclaredMethod(extensions, "MpComp", new[] { typeof(Map) })
                ?? throw new MissingMethodException(extensions.FullName, "MpComp");
            setFaction = AccessTools.DeclaredMethod(getMapComp.ReturnType, "SetFaction", new[] { typeof(Faction) })
                ?? throw new MissingMethodException("MP map SetFaction");
            // Exact manager references replaced by the installed MP SetFaction.
            mapManagers = new[] { "designationManager", "areaManager", "zoneManager", "planManager",
                "haulDestinationManager", "listerHaulables", "resourceCounter", "listerFilthInHomeArea", "listerMergeables" }
                .Select(n => AccessTools.Field(typeof(Map), n) ?? throw new MissingFieldException(typeof(Map).FullName, n)).ToArray();
            MP.RegisterSyncMethod(refresh);
            harmony.Patch(refresh, prefix: new HarmonyMethod(typeof(NivarianMetricsRefresh), nameof(Before)),
                finalizer: new HarmonyMethod(typeof(NivarianMetricsRefresh), nameof(After)));
            Log.Message("[TaleNivarianCompat] Nira UI metric refresh synchronized with faction map managers restored after execution.");
        }

        internal static void Before(out Scope __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || MP.InInterface) return;
            __state = new Scope();
            // World commands select the issuer's world data, but maps may still
            // contain spectator managers. Preserve their exact incoming references.
            var faction = Faction.OfPlayer;
            foreach (var map in Find.Maps.OrderBy(m => m.uniqueID))
            {
                var comp = getMapComp.Invoke(null, new object[] { map });
                if (comp == null) continue;
                __state.Maps.Add(new SavedMap { Map = map, Managers = mapManagers.Select(f => f.GetValue(map)).ToArray() });
                setFaction.Invoke(comp, new object[] { faction });
            }
        }

        internal static void After(Scope __state)
        {
            if (__state == null) return;
            for (int m = __state.Maps.Count - 1; m >= 0; m--)
            {
                var saved = __state.Maps[m];
                for (int i = 0; i < mapManagers.Length; i++) mapManagers[i].SetValue(saved.Map, saved.Managers[i]);
            }
        }
    }
}
