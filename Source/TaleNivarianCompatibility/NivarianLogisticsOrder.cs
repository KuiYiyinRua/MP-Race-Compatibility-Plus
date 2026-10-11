using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianLogisticsOrder
    {
        static FieldInfo[] componentLists;
        static FieldInfo neighbors, buckets, routes, stockThing, stockInput;
        static FieldInfo demandPriority, demandKey, keyDest, keyDef, keySink, keyFuel;
        internal static void Apply(Harmony harmony)
        {
            var network = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Logistics.CannonNetworkMapComponent");
            componentLists = new[] { Field(network, "nodes"), Field(network, "inputs"), Field(network, "containers") };
            neighbors = Field(network, "neighbors"); buckets = Field(network, "buckets"); routes = Field(network, "routes");
            var stock = network.GetNestedType("GroundStock", AccessTools.all) ?? throw new TypeLoadException("Cannon GroundStock");
            stockThing = Field(stock, "thing"); stockInput = Field(stock, "input");
            foreach (var method in network.GetMethods(AccessTools.allDeclared))
                if ((method.Name == "Register" && method.GetParameters()[0].ParameterType.Name != "CannonCargoFlyer") || method.Name == "RebuildAllEdges" || method.Name == "RebuildEdgesFor")
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(NivarianLogisticsOrder), nameof(OrderNetwork)));
            harmony.Patch(NivarianUpdateActions.RequiredMethod(network, "CollectGroundStock", typeof(ThingDef), typeof(System.Collections.Generic.List<>).MakeGenericType(stock)),
                postfix: new HarmonyMethod(typeof(NivarianLogisticsOrder), nameof(OrderStock)));
            var calculator = NivarianUpdateActions.RequiredType("Nivarian_Race.Code.Logistics.Orders.OrderDemandCalculator");
            var entry = calculator.GetNestedType("NeedEntry", AccessTools.all) ?? throw new TypeLoadException("Cannon NeedEntry");
            demandPriority = Field(entry, "priority"); demandKey = Field(entry, "key");
            keyDest = Field(demandKey.FieldType, "destBuilding"); keyDef = Field(demandKey.FieldType, "def");
            keySink = Field(demandKey.FieldType, "sink"); keyFuel = Field(demandKey.FieldType, "fuelCompIndex");
            harmony.Patch(NivarianUpdateActions.RequiredMethod(calculator, "Compute", network, typeof(Map)), postfix: new HarmonyMethod(typeof(NivarianLogisticsOrder), nameof(OrderDemands)));
            Log.Message("[TaleNivarianCompat] cannon registration, graph neighbors and HashSet ground stock ordered by durable thing ID.");
        }
        static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        static int Id(object value) => value is ThingComp comp ? comp.parent?.thingIDNumber ?? -1 : value is Thing thing ? thing.thingIDNumber : -1;
        static bool Sort(IList list, Comparison<object> comparison)
        {
            if (list.Count < 2) return false;
            var values = new object[list.Count]; list.CopyTo(values, 0);
            Array.Sort(values, comparison);
            bool changed = false;
            for (int i = 0; i < values.Length; i++) changed |= comparison(list[i], values[i]) != 0;
            for (int i = 0; i < values.Length; i++) list[i] = values[i];
            return changed;
        }
        static void OrderNetwork(object __instance)
        {
            if (!MP.IsInMultiplayer) return;
            bool changed = false;
            foreach (var field in componentLists) changed |= Sort((IList)field.GetValue(__instance), CompareId);
            foreach (var field in new[] { neighbors, buckets })
                foreach (IList list in ((IDictionary)field.GetValue(__instance)).Values) changed |= Sort(list, CompareId);
            // Route tables derive from neighbor traversal and must be rebuilt after its order changes.
            if (changed) ((IDictionary)routes.GetValue(__instance)).Clear();
        }
        static int CompareId(object a, object b) => Id(a).CompareTo(Id(b));
        static void OrderDemands(object __result)
        {
            if (!MP.IsInMultiplayer) return;
            Sort((IList)__result, (a, b) =>
            {
                int c = ((int)demandPriority.GetValue(b)).CompareTo((int)demandPriority.GetValue(a));
                if (c != 0) return c;
                var x = demandKey.GetValue(a); var y = demandKey.GetValue(b);
                c = Id(keyDest.GetValue(x)).CompareTo(Id(keyDest.GetValue(y))); if (c != 0) return c;
                c = Convert.ToInt32(keySink.GetValue(x)).CompareTo(Convert.ToInt32(keySink.GetValue(y))); if (c != 0) return c;
                c = ((int)keyFuel.GetValue(x)).CompareTo((int)keyFuel.GetValue(y)); if (c != 0) return c;
                return string.CompareOrdinal((keyDef.GetValue(x) as ThingDef)?.defName, (keyDef.GetValue(y) as ThingDef)?.defName);
            });
        }
        static void OrderStock(object __1)
        {
            if (!MP.IsInMultiplayer) return;
            Sort((IList)__1, (a, b) =>
            {
                int result = Id(stockThing.GetValue(a)).CompareTo(Id(stockThing.GetValue(b)));
                return result != 0 ? result : Id(stockInput.GetValue(a)).CompareTo(Id(stockInput.GetValue(b)));
            });
        }
    }
}
