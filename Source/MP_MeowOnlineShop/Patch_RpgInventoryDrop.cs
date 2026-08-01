using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using Verse.AI;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// RPG Style Inventory Revamped 联机“丢下”兼容：
    /// - 本地点击丢下按钮时转发为 SyncMethod；
    /// - 回放同步命令时放行原逻辑；
    /// - 执行时统一做 Pawn/Thing/Tracker 的空引用防护。
    /// </summary>
    internal static class Patch_RpgInventoryDrop
    {
        private const string SandyGearTabTypeName = "Sandy_Detailed_RPG_Inventory.Sandy_Detailed_RPG_GearTab";

        [ThreadStatic] private static bool _isSyncReplay;
        private static int _warnCount;
        private const int MaxWarnLogs = 12;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MP.RegisterSyncMethod(typeof(Patch_RpgInventoryDrop), nameof(SyncDropThingFromPawn));
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RPG inventory drop: failed to register sync method: {e.Message}");
            }

            var patched = new HashSet<MethodBase>();

            TryPatchMethod(harmony, patched, AccessTools.Method(typeof(ITab_Pawn_Gear), "InterfaceDrop", new[] { typeof(Thing) }), "ITab_Pawn_Gear.InterfaceDrop");

            var sandyType = AccessTools.TypeByName(SandyGearTabTypeName);
            if (sandyType != null)
            {
                foreach (var m in sandyType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!LooksLikeDropMethod(m))
                        continue;
                    TryPatchMethod(harmony, patched, m, $"{sandyType.FullName}.{m.Name}");
                }
            }

            if (patched.Count == 0)
                Log.Warning("[MP-MeowOnlineShop] RPG inventory drop: no drop entry method found, patch skipped.");
            else
                Log.Message($"[MP-MeowOnlineShop] RPG inventory drop: patched {patched.Count} method(s).");
        }

        private static bool LooksLikeDropMethod(MethodInfo m)
        {
            if (m == null) return false;
            if (m.IsStatic) return false;
            if (m.ReturnType != typeof(void) && m.ReturnType != typeof(bool)) return false;
            if (!m.Name.ToLowerInvariant().Contains("drop")) return false;
            var ps = m.GetParameters();
            return ps.Length == 1 && typeof(Thing).IsAssignableFrom(ps[0].ParameterType);
        }

        private static void TryPatchMethod(Harmony harmony, HashSet<MethodBase> patched, MethodInfo method, string label)
        {
            if (method == null || patched.Contains(method))
                return;

            try
            {
                var prefix = AccessTools.Method(typeof(Patch_RpgInventoryDrop), nameof(DropPrefix));
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                patched.Add(method);
                Log.Message($"[MP-MeowOnlineShop] RPG inventory drop: patched {label}.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RPG inventory drop: failed to patch {label}: {e.Message}");
            }
        }

        private static bool DropPrefix(object __instance, Thing t)
        {
            if (!MP.IsInMultiplayer)
                return true;
            if (_isSyncReplay || MP.IsExecutingSyncCommand)
                return true;
            if (t == null || t.Destroyed)
                return false;

            try
            {
                var pawn = ResolvePawnFromTab(__instance);
                if (pawn == null || pawn.Destroyed)
                {
                    WarnLimited("DropPrefix: failed to resolve pawn from gear tab instance; blocking unsynced drop.");
                    return false;
                }

                SyncDropThingFromPawn(pawn.thingIDNumber, t.thingIDNumber);
                return false;
            }
            catch (Exception e)
            {
                WarnLimited($"DropPrefix failed: {e.Message}; blocking unsynced drop.");
                return false;
            }
        }

        public static void SyncDropThingFromPawn(int pawnThingId, int thingId)
        {
            if (pawnThingId <= 0 || thingId <= 0)
                return;

            Pawn pawn = ResolvePawnById(pawnThingId);
            if (pawn == null || pawn.Destroyed || pawn.Map == null)
            {
                WarnLimited($"SyncDropThingFromPawn: pawn not found or invalid (pawnId={pawnThingId}).");
                return;
            }

            Thing thing;
            var kind = ResolveThingOnPawn(pawn, thingId, out thing);
            if (kind == DropKind.Unknown || thing == null || thing.Destroyed)
            {
                WarnLimited($"SyncDropThingFromPawn: thing not found on pawn (pawn={pawn.LabelShortCap}, thingId={thingId}).");
                return;
            }

            _isSyncReplay = true;
            try
            {
                ExecuteDropByKind(pawn, thing, kind);
            }
            catch (Exception e)
            {
                WarnLimited($"SyncDropThingFromPawn execution failed: {e}");
            }
            finally
            {
                _isSyncReplay = false;
            }
        }

        private static Pawn ResolvePawnFromTab(object tabInstance)
        {
            if (tabInstance == null) return null;
            var t = tabInstance.GetType();

            var pawn =
                AccessTools.Property(t, "SelPawnForGear")?.GetValue(tabInstance, null) as Pawn ??
                AccessTools.Property(t, "SelPawn")?.GetValue(tabInstance, null) as Pawn ??
                AccessTools.Field(t, "SelPawnForGear")?.GetValue(tabInstance) as Pawn ??
                AccessTools.Field(t, "selPawnForGear")?.GetValue(tabInstance) as Pawn;

            if (pawn != null)
                return pawn;

            var selThing = AccessTools.Property(t, "SelThing")?.GetValue(tabInstance, null) as Thing
                           ?? AccessTools.Field(t, "SelThing")?.GetValue(tabInstance) as Thing;
            return selThing as Pawn;
        }

        private static Pawn ResolvePawnById(int pawnThingId)
        {
            try
            {
                foreach (var map in Find.Maps)
                {
                    var fromMap = map?.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
                    if (fromMap != null)
                        return fromMap;
                }
            }
            catch { }

            try
            {
                return Find.WorldPawns?.AllPawnsAliveOrDead?.FirstOrDefault(p => p != null && p.thingIDNumber == pawnThingId);
            }
            catch
            {
                return null;
            }
        }

        private static DropKind ResolveThingOnPawn(Pawn pawn, int thingId, out Thing thing)
        {
            thing = null;
            if (pawn == null)
                return DropKind.Unknown;

            var apparel = pawn.apparel?.WornApparel?.FirstOrDefault(a => a != null && a.thingIDNumber == thingId);
            if (apparel != null)
            {
                thing = apparel;
                return DropKind.Apparel;
            }

            var equipment = pawn.equipment?.AllEquipmentListForReading?.FirstOrDefault(e => e != null && e.thingIDNumber == thingId);
            if (equipment != null)
            {
                thing = equipment;
                return DropKind.Equipment;
            }

            var inventoryThing = pawn.inventory?.innerContainer?.FirstOrDefault(i => i != null && i.thingIDNumber == thingId);
            if (inventoryThing != null)
            {
                thing = inventoryThing;
                return DropKind.Inventory;
            }

            return DropKind.Unknown;
        }

        private static void ExecuteDropByKind(Pawn pawn, Thing thing, DropKind kind)
        {
            switch (kind)
            {
                case DropKind.Apparel:
                {
                    var apparel = thing as Apparel;
                    if (apparel == null || pawn.jobs == null)
                        return;
                    var job = new Job(JobDefOf.RemoveApparel, apparel);
                    pawn.jobs.TryTakeOrderedJob(job);
                    break;
                }
                case DropKind.Equipment:
                {
                    var equip = thing as ThingWithComps;
                    if (equip == null || pawn.jobs == null)
                        return;
                    var job = new Job(JobDefOf.DropEquipment, equip);
                    pawn.jobs.TryTakeOrderedJob(job);
                    break;
                }
                case DropKind.Inventory:
                {
                    if (pawn.MapHeld == null || pawn.inventory?.innerContainer == null)
                        return;
                    pawn.inventory.innerContainer.TryDrop(thing, pawn.PositionHeld, pawn.MapHeld, ThingPlaceMode.Near, out _);
                    break;
                }
            }
        }

        private static void WarnLimited(string msg)
        {
            if (_warnCount >= MaxWarnLogs)
                return;
            _warnCount++;
            Log.Warning("[MP-MeowOnlineShop] RPG inventory drop: " + msg);
        }

        private enum DropKind
        {
            Unknown = 0,
            Apparel = 1,
            Equipment = 2,
            Inventory = 3
        }
    }
}
