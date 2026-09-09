using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Multiplayer compatibility for Milira flight and weapon-skill abilities (CastJump + Milira jump/charge verbs).
    /// Root cause: TryTakeOrderedJob sync serializes Job.verbToUse as a cross-ref, but the Verb is not in the same deep-save graph →
    /// "verbToUse is referenced but is not deep-saved" and null verb on replay → JobDriver_CastJump NRE.
    /// Fix: register <see cref="Verb.OrderForceTarget"/> on all Milira fly verb types so the sync boundary is the verb method
    /// (each client runs OrderJump → TryTakeOrderedJob locally). Optional: register <c>MiliraFlyUtility.OrderJump</c> if present.
    /// The old late <see cref="Job.verbToUse"/> repair is intentionally disabled:
    /// the Multiplayer serializer has already crossed that boundary.
    /// </summary>
    internal static class Patch_MiliraFly
    {
        internal const string HarmonyId = "mp.meowonlineshop.milirafly";
        private const int MaxInfoLogs = 6;

        private static int _infoLogCount;
        private static JobDef _cachedCastJumpDef;
        private static bool _castJumpResolved;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled)
                return;

            int registeredVerbs = RegisterMiliraFlyVerbSyncMethods();
            // 不注册 MiliraFlyUtility.OrderJump：UI 路径走 Verb.OrderForceTarget → OrderJump，若再注册静态 OrderJump 可能重复同步。

            if (registeredVerbs == 0)
            {
                Log.Message("[MP-MeowOnlineShop] Milira fly MP: Milira types not found or sync registration skipped.");
                return;
            }

            // Do not repair Job.verbToUse in TryTakeOrderedJob: Multiplayer has
            // already serialized the Job by that point. Milian/Milira jump
            // abilities use the stable-identity OrderJump boundary instead.
            Log.Message($"[MP-MeowOnlineShop] Milira fly MP: registered OrderForceTarget sync for {registeredVerbs} declaring type(s); late CastJump repair disabled.");
        }

        private static void Info(string msg)
        {
            if (_infoLogCount >= MaxInfoLogs)
                return;
            _infoLogCount++;
            Log.Message("[MP-MeowOnlineShop] Milira fly MP: " + msg);
        }

        /// <summary>Registers MP sync on <c>OrderForceTarget(LocalTargetInfo)</c> for every Verb declared by the base Milira assembly.</summary>
        private static int RegisterMiliraFlyVerbSyncMethods()
        {
            int count = 0;
            var baseType = AccessTools.TypeByName("Milira.Verb_CastAbilityMiliraFly");
            if (baseType == null)
                return 0;

            // Weapon-skill verbs (Blade/Hammer/Lance/KnightCharge/Rook) inherit
            // Verb_CastAbility directly instead of Milira.Verb_CastAbilityMiliraFly,
            // so the old derived-type scan never reached their OrderForceTarget.
            var registeredDeclaringTypes = new HashSet<Type>();
            foreach (var type in DiscoverMiliraVerbTypes(baseType.Assembly))
            {
                var m = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, "OrderForceTarget", StringComparison.Ordinal) &&
                        method.GetParameters().Length == 1 &&
                        method.GetParameters()[0].ParameterType == typeof(LocalTargetInfo));
                if (m == null)
                    continue;

                var decl = m.DeclaringType;
                if (decl == null || !registeredDeclaringTypes.Add(decl))
                    continue;

                if (TryRegisterSyncMethod(m))
                {
                    count++;
                    Info($"Registered SyncMethod {decl.FullName}.OrderForceTarget(LocalTargetInfo).");
                }
            }

            return count;
        }

        private static IEnumerable<Type> DiscoverMiliraVerbTypes(Assembly miliraAssembly)
        {
            if (miliraAssembly == null)
                yield break;

            Type[] types;
            try
            {
                types = miliraAssembly.GetTypes();
            }
            catch
            {
                yield break;
            }

            var seen = new HashSet<Type>();
            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || !typeof(Verb).IsAssignableFrom(t))
                    continue;
                if (seen.Add(t))
                    yield return t;
            }
        }

        /// <summary>
        /// Uses <c>MP.RegisterSyncMethod(MethodInfo, null)</c> so Multiplayer infers parameter serialization from the method signature.
        /// </summary>
        private static bool TryRegisterSyncMethod(MethodInfo method)
        {
            if (method == null)
                return false;
            try
            {
                MP.RegisterSyncMethod(method, null);
                return true;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira fly MP: RegisterSyncMethod {method.DeclaringType?.FullName}.{method.Name} failed: {e.Message}");
                return false;
            }
        }

        private static JobDef GetCastJumpDef()
        {
            if (_castJumpResolved)
                return _cachedCastJumpDef;

            _castJumpResolved = true;
            try
            {
                _cachedCastJumpDef = DefDatabase<JobDef>.GetNamedSilentFail("CastJump");
            }
            catch
            {
                _cachedCastJumpDef = null;
            }

            return _cachedCastJumpDef;
        }

        private static Pawn GetPawnFromJobTracker(Pawn_JobTracker tracker)
        {
            if (tracker == null)
                return null;
            try
            {
                return AccessTools.Property(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn
                       ?? AccessTools.Field(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// When CastJump arrives with null <see cref="Job.verbToUse"/> (failed cross-ref), pick a Milira fly verb on the pawn that matches targets.
        /// </summary>
        private static void TryPatchTryTakeOrderedJobRepair(Harmony harmony)
        {
            try
            {
                var trackerType = typeof(Pawn_JobTracker);
                foreach (var m in trackerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != "TryTakeOrderedJob")
                        continue;
                    var parameters = m.GetParameters();
                    if (parameters.Length == 0 || parameters[0].ParameterType != typeof(Job))
                        continue;

                    var prefix = AccessTools.Method(typeof(Patch_MiliraFly), nameof(TryTakeOrderedJob_Prefix));
                    harmony.Patch(m, prefix: new HarmonyMethod(prefix) { priority = 5 });
                    Info($"Patched {m.DeclaringType?.FullName}.TryTakeOrderedJob for CastJump verb repair.");
                    return;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira fly MP: TryTakeOrderedJob repair patch failed: {e.Message}");
            }
        }

        /// <summary>Repair job.verbToUse before job execution / sync write when possible.</summary>
        private static void TryTakeOrderedJob_Prefix(Job job, Pawn_JobTracker __instance)
        {
            if (!MP.IsInMultiplayer || job == null)
                return;

            var castJump = GetCastJumpDef();
            if (castJump == null || job.def != castJump)
                return;

            if (job.verbToUse != null)
                return;

            var pawn = GetPawnFromJobTracker(__instance);
            if (pawn?.VerbTracker?.AllVerbs == null)
                return;

            var verb = ResolveMiliraFlyVerbForCastJump(pawn, job);
            if (verb == null)
                return;

            job.verbToUse = verb;
            if (_infoLogCount < MaxInfoLogs)
                Info($"Repaired CastJump.verbToUse → {verb.GetType().Name} for pawn {pawn.LabelShort}.");
        }

        private static Verb ResolveMiliraFlyVerbForCastJump(Pawn pawn, Job job)
        {
            var candidates = pawn.VerbTracker.AllVerbs
                .Where(v => v != null && IsMiliraFlyVerbType(v.GetType()))
                .ToList();

            if (candidates.Count == 0)
                return null;

            if (candidates.Count == 1)
                return candidates[0];

            // Deterministic pick when multiple Milira fly verbs exist (avoid wrong verb).
            candidates.Sort((x, y) => string.Compare(x?.GetUniqueLoadID(), y?.GetUniqueLoadID(), StringComparison.Ordinal));
            return candidates[0];
        }

        private static bool IsMiliraFlyVerbType(Type t)
        {
            if (t == null || !typeof(Verb).IsAssignableFrom(t))
                return false;
            string name = t.FullName ?? t.Name ?? "";
            if (name.IndexOf("Milira", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return name.IndexOf("Jump", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("Fly", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("Charge", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    internal static class Patch_MiliraFlyBootstrap
    {
        public static void Apply()
        {
            try
            {
                var harmony = new Harmony(Patch_MiliraFly.HarmonyId);
                Patch_MiliraFly.Apply(harmony);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Milira fly MP bootstrap failed: {e.Message}");
            }
        }
    }
}
