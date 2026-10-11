using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class MonolynWeather
    {
        sealed class Marker { }
        sealed class Capture { internal List<MonolynFlashRecord> Records; }
        static readonly ConditionalWeakTable<WeatherEvent_LightningFlash, Marker> tracked = new ConditionalWeakTable<WeatherEvent_LightningFlash, Marker>();
        static readonly ConditionalWeakTable<Map, Capture> captures = new ConditionalWeakTable<Map, Capture>();
        internal static FieldInfo Duration, Age, Shadow, StrikeLoc, BoltMesh;

        internal static void Apply(Harmony harmony)
        {
            var flash = typeof(WeatherEvent_LightningFlash);
            Duration = AccessTools.DeclaredField(flash, "duration");
            Age = AccessTools.DeclaredField(flash, "age");
            Shadow = AccessTools.DeclaredField(flash, "shadowVector");
            if (Duration?.FieldType != typeof(int) || Age?.FieldType != typeof(int) || Shadow?.FieldType != typeof(Vector2))
                throw new MissingFieldException(flash.FullName, "native flash phase");
            StrikeLoc = AccessTools.DeclaredField(typeof(WeatherEvent_LightningStrike), "strikeLoc");
            BoltMesh = AccessTools.DeclaredField(typeof(WeatherEvent_LightningStrike), "boltMesh");
            if (StrikeLoc?.FieldType != typeof(IntVec3) || BoltMesh?.FieldType != typeof(Mesh))
                throw new MissingFieldException(typeof(WeatherEvent_LightningStrike).FullName, "native strike phase");
            foreach (string name in new[] { "ASEL.Astridite", "ASEL.MNPunishment" })
            {
                var type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
                if (!typeof(Thing).IsAssignableFrom(type)) throw new InvalidOperationException("Native weather owner is not a Thing");
                var tick = AccessTools.DeclaredMethod(type, "Tick", Type.EmptyTypes) ?? throw new MissingMethodException(name, "Tick");
                harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(MonolynWeather), nameof(OwnerWeather)));
            }
            var trail = AccessTools.TypeByName("ASEL.TrailProjectileNew") ?? throw new TypeLoadException("ASEL.TrailProjectileNew");
            harmony.Patch(AccessTools.DeclaredMethod(trail, "Channeling", Type.EmptyTypes) ?? throw new MissingMethodException(trail.FullName, "Channeling"),
                transpiler: new HarmonyMethod(typeof(MonolynWeather), nameof(TrackStrikes)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Map), nameof(Map.ExposeData), Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(MonolynWeather), nameof(SaveMap)));
            Log.Message("[MonolynWeather] resolved=2 native owner-map flash paths and channeling strikes, persisted active weather phase; missing=0");
        }

        static Map OwnerMap(Thing owner) => MP.IsInMultiplayer ? owner.Map : Find.CurrentMap;
        static WeatherEvent_LightningFlash NewFlash(Map map)
        {
            // Keep the native constructor's simulation randomness at its original
            // call site. Only cold reconstruction below uses an isolated scope.
            var flash = new WeatherEvent_LightningFlash(map);
            if (MP.IsInMultiplayer) tracked.Add(flash, new Marker());
            return flash;
        }
        static WeatherEvent_LightningStrike NewStrike(Map map, IntVec3 location)
        {
            var strike = new WeatherEvent_LightningStrike(map, location);
            if (MP.IsInMultiplayer) tracked.Add(strike, new Marker());
            return strike;
        }
        static IEnumerable<CodeInstruction> TrackStrikes(IEnumerable<CodeInstruction> instructions)
        {
            var constructor = AccessTools.Constructor(typeof(WeatherEvent_LightningStrike), new[] { typeof(Map), typeof(IntVec3) });
            int count = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Newobj && Equals(code.operand, constructor))
                {
                    count++;
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(MonolynWeather), nameof(NewStrike));
                }
                yield return code;
            }
            if (count != 2) throw new InvalidOperationException("Expected exactly two native channeling strike constructors; found " + count);
        }
        static IEnumerable<CodeInstruction> OwnerWeather(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.CurrentMap));
            var constructor = AccessTools.Constructor(typeof(WeatherEvent_LightningFlash), new[] { typeof(Map) });
            int maps = 0, flashes = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(getter))
                {
                    maps++;
                    var owner = new CodeInstruction(OpCodes.Ldarg_0);
                    owner.labels.AddRange(code.labels);
                    owner.blocks.AddRange(code.blocks);
                    code.labels.Clear();
                    code.blocks.Clear();
                    yield return owner;
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(MonolynWeather), nameof(OwnerMap));
                }
                else if (code.opcode == OpCodes.Newobj && Equals(code.operand, constructor))
                {
                    flashes++;
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(MonolynWeather), nameof(NewFlash));
                }
                yield return code;
            }
            if (maps != 2 || flashes != 1)
                throw new InvalidOperationException("Expected two native CurrentMap getters and one flash constructor; found " + maps + "/" + flashes);
        }

        static void SaveMap(Map __instance)
        {
            var capture = captures.GetValue(__instance, _ => new Capture());
            if (Scribe.mode == LoadSaveMode.Saving)
                capture.Records = __instance.weatherManager.eventHandler.LiveEventsListForReading
                    .OfType<WeatherEvent_LightningFlash>()
                    .Where(e => (e.GetType() == typeof(WeatherEvent_LightningFlash) || e.GetType() == typeof(WeatherEvent_LightningStrike)) && tracked.TryGetValue(e, out _) && !e.Expired)
                    .Select(e => new MonolynFlashRecord(e)).ToList();
            Scribe_Collections.Look(ref capture.Records, "meowMonolynActiveFlashes", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
            // Map.ConstructComponents/ExposeComponents create the weather handler
            // during LoadingVars. Native FinalizeInit does not replace it.
            if (capture.Records != null)
                foreach (var record in capture.Records)
                {
                    WeatherEvent_LightningFlash flash;
                    Rand.PushState();
                    try
                    {
                        if (record.IsStrike)
                        {
                            var strike = new WeatherEvent_LightningStrike(__instance, new IntVec3(record.StrikeX, record.StrikeY, record.StrikeZ));
                            if (record.MeshPresent) BoltMesh.SetValue(strike, LightningBoltMeshPool.RandomBoltMesh);
                            flash = strike;
                        }
                        else flash = new WeatherEvent_LightningFlash(__instance);
                    }
                    finally { Rand.PopState(); }
                    Duration.SetValue(flash, record.Duration);
                    Age.SetValue(flash, record.Age);
                    Shadow.SetValue(flash, new Vector2(record.ShadowX, record.ShadowY));
                    tracked.Add(flash, new Marker());
                    // AddEvent would replay native FireEvent/Thunder. Preserve the
                    // live-event order without replaying that one-shot side effect.
                    __instance.weatherManager.eventHandler.LiveEventsListForReading.Add(flash);
                }
            captures.Remove(__instance);
        }
    }

    public sealed class MonolynFlashRecord : IExposable
    {
        internal int Duration, Age;
        internal float ShadowX, ShadowY;
        internal bool IsStrike, MeshPresent;
        internal int StrikeX, StrikeY, StrikeZ;
        public MonolynFlashRecord() { }
        internal MonolynFlashRecord(WeatherEvent_LightningFlash flash)
        {
            Duration = (int)MonolynWeather.Duration.GetValue(flash);
            Age = (int)MonolynWeather.Age.GetValue(flash);
            var shadow = (Vector2)MonolynWeather.Shadow.GetValue(flash);
            ShadowX = shadow.x;
            ShadowY = shadow.y;
            IsStrike = flash.GetType() == typeof(WeatherEvent_LightningStrike);
            if (IsStrike)
            {
                var location = (IntVec3)MonolynWeather.StrikeLoc.GetValue(flash);
                StrikeX = location.x; StrikeY = location.y; StrikeZ = location.z;
                MeshPresent = MonolynWeather.BoltMesh.GetValue(flash) != null;
            }
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref Duration, "duration", 0, true);
            Scribe_Values.Look(ref Age, "age", 0, true);
            Scribe_Values.Look(ref ShadowX, "shadowX", 0f, true);
            Scribe_Values.Look(ref ShadowY, "shadowY", 0f, true);
            Scribe_Values.Look(ref IsStrike, "isStrike", false, true);
            Scribe_Values.Look(ref MeshPresent, "meshPresent", false, true);
            Scribe_Values.Look(ref StrikeX, "strikeX", 0, true);
            Scribe_Values.Look(ref StrikeY, "strikeY", 0, true);
            Scribe_Values.Look(ref StrikeZ, "strikeZ", 0, true);
        }
    }
}
