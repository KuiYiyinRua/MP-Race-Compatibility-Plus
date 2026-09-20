using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Derive ownership from saved buildings, never from the local player's faction.
    internal static class NivarianMonumentOwnership
    {
        static Type component;
        static FieldInfo count;
        static Func<Pawn, bool> isNivarian;
        static Action<Map, Faction, bool> push;
        static Func<Map, Faction> pop;
        static MethodInfo addHediff, removeHediff;

        internal static void Apply(Harmony harmony)
        {
            component = Required("Nivarian.GameComp_NivarianRebirthMonument");
            count = AccessTools.Field(component, "_liveMonumentCount") ?? throw new MissingFieldException(component.FullName, "_liveMonumentCount");
            var utility = Required("Nivarian_Race.Code.Helper.RebirthMonumentEffectUtility");
            var comp = Required("Nivarian_Race.Code.Comps.BuildingComps.Comp_RebirthMonument");
            var context = Required("Multiplayer.Client.Factions.FactionExtensions");
            push = (Action<Map, Faction, bool>)Delegate.CreateDelegate(typeof(Action<Map, Faction, bool>), AccessTools.DeclaredMethod(context, "PushFaction", new[] { typeof(Map), typeof(Faction), typeof(bool) }));
            pop = (Func<Map, Faction>)Delegate.CreateDelegate(typeof(Func<Map, Faction>), AccessTools.DeclaredMethod(context, "PopFaction", new[] { typeof(Map) }));
            isNivarian = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), AccessTools.DeclaredMethod(Required("PawnExtensions"), "IsNivarian", new[] { typeof(Pawn) }));
            addHediff = AccessTools.DeclaredMethod(utility, "TryAddHediff") ?? throw new MissingMethodException(utility.FullName, "TryAddHediff");
            removeHediff = AccessTools.DeclaredMethod(utility, "TryRemoveHediff") ?? throw new MissingMethodException(utility.FullName, "TryRemoveHediff");
            Prefix(harmony, AccessTools.PropertyGetter(component, "IsActive"), nameof(Active));
            Prefix(harmony, AccessTools.DeclaredMethod(component, "RebuildFromWorld"), nameof(Rebuild));
            foreach (var name in new[] { "NotifyMonumentSpawned", "NotifyMonumentDestroyed" })
                Prefix(harmony, AccessTools.DeclaredMethod(component, name), nameof(Notify));
            Prefix(harmony, AccessTools.DeclaredMethod(comp, "PostSpawnSetup"), nameof(Spawn));
            Prefix(harmony, AccessTools.DeclaredMethod(comp, "PostDestroy"), nameof(Destroy));
            Prefix(harmony, AccessTools.DeclaredMethod(utility, "ShouldHaveColonistBlessing"), nameof(Blessing));
            Prefix(harmony, AccessTools.DeclaredMethod(utility, "ShouldHaveMechBackdoor"), nameof(Backdoor));
            Prefix(harmony, AccessTools.DeclaredMethod(Required("Nivarian_Race.Code.Patches.Patch_RebirthMonument_PawnSpawn"), "Postfix"), nameof(PawnLoaded));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.SetFaction), new[] { typeof(Faction), typeof(Pawn) }), postfix: new HarmonyMethod(typeof(NivarianMonumentOwnership), nameof(FactionChanged)));
            Log.Message("[TaleNivarianCompat] monument effects derive from actual owner factions; load-time pawn effects preserved.");
        }

        static Type Required(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        static void Prefix(Harmony harmony, MethodInfo method, string prefix)
        {
            if (method == null) throw new MissingMethodException("Monument ownership target for " + prefix);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(NivarianMonumentOwnership), prefix));
        }
        static List<Thing> Monuments()
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("Nivarian_RebirthMonument");
            if (def == null) return new List<Thing>();
            return Find.Maps.SelectMany(m => m.listerThings.ThingsOfDef(def))
                .Where(t => t.Spawned && !t.Destroyed && t.Faction?.def?.isPlayer == true)
                .OrderBy(t => t.thingIDNumber).ToList();
        }
        static bool Active(ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = Monuments().Count != 0;
            return false;
        }
        static bool Rebuild(object __instance)
        {
            if (!MP.IsInMultiplayer) return true;
            count.SetValue(__instance, Monuments().Count);
            return false;
        }
        static bool Notify()
        {
            if (!MP.IsInMultiplayer) return true;
            Refresh();
            return false;
        }
        static bool Spawn(bool respawningAfterLoad)
        {
            if (!MP.IsInMultiplayer) return true;
            if (!respawningAfterLoad) Refresh();
            return false;
        }
        static bool Destroy()
        {
            if (!MP.IsInMultiplayer) return true;
            Refresh();
            return false;
        }
        static bool PawnLoaded(bool respawningAfterLoad) => !MP.IsInMultiplayer || !respawningAfterLoad;
        static void FactionChanged(Thing __instance)
        {
            if (MP.IsInMultiplayer && __instance.Spawned && (__instance is Pawn || __instance.def.defName == "Nivarian_RebirthMonument")) Refresh();
        }
        static bool Colonist(Pawn pawn, HashSet<Faction> owners)
        {
            if (pawn?.Faction == null || pawn.Map == null || !owners.Contains(pawn.Faction)) return false;
            var saved = new SavedMapManagers(pawn.Map);
            push(pawn.Map, pawn.Faction, true);
            try { return pawn.IsFreeColonist && isNivarian(pawn); }
            finally { try { pop(pawn.Map); } finally { saved.Restore(); } }
        }
        static bool Mech(Pawn pawn, HashSet<Faction> owners) => pawn?.RaceProps.IsMechanoid == true && owners.Any(f => pawn.HostileTo(f));
        static HashSet<Faction> Owners() => new HashSet<Faction>(Monuments().Select(t => t.Faction));
        static bool Blessing(Pawn pawn, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = Colonist(pawn, Owners());
            return false;
        }
        static bool Backdoor(Pawn pawn, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            __result = Mech(pawn, Owners());
            return false;
        }
        static void Refresh()
        {
            var monuments = Monuments();
            var owners = new HashSet<Faction>(monuments.Select(t => t.Faction));
            var current = Current.Game?.components.FirstOrDefault(c => component.IsInstanceOfType(c));
            if (current != null) count.SetValue(current, monuments.Count);
            var blessing = DefDatabase<HediffDef>.GetNamedSilentFail("Nivarian_RebirthMonument_Blessing");
            var backdoor = DefDatabase<HediffDef>.GetNamedSilentFail("Nivarian_RebirthMonument_MechBackdoor");
            foreach (var pawn in Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).OrderBy(p => p.thingIDNumber).ToList())
            {
                if (pawn.Dead || pawn.health?.hediffSet == null) continue;
                (Colonist(pawn, owners) ? addHediff : removeHediff).Invoke(null, new object[] { pawn, blessing });
                (Mech(pawn, owners) ? addHediff : removeHediff).Invoke(null, new object[] { pawn, backdoor });
            }
        }
    }
}
