using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Desync-138/139: forcing a pawn to wear apparel and dressing a Milian.
    /// The Milira dressing item uses `CompTargetable_Milian` (PlayerChoosesTarget
    /// = true) + `CompTargetEffect_DressMilian`. `CompTargetable.DoEffect`
    /// re-validates the target with a `Faction.OfPlayer`-relative predicate,
    /// and `CompTargetEffect_DressMilian.DoEffectOn` only queues the
    /// `Milira_DressMilian` job when `user.IsColonistPlayerControlled`
    /// (also player-relative). In a multifaction session one peer evaluates
    /// those predicates against its own player faction while the other uses
    /// its own, so the dress job runs on only one side and map/world state
    /// drifts.
    ///
    /// Fix: while those callbacks run in multiplayer, temporarily switch
    /// `FactionManager.ofPlayer` to the shared user pawn's faction (fallback:
    /// Multiplayer's spectator faction) and restore it after, so every peer
    /// evaluates the same predicates and queues the same synced dress job.
    /// Singleplayer is untouched.
    /// </summary>
    internal static class Patch_MilianDressMp
    {
        private const string TargetableTypeName =
            "Milira.CompTargetable_Milian";
        private const string DressEffectTypeName =
            "Milira.CompTargetEffect_DressMilian";

        private static bool _applied;
        private static Type _targetableType;
        private static Type _dressEffectType;
        private static FieldInfo _ofPlayerField;
        private static PropertyInfo _worldCompProperty;
        private static FieldInfo _spectatorFactionField;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || !MP.enabled || harmony == null)
                return;
            _applied = true;

            try
            {
                _targetableType = AccessTools.TypeByName(TargetableTypeName);
                _dressEffectType = AccessTools.TypeByName(DressEffectTypeName);
                _ofPlayerField = AccessTools.Field(
                    typeof(FactionManager), "ofPlayer");
                Type multiplayerType = AccessTools.TypeByName(
                    "Multiplayer.Client.Multiplayer");
                _worldCompProperty = multiplayerType == null
                    ? null
                    : AccessTools.Property(multiplayerType, "WorldComp");
                _spectatorFactionField = null;
                if (_worldCompProperty?.PropertyType != null)
                {
                    _spectatorFactionField = AccessTools.Field(
                        _worldCompProperty.PropertyType,
                        "spectatorFaction");
                }

                if (_targetableType == null || _dressEffectType == null ||
                    _ofPlayerField == null || _worldCompProperty == null ||
                    _spectatorFactionField == null ||
                    _spectatorFactionField.FieldType != typeof(Faction))
                {
                    Log.Message(
                        "[MP-MeowOnlineShop] Milian dress MP patch skipped: " +
                        $"targetable={_targetableType != null}, " +
                        $"effect={_dressEffectType != null}, " +
                        $"factionCtx={_ofPlayerField != null && _spectatorFactionField != null}.");
                    return;
                }

                int patched = 0;
                patched += TryPatch(
                    harmony,
                    typeof(CompTargetable),
                    "DoEffect",
                    new[] { typeof(Pawn) },
                    "CompTargetable.DoEffect",
                    RequireTargetableInstance);
                patched += TryPatch(
                    harmony,
                    _dressEffectType,
                    "DoEffectOn",
                    new[] { typeof(Pawn), typeof(Thing) },
                    "CompTargetEffect_DressMilian.DoEffectOn",
                    null);

                Log.Message(
                    "[MP-MeowOnlineShop] Milian dress MP determinism active: " +
                    $"targets patched={patched}/2.");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milian dress MP patch failed: " +
                    e.Message);
            }
        }

        private delegate bool InstanceFilter(object instance);

        private static int TryPatch(
            Harmony harmony,
            Type type,
            string methodName,
            Type[] parameterTypes,
            string label,
            InstanceFilter filter)
        {
            try
            {
                MethodInfo target = AccessTools.Method(
                    type,
                    methodName,
                    parameterTypes);
                MethodInfo prefix = AccessTools.Method(
                    typeof(Patch_MilianDressMp),
                    nameof(FactionScopePrefix));
                MethodInfo finalizer = AccessTools.Method(
                    typeof(Patch_MilianDressMp),
                    nameof(FactionScopeFinalizer));
                if (target == null || prefix == null || finalizer == null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Milian dress " + label +
                        " target resolution failed.");
                    return 0;
                }

                harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(finalizer) { priority = Priority.Last });

                PatchedTargets.Add(target);
                if (filter != null)
                    ActiveFilters[target] = filter;
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] Milian dress " + label +
                    " patch failed: " + e.Message);
                return 0;
            }
        }

        private static readonly System.Collections.Generic.Dictionary<
            MethodBase, InstanceFilter> ActiveFilters =
            new System.Collections.Generic.Dictionary<
                MethodBase, InstanceFilter>();
        private static readonly System.Collections.Generic.HashSet<MethodBase>
            PatchedTargets =
            new System.Collections.Generic.HashSet<MethodBase>();

        private static bool RequireTargetableInstance(object instance)
        {
            return instance != null &&
                   _targetableType != null &&
                   _targetableType.IsInstanceOfType(instance);
        }

        private static void FactionScopePrefix(
            object __instance,
            Pawn userOrUsedBy,
            ref Faction __state,
            MethodBase __originalMethod)
        {
            __state = null;
            if (!MP.IsInMultiplayer)
                return;

            if (__originalMethod == null ||
                !PatchedTargets.Contains(__originalMethod))
            {
                return;
            }

            if (ActiveFilters.TryGetValue(
                    __originalMethod,
                    out InstanceFilter filter) &&
                !filter(__instance))
            {
                return;
            }

            Faction context =
                userOrUsedBy?.Faction ?? TryGetSpectatorFaction();
            FactionManager factionManager = Find.FactionManager;
            if (context == null || factionManager == null ||
                _ofPlayerField == null)
            {
                return;
            }

            try
            {
                __state = _ofPlayerField.GetValue(factionManager) as Faction;
                _ofPlayerField.SetValue(factionManager, context);
            }
            catch
            {
                __state = null;
            }
        }

        private static Exception FactionScopeFinalizer(
            Exception __exception,
            Faction __state)
        {
            if (__state != null)
            {
                try
                {
                    FactionManager factionManager = Find.FactionManager;
                    if (factionManager != null)
                        _ofPlayerField.SetValue(factionManager, __state);
                }
                catch
                {
                    // Fail open; the next world-tick context restores ofPlayer.
                }
            }

            return __exception;
        }

        private static Faction TryGetSpectatorFaction()
        {
            try
            {
                object worldComp = _worldCompProperty.GetValue(null, null);
                return worldComp == null
                    ? null
                    : _spectatorFactionField.GetValue(worldComp) as Faction;
            }
            catch
            {
                return null;
            }
        }
    }
}
