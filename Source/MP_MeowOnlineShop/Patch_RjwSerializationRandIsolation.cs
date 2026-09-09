using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RJW Core and Sized Apparel perform random fallback initialization from
    /// ExposeData methods. Multiplayer's timestamp fixer invokes ExposeData for
    /// diagnostic serialization while a map is ticking, and the two peers can
    /// legitimately visit different fallback branches. Contain those calls by
    /// stable pawn identity so serialization cannot advance the map Rand stream.
    /// </summary>
    internal static class Patch_RjwSerializationRandIsolation
    {
        private const string RjwSexPartCompTypeName = "rjw.HediffComp_SexPart";
        private const string SizedApparelCompTypeName = "SizedApparel.ApparelRecorderComp";
        private const string MenstruationCompTypeName =
            "RJW_Menstruation.HediffComp_Menstruation";

        private static int _traceCount;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            NormalizeDuplicateMenstruationComps();

            PatchTarget(
                harmony,
                RjwSexPartCompTypeName,
                "CompExposeData",
                nameof(RjwPrefix),
                "RJW sex-part CompExposeData");
            PatchTarget(
                harmony,
                SizedApparelCompTypeName,
                "PostExposeData",
                nameof(SizedApparelPrefix),
                "Sized Apparel PostExposeData");
        }

        /// <summary>
        /// Several race integration mods can append their own menstruation comp
        /// to the same genital HediffDef.  Derived menstruation comps inherit the
        /// base CompExposeData implementation, so two of them serialize identical
        /// labels (notably "pregnancy") beneath the same Hediff parent.  RimWorld's
        /// reference loader then registers that path twice and a joining client can
        /// fail to resolve the pregnancy reference before simulation starts.
        ///
        /// A genital hediff represents one womb cycle, so retain one component.
        /// Prefer a more-derived component (for example InducedOvulator over the
        /// generic Menstruation comp); preserve definition order for sibling types.
        /// Def order is identical on all peers, making the normalization deterministic.
        /// </summary>
        private static void NormalizeDuplicateMenstruationComps()
        {
            Type menstruationCompType = AccessTools.TypeByName(MenstruationCompTypeName);
            if (menstruationCompType == null)
                return;

            int affectedDefs = 0;
            int removedComps = 0;
            foreach (HediffDef def in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                List<HediffCompProperties> comps = def?.comps;
                if (comps == null || comps.Count < 2)
                    continue;

                var candidates = comps
                    .Where(properties =>
                        properties?.compClass != null &&
                        menstruationCompType.IsAssignableFrom(properties.compClass))
                    .ToList();
                if (candidates.Count < 2)
                    continue;

                HediffCompProperties retained = candidates[0];
                for (int index = 1; index < candidates.Count; index++)
                {
                    Type retainedType = retained.compClass;
                    Type candidateType = candidates[index].compClass;
                    if (retainedType != candidateType &&
                        retainedType.IsAssignableFrom(candidateType))
                    {
                        retained = candidates[index];
                    }
                }

                var removedTypes = new List<string>();
                for (int index = comps.Count - 1; index >= 0; index--)
                {
                    HediffCompProperties properties = comps[index];
                    if (ReferenceEquals(properties, retained) ||
                        properties == null ||
                        properties.compClass == null ||
                        !menstruationCompType.IsAssignableFrom(properties.compClass))
                    {
                        continue;
                    }

                    removedTypes.Add(properties.compClass.FullName);
                    comps.RemoveAt(index);
                    removedComps++;
                }

                affectedDefs++;
                removedTypes.Reverse();
                Log.Warning(
                    $"[MP-MeowOnlineShop][RJW] Removed duplicate menstruation comps from " +
                    $"{def.defName}: kept={retained.compClass.FullName}, " +
                    $"removed={string.Join(",", removedTypes.ToArray())}. This prevents " +
                    "duplicate pregnancy reference paths during multiplayer snapshot loading.");
            }

            if (affectedDefs > 0)
            {
                Log.Message(
                    $"[MP-MeowOnlineShop][RJW] Menstruation comp normalization complete: " +
                    $"defs={affectedDefs}, removed={removedComps}.");
            }
        }

        private static void PatchTarget(
            Harmony harmony,
            string typeName,
            string methodName,
            string prefixName,
            string label)
        {
            Type type = AccessTools.TypeByName(typeName);
            if (type == null)
                return;

            var target = AccessTools.Method(type, methodName);
            if (target == null || target.ReturnType != typeof(void) ||
                target.GetParameters().Length != 0)
            {
                Log.Warning(
                    $"[MP-MeowOnlineShop][RJW] {label} signature drift; " +
                    "serialization Rand isolation was not installed.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwSerializationRandIsolation),
                    prefixName))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwSerializationRandIsolation),
                    nameof(Finalizer)))
                {
                    priority = Priority.Last
                });

            Log.Message(
                $"[MP-MeowOnlineShop][RJW] {label} Rand isolation active in multiplayer.");
        }

        private static void RjwPrefix(HediffComp __instance, ref bool __state)
        {
            Pawn pawn = __instance?.parent?.pawn;
            Begin(pawn, 0x52574A53, "RJW_SEXPART_SCRIBE", ref __state);
        }

        private static void SizedApparelPrefix(ThingComp __instance, ref bool __state)
        {
            Begin(__instance?.parent as Pawn, 0x53415A45, "SIZEDAPPAREL_SCRIBE", ref __state);
        }

        private static void Begin(Pawn pawn, int salt, string label, ref bool state)
        {
            state = false;
            if (!MP.IsInMultiplayer || pawn == null)
                return;

            int seed = Gen.HashCombineInt(salt, pawn.thingIDNumber);
            seed = Gen.HashCombineInt(seed, (int)Scribe.mode);
            Rand.PushState(seed);
            state = true;

            if (++_traceCount <= 8 && IsRjwAddonTestEnabled())
            {
                Log.Message(
                    $"[MP-MeowOnlineShop][RJW][test] {label} pawn={pawn.thingIDNumber} " +
                    $"mode={Scribe.mode} seed={seed} ordinal={_traceCount}");
            }
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        private static bool IsRjwAddonTestEnabled()
        {
            string enabledText;
            return GenCommandLine.TryGetCommandLineArg(
                       "mpautotestrjwaddons",
                       out enabledText) &&
                   bool.TryParse(enabledText, out var enabled) &&
                   enabled;
        }
    }
}
