using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynLanternCache
    {
        sealed class Capture
        {
            internal readonly bool[] Detached = new bool[2];
            internal readonly Pawn[] Owners = new Pawn[2];
        }
        static readonly ConditionalWeakTable<ThingComp, Capture> captures = new ConditionalWeakTable<ThingComp, Capture>();
        static FieldInfo[] fields;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("ASEL.CompLightCharge") ?? throw new TypeLoadException("ASEL.CompLightCharge");
            fields = new[] { AccessTools.DeclaredField(type, "lcI"), AccessTools.DeclaredField(type, "faith") };
            foreach (var field in fields)
                if (field?.FieldType != typeof(Hediff)) throw new MissingFieldException(type.FullName, "native Hediff cache");
            var expose = AccessTools.DeclaredMethod(type, "PostExposeData", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "PostExposeData");
            harmony.Patch(expose, postfix: new HarmonyMethod(typeof(MonolynLanternCache), nameof(Save)));
            Log.Message("[MonolynLanternCache] resolved=2 native calculator/faith captures, owned references and detached cache persistence; missing=0");
        }
        static void Save(ThingComp __instance)
        {
            if (MonolynLanternCanonical.TrySave(__instance)) return;
            var capture = captures.GetValue(__instance, _ => new Capture());
            for (int i = 0; i < fields.Length; i++)
            {
                var value = (Hediff)fields[i].GetValue(__instance);
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    // The native CompTick retains the original calculator/faith
                    // object after apparel transfer or hediff removal. Keep that
                    // object, rather than resolving a different wearer's hediff.
                    capture.Detached[i] = value != null && (value.pawn == null || value.pawn.Discarded ||
                        !value.pawn.health.hediffSet.hediffs.Contains(value));
                    capture.Owners[i] = capture.Detached[i] ? value?.pawn : null;
                }
                string key = "meowLanternCache_" + fields[i].Name;
                Scribe_Values.Look(ref capture.Detached[i], key + "Detached");
                // Native Hediff.ExposeData omits pawn: a health tracker normally
                // restores it. Detached captures have no tracker to do that.
                Scribe_References.Look(ref capture.Owners[i], key + "Pawn",
                    capture.Owners[i] != null && !capture.Owners[i].Discarded);
                var reference = capture.Detached[i] ? null : value;
                var detached = capture.Detached[i] ? value : null;
                Scribe_References.Look(ref reference, key + "Reference");
                Scribe_Deep.Look(ref detached, key + "DetachedHediff");
                if (detached != null && Scribe.mode != LoadSaveMode.Saving) detached.pawn = capture.Owners[i];
                fields[i].SetValue(__instance, capture.Detached[i] ? detached : reference);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) captures.Remove(__instance);
        }
    }
}
