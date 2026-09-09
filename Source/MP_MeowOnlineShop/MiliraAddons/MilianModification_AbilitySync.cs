using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Bridges the two custom click paths in Milian Modification's
/// Command_MilianAbility that do not pass through Multiplayer's native
/// Ability.QueueCastingJob delegate registrations.
/// </summary>
[StaticConstructorOnStartup]
public static class MilianModification_AbilitySync
{
    private const string LogTag = "[MP-MeowOnlineShop][MilianAbility]";
    private const string TargetTypeName = "MilianModification.Command_MilianAbility";

    private static readonly Harmony Harmony = new Harmony("Ancot.MilianModification.AbilitySync");

    private static Type _commandType;
    private static FieldInfo _abilityField;
    private static MethodInfo _processInput;
    private static MethodInfo _globalTargetCallback;
    private static MethodInfo _transitionDriveApply;
    private static bool _selfCastSyncRegistered;
    private static bool _globalTargetSyncRegistered;

    static MilianModification_AbilitySync()
    {
        if (MiliraMpCompatGate.ReferenceModActive)
        {
            Log.Message(LogTag + " skipped: usamiseika.fixmod.miliramultiplayer is active.");
            return;
        }

        if (!MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification"))
            return;

        try
        {
            LongEventHandler.ExecuteWhenFinished(Patch);
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " init failed: " + e);
        }
    }

    private static void Patch()
    {
        try
        {
            _commandType = CompatUtility.ResolveTypeSilent(TargetTypeName);
            if (_commandType == null)
            {
                Log.Warning(LogTag + " command type unresolved; ability click sync skipped.");
                return;
            }

            _abilityField = AccessTools.DeclaredField(_commandType, "ability");
            _processInput = AccessTools.DeclaredMethod(
                _commandType,
                "ProcessInput",
                new[] { typeof(Event) });
            _globalTargetCallback = FindGlobalTargetCallback();

            if (_abilityField == null || _processInput == null)
            {
                Log.Warning(LogTag + " command field/method unresolved; ability click sync skipped.");
                return;
            }

            _selfCastSyncRegistered = RegisterSyncMethod(
                AccessTools.DeclaredMethod(
                    typeof(MilianModification_AbilitySync),
                    nameof(SyncedQueueSelfCast),
                    new[] { typeof(Ability) }));
            _globalTargetSyncRegistered = RegisterSyncMethod(
                AccessTools.DeclaredMethod(
                    typeof(MilianModification_AbilitySync),
                    nameof(SyncedQueueGlobalAbility),
                    new[] { typeof(Ability), typeof(GlobalTargetInfo) }));

            if (_selfCastSyncRegistered)
            {
                Harmony.Patch(
                    _processInput,
                    prefix: new HarmonyMethod(
                        typeof(MilianModification_AbilitySync),
                        nameof(ProcessInputPrefix)));
            }

            if (_globalTargetCallback != null && _globalTargetSyncRegistered)
            {
                Harmony.Patch(
                    _globalTargetCallback,
                    prefix: new HarmonyMethod(
                        typeof(MilianModification_AbilitySync),
                        nameof(GlobalTargetCallbackPrefix)));
            }
            else
            {
                Log.Warning(LogTag + " global-target callback/sync unresolved; global ability path left vanilla.");
            }

            PatchTransitionDriveRandomness();

            Log.Message(
                LogTag + " active: selfCast=" + _selfCastSyncRegistered +
                " globalTarget=" + _globalTargetSyncRegistered +
                " callback=" + (_globalTargetCallback?.Name ?? "missing") + ".");
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " patch failed: " + e);
        }
    }

    private static MethodInfo FindGlobalTargetCallback()
    {
        MethodInfo[] candidates = _commandType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method =>
                method.Name.StartsWith("<ProcessInput>b__", StringComparison.Ordinal) &&
                method.ReturnType == typeof(bool) &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(GlobalTargetInfo))
            .ToArray();

        if (candidates.Length != 1)
        {
            Log.Warning(LogTag + " expected one ProcessInput GlobalTargetInfo callback, found " + candidates.Length + ".");
            return null;
        }

        return candidates[0];
    }

    private static bool RegisterSyncMethod(MethodInfo method)
    {
        if (method == null)
            return false;

        try
        {
            MP.RegisterSyncMethod(method, (SyncType[])null);
            return true;
        }
        catch (Exception e)
        {
            Log.Warning(LogTag + " sync registration failed for " + method.Name + ": " + e.Message);
            return false;
        }
    }

    private static Ability GetAbility(object instance)
    {
        return instance == null || _abilityField == null
            ? null
            : _abilityField.GetValue(instance) as Ability;
    }

    public static bool ProcessInputPrefix(object __instance)
    {
        if (!_selfCastSyncRegistered || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
            !MP.InInterface || __instance == null)
        {
            return true;
        }

        Ability ability = GetAbility(__instance);
        if (ability == null || ability.def == null || ability.def.targetRequired)
            return true;

        SyncedQueueSelfCast(ability);
        return false;
    }

    public static bool GlobalTargetCallbackPrefix(
        object __instance,
        object[] __args,
        ref bool __result)
    {
        if (!_globalTargetSyncRegistered || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand ||
            !MP.InInterface || __instance == null || __args == null || __args.Length != 1 ||
            !(__args[0] is GlobalTargetInfo))
        {
            return true;
        }

        Ability ability = GetAbility(__instance);
        GlobalTargetInfo target = (GlobalTargetInfo)__args[0];
        if (ability == null || !ability.ValidateGlobalTarget(target))
            return true;

        SyncedQueueGlobalAbility(ability, target);
        __result = true;
        return false;
    }

    public static void SyncedQueueSelfCast(Ability ability)
    {
        if (ability == null || ability.pawn == null)
            return;

        ability.QueueCastingJob(new LocalTargetInfo(ability.pawn), LocalTargetInfo.Invalid);
    }

    public static void SyncedQueueGlobalAbility(Ability ability, GlobalTargetInfo target)
    {
        if (ability == null)
            return;

        ability.QueueCastingJob(target);
    }

    private static void PatchTransitionDriveRandomness()
    {
        Type type = CompatUtility.ResolveTypeSilent("MilianModification.CompAbilityTransitionDrive");
        _transitionDriveApply = type == null
            ? null
            : AccessTools.DeclaredMethod(type, "Apply", new[] { typeof(GlobalTargetInfo) });
        if (_transitionDriveApply == null)
        {
            Log.Warning(LogTag + " transition-drive Apply unresolved; random landing-cell isolation skipped.");
            return;
        }

        Harmony.Patch(
            _transitionDriveApply,
            prefix: new HarmonyMethod(
                typeof(MilianModification_AbilitySync),
                nameof(TransitionDriveApplyPrefix)),
            finalizer: new HarmonyMethod(
                typeof(MilianModification_AbilitySync),
                nameof(TransitionDriveApplyFinalizer)));
    }

    public static void TransitionDriveApplyPrefix(CompAbilityEffect __instance)
    {
        CompatUtility.PushDetRand_CompAbility(__instance);
    }

    public static Exception TransitionDriveApplyFinalizer(Exception __exception)
    {
        return CompatUtility.PopDetRandFinalizer(__exception);
    }
}
