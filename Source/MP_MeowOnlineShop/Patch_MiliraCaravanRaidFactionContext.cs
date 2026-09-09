using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-243/244 and Desync-587: The Tale of Milira's ruin-outpost and
    /// church-battlefield raid outcomes queue a
    /// LongEvent whose body calls FactionManager.RandomEnemyFaction() and
    /// PawnGroupMakerUtility.GeneratePawns(). In a multifaction session the
    /// first call evaluates Faction.OfPlayer on each peer, and the second runs
    /// Milira/Milian PawnGenerator postfixes that read Find.CurrentMap. A host
    /// and a client can therefore select a different enemy faction and generate
    /// different raider pawns before the temporary caravan-attack map exists.
    ///
    /// Installed source authority:
    /// Pakerwot.MiliraEventandStortExpandTheTaleofMilira workshop 3477405110;
    /// 1.6 TheTaleofMilira.dll SHA-256 9DF447A2249A81BA1A31C854401E8147A6BE85FA1F6303418CB115C960FBB589.
    ///
    /// Fix: run the generated Outcome_Raid lambda under the caravan's player
    /// faction and a stable home map. The same faction-context pattern is
    /// already used for vanilla CaravanArrivalAction_Enter, but the custom
    /// Milira arrival actions bypass that path. Singleplayer is untouched.
    /// </summary>
    internal static class Patch_MiliraCaravanRaidFactionContext
    {
        private const string LogTag = "[MP-MeowOnlineShop] MiliraCaravanRaid";

        private static readonly string[] RaidTypeNames =
        {
            "TheTaleofMilira.TaleOfMilira_TheRuinOutpost",
            "TheTaleofMilira.TaleOfMilira_TheRuinOutpost_m",
            "TheTaleofMilira.TaleOfMilira_TheBattlefield",
            "TheTaleofMilira.TaleOfMilira_TheBattlefield_m"
        };

        private static FieldInfo _ofPlayerField;
        private static FieldInfo _currentMapIndexField;
        private static bool _applied;
        private static bool _loggedActive;

        private sealed class ScopeState
        {
            internal bool FactionActive;
            internal Faction SavedFaction;
            internal bool MapActive;
            internal Map SavedMap;
            internal sbyte SavedMapIndex;
        }

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
            _currentMapIndexField = AccessTools.Field(typeof(Game), "currentMapIndex");

            MethodInfo prefix = AccessTools.Method(
                typeof(Patch_MiliraCaravanRaidFactionContext),
                nameof(RaidLambdaPrefix));
            MethodInfo finalizer = AccessTools.Method(
                typeof(Patch_MiliraCaravanRaidFactionContext),
                nameof(RaidLambdaFinalizer));

            if (prefix == null || finalizer == null)
            {
                Log.Warning(
                    LogTag + " prefix/finalizer resolution failed; raid can still desync.");
                return;
            }

            int patched = 0;
            for (int i = 0; i < RaidTypeNames.Length; i++)
            {
                try
                {
                    Type outpostType = AccessTools.TypeByName(RaidTypeNames[i]);
                    FieldInfo caravanField;
                    MethodInfo raidLambda = FindRaidLambda(outpostType, out caravanField);
                    if (raidLambda == null || caravanField == null)
                    {
                        Log.Warning(
                            LogTag + " target resolution failed for " +
                            RaidTypeNames[i] + "; raid can still desync.");
                        continue;
                    }

                    harmony.Patch(
                        raidLambda,
                        prefix: new HarmonyMethod(prefix)
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(finalizer)
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        LogTag + " apply failed for " + RaidTypeNames[i] +
                        ": " + e.Message);
                }
            }

            if (patched == 0)
            {
                Log.Warning(
                    LogTag + " no raid lambda patched; multifaction raid can desync.");
                return;
            }

            Log.Message(
                LogTag + " raid context active: " +
                $"patched={patched}/{RaidTypeNames.Length}, " +
                "caravan faction and stable home-map context applied to ruin-outpost " +
                "and church-battlefield pawn/raid generation.");
        }

        private static MethodInfo FindRaidLambda(
            Type outpostType,
            out FieldInfo caravanField)
        {
            caravanField = null;
            if (outpostType == null)
                return null;

            Type[] nestedTypes = outpostType.GetNestedTypes(
                BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (Type nested in nestedTypes)
            {
                if (nested == null || !nested.Name.Contains("DisplayClass"))
                    continue;

                FieldInfo field = nested
                    .GetFields(
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f =>
                        f.Name == "caravan" &&
                        typeof(Caravan).IsAssignableFrom(f.FieldType));
                if (field == null)
                    continue;

                MethodInfo method = nested
                    .GetMethods(
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m =>
                        m.Name.StartsWith("<Outcome_Raid>", StringComparison.Ordinal) &&
                        m.Name.IndexOf("b__", StringComparison.Ordinal) >= 0);
                if (method == null)
                    continue;

                caravanField = field;
                return method;
            }

            return null;
        }

        private static void RaidLambdaPrefix(
            object __instance,
            ref ScopeState __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || __instance == null)
                return;

            Caravan caravan = ReadCapturedCaravan(__instance);
            if (caravan == null)
                return;

            ScopeState state = new ScopeState();
            __state = state;
            BeginFactionScope(caravan, state);
            BeginStableMapScope(state);

            if (!_loggedActive && (state.FactionActive || state.MapActive))
            {
                _loggedActive = true;
                Log.Message(
                    LogTag + " executing Milira ruin-outpost raid with " +
                    "caravan faction context and stable map context.");
            }
        }

        private static Caravan ReadCapturedCaravan(object closure)
        {
            try
            {
                FieldInfo field = closure.GetType()
                    .GetFields(
                        BindingFlags.Instance | BindingFlags.Public |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f =>
                        f.Name == "caravan" &&
                        typeof(Caravan).IsAssignableFrom(f.FieldType));
                return field?.GetValue(closure) as Caravan;
            }
            catch
            {
                return null;
            }
        }

        private static void BeginFactionScope(
            Caravan caravan,
            ScopeState state)
        {
            if (_ofPlayerField == null || Find.FactionManager == null)
                return;

            Faction faction = caravan.Faction;
            if (faction?.def?.isPlayer != true)
                return;

            Faction previous = _ofPlayerField.GetValue(
                Find.FactionManager) as Faction;
            if (ReferenceEquals(previous, faction))
                return;

            _ofPlayerField.SetValue(Find.FactionManager, faction);
            state.FactionActive = true;
            state.SavedFaction = previous;
        }

        private static void BeginStableMapScope(ScopeState state)
        {
            if (_currentMapIndexField == null || Current.Game == null ||
                Find.Maps == null || Find.Maps.Count == 0)
            {
                return;
            }

            Map map = Find.Maps
                .Where(m => m != null && m.IsPlayerHome)
                .OrderBy(m => m.uniqueID)
                .FirstOrDefault();
            if (map == null)
            {
                map = Find.Maps
                    .Where(m => m != null)
                    .OrderBy(m => m.uniqueID)
                    .FirstOrDefault();
            }

            if (map == null)
                return;

            int index = Find.Maps.IndexOf(map);
            if (index < 0 || index > sbyte.MaxValue)
                return;

            sbyte previous = (sbyte)_currentMapIndexField.GetValue(Current.Game);
            sbyte next = (sbyte)index;
            if (previous == next)
                return;

            _currentMapIndexField.SetValue(Current.Game, next);
            state.MapActive = true;
            state.SavedMap = map;
            state.SavedMapIndex = previous;
        }

        private static Exception RaidLambdaFinalizer(
            Exception __exception,
            ScopeState __state)
        {
            if (__state == null)
                return __exception;

            try
            {
                if (__state.MapActive && _currentMapIndexField != null &&
                    Current.Game != null)
                {
                    sbyte restoreIndex = __state.SavedMapIndex;
                    if (__state.SavedMap != null && Find.Maps != null)
                    {
                        int currentIndex = Find.Maps.IndexOf(__state.SavedMap);
                        if (currentIndex >= 0 && currentIndex <= sbyte.MaxValue)
                            restoreIndex = (sbyte)currentIndex;
                    }
                    _currentMapIndexField.SetValue(Current.Game, restoreIndex);
                }

                if (__state.FactionActive && _ofPlayerField != null &&
                    Find.FactionManager != null)
                {
                    _ofPlayerField.SetValue(
                        Find.FactionManager,
                        __state.SavedFaction);
                }
            }
            catch (Exception e)
            {
                Log.Warning(
                    LogTag + " context restore failed: " + e.Message);
            }

            return __exception;
        }
    }
}
