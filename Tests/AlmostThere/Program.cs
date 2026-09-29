using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MP_MeowOnlineShop;
using RimWorld.Planet;
using CaravanDontRest;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL " + name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Resting(Caravan caravan, bool original = true)
    {
        Caravan_NightResting_Patch.Postfix(caravan, ref original);
        return original;
    }

    private static void Main()
    {
        var caravan = new Caravan();
        Multiplayer.API.MP.IsInMultiplayer = true;
        Verse.Find.TickManager.TicksGame = 818439;
        CaravanArrivalTimeEstimator.FreshResult = 11000;
        CaravanArrivalTimeEstimator.Clear();
        bool cold = Resting(caravan);
        CaravanArrivalTimeEstimator.Prime(caravan, 2028374, 18000);
        bool warm = Resting(caravan);
        Check(!cold && warm, "UNFIXED: cold join and future map-clock cache disagree on night rest");
        CaravanArrivalTimeEstimator.Prime(caravan, 818430, 18000);
        Check(Resting(caravan) != cold, "UNFIXED: recent UI cache also changes the rest branch");

        Patch_AlmostThereMp.ApplyNightRestCacheBoundary(new Harmony("meow.test.almostthere"));
        Check(Harmony.GetPatchInfo(typeof(Caravan_NightResting_Patch).GetMethod("Postfix"))
            .Transpilers.Any(x => x.owner == "meow.test.almostthere"), "real Harmony transpiler installed on extracted mod method");
        foreach (int cacheTick in new[] { 818438, 818430, 2028374, 819492, -1 })
        {
            CaravanArrivalTimeEstimator.Prime(caravan, cacheTick, 18000);
            int readsBefore = CaravanArrivalTimeEstimator.FreshCalls;
            Check(Resting(caravan) == cold, "MP ignores cache history " + cacheTick);
            Check(CaravanArrivalTimeEstimator.FreshCalls == readsBefore + 1,
                "MP runs original fresh estimator " + cacheTick);
            Check(CaravanArrivalTimeEstimator.CacheTick == cacheTick && CaravanArrivalTimeEstimator.CacheResult == 18000,
                "MP leaves UI cache untouched " + cacheTick);
        }

        var other = new Caravan();
        CaravanArrivalTimeEstimator.Prime(other, 2028374, 18000);
        Check(!Resting(caravan), "other caravan's cache has no effect");
        CaravanArrivalTimeEstimator.FreshResult = 14000;
        CaravanArrivalTimeEstimator.Prime(caravan, 2028374, 1000);
        Check(Resting(caravan), "farther destination still rests despite stale near-arrival cache");
        CaravanArrivalTimeEstimator.FreshResult = 12000;
        Check(Resting(caravan), "exact configured threshold retains strict less-than rule");
        AlmostThereSettings.AlmostThereHours = 5;
        Check(!Resting(caravan), "configured hours still govern rest");
        AlmostThereSettings.AlmostThereHours = 4;
        caravan.Comp.AlmostThere = 1;
        int callsBefore = CaravanArrivalTimeEstimator.FreshCalls;
        Check(!Resting(caravan), "never-rest mode preserved");
        caravan.Comp.AlmostThere = 2;
        Check(Resting(caravan), "normal-rest mode preserved");
        Check(CaravanArrivalTimeEstimator.FreshCalls == callsBefore, "non-estimating modes do no extra work");
        Check(!Resting(caravan, false), "vanilla non-resting branch preserved");
        caravan.Comp = null;
        Check(Resting(caravan), "missing component leaves vanilla result");
        caravan.Comp = new CompNightRestControl();

        Multiplayer.API.MP.IsInMultiplayer = false;
        CaravanArrivalTimeEstimator.Prime(caravan, 2028374, 18000);
        CaravanArrivalTimeEstimator.FreshResult = 11000;
        Check(Resting(caravan), "single-player still uses vanilla cached ETA");
        CaravanArrivalTimeEstimator.Clear();
        Check(!Resting(caravan), "single-player cold estimate preserved");
        Check(CaravanArrivalTimeEstimator.CacheTick == 818439, "single-player still fills the cache");
        CaravanArrivalTimeEstimator.Prime(caravan, 2028374, 18000);
        Check(Patch_AlmostThereMp.EstimateForNightRest(caravan, false) == 11000,
            "explicit cache-off argument remains cache-off in single-player");

        var original = typeof(CaravanArrivalTimeEstimator).GetMethod("EstimatedTicksToArrive",
            new[] { typeof(Caravan), typeof(bool) });
        var call = new CodeInstruction(OpCodes.Call, original);
        var dynamic = new DynamicMethod("labels", typeof(void), Type.EmptyTypes);
        var label = dynamic.GetILGenerator().DefineLabel();
        call.labels.Add(label);
        call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var patched = Patch_AlmostThereMp.NightRestCacheTranspiler(new[] { call }).Single();
        Check(patched.labels.Contains(label) && patched.blocks.Count == 1,
            "transpiler preserves labels and exception blocks");
        Check(Rejects(new CodeInstruction[0]), "missing ETA call rejected");
        Check(Rejects(new[] { new CodeInstruction(OpCodes.Call, original), new CodeInstruction(OpCodes.Call, original) }),
            "ambiguous duplicate ETA calls rejected");
        Console.WriteLine("COMPLETE " + checks + " offline assertions; Unity multiplayer runtime not exercised.");
    }

    private static bool Rejects(IEnumerable<CodeInstruction> code)
    {
        try { Patch_AlmostThereMp.NightRestCacheTranspiler(code).ToList(); return false; }
        catch (InvalidOperationException) { return true; }
    }
}

namespace Multiplayer.API
{
    public static class MP
    {
        public static bool enabled = true, IsInMultiplayer;
        public static void RegisterSyncMethod(MethodInfo method, object instance) { }
    }
}
namespace Verse
{
    public static class ModsConfig { public static bool IsActive(string id) => true; }
    public static class Log
    {
        public static void Message(string s) => Console.WriteLine(s);
        public static void Warning(string s) => Console.WriteLine(s);
        public static void Error(string s) => throw new Exception(s);
    }
    public class TickManager { public int TicksGame; public int TicksAbs => TicksGame; }
    public static class Find { public static TickManager TickManager = new TickManager(); }
}
namespace CaravanDontRest
{
    public class CompNightRestControl { public int AlmostThere; }
    public static class AlmostThereSettings { public static int AlmostThereHours = 4; }
}
namespace RimWorld.Planet
{
    public struct PlanetTile
    {
        public int Value;
        public static PlanetTile Invalid => new PlanetTile { Value = -1 };
        public static bool operator ==(PlanetTile a, PlanetTile b) => a.Value == b.Value;
        public static bool operator !=(PlanetTile a, PlanetTile b) => !(a == b);
        public override bool Equals(object other) => other is PlanetTile tile && this == tile;
        public override int GetHashCode() => Value;
    }
    public class WorldPath { }
    public class Pather
    {
        public bool Moving = true;
        public PlanetTile Destination = new PlanetTile { Value = 3 };
        public WorldPath curPath = new WorldPath();
        public float nextTileCostLeft;
    }
    public class WorldObject
    {
        public PlanetTile Tile;
        public CompNightRestControl Comp = new CompNightRestControl();
        public T GetComponent<T>() where T : class => Comp as T;
    }
    public class Caravan : WorldObject
    {
        public bool Spawned = true;
        public Pather pather = new Pather();
        public int TicksPerMove = 100;
    }
    public static class CaravanNightRestUtility
    {
        public static int LeftRestTicksAt(PlanetTile tile, long ticks) => 2000;
    }
    public static partial class CaravanArrivalTimeEstimator
    {
        private static int cacheTicks = -1, cachedResult = -1;
        private static Caravan cachedForCaravan;
        private static PlanetTile cachedForDest = PlanetTile.Invalid;
        public static int FreshResult, FreshCalls;
        public static int CacheTick => cacheTicks;
        public static int CacheResult => cachedResult;
        public static void Clear() { cachedForCaravan = null; cacheTicks = -1; cachedResult = -1; }
        public static void Prime(Caravan caravan, int ticks, int result)
        {
            cachedForCaravan = caravan; cachedForDest = caravan.pather.Destination;
            cacheTicks = ticks; cachedResult = result;
        }
        public static int EstimatedTicksToArrive(PlanetTile from, PlanetTile to, WorldPath path,
            float cost, int ticksPerMove, int ticksAbs)
        {
            FreshCalls++;
            return FreshResult;
        }
    }
}
