using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianShieldState
    {
        static Type shieldType;
        static FieldInfo lastIntercept, activeShields, laserDefenders;

        internal static void Apply(Harmony harmony)
        {
            shieldType = AccessTools.TypeByName("Nivarian_Race.Code.Comps.ThingComps.ThingComp_ProjectileDefence")
                ?? throw new TypeLoadException("Nivarian projectile defence");
            lastIntercept = AccessTools.Field(shieldType, "lastInterceptTick")
                ?? throw new MissingFieldException(shieldType.FullName, "lastInterceptTick");
            var manager = AccessTools.TypeByName("Nivarian.MapComp_ShieldManager")
                ?? throw new TypeLoadException("Nivarian shield manager");
            activeShields = AccessTools.Field(manager, "ActiveShields")
                ?? throw new MissingFieldException(manager.FullName, "ActiveShields");
            laserDefenders = AccessTools.Field(manager, "LaserDefenders")
                ?? throw new MissingFieldException(manager.FullName, "LaserDefenders");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingWithComps), "ExposeData")
                ?? throw new MissingMethodException("ThingWithComps.ExposeData"),
                postfix: new HarmonyMethod(typeof(NivarianShieldState), nameof(ExposeCooldowns)));
            harmony.Patch(AccessTools.DeclaredMethod(manager, "CleanupInvalidEntries", Type.EmptyTypes)
                ?? throw new MissingMethodException(manager.FullName, "CleanupInvalidEntries"),
                postfix: new HarmonyMethod(typeof(NivarianShieldState), nameof(OrderShields)));
            Log.Message("[TaleNivarianCompat] projectile shield cooldowns saved; stable interception priority.");
        }

        // Save even outside an MP session so hosting an existing save preserves its cooldowns.
        static void ExposeCooldowns(ThingWithComps __instance)
        {
            var comps = __instance.AllComps;
            for (var i = 0; i < comps.Count; i++)
            {
                var comp = comps[i];
                if (!shieldType.IsInstanceOfType(comp)) continue;
                var tick = (int)lastIntercept.GetValue(comp);
                Scribe_Values.Look(ref tick, "meowNivarianInterceptTick_" + i, -999999);
                if (Scribe.mode == LoadSaveMode.LoadingVars) lastIntercept.SetValue(comp, tick);
            }
        }

        // Native interception cleans the list before selecting the first eligible shield.
        // Use the item's ID (not its wearer, who may wear several shields), then comp index.
        static void OrderShields(object __instance)
        {
            if (!MP.IsInMultiplayer) return;
            var list = (IList)activeShields.GetValue(__instance);
            for (var i = 1; i < list.Count; i++)
            {
                var value = (ThingComp)list[i];
                var j = i - 1;
                while (j >= 0 && Compare((ThingComp)list[j], value) > 0)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = value;
            }
            // Royalty reflective shields are Things, registered in this second list.
            // Leave unknown third-party non-Thing implementations in their original slots.
            var lasers = (IList)laserDefenders.GetValue(__instance);
            var slots = new System.Collections.Generic.List<int>();
            var things = new System.Collections.Generic.List<Thing>();
            for (var i = 0; i < lasers.Count; i++)
                if (lasers[i] is Thing thing) { slots.Add(i); things.Add(thing); }
            things.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            for (var i = 0; i < slots.Count; i++) lasers[slots[i]] = things[i];
        }

        static int Compare(ThingComp a, ThingComp b)
        {
            var order = a.parent.thingIDNumber.CompareTo(b.parent.thingIDNumber);
            return order != 0 ? order : a.parent.AllComps.IndexOf(a).CompareTo(b.parent.AllComps.IndexOf(b));
        }
    }
}
