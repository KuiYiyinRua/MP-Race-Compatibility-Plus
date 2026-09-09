using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class NivarianExpansions
    {
        const string Furniture = "Nivarian_Race_Draconiture.";
        const string Military = "Nivarian_Race_DraconicMilitary.";
        static ISyncMethod cone, teleport, rename, droneTarget, wheelchair;
        static Type registryType;
        static Type T(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        static object Get(object value, string field) => AccessTools.Field(value.GetType(), field).GetValue(value);
        static void Set(object value, string field, object data) => AccessTools.Field(value.GetType(), field).SetValue(value, data);
        static void Call(object value, string method, params object[] args) => AccessTools.Method(value.GetType(), method).Invoke(value, args);
        static void Sync(string type, string method, params Type[] args)
        {
            var target = AccessTools.DeclaredMethod(T(type), method, args) ?? throw new MissingMethodException(type, method);
            MP.RegisterSyncMethod(target);
            Log.Message("[NivarianExpansionCompat] resolved " + type + "." + method);
        }
        static void Patch(Harmony h, string type, string method, string prefix)
        {
            var target = AccessTools.DeclaredMethod(T(type), method) ?? throw new MissingMethodException(type, method);
            h.Patch(target, prefix: new HarmonyMethod(typeof(NivarianExpansions), prefix));
        }
        internal static void Apply(Harmony h)
        {
            if (ModsConfig.IsActive("keeptpa.NivarianDraconiture"))
            {
                Sync("Nivarian.NivarianEngineeringDroneHubComp", "ToggleFactionSetting");
                Sync("Nivarian.NivarianEngineeringDroneHubComp", "ToggleExtinguish");
                Sync(Furniture + "Code.Things.Building.NivarianHydroponicsBasin", "ToggleLamp");
                Sync(Furniture + "Building_ReshapingCasket", "ConfirmReshaping", typeof(List<Hediff>));
                Sync(Furniture + "Building_ReshapingCasket", "CancelReshaping");
            }
            if (!ModsConfig.IsActive("keeptpa.NivarianDraconicMilitary")) return;
            Sync("Nivarian.ThingComAttackerDroneHub", "ToggleAttackPrisoners");
            Sync("Nivarian.ThingComp_UniversalSupportDroneHub", "<CompGetGizmosExtraNivarian>b__7_1");
            Sync(Military + "Code.ThingComp.ThingComp_EnergyShield", "<CompGetGizmosExtraNivarian>b__42_1");
            Sync(Military + "Code.ThingComp.ThingComp_SpiritArtEarring", "set_SuitActive", typeof(bool));
            cone = MP.RegisterSyncMethod(typeof(NivarianExpansions), nameof(ConfirmCone));
            teleport = MP.RegisterSyncMethod(typeof(NivarianExpansions), nameof(Teleport));
            rename = MP.RegisterSyncMethod(typeof(NivarianExpansions), nameof(Rename));
            droneTarget = MP.RegisterSyncMethod(typeof(NivarianExpansions), nameof(TargetDrones));
            wheelchair = MP.RegisterSyncMethod(typeof(NivarianExpansions), nameof(ToggleWheelchair));
            registryType = T(Military + "Code.GameComp.GameComp_TeleporterBeacon");
            Patch(h, "NivarianRace.DraconicMilitary.Building_TurretWinter", "ConfirmConeRotation", nameof(ConePrefix));
            Patch(h, registryType.FullName, "TeleportToBeacon", nameof(TeleportPrefix));
            Patch(h, "Nivarian.Dialog_RenameBeacon", "OnRenamed", nameof(RenamePrefix));
            Patch(h, Military + "Code.ThingComp.ThingComp_HydrazineWheelchair+<>c__DisplayClass26_0", "<CompGetGizmosExtraNivarian>b__1", nameof(WheelchairPrefix));
            foreach (var pair in new[] { new[]{"HoveringAttackerThing", "25"}, new[]{"StrafingAttackerThing", "24"}, new[]{"VectorHoveringAttackerThing", "25"} })
                Patch(h, "NivarianRace.DraconicMilitary." + pair[0] + "+<>c", "<GetGizmos>b__" + pair[1] + "_0", nameof(TargetPrefix));
            foreach (string type in new[]{"HoveringAttackerThing", "VectorHoveringAttackerThing"})
            {
                // The saved movement offset is overwritten by the original SpawnSetup. Derive it from the durable Thing ID on both spawn and load.
                h.Patch(AccessTools.DeclaredMethod(T("NivarianRace.DraconicMilitary." + type), "SpawnSetup"),
                    postfix: new HarmonyMethod(typeof(NivarianExpansions), nameof(HoverOffset)));
            }
            h.Patch(AccessTools.DeclaredMethod(T("NivarianRace.DraconicMilitary.StrafingApproachState"), "OnEnter"),
                transpiler: new HarmonyMethod(typeof(NivarianExpansions), nameof(ReplaceRange)));
            Log.Message("[NivarianExpansionCompat] military targets resolved");
        }
        static bool ConePrefix(Thing __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            if ((bool)Get(__instance, "hasConeFacingPreview"))
            {
                float angle = (float)Get(__instance, "previewConeFacingDegrees");
                // Preview is local; restore its serialized fields before dispatching the complete outcome.
                Set(__instance, "hasConeFacingPreview", false);
                Set(__instance, "previewConeFacingDegrees", Get(__instance, "coneFacingDegrees"));
                cone.DoSync(null, __instance, angle);
            }
            return false;
        }
        static void ConfirmCone(Thing turret, float angle)
        {
            if (turret == null || turret.Destroyed || (int)Get(turret,"coneAdjustmentTicksLeft") > 0 || float.IsNaN(angle) || float.IsInfinity(angle)) return;
            Set(turret, "previewConeFacingDegrees", UnityEngine.Mathf.Repeat(angle, 360f));
            Set(turret, "hasConeFacingPreview", true);
            Call(turret, "ConfirmConeRotation");
        }
        static bool TeleportPrefix(Pawn pawn, object dest)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            if (pawn != null && dest != null) teleport.DoSync(null, pawn, (int)Get(dest,"mapID"), (IntVec3)Get(dest,"beaconPos"));
            return false;
        }
        static object Registry() => Current.Game.components.First(c => c.GetType() == registryType);
        static void Teleport(Pawn pawn, int mapId, IntVec3 position)
        {
            if (pawn == null || pawn.Destroyed) return;
            var registry = Registry();
            foreach (object item in (IEnumerable)AccessTools.Method(registryType,"GetAllBeacons").Invoke(registry,null))
                if ((int)Get(item,"mapID") == mapId && (IntVec3)Get(item,"beaconPos") == position)
                { Call(registry,"TeleportToBeacon",pawn,item); return; }
        }
        static bool RenamePrefix(object __instance, string name)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            rename.DoSync(null, (Thing)Get(__instance,"beacon"), name);
            return false;
        }
        static void Rename(Thing beacon, string name)
        {
            if (beacon == null || beacon.Destroyed || name == null) return;
            Set(beacon,"beaconName",name);
            Call(Registry(),"UpdateBeaconName",beacon);
        }
        static bool WheelchairPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            wheelchair.DoSync(null,(ThingComp)Get(__instance,"<>4__this"));
            return false;
        }
        static void ToggleWheelchair(ThingComp comp)
        {
            if (comp?.parent == null || comp.parent.Destroyed) return;
            bool enabled = !(bool)Get(comp,"_playerWantsOn");
            Set(comp,"_playerWantsOn",enabled);
            if (!enabled)
            {
                var wearer = AccessTools.Property(comp.GetType(),"Wearer").GetValue(comp);
                if (wearer != null) Call(comp,"RemoveBoostHediff",wearer);
                Call(comp,"EndRacingSound");
            }
        }
        static bool TargetPrefix(MethodBase __originalMethod, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            Type type = __originalMethod.DeclaringType.DeclaringType;
            var selected = Find.Selector.SelectedObjects.OfType<Thing>().Where(type.IsInstanceOfType).OrderBy(t=>t.thingIDNumber).ToList();
            if (selected.Count > 0) droneTarget.DoSync(null,selected,target);
            return false;
        }
        static void TargetDrones(List<Thing> drones, LocalTargetInfo target)
        {
            foreach (Thing drone in drones)
            {
                if (drone == null || !drone.Spawned) continue;
                Call(drone,"ClearTask");
                AccessTools.Property(drone.GetType(),"CurTarget").SetValue(drone,target);
                object machine = Get(drone,"stateMachine");
                bool strafing = drone.GetType().Name == "StrafingAttackerThing";
                if (machine != null) Call(machine,"ChangeState",strafing ? "Approach" : "GoTarget");
                if (!strafing) Set(drone,"ManualTarget",true);
            }
        }
        static void HoverOffset(Thing __instance)
        {
            if (!MP.IsInMultiplayer) return;
            Set(__instance,"_randomHoveringRadiusOffset",(float)(new Random(__instance.thingIDNumber).NextDouble()*3-1));
        }
        static IEnumerable<CodeInstruction> ReplaceRange(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(UnityEngine.Random),"Range",new[]{typeof(float),typeof(float)});
            var replacement = AccessTools.Method(typeof(NivarianExpansions),nameof(Range));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original)) { instruction.operand = replacement; count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("StrafingApproachState expected one Unity random call, got " + count);
        }
        static float Range(float min, float max) => MP.IsInMultiplayer ? Rand.Range(min,max) : UnityEngine.Random.Range(min,max);
    }
}
