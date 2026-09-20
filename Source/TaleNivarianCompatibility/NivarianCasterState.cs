using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianCasterState
    {
        static Type hediffCaster, thingCaster, healing, cryo;
        static FieldInfo hediffPawn, thingPawn, healingCache, cryoCache, interval;
        static MethodInfo recalculate;

        internal static void Apply(Harmony harmony)
        {
            hediffCaster = AccessTools.TypeByName("Nivarian.HediffCompCaster") ?? throw new TypeLoadException("Nivarian.HediffCompCaster");
            thingCaster = AccessTools.TypeByName("Nivarian.ThingCompCaster") ?? throw new TypeLoadException("Nivarian.ThingCompCaster");
            hediffPawn = RequiredField(hediffCaster, "caster");
            thingPawn = RequiredField(thingCaster, "caster");
            healing = AccessTools.TypeByName("Nivarian.HediffComp_HealingBooster");
            cryo = AccessTools.TypeByName("Nivarian.HediffComp_CryoEnchant");
            if (healing != null)
            {
                healingCache = RequiredField(healing, "_casterComp");
                var tend = AccessTools.DeclaredMethod(healing, "TendOneInjury", Type.EmptyTypes) ?? throw new MissingMethodException(healing.FullName, "TendOneInjury");
                harmony.Patch(tend, prefix: new HarmonyMethod(typeof(NivarianCasterState), nameof(HasTendableInjury)));
                interval = RequiredField(healing, "_effectiveTendIntervalTicks");
                recalculate = AccessTools.DeclaredMethod(healing, "RecalculateTendInterval", Type.EmptyTypes)
                    ?? throw new MissingMethodException(healing.FullName, "RecalculateTendInterval");
            }
            if (cryo != null) cryoCache = RequiredField(cryo, "_casterComp");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(HediffWithComps), "ExposeData"),
                postfix: new HarmonyMethod(typeof(NivarianCasterState), nameof(ExposeHediff)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), "ExposeData"),
                postfix: new HarmonyMethod(typeof(NivarianCasterState), nameof(ExposeThing)));
            Log.Message("[TaleNivarianCompat] Nivarian caster references and ability load caches installed.");
        }

        // Keep native selection and tending intact whenever a candidate exists.
        static bool HasTendableInjury(HediffComp __instance)
        {
            foreach (var hediff in __instance.Pawn.health.hediffSet.hediffs)
                if (hediff is Hediff_Injury injury && injury.TendableNow(true)) return true;
            return false;
        }
        static FieldInfo RequiredField(Type type, string name) => AccessTools.DeclaredField(type, name)
            ?? throw new MissingFieldException(type.FullName, name);

        static void ExposeCaster(object comp, FieldInfo field)
        {
            var pawn = (Pawn)field.GetValue(comp);
            Scribe_References.Look(ref pawn, "meowNivarianCaster");
            field.SetValue(comp, pawn);
        }

        static void ExposeThing(ThingWithComps __instance)
        {
            foreach (var comp in __instance.AllComps)
                if (thingCaster.IsInstanceOfType(comp)) ExposeCaster(comp, thingPawn);
        }

        static void ExposeHediff(HediffWithComps __instance)
        {
            if (__instance.comps == null) return;
            object casterComp = null;
            foreach (var comp in __instance.comps)
                if (hediffCaster.IsInstanceOfType(comp))
                {
                    casterComp = comp;
                    ExposeCaster(comp, hediffPawn);
                }
            foreach (var comp in __instance.comps)
            {
                if (healing != null && healing.IsInstanceOfType(comp))
                {
                    // Preserve the interval computed when applied, even if caster power later changes.
                    int ticks = (int)interval.GetValue(comp);
                    Scribe_Values.Look(ref ticks, "meowNivarianTendInterval", -1);
                    if (Scribe.mode == LoadSaveMode.LoadingVars) interval.SetValue(comp, ticks);
                    if (Scribe.mode == LoadSaveMode.PostLoadInit)
                    {
                        healingCache.SetValue(comp, casterComp);
                        // Legacy saves have no interval or caster reference; do not invent a caster.
                        if ((int)interval.GetValue(comp) <= 0) recalculate.Invoke(comp, null);
                    }
                }
                if (cryo != null && cryo.IsInstanceOfType(comp) && Scribe.mode == LoadSaveMode.PostLoadInit)
                    cryoCache.SetValue(comp, casterComp);
            }
        }
    }
}
