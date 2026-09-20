using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class LazySimulationCaches
    {
        static GeneDef[] skinGenes;
        internal static void Apply(Harmony harmony)
        {
            // Resolve on the main thread once; visual worker threads must never populate
            // PawnSkinColors' lazy static list or touch Verse.Rand's global state stack.
            skinGenes = DefDatabase<GeneDef>.AllDefs
                .Where(g => (g.endogeneCategory == EndogeneCategory.Melanin || !(g.minMelanin >= 0f)) && g.skinColorBase.HasValue)
                .OrderBy(g => g.minMelanin).ThenBy(g => g.defName, StringComparer.Ordinal).ToArray();
            Bootstrap.Field(typeof(Hediff), "abilities", typeof(List<Ability>));
            Bootstrap.Field(typeof(Pawn_StoryTracker), "pawn", typeof(Pawn));
            Bootstrap.Field(typeof(Pawn_StoryTracker), "skinColorBase", typeof(Color?));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Hediff), nameof(Hediff.AllAbilitiesForReading)),
                prefix: new HarmonyMethod(typeof(LazySimulationCaches), nameof(Abilities)) { priority = Priority.First });
            harmony.Patch(AccessTools.PropertyGetter(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.SkinColorBase)),
                prefix: new HarmonyMethod(typeof(LazySimulationCaches), nameof(SkinColor)) { priority = Priority.First });
        }

        internal static bool Abilities(List<Ability> ___abilities, ref List<Ability> __result)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            // Match MP's outer ability-tracker guard at the actual lazy allocation boundary.
            // Do not assign an empty cache: that would prevent later simulation initialization.
            __result = ___abilities ?? new List<Ability>();
            return false;
        }

        internal static bool SkinColor(Pawn ___pawn, Color? ___skinColorBase, ref Color __result)
        {
            if (!MP.IsInMultiplayer || ___skinColorBase.HasValue || ___pawn?.genes == null) return true;
            var gene = ___pawn.genes.GetMelaninGene();
            if (gene == null)
            {
                // Legacy/modded pawns can lack both the color and a melanin gene. The original
                // getter randomly ADDS a gene during graphics initialization. Merely saving RNG
                // around drawing would leave that gameplay mutation local. Compute a stable,
                // read-only fallback instead, equally in UI and simulation; existing genes/colors
                // are preserved. No persistent cache or process-local initialization timing.
                gene = FallbackSkinGene(___pawn);
            }
            __result = gene?.skinColorBase ?? Color.white;
            return false;
        }

        internal static GeneDef FallbackSkinGene(Pawn pawn)
        {
            if (skinGenes.Length == 0) return null;
            // Stateless integer mixer, independent of runtime string hashes, process RNG,
            // viewing map, thread, or order of reads. Keep vanilla range/weight selection.
            uint key = unchecked((uint)pawn.thingIDNumber + 0x4D534B49u);
            key = (key ^ (key >> 16)) * 0x7feb352du;
            key = (key ^ (key >> 15)) * 0x846ca68bu;
            key ^= key >> 16;
            double unit = key / 4294967296.0;
            if (pawn.Faction != null)
            {
                var range = pawn.Faction.def.melaninRange;
                float melanin = (float)(range.min + (range.max - range.min) * unit);
                for (int i = skinGenes.Length - 1; i >= 0; i--)
                    if (melanin >= skinGenes[i].minMelanin) return skinGenes[i];
            }
            else
            {
                double total = 0;
                foreach (var g in skinGenes) if (g.selectionWeight > 0) total += g.selectionWeight;
                double choice = total * unit;
                foreach (var g in skinGenes)
                {
                    if (g.selectionWeight <= 0) continue;
                    choice -= g.selectionWeight;
                    if (choice < 0) return g;
                }
            }
            return skinGenes[(int)(unit * skinGenes.Length)];
        }
    }
}
