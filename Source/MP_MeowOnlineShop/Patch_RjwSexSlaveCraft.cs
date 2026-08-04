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
    /// SexSlaveCraft's personality-gel ITab mutates a map component directly
    /// through Assign/Unassign.  Those mutations are not ordered jobs and the
    /// original UI callbacks are compiler-generated, so synchronize the stable
    /// map-component executors rather than the tab or FloatMenu lifecycle.
    /// </summary>
    internal static class Patch_RjwSexSlaveCraft
    {
        private const string PackageId = "rjw.sexslavecraft";
        private const string AssignmentTypeName = "SexSlaveCraft.MapComponent_PersonalityAssignment";
        private const string TrainingTypeName = "SexSlaveCraft.CompSexSlaveTraining";
        private const string TrainingTabTypeName = "SexSlaveCraft.ITab_SexSlaveTraining";

        private static Type _trainingType;
        private static FieldInfo _identityField;
        private static FieldInfo _modeField;
        private static FieldInfo _actField;
        private static FieldInfo _trainerField;
        private static ISyncMethod _syncIdentity;
        private static ISyncMethod _syncMode;
        private static ISyncMethod _syncAct;
        private static ISyncMethod _syncTrainer;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            Type type = AccessTools.TypeByName(AssignmentTypeName);
            MethodInfo assign = type == null ? null : AccessTools.Method(
                type, "Assign", new[] { typeof(Thing), typeof(Pawn) });
            MethodInfo unassign = type == null ? null : AccessTools.Method(
                type, "Unassign", new[] { typeof(Thing) });
            MethodInfo unassignByPawn = type == null ? null : AccessTools.Method(
                type, "UnassignByPawn", new[] { typeof(Pawn) });

            if (assign == null || unassign == null || unassignByPawn == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-SSC] personality-assignment executor signature drift; sync was not installed.");
                return;
            }

            MP.RegisterSyncMethod(assign, null)
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(unassign, null)
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);
            MP.RegisterSyncMethod(unassignByPawn, null)
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            InstallTrainingTabSync(harmony);

            Log.Message("[MP-MeowOnlineShop][RJW-SSC] synchronized personality-gel assignment executors.");
        }

        private static void InstallTrainingTabSync(Harmony harmony)
        {
            _trainingType = AccessTools.TypeByName(TrainingTypeName);
            Type tabType = AccessTools.TypeByName(TrainingTabTypeName);
            _identityField = AccessTools.Field(_trainingType, "pawnIdentity");
            _modeField = AccessTools.Field(_trainingType, "mode");
            _actField = AccessTools.Field(_trainingType, "selectedMode");
            _trainerField = AccessTools.Field(_trainingType, "selectedTrainer");
            MethodInfo fillTab = AccessTools.Method(tabType, "FillTab");
            if (_trainingType == null || _identityField == null || _modeField == null ||
                _actField == null || _trainerField == null || fillTab == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-SSC] training tab signature drift; its direct settings were not synchronized.");
                return;
            }

            _syncIdentity = MP.RegisterSyncMethod(typeof(Patch_RjwSexSlaveCraft), nameof(SetTrainingIdentity))
                .CancelIfAnyArgNull().SetContext(SyncContext.CurrentMap);
            _syncMode = MP.RegisterSyncMethod(typeof(Patch_RjwSexSlaveCraft), nameof(SetTrainingMode))
                .CancelIfAnyArgNull().SetContext(SyncContext.CurrentMap);
            _syncAct = MP.RegisterSyncMethod(typeof(Patch_RjwSexSlaveCraft), nameof(SetTrainingAct))
                .CancelIfAnyArgNull().SetContext(SyncContext.CurrentMap);
            _syncTrainer = MP.RegisterSyncMethod(typeof(Patch_RjwSexSlaveCraft), nameof(SetTrainingTrainer))
                .CancelIfAnyArgNull().SetContext(SyncContext.CurrentMap);

            harmony.Patch(fillTab, transpiler: new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RjwSexSlaveCraft), nameof(TrainingTabTranspiler))));
            foreach (ConstructorInfo ctor in typeof(FloatMenuOption).GetConstructors())
                harmony.Patch(ctor, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(Patch_RjwSexSlaveCraft), nameof(TrainingOptionConstructed))));
            Log.Message("[MP-MeowOnlineShop][RJW-SSC] synchronized sex-slave training settings.");
        }

        // Replaces only direct mode writes in FillTab and FloatMenuOption construction.
        // Menu delegates themselves remain third-party implementation details, so the
        // factory inspects their single component-field store at runtime instead of
        // depending on compiler-generated lambda names.
        private static IEnumerable<CodeInstruction> TrainingTabTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo setMode = AccessTools.Method(typeof(Patch_RjwSexSlaveCraft), nameof(InterceptTrainingModeWrite));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && SameField(instruction.operand as FieldInfo, _modeField))
                    yield return new CodeInstruction(OpCodes.Call, setMode);
                else
                    yield return instruction;
            }
        }

        private static void TrainingOptionConstructed(FloatMenuOption __instance)
        {
            if (__instance != null)
                __instance.action = RewriteTrainingOption(__instance.Label, __instance.action);
        }

        private static Action RewriteTrainingOption(string label, Action original)
        {
            if (!MP.IsInMultiplayer || original == null)
                return original;

            FieldInfo changedField = FindChangedTrainingField(original.Method);
            object component = FindCapturedComponent(original.Target);
            var parent = GetTrainingPawn(component);
            if (changedField == null || parent == null)
                return original;

            if (SameField(changedField, _identityField))
            {
                bool master = label == "ITab_SetAsMaster".Translate();
                return () => _syncIdentity.DoSync(null, parent, master ? 1 : 0);
            }
            if (SameField(changedField, _actField))
            {
                int value = FindCapturedEnumValue(original.Target, _actField.FieldType);
                return () => _syncAct.DoSync(null, parent, value);
            }
            if (SameField(changedField, _trainerField))
            {
                Pawn trainer = FindCapturedPawn(original.Target);
                return () => _syncTrainer.DoSync(null, parent, trainer);
            }
            return original;
        }

        private static void InterceptTrainingModeWrite(object component, int mode)
        {
            Pawn pawn = GetTrainingPawn(component);
            if (pawn == null || _syncMode == null || !MP.IsInMultiplayer)
            {
                _modeField?.SetValue(component, Enum.ToObject(_modeField.FieldType, mode));
                return;
            }
            _syncMode.DoSync(null, pawn, mode);
        }

        private static void SetTrainingIdentity(Pawn pawn, int identity)
        {
            object comp = FindTrainingComponent(pawn);
            if (comp == null)
                return;
            _identityField.SetValue(comp, Enum.ToObject(_identityField.FieldType, identity));
            if (identity == 1)
            {
                _modeField.SetValue(comp, Enum.ToObject(_modeField.FieldType, 0));
                _trainerField.SetValue(comp, null);
                AccessTools.Field(_trainingType, "isBeingTrained")?.SetValue(comp, false);
            }
        }

        private static void SetTrainingMode(Pawn pawn, int mode)
        {
            object comp = FindTrainingComponent(pawn);
            if (comp == null)
                return;
            _modeField.SetValue(comp, Enum.ToObject(_modeField.FieldType, mode));
            AccessTools.Field(_trainingType, "isBeingTrained")?.SetValue(comp, false);
        }

        private static void SetTrainingAct(Pawn pawn, int act)
        {
            object comp = FindTrainingComponent(pawn);
            if (comp != null)
                _actField.SetValue(comp, Enum.ToObject(_actField.FieldType, act));
        }

        private static void SetTrainingTrainer(Pawn pawn, Pawn trainer)
        {
            object comp = FindTrainingComponent(pawn);
            if (comp != null)
                _trainerField.SetValue(comp, trainer);
        }

        private static object FindCapturedComponent(object closure)
        {
            if (closure == null)
                return null;
            foreach (FieldInfo field in closure.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (_trainingType.IsAssignableFrom(field.FieldType))
                    return field.GetValue(closure);
            return null;
        }

        private static Pawn GetTrainingPawn(object component)
        {
            return component == null ? null : AccessTools.Field(_trainingType, "parent")?.GetValue(component) as Pawn;
        }

        private static object FindTrainingComponent(Pawn pawn)
        {
            if (pawn?.AllComps == null)
                return null;
            foreach (ThingComp comp in pawn.AllComps)
                if (comp != null && _trainingType.IsInstanceOfType(comp))
                    return comp;
            return null;
        }

        private static Pawn FindCapturedPawn(object closure)
        {
            if (closure == null)
                return null;
            foreach (FieldInfo field in closure.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (typeof(Pawn).IsAssignableFrom(field.FieldType))
                    return field.GetValue(closure) as Pawn;
            return null;
        }

        private static int FindCapturedEnumValue(object closure, Type enumType)
        {
            if (closure != null)
                foreach (FieldInfo field in closure.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (field.FieldType == enumType)
                        return Convert.ToInt32(field.GetValue(closure));
            return 0;
        }

        private static FieldInfo FindChangedTrainingField(MethodInfo method)
        {
            byte[] il = method?.GetMethodBody()?.GetILAsByteArray();
            if (il == null)
                return null;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != OpCodes.Stfld.Value)
                    continue;
                try
                {
                    FieldInfo field = method.Module.ResolveField(BitConverter.ToInt32(il, i + 1));
                    if (SameField(field, _identityField) || SameField(field, _actField) || SameField(field, _trainerField))
                        return field;
                }
                catch { }
            }
            return null;
        }

        private static bool SameField(FieldInfo left, FieldInfo right)
        {
            return left != null && right != null && left.Module == right.Module && left.MetadataToken == right.MetadataToken;
        }

    }
}
