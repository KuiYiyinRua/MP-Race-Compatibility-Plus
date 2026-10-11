using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynCable
    {
        sealed class AbilityState { internal bool Detached; internal Ability Value; }
        static readonly ConditionalWeakTable<Thing, AbilityState> states = new ConditionalWeakTable<Thing, AbilityState>();
        static FieldInfo caster, ability;
        internal static void Apply(Harmony harmony)
        {
            foreach (var name in new[] { "ServitorCableP", "ServitorCable", "ServitorCableR" })
                harmony.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("ASEL." + name), "ExposeData"),
                    transpiler: new HarmonyMethod(typeof(MonolynCable), nameof(CasterReference)));
            var type = AccessTools.TypeByName("ASEL.ServitorCableP");
            caster = AccessTools.DeclaredField(type, "Caster");
            ability = AccessTools.DeclaredField(type, "ability");
            harmony.Patch(AccessTools.DeclaredMethod(type, "ExposeData"), postfix: new HarmonyMethod(typeof(MonolynCable), nameof(SaveAbility)));
            harmony.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("ASEL.CableFlyer"), "RespawnPawn"),
                transpiler: new HarmonyMethod(typeof(MonolynCable), nameof(FlyerMote)));
            Log.Message("[MonolynCable] resolved=3 caster identities, native owned/detached ability and flyer missing-mote boundary; missing=0");
        }
        static IEnumerable<CodeInstruction> CasterReference(IEnumerable<CodeInstruction> source)
        {
            int count = 0;
            foreach (var code in source)
            {
                if (code.operand is MethodInfo m && m.DeclaringType == typeof(Scribe_Deep) && m.Name == "Look" &&
                    m.IsGenericMethod && m.GetGenericArguments().SequenceEqual(new[] { typeof(Pawn) }))
                { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(MonolynCable), nameof(SaveCaster)); count++; }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected native cable caster saves: " + count);
        }
        static void SaveCaster(ref Pawn value, string label, object[] args)
        {
            if (!MP.IsInMultiplayer) { Scribe_Deep.Look(ref value, label, args); return; }
            // The projectile does not own this pawn. Deep saving creates a second
            // caster with the same load ID and loses identity across a joinpoint.
            Scribe_References.Look(ref value, label, value != null && !value.Discarded);
        }
        static void SaveAbility(Thing __instance)
        {
            if (!MP.IsInMultiplayer) return;
            var state = states.GetValue(__instance, _ => new AbilityState());
            var owner = (Pawn)caster.GetValue(__instance);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                state.Value = (Ability)ability.GetValue(__instance);
                state.Detached = state.Value != null && !((owner?.abilities?.abilities.Contains(state.Value) ?? false) ||
                    (owner?.abilities?.AllAbilitiesForReading.Contains(state.Value) ?? false));
            }
            Scribe_Values.Look(ref state.Detached, "meowCableDetachedAbility");
            if (state.Detached) Scribe_Deep.Look(ref state.Value, "meowCableAbility", new object[] { owner });
            else Scribe_References.Look(ref state.Value, "meowCableAbility");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (state.Value != null && state.Detached) state.Value.pawn = owner;
                ability.SetValue(__instance, state.Value);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.Saving) states.Remove(__instance);
        }
        static IEnumerable<CodeInstruction> FlyerMote(IEnumerable<CodeInstruction> source)
        {
            int count = 0;
            foreach (var code in source)
            {
                if (code.operand as MethodInfo == AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Spawned)))
                { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(MonolynCable), nameof(MoteSpawned)); count++; }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Unexpected cable flyer mote reads: " + count);
        }
        static bool MoteSpawned(Thing mote) => MP.IsInMultiplayer ? mote != null && mote.Spawned : mote.Spawned;
    }
}
