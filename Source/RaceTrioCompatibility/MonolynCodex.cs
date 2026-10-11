using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynCodex
    {
        // Thanks to AzzyH, author of Multiplayer Monolyn Patch (3748145930),
        // for the separate synchronized Codex state/action boundary design.
        // Reference: AzzyH.MultiplayerMonolyn, MultiplayerMonolyn.dll.
        // This implementation invokes the installed ASEL callbacks to retain
        // their equipment, storage, job and effect behavior; no upstream source
        // or bundled API assemblies are redistributed.
        static Type outer, dogma, take;
        static MethodInfo takeNative;
        static FieldInfo takeOuter, takeWeapon, outerComp, outerStorage, outerPawn, outerHediff, weapons;
        static ISyncMethod takeSync;
        static ISyncMethod ensureSync;
        static HediffDef codexHediff;
        static FieldInfo healthPawn;

        internal static void Apply(Harmony harmony)
        {
            var comp = Require("ASEL.CompAbilityEffect_WeaponCodex");
            outer = Require(comp.FullName + "+<>c__DisplayClass4_0");
            dogma = Require(comp.FullName + "+<>c__DisplayClass4_1");
            take = Require(comp.FullName + "+<>c__DisplayClass4_2");
            outerComp = Field(outer, "<>4__this");
            outerStorage = Field(outer, "codex");
            outerPawn = Field(outer, "wearer");
            outerHediff = Field(outer, "h");
            takeOuter = Field(take, "CS$<>8__locals2");
            takeWeapon = Field(take, "twc");
            weapons = Field(Require("ASEL.CompWeaponCodexStorage"), "weapons");
            takeNative = Method(take, "<Apply>b__4");
            codexHediff = DefDatabase<HediffDef>.GetNamed("MNArsenalCodex");
            healthPawn = AccessTools.DeclaredField(typeof(Pawn_HealthTracker), "pawn");
            if (healthPawn?.FieldType != typeof(Pawn)) throw new MissingFieldException("Pawn_HealthTracker.pawn");
            ensureSync = MP.RegisterSyncMethod(typeof(MonolynCodex), nameof(EnsureMenuState));
            harmony.Patch(AccessTools.DeclaredMethod(comp, "Apply", new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) }),
                transpiler: new HarmonyMethod(typeof(MonolynCodex), nameof(MenuStateBoundary)));
            Method(outer, "<Apply>b__0");
            Method(dogma, "<Apply>b__3");
            MP.RegisterSyncDelegate(comp, outer.Name, "<Apply>b__0", new[] { "wearer", "codex", "<>4__this" })
                .CancelIfFieldsNull("wearer", "codex", "<>4__this");
            MP.RegisterSyncDelegate(comp, dogma.Name, "<Apply>b__3", new[] {
                "ability", "CS$<>8__locals1/wearer", "CS$<>8__locals1/codex",
                "CS$<>8__locals1/<>4__this" })
                .CancelIfFieldsNull("ability", "CS$<>8__locals1/wearer", "CS$<>8__locals1/codex", "CS$<>8__locals1/<>4__this");
            harmony.Patch(Method(outer, "<Apply>b__0"), prefix: new HarmonyMethod(typeof(MonolynCodex), nameof(StoreState)));
            harmony.Patch(Method(dogma, "<Apply>b__3"), prefix: new HarmonyMethod(typeof(MonolynCodex), nameof(DogmaState)));
            var mech = Require("ASEL.CompAbilityEffect_WeaponCodexMech");
            var mechOuter = Require(mech.FullName + "+<>c__DisplayClass4_0");
            var mechChoice = Require(mech.FullName + "+<>c__DisplayClass4_1");
            Field(mechOuter, "wearer"); Field(mechOuter, "<>4__this");
            Field(mechChoice, "ability"); Field(mechChoice, "CS$<>8__locals1");
            Method(mechChoice, "<Apply>b__2");
            MP.RegisterSyncDelegate(mech, mechChoice.Name, "<Apply>b__2", new[] {
                "ability", "CS$<>8__locals1/wearer", "CS$<>8__locals1/<>4__this" })
                .CancelIfFieldsNull("ability", "CS$<>8__locals1/wearer", "CS$<>8__locals1/<>4__this");
            takeSync = MP.RegisterSyncMethod(typeof(MonolynCodex), nameof(TakeWeapon));
            harmony.Patch(takeNative, prefix: new HarmonyMethod(typeof(MonolynCodex), nameof(TakePrefix)));
            Log.Message("[MonolynCodex] resolved=menu state, store, dogma, stored weapon, mech dogma; missing=0");
        }

        static bool TakePrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand) return true;
            var context = takeOuter.GetValue(__instance);
            var weapon = takeWeapon.GetValue(__instance) as Thing;
            if (weapon != null)
                takeSync.DoSync(null, outerComp.GetValue(context), outerStorage.GetValue(context),
                    weapon.thingIDNumber);
            return false;
        }

        static void TakeWeapon(AbilityComp comp, ThingComp storage, int weaponId)
        {
            var pawn = comp?.parent?.pawn;
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.equipment == null ||
                storage?.parent == null || storage.parent.Destroyed || !outerComp.FieldType.IsInstanceOfType(comp) ||
                !outerStorage.FieldType.IsInstanceOfType(storage)) return;
            // A menu can remain open after another player consumes its item.
            var list = weapons.GetValue(storage) as IList;
            ThingWithComps weapon = null;
            if (list != null)
                foreach (var item in list)
                    if (item is ThingWithComps candidate && candidate.thingIDNumber == weaponId) { weapon = candidate; break; }
            if (weapon == null || weapon.Destroyed || !weapon.def.IsWeapon) return;
            var context = Activator.CreateInstance(outer, true);
            outerComp.SetValue(context, comp);
            outerStorage.SetValue(context, storage);
            outerPawn.SetValue(context, pawn);
            outerHediff.SetValue(context, pawn.health.hediffSet.GetFirstHediffOfDef(codexHediff));
            var choice = Activator.CreateInstance(take, true);
            takeOuter.SetValue(choice, context);
            takeWeapon.SetValue(choice, weapon);
            takeNative.Invoke(choice, null);
        }

        static IEnumerable<CodeInstruction> MenuStateBoundary(IEnumerable<CodeInstruction> codes)
        {
            int replaced = 0;
            foreach (var code in codes)
            {
                if (code.operand is MethodInfo method && method.DeclaringType == typeof(Pawn_HealthTracker)
                    && method.Name == nameof(Pawn_HealthTracker.AddHediff) && method.ReturnType == typeof(Hediff)
                    && method.GetParameters().Length == 4 && method.GetParameters()[0].ParameterType == typeof(HediffDef))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(MonolynCodex), nameof(AddMenuState));
                    replaced++;
                }
                yield return code;
            }
            if (replaced != 1) throw new InvalidOperationException("Native Codex menu state boundary changed: " + replaced);
        }

        static Hediff AddMenuState(Pawn_HealthTracker health, HediffDef def, BodyPartRecord part,
            DamageInfo? damage, DamageWorker.DamageResult result)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand)
                return health.AddHediff(def, part, damage, result);
            // The local menu must not allocate a Hediff/load ID or advance Rand.
            // Its cosmetic state is created once in the replicated command.
            ensureSync.DoSync(null, (Pawn)healthPawn.GetValue(health));
            return health.hediffSet.GetFirstHediffOfDef(def);
        }

        static void EnsureMenuState(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.health == null) return;
            if (!pawn.health.hediffSet.HasHediff(codexHediff)) pawn.health.AddHediff(codexHediff);
        }

        static void StoreState(object __instance) => RestoreMenuState(__instance);
        static void DogmaState(object __instance) => RestoreMenuState(Field(dogma, "CS$<>8__locals1").GetValue(__instance));
        static void RestoreMenuState(object context)
        {
            if (!MP.IsInMultiplayer || (MP.InInterface && !MP.IsExecutingSyncCommand)) return;
            var pawn = outerPawn.GetValue(context) as Pawn;
            outerHediff.SetValue(context, pawn?.health?.hediffSet.GetFirstHediffOfDef(codexHediff));
        }

        static Type Require(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        static MethodInfo Method(Type type, string name) => AccessTools.DeclaredMethod(type, name, Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, name);
    }
}
