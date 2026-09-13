using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RJW frequently uses Faction.OfPlayer inside simulation code. In a
    /// multifaction game that property is peer-local, so the same pregnancy,
    /// AI or recipe can observe a different colony on each client. Scope only
    /// the affected RJW executions to the player faction which owns/hosts the
    /// pawn driving that execution, then restore the previous value in a
    /// finalizer (including exceptional exits).
    /// </summary>
    internal static class Patch_RjwMultifactionContext
    {
        private const string LogTag = "[MP-MeowOnlineShop][RJW-Multifaction]";
        private static FieldInfo _ofPlayerField;
        private static int _resolved;
        private static int _patched;

        private sealed class ScopeState
        {
            internal FactionManager Manager;
            internal Faction Previous;
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !ModsConfig.IsActive(Patch_RimJobWorld.PackageId) || !MP.enabled)
                return;

            _ofPlayerField = AccessTools.Field(typeof(FactionManager), "ofPlayer");
            if (_ofPlayerField == null)
            {
                Log.Warning(LogTag + " FactionManager.ofPlayer was not found; patch disabled.");
                return;
            }

            var prefix = new HarmonyMethod(AccessTools.Method(typeof(Patch_RjwMultifactionContext), nameof(Prefix)));
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(Patch_RjwMultifactionContext), nameof(Finalizer)));

            Patch(harmony, "rjw.Hediff_BasePregnancy", "GenerateBabies", prefix, finalizer);
            Patch(harmony, "rjw.Hediff_HumanlikePregnancy", "GiveBirth", prefix, finalizer);
            Patch(harmony, "rjw.Hediff_InsectEggPregnancy", "GiveBirth", prefix, finalizer);
            Patch(harmony, "rjw.Hediff_MechanoidPregnancy", "GiveBirth", prefix, finalizer);
            Patch(harmony, "rjw.Hediff_BestialPregnancy", "GiveBirth", prefix, finalizer);
            Patch(harmony, "rjw.Recipe_ClaimChild", "ApplyOnPawn", prefix, finalizer);
            Patch(harmony, "rjw.CasualSex_Helper", "FindPartner", prefix, finalizer);
            Patch(harmony, "rjw.CasualSex_Helper", "HookupAllowedViaSettings", prefix, finalizer);
            Patch(harmony, "rjw.ThinkNode_ConditionalMate", "Satisfied", prefix, finalizer);
            Patch(harmony, "rjw.ThinkNode_ConditionalCanRapeCP", "Satisfied", prefix, finalizer);
            Patch(harmony, "rjw.AfterSexUtility", "think_about_sex_Bestiality_TameAttempt", prefix, finalizer);
            Patch(harmony, "rjw.PATCH_JobGiver_Mate_TryGiveJob", "CanOverride", prefix, finalizer);

            Log.Message(LogTag + $" target resolution complete: resolved={_resolved}, patched={_patched}, expected=12.");
            if (_patched != 12)
                Log.Warning(LogTag + " one or more required RJW 6.1.2 targets were not patched.");
        }

        private static void Patch(Harmony harmony, string typeName, string methodName, HarmonyMethod prefix, HarmonyMethod finalizer)
        {
            Type type = AccessTools.TypeByName(typeName);
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName);
            if (method == null)
            {
                Log.Warning(LogTag + $" missing target {typeName}.{methodName}.");
                return;
            }

            _resolved++;
            harmony.Patch(method, prefix: prefix, finalizer: finalizer);
            _patched++;
        }

        private static void Prefix(MethodBase __originalMethod, object __instance, object[] __args, ref ScopeState __state)
        {
            bool multifaction;
            if (!MP.IsInMultiplayer || !MpRuntimeInfo.TryGetMultifactionActive(out multifaction) || !multifaction)
                return;

            Pawn contextPawn = ResolveContextPawn(__originalMethod, __instance, __args);
            Faction contextFaction = ResolvePlayerFaction(contextPawn);
            FactionManager manager = Find.FactionManager;
            if (contextFaction == null || manager == null)
                return;

            Faction previous = _ofPlayerField.GetValue(manager) as Faction;
            if (ReferenceEquals(previous, contextFaction))
                return;

            _ofPlayerField.SetValue(manager, contextFaction);
            __state = new ScopeState { Manager = manager, Previous = previous };
        }

        private static Exception Finalizer(Exception __exception, ScopeState __state)
        {
            if (__state != null && __state.Manager != null)
                _ofPlayerField.SetValue(__state.Manager, __state.Previous);
            return __exception;
        }

        private static Pawn ResolveContextPawn(MethodBase method, object instance, object[] args)
        {
            // Claim Child belongs to the surgeon/issuing faction, not the child.
            if (method?.DeclaringType?.FullName == "rjw.Recipe_ClaimChild" && args != null && args.Length > 2)
                return args[2] as Pawn;

            var hediff = instance as Hediff;
            if (hediff?.pawn != null)
                return hediff.pawn;

            if (args != null)
                foreach (object arg in args)
                    if (arg is Pawn pawn)
                        return pawn;

            return null;
        }

        private static Faction ResolvePlayerFaction(Pawn pawn)
        {
            if (pawn == null)
                return null;

            if (pawn.HostFaction?.def?.isPlayer == true)
                return pawn.HostFaction;
            if (pawn.Faction?.def?.isPlayer == true)
                return pawn.Faction;

            Map map = pawn.MapHeld;
            Faction parentFaction = map?.ParentFaction;
            return parentFaction?.def?.isPlayer == true ? parentFaction : null;
        }
    }
}
