using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Equal Milking (`akaster.equalmilking.fix`) mutates saved simulation
    /// state from UI callbacks: per-pawn MilkSettings toggles and the
    /// Window_AssignFeeder assigned-feeders list. The stable per-pawn setters
    /// and the feeder window's add/remove actions are rewritten to registered
    /// sync methods; deterministic labor-pushing calls that use the same
    /// extension setters are left untouched.
    /// </summary>
    internal static class Patch_EqualMilkingMp
    {
        private const string PackageId = "akaster.equalmilking.fix";
        private const string CompTypeName = "EqualMilking.CompEquallyMilkable";
        private const string HelperTypeName = "EqualMilking.Helpers.ExtensionHelper";
        private const string WindowTypeName = "EqualMilking.UI.Window_AssignFeeder";
        private const string SettingsModTypeName = "EqualMilking.EqualMilkingMod";
        private const string SettingsTypeName = "EqualMilking.EqualMilkingSettings";
        private const string ExposeAttributeTypeName = "EqualMilking.Expose";

        private static readonly string[] SetterNames =
        {
            "SetAllowMilking",
            "SetAllowMilkingSelf",
            "SetAllowBreastFeeding",
            "SetAllowBreastFeedingAdult",
            "SetAllowToBeFed"
        };

        private static readonly string[] SetterTypeNames =
        {
            "EqualMilking.PawnColumnWorker_MilkAllowManual",
            "EqualMilking.PawnColumnWorker_MilkAllowSelf",
            "EqualMilking.PawnColumnWorker_MilkAllowBreastFeeding",
            "EqualMilking.PawnColumnWorker_MilkAllowBreastFeedingAdult",
            "EqualMilking.PawnColumnWorker_FedBy"
        };

        private static bool _applied;
        private static Type _compType;
        private static Type _windowType;
        private static FieldInfo _assignedFeedersField;
        private static FieldInfo _fedPawnField;
        private static FieldInfo _compField;
        private static MethodInfo _getCompMethod;
        private static MethodInfo[] _setMethods;
        private static Type _settingsModType;
        private static Type _settingsType;
        private static Type _exposeAttributeType;
        private static FieldInfo _settingsField;
        private static MethodInfo _writeExposable;
        private static MethodInfo _readExposable;
        private static MethodInfo _updateSettingsMethod;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                if (!ModsConfig.IsActive(PackageId))
                {
                    Log.Message("[MP-MeowOnlineShop] Equal Milking sync skipped (target mod not active).");
                    return;
                }

                _compType = AccessTools.TypeByName(CompTypeName);
                _windowType = AccessTools.TypeByName(WindowTypeName);
                Type helperType = AccessTools.TypeByName(HelperTypeName);
                _assignedFeedersField = _compType == null ? null : AccessTools.Field(_compType, "assignedFeeders");
                _fedPawnField = _windowType == null ? null : AccessTools.Field(_windowType, "fedPawn");
                _compField = _windowType == null ? null : AccessTools.Field(_windowType, "compEquallyMilkable");
                _getCompMethod = helperType == null
                    ? null
                    : AccessTools.Method(helperType, "CompEquallyMilkable", new[] { typeof(ThingWithComps) });

                _setMethods = new MethodInfo[SetterNames.Length];
                for (int i = 0; i < SetterNames.Length; i++)
                {
                    _setMethods[i] = helperType == null
                        ? null
                        : AccessTools.Method(helperType, SetterNames[i], new[] { typeof(Pawn), typeof(bool) });
                }

                if (_compType == null || _windowType == null || helperType == null ||
                    _assignedFeedersField == null || _fedPawnField == null || _compField == null ||
                    _getCompMethod == null || Array.IndexOf(_setMethods, null) >= 0)
                {
                    Log.Warning("[MP-MeowOnlineShop] Equal Milking target resolution failed; patch skipped.");
                    return;
                }

                MethodInfo syncSetting = AccessTools.Method(
                    typeof(Patch_EqualMilkingMp),
                    nameof(SyncSetMilkSetting),
                    new[] { typeof(Pawn), typeof(int), typeof(bool) });
                MethodInfo syncFeeder = AccessTools.Method(
                    typeof(Patch_EqualMilkingMp),
                    nameof(SyncSetAssignedFeeder),
                    new[] { typeof(Pawn), typeof(Pawn), typeof(bool) });

                if (syncSetting == null || syncFeeder == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Equal Milking sync method resolution failed; patch skipped.");
                    return;
                }

                MP.RegisterSyncMethod(syncSetting, null);
                MP.RegisterSyncMethod(syncFeeder, null);

                MethodInfo setPrefix = AccessTools.Method(
                    typeof(Patch_EqualMilkingMp),
                    nameof(SetValuePrefix));
                if (setPrefix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Equal Milking SetValue prefix not resolved; patch skipped.");
                    return;
                }

                int patchedSetters = 0;
                for (int i = 0; i < SetterTypeNames.Length; i++)
                {
                    Type setterType = AccessTools.TypeByName(SetterTypeNames[i]);
                    MethodInfo setValue = setterType == null
                        ? null
                        : AccessTools.DeclaredMethod(
                            setterType,
                            "SetValue",
                            new[] { typeof(Pawn), typeof(bool), typeof(PawnTable) });
                    if (setValue == null)
                        continue;

                    try
                    {
                        harmony.Patch(setValue, prefix: new HarmonyMethod(setPrefix));
                        patchedSetters++;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Equal Milking SetValue patch failed on " +
                                    SetterTypeNames[i] + ": " + e.Message);
                    }
                }

                MethodInfo drawRow = AccessTools.DeclaredMethod(_windowType, "DrawRow");
                MethodInfo drawPrefix = AccessTools.Method(
                    typeof(Patch_EqualMilkingMp),
                    nameof(DrawRowPrefix));
                MethodInfo drawPostfix = AccessTools.Method(
                    typeof(Patch_EqualMilkingMp),
                    nameof(DrawRowPostfix));

                int patchedWindow = 0;
                if (drawRow != null && drawPrefix != null && drawPostfix != null)
                {
                    try
                    {
                        harmony.Patch(
                            drawRow,
                            prefix: new HarmonyMethod(drawPrefix),
                            postfix: new HarmonyMethod(drawPostfix));
                        patchedWindow = 1;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[MP-MeowOnlineShop] Equal Milking feeder window patch failed: " + e.Message);
                    }
                }

                int settingsSync = TryApplySettingsSync(harmony);

                Log.Message(
                    "[MP-MeowOnlineShop] Equal Milking MP patch active: " +
                    "2 sync methods, setters patched=" + patchedSetters + "/" + SetterTypeNames.Length +
                    ", feeder window patched=" + patchedWindow + "/1" +
                    ", settings snapshot sync=" + settingsSync + "/1.");
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Equal Milking MP compat restore failed: " + e.Message);
            }
        }

        private static int TryApplySettingsSync(Harmony harmony)
        {
            try
            {
                _settingsModType = AccessTools.TypeByName(SettingsModTypeName);
                _settingsType = AccessTools.TypeByName(SettingsTypeName);
                _exposeAttributeType = AccessTools.TypeByName(ExposeAttributeTypeName);
                _settingsField = _settingsModType == null
                    ? null
                    : AccessTools.Field(_settingsModType, "Settings");
                _updateSettingsMethod = _settingsType == null
                    ? null
                    : AccessTools.Method(_settingsType, "UpdateEqualMilkingSettings", Type.EmptyTypes);

                Type scribeUtilType = AccessTools.TypeByName("Multiplayer.Client.ScribeUtil");
                if (scribeUtilType != null)
                {
                    _writeExposable = scribeUtilType
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m =>
                            m.Name == "WriteExposable" && m.GetParameters().Length == 4);
                    _readExposable = scribeUtilType
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m =>
                            m.Name == "ReadExposable" && m.IsGenericMethodDefinition &&
                            m.GetParameters().Length == 2);
                }

                if (_settingsModType == null || _settingsType == null ||
                    _exposeAttributeType == null || _settingsField == null ||
                    _updateSettingsMethod == null || _writeExposable == null ||
                    _readExposable == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] Equal Milking settings snapshot targets not resolved; per-pawn sync remains active.");
                    return 0;
                }

                MethodInfo syncSnapshot = AccessTools.Method(
                    typeof(Patch_EqualMilkingMp),
                    nameof(SyncApplySettingsSnapshot),
                    new[] { typeof(string) });
                if (syncSnapshot == null)
                    return 0;

                MP.RegisterSyncMethod(syncSnapshot, null);

                MethodInfo writeSettings = AccessTools.Method(typeof(Mod), "WriteSettings", Type.EmptyTypes);
                MethodInfo postfix = AccessTools.Method(
                    typeof(Patch_EqualMilkingMp),
                    nameof(WriteSettingsPostfix));
                if (writeSettings == null || postfix == null)
                    return 0;

                harmony.Patch(writeSettings, postfix: new HarmonyMethod(postfix));
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Equal Milking settings snapshot sync failed: " + e.Message);
                return 0;
            }
        }

        private static void WriteSettingsPostfix(Mod __instance)
        {
            if (__instance == null || _settingsModType == null ||
                !_settingsModType.IsInstanceOfType(__instance) ||
                !MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
                _settingsField == null || _writeExposable == null)
            {
                return;
            }

            try
            {
                object settings = _settingsField.GetValue(null);
                if (settings == null)
                    return;

                byte[] data = (byte[])_writeExposable.Invoke(
                    null,
                    new[] { settings, "root", false, null });
                SyncApplySettingsSnapshot(Convert.ToBase64String(data));
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Equal Milking settings snapshot send failed: " + e.Message);
            }
        }

        public static void SyncApplySettingsSnapshot(string xmlBase64)
        {
            if (string.IsNullOrEmpty(xmlBase64) || _settingsType == null ||
                _settingsField == null || _readExposable == null ||
                _updateSettingsMethod == null || _exposeAttributeType == null)
            {
                return;
            }

            try
            {
                byte[] data = Convert.FromBase64String(xmlBase64);
                MethodInfo reader = _readExposable.MakeGenericMethod(_settingsType);
                object loaded = reader.Invoke(null, new object[] { data, null });
                if (loaded == null)
                    return;

                object current = _settingsField.GetValue(null);
                if (current == null)
                    return;

                CopyExposedSettings(loaded, current);
                _updateSettingsMethod.Invoke(current, null);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Equal Milking settings replay failed: " + e.Message);
            }
        }

        private static void CopyExposedSettings(object source, object target)
        {
            FieldInfo[] fields = _settingsType.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsDefined(_exposeAttributeType, false))
                    field.SetValue(target, field.GetValue(source));
            }
        }

        public static void SyncSetMilkSetting(Pawn pawn, int kind, bool value)
        {
            if (pawn == null || kind < 0 || kind >= _setMethods.Length)
                return;

            MethodInfo setter = _setMethods[kind];
            if (setter == null)
                return;

            try
            {
                setter.Invoke(null, new object[] { pawn, value });
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Equal Milking setting replay failed: " + e.Message);
            }
        }

        public static void SyncSetAssignedFeeder(Pawn fedPawn, Pawn feeder, bool assigned)
        {
            if (fedPawn == null || feeder == null)
                return;

            IList list = GetAssignedList(fedPawn);
            if (list == null)
                return;

            if (assigned)
            {
                if (!list.Contains(feeder))
                    list.Add(feeder);
            }
            else
            {
                list.Remove(feeder);
            }
        }

        private static bool SetValuePrefix(
            Pawn pawn,
            bool value,
            PawnTable table,
            MethodBase __originalMethod)
        {
            if (pawn == null || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;

            int kind = KindForMethod(__originalMethod?.DeclaringType);
            if (kind < 0)
                return true;

            SyncSetMilkSetting(pawn, kind, value);
            return false;
        }

        private static void DrawRowPrefix(object __instance, ref string __state)
        {
            if (__instance == null || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return;

            object comp = _compField?.GetValue(__instance);
            __state = SerializeFeeders(comp);
        }

        private static void DrawRowPostfix(object __instance, Pawn pawn, bool assigned, string __state)
        {
            if (__instance == null || pawn == null || string.IsNullOrEmpty(__state) ||
                !MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
            {
                return;
            }

            object comp = _compField?.GetValue(__instance);
            string current = SerializeFeeders(comp);
            if (current == __state)
                return;

            RestoreFeeders(comp, __state);

            Pawn fedPawn = _fedPawnField?.GetValue(__instance) as Pawn;
            if (fedPawn == null)
                return;

            SyncSetAssignedFeeder(fedPawn, pawn, assigned);
        }

        private static int KindForMethod(Type type)
        {
            if (type == null)
                return -1;

            for (int i = 0; i < SetterTypeNames.Length; i++)
            {
                if (type.FullName == SetterTypeNames[i])
                    return i;
            }

            return -1;
        }

        private static IList GetAssignedList(Pawn pawn)
        {
            if (pawn == null || _getCompMethod == null || _assignedFeedersField == null)
                return null;

            object comp = _getCompMethod.Invoke(null, new object[] { pawn });
            if (comp == null)
                return null;

            return _assignedFeedersField.GetValue(comp) as IList;
        }

        private static string SerializeFeeders(object comp)
        {
            if (comp == null || _assignedFeedersField == null)
                return "";

            IList list = _assignedFeedersField.GetValue(comp) as IList;
            if (list == null || list.Count == 0)
                return "";

            var ids = new List<int>();
            foreach (object item in list)
            {
                if (item is Pawn pawn)
                    ids.Add(pawn.thingIDNumber);
            }

            ids.Sort();
            return string.Join(",", ids);
        }

        private static void RestoreFeeders(object comp, string state)
        {
            if (comp == null || _assignedFeedersField == null)
                return;

            IList list = _assignedFeedersField.GetValue(comp) as IList;
            if (list == null)
                return;

            list.Clear();
            if (string.IsNullOrEmpty(state))
                return;

            string[] parts = state.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int id))
                {
                    Pawn pawn = FindPawnById(id);
                    if (pawn != null)
                        list.Add(pawn);
                }
            }
        }

        private static Pawn FindPawnById(int pawnId)
        {
            if (Find.Maps != null)
            {
                for (int m = 0; m < Find.Maps.Count; m++)
                {
                    Map map = Find.Maps[m];
                    IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
                    if (pawns == null)
                        continue;

                    for (int i = 0; i < pawns.Count; i++)
                    {
                        if (pawns[i] != null && pawns[i].thingIDNumber == pawnId)
                            return pawns[i];
                    }
                }
            }

            if (Find.WorldPawns != null)
            {
                List<Pawn> worldPawns = Find.WorldPawns.AllPawnsAliveOrDead;
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    if (worldPawns[i] != null && worldPawns[i].thingIDNumber == pawnId)
                        return worldPawns[i];
                }
            }

            return null;
        }
    }
}
