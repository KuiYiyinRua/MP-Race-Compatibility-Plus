using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynStages
    {
        static FieldInfo cachedStage;
        static FieldInfo bandStage;
        static Func<bool> nativeTicking;
        sealed class ReadState { internal HediffStage Stage; }
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.Hediff_PurificationMN") ?? throw new TypeLoadException("ASEL.Hediff_PurificationMN");
            cachedStage = AccessTools.DeclaredField(type, "curStage");
            if (cachedStage?.FieldType != typeof(HediffStage)) throw new MissingFieldException(type.FullName, "curStage");
            var multiplayer = AccessTools.TypeByName("Multiplayer.Client.Multiplayer") ?? throw new TypeLoadException("Multiplayer.Client.Multiplayer");
            var ticking = AccessTools.DeclaredPropertyGetter(multiplayer, "Ticking");
            if (ticking == null || !ticking.IsStatic || ticking.ReturnType != typeof(bool))
                throw new MissingMemberException(multiplayer.FullName, "Ticking");
            nativeTicking = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), ticking);
            harmony.Patch(AccessTools.DeclaredPropertyGetter(type, "CurStage") ?? throw new MissingMemberException(type.FullName, "CurStage"),
                prefix: new HarmonyMethod(typeof(MonolynStages), nameof(BeforeRead)), finalizer: new HarmonyMethod(typeof(MonolynStages), nameof(AfterRead)));
            harmony.Patch(AccessTools.DeclaredMethod(type, "ExposeData", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "ExposeData"),
                postfix: new HarmonyMethod(typeof(MonolynStages), nameof(Save)));
            var band = AccessTools.TypeByName("ASEL.Hediff_BandNodeMN") ?? throw new TypeLoadException("ASEL.Hediff_BandNodeMN");
            bandStage = AccessTools.DeclaredField(band, "curStage");
            if (bandStage?.FieldType != typeof(HediffStage)) throw new MissingFieldException(band.FullName, "curStage");
            harmony.Patch(AccessTools.DeclaredPropertyGetter(band, "CurStage"),
                prefix: new HarmonyMethod(typeof(MonolynStages), nameof(BeforeRead)), finalizer: new HarmonyMethod(typeof(MonolynStages), nameof(AfterRead)));
            harmony.Patch(AccessTools.DeclaredMethod(band, "ExposeData"), postfix: new HarmonyMethod(typeof(MonolynStages), nameof(SaveBand)));
            Log.Message("[MonolynStages] resolved=purification stat-stage cache persistence and read-only interface/loading hydration; missing=0");
        }
        static FieldInfo StageField(Hediff owner) => cachedStage.DeclaringType.IsInstanceOfType(owner) ? cachedStage : bandStage;
        static void BeforeRead(Hediff __instance, out ReadState __state)
        {
            // MP.InInterface excludes its loader/long events. Those readers also
            // must preserve a saved null cache; native map/world ticks and synced
            // commands keep the native lazy initialization and 100-tick phase.
            bool readOnly = MP.IsInMultiplayer && !nativeTicking() && !MP.IsExecutingSyncCommand;
            __state = readOnly ? new ReadState { Stage = (HediffStage)StageField(__instance).GetValue(__instance) } : null;
        }
        static Exception AfterRead(Hediff __instance, Exception __exception, ReadState __state)
        {
            if (__state != null) StageField(__instance).SetValue(__instance, __state.Stage);
            return __exception;
        }
        static void Save(Hediff __instance)
        {
            var stage = (HediffStage)cachedStage.GetValue(__instance);
            bool present = stage != null;
            float flammability = present ? stage.statOffsets.Single(s => s.stat == StatDefOf.Flammability).value : 0f;
            float armor = present ? stage.statOffsets.Single(s => s.stat == StatDefOf.ArmorRating_Heat).value : 0f;
            Scribe_Values.Look(ref present, "meowPurificationStagePresent", false, true);
            Scribe_Values.Look(ref flammability, "meowPurificationFlammability");
            Scribe_Values.Look(ref armor, "meowPurificationHeatArmor");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                cachedStage.SetValue(__instance, present ? new HediffStage { statOffsets = new List<StatModifier> {
                    new StatModifier { stat = StatDefOf.Flammability, value = flammability },
                    new StatModifier { stat = StatDefOf.ArmorRating_Heat, value = armor }
                } } : null);
        }
        static void SaveBand(Hediff __instance)
        {
            var stage = (HediffStage)bandStage.GetValue(__instance);
            bool present = stage != null;
            float bandwidth = present ? stage.statOffsets.Single(s => s.stat == StatDefOf.MechBandwidth).value : 0f;
            float consciousness = present ? stage.capMods.Single(c => c.capacity == PawnCapacityDefOf.Consciousness).offset : 0f;
            Scribe_Values.Look(ref present, "meowBandStagePresent", false, true);
            Scribe_Values.Look(ref bandwidth, "meowBandStageBandwidth");
            Scribe_Values.Look(ref consciousness, "meowBandStageConsciousness");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                bandStage.SetValue(__instance, present ? new HediffStage {
                    statOffsets = new List<StatModifier> { new StatModifier { stat = StatDefOf.MechBandwidth, value = bandwidth } },
                    capMods = new List<PawnCapacityModifier> { new PawnCapacityModifier { capacity = PawnCapacityDefOf.Consciousness, offset = consciousness } }
                } : null);
        }
    }
}
