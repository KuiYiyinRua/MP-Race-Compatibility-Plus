using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using UnityEngine;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynProjectiles
    {
        static readonly Dictionary<Type, FieldInfo[]> saved = new Dictionary<Type, FieldInfo[]>();
        static readonly Dictionary<Type, FieldInfo[]> complete = new Dictionary<Type, FieldInfo[]>();
        sealed class DamageState { internal List<MonolynPendingDamage> Values; }
        static readonly ConditionalWeakTable<Projectile, DamageState> damageStates = new ConditionalWeakTable<Projectile, DamageState>();
        sealed class LancePhase { internal bool Initialized; }
        static readonly ConditionalWeakTable<Thing, LancePhase> lancePhases = new ConditionalWeakTable<Thing, LancePhase>();
        static FieldInfo homingInitial, homingInitialized, lanceDistance;
        static FieldInfo lanceEffecter;
        static MethodInfo lanceBurn;
        internal static void Apply(Harmony harmony)
        {
            MonolynCable.Apply(harmony);
            MonolynWeather.Apply(harmony);
            Add("ASEL.TrailProjectileNewB", "trailPoints", "impactTriggered", "tick");
            Add("ASEL.TrailProjectileNewAngle", "trailPoints", "tick");
            Add("ASEL.TrailProjectileNew", "reTarget", "cachedWeapon", "impactTriggered", "check", "ricochetCount", "firstBullet", "mustHit", "activated", "Freischutz", "tick", "weather");
            Add("ASEL.ProjectileTrail", "trail", "remainingTicks", "lastKnownPosition", "forwardOffset", "trailLength", "trailWidth", "baseAlpha");
            Add("ASEL.BulletHoming", "curPosition", "positionInitialized", "currentVelocityDirection", "initial", "turnRateGrowed", "impactTriggered");
            Add("ASEL.Projectile_Homing", "curPosition", "positionInitialized", "currentVelocityDirection", "turnRateGrowed");
            Add("ASEL.TrailProjectileHoming", "trailPoints", "impactTriggered", "tick");
            Add("ASEL.Projectile_ExplosiveGrenade", "intt");
            Add("ASEL.Projectile_PiercingExplosion", "damagedPawns");
            Add("ASEL.LanceFlyer", "effectsEnabled", "damagedPawns", "damagedPawnsB", "startPos", "totalTravelDistance");
            var derived = AccessTools.TypeByName("ASEL.TrailProjectileNew");
            saved[derived] = saved[AccessTools.TypeByName("ASEL.TrailProjectileNewB")].Concat(saved[derived]).ToArray();
            var homing = AccessTools.TypeByName("ASEL.BulletHoming");
            saved[homing] = saved[AccessTools.TypeByName("ASEL.TrailProjectileNewB")].Concat(saved[homing]).ToArray();
            homingInitial = AccessTools.DeclaredField(homing, "initial");
            homingInitialized = AccessTools.DeclaredField(homing, "positionInitialized");
            harmony.Patch(AccessTools.DeclaredMethod(homing, "SpawnSetup"), prefix: new HarmonyMethod(typeof(MonolynProjectiles), nameof(HomingBeforeSpawn)),
                postfix: new HarmonyMethod(typeof(MonolynProjectiles), nameof(HomingAfterSpawn)));
            var lance = AccessTools.TypeByName("ASEL.LanceFlyer");
            lanceDistance = AccessTools.DeclaredField(lance, "totalTravelDistance");
            lanceEffecter = AccessTools.DeclaredField(lance, "flightEffecter");
            lanceBurn = AccessTools.DeclaredMethod(lance, "ApplyBurnDamage", Type.EmptyTypes);
            if (lanceEffecter?.FieldType != typeof(Effecter) || lanceBurn?.ReturnType != typeof(void) || lanceBurn.IsStatic)
                throw new InvalidOperationException("Native lance damage phase signatures changed");
            harmony.Patch(AccessTools.DeclaredMethod(lance, "Tick"), transpiler: new HarmonyMethod(typeof(MonolynProjectiles), nameof(LanceEffects)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.ExposeData)), postfix: new HarmonyMethod(typeof(MonolynProjectiles), nameof(Save)));
            Log.Message("[MonolynProjectiles] resolved=10 projectile state families including inherited purification-energy flight, homing spawn phase, lance distance/effects; missing=0");
        }
        static void Add(string name, params string[] fields)
        {
            var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
            saved[type] = fields.Select(field => AccessTools.DeclaredField(type, field) ?? throw new MissingFieldException(name, field)).ToArray();
            foreach (var field in saved[type])
                if (!new[] { typeof(bool), typeof(int), typeof(float), typeof(Vector3), typeof(List<Vector3>), typeof(List<Pawn>), typeof(Thing), typeof(WeatherDef) }.Contains(field.FieldType))
                    throw new InvalidOperationException("Unexpected projectile field " + field);
        }
        static void Save(Thing __instance)
        {
            var actualType = __instance.GetType();
            if (!complete.TryGetValue(actualType, out var fields))
            {
                // Native Projectile_PurificationEnergy inherits two mod state
                // families and has no own fields. Exact runtime-type lookup
                // omitted both. Preserve the original declaring-type keys.
                var inherited = new List<FieldInfo>();
                for (var type = actualType; type != null; type = type.BaseType)
                    if (saved.TryGetValue(type, out var family)) inherited.AddRange(family);
                fields = inherited.Distinct().ToArray();
                complete.Add(actualType, fields);
            }
            if (fields.Length == 0) return;
            foreach (var field in fields)
            {
                string key = "meow" + field.DeclaringType.Name + "_" + field.Name;
                object value = field.GetValue(__instance);
                if (field.FieldType == typeof(bool)) { bool v = (bool)value; Scribe_Values.Look(ref v, key, field.Name == "check" || field.Name == "firstBullet" || field.Name == "effectsEnabled"); value = v; }
                else if (field.FieldType == typeof(int)) { int v = (int)value; Scribe_Values.Look(ref v, key, field.Name == "intt" ? 1 : 0); value = v; }
                else if (field.FieldType == typeof(float)) { float v = (float)value; Scribe_Values.Look(ref v, key); value = v; }
                else if (field.FieldType == typeof(Vector3)) { Vector3 v = (Vector3)value; Scribe_Values.Look(ref v, key); value = v; }
                else if (field.FieldType == typeof(List<Vector3>)) { var v = (List<Vector3>)value; Scribe_Collections.Look(ref v, key, LookMode.Value); value = v ?? new List<Vector3>(); }
                else if (field.FieldType == typeof(List<Pawn>)) { var v = (List<Pawn>)value; if (MP.IsInMultiplayer) MonolynRetainedRoots.Look(ref v, key); else Scribe_Collections.Look(ref v, key, LookMode.Reference); value = v ?? new List<Pawn>(); }
                else if (field.FieldType == typeof(Thing)) { var v = (Thing)value; if (MP.IsInMultiplayer) MonolynRetainedRoots.Look(ref v, key); else Scribe_References.Look(ref v, key); value = v; }
                else if (field.FieldType == typeof(WeatherDef)) { var v = (WeatherDef)value; Scribe_Defs.Look(ref v, key); value = v; }
                field.SetValue(__instance, value);
            }
            if (lanceDistance.DeclaringType.IsInstanceOfType(__instance)) SaveLancePhase(__instance);
            if (!(__instance is Projectile projectile)) return;
            Scribe_Defs.Look(ref projectile.damageDefOverride, "meowProjectileDamageOverride");
            Scribe_Values.Look(ref projectile.stoppingPower, "meowProjectileStoppingPower");
            var damages = damageStates.GetValue(projectile, p => new DamageState());
            if (Scribe.mode == LoadSaveMode.Saving) damages.Values = projectile.extraDamages?.Select(d => new MonolynPendingDamage(d)).ToList();
            // Primitive ExtraDamage records cannot be deep-scribed directly.
            // Retain the loaded list between LoadingVars and later Scribe passes.
            Scribe_Collections.Look(ref damages.Values, "meowProjectileExtraDamages", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) projectile.extraDamages = damages.Values?.Select(d => d.Restore()).ToList();
            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.Saving) damageStates.Remove(projectile);
        }
        static void HomingBeforeSpawn(Thing __instance, out int __state) => __state = (int)homingInitial.GetValue(__instance);
        static void HomingAfterSpawn(Thing __instance, bool respawningAfterLoad, int __state)
        {
            // Native SpawnSetup resets the delay even for an already-moving bullet.
            if (respawningAfterLoad && (bool)homingInitialized.GetValue(__instance)) homingInitial.SetValue(__instance, __state);
        }
        static IEnumerable<CodeInstruction> LanceEffects(IEnumerable<CodeInstruction> instructions)
        {
            int distance = 0, spawns = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && instruction.operand as FieldInfo == lanceDistance)
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MonolynProjectiles), nameof(Distance)); distance++; }
                else if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(EffecterDef) && method.Name == "Spawn" && method.GetParameters().Length == 0)
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(MonolynProjectiles), nameof(Effect)); spawns++; }
                yield return instruction;
            }
            if (distance != 1 || spawns != 3) throw new InvalidOperationException("Unexpected lance distance/effect sites " + distance + "/" + spawns);
        }
        static void Distance(Thing owner, float value)
        {
            // Recreating a local mote after load must retain the original flight distance.
            if (!MP.IsInMultiplayer || (float)lanceDistance.GetValue(owner) == 0f) lanceDistance.SetValue(owner, value);
            if (!MP.IsInMultiplayer) return;
            var phase = lancePhases.GetValue(owner, _ => new LancePhase());
            // Native Tick normally skips damage only on the first visual setup.
            // A cold join repeats visual setup; an already-initialized flight
            // still owes this tick's original damage before the 80% cutoff.
            bool resumed = phase.Initialized;
            phase.Initialized = true;
            if (resumed) lanceBurn.Invoke(owner, null);
        }
        static void SaveLancePhase(Thing owner)
        {
            var phase = lancePhases.GetValue(owner, _ => new LancePhase());
            if (Scribe.mode == LoadSaveMode.Saving)
                phase.Initialized |= lanceEffecter.GetValue(owner) != null;
            bool recorded = true;
            Scribe_Values.Look(ref recorded, "meowLanceFlightPhaseRecorded", false, true);
            Scribe_Values.Look(ref phase.Initialized, "meowLanceFlightInitialized", false, true);
            // Older patch saves contain the original full distance but no phase.
            // Native distance is assigned only after first visual initialization.
            if (Scribe.mode == LoadSaveMode.LoadingVars && !recorded)
                phase.Initialized = (float)lanceDistance.GetValue(owner) > 0f;
        }
        static Effecter Effect(EffecterDef def)
        {
            if (!MP.IsInMultiplayer) return def.Spawn();
            Rand.PushState();
            try { return def.Spawn(); }
            finally { Rand.PopState(); }
        }
    }
}
