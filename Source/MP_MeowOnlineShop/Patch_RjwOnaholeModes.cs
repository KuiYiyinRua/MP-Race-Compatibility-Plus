using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RJW Onahole creates its mode pickers from a generic Command subclass.
    /// The picker callbacks write component fields directly, bypassing both
    /// RimWorld's ordered-job command and MP's standard gizmo handlers.  Replace
    /// only the three simulation-relevant mode callbacks with one command that
    /// resolves the parent Thing and applies the same original setter on every
    /// peer.  The configuration windows and all purely graphical selectors stay
    /// local.
    /// </summary>
    internal static class Patch_RjwOnaholeModes
    {
        private const string PackageId = "rim.job.world.onahole.ext";
        private const string MilkingGizmoTypeName = "RJW_Onahole.UI.MilkingGizmo";
        private const string ElectrocuteGizmoTypeName = "RJW_Onahole.UI.ElectrocuteGizmo";
        private const string MilkingCompTypeName = "RJW_Onahole.Comps.CompMilkingMachine";
        private const string ElectrocuteCompTypeName = "RJW_Onahole.Comps.CompElectrocutePowerSupply";

        // The values are intentionally independent of the third-party enums.
        private const int MilkingMode = 0;
        private const int BreastElectrocuteMode = 1;
        private const int GenitalElectrocuteMode = 2;

        private static Type _milkingCompType;
        private static Type _electrocuteCompType;
        private static FieldInfo _milkingModeField;
        private static MethodInfo _milkingNotify;
        private static MethodInfo _setBreastMode;
        private static MethodInfo _setGenitalMode;
        private static ISyncMethod _syncSetMode;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            _milkingCompType = AccessTools.TypeByName(MilkingCompTypeName);
            _electrocuteCompType = AccessTools.TypeByName(ElectrocuteCompTypeName);
            _milkingModeField = AccessTools.Field(_milkingCompType, "MilkingMode");
            _milkingNotify = AccessTools.Method(_milkingCompType, "Notify_EnableStatusChanged");
            _setBreastMode = AccessTools.PropertySetter(_electrocuteCompType, "BreastElectrocuteMode");
            _setGenitalMode = AccessTools.PropertySetter(_electrocuteCompType, "GenitalElectrocuteMode");

            if (_milkingCompType == null || _electrocuteCompType == null ||
                _milkingModeField == null || _milkingNotify == null ||
                _setBreastMode == null || _setGenitalMode == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Onahole] mode setter signature drift; mode sync was not installed.");
                return;
            }

            _syncSetMode = MP.RegisterSyncMethod(
                    typeof(Patch_RjwOnaholeModes),
                    nameof(SetMode))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            int patched = 0;
            patched += PatchPicker(harmony, AccessTools.TypeByName(MilkingGizmoTypeName));
            patched += PatchPicker(harmony, AccessTools.TypeByName(ElectrocuteGizmoTypeName));
            if (patched != 2)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Onahole] expected two generic mode pickers, " +
                    "resolved " + patched + ". Their local callbacks were left untouched.");
                return;
            }

            Log.Message("[MP-MeowOnlineShop][RJW-Onahole] synchronized milking and electrode mode pickers.");
        }

        private static int PatchPicker(Harmony harmony, Type gizmoType)
        {
            // Harmony rejects a MethodInfo reflected through a derived type.  It
            // requires the declared method, whose ReflectedType and DeclaringType
            // are both the closed generic base.  MilkingGizmo also has an
            // intermediate generic base, so walk up until the declaration exists.
            MethodInfo target = null;
            for (Type current = gizmoType; current != null; current = current.BaseType)
            {
                MethodInfo candidate = current.GetMethod(
                    "FloatMenuOption",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (candidate == null || candidate.ReturnType != typeof(FloatMenuOption) ||
                    candidate.GetParameters().Length != 1)
                    continue;
                target = candidate;
                break;
            }
            if (target == null)
                return 0;

            try
            {
                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(AccessTools.Method(
                        typeof(Patch_RjwOnaholeModes), nameof(ModeOptionPostfix))));
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Onahole] mode picker patch failed: " + e.Message);
                return 0;
            }
            return 1;
        }

        private static void ModeOptionPostfix(object __instance, object mode, ref FloatMenuOption __result)
        {
            if (__instance == null || mode == null || __result == null || _syncSetMode == null)
                return;

            FieldInfo setterField = AccessTools.Field(__instance.GetType(), "setMode");
            var setter = setterField?.GetValue(__instance) as Delegate;
            var component = setter?.Target as ThingComp;
            int kind = ResolveModeKind(component, setter?.Method);
            if (component?.parent == null || kind < 0)
                return;

            int value;
            try
            {
                value = Convert.ToInt32(mode);
            }
            catch
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Onahole] could not serialize a mode-picker enum value.");
                return;
            }

            Thing parent = component.parent;
            __result.action = () => _syncSetMode.DoSync(null, parent, kind, value);
        }

        private static int ResolveModeKind(ThingComp component, MethodInfo setter)
        {
            if (component == null)
                return -1;
            if (_milkingCompType.IsInstanceOfType(component))
                return MilkingMode;
            if (!_electrocuteCompType.IsInstanceOfType(component) || setter == null)
                return -1;

            // Both electrode gizmos capture the same component.  Inspect the
            // source-generated callback for its actual property setter instead of
            // relying on compiler lambda ordinal or translated gizmo labels.
            foreach (MethodBase called in CalledMethods(setter))
            {
                if (SameMethod(called, _setBreastMode))
                    return BreastElectrocuteMode;
                if (SameMethod(called, _setGenitalMode))
                    return GenitalElectrocuteMode;
            }

            Log.Warning("[MP-MeowOnlineShop][RJW-Onahole] electrode picker callback did not resolve its target setter.");
            return -1;
        }

        private static void SetMode(Thing parent, int kind, int value)
        {
            var holder = parent as ThingWithComps;
            if (holder == null)
                return;

            ThingComp component = null;
            Type expected = kind == MilkingMode ? _milkingCompType : _electrocuteCompType;
            foreach (ThingComp candidate in holder.AllComps)
            {
                if (candidate != null && expected.IsInstanceOfType(candidate))
                {
                    component = candidate;
                    break;
                }
            }
            if (component == null)
                return;

            if (kind == MilkingMode)
            {
                object enumValue = Enum.ToObject(_milkingModeField.FieldType, value);
                _milkingModeField.SetValue(component, enumValue);
                _milkingNotify.Invoke(component, null);
            }
            else if (kind == BreastElectrocuteMode)
            {
                _setBreastMode.Invoke(component, new[] { Enum.ToObject(_setBreastMode.GetParameters()[0].ParameterType, value) });
            }
            else if (kind == GenitalElectrocuteMode)
            {
                _setGenitalMode.Invoke(component, new[] { Enum.ToObject(_setGenitalMode.GetParameters()[0].ParameterType, value) });
            }
        }

        private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
        {
            byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
            if (bytes == null)
                yield break;

            for (int i = 0; i + 4 < bytes.Length; i++)
            {
                byte opcode = bytes[i];
                if (opcode != OpCodes.Call.Value && opcode != OpCodes.Callvirt.Value)
                    continue;
                MethodBase called = null;
                try
                {
                    called = method.Module.ResolveMethod(
                        BitConverter.ToInt32(bytes, i + 1),
                        GetTypeArguments(method),
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch
                {
                    // A byte in an unrelated operand can look like a call opcode.
                }
                if (called != null)
                    yield return called;
            }
        }

        private static Type[] GetTypeArguments(MethodBase method)
        {
            return method.DeclaringType != null && method.DeclaringType.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : null;
        }

        private static bool SameMethod(MethodBase left, MethodBase right)
        {
            return left != null && right != null && left.Module == right.Module &&
                   left.MetadataToken == right.MetadataToken;
        }
    }
}
