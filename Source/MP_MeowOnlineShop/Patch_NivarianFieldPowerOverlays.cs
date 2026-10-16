using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop
{
    // NMC can mask PowerOn while a saved grid lease has no reconstructed PowerNet.
    // PostSpawnSetup then caches NeedsPower. When the network becomes available,
    // raw powerOnInt is already true, so the native setter emits no transition and
    // never refreshes that icon. Reconcile presentation from the logical state in
    // MapComponentUpdate; never write PowerOn, emit signals, or rebuild a network.
    [StaticConstructorOnStartup]
    internal static class Patch_NivarianFieldPowerOverlays
    {
        private sealed class FrameGate { public int nextFrame; }
        private static readonly ConditionalWeakTable<Map, FrameGate> frames =
            new ConditionalWeakTable<Map, FrameGate>();
        private static FieldInfo gridLeases, directLeases, leaseTarget;
        private static AccessTools.FieldRef<CompPowerTrader, OverlayHandle?> needsPower;
        private static AccessTools.FieldRef<CompPowerTrader, OverlayHandle?> powerOff;
        private static Action<CompPowerTrader> updateOverlays;

        static Patch_NivarianFieldPowerOverlays()
        {
            if (!CompatibilityPatchCategories.IsEnabled("visual") ||
                !ModsConfig.IsActive("rwmt.Multiplayer") ||
                !ModsConfig.IsActive("local.nivarian.mothership.commander")) return;
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        private static void Install()
        {
            try
            {
                var energy = AccessTools.TypeByName("NivarianCommander.FieldEnergy");
                var lease = AccessTools.TypeByName("NivarianCommander.FieldPowerLease");
                var target = AccessTools.DeclaredMethod(energy, "MapComponentUpdate");
                gridLeases = AccessTools.Field(energy, "gridLeases");
                directLeases = AccessTools.Field(energy, "leases");
                leaseTarget = AccessTools.Field(lease, "target");
                if (target == null || gridLeases == null || directLeases == null || leaseTarget == null ||
                    !typeof(MapComponent).IsAssignableFrom(energy))
                    throw new MissingMemberException("NMC field power overlay targets changed");
                needsPower = AccessTools.FieldRefAccess<CompPowerTrader, OverlayHandle?>("overlayNeedsPower");
                powerOff = AccessTools.FieldRefAccess<CompPowerTrader, OverlayHandle?>("overlayPowerOff");
                updateOverlays = AccessTools.MethodDelegate<Action<CompPowerTrader>>(
                    AccessTools.DeclaredMethod(typeof(CompPowerTrader), "UpdateOverlays"));
                new Harmony("mp.meowonlineshop.nivarian-field-power-overlays").Patch(target,
                    postfix: new HarmonyMethod(typeof(Patch_NivarianFieldPowerOverlays), nameof(RefreshPresentation)));
                Log.Message("[MP-MeowOnlineShop] NMC field power overlays: logical state reconciled in map presentation; power, callbacks and network state unchanged.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop] NMC field power overlay REQUIRED_TARGET_FAILED: " + e);
            }
        }

        private static void RefreshPresentation(MapComponent __instance)
        {
            if (!MP.IsInMultiplayer || Current.ProgramState != ProgramState.Playing) return;
            var map = __instance.map;
            if (map == null) return;
            var gate = frames.GetValue(map, _ => new FrameGate());
            int frame = Time.frameCount;
            if (frame < gate.nextFrame) return;
            gate.nextFrame = frame + 30;
            if (gridLeases.GetValue(__instance) is IList grid)
                for (int i = 0; i < grid.Count; i++) RefreshThing(grid[i] as ThingWithComps, map);
            if (directLeases.GetValue(__instance) is IList direct)
                for (int i = 0; i < direct.Count; i++)
                    if (direct[i] != null) RefreshThing(leaseTarget.GetValue(direct[i]) as ThingWithComps, map);
        }

        private static void RefreshThing(ThingWithComps thing, Map map)
        {
            if (thing == null || !thing.Spawned || thing.Map != map) return;
            var power = thing.GetComp<CompPowerTrader>();
            if (power == null) return;
            bool broken = thing.IsBrokenDown();
            bool off = !broken && thing.GetComp<CompFlickable>()?.SwitchIsOn == false;
            bool needs = !broken && !off && power.Props.showPowerNeededIfOff &&
                FlickUtility.WantsToBeOn(thing) && !power.PowerOn;
            if (needsPower(power).HasValue != needs || powerOff(power).HasValue != off)
                updateOverlays(power);
        }
    }
}
