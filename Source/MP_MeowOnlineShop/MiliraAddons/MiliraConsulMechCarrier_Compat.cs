using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat
{
    /// <summary>
    /// The Milira Consul ("BishopIII") inherits both state-changing gizmos
    /// from AncotLibrary.CompMechCarrier_Custom. Multiplayer registers the
    /// vanilla CompMechCarrier callbacks, but those registrations do not
    /// cover the custom carrier's declaring type. The release callback was
    /// previously registered here; the recover callback must also be
    /// registered because it creates resources, destroys the stored pawns,
    /// clears spawnedPawns, and starts the recovery cooldown.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MiliraConsulMechCarrier_Compat
    {
        private const string PackageId = "ancot.milirarace";
        private const string CarrierTypeName =
            "AncotLibrary.CompMechCarrier_Custom";
        private const string ConsulCarrierTypeName =
            "Milira.CompMechCarrier_Consul";
        private const string TrySpawnPawnsName = "TrySpawnPawns";
        private const string CompGetGizmosExtraName = "CompGetGizmosExtra";
        private const int RecoverLambdaOrdinal = 3;

        private static bool _initialized;
        private static Type _consulType;
        private static ISyncField _maxToFill;
        private static readonly Dictionary<MethodBase, FieldInfo> GizmoCarriers =
            new Dictionary<MethodBase, FieldInfo>();

        static MiliraConsulMechCarrier_Compat()
        {
        if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
            if (MiliraMpCompatGate.ReferenceModActive)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "skipped: usamiseika.fixmod.miliramultiplayer is active.");
                return;
            }

            if (!MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            try
            {
                Initialize();
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "failed: " + e);
            }
        }

        private static void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;

            Type carrierType = AccessTools.TypeByName(CarrierTypeName);
            Type consulType = AccessTools.TypeByName(ConsulCarrierTypeName);
            if (carrierType == null || consulType == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "skipped: carrier/consul types were not resolved " +
                    $"(carrier={carrierType != null}, consul={consulType != null}).");
                return;
            }

            MethodInfo trySpawnPawns = AccessTools.Method(
                carrierType,
                TrySpawnPawnsName,
                Type.EmptyTypes);
            if (trySpawnPawns == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "skipped: TrySpawnPawns was not found.");
                return;
            }

            MethodInfo compGetGizmosExtra = AccessTools.DeclaredMethod(
                carrierType,
                CompGetGizmosExtraName,
                Type.EmptyTypes);
            if (compGetGizmosExtra == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "warning: custom carrier CompGetGizmosExtra was not found; " +
                    "recovery callback cannot be registered.");
            }

            bool releaseRegistered = false;
            bool recoverRegistered = false;
            try
            {
                MP.RegisterSyncMethod(trySpawnPawns, (SyncType[])null);
                releaseRegistered = true;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                    "release registration failed: " + e.Message);
            }

            if (compGetGizmosExtra != null)
            {
                try
                {
                    // In the current Ancot 1.6 assembly, lambda 3 is the
                    // recovery action (<CompGetGizmosExtra>b__31_3). It is
                    // the same callback MP calls "Empty" for vanilla carriers.
                    MP.RegisterSyncMethodLambda(
                        carrierType,
                        CompGetGizmosExtraName,
                        RecoverLambdaOrdinal);
                    recoverRegistered = true;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Milira Consul mech-carrier sync " +
                        "recovery registration failed: " + e.Message);
                }
            }

            RegisterStoragePreset(consulType);

            Log.Message(
                "[MP-MeowOnlineShop] Milira Consul mech-carrier sync active: " +
                "release=" + releaseRegistered + ", recovery=" +
                recoverRegistered + " (CompGetGizmosExtra lambda " +
                RecoverLambdaOrdinal + ").");
        }

        private static void RegisterStoragePreset(Type consulType)
        {
            Type storageType = AccessTools.TypeByName("AncotLibrary.CompThingCarrier_Custom");
            FieldInfo maximum = storageType == null ? null : AccessTools.Field(storageType, "maxToFill");
            if (maximum == null || maximum.FieldType != typeof(int))
                throw new MissingFieldException("AncotLibrary.CompThingCarrier_Custom", "maxToFill");

            // Current Ancot uses ThingCarrierGizmo through the inherited base
            // iterator. Also cover its retained MechCarrierGizmo_Custom path.
            foreach (string name in new[] { "AncotLibrary.ThingCarrierGizmo", "AncotLibrary.MechCarrierGizmo_Custom" })
            {
                Type gizmoType = AccessTools.TypeByName(name);
                FieldInfo carrier = gizmoType == null ? null : AccessTools.Field(gizmoType, "carrier");
                MethodInfo draw = gizmoType == null ? null : AccessTools.DeclaredMethod(gizmoType,
                    "GizmoOnGUI", new[] { typeof(UnityEngine.Vector2), typeof(float), typeof(GizmoRenderParms) });
                if (carrier == null || draw == null || !storageType.IsAssignableFrom(carrier.FieldType))
                    throw new MissingMemberException(name + ": required storage gizmo targets changed");
                GizmoCarriers.Add(draw, carrier);
            }

            _consulType = consulType;
            _maxToFill = MP.RegisterSyncField(maximum.DeclaringType, maximum.Name).SetBufferChanges();
            var harmony = new Harmony("mp.meowonlineshop.miliraconsulstorage");
            foreach (MethodBase draw in GizmoCarriers.Keys)
                harmony.Patch(draw,
                    prefix: new HarmonyMethod(typeof(MiliraConsulMechCarrier_Compat), nameof(BeforeStorageGizmo)),
                    finalizer: new HarmonyMethod(typeof(MiliraConsulMechCarrier_Compat), nameof(AfterStorageGizmo)));
            Log.Message("[MP-MeowOnlineShop] Milira Consul storage preset sync active: maxToFill, gizmos=" + GizmoCarriers.Count);
        }

        private static void BeforeStorageGizmo(object __instance, MethodBase __originalMethod, ref bool __state)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface)
                return;
            object carrier = GizmoCarriers[__originalMethod].GetValue(__instance);
            if (!_consulType.IsInstanceOfType(carrier))
                return;

            MP.WatchBegin();
            __state = true;
            // Watch the actual component, not the ephemeral gizmo. MP restores
            // simulation state after drawing and sends the buffered integer;
            // targetValue/draggingBar remain local presentation state.
            _maxToFill.Watch(carrier);
        }

        private static void AfterStorageGizmo(bool __state)
        {
            // A finalizer also balances the watch scope if drawing throws.
            if (__state)
                MP.WatchEnd();
        }
    }
}
