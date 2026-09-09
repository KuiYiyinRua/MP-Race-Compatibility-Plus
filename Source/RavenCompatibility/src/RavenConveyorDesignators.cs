using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenConveyorDesignators
    {
        private const string Ns = "RavenRace.Features.RavenConveyor.";
        private static Type buildType;
        private static MethodInfo selectedLayer;
        private static readonly ConditionalWeakTable<Designator_Build, Selection> selections = new ConditionalWeakTable<Designator_Build, Selection>();
        [ThreadStatic] private static Selection current;
        private sealed class Selection
        {
            public ThingDef def;
            public int layer;
        }
        internal static void Apply(Harmony harmony)
        {
            buildType = AccessTools.TypeByName(Ns + "Designator_BuildRavenConveyor");
            var layers = AccessTools.TypeByName(Ns + "RavenConveyorBuildLayerState");
            selectedLayer = AccessTools.DeclaredMethod(layers, "SelectedLayerFor");
            foreach (string suffix in new[] { "Normal", "Splitter", "Merger", "InfiniteSource" })
            {
                var type = AccessTools.TypeByName(Ns + "Designator_BuildRavenConveyor" + suffix)
                    ?? throw new TypeLoadException("Raven conveyor designator " + suffix);
                MP.RegisterSyncWorker<Designator_Build>((SyncWorker sync, ref Designator_Build value) => Sync(sync, ref value, type), type, false, false);
            }
            foreach (string name in new[] { "CanDesignateCell", "DesignateSingleCell", "DesignateMultiCell" })
                harmony.Patch(AccessTools.DeclaredMethod(buildType, name),
                    prefix: new HarmonyMethod(typeof(RavenConveyorDesignators), nameof(BeginScope)),
                    finalizer: new HarmonyMethod(typeof(RavenConveyorDesignators), nameof(EndScope)));
            harmony.Patch(selectedLayer, prefix: new HarmonyMethod(typeof(RavenConveyorDesignators), nameof(LayerOverride)));
        }
        private static void Sync(SyncWorker sync, ref Designator_Build value, Type concrete)
        {
            if (!sync.isWriting) value = (Designator_Build)Activator.CreateInstance(concrete);
            var rotationField = AccessTools.Field(typeof(Designator_Place), "placingRot");
            var stuffField = AccessTools.Field(typeof(Designator_Build), "stuffDef");
            var preceptField = AccessTools.Field(typeof(Designator_Build), "sourcePrecept");
            var originField = AccessTools.Field(buildType, "dragStart");
            Rot4 rotation = sync.isWriting ? (Rot4)rotationField.GetValue(value) : Rot4.North;
            ThingDef stuff = sync.isWriting ? (ThingDef)stuffField.GetValue(value) : null;
            Precept_Building precept = sync.isWriting ? (Precept_Building)preceptField.GetValue(value) : null;
            IntVec3 origin = sync.isWriting ? (IntVec3)originField.GetValue(value) : IntVec3.Invalid;
            int layer = sync.isWriting ? (int)selectedLayer.Invoke(null, new object[] { value.PlacingDef }) : 0;
            sync.Bind(ref rotation); sync.Bind(ref stuff); sync.Bind(ref precept); sync.Bind(ref origin); sync.Bind(ref layer);
            if (layer < 0 || layer > 3) throw new InvalidOperationException("Raven conveyor build layer " + layer);
            if (!sync.isWriting)
            {
                rotationField.SetValue(value, rotation); stuffField.SetValue(value, stuff); preceptField.SetValue(value, precept);
                originField.SetValue(value, origin);
                AccessTools.Field(buildType, "dragTrackingActive").SetValue(value, origin.IsValid);
                selections.Add(value, new Selection { def = (ThingDef)value.PlacingDef, layer = layer });
            }
        }
        private static void BeginScope(Designator_Build __instance, out Selection __state)
        {
            __state = current;
            if (selections.TryGetValue(__instance, out var selection)) current = selection;
        }
        private static void EndScope(Selection __state) => current = __state;
        private static bool LayerOverride(ThingDef __0, ref int __result)
        {
            if (current == null || current.def != __0) return true;
            __result = current.layer;
            return false;
        }
    }
}
