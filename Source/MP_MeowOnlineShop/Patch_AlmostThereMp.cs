using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Almost There! Fork (`duz.almosttherefork`) exposes a saved caravan
    /// night-rest mode through a Command_Toggle on CompNightRestControl. The
    /// toggle lambda changes the `AlmostThere` property on the clicking
    /// peer; Caravan_PostAdd_Patch later reads it during deterministic world
    /// simulation.
    ///
    /// Multiplayer's official compatibility package only covers the older
    /// `roolo.AlmostThere` and `Chad.Almostthere1.5` package IDs. Sync the
    /// actual toggle action, so consecutive clicks advance the shared mode
    /// in command order. A bare SyncField registration does not watch writes.
    /// PostAdd initialization and save/load remain outside this UI boundary.
    /// </summary>
    internal static class Patch_AlmostThereMp
    {
        private const string PackageId = "duz.almosttherefork";
        private const string CompTypeName = "CaravanDontRest.CompNightRestControl";
        // Reference: rwmt/Multiplayer-Compatibility contributors, AlmostThere.cs
        // (original mod by Roolo), MIT; action-sync approach, not copied code:
        // https://github.com/rwmt/Multiplayer-Compatibility/blob/bcffd46671bd326f1b978d421423e3c5b68305b8/Source/Mods/AlmostThere.cs
        // Fork author: Duztamva, https://steamcommunity.com/sharedfiles/filedetails/?id=3515165298
        // https://github.com/duztamva/Almost-There-Fork-1.5-/tree/b89a9c41dbe8d9797a2d8826921ba42bb73ab860
        // Both installed RW 1.6 variants (AT1.6 / AT1.6Vehicle) bind this
        // instance method to Command_Toggle.toggleAction; ordinal 0 is isActive.
        private const string ToggleMethodName = "<GetGizmos>b__5_1";
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            Type compType = AccessTools.TypeByName(CompTypeName);
            MethodInfo toggle = compType == null ? null :
                AccessTools.DeclaredMethod(compType, ToggleMethodName, Type.EmptyTypes);
            if (toggle == null || toggle.IsStatic || toggle.ReturnType != typeof(void) ||
                !toggle.IsDefined(typeof(CompilerGeneratedAttribute), false))
            {
                Log.Warning("[MP-MeowOnlineShop] Almost There fork target resolution failed; patch skipped.");
                return;
            }

            try
            {
                MP.RegisterSyncMethod(toggle, null);
                _applied = true;
                Log.Message("[MP-MeowOnlineShop] Almost There fork MP action registered: " +
                    compType.FullName + "::" + toggle.Name);
                ApplyNightRestCacheBoundary(harmony);
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Almost There fork toggle registration failed: " + e.Message);
            }
        }

        internal static void ApplyNightRestCacheBoundary(Harmony harmony)
        {
            var type = AccessTools.TypeByName("CaravanDontRest.Caravan_NightResting_Patch");
            var target = type == null ? null : AccessTools.DeclaredMethod(type, "Postfix",
                new[] { typeof(Caravan), typeof(bool).MakeByRefType() });
            if (target == null || !target.IsStatic || target.ReturnType != typeof(void))
            {
                Log.Error("[MP-MeowOnlineShop] Almost There night-rest cache target missing; " +
                    "deterministic night-rest boundary was NOT installed.");
                return;
            }

            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Patch_AlmostThereMp),
                nameof(NightRestCacheTranspiler)));
            Log.Message("[MP-MeowOnlineShop] Almost There night-rest cache boundary active: " +
                "MP rest decisions use a fresh arrival estimate; local ETA cache is neither read nor written.");
        }

        internal static IEnumerable<CodeInstruction> NightRestCacheTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var original = AccessTools.DeclaredMethod(typeof(CaravanArrivalTimeEstimator),
                nameof(CaravanArrivalTimeEstimator.EstimatedTicksToArrive),
                new[] { typeof(Caravan), typeof(bool) });
            var replacement = AccessTools.DeclaredMethod(typeof(Patch_AlmostThereMp),
                nameof(EstimateForNightRest));
            // Resolve the installed overload, not a lambda ordinal or a nearby call.
            // Refuse an unknown layout before emitting any modified instructions.
            if (original == null || replacement == null || code.Count(x => x.Calls(original)) != 1)
                throw new InvalidOperationException("Almost There night-rest ETA call layout changed; expected exactly one call.");
            foreach (var instruction in code)
            {
                if (instruction.Calls(original))
                    instruction.operand = replacement;
                yield return instruction;
            }
        }

        internal static int EstimateForNightRest(Caravan caravan, bool allowCaching)
        {
            // The vanilla cache is static, UI-warmed, unsaved, and accepts negative
            // age after an async map/world clock switch. Almost There consumes it
            // in CantMove -> MovingNow -> caravan needs, where a stale result changes
            // both gameplay and the number of world Rand draws (Desync 33-35).
            // Keep the original algorithm, settings, faction/time context and RNG.
            return CaravanArrivalTimeEstimator.EstimatedTicksToArrive(caravan,
                allowCaching && !MP.IsInMultiplayer);
        }
    }
}
