using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianReturnProjectile
    {
        sealed class Shot
        {
            public DamageDef damage;
            public Vector2 size;
            public Graphic graphic;
        }
        [ThreadStatic] static Shot pending;
        static readonly ConditionalWeakTable<Projectile, Shot> shots = new ConditionalWeakTable<Projectile, Shot>();
        static readonly MethodInfo clone = AccessTools.Method(typeof(object), "MemberwiseClone");

        internal static void Apply(Harmony harmony)
        {
            if (!ModsConfig.RoyaltyActive) return;
            var type = AccessTools.TypeByName("Nivarian.ReflectiveShield") ?? throw new TypeLoadException("Nivarian.ReflectiveShield");
            var target = AccessTools.DeclaredMethod(type, "LaunchReturnShot") ?? throw new MissingMethodException(type.FullName, "LaunchReturnShot");
            harmony.Patch(target, prefix: Hook(nameof(Begin)), transpiler: Hook(nameof(Rewrite)), finalizer: Hook(nameof(End)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Projectile), "ExposeData"), postfix: Hook(nameof(Expose)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(Thing), "Graphic"), postfix: Hook(nameof(GraphicForShot)));
            Log.Message("[TaleNivarianCompat] reflected projectile damage and size are per-projectile, saved without shared Def mutations.");
        }
        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(NivarianReturnProjectile), name);
        static void Begin(out object __state)
        {
            __state = pending;
            pending = MP.IsInMultiplayer ? new Shot() : null;
        }
        static void End(object __state) { pending = (Shot)__state; }
        static void SetSize(GraphicData data, Vector2 value)
        {
            if (pending == null) data.drawSize = value;
            else pending.size = value;
        }
        static void SetDamage(ProjectileProperties props, DamageDef value)
        {
            if (pending == null) props.damageDef = value;
            else pending.damage = value;
        }
        static Thing MakeShot(ThingDef def, ThingDef stuff)
        {
            var result = ThingMaker.MakeThing(def, stuff);
            if (pending != null && result is Projectile projectile)
            {
                var state = new Shot { damage = pending.damage ?? def.projectile.damageDef, size = pending.size };
                shots.Add(projectile, state);
                projectile.damageDefOverride = state.damage;
            }
            return result;
        }
        static void LaunchShot(Projectile projectile, Thing launcher, LocalTargetInfo used, LocalTargetInfo intended,
            ProjectileHitFlags flags, bool preventFriendlyFire, Thing equipment)
        {
            projectile.Launch(launcher, used, intended, flags, preventFriendlyFire, equipment);
            RestoreStoppingPower(projectile);
        }
        static void RestoreStoppingPower(Projectile projectile)
        {
            if (!shots.TryGetValue(projectile, out var state) || projectile.def.projectile.stoppingPower != 0f) return;
            // Vanilla Launch reads the shared Def, not DamageDef. Preserve its equipment additions
            // while substituting the absorbed damage's default, as the original mod did via Def mutation.
            float originalDefault = projectile.def.projectile.damageDef?.defaultStoppingPower ?? 0f;
            float shotDefault = state.damage?.defaultStoppingPower ?? 0f;
            projectile.stoppingPower += shotDefault - originalDefault;
        }
        static void GraphicForShot(Thing __instance, ref Graphic __result)
        {
            if (!(__instance is Projectile projectile) || !shots.TryGetValue(projectile, out var state)) return;
            if (state.graphic == null)
            {
                state.graphic = (Graphic)clone.Invoke(__result, null);
                state.graphic.drawSize = state.size;
            }
            __result = state.graphic;
        }
        static void Expose(Projectile __instance)
        {
            bool marked = shots.TryGetValue(__instance, out var state);
            Scribe_Values.Look(ref marked, "meowReflectedProjectile");
            if (!marked) return;
            if (state == null) { state = new Shot(); shots.Add(__instance, state); }
            Scribe_Defs.Look(ref state.damage, "meowReflectedDamage");
            Scribe_Values.Look(ref state.size, "meowReflectedSize");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                state.graphic = null;
                __instance.damageDefOverride = state.damage;
            }
        }
        static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code)
        {
            int sizes = 0, damages = 0, factories = 0, launches = 0;
            var size = AccessTools.Field(typeof(GraphicData), "drawSize");
            var damage = AccessTools.Field(typeof(ProjectileProperties), "damageDef");
            var make = AccessTools.Method(typeof(ThingMaker), "MakeThing", new[] { typeof(ThingDef), typeof(ThingDef) });
            var launch = AccessTools.Method(typeof(Projectile), "Launch", new[] { typeof(Thing), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing) });
            foreach (var instruction in code)
            {
                string replacement = null;
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, size)) { sizes++; replacement = nameof(SetSize); }
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, damage)) { damages++; replacement = nameof(SetDamage); }
                if (instruction.Calls(make)) { factories++; replacement = nameof(MakeShot); }
                if (instruction.Calls(launch)) { launches++; replacement = nameof(LaunchShot); }
                if (replacement != null) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(NivarianReturnProjectile), replacement); }
                yield return instruction;
            }
            if (sizes != 1 || damages != 1 || factories != 1 || launches != 1)
                throw new InvalidOperationException("Unexpected reflected projectile writes/factory/launch: " + sizes + "/" + damages + "/" + factories + "/" + launches);
        }
    }
}
