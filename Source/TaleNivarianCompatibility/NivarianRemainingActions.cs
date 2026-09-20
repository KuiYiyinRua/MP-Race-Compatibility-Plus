using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianRemainingActions
    {
        static Type moduleComp;
        static FieldInfo installedWorkers, moduleDef, moduleEnabled;
        static ISyncMethod moduleCharging;
        internal static void Apply(Harmony harmony)
        {
            Sync("Nivarian_Race.Code.Comps.BuildingComps.CompUniversalContainer", "PopOutThings", typeof(IntVec3), typeof(Map));
            // Only the developer UI callbacks are synchronized; their shared tick
            // executors retain ordinary simulation behavior and permissions.
            foreach (var target in new[] {
                new[] { "Nivarian_Race.Code.Comps.BuildingComps.CompArchiveTerminal", "<CompGetGizmosExtra>b__15_1", "<CompGetGizmosExtra>b__15_2" },
                new[] { "Nivarian_Race.Code.Comps.BuildingComps.CompColonistPowerCollector", "<CompGetGizmosExtra>b__17_0" },
                new[] { "Nivarian_Race.Code.Comps.ThingComps.ThingComp_DragonEggContainer", "<CompGetGizmosExtra>b__18_0" }
            })
            {
                var type = AccessTools.TypeByName(target[0]) ?? throw new TypeLoadException(target[0]);
                for (int i = 1; i < target.Length; i++)
                    MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, target[i], Type.EmptyTypes)
                        ?? throw new MissingMethodException(type.FullName, target[i])).SetDebugOnly();
            }
            var story = AccessTools.TypeByName("Nivarian.GameComp_NivarianMainStory")
                ?? throw new TypeLoadException("Nivarian main story");
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(story, "GodModeSetNextMechRaidTickTo300", Type.EmptyTypes)
                ?? throw new MissingMethodException(story.FullName, "GodModeSetNextMechRaidTickTo300")).SetDebugOnly();
            var beacon = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.Comp_ContiniumBeacon")
                ?? throw new TypeLoadException("Nivarian continium beacon");
            foreach (var action in new[] { "<CompGetGizmosExtra>b__24_1", "<CompGetGizmosExtra>b__24_2" })
                MP.RegisterSyncMethod(AccessTools.DeclaredMethod(beacon, action, Type.EmptyTypes)
                    ?? throw new MissingMethodException(beacon.FullName, action)).SetDebugOnly();
            // These methods are referenced exclusively by native god-mode gizmos.
            // Synchronize the native executors so material IDs and saved progress agree.
            foreach (var name in new[] { "Nivarian_Race.Code.ShuttleUpgradeSystem.Comp_ShuttleUpgrade", "Nivarian_Race.Code.Comps.BuildingComps.CompNiraControlCenter" })
            {
                var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
                foreach (var action in new[] { "DebugSetProgressTo99", "SpawnMissingMaterials" })
                    MP.RegisterSyncMethod(AccessTools.DeclaredMethod(type, action, Type.EmptyTypes)
                        ?? throw new MissingMethodException(name, action)).SetDebugOnly();
            }
            var tuning = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompTuneableMachines")
                ?? throw new TypeLoadException("Nivarian machine tuning");
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(tuning, "<CompGetGizmosExtra>b__26_1", Type.EmptyTypes)
                ?? throw new MissingMethodException(tuning.FullName, "native debug efficiency action")).SetDebugOnly();
            // Native automatic building still runs in simulation; only interface calls
            // become commands, and MP enforces the same debug gate as vanilla dev gizmos.
            var selfBuilding = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.CompSelfBuilding")
                ?? throw new TypeLoadException("Nivarian self-building component");
            var forceStart = AccessTools.DeclaredMethod(selfBuilding, "StartBuild", Type.EmptyTypes)
                ?? throw new MissingMethodException(selfBuilding.FullName, "StartBuild");
            MP.RegisterSyncMethod(forceStart).SetDebugOnly();
            Sync("Nivarian_Race.Code.Comps.ThingComps.Comp_NivarianWirelessMechEnergyReceiver", "SetWirelessCharging", typeof(bool));
            Sync("Nivarian_Race.Code.Comps.ThingComps.Comp_AdjustableStocking", "SetDenier", typeof(int));
            Sync("Nivarian_Race.Code.MechModuleSystem.Nira_MechUnitCommon", "set_GlowColor", typeof(Color));
            Sync("Nivarian_Race.Code.MechModuleSystem.Nira_MechUnitCommon", "set_RainbowGlow", typeof(bool));
            var worker = AccessTools.TypeByName("Nivarian_Race.Code.MechModuleSystem.ModuleWorker_WirelessCharging") ?? throw new TypeLoadException("Nira wireless module");
            moduleComp = AccessTools.TypeByName("Nivarian_Race.Code.MechModuleSystem.Comp_MechModule") ?? throw new TypeLoadException("Nira module component");
            installedWorkers = AccessTools.Field(moduleComp, "installedWorkers") ?? throw new MissingFieldException("installedWorkers");
            moduleDef = AccessTools.Field(worker, "def") ?? throw new MissingFieldException("ModuleWorker.def");
            moduleEnabled = AccessTools.Field(worker, "_enabled") ?? throw new MissingFieldException("ModuleWorker_WirelessCharging._enabled");
            moduleCharging = MP.RegisterSyncMethod(typeof(NivarianRemainingActions), nameof(SetModuleCharging));
            harmony.Patch(AccessTools.DeclaredMethod(worker, "GetGizmos", new[] { typeof(Pawn) }),
                postfix: new HarmonyMethod(typeof(NivarianRemainingActions), nameof(ModuleGizmos)));
            Log.Message("[TaleNivarianCompat] container eject, mech/module charging, stocking and Nira appearance executors synchronized=6.");
        }

        static void ModuleGizmos(object __instance, Pawn pawn, ref IEnumerable<Gizmo> __result)
        {
            if (MP.IsInMultiplayer && MP.InInterface) __result = WrapModule(__result, __instance, pawn);
        }
        static IEnumerable<Gizmo> WrapModule(IEnumerable<Gizmo> gizmos, object worker, Pawn pawn)
        {
            foreach (var gizmo in gizmos)
            {
                if (gizmo is Command_Toggle toggle)
                    toggle.toggleAction = () => moduleCharging.DoSync(null, pawn, (Def)moduleDef.GetValue(worker), !(bool)moduleEnabled.GetValue(worker));
                yield return gizmo;
            }
        }
        static void SetModuleCharging(Pawn pawn, Def def, bool value)
        {
            if (pawn == null || pawn.Destroyed || def == null) return;
            foreach (var comp in pawn.AllComps)
                if (moduleComp.IsInstanceOfType(comp))
                    foreach (var worker in (IEnumerable)installedWorkers.GetValue(comp))
                        if (worker != null && moduleEnabled.DeclaringType.IsInstanceOfType(worker) && ReferenceEquals(moduleDef.GetValue(worker), def))
                        { moduleEnabled.SetValue(worker, value); return; }
        }

        static void Sync(string name, string method, params Type[] args)
        {
            var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
            var target = AccessTools.DeclaredMethod(type, method, args) ?? throw new MissingMethodException(name, method);
            MP.RegisterSyncMethod(target);
        }
    }
}
