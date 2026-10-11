using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Meow.RaceTrioCompatibility
{
    // Only fields proven to affect later native simulation and omitted by native Scribe.
    internal static class MonolynProgress
    {
        sealed class Slot
        {
            internal FieldInfo Field;
            internal object Default;
        }
        static readonly Dictionary<Type, List<Slot>> slots = new Dictionary<Type, List<Slot>>();
        sealed class Rates { internal bool Present; }
        static readonly ConditionalWeakTable<object, Rates> rates = new ConditionalWeakTable<object, Rates>();

        internal static void Apply(Harmony harmony)
        {
            MonolynSigil.Apply(harmony);
            Add("ASEL.CompLightBuilder", "Start", false);
            Add("ASEL.MNPunishment", "warmupTicks", 1);
            Add("ASEL.Building_TowerOfLight", "deliveranceTick", 60000);
            Add("ASEL.Building_MonolynProducer", "insufficientLight", false);
            Add("ASEL.Building_Crucible", "lastUsedTickVenting", -210000);
            Add("ASEL.Building_ConstructFormator", "Mechanitor", null, typeof(Pawn));
            Add("ASEL.CompLightConsumer", "tick", 500);
            Add("ASEL.CompDeathEffect", "startStorage", false);
            Add("ASEL.CompDeathEffect", "ticks", 0);
            Add("ASEL.CompDeathEffect", "corpse2", null, typeof(Corpse));
            Add("ASEL.CompDeathEffect", "pawn2", null, typeof(Pawn));
            Add("ASEL.HediffComp_MeditationSeverity", "ticks", 0);
            Add("ASEL.HediffComp_MeditationSeverity", "ticksB", 0);
            Add("ASEL.HediffComp_MeditationSeverity", "ticksC", 0);
            Add("ASEL.GameCondition_TheRust", "num", 0);
            Add("ASEL.RustFilth", "interval", 1600);
            Add("ASEL.Well", "rustTime", 60000);
            Add("ASEL.Hediff_PurificationMN", "damageAmount", 0);
            Add("ASEL.Hediff_PurificationMN", "casterB", null, typeof(Pawn));
            Add("ASEL.JobDriver_MNPray", "prayEfficiency", 0f);
            Add("ASEL.CompAbilityEffect_Punishment", "AICharge", 2);
            Add("ASEL.Comp_MNC", "lightConsumption", 0f);
            Add("ASEL.Comp_MNC", "chargeFactor", 1f);
            Add("ASEL.PillarOfSalt", "ep", default(IntVec3));
            Add("ASEL.PillarOfSalt", "breaker", null, typeof(Pawn));
            Add("ASEL.PillarOfSalt", "armorPenetration", 0f);
            Add("ASEL.Building_MNWorkTable", "activeTick", 0);
            Add("ASEL.Bill_ResurrectFont", "cp", null, typeof(Corpse));
            Add("ASEL.Bill_ResurrectFont", "ppp", null, typeof(Pawn));
            Add("ASEL.Bill_ResurrectMech", "cp", null, typeof(Corpse));
            Add("ASEL.Bill_ResurrectMech", "ppp", null, typeof(Pawn));

            Patch(harmony, typeof(Thing), nameof(Thing.ExposeData));
            Patch(harmony, typeof(ThingComp), nameof(ThingComp.PostExposeData));
            Patch(harmony, typeof(Hediff), nameof(Hediff.ExposeData));
            Patch(harmony, typeof(HediffComp), nameof(HediffComp.CompExposeData));
            Patch(harmony, typeof(GameCondition), nameof(GameCondition.ExposeData));
            Patch(harmony, typeof(JobDriver), nameof(JobDriver.ExposeData));
            Patch(harmony, typeof(AbilityComp), nameof(AbilityComp.PostExposeData));
            Patch(harmony, typeof(Bill), nameof(Bill.ExposeData));
            var mech = AccessTools.TypeByName("ASEL.Comp_MNC");
            harmony.Patch(AccessTools.DeclaredMethod(mech, "PostSpawnSetup", new[] { typeof(bool) }) ?? throw new MissingMethodException(mech.FullName, "PostSpawnSetup"),
                prefix: new HarmonyMethod(typeof(MonolynProgress), nameof(BeforeMechSpawn)), postfix: new HarmonyMethod(typeof(MonolynProgress), nameof(AfterMechSpawn)));
            Log.Message("[MonolynProgress] resolved=31 verified native progress fields across 20 types, inherited progress and mech cached rates; missing=0");
        }
        static void Patch(Harmony harmony, Type root, string method)
        {
            var target = AccessTools.DeclaredMethod(root, method, Type.EmptyTypes) ?? throw new MissingMethodException(root.FullName, method);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(MonolynProgress), nameof(Save)));
        }
        static void Add(string name, string fieldName, object initial, Type explicitType = null)
        {
            var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
            var field = AccessTools.DeclaredField(type, fieldName);
            if (field == null || field.FieldType != (explicitType ?? initial.GetType())) throw new MissingFieldException(name, fieldName);
            if (!slots.TryGetValue(type, out var list)) slots.Add(type, list = new List<Slot>());
            list.Add(new Slot { Field = field, Default = initial });
        }
        static void Save(object __instance)
        {
            var list = Slots(__instance.GetType());
            if (list == null) return;
            foreach (var slot in list)
            {
                string key = "meowProgress_" + slot.Field.Name;
                object value = slot.Field.GetValue(__instance);
                if (slot.Field.FieldType == typeof(bool)) { bool v = (bool)value; Scribe_Values.Look(ref v, key, (bool)slot.Default); value = v; }
                else if (slot.Field.FieldType == typeof(int)) { int v = (int)value; Scribe_Values.Look(ref v, key, (int)slot.Default); value = v; }
                else if (slot.Field.FieldType == typeof(float)) { float v = (float)value; Scribe_Values.Look(ref v, key, (float)slot.Default); value = v; }
                else if (slot.Field.FieldType == typeof(Pawn))
                {
                    var v = (Pawn)value;
                    // Death storage and resurrection bills capture the killed
                    // pawn, still owned by its corpse/worldPawns. Native default
                    // reference saving nulls it.
                    string owner = slot.Field.DeclaringType.FullName;
                    bool deathCapture = owner == "ASEL.CompDeathEffect" && slot.Field.Name == "pawn2";
                    bool resurrectionCapture = (owner == "ASEL.Bill_ResurrectFont" || owner == "ASEL.Bill_ResurrectMech") && slot.Field.Name == "ppp";
                    bool mechanitorCapture = owner == "ASEL.Building_ConstructFormator" && slot.Field.Name == "Mechanitor";
                    bool deadPawn = (deathCapture || resurrectionCapture || mechanitorCapture) && v != null && !v.Discarded;
                    if (Multiplayer.API.MP.IsInMultiplayer) MonolynRetainedRoots.Look(ref v, key); else Scribe_References.Look(ref v, key, deadPawn);
                    value = v;
                }
                else if (slot.Field.FieldType == typeof(Corpse)) { var v = (Corpse)value; if (Multiplayer.API.MP.IsInMultiplayer) MonolynRetainedRoots.Look(ref v, key); else Scribe_References.Look(ref v, key); value = v; }
                else if (slot.Field.FieldType == typeof(IntVec3)) { var v = (IntVec3)value; Scribe_Values.Look(ref v, key, (IntVec3)slot.Default); value = v; }
                else throw new InvalidOperationException("Unexpected progress field " + slot.Field);
                // References must be retained through cross-reference resolution, not only LoadingVars.
                slot.Field.SetValue(__instance, value);
            }
            if (list[0].Field.DeclaringType.FullName == "ASEL.Comp_MNC")
            {
                bool present = true;
                Scribe_Values.Look(ref present, "meowProgressRatesPresent", false, true);
                if (Scribe.mode == LoadSaveMode.LoadingVars) rates.GetValue(__instance, x => new Rates()).Present = present;
            }
        }
        static List<Slot> Slots(Type type)
        {
            // Fabricators and work-table subclasses keep their progress in the
            // native base class, so exact runtime-type lookup would omit them.
            while (type != null)
            {
                if (slots.TryGetValue(type, out var list)) return list;
                type = type.BaseType;
            }
            return null;
        }
        static void BeforeMechSpawn(object __instance, out object[] __state)
        {
            var fields = Slots(__instance.GetType());
            __state = new object[fields.Count];
            for (int i = 0; i < fields.Count; i++) __state[i] = fields[i].Field.GetValue(__instance);
        }
        static void AfterMechSpawn(object __instance, bool respawningAfterLoad, object[] __state)
        {
            bool preserve = respawningAfterLoad && rates.TryGetValue(__instance, out var loaded) && loaded.Present;
            rates.Remove(__instance);
            if (!preserve) return;
            // These cached rates are initialized once on the host. Recomputing
            // with a later pawn's changed stats during a join changes simulation.
            var fields = Slots(__instance.GetType());
            for (int i = 0; i < fields.Count; i++) fields[i].Field.SetValue(__instance, __state[i]);
        }
    }
}
