using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Replaces the jump-order sync boundary for Milian/Milira abilities.
///
/// AncotLibrary.JumpUtility_Custom.OrderJump creates a CastJump job and stores
/// the runtime Ability Verb in Job.verbToUse. Multiplayer's native
/// TryTakeOrderedJob sync then deep-saves that Verb, but the Verb is not part
/// of the deep-save graph. Send stable Ability identity instead and rebuild
/// the local Verb before invoking the original method on every peer.
/// </summary>
[StaticConstructorOnStartup]
public static class MilianModification_AncotJumpSync
{
    private const string LogTag = "[MP-MeowOnlineShop][MilianAncotJump]";
    private const string JumpUtilityTypeName = "AncotLibrary.JumpUtility_Custom";
    private const int MaxRuntimeLogs = 4;

    private static readonly Harmony Harmony = new Harmony("Ancot.MilianModification.AncotJumpSync");

    private static MethodInfo _orderJump;
    private static MethodInfo _vanillaOrderForceTarget;
    private static MethodInfo _vanillaOrderJump;
    private static bool _syncRegistered;
    private static bool _vanillaSyncRegistered;
    private static int _runtimeLogCount;

    static MilianModification_AncotJumpSync()
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
            Type jumpUtility = CompatUtility.ResolveTypeSilent(JumpUtilityTypeName);
            _orderJump = jumpUtility == null
                ? null
                : AccessTools.DeclaredMethod(
                    jumpUtility,
                    "OrderJump",
                    new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(Verb), typeof(float) });

            MethodInfo syncMethod = AccessTools.DeclaredMethod(
                typeof(MilianModification_AncotJumpSync),
                nameof(SyncedOrderJump),
                new[] { typeof(Pawn), typeof(int), typeof(string), typeof(LocalTargetInfo), typeof(float) });
            _syncRegistered = TryRegisterSyncMethod(syncMethod);
            if (_orderJump != null && _syncRegistered)
            {
                Harmony.Patch(
                    _orderJump,
                    prefix: new HarmonyMethod(
                        typeof(MilianModification_AncotJumpSync),
                        nameof(OrderJumpPrefix)));
            }

            _vanillaOrderForceTarget = AccessTools.DeclaredMethod(
                typeof(Verb_CastAbilityJump),
                nameof(Verb_CastAbilityJump.OrderForceTarget),
                new[] { typeof(LocalTargetInfo) });
            _vanillaOrderJump = AccessTools.DeclaredMethod(
                typeof(JumpUtility),
                nameof(JumpUtility.OrderJump),
                new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(Verb), typeof(float) });

            MethodInfo vanillaSyncMethod = AccessTools.DeclaredMethod(
                typeof(MilianModification_AncotJumpSync),
                nameof(SyncedVanillaOrderJump),
                new[] { typeof(Pawn), typeof(int), typeof(string), typeof(LocalTargetInfo), typeof(float) });
            _vanillaSyncRegistered = TryRegisterSyncMethod(vanillaSyncMethod);
            if (_vanillaOrderForceTarget != null && _vanillaOrderJump != null && _vanillaSyncRegistered)
            {
                Harmony.Patch(
                    _vanillaOrderForceTarget,
                    prefix: new HarmonyMethod(
                        typeof(MilianModification_AncotJumpSync),
                        nameof(VanillaOrderForceTargetPrefix)));
            }

            Log.Message(LogTag + " active: Ancot=" + (_orderJump != null && _syncRegistered) +
                        ", vanilla Verb_CastAbilityJump=" +
                        (_vanillaOrderForceTarget != null && _vanillaOrderJump != null && _vanillaSyncRegistered) +
                        "; stable Ability identity is resolved before CastJump Job serialization.");
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " patch failed: " + e);
        }
    }

    private static bool TryRegisterSyncMethod(MethodInfo method)
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
            Log.Warning(LogTag + " RegisterSyncMethod failed: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// The original method's Verb parameter is intentionally not passed to MP.
    /// It is a runtime-created Ability Verb and is exactly the object that
    /// caused the Job.verbToUse deep-save warning in the supplied trace.
    /// </summary>
    public static bool OrderJumpPrefix(object[] __args)
    {
        if (!_syncRegistered || _orderJump == null || !MP.IsInMultiplayer ||
            MP.IsExecutingSyncCommand || !MP.InInterface)
        {
            return true;
        }

        if (__args == null || __args.Length != 4)
        {
            Log.Warning(LogTag + " unexpected OrderJump argument shape; local execution blocked.");
            return false;
        }

        Pawn pawn = __args[0] as Pawn;
        Verb verb = __args[2] as Verb;
        if (pawn == null || verb == null)
            return true;

        // Ancot uses the same helper for non-Ability verbs. Leave those paths
        // alone; this compatibility boundary is for the custom Ability Verb
        // that cannot be deep-saved by Multiplayer.
        if (!(verb is Verb_CastAbility))
            return true;

        Ability ability = ResolveAbilityFromVerb(pawn, verb);
        if (ability == null || ability.def == null)
        {
            Log.Warning(LogTag + " could not resolve Ability for custom jump Verb " +
                        (verb.GetType().FullName ?? verb.GetType().Name) + "; local execution blocked.");
            return false;
        }

        LocalTargetInfo target = (LocalTargetInfo)__args[1];
        float range = Convert.ToSingle(__args[3]);
        SyncedOrderJump(pawn, ability.Id, ability.def.defName ?? string.Empty, target, range);
        RuntimeLog("intercepted jump: pawn=" + pawn.thingIDNumber +
                   " ability=" + ability.Id + "/" + (ability.def.defName ?? "unknown") + ".");
        return false;
    }

    /// <summary>
    /// Vanilla Verb_CastAbilityJump calls JumpUtility.OrderJump directly. The
    /// native Multiplayer OrderForceTarget sync therefore serializes the
    /// resulting Job.verbToUse. Intercept only Milian/Milira ability verbs and
    /// send the stable Ability identity instead.
    /// </summary>
    public static bool VanillaOrderForceTargetPrefix(
        Verb_CastAbilityJump __instance,
        LocalTargetInfo target)
    {
        if (!_vanillaSyncRegistered || _vanillaOrderForceTarget == null ||
            _vanillaOrderJump == null || !MP.IsInMultiplayer ||
            MP.IsExecutingSyncCommand || !MP.InInterface)
        {
            return true;
        }

        Ability ability = __instance?.ability;
        Pawn pawn = ability?.pawn ?? __instance?.CasterPawn;
        if (pawn == null || ability == null || ability.def == null ||
            !IsMilianOrMiliraJump(__instance, pawn, ability))
        {
            return true;
        }

        SyncedVanillaOrderJump(
            pawn,
            ability.Id,
            ability.def.defName ?? string.Empty,
            target,
            __instance.EffectiveRange);
        RuntimeLog("intercepted vanilla jump: pawn=" + pawn.thingIDNumber +
                   " ability=" + ability.Id + "/" + (ability.def.defName ?? "unknown") + ".");
        return false;
    }

    /// <summary>
    /// Runs only from Multiplayer's scheduled sync command. The original
    /// OrderJump then creates CastJump locally with the peer-local Ability Verb,
    /// so no custom Verb crosses the Job serializer.
    /// </summary>
    public static void SyncedOrderJump(
        Pawn pawn,
        int abilityId,
        string abilityDefName,
        LocalTargetInfo target,
        float range)
    {
        if (pawn == null || _orderJump == null)
            return;

        Ability ability = ResolveAbility(pawn, abilityId, abilityDefName);
        Verb verb = ability?.verb;
        if (ability == null || verb == null)
        {
            Log.Warning(LogTag + " peer Ability/Verb resolve failed: pawn=" +
                        pawn.thingIDNumber + " id=" + abilityId + "/" + (abilityDefName ?? "null") + ".");
            return;
        }

        try
        {
            _orderJump.Invoke(null, new object[] { pawn, target, verb, range });
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException ?? e;
        }
    }

    public static void SyncedVanillaOrderJump(
        Pawn pawn,
        int abilityId,
        string abilityDefName,
        LocalTargetInfo target,
        float range)
    {
        if (pawn == null || _vanillaOrderJump == null)
            return;

        Ability ability = ResolveAbility(pawn, abilityId, abilityDefName);
        Verb verb = ability?.verb;
        if (ability == null || verb == null)
        {
            Log.Warning(LogTag + " peer vanilla jump Ability/Verb resolve failed: pawn=" +
                        pawn.thingIDNumber + " id=" + abilityId + "/" + (abilityDefName ?? "null") + ".");
            return;
        }

        try
        {
            _vanillaOrderJump.Invoke(null, new object[] { pawn, target, verb, range });
        }
        catch (TargetInvocationException e)
        {
            throw e.InnerException ?? e;
        }
    }

    private static bool IsMilianOrMiliraJump(
        Verb_CastAbilityJump verb,
        Pawn pawn,
        Ability ability)
    {
        string verbName = verb?.GetType().FullName ?? string.Empty;
        string pawnDefName = pawn?.def?.defName ?? string.Empty;
        string bodyDefName = pawn?.RaceProps?.body?.defName ?? string.Empty;
        string abilityDefName = ability?.def?.defName ?? string.Empty;

        return ContainsMiliraMarker(verbName) ||
               ContainsMiliraMarker(pawnDefName) ||
               ContainsMiliraMarker(bodyDefName) ||
               ContainsMiliraMarker(abilityDefName);
    }

    private static bool ContainsMiliraMarker(string value)
    {
        return value.IndexOf("Milira", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("Milian", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Ability ResolveAbilityFromVerb(Pawn pawn, Verb verb)
    {
        if (verb == null)
            return null;

        Ability direct = ReadAbilityMember(verb);
        if (direct != null && (direct.pawn == null || direct.pawn == pawn))
            return direct;

        foreach (Ability ability in EnumerateAbilities(pawn))
        {
            if (ability != null && ReferenceEquals(ability.verb, verb))
                return ability;
        }

        return null;
    }

    private static Ability ResolveAbility(Pawn pawn, int abilityId, string abilityDefName)
    {
        Ability byDef = null;
        foreach (Ability ability in EnumerateAbilities(pawn))
        {
            if (ability == null)
                continue;
            if (ability.Id == abilityId)
                return ability;
            if (byDef == null && string.Equals(
                    ability.def?.defName,
                    abilityDefName,
                    StringComparison.Ordinal))
            {
                byDef = ability;
            }
        }

        return byDef;
    }

    private static IEnumerable<Ability> EnumerateAbilities(Pawn pawn)
    {
        if (pawn?.abilities?.AllAbilitiesForReading != null)
        {
            foreach (Ability ability in pawn.abilities.AllAbilitiesForReading)
                yield return ability;
        }

        if (pawn?.mutant?.AllAbilitiesForReading != null)
        {
            foreach (Ability ability in pawn.mutant.AllAbilitiesForReading)
                yield return ability;
        }
    }

    private static Ability ReadAbilityMember(Verb verb)
    {
        for (Type type = verb?.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo[] fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                if (field.FieldType != typeof(Ability))
                    continue;
                try
                {
                    return field.GetValue(verb) as Ability;
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static void RuntimeLog(string message)
    {
        if (_runtimeLogCount++ < MaxRuntimeLogs)
            Log.Message(LogTag + " " + message);
    }
}
