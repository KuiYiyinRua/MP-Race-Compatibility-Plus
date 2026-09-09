using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Blueprints Forked stores its blueprint templates in a WorldComponent
    /// (`BlueprintController`). Rotate/Flip gizmo and key actions call
    /// Blueprint.Rotate/Flip directly, which mutates the saved template.
    /// A Blueprint SyncWorker (by name) lets us register those two instance
    /// methods as sync methods.
    /// </summary>
    internal static class Patch_BlueprintsMp
    {
        private const string PackageId = "Defi.Blueprints.fork";
        private const string BlueprintTypeName = "Blueprints.Blueprint";
        private const string ControllerTypeName = "Blueprints.BlueprintController";

        private static bool _applied;
        private static FieldInfo _nameField;
        private static MethodInfo _findBlueprintMethod;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Blueprints sync skipped (target mod not active).");
                    return;
                }

                Type blueprintType = AccessTools.TypeByName(BlueprintTypeName);
                Type controllerType = AccessTools.TypeByName(ControllerTypeName);
                _nameField = blueprintType == null ? null : AccessTools.Field(blueprintType, "name");
                _findBlueprintMethod = controllerType == null
                    ? null
                    : AccessTools.Method(controllerType, "FindBlueprint", new[] { typeof(string) });

                if (blueprintType == null || controllerType == null ||
                    _nameField == null || _findBlueprintMethod == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Blueprints target resolution failed; patch skipped.");
                    return;
                }

                try
                {
                    MP.RegisterSyncWorker<object>(
                        BlueprintSyncer,
                        blueprintType,
                        false,
                        false);
                }
                catch (Exception e)
                {
                    Log.Warning("[MP-MeowOnlineShop] Blueprints SyncWorker registration failed: " + e.Message);
                    return;
                }

                int registered = 0;
                registered += TryRegister(
                    AccessTools.Method(blueprintType, "Rotate", new[] { typeof(RotationDirection) }));
                registered += TryRegister(
                    AccessTools.Method(blueprintType, "Flip", Type.EmptyTypes));

                if (registered == 0)
                    Log.Warning("[MP-MeowOnlineShop] Blueprints sync registration failed; patch skipped.");
                else
                    Log.Message("[MP-MeowOnlineShop] Blueprints MP patch active: 1 SyncWorker, " + registered + " sync methods.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Blueprints MP compat restore failed: " + e.Message);
            }
        }

        private static int TryRegister(MethodInfo method)
        {
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Blueprints sync register failed on " + method.Name + ": " + e.Message);
                return 0;
            }
        }

        private static void BlueprintSyncer(SyncWorker sync, ref object inst)
        {
            if (sync.isWriting)
            {
                string name = inst == null ? null : (string)_nameField.GetValue(inst);
                sync.Bind(ref name);
                return;
            }

            string blueprintName = null;
            sync.Bind(ref blueprintName);
            inst = _findBlueprintMethod.Invoke(null, new object[] { blueprintName });
        }
    }
}
