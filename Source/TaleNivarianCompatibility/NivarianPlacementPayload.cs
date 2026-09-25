using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // The native build-designator postfix reads a process-local selected-designator
    // payload. Carry that value with the map command instead of replaying local UI state.
    internal static class NivarianPlacementPayload
    {
        static Type compType, payloadType;
        static FieldInfo current, angle, buildingDef;
        static ISyncMethod place;

        internal static void Apply(Harmony harmony)
        {
            compType = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompProperties_SelfBuilding")
                ?? throw new TypeLoadException("CompProperties_SelfBuilding");
            payloadType = AccessTools.TypeByName("Nivarian_Race.Code.Payload.PlacementPayload")
                ?? throw new TypeLoadException("PlacementPayload");
            var state = AccessTools.TypeByName("Nivarian_Race.Code.Patches.PayloadPlacementState")
                ?? throw new TypeLoadException("PayloadPlacementState");
            current = AccessTools.Field(state, "Current") ?? throw new MissingFieldException(state.FullName, "Current");
            angle = AccessTools.Field(payloadType, "winterTurretConeAngle")
                ?? throw new MissingFieldException(payloadType.FullName, "winterTurretConeAngle");
            buildingDef = AccessTools.Field(compType, "buildingDef")
                ?? throw new MissingFieldException(compType.FullName, "buildingDef");
            place = MP.RegisterSyncMethod(typeof(NivarianPlacementPayload), nameof(Place), new[] {
                new SyncType(typeof(Map)) { contextMap = true },
                new SyncType(typeof(Designator_Build)), new SyncType(typeof(IntVec3)),
                new SyncType(typeof(bool)), new SyncType(typeof(float))
            });
            var target = AccessTools.DeclaredMethod(typeof(Designator_Build), nameof(Designator_Build.DesignateSingleCell),
                new[] { typeof(IntVec3) }) ?? throw new MissingMethodException("Designator_Build.DesignateSingleCell");
            // Multiplayer's own designator prefix uses Priority.First + 1 and
            // returns false in interface mode. Run before it to replace the command.
            var prefix = new HarmonyMethod(typeof(NivarianPlacementPayload), nameof(Designate)) { priority = Priority.First + 2 };
            harmony.Patch(target, prefix: prefix);
            Log.Message("[TaleNivarianCompat] Self-building designator commands carry the selected placement payload.");
        }

        static bool HasNativePayload(Designator_Build designator)
        {
            if (!(designator?.PlacingDef is ThingDef core) || core.comps == null) return false;
            return core.comps.Any(comp => comp != null && compType.IsInstanceOfType(comp)
                && buildingDef.GetValue(comp) is ThingDef);
        }

        static bool Designate(Designator_Build __instance, IntVec3 c)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || !HasNativePayload(__instance)) return true;
            var map = Find.CurrentMap;
            if (map == null) return false;
            var selected = current.GetValue(null);
            float rotation = selected == null ? 0f : (float)angle.GetValue(selected);
            if (float.IsNaN(rotation) || float.IsInfinity(rotation)) return false;
            if (!place.DoSync(null, map, __instance, c, selected != null, rotation))
                Log.Error("[TaleNivarianCompat] Placement payload command rejected for " + __instance.PlacingDef.defName);
            return false;
        }

        static void Place(Map map, Designator_Build designator, IntVec3 c, bool hasPayload, float rotation)
        {
            if (map == null || designator == null || !HasNativePayload(designator)
                || float.IsNaN(rotation) || float.IsInfinity(rotation)) return;
            var previous = current.GetValue(null);
            try
            {
                object synced = null;
                if (hasPayload)
                {
                    synced = Activator.CreateInstance(payloadType);
                    angle.SetValue(synced, rotation);
                }
                current.SetValue(null, synced);
                designator.DesignateSingleCell(c);
                // Match Multiplayer's native designator-command replay.
                designator.Finalize(true);
            }
            finally
            {
                current.SetValue(null, previous);
            }
        }
    }
}
