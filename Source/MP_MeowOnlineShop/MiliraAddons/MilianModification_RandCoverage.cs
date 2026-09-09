using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

/// <summary>
/// Covers MilianModification random boundaries that are not reached by the
/// generic Milira/Ancot projectile patches: the overridden Fortress bullet
/// tick, Fortress fire effects, and the install-post-finished cooldown reset.
/// </summary>
[StaticConstructorOnStartup]
public static class MilianModification_RandCoverage
{
    private const string LogTag = "[MP-MeowOnlineShop][MilianRand]";
    private const int WorldSeedOffset = 0x4D494C52; // "MILR"
    private const int BulletTickSalt = 0x4D494C42; // "MILB"
    private const int CiwsFireSalt = 0x4D494343; // "MILCC"
    private const int PlasmaFireSalt = 0x4D494C50; // "MILP"
    private const int InstallSalt = 0x4D494C49; // "MILI"

    private static readonly Harmony Harmony = new Harmony("Ancot.MilianModification.RandCoverage");

    static MilianModification_RandCoverage()
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
            int patched = 0;
            patched += PatchThingMethod(
                "MilianModification.Bullet_FortressCIWS",
                "Tick",
                BulletTickSalt);
            patched += PatchVerbMethod(
                "MilianModification.Verb_Shoot_FortressCIWS",
                CiwsFireSalt);
            patched += PatchVerbMethod(
                "MilianModification.Verb_Shoot_FortressPlasma",
                PlasmaFireSalt);
            patched += PatchCompMethod(
                "MilianModification.CompModification",
                "Notify_InstallPostFinished",
                InstallSalt);

            if (patched == 0)
            {
                Log.Warning(LogTag + " no additional random boundary resolved.");
                return;
            }

            Log.Message(LogTag + " active: boundaries=" + patched +
                        " (Fortress bullet/fire and install cooldown scope).");
        }
        catch (Exception e)
        {
            Log.Error(LogTag + " patch failed: " + e);
        }
    }

    private static int PatchThingMethod(string typeName, string methodName, int salt)
    {
        Type type = CompatUtility.ResolveTypeSilent(typeName);
        MethodInfo target = type == null
            ? null
            : AccessTools.DeclaredMethod(type, methodName, Type.EmptyTypes);
        if (target == null)
            return 0;

        Harmony.Patch(
            target,
            prefix: new HarmonyMethod(
                typeof(MilianModification_RandCoverage),
                nameof(ThingPrefix))
            {
                priority = Priority.First
            },
            finalizer: new HarmonyMethod(
                typeof(MilianModification_RandCoverage),
                nameof(ScopeFinalizer))
            {
                priority = Priority.Last
            });
        ThingSaltByTarget[target] = salt;
        return 1;
    }

    private static int PatchVerbMethod(string typeName, int salt)
    {
        Type type = CompatUtility.ResolveTypeSilent(typeName);
        MethodInfo target = type == null
            ? null
            : AccessTools.DeclaredMethod(type, "TryCastShot", Type.EmptyTypes);
        if (target == null)
            return 0;

        Harmony.Patch(
            target,
            prefix: new HarmonyMethod(
                typeof(MilianModification_RandCoverage),
                nameof(VerbPrefix))
            {
                priority = Priority.First
            },
            finalizer: new HarmonyMethod(
                typeof(MilianModification_RandCoverage),
                nameof(ScopeFinalizer))
            {
                priority = Priority.Last
            });
        ThingSaltByTarget[target] = salt;
        return 1;
    }

    private static int PatchCompMethod(string typeName, string methodName, int salt)
    {
        Type type = CompatUtility.ResolveTypeSilent(typeName);
        MethodInfo target = type == null
            ? null
            : AccessTools.DeclaredMethod(type, methodName);
        if (target == null)
            return 0;

        Harmony.Patch(
            target,
            prefix: new HarmonyMethod(
                typeof(MilianModification_RandCoverage),
                nameof(CompPrefix))
            {
                priority = Priority.First
            },
            finalizer: new HarmonyMethod(
                typeof(MilianModification_RandCoverage),
                nameof(ScopeFinalizer))
            {
                priority = Priority.Last
            });
        ThingSaltByTarget[target] = salt;
        return 1;
    }

    private sealed class ScopeState
    {
        internal int RandState;
        internal Map MapForPop;
    }

    private static readonly System.Collections.Generic.Dictionary<MethodBase, int> ThingSaltByTarget =
        new System.Collections.Generic.Dictionary<MethodBase, int>();

    private static void ThingPrefix(object __instance, MethodBase __originalMethod, ref ScopeState __state)
    {
        __state = null;
        if (!ThingSaltByTarget.TryGetValue(__originalMethod, out int salt))
            return;

        BeginThingScope(__instance as Thing, salt, ref __state);
    }

    private static void VerbPrefix(object __instance, MethodBase __originalMethod, ref ScopeState __state)
    {
        __state = null;
        if (!ThingSaltByTarget.TryGetValue(__originalMethod, out int salt) ||
            !(__instance is Verb verb))
        {
            return;
        }

        Thing caster;
        try
        {
            caster = verb.Caster;
        }
        catch
        {
            return;
        }

        BeginThingScope(caster, salt, ref __state);
    }

    private static void CompPrefix(object __instance, MethodBase __originalMethod, ref ScopeState __state)
    {
        __state = null;
        if (!ThingSaltByTarget.TryGetValue(__originalMethod, out int salt) ||
            !(__instance is ThingComp comp))
        {
            return;
        }

        BeginThingScope(comp.parent, salt, ref __state);
    }

    private static void BeginThingScope(Thing thing, int salt, ref ScopeState state)
    {
        if (!MP.IsInMultiplayer || thing == null)
            return;

        Map map = thing.Map;
        int seed = Gen.HashCombineInt(salt, map?.uniqueID ?? 0);
        seed = Gen.HashCombineInt(seed, thing.thingIDNumber);
        seed = Gen.HashCombineInt(seed, thing.def?.shortHash ?? 0);
        seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);

        int randState = 0;
        if (!MP_MeowOnlineShop.DeterministicRandScope.Begin(
                map,
                seed,
                WorldSeedOffset,
                ref randState,
                out Map mapForPop,
                ignoreGate: true))
        {
            return;
        }

        state = new ScopeState
        {
            RandState = randState,
            MapForPop = mapForPop
        };
    }

    private static Exception ScopeFinalizer(Exception __exception, ScopeState __state)
    {
        if (__state == null || __state.RandState == 0)
            return __exception;

        try
        {
            MP_MeowOnlineShop.DeterministicRandScope.End(
                __state.RandState,
                __state.MapForPop);
        }
        catch (Exception e)
        {
            Log.Warning(LogTag + " Rand scope restore failed: " + e.Message);
        }

        return __exception;
    }
}
