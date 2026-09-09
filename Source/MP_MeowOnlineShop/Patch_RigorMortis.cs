using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>棺材/恢复关键路径日志，便于排查“恢复棺材后掉线”。仅当 ModDebug.EnableRigorMortisTrace 为 true 时输出。</summary>
    internal static class RigorMortisTrace
    {
        public const string Tag = "[MP-Meow.RM]";
        private static int _fallCount;
        private static int _recoverCount;
        private static int _workGiverCount;
        private static int _incantationPutCount;
        private static int _incantationExecuteOrderCount;
        private static int _incantationExecuteApplyCount;
        private static int _insectBloodOrderCount;
        private static int _chickenBloodOrderCount;
        private static int _lightningOrderCount;
        private static int _bellOrderGotoCount;
        private static int _bellOrderAttackCount;
        private static int _bellGotoCount;
        private static int _bellAttackCount;
        private static int _bellLockCount;
        private static int _stickyRiceImpactCount;
        private static int _draftedAttackGuardCount;
        private static int _zombieCatchActionCount;
        private const int MaxLogPerType = 20;
        private const int MaxLogPotentialWorkThings = 3;

        public static void LogFall(int casketId, int mapIndex, int tick)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _fallCount) <= MaxLogPerType)
                Verse.Log.Message($"{Tag} Fall: casketId={casketId} map={mapIndex} tick={tick}");
        }
        public static void LogRecover(int casketId, int mapIndex, int tick)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _recoverCount) <= MaxLogPerType)
                Verse.Log.Message($"{Tag} Recover: casketId={casketId} map={mapIndex} tick={tick}");
        }
        public static void LogPotentialWorkThings(Pawn pawn, int count)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _workGiverCount) <= MaxLogPotentialWorkThings)
                Verse.Log.Message($"{Tag} PotentialWorkThings: pawn={pawn?.LabelShort ?? "null"} count={count}");
        }

        public static void LogIncantationPut(Pawn taoist, Thing target)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _incantationPutCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? target?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string targetName = target?.LabelShort ?? target?.LabelCap.ToString() ?? "null";
            Verse.Log.Message($"{Tag} IncantationPut: taoist={taoistName} target={targetName} map={mapIndex} tick={tick}");
        }

        public static void LogIncantationExecuteOrder(Pawn taoist, Pawn zombie)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _incantationExecuteOrderCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? zombie?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string targetName = zombie?.LabelShort ?? "null";
            Verse.Log.Message($"{Tag} IncantationExecuteOrder: taoist={taoistName} target={targetName} map={mapIndex} tick={tick}");
        }

        public static void LogIncantationExecuteApply(Pawn taoist, Pawn zombie)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _incantationExecuteApplyCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? zombie?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string targetName = zombie?.LabelShort ?? "null";
            Verse.Log.Message($"{Tag} IncantationExecuteApply: taoist={taoistName} target={targetName} map={mapIndex} tick={tick}");
        }

        public static void LogInsectBloodOrder(Pawn taoist, Thing target)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _insectBloodOrderCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? target?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string targetName = target?.LabelShort ?? target?.LabelCap.ToString() ?? "null";
            Verse.Log.Message($"{Tag} InsectBloodOrder: taoist={taoistName} target={targetName} map={mapIndex} tick={tick}");
        }

        public static void LogChickenBloodOrder(Pawn taoist, Thing target)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _chickenBloodOrderCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? target?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string targetName = target?.LabelShort ?? target?.LabelCap.ToString() ?? "null";
            Verse.Log.Message($"{Tag} ChickenBloodOrder: taoist={taoistName} target={targetName} map={mapIndex} tick={tick}");
        }

        public static void LogLightningOrder(Pawn taoist, IntVec3 cell)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _lightningOrderCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            Verse.Log.Message($"{Tag} LightningOrder: taoist={taoistName} cell={cell} map={mapIndex} tick={tick}");
        }

        public static void LogBellOrderGoto(Pawn taoist, Pawn zombie)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _bellOrderGotoCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? zombie?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string zombieName = zombie?.LabelShort ?? "null";
            Verse.Log.Message($"{Tag} BellOrderGoto: taoist={taoistName} zombie={zombieName} map={mapIndex} tick={tick}");
        }

        public static void LogBellOrderAttack(Pawn taoist, Pawn zombie)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _bellOrderAttackCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? zombie?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string zombieName = zombie?.LabelShort ?? "null";
            Verse.Log.Message($"{Tag} BellOrderAttack: taoist={taoistName} zombie={zombieName} map={mapIndex} tick={tick}");
        }

        public static void LogBellGoto(Pawn taoist, IntVec3 cell)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _bellGotoCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            Verse.Log.Message($"{Tag} BellGoto: taoist={taoistName} cell={cell} map={mapIndex} tick={tick}");
        }

        public static void LogBellAttack(Pawn taoist, Thing target)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _bellAttackCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? target?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string targetName = target?.LabelShort ?? target?.LabelCap.ToString() ?? "null";
            Verse.Log.Message($"{Tag} BellAttack: taoist={taoistName} target={targetName} map={mapIndex} tick={tick}");
        }

        public static void LogBellLock(Pawn taoist, Pawn zombie)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _bellLockCount) > MaxLogPerType) return;
            int mapIndex = taoist?.Map?.Index ?? zombie?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string taoistName = taoist?.LabelShort ?? "null";
            string zombieName = zombie?.LabelShort ?? "null";
            Verse.Log.Message($"{Tag} BellLock: taoist={taoistName} zombie={zombieName} map={mapIndex} tick={tick}");
        }

        public static void LogStickyRiceImpact(Thing projectile, Thing hitThing)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _stickyRiceImpactCount) > MaxLogPerType) return;
            int mapIndex = projectile?.Map?.Index ?? hitThing?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            string hitName = hitThing?.LabelShort ?? hitThing?.LabelCap.ToString() ?? "null";
            Verse.Log.Message($"{Tag} StickyRiceImpact: projectileId={projectile?.thingIDNumber ?? 0} hit={hitName} map={mapIndex} tick={tick}");
        }

        public static void LogDraftedAttackGuard(Pawn pawn, string reason)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _draftedAttackGuardCount) > MaxLogPerType) return;
            int mapIndex = pawn?.Map?.Index ?? -1;
            int tick = Find.TickManager?.TicksGame ?? 0;
            Verse.Log.Message($"{Tag} DraftedAttackGuard: pawn={pawn?.LabelShort ?? "null"} id={pawn?.thingIDNumber ?? 0} map={mapIndex} tick={tick} reason={reason}");
        }

        public static void LogZombieCatchAction(string stage, Pawn zombie, Thing target, int mapIndex, int seed, string detail = null)
        {
            if (!ModDebug.EnableRigorMortisTrace) return;
            if (System.Threading.Interlocked.Increment(ref _zombieCatchActionCount) > MaxLogPerType) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            Verse.Log.Message($"{Tag} ZombieCatch: stage={stage} zombie={zombie?.LabelShort ?? "null"} zid={zombie?.thingIDNumber ?? 0} target={target?.LabelShort ?? target?.LabelCap.ToString() ?? "null"} map={mapIndex} tick={tick} seed={seed} detail={detail ?? "none"}");
        }
    }

    /// <summary>
    /// 联机下为模组 Rigor Mortis（僵死模组，Workshop 3454053400）稳定随机数，避免“恢复棺材位置”“给僵尸贴符/换装”等操作导致
    /// "Wrong random state on map X" 掉线。
    /// 原因：棺材 Tick、僵尸 CompTick、以及打开棺材/僵尸 Gizmo 时若消耗了 map.Rand 或 Verse.Rand，主机与客户端消耗顺序或次数不一致即 desync。
    /// 做法：对 Building_ZombieCasket.Tick、CompYinAndMalevolent.CompTick 用确定性种子包裹整次 Tick 的 Rand；
    ///       对 GetGizmos/CompGetGizmosExtra（棺材、僵尸阴煞气、换装）用确定性种子包裹，与 Patch_WorldRandStabilizer 的 Zone 思路一致。
    /// </summary>
    internal static class Patch_RigorMortis
    {
        private const int SeedOffsetCasketTick = 0x3D5E;
        private const int SeedOffsetCompTick = 0x4E6F;
        private const int SeedOffsetGizmos = 0x5F70;
        private const int ZoneWorldSeedOffset = 0x2C4D;

        private static bool PushMapRand(Map map, int seed)
        {
            if (map == null) return false;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return false;
                var push = mapRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (push == null) return false;
                push.Invoke(mapRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        private static void PopMapRand(Map map)
        {
            if (map == null) return;
            try
            {
                var t = map.GetType();
                var mapRand = AccessTools.Property(t, "Rand")?.GetValue(map) ?? AccessTools.Property(t, "rand")?.GetValue(map)
                    ?? AccessTools.Field(t, "Rand")?.GetValue(map) ?? AccessTools.Field(t, "rand")?.GetValue(map);
                if (mapRand == null) return;
                mapRand.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(mapRand, null);
            }
            catch { }
        }

        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();
        private static Func<object> TryGetWorldRandGetter()
        {
            try
            {
                return () =>
                {
                    var w = Find.World;
                    if (w == null) return null;
                    var t = w.GetType();
                    return AccessTools.Property(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Property(t, "rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "Rand")?.GetValue(w)
                        ?? AccessTools.Field(t, "rand")?.GetValue(w);
                };
            }
            catch { return null; }
        }

        private static bool PushWorldRand(int seed)
        {
            if (WorldRandGetter == null) return false;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return false;
                var push = worldRand.GetType().GetMethod("PushState", new[] { typeof(int) });
                if (push == null) return false;
                push.Invoke(worldRand, new object[] { seed });
                return true;
            }
            catch { return false; }
        }

        private static void PopWorldRand()
        {
            if (WorldRandGetter == null) return;
            try
            {
                var worldRand = WorldRandGetter();
                if (worldRand == null) return;
                worldRand.GetType().GetMethod("PopState", Type.EmptyTypes)?.Invoke(worldRand, null);
            }
            catch { }
        }

        private static int DeterministicTypeHash(Type type)
        {
            if (type == null) return 0;
            string name = type.FullName ?? type.Name ?? "";
            return DeterministicStringHash(name);
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int hash = 0;
            foreach (char c in value)
                hash = Gen.HashCombineInt(hash, (int)c);
            return hash;
        }

        // ----- Taoist artifact cooldown tick migration (async-time map switch) -----
        private const int MaxCooldownShiftLog = 40;
        private static int _cooldownShiftLogCount;
        private static readonly object CooldownShiftLock = new object();
        private static readonly Dictionary<int, int> PawnLastTickById = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> PawnLastMapIndexById = new Dictionary<int, int>();

        private static readonly Type MpExtensionsType = AccessTools.TypeByName("Multiplayer.Client.Extensions");
        private static readonly MethodInfo MpAsyncTimeMethod = AccessTools.Method(MpExtensionsType, "AsyncTime", new[] { typeof(Map) });
        private static readonly FieldInfo MpAsyncMapTicksField = MpAsyncTimeMethod?.ReturnType != null
            ? AccessTools.Field(MpAsyncTimeMethod.ReturnType, "mapTicks")
            : null;

        private static readonly Type RmCompBellType = AccessTools.TypeByName("RigorMortis.CompBell");
        private static readonly Type RmCompLightningSwordType = AccessTools.TypeByName("RigorMortis.CompLightningSword");
        private static readonly Type RmCompBaguaMirrorType = AccessTools.TypeByName("RigorMortis.CompBaguaMirror");
        private static readonly Type RmCompFlagWindRainType = AccessTools.TypeByName("RigorMortis.CompFlagWindRain");
        private static readonly Type RmCompInkFountainType = AccessTools.TypeByName("RigorMortis.CompInkFountain");
        private static readonly Type MpClientMultiplayerType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
        private static readonly PropertyInfo MpAsyncWorldTimeProperty = AccessTools.Property(MpClientMultiplayerType, "AsyncWorldTime");
        private static readonly FieldInfo MpAsyncWorldTicksField = MpAsyncWorldTimeProperty?.PropertyType != null
            ? AccessTools.Field(MpAsyncWorldTimeProperty.PropertyType, "worldTicks")
            : null;
        private static readonly MethodInfo FindTickManagerGetter = AccessTools.PropertyGetter(typeof(Find), nameof(Find.TickManager));
        private static readonly MethodInfo TickManagerTicksGameGetter = AccessTools.PropertyGetter(typeof(TickManager), nameof(TickManager.TicksGame));
        private static readonly MethodInfo GlobalCooldownNowMethod = AccessTools.Method(typeof(Patch_RigorMortis), nameof(GetGlobalCooldownNowTicks));
        private static readonly FieldInfo LastUseTickFieldBell = RmCompBellType != null ? AccessTools.Field(RmCompBellType, "lastUseTick") : null;
        private static readonly FieldInfo LastUseTickFieldLightning = RmCompLightningSwordType != null ? AccessTools.Field(RmCompLightningSwordType, "lastUseTick") : null;
        private static readonly FieldInfo LastUseTickFieldBagua = RmCompBaguaMirrorType != null ? AccessTools.Field(RmCompBaguaMirrorType, "lastUseTick") : null;
        private static readonly FieldInfo LastUseTickFieldFlag = RmCompFlagWindRainType != null ? AccessTools.Field(RmCompFlagWindRainType, "lastUseTick") : null;
        private static readonly FieldInfo LastUseTickFieldInk = RmCompInkFountainType != null ? AccessTools.Field(RmCompInkFountainType, "lastUseTick") : null;
        private static readonly List<Type> CooldownCompTypes = new List<Type>
        {
            RmCompBellType,
            RmCompLightningSwordType,
            RmCompBaguaMirrorType,
            RmCompFlagWindRainType,
            RmCompInkFountainType
        }.Where(t => t != null).ToList();

        public static void LogCooldownPatchStartupSummary()
        {
            Log.Message(
                "[MP-MeowOnlineShop] RigorMortis cooldown shift symbols: " +
                $"asyncMethod={(MpAsyncTimeMethod != null)}, asyncMapTicksField={(MpAsyncMapTicksField != null)}, " +
                $"asyncWorldTimeProperty={(MpAsyncWorldTimeProperty != null)}, asyncWorldTicksField={(MpAsyncWorldTicksField != null)}, " +
                $"lastUseFieldBell={(LastUseTickFieldBell != null)}, lastUseFieldLightning={(LastUseTickFieldLightning != null)}, " +
                $"lastUseFieldBagua={(LastUseTickFieldBagua != null)}, lastUseFieldFlag={(LastUseTickFieldFlag != null)}, " +
                $"lastUseFieldInk={(LastUseTickFieldInk != null)}, " +
                $"compBell={(RmCompBellType != null)}, compLightningSword={(RmCompLightningSwordType != null)}, " +
                $"compBaguaMirror={(RmCompBaguaMirrorType != null)}, compFlagWindRain={(RmCompFlagWindRainType != null)}, " +
                $"compInkFountain={(RmCompInkFountainType != null)}.");
        }

        public static int GetGlobalCooldownNowTicks()
        {
            try
            {
                var asyncWorld = MpAsyncWorldTimeProperty?.GetValue(null, null);
                if (asyncWorld != null && MpAsyncWorldTicksField != null)
                {
                    var worldTicksObj = MpAsyncWorldTicksField.GetValue(asyncWorld);
                    if (worldTicksObj is int worldTicks && worldTicks >= 0)
                        return worldTicks;
                }
            }
            catch
            {
                // Fallback to local tick manager below.
            }

            return Find.TickManager?.TicksGame ?? 0;
        }

        public static IEnumerable<CodeInstruction> ReplaceTicksGameWithGlobalNowTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            if (instructions == null)
                yield break;

            var list = instructions.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if (TickManagerTicksGameGetter != null && ins.Calls(TickManagerTicksGameGetter))
                {
                    if (i > 0 && FindTickManagerGetter != null && list[i - 1].Calls(FindTickManagerGetter))
                        list[i - 1] = new CodeInstruction(OpCodes.Nop);

                    list[i] = new CodeInstruction(OpCodes.Call, GlobalCooldownNowMethod);
                }
            }

            for (int i = 0; i < list.Count; i++)
                yield return list[i];
        }

        private static FieldInfo ResolveLastUseTickField(object instance)
        {
            if (instance == null)
                return null;

            var type = instance.GetType();
            if (RmCompBellType != null && RmCompBellType.IsAssignableFrom(type))
                return LastUseTickFieldBell;
            if (RmCompLightningSwordType != null && RmCompLightningSwordType.IsAssignableFrom(type))
                return LastUseTickFieldLightning;
            if (RmCompBaguaMirrorType != null && RmCompBaguaMirrorType.IsAssignableFrom(type))
                return LastUseTickFieldBagua;
            if (RmCompFlagWindRainType != null && RmCompFlagWindRainType.IsAssignableFrom(type))
                return LastUseTickFieldFlag;
            if (RmCompInkFountainType != null && RmCompInkFountainType.IsAssignableFrom(type))
                return LastUseTickFieldInk;

            return AccessTools.Field(type, "lastUseTick");
        }

        private static void ForceClearLastUseTick(object instance)
        {
            if (instance == null)
                return;

            var field = ResolveLastUseTickField(instance);
            if (field == null || field.FieldType != typeof(int))
                return;

            try
            {
                field.SetValue(instance, -1);
            }
            catch
            {
                // Ignore reflection failures, keep original behavior.
            }
        }

        public static void DisableCooldownPrefix(object __instance)
        {
            ForceClearLastUseTick(__instance);
        }

        public static void DisableCooldownPostfix(object __instance)
        {
            ForceClearLastUseTick(__instance);
        }

        public static void ForceZeroCooldownPostfix(ref int __result)
        {
            __result = 0;
        }

        private static bool IsAsyncCooldownShiftEnabled()
        {
            if (!MP.enabled || !MP.IsInMultiplayer)
                return false;
            return MpRuntimeInfo.TryGetAsyncTimeActive(out var active) && active;
        }

        private static bool TryGetTickForMap(Map map, out int tick)
        {
            tick = Find.TickManager?.TicksGame ?? 0;
            if (map == null)
                return false;

            if (MpAsyncTimeMethod == null || MpAsyncMapTicksField == null)
                return true;

            try
            {
                var asyncComp = MpAsyncTimeMethod.Invoke(null, new object[] { map });
                if (asyncComp == null)
                    return true;
                if (MpAsyncMapTicksField.GetValue(asyncComp) is int mapTick)
                    tick = mapTick;
                return true;
            }
            catch
            {
                return true;
            }
        }

        private static void RecordPawnTickSnapshot(Pawn pawn, Map map)
        {
            if (pawn == null || map == null)
                return;
            if (pawn.thingIDNumber <= 0)
                return;
            if (!TryGetTickForMap(map, out var tick))
                return;

            lock (CooldownShiftLock)
            {
                PawnLastTickById[pawn.thingIDNumber] = tick;
                PawnLastMapIndexById[pawn.thingIDNumber] = map.Index;
            }
        }

        private static bool TryConsumePawnTickSnapshot(int pawnId, out int oldTick, out int oldMapIndex)
        {
            oldTick = 0;
            oldMapIndex = -1;
            lock (CooldownShiftLock)
            {
                if (!PawnLastTickById.TryGetValue(pawnId, out oldTick))
                    return false;

                PawnLastTickById.Remove(pawnId);
                if (PawnLastMapIndexById.TryGetValue(pawnId, out oldMapIndex))
                    PawnLastMapIndexById.Remove(pawnId);
                return true;
            }
        }

        private static void LogCooldownShift(string detail)
        {
            if (System.Threading.Interlocked.Increment(ref _cooldownShiftLogCount) > MaxCooldownShiftLog)
                return;
            Log.Message("[MP-Meow.RM] CooldownShift: " + detail);
        }

        private static bool IsTargetCooldownComp(ThingComp comp)
        {
            if (comp == null || CooldownCompTypes.Count == 0)
                return false;

            var type = comp.GetType();
            for (int i = 0; i < CooldownCompTypes.Count; i++)
            {
                if (CooldownCompTypes[i].IsAssignableFrom(type))
                    return true;
            }
            return false;
        }

        private static bool TryShiftCompLastUseTick(ThingComp comp, int delta, out int before, out int after)
        {
            before = 0;
            after = 0;
            if (comp == null || delta == 0)
                return false;
            if (!IsTargetCooldownComp(comp))
                return false;

            var field = AccessTools.Field(comp.GetType(), "lastUseTick");
            if (field == null || field.FieldType != typeof(int))
                return false;

            int value;
            try
            {
                value = (int)field.GetValue(comp);
            }
            catch
            {
                return false;
            }

            if (value < 0)
                return false;

            before = value;
            after = value + delta;
            try
            {
                field.SetValue(comp, after);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<Thing> EnumeratePawnArtifactThings(Pawn pawn)
        {
            var yielded = new HashSet<int>();
            if (pawn == null)
                yield break;

            var equipment = pawn.equipment?.AllEquipmentListForReading;
            if (equipment != null)
            {
                for (int i = 0; i < equipment.Count; i++)
                {
                    var thing = equipment[i];
                    if (thing == null || !yielded.Add(thing.thingIDNumber))
                        continue;
                    yield return thing;
                }
            }

            var apparel = pawn.apparel?.WornApparel;
            if (apparel != null)
            {
                for (int i = 0; i < apparel.Count; i++)
                {
                    var thing = apparel[i];
                    if (thing == null || !yielded.Add(thing.thingIDNumber))
                        continue;
                    yield return thing;
                }
            }

            var inventory = pawn.inventory?.innerContainer;
            if (inventory != null)
            {
                for (int i = 0; i < inventory.Count; i++)
                {
                    var thing = inventory[i];
                    if (thing == null || !yielded.Add(thing.thingIDNumber))
                        continue;
                    yield return thing;
                }
            }
        }

        private static int ShiftPawnCooldownTicks(Pawn pawn, int delta)
        {
            if (pawn == null || delta == 0)
                return 0;

            int adjusted = 0;
            foreach (var thing in EnumeratePawnArtifactThings(pawn))
            {
                var thingWithComps = thing as ThingWithComps;
                var comps = thingWithComps?.AllComps;
                if (comps == null) continue;

                for (int i = 0; i < comps.Count; i++)
                {
                    var comp = comps[i];
                    if (!TryShiftCompLastUseTick(comp, delta, out var before, out var after))
                        continue;

                    adjusted++;
                    LogCooldownShift(
                        $"pawn={pawn.LabelShort} map={pawn.Map?.Index ?? -1} item={thing.LabelCap} comp={comp.GetType().Name} " +
                        $"delta={delta} old={before} new={after}");
                }
            }
            return adjusted;
        }

        public static void PawnDeSpawnPrefix(Pawn __instance)
        {
            if (!IsAsyncCooldownShiftEnabled() || __instance == null || __instance.Map == null)
                return;
            RecordPawnTickSnapshot(__instance, __instance.Map);
        }

        public static void WorldPawnsAddPawnPrefix(Pawn p)
        {
            if (!IsAsyncCooldownShiftEnabled() || p == null)
                return;
            // Fallback: some transfer paths pass through WorldPawns without an obvious map despawn context.
            if (p.thingIDNumber <= 0)
                return;

            lock (CooldownShiftLock)
            {
                if (PawnLastTickById.ContainsKey(p.thingIDNumber))
                    return;

                PawnLastTickById[p.thingIDNumber] = Find.TickManager?.TicksGame ?? 0;
                PawnLastMapIndexById[p.thingIDNumber] = -1;
            }
        }

        public static void WorldPawnsRemovePawnPrefix(Pawn p)
        {
            if (!IsAsyncCooldownShiftEnabled() || p == null || p.Map == null)
                return;
            RecordPawnTickSnapshot(p, p.Map);
        }

        public static void PawnSpawnSetupPostfix(Pawn __instance)
        {
            if (!IsAsyncCooldownShiftEnabled() || __instance == null || __instance.Map == null)
                return;
            if (__instance.thingIDNumber <= 0)
                return;

            if (!TryConsumePawnTickSnapshot(__instance.thingIDNumber, out var oldTick, out var oldMapIndex))
                return;
            if (!TryGetTickForMap(__instance.Map, out var newTick))
                return;

            int delta = newTick - oldTick;
            if (delta == 0)
                return;

            int adjusted = ShiftPawnCooldownTicks(__instance, delta);
            if (adjusted <= 0)
                return;

            LogCooldownShift(
                $"pawn={__instance.LabelShort} fromMap={oldMapIndex} toMap={__instance.Map.Index} oldTick={oldTick} newTick={newTick} " +
                $"delta={delta} adjustedComps={adjusted}");
        }

        // ----- Building_ZombieCasket.Tick -----
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;
        [ThreadStatic] private static Map _tickMapForRandPop;

        public static void CasketTickPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _tickMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null) return;
            var building = __instance as Building;
            if (building == null) return;
            Map map = building.Map;
            if (map == null) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < 0) return;
            int seed = Gen.HashCombineInt(Gen.HashCombineInt(tick, map.Index), Gen.HashCombineInt(building.thingIDNumber, SeedOffsetCasketTick));
            DeterministicRandScope.Begin(
                map,
                seed,
                ZoneWorldSeedOffset,
                ref __state,
                out _tickMapForRandPop,
                ignoreGate: true);
        }

        public static void CasketTickFinalizer(int __state)
        {
            if (__state == 0) return;
            try
            {
                DeterministicRandScope.End(__state, _tickMapForRandPop);
            }
            catch { }
            finally
            {
                _tickMapForRandPop = null;
            }
        }

        // ----- CompYinAndMalevolent.CompTick -----
        [ThreadStatic] private static Map _compTickMapForRandPop;

        public static void CompYinTickPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _compTickMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null) return;
            var comp = __instance as ThingComp;
            if (comp?.parent == null) return;
            Map map = (comp.parent as Thing)?.Map;
            if (map == null) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < 0) return;
            int seed = Gen.HashCombineInt(Gen.HashCombineInt(tick, map.Index), Gen.HashCombineInt(comp.parent.thingIDNumber, SeedOffsetCompTick));
            DeterministicRandScope.Begin(
                map,
                seed,
                ZoneWorldSeedOffset,
                ref __state,
                out _compTickMapForRandPop,
                ignoreGate: true);
        }

        public static void CompYinTickFinalizer(int __state)
        {
            if (__state == 0) return;
            try
            {
                DeterministicRandScope.End(__state, _compTickMapForRandPop);
            }
            catch { }
            finally
            {
                _compTickMapForRandPop = null;
            }
        }

        // ----- GetGizmos / CompGetGizmosExtra（棺材、阴煞气、换装）-----
        [ThreadStatic] private static Map _gizmoMapForRandPop;

        public static void GizmoPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _gizmoMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null) return;
            Map map = null;
            Thing thing = __instance as Thing;
            if (thing != null)
                map = thing.Map;
            else
            {
                var comp = __instance as ThingComp;
                if (comp?.parent is Thing t)
                    map = t.Map;
            }
            if (map == null) return;
            int tick = Find.TickManager?.TicksGame ?? 0;
            if (tick < 0) return;
            int id = thing?.thingIDNumber ?? (__instance as ThingComp)?.parent?.thingIDNumber ?? 0;
            int typeHash = DeterministicTypeHash(__instance.GetType());
            // 种子不包含 tick：客户端可能落后，选中同一棺材时双端应得到相同 Gizmo 列表，避免 Rand 消耗顺序不同导致 desync。
            int seed = Gen.HashCombineInt(Gen.HashCombineInt(map.Index, id), Gen.HashCombineInt(typeHash, SeedOffsetGizmos));
            Rand.PushState(seed);
            __state |= StateStaticRand;
            if (PushMapRand(map, seed))
            {
                _gizmoMapForRandPop = map;
                __state |= StateMapRand;
            }
            if (PushWorldRand(seed + ZoneWorldSeedOffset))
                __state |= StateWorldRand;
        }

        public static void GizmoFinalizer(int __state)
        {
            if (__state == 0) return;
            try
            {
                if ((__state & StateWorldRand) != 0) PopWorldRand();
                if ((__state & StateMapRand) != 0 && _gizmoMapForRandPop != null)
                {
                    PopMapRand(_gizmoMapForRandPop);
                    _gizmoMapForRandPop = null;
                }
                if ((__state & StateStaticRand) != 0) Rand.PopState();
            }
            catch { }
        }

        // ----- Building_ZombieCasket.Fall() -----
        // 开发者触发棺材坠落时调用；ReceiveLetter 等可能消耗 Rand，用确定性种子包裹静态/map/world Rand，双端同步执行 Fall() 时序列一致。
        // 种子不包含 tick：命令回放时客户端可能与主机处于不同 tick，若用 tick 则双端种子不同导致 "Random state from commands doesn't match"。
        // 已移除“客户端固定次数烧 Rand”的魔数对齐：易因版本差异分叉；统一依赖本方法内的 Rand 作用域与 Multiplayer 同步。
        private const int SeedOffsetFall = 0x7B92;
        [ThreadStatic] private static Map _fallMapForRandPop;

        public static void FallPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _fallMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null) return;
            var building = __instance as Building;
            if (building == null) return;
            Map map = building.Map;
            if (map == null) return;

            int seed = Gen.HashCombineInt(map.Index, Gen.HashCombineInt(building.thingIDNumber, SeedOffsetFall));
            Rand.PushState(seed);
            __state |= StateStaticRand;
            if (PushMapRand(map, seed))
            {
                _fallMapForRandPop = map;
                __state |= StateMapRand;
            }
            if (PushWorldRand(seed + ZoneWorldSeedOffset))
                __state |= StateWorldRand;
            RigorMortisTrace.LogFall(building.thingIDNumber, map.Index, Find.TickManager?.TicksGame ?? 0);
        }

               public static void FallFinalizer(int __state)
        {
            if (__state == 0) return;
            try
            {
                if ((__state & StateWorldRand) != 0) PopWorldRand();
                if ((__state & StateMapRand) != 0 && _fallMapForRandPop != null)
                {
                    PopMapRand(_fallMapForRandPop);
                    _fallMapForRandPop = null;
                }
                if ((__state & StateStaticRand) != 0) Rand.PopState();
            }
            catch { }
            finally
            {
                _fallMapForRandPop = null;
            }
        }


        // ----- Building_ZombieCasket.Recover() -----
        // 殖民者执行“恢复棺材”时调用；用确定性 Rand 包裹，避免 "Random state from commands doesn't match"。
        // 种子不包含 tick：任务完成时双端可能略有 tick 差，同逻辑动作需相同种子。
        private const int SeedOffsetRecover = 0x6A81;
        [ThreadStatic] private static Map _recoverMapForRandPop;

        public static void RecoverPrefix(object __instance, ref int __state)
        {
            __state = 0;
            _recoverMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null) return;
            var building = __instance as Building;
            if (building == null) return;
            Map map = building.Map;
            if (map == null) return;
            int seed = Gen.HashCombineInt(map.Index, Gen.HashCombineInt(building.thingIDNumber, SeedOffsetRecover));
            Rand.PushState(seed);
            __state |= StateStaticRand;
            if (PushMapRand(map, seed))
            {
                _recoverMapForRandPop = map;
                __state |= StateMapRand;
            }
            if (PushWorldRand(seed + ZoneWorldSeedOffset))
                __state |= StateWorldRand;
            RigorMortisTrace.LogRecover(building.thingIDNumber, map.Index, Find.TickManager?.TicksGame ?? 0);
        }

        public static void RecoverFinalizer(int __state)
        {
            if (__state == 0) return;
            try
            {
                if ((__state & StateWorldRand) != 0) PopWorldRand();
                if ((__state & StateMapRand) != 0 && _recoverMapForRandPop != null)
                {
                    PopMapRand(_recoverMapForRandPop);
                    _recoverMapForRandPop = null;
                }
                if ((__state & StateStaticRand) != 0) Rand.PopState();
            }
            catch { }
        }

        // ----- WorkGiver_Recover.PotentialWorkThingsGlobal -----
        // 返回按 thingIDNumber 排序的棺材列表，保证主机与客户端“可恢复棺材”顺序一致，避免指派到不同目标导致命令回放 Rand 不一致。
        // 整次调用用确定性种子包裹静态 Rand；使用 ref int __state + Finalizer 配对 Pop，支持嵌套调用（避免 ThreadStatic bool 导致外层漏 Pop）。
        private const int SeedOffsetPotentialWorkThings = 0x8C03;
        private const int StatePotentialWorkThingsStaticRand = 1;
        private const int SeedOffsetBellAction = 0x9A44;
        private const int SeedOffsetStickyRiceImpact = 0x1F27;
        private const int SeedOffsetZombieCatch = 0x3E2B;

        public static bool PotentialWorkThingsPrefix(Pawn pawn, ref IEnumerable<Thing> __result, ref int __state)
        {
            __state = 0;
            if (!MP.IsInMultiplayer || pawn?.Map == null) return true;
            var list = pawn.Map.listerThings.AllThings;
            if (list == null) return true;
            var casketType = AccessTools.TypeByName("RigorMortis.Building_ZombieCasket");
            if (casketType == null) return true;
            var amissProp = AccessTools.Property(casketType, "Amiss");
            if (amissProp == null) return true;
            int seed = Gen.HashCombineInt(pawn.Map.Index, Gen.HashCombineInt(pawn.thingIDNumber, SeedOffsetPotentialWorkThings));
            Rand.PushState(seed);
            __state = StatePotentialWorkThingsStaticRand;
            var caskets = new List<Thing>();
            for (int i = 0; i < list.Count; i++)
            {
                var thing = list[i];
                if (thing == null || !casketType.IsInstanceOfType(thing)) continue;
                if (thing.IsForbidden(pawn)) continue;
                try
                {
                    if (!(bool)amissProp.GetValue(thing)) continue;
                }
                catch { continue; }
                caskets.Add(thing);
            }
            caskets.Sort((a, b) => (a?.thingIDNumber ?? 0).CompareTo(b?.thingIDNumber ?? 0));
            RigorMortisTrace.LogPotentialWorkThings(pawn, caskets.Count);
            __result = caskets;
            return false;
        }

        public static void PotentialWorkThingsFinalizer(int __state)
        {
            if ((__state & StatePotentialWorkThingsStaticRand) == 0) return;
            try { Rand.PopState(); }
            catch { }
        }

        // ----- Bell actions (zombie control) -----
        [ThreadStatic] private static Map _bellMapForRandPop;
        [ThreadStatic] private static Map _stickyRiceMapForRandPop;
        [ThreadStatic] private static Map _zombieCatchMapForRandPop;

        private static readonly Type RmAxolotlZombieCatchType = AccessTools.TypeByName("RigorMortis.AxolotlZombieCatch");
        private static readonly FieldInfo RmAxolotlZombieCatchTargetField = RmAxolotlZombieCatchType != null
            ? AccessTools.Field(RmAxolotlZombieCatchType, "target")
            : null;

        private static bool IsLikelyRigorMortisZombie(Pawn pawn)
        {
            var mutantDefName = pawn?.mutant?.Def?.defName;
            if (string.IsNullOrEmpty(mutantDefName))
                return false;
            return mutantDefName.IndexOf("Axolotl", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsLikelyRigorMortisZombieAbility(Ability ability)
        {
            if (ability == null)
                return false;

            var defName = ability.def?.defName ?? string.Empty;
            if (defName.StartsWith("RM_", StringComparison.OrdinalIgnoreCase))
                return true;

            var verbTypeName = ability.verb?.GetType()?.FullName ?? string.Empty;
            if (verbTypeName.StartsWith("RigorMortis.Verb_", StringComparison.OrdinalIgnoreCase))
                return true;

            return IsLikelyRigorMortisZombie(ability.pawn);
        }

        private static bool IsRigorMortisMutantAbility(Ability ability)
        {
            if (ability?.pawn?.mutant?.AllAbilitiesForReading == null)
                return false;
            if (!IsLikelyRigorMortisZombieAbility(ability))
                return false;

            var mutantAbilities = ability.pawn.mutant.AllAbilitiesForReading;
            for (int i = 0; i < mutantAbilities.Count; i++)
            {
                var candidate = mutantAbilities[i];
                if (ReferenceEquals(candidate, ability))
                    return true;
                if (candidate != null && candidate.Id == ability.Id && candidate.def == ability.def)
                    return true;
            }

            return false;
        }

        private static Ability TryResolveAbilityFromOrderSource(object source)
        {
            if (source == null)
                return null;

            if (source is Ability direct)
                return direct;

            var sourceType = source.GetType();

            try
            {
                var abilityObj = AccessTools.Property(sourceType, "ability")?.GetValue(source)
                    ?? AccessTools.Property(sourceType, "Ability")?.GetValue(source)
                    ?? AccessTools.Field(sourceType, "ability")?.GetValue(source)
                    ?? AccessTools.Field(sourceType, "Ability")?.GetValue(source);
                if (abilityObj is Ability byName)
                    return byName;
            }
            catch { }

            try
            {
                foreach (var f in sourceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!typeof(Ability).IsAssignableFrom(f.FieldType))
                        continue;
                    if (f.GetValue(source) is Ability byFieldType)
                        return byFieldType;
                }
            }
            catch { }

            try
            {
                if (source is Verb verb)
                {
                    var pawn = verb.CasterPawn;
                    var allAbilities = pawn?.abilities?.abilities;
                    if (allAbilities != null)
                    {
                        foreach (var a in allAbilities)
                        {
                            if (a == null)
                                continue;
                            if (ReferenceEquals(a.verb, verb))
                                return a;
                        }
                    }

                    var mutantAbilities = pawn?.mutant?.AllAbilitiesForReading;
                    if (mutantAbilities != null)
                    {
                        foreach (var a in mutantAbilities)
                        {
                            if (a != null && ReferenceEquals(a.verb, verb))
                                return a;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public static bool ZombieAbilityOrderForceTargetPrefix(object __instance, object __0)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            var ability = TryResolveAbilityFromOrderSource(__instance);
            var sourceTypeName = __instance.GetType().FullName ?? __instance.GetType().Name ?? "UnknownType";
            if (ability == null)
            {
                if (sourceTypeName.StartsWith("RigorMortis.Verb_", StringComparison.OrdinalIgnoreCase))
                    Log.Warning($"[MP-MeowOnlineShop] Zombie ability OrderForceTarget hit but ability unresolved: source={sourceTypeName} argType={__0?.GetType().FullName ?? "null"}");
                return true;
            }

            if (!IsRigorMortisMutantAbility(ability))
                return true;

            if (!(__0 is LocalTargetInfo target))
            {
                Log.Warning($"[MP-MeowOnlineShop] Zombie ability OrderForceTarget unsupported arg type: def={ability.def?.defName ?? "unknown"} source={sourceTypeName} argType={__0?.GetType().FullName ?? "null"}");
                return true;
            }

            if (MP.IsExecutingSyncCommand)
                return true;

            if (SyncZombieMutantAbilityCastMethod == null)
            {
                Log.Warning($"[MP-MeowOnlineShop] Zombie ability sync missing for {ability.def?.defName ?? "unknown"}; block local OrderForceTarget to avoid desync.");
                return false;
            }

            try
            {
                Log.Message($"[MP-MeowOnlineShop] Zombie ability OrderForceTarget intercepted: def={ability.def?.defName ?? "unknown"} source={sourceTypeName} targetPawn={target.Pawn?.LabelShort ?? "null"} targetCell={target.Cell}");
                SyncZombieMutantAbilityCastMethod.DoSync(
                    null,
                    ability.pawn,
                    ability.Id,
                    ability.def?.defName ?? string.Empty,
                    target,
                    true);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Zombie ability DoSync failed def={ability.def?.defName ?? "unknown"}: {e.Message}");
            }

            return false;
        }

        public static bool ZombieAbilityCommandProcessInputPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null)
                return true;

            Ability ability = null;
            try
            {
                var t = __instance.GetType();
                ability = AccessTools.Field(t, "ability")?.GetValue(__instance) as Ability
                    ?? AccessTools.Property(t, "ability")?.GetValue(__instance) as Ability
                    ?? AccessTools.Field(t, "Ability")?.GetValue(__instance) as Ability
                    ?? AccessTools.Property(t, "Ability")?.GetValue(__instance) as Ability;
            }
            catch { }

            if (ability == null || !IsRigorMortisMutantAbility(ability))
                return true;

            var verbType = ability.verb?.GetType();
            Log.Message($"[MP-MeowOnlineShop] Zombie ability gizmo clicked: cmdType={__instance.GetType().FullName} def={ability.def?.defName ?? "unknown"} pawn={ability.pawn?.LabelShort ?? "null"} verb={verbType?.FullName ?? "null"} mpExec={MP.IsExecutingSyncCommand}");

            if (!ability.def.targetRequired && !MP.IsExecutingSyncCommand)
            {
                if (SyncZombieMutantAbilityCastMethod == null)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Zombie self-cast sync missing for {ability.def?.defName ?? "unknown"}; block local click to avoid MP divergence.");
                    return false;
                }

                try
                {
                    Log.Message($"[MP-MeowOnlineShop] Zombie self-cast ability intercepted: def={ability.def?.defName ?? "unknown"} pawn={ability.pawn?.LabelShort ?? "null"}");
                    SyncZombieMutantAbilityCastMethod.DoSync(
                        null,
                        ability.pawn,
                        ability.Id,
                        ability.def?.defName ?? string.Empty,
                        LocalTargetInfo.Invalid,
                        false);
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] Zombie self-cast DoSync failed def={ability.def?.defName ?? "unknown"}: {e.Message}");
                }

                return false;
            }

            return true;
        }

        private static Ability ResolveMutantAbility(Pawn pawn, int abilityId, string abilityDefName)
        {
            if (pawn?.mutant?.AllAbilitiesForReading == null)
                return null;

            var abilities = pawn.mutant.AllAbilitiesForReading;
            Ability byDef = null;
            for (int i = 0; i < abilities.Count; i++)
            {
                var candidate = abilities[i];
                if (candidate == null)
                    continue;
                if (candidate.Id == abilityId)
                    return candidate;
                if (byDef == null && string.Equals(candidate.def?.defName, abilityDefName, StringComparison.Ordinal))
                    byDef = candidate;
            }

            return byDef;
        }

        public static void SyncZombieMutantAbilityCast(
            Pawn pawn,
            int abilityId,
            string abilityDefName,
            LocalTargetInfo target,
            bool hasTarget)
        {
            var ability = ResolveMutantAbility(pawn, abilityId, abilityDefName);
            if (ability == null)
            {
                Log.Warning($"[MP-MeowOnlineShop] Zombie mutant ability resolve failed: pawn={pawn?.thingIDNumber ?? 0} id={abilityId} def={abilityDefName ?? "null"}.");
                return;
            }

            try
            {
                if (hasTarget)
                {
                    ability.verb.OrderForceTarget(target);
                }
                else
                {
                    // Match Command_Ability.ProcessInput exactly for targetRequired=false.
                    ability.QueueCastingJob(ability.pawn, LocalTargetInfo.Invalid);
                }

                Log.Message(
                    $"[MP-MeowOnlineShop] Zombie mutant ability sync fired: def={ability.def?.defName ?? "unknown"} " +
                    $"pawn={ability.pawn?.LabelShort ?? "null"} id={ability.Id} hasTarget={hasTarget} " +
                    $"targetPawn={target.Pawn?.LabelShort ?? "null"} targetCell={target.Cell}.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SyncZombieMutantAbilityCast exception def={ability.def?.defName ?? "unknown"}: {e}");
                throw;
            }
        }

        public static void SyncZombieAbilityOrderForceTarget(Ability ability, LocalTargetInfo target)
        {
            if (ability == null)
                return;

            if (!IsLikelyRigorMortisZombieAbility(ability))
                return;

            try
            {
                var orderOnAbility = AccessTools.Method(ability.GetType(), "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                if (orderOnAbility != null)
                {
                    orderOnAbility.Invoke(ability, new object[] { target });
                }
                else
                {
                    var verb = ability.verb;
                    var orderOnVerb = AccessTools.Method(verb?.GetType(), "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    orderOnVerb?.Invoke(verb, new object[] { target });
                }

                Log.Message($"[MP-MeowOnlineShop] Zombie ability sync fired: def={ability.def?.defName ?? "unknown"} pawn={ability.pawn?.LabelShort ?? "null"} targetPawn={target.Pawn?.LabelShort ?? "null"} targetCell={target.Cell}");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SyncZombieAbilityOrderForceTarget exception def={ability.def?.defName ?? "unknown"}: {e}");
                throw;
            }
        }

        public static void SyncZombieAbilitySelfCast(Ability ability)
        {
            if (ability == null)
                return;

            if (!IsLikelyRigorMortisZombieAbility(ability))
                return;

            try
            {
                var pawn = ability.pawn;
                var self = pawn != null ? new LocalTargetInfo(pawn) : LocalTargetInfo.Invalid;
                var verb = ability.verb;

                bool invoked = false;

                var orderOnAbility = AccessTools.Method(ability.GetType(), "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                if (orderOnAbility != null && self.IsValid)
                {
                    orderOnAbility.Invoke(ability, new object[] { self });
                    invoked = true;
                }

                if (!invoked)
                {
                    var orderOnVerb = AccessTools.Method(verb?.GetType(), "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    if (orderOnVerb != null && self.IsValid)
                    {
                        orderOnVerb.Invoke(verb, new object[] { self });
                        invoked = true;
                    }
                }

                if (!invoked)
                {
                    var tryStartCast = AccessTools.Method(verb?.GetType(), "TryStartCastOn", new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                    if (tryStartCast != null && self.IsValid)
                    {
                        tryStartCast.Invoke(verb, new object[] { self, self, false, true, false, false });
                        invoked = true;
                    }
                }

                if (!invoked)
                {
                    var cast = AccessTools.Method(ability.GetType(), "Cast", Type.EmptyTypes);
                    if (cast != null)
                    {
                        cast.Invoke(ability, null);
                        invoked = true;
                    }
                }

                Log.Message($"[MP-MeowOnlineShop] Zombie self-cast sync fired: def={ability.def?.defName ?? "unknown"} pawn={ability.pawn?.LabelShort ?? "null"} invoked={invoked}");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SyncZombieAbilitySelfCast exception def={ability.def?.defName ?? "unknown"}: {e}");
                throw;
            }
        }

        private static Thing TryGetZombieCatchTargetThing(object instance)
        {
            if (instance == null || RmAxolotlZombieCatchTargetField == null)
                return null;
            try
            {
                var targetObj = RmAxolotlZombieCatchTargetField.GetValue(instance);
                if (targetObj is LocalTargetInfo targetInfo)
                    return targetInfo.Thing;
            }
            catch
            {
            }
            return null;
        }

        public static bool UseRangedAttackGuardPrefix(Pawn pawn, ref bool __result)
        {
            if (!MP.IsInMultiplayer || pawn == null)
                return true;
            if (!IsLikelyRigorMortisZombie(pawn))
                return true;

            bool noDraftSupport = pawn.mutant?.Def != null && !pawn.mutant.Def.canBeDrafted;
            bool missingTrackers = pawn.drafter == null || pawn.stances == null || pawn.verbTracker == null || pawn.equipment == null;
            if (!noDraftSupport && !missingTrackers)
                return true;

            __result = false;
            string reason = noDraftSupport
                ? "mutant_def_cannot_be_drafted"
                : "missing_drafter_or_combat_trackers";
            RigorMortisTrace.LogDraftedAttackGuard(pawn, reason);
            return false;
        }

        public static void BellOrderTargetPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogBellOrderGoto(taoist, target.Pawn);
            }
            catch { }
        }

        public static void BellOrderAttackTargetPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogBellOrderAttack(taoist, target.Pawn);
            }
            catch { }
        }

        public static void BellGotoPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogBellGoto(taoist, target.Cell);
            }
            catch { }
        }

        public static void BellAttackPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogBellAttack(taoist, target.Thing);
            }
            catch { }
        }

        public static void BellLockPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogBellLock(taoist, target.Pawn);
            }
            catch { }
        }

        public static void BellActionPrefix(object __instance, MethodBase __originalMethod, ref int __state)
        {
            __state = 0;
            _bellMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null) return;
            var comp = __instance as ThingComp;
            if (comp?.parent == null) return;
            var map = comp.parent.Map;
            if (map == null) return;
            var methodInfo = __originalMethod as MethodInfo;
            int methodHash = DeterministicTypeHash(__originalMethod?.DeclaringType) ^ DeterministicTypeHash(methodInfo?.ReturnType);
            methodHash = Gen.HashCombineInt(methodHash, DeterministicStringHash(__originalMethod?.Name));
            int seed = Gen.HashCombineInt(map.Index, Gen.HashCombineInt(comp.parent.thingIDNumber, SeedOffsetBellAction));
            seed = Gen.HashCombineInt(seed, methodHash);
            Rand.PushState(seed);
            __state |= StateStaticRand;
            if (PushMapRand(map, seed))
            {
                _bellMapForRandPop = map;
                __state |= StateMapRand;
            }
            if (PushWorldRand(seed + ZoneWorldSeedOffset))
                __state |= StateWorldRand;
        }

        public static void BellActionFinalizer(int __state)
        {
            if (__state == 0) return;
            try
            {
                if ((__state & StateWorldRand) != 0) PopWorldRand();
                if ((__state & StateMapRand) != 0 && _bellMapForRandPop != null)
                {
                    PopMapRand(_bellMapForRandPop);
                    _bellMapForRandPop = null;
                }
                if ((__state & StateStaticRand) != 0) Rand.PopState();
            }
            catch { }
        }

        public static void StopTargetingPostfix()
        {
            if (!MP.IsInMultiplayer || !MP.IsExecutingSyncCommand)
                return;
            try
            {
                if (Find.Targeter != null && Find.Targeter.IsTargeting)
                    Find.Targeter.StopTargeting();
            }
            catch { }
        }

        // ----- Sticky rice projectile -----
        public static void StickyRiceImpactPrefix(object __instance, Thing hitThing, ref int __state)
        {
            __state = 0;
            _stickyRiceMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null) return;
            var projectile = __instance as Thing;
            var map = projectile?.Map;
            if (map == null) return;
            int seed = Gen.HashCombineInt(map.Index, Gen.HashCombineInt(projectile.thingIDNumber, SeedOffsetStickyRiceImpact));
            Rand.PushState(seed);
            __state |= StateStaticRand;
            if (PushMapRand(map, seed))
            {
                _stickyRiceMapForRandPop = map;
                __state |= StateMapRand;
            }
            if (PushWorldRand(seed + ZoneWorldSeedOffset))
                __state |= StateWorldRand;
            RigorMortisTrace.LogStickyRiceImpact(projectile, hitThing);
        }

        public static void StickyRiceImpactFinalizer(int __state)
        {
            if (__state == 0) return;
            try
            {
                if ((__state & StateWorldRand) != 0) PopWorldRand();
                if ((__state & StateMapRand) != 0 && _stickyRiceMapForRandPop != null)
                {
                    PopMapRand(_stickyRiceMapForRandPop);
                    _stickyRiceMapForRandPop = null;
                }
                if ((__state & StateStaticRand) != 0) Rand.PopState();
            }
            catch { }
        }

        public static void ZombieCatchTryCatchPawnPrefix(object __instance, Pawn zombie, ref int __state)
        {
            __state = 0;
            _zombieCatchMapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null || zombie == null)
                return;

            var map = zombie.Map ?? (__instance as Thing)?.Map;
            if (map == null)
                return;

            int seed = Gen.HashCombineInt(map.Index, Gen.HashCombineInt(zombie.thingIDNumber, SeedOffsetZombieCatch));
            seed = Gen.HashCombineInt(seed, (__instance as Thing)?.thingIDNumber ?? 0);
            Rand.PushState(seed);
            __state |= StateStaticRand;
            if (PushMapRand(map, seed))
            {
                _zombieCatchMapForRandPop = map;
                __state |= StateMapRand;
            }
            if (PushWorldRand(seed + ZoneWorldSeedOffset))
                __state |= StateWorldRand;

            RigorMortisTrace.LogZombieCatchAction(
                "TryCatchPawn.Prefix",
                zombie,
                TryGetZombieCatchTargetThing(__instance),
                map.Index,
                seed,
                "entered");
        }

        public static void ZombieCatchTryCatchPawnFinalizer(object __instance, Pawn zombie, int __state)
        {
            if (__state == 0)
                return;
            try
            {
                if ((__state & StateWorldRand) != 0) PopWorldRand();
                if ((__state & StateMapRand) != 0 && _zombieCatchMapForRandPop != null)
                {
                    PopMapRand(_zombieCatchMapForRandPop);
                    _zombieCatchMapForRandPop = null;
                }
                if ((__state & StateStaticRand) != 0) Rand.PopState();
            }
            catch
            {
            }
            finally
            {
                int mapIndex = zombie?.Map?.Index ?? (__instance as Thing)?.Map?.Index ?? -1;
                RigorMortisTrace.LogZombieCatchAction(
                    "TryCatchPawn.Finalizer",
                    zombie,
                    TryGetZombieCatchTargetThing(__instance),
                    mapIndex,
                    0,
                    "scope_exit");
                _zombieCatchMapForRandPop = null;
            }
        }

        // ----- Taoist incantation (talisman put & execution) -----

        public static void IncantationPutPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogIncantationPut(taoist, target.Thing);
            }
            catch { }
        }

        public static void IncantationExecuteOrderPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                var zombie = target.Pawn as Pawn;
                RigorMortisTrace.LogIncantationExecuteOrder(taoist, zombie);
            }
            catch { }
        }

        public static void IncantationExecuteApplyPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var hediff = __instance as HediffWithComps;
                Pawn taoist = hediff?.pawn;
                Pawn zombie = null;
                try
                {
                    var field = AccessTools.Field(__instance.GetType(), "target");
                    if (field != null)
                        zombie = field.GetValue(__instance) as Pawn;
                }
                catch { }
                RigorMortisTrace.LogIncantationExecuteApply(taoist, zombie);
            }
            catch { }
        }

        // ----- Blood order trace (casket insect blood / chicken blood enchant) -----
        public static void InsectBloodOrderPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogInsectBloodOrder(taoist, target.Thing);
            }
            catch { }
        }

        public static void ChickenBloodOrderPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogChickenBloodOrder(taoist, target.Thing);
            }
            catch { }
        }

        public static void LightningOrderPrefix(object __instance, LocalTargetInfo target)
        {
            if (!MP.IsInMultiplayer || __instance == null) return;
            try
            {
                var taoist = AccessTools.Property(__instance.GetType(), "Taoist")?.GetValue(__instance) as Pawn;
                RigorMortisTrace.LogLightningOrder(taoist, target.Cell);
            }
            catch { }
        }

        // ----- QuestEditor CQF story actions (Rigor Mortis mainline) -----
        internal static ISyncMethod SyncCqfSendSignalMethod;
        internal static ISyncMethod SyncCqfGiveRewardsMethod;
        internal static ISyncMethod SyncCqfDisableIncantationMethod;
        internal static ISyncMethod SyncCqfPlayerFactionMethod;
        internal static ISyncMethod SyncCqfRemoveHediffMethod;
        internal static ISyncMethod SyncCqfLeftMapMethod;
        internal static ISyncMethod SyncCqfAdjustRelationMethod;
        internal static ISyncMethod SyncCqfSwitchEntranceMethod;
        internal static ISyncMethod SyncCqfUnlockTraderMethod;
        internal static ISyncMethod SyncCqfSendLetterMethod;
        internal static ISyncMethod SyncStoryChooseOptionMethod;
        internal static ISyncMethod SyncStoryCloseMethod;
        internal static ISyncMethod SyncZombieMutantAbilityCastMethod;
        internal static ISyncMethod SyncZombieQuestSignalMethod;
        private static MethodInfo RigorMortisForceEndQuestOfSiteMethod;

        private static readonly Dictionary<string, string> CqfActionPrefixByType = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "RigorMortis.CQFAction_SendSignal", nameof(CQF_SendSignal_RealWork_Prefix) },
            { "RigorMortis.CQFAction_GiveRewards", nameof(CQF_GiveRewards_RealWork_Prefix) },
            { "RigorMortis.CQFAction_DisableIncantation", nameof(CQF_DisableIncantation_RealWork_Prefix) },
            { "RigorMortis.CQFAction_PlayerFaction", nameof(CQF_PlayerFaction_RealWork_Prefix) },
            { "RigorMortis.CQFAction_RemoveHediff", nameof(CQF_RemoveHediff_RealWork_Prefix) },
            { "RigorMortis.CQFAction_LeftMap", nameof(CQF_LeftMap_RealWork_Prefix) },
            { "RigorMortis.CQFAction_AdjustRelation", nameof(CQF_AdjustRelation_RealWork_Prefix) },
            { "RigorMortis.CQFAction_SwitchEntrance", nameof(CQF_SwitchEntrance_RealWork_Prefix) },
            { "RigorMortis.CQFAction_UnlockTrader", nameof(CQF_UnlockTrader_RealWork_Prefix) },
            { "RigorMortis.CQFAction_SendLetter", nameof(CQF_SendLetter_RealWork_Prefix) },
        };

        private static readonly HashSet<string> StoryRuntimeLogOnce = new HashSet<string>();
        private static readonly HashSet<string> StoryCommittedEvents = new HashSet<string>();
        private static readonly Queue<string> StoryCommittedOrder = new Queue<string>();
        private static int StoryEventCounter;
        private const int StoryCommittedEventLimit = 1024;

        private static string CreateStoryEventId(string actionKey, int sessionId)
        {
            int tick = Find.TickManager?.TicksGame ?? 0;
            int seq = System.Threading.Interlocked.Increment(ref StoryEventCounter);
            string key = string.IsNullOrEmpty(actionKey) ? "story" : actionKey;
            return $"{key}:{sessionId}:{tick}:{seq}";
        }

        private static void LogStoryRuntimeOnce(string key, string detail, bool warning = false)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (!StoryRuntimeLogOnce.Add(key))
                return;
            if (warning)
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis story sync: {detail}");
            else
                Log.Message($"[MP-MeowOnlineShop] RigorMortis story sync: {detail}");
        }

        private static bool DoStorySyncRequest(ISyncMethod method, string actionKey, int sessionId, params object[] payloadArgs)
        {
            if (method == null)
                return false;

            try
            {
                string eventId = CreateStoryEventId(actionKey, sessionId);
                object[] args;
                if (payloadArgs == null || payloadArgs.Length == 0)
                {
                    args = new object[] { eventId, sessionId };
                }
                else
                {
                    args = new object[payloadArgs.Length + 2];
                    args[0] = eventId;
                    args[1] = sessionId;
                    Array.Copy(payloadArgs, 0, args, 2, payloadArgs.Length);
                }

                LogStoryRuntimeOnce($"request:{actionKey}:{eventId}", $"request {actionKey} event={eventId} session={sessionId}.");
                method.DoSync(null, args);
                return true;
            }
            catch (Exception e)
            {
                LogStoryRuntimeOnce($"request:{actionKey}:exception", $"request dispatch failed for {actionKey}: {e.Message}", warning: true);
                return false;
            }
        }

        private static bool ValidateStorySessionForEvent(int sessionId, string actionKey)
        {
            if (sessionId <= 0)
                return true;

            if (RigorMortisStorySessionManager.TryGetById(sessionId, out var session) && session != null && !session.IsClosed && session.SourceWindow != null)
                return true;

            LogStoryRuntimeOnce($"session-missing:{actionKey}:{sessionId}", $"event {actionKey} ignored: session {sessionId} missing or closed.", warning: true);
            return false;
        }

        private static bool TryBeginStoryCommit(string eventId, string actionKey)
        {
            if (string.IsNullOrEmpty(eventId))
                return false;

            if (!StoryCommittedEvents.Add(eventId))
            {
                LogStoryRuntimeOnce($"commit-dup:{eventId}", $"commit dedupe hit for {actionKey}, event={eventId}.");
                return false;
            }

            StoryCommittedOrder.Enqueue(eventId);
            while (StoryCommittedOrder.Count > StoryCommittedEventLimit)
            {
                string stale = StoryCommittedOrder.Dequeue();
                StoryCommittedEvents.Remove(stale);
            }

            return true;
        }

        private static void LogStoryAck(string eventId, int sessionId, string actionKey)
        {
            if (string.IsNullOrEmpty(eventId))
                return;
            LogStoryRuntimeOnce($"ack:{eventId}", $"ack {actionKey} event={eventId} session={sessionId}.");
        }

        private static string StoryMethodSignature(string methodName)
        {
            try
            {
                var method = AccessTools.Method(typeof(Patch_RigorMortis), methodName);
                if (method == null)
                    return $"{methodName}=missing";
                var ps = method.GetParameters();
                var sig = string.Join(",", ps.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                return $"{methodName}({sig})";
            }
            catch (Exception e)
            {
                return $"{methodName}=error:{e.GetType().Name}";
            }
        }

        internal static void RegisterStorySyncMethods()
        {
            try
            {
                SyncStoryChooseOptionMethod = RegisterSyncMethodByName(nameof(SyncStoryChooseOption));
                SyncStoryCloseMethod = RegisterSyncMethodByName(nameof(SyncStoryClose));
                SyncCqfSendSignalMethod = RegisterSyncMethodByName(nameof(SyncCqfSendSignal));
                SyncCqfGiveRewardsMethod = RegisterSyncMethodByName(nameof(SyncCqfGiveRewards));
                SyncCqfDisableIncantationMethod = RegisterSyncMethodByName(nameof(SyncCqfDisableIncantation));
                SyncCqfPlayerFactionMethod = RegisterSyncMethodByName(nameof(SyncCqfPlayerFaction));
                SyncCqfRemoveHediffMethod = RegisterSyncMethodByName(nameof(SyncCqfRemoveHediff));
                SyncCqfLeftMapMethod = RegisterSyncMethodByName(nameof(SyncCqfLeftMap));
                SyncCqfAdjustRelationMethod = RegisterSyncMethodByName(nameof(SyncCqfAdjustRelation));
                SyncCqfSwitchEntranceMethod = RegisterSyncMethodByName(nameof(SyncCqfSwitchEntrance));
                SyncCqfUnlockTraderMethod = RegisterSyncMethodByName(nameof(SyncCqfUnlockTrader));
                SyncCqfSendLetterMethod = RegisterSyncMethodByName(nameof(SyncCqfSendLetter));
                SyncZombieMutantAbilityCastMethod = RegisterSyncMethodByName(nameof(SyncZombieMutantAbilityCast));
                SyncZombieQuestSignalMethod = RegisterSyncMethodByName(nameof(SyncZombieQuestSignal));
                Log.Message("[MP-MeowOnlineShop] RigorMortis: CQF story sync signatures => "
                    + $"{StoryMethodSignature(nameof(SyncStoryChooseOption))}; "
                    + $"{StoryMethodSignature(nameof(SyncStoryClose))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfSendSignal))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfGiveRewards))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfDisableIncantation))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfPlayerFaction))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfRemoveHediff))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfLeftMap))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfAdjustRelation))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfSwitchEntrance))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfUnlockTrader))}; "
                    + $"{StoryMethodSignature(nameof(SyncCqfSendLetter))}; "
                    + $"{StoryMethodSignature(nameof(SyncZombieQuestSignal))}");
                Log.Message("[MP-MeowOnlineShop] RigorMortis: CQF story sync methods registered.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis: register CQF story sync methods failed: {e.Message}");
            }
        }

        internal static void TrySyncStoryChooseOption(int sessionId, int optionIndex, int expectedVersion)
        {
            if (sessionId <= 0)
                return;

            if (SyncStoryChooseOptionMethod == null)
            {
                LogStoryRuntimeOnce("storyChooseOption:missing-sync", "SyncStoryChooseOptionMethod unavailable, block local apply to prevent desync.", warning: true);
                return;
            }

            DoStorySyncRequest(SyncStoryChooseOptionMethod, "storyChooseOption", sessionId, optionIndex, expectedVersion);
        }

        public static void SyncStoryChooseOption(string eventId, int sessionId, int optionIndex, int expectedVersion)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncStoryChooseOption)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncStoryChooseOption)))
                return;

            if (!RigorMortisStorySessionManager.TryGetById(sessionId, out var session))
            {
                LogStoryRuntimeOnce($"storyChooseOption:missing-session:{sessionId}", $"session {sessionId} not found while choosing option.", warning: true);
                return;
            }

            if (session.IsClosed || session.SourceWindow == null)
            {
                RigorMortisStorySessionManager.MarkClosed(sessionId, "source_window_missing");
                return;
            }

            if (expectedVersion != session.Version)
            {
                LogStoryRuntimeOnce($"storyChooseOption:version-mismatch:{sessionId}", $"session {sessionId} version mismatch expected={expectedVersion} actual={session.Version}.", warning: true);
                return;
            }

            var node = RigorMortisStoryRuntimeMap.TryGetCurrentNode(session.SourceWindow);
            if (node?.options == null || optionIndex < 0 || optionIndex >= node.options.Count)
            {
                LogStoryRuntimeOnce($"storyChooseOption:invalid-option:{sessionId}:{optionIndex}", $"invalid option index {optionIndex} for session {sessionId}.", warning: true);
                return;
            }

            var option = node.options[optionIndex];
            if (option == null)
                return;

            try
            {
                if (!RigorMortisStoryRuntimeMap.TryActivateOption(option))
                {
                    LogStoryRuntimeOnce($"storyChooseOption:activate-failed:{sessionId}:{optionIndex}", $"failed to invoke option.Activate for session {sessionId}, option {optionIndex}.", warning: true);
                    return;
                }
                RigorMortisStorySessionManager.IncrementVersion(session);
                LogStoryAck(eventId, sessionId, nameof(SyncStoryChooseOption));
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SyncStoryChooseOption failed (session={sessionId}, option={optionIndex}): {e.Message}");
                throw;
            }
        }

        public static void SyncStoryClose(int sessionId, string reason)
        {
            RigorMortisStorySessionManager.MarkClosed(sessionId, reason ?? "sync_close");
        }

        private static ISyncMethod RegisterSyncMethodByName(string methodName)
        {
            var method = AccessTools.Method(typeof(Patch_RigorMortis), methodName);
            if (method == null)
                throw new MissingMethodException(typeof(Patch_RigorMortis).FullName, methodName);
            return MP.RegisterSyncMethod(method, null);
        }

        /// <summary>
        /// Synchronizes the natural zombie-quest signal path. Rigor Mortis calls
        /// RMUtility.ForceEndQuestOfSite with force=false after the last hostile
        /// zombie dies; that method dispatches QuestUtility signals directly and
        /// has no Multiplayer sync boundary of its own.
        ///
        /// Deliberately leave force=true alone: debug/forced quest completion is
        /// not the natural completion path this patch is intended to repair.
        /// </summary>
        internal static void ApplyZombieQuestCompletionPatch(Harmony harmony)
        {
            if (harmony == null)
                return;

            try
            {
                var rmUtilityType = AccessTools.TypeByName("RigorMortis.RMUtility");
                var target = AccessTools.Method(
                    rmUtilityType,
                    "ForceEndQuestOfSite",
                    new[] { typeof(Site), typeof(QuestEndOutcome), typeof(bool) });
                var prefix = AccessTools.Method(typeof(Patch_RigorMortis), nameof(RigorMortisForceEndQuestOfSitePrefix));
                if (target == null || prefix == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] RigorMortis zombie quest completion patch skipped: ForceEndQuestOfSite or prefix not found.");
                    return;
                }

                RigorMortisForceEndQuestOfSiteMethod = target;
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                Log.Message("[MP-MeowOnlineShop] RigorMortis zombie quest completion sync patch applied to RMUtility.ForceEndQuestOfSite.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis zombie quest completion sync patch failed: {e.Message}");
            }
        }

        public static bool RigorMortisForceEndQuestOfSitePrefix(Site site, QuestEndOutcome outcome, bool force)
        {
            if (!MP.enabled || !MP.IsInMultiplayer || MP.IsExecutingSyncCommand || force)
                return true;

            if (outcome != QuestEndOutcome.Success && outcome != QuestEndOutcome.Fail)
                return true;

            if (SyncZombieQuestSignalMethod == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortis zombie quest signal sync missing; block local completion to avoid divergent quest state.");
                return false;
            }

            try
            {
                Log.Message($"[MP-MeowOnlineShop] RigorMortis zombie quest signal intercepted: site={site?.ID ?? -1} outcome={outcome}.");
                SyncZombieQuestSignalMethod.DoSync(null, site, (int)outcome);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis zombie quest signal dispatch failed: {e.Message}");
            }

            return false;
        }

        public static void SyncZombieQuestSignal(WorldObject siteObject, int outcomeValue)
        {
            var site = siteObject as Site;
            if (site == null ||
                (outcomeValue != (int)QuestEndOutcome.Success && outcomeValue != (int)QuestEndOutcome.Fail))
                return;

            if (RigorMortisForceEndQuestOfSiteMethod == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortis ForceEndQuestOfSite target unavailable while executing zombie quest signal sync.");
                return;
            }

            try
            {
                RigorMortisForceEndQuestOfSiteMethod.Invoke(
                    null,
                    new object[] { site, (QuestEndOutcome)outcomeValue, false });
            }
            catch (TargetInvocationException e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis synced zombie quest signal failed: {e.InnerException?.Message ?? e.Message}");
                throw e.InnerException ?? e;
            }
        }

        internal static void ApplyStoryActionPatches(Harmony harmony)
        {
            if (harmony == null) return;

            int patched = 0;
            int failed = 0;
            var patchedTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in CqfActionPrefixByType)
                TryPatchCqfRealWork(harmony, pair.Key, pair.Value, ref patched, ref failed, patchedTypes);

            AuditCqfActionCoverage(harmony, ref patched, ref failed, patchedTypes);
            Log.Message($"[MP-MeowOnlineShop] RigorMortis: CQF story action patch summary patched={patched} failed={failed}.");
        }

        private static void TryPatchCqfRealWork(Harmony harmony, string typeName, string prefixName, ref int patched, ref int failed, HashSet<string> patchedTypes = null)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    failed++;
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis CQF patch skipped: type not found {typeName}.");
                    return;
                }

                var method = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "RealWork" && m.GetParameters().Length == 2);
                var prefix = AccessTools.Method(typeof(Patch_RigorMortis), prefixName);
                if (method == null || prefix == null)
                {
                    failed++;
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortis CQF patch skipped: method/prefix missing {typeName}.{prefixName}.");
                    return;
                }

                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                patched++;
                patchedTypes?.Add(typeName);
                Log.Message($"[MP-MeowOnlineShop] Patched {type.FullName}.RealWork for MP story sync.");
            }
            catch (Exception e)
            {
                failed++;
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis CQF patch failed for {typeName}: {e.Message}");
            }
        }

        private static void AuditCqfActionCoverage(Harmony harmony, ref int patched, ref int failed, HashSet<string> patchedTypes)
        {
            if (harmony == null)
                return;

            var discovered = DiscoverRigorCqfActionTypes();
            var uncovered = new List<string>();
            foreach (var typeName in discovered)
            {
                if (patchedTypes != null && patchedTypes.Contains(typeName))
                    continue;

                if (CqfActionPrefixByType.TryGetValue(typeName, out var prefixName))
                {
                    TryPatchCqfRealWork(harmony, typeName, prefixName, ref patched, ref failed, patchedTypes);
                    continue;
                }

                uncovered.Add(typeName);
            }

            if (uncovered.Count > 0)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortis CQF coverage audit: uncovered actions={string.Join(", ", uncovered)}");
            }
            else
            {
                Log.Message($"[MP-MeowOnlineShop] RigorMortis CQF coverage audit: discovered={discovered.Count}, all covered. QuestEditor RemoveDialogManager gate handled by Patch_RigorMortisStoryDialogs.");
            }
        }

        private static List<string> DiscoverRigorCqfActionTypes()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm == null || asm.IsDynamic)
                    continue;
                var asmName = asm.GetName().Name ?? "";
                if (asmName.IndexOf("Rigor", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types?.Where(t => t != null).ToArray() ?? Array.Empty<Type>();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract)
                        continue;

                    var fullName = type.FullName ?? "";
                    if (fullName.IndexOf("RigorMortis.CQFAction_", StringComparison.Ordinal) != 0)
                        continue;

                    var realWork = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m => m.Name == "RealWork" && m.GetParameters().Length == 2);
                    if (realWork == null)
                        continue;
                    result.Add(fullName);
                }
            }

            return result.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        private static bool ShouldBypassLocalCqfAction()
        {
            return MP.enabled && MP.IsInMultiplayer && !MP.IsExecutingSyncCommand;
        }

        private static int ResolveStorySessionIdFromThingIds(int[] ids)
        {
            if (ids == null || ids.Length == 0)
                return 0;

            for (int i = 0; i < ids.Length; i++)
            {
                int sessionId = RigorMortisStorySessionManager.TryGetSessionIdByThingId(ids[i]);
                if (sessionId > 0)
                    return sessionId;
            }

            return 0;
        }

        private static int[] CollectTargetThingIds(object targetsObj)
        {
            var result = new HashSet<int>();
            if (targetsObj == null)
                return Array.Empty<int>();

            if (targetsObj is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                    AppendThingIdFromTargetInfo(entry.Value, result);
            }
            else if (targetsObj is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var value = AccessTools.Property(item?.GetType(), "Value")?.GetValue(item);
                    if (value != null)
                        AppendThingIdFromTargetInfo(value, result);
                }
            }

            return result.Where(i => i > 0).ToArray();
        }

        private static string EncodeThingIds(int[] ids)
        {
            if (ids == null || ids.Length == 0)
                return string.Empty;

            var stable = ids.Where(i => i > 0).Distinct().OrderBy(i => i).ToArray();
            if (stable.Length == 0)
                return string.Empty;
            return string.Join(",", stable);
        }

        private static int[] DecodeThingIds(string packedIds, string contextKey)
        {
            if (string.IsNullOrWhiteSpace(packedIds))
            {
                LogStoryRuntimeOnce($"{contextKey}:empty", $"{contextKey} received empty target ids payload.", warning: true);
                return Array.Empty<int>();
            }

            var parts = packedIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                LogStoryRuntimeOnce($"{contextKey}:split-empty", $"{contextKey} split target ids payload but got no parts.", warning: true);
                return Array.Empty<int>();
            }

            var ids = new List<int>(parts.Length);
            bool hasInvalid = false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int id) || id <= 0)
                {
                    hasInvalid = true;
                    continue;
                }
                if (!ids.Contains(id))
                    ids.Add(id);
            }

            if (hasInvalid)
                LogStoryRuntimeOnce($"{contextKey}:invalid", $"{contextKey} target ids payload contains invalid id token(s).", warning: true);
            if (ids.Count == 0)
            {
                LogStoryRuntimeOnce($"{contextKey}:decoded-empty", $"{contextKey} decoded target ids but result is empty.", warning: true);
                return Array.Empty<int>();
            }

            return ids.ToArray();
        }

        private static void AppendThingIdFromTargetInfo(object targetInfoObj, HashSet<int> ids)
        {
            if (targetInfoObj == null || ids == null)
                return;

            try
            {
                var type = targetInfoObj.GetType();
                var hasThing = AccessTools.Property(type, "HasThing")?.GetValue(targetInfoObj) as bool?;
                if (hasThing != true)
                    return;

                var thing = AccessTools.Property(type, "Thing")?.GetValue(targetInfoObj) as Thing;
                if (thing?.thingIDNumber > 0)
                    ids.Add(thing.thingIDNumber);
            }
            catch { }
        }

        private static Thing FindThingById(int thingId)
        {
            if (thingId <= 0)
                return null;

            try
            {
                foreach (var map in Find.Maps)
                {
                    if (map == null) continue;
                    var all = map.listerThings?.AllThings;
                    if (all == null) continue;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var thing = all[i];
                        if (thing?.thingIDNumber == thingId)
                            return thing;
                    }
                }
            }
            catch { }

            return null;
        }

        private static Pawn FindPawnById(int pawnId)
        {
            if (pawnId <= 0)
                return null;

            var thing = FindThingById(pawnId) as Pawn;
            if (thing != null)
                return thing;

            try
            {
                foreach (var pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                {
                    if (pawn?.thingIDNumber == pawnId)
                        return pawn;
                }
            }
            catch { }

            return null;
        }

        private static string GetDefNameField(object instance, string fieldName)
        {
            if (instance == null) return null;
            try
            {
                var defObj = AccessTools.Field(instance.GetType(), fieldName)?.GetValue(instance)
                             ?? AccessTools.Property(instance.GetType(), fieldName)?.GetValue(instance);
                if (defObj == null) return null;
                return AccessTools.Field(defObj.GetType(), "defName")?.GetValue(defObj) as string
                       ?? AccessTools.Property(defObj.GetType(), "defName")?.GetValue(defObj) as string;
            }
            catch
            {
                return null;
            }
        }

        private static int GetIntField(object instance, string fieldName, int defaultValue = 0)
        {
            if (instance == null) return defaultValue;
            try
            {
                var value = AccessTools.Field(instance.GetType(), fieldName)?.GetValue(instance)
                            ?? AccessTools.Property(instance.GetType(), fieldName)?.GetValue(instance);
                return value is int i ? i : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private static bool GetBoolField(object instance, string fieldName, bool defaultValue = false)
        {
            if (instance == null) return defaultValue;
            try
            {
                var value = AccessTools.Field(instance.GetType(), fieldName)?.GetValue(instance)
                            ?? AccessTools.Property(instance.GetType(), fieldName)?.GetValue(instance);
                return value is bool b ? b : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public static bool CQF_SendSignal_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            var signal = AccessTools.Field(__instance.GetType(), "signal")?.GetValue(__instance) as string;
            var ids = CollectTargetThingIds(targets);
            var packedIds = EncodeThingIds(ids);
            int sessionId = ResolveStorySessionIdFromThingIds(ids);
            if (SyncCqfSendSignalMethod == null)
            {
                LogStoryRuntimeOnce("sendSignal:missing-sync", "SyncCqfSendSignalMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            LogStoryRuntimeOnce("sendSignalPacked", $"SendSignal payload enabled: signal={signal ?? "null"}, packedLen={packedIds.Length}, idCount={ids.Length}.");
            if (!DoStorySyncRequest(SyncCqfSendSignalMethod, "sendSignal", sessionId, signal, packedIds))
                return false;
            return false;
        }

        public static bool CQF_GiveRewards_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            var thingDefName = GetDefNameField(__instance, "thingDef");
            var count = GetIntField(__instance, "count", 1);
            var ids = CollectTargetThingIds(targets);
            var packedIds = EncodeThingIds(ids);
            int sessionId = ResolveStorySessionIdFromThingIds(ids);
            if (SyncCqfGiveRewardsMethod == null)
            {
                LogStoryRuntimeOnce("giveRewards:missing-sync", "SyncCqfGiveRewardsMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfGiveRewardsMethod, "giveRewards", sessionId, thingDefName, count, packedIds))
                return false;
            return false;
        }

        public static bool CQF_DisableIncantation_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            var disable = GetBoolField(__instance, "disableIncantation", false);
            if (SyncCqfDisableIncantationMethod == null)
            {
                LogStoryRuntimeOnce("disableIncantation:missing-sync", "SyncCqfDisableIncantationMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfDisableIncantationMethod, "disableIncantation", 0, disable))
                return false;
            return false;
        }

        public static bool CQF_PlayerFaction_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            var ids = CollectTargetThingIds(targets);
            var packedIds = EncodeThingIds(ids);
            int sessionId = ResolveStorySessionIdFromThingIds(ids);
            if (SyncCqfPlayerFactionMethod == null)
            {
                LogStoryRuntimeOnce("playerFaction:missing-sync", "SyncCqfPlayerFactionMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfPlayerFactionMethod, "playerFaction", sessionId, packedIds))
                return false;
            return false;
        }

        public static bool CQF_RemoveHediff_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            var hediffDefName = GetDefNameField(__instance, "hediffDef");
            var ids = CollectTargetThingIds(targets);
            var packedIds = EncodeThingIds(ids);
            int sessionId = ResolveStorySessionIdFromThingIds(ids);
            if (SyncCqfRemoveHediffMethod == null)
            {
                LogStoryRuntimeOnce("removeHediff:missing-sync", "SyncCqfRemoveHediffMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfRemoveHediffMethod, "removeHediff", sessionId, hediffDefName, packedIds))
                return false;
            return false;
        }

        public static bool CQF_LeftMap_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            var ids = CollectTargetThingIds(targets);
            var packedIds = EncodeThingIds(ids);
            int sessionId = ResolveStorySessionIdFromThingIds(ids);
            if (SyncCqfLeftMapMethod == null)
            {
                LogStoryRuntimeOnce("leftMap:missing-sync", "SyncCqfLeftMapMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfLeftMapMethod, "leftMap", sessionId, packedIds))
                return false;
            return false;
        }

        public static bool CQF_AdjustRelation_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            int num = GetIntField(__instance, "value", GetIntField(__instance, "num", 0));
            if (SyncCqfAdjustRelationMethod == null)
            {
                LogStoryRuntimeOnce("adjustRelation:missing-sync", "SyncCqfAdjustRelationMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfAdjustRelationMethod, "adjustRelation", 0, num))
                return false;
            return false;
        }

        public static bool CQF_SwitchEntrance_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            bool value = GetBoolField(__instance, "value", false);
            var ids = CollectTargetThingIds(targets);
            var packedIds = EncodeThingIds(ids);
            int sessionId = ResolveStorySessionIdFromThingIds(ids);
            if (SyncCqfSwitchEntranceMethod == null)
            {
                LogStoryRuntimeOnce("switchEntrance:missing-sync", "SyncCqfSwitchEntranceMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfSwitchEntranceMethod, "switchEntrance", sessionId, value, packedIds))
                return false;
            return false;
        }

        public static bool CQF_UnlockTrader_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            if (SyncCqfUnlockTraderMethod == null)
            {
                LogStoryRuntimeOnce("unlockTrader:missing-sync", "SyncCqfUnlockTraderMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfUnlockTraderMethod, "unlockTrader", 0))
                return false;
            return false;
        }

        public static bool CQF_SendLetter_RealWork_Prefix(object __instance, object targets, Quest quest)
        {
            if (!ShouldBypassLocalCqfAction())
                return true;

            var title = AccessTools.Field(__instance.GetType(), "title")?.GetValue(__instance) as string;
            var desc = AccessTools.Field(__instance.GetType(), "desc")?.GetValue(__instance) as string;
            var letterDefName = GetDefNameField(__instance, "letterDef");
            var ids = CollectTargetThingIds(targets);
            var packedIds = EncodeThingIds(ids);
            int sessionId = ResolveStorySessionIdFromThingIds(ids);
            if (SyncCqfSendLetterMethod == null)
            {
                LogStoryRuntimeOnce("sendLetter:missing-sync", "SyncCqfSendLetterMethod unavailable, block local apply to prevent desync.", warning: true);
                return false;
            }

            if (!DoStorySyncRequest(SyncCqfSendLetterMethod, "sendLetter", sessionId, title, desc, letterDefName, packedIds))
                return false;
            return false;
        }

        public static void SyncCqfSendSignal(string eventId, int sessionId, string signal, string packedThingIds)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncCqfSendSignal)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfSendSignal)))
                return;

            var thingIds = DecodeThingIds(packedThingIds, nameof(SyncCqfSendSignal));
            if (string.IsNullOrEmpty(signal) || thingIds == null)
                return;

            for (int i = 0; i < thingIds.Length; i++)
            {
                var thing = FindThingById(thingIds[i]);
                if (thing == null)
                    continue;
                QuestUtility.SendQuestTargetSignals(thing.questTags, signal, thing.Named("SUBJECT"));
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfSendSignal));
        }

        public static void SyncCqfGiveRewards(string eventId, int sessionId, string thingDefName, int count, string packedPawnIds)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncCqfGiveRewards)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfGiveRewards)))
                return;

            var pawnIds = DecodeThingIds(packedPawnIds, nameof(SyncCqfGiveRewards));
            if (string.IsNullOrEmpty(thingDefName) || count <= 0 || pawnIds == null)
                return;

            var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            if (thingDef == null)
                return;

            for (int i = 0; i < pawnIds.Length; i++)
            {
                var pawn = FindPawnById(pawnIds[i]);
                if (pawn?.inventory == null)
                    continue;

                var thing = ThingMaker.MakeThing(thingDef);
                thing.stackCount = count;
                pawn.inventory.GetDirectlyHeldThings().TryAdd(thing.SplitOff(count));
                Messages.Message("RiM.ActionGiveRewards".Translate(pawn.LabelShort, thing.LabelShort, count), pawn, MessageTypeDefOf.PositiveEvent);
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfGiveRewards));
        }

        public static void SyncCqfDisableIncantation(string eventId, int sessionId, bool disableIncantation)
        {
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfDisableIncantation)))
                return;

            try
            {
                var rmUtilityType = AccessTools.TypeByName("RigorMortis.RMUtility");
                var component = AccessTools.Property(rmUtilityType, "TheComponent")?.GetValue(null);
                if (component == null)
                    return;
                AccessTools.Field(component.GetType(), "globalCanUseIncantationBurn")
                    ?.SetValue(component, !disableIncantation);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SyncCqfDisableIncantation failed: {e.Message}");
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfDisableIncantation));
        }

        public static void SyncCqfPlayerFaction(string eventId, int sessionId, string packedPawnIds)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncCqfPlayerFaction)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfPlayerFaction)))
                return;

            var pawnIds = DecodeThingIds(packedPawnIds, nameof(SyncCqfPlayerFaction));
            if (pawnIds == null) return;
            for (int i = 0; i < pawnIds.Length; i++)
            {
                var pawn = FindPawnById(pawnIds[i]);
                if (pawn != null && pawn.Faction != Faction.OfPlayer)
                    pawn.SetFaction(Faction.OfPlayer);
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfPlayerFaction));
        }

        public static void SyncCqfRemoveHediff(string eventId, int sessionId, string hediffDefName, string packedPawnIds)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncCqfRemoveHediff)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfRemoveHediff)))
                return;

            var pawnIds = DecodeThingIds(packedPawnIds, nameof(SyncCqfRemoveHediff));
            if (string.IsNullOrEmpty(hediffDefName) || pawnIds == null)
                return;

            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
            if (hediffDef == null)
                return;

            for (int i = 0; i < pawnIds.Length; i++)
            {
                var pawn = FindPawnById(pawnIds[i]);
                if (pawn?.health?.hediffSet == null)
                    continue;
                var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (hediff != null)
                    pawn.health.RemoveHediff(hediff);
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfRemoveHediff));
        }

        public static void SyncCqfLeftMap(string eventId, int sessionId, string packedPawnIds)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncCqfLeftMap)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfLeftMap)))
                return;

            var pawnIds = DecodeThingIds(packedPawnIds, nameof(SyncCqfLeftMap));
            if (pawnIds == null) return;

            for (int i = 0; i < pawnIds.Length; i++)
            {
                var pawn = FindPawnById(pawnIds[i]);
                if (pawn == null || pawn.Faction == Faction.OfPlayer || pawn.Map == null)
                    continue;

                pawn.jobs?.EndCurrentJob(Verse.AI.JobCondition.Succeeded);
                var lord = Verse.AI.Group.LordMaker.MakeNewLord(
                    pawn.Faction,
                    new Verse.AI.Group.LordJob_ExitMapBest(Verse.AI.LocomotionUrgency.Walk, true, true),
                    pawn.Map);
                lord?.AddPawn(pawn);
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfLeftMap));
        }

        public static void SyncCqfAdjustRelation(string eventId, int sessionId, int num)
        {
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfAdjustRelation)))
                return;

            try
            {
                var rmUtilityType = AccessTools.TypeByName("RigorMortis.RMUtility");
                var component = AccessTools.Property(rmUtilityType, "TheComponent")?.GetValue(null);
                if (component == null)
                    return;

                var relationField = AccessTools.Field(component.GetType(), "relationship");
                if (relationField == null)
                    return;
                int current = (int)relationField.GetValue(component);
                relationField.SetValue(component, current + num);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SyncCqfAdjustRelation failed: {e.Message}");
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfAdjustRelation));
        }

        public static void SyncCqfSwitchEntrance(string eventId, int sessionId, bool value, string packedThingIds)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncCqfSwitchEntrance)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfSwitchEntrance)))
                return;

            var thingIds = DecodeThingIds(packedThingIds, nameof(SyncCqfSwitchEntrance));
            if (thingIds == null) return;

            var maps = new HashSet<Map>();
            for (int i = 0; i < thingIds.Length; i++)
            {
                var thing = FindThingById(thingIds[i]);
                var pawn = thing as Pawn;
                if (pawn?.Map != null)
                    maps.Add(pawn.Map);
                else if (thing?.Map != null)
                    maps.Add(thing.Map);
            }

            foreach (var map in maps)
            {
                if (map?.listerThings?.AllThings == null)
                    continue;

                foreach (var thing in map.listerThings.AllThings)
                {
                    if (thing == null)
                        continue;
                    var method = AccessTools.Method(thing.GetType(), "Swtich", new[] { typeof(bool) })
                                 ?? AccessTools.Method(thing.GetType(), "Switch", new[] { typeof(bool) });
                    if (method == null)
                        continue;

                    try
                    {
                        method.Invoke(thing, new object[] { value });
                        Messages.Message(value ? "RiM.EntranceOpened".Translate() : "RiM.EntranceClosed".Translate(), thing, MessageTypeDefOf.NeutralEvent);
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] SyncCqfSwitchEntrance failed on {thing.GetType().FullName}: {e.Message}");
                    }
                }
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfSwitchEntrance));
        }

        public static void SyncCqfUnlockTrader(string eventId, int sessionId)
        {
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfUnlockTrader)))
                return;

            try
            {
                var rmUtilityType = AccessTools.TypeByName("RigorMortis.RMUtility");
                var component = AccessTools.Property(rmUtilityType, "TheComponent")?.GetValue(null);
                if (component == null)
                    return;
                AccessTools.Field(component.GetType(), "canTrade")?.SetValue(component, true);
                Find.LetterStack.ReceiveLetter("RiM.UnlockTrader".Translate(), "RiM.UnlockTraderDesc".Translate(), LetterDefOf.PositiveEvent);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] SyncCqfUnlockTrader failed: {e.Message}");
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfUnlockTrader));
        }

        public static void SyncCqfSendLetter(string eventId, int sessionId, string title, string desc, string letterDefName, string packedPawnIds)
        {
            if (!ValidateStorySessionForEvent(sessionId, nameof(SyncCqfSendLetter)))
                return;
            if (!TryBeginStoryCommit(eventId, nameof(SyncCqfSendLetter)))
                return;

            var pawnIds = DecodeThingIds(packedPawnIds, nameof(SyncCqfSendLetter));
            if (pawnIds == null || pawnIds.Length == 0)
                return;

            var letterDef = DefDatabase<LetterDef>.GetNamedSilentFail(letterDefName) ?? LetterDefOf.NeutralEvent;
            for (int i = 0; i < pawnIds.Length; i++)
            {
                var pawn = FindPawnById(pawnIds[i]);
                if (pawn == null)
                    continue;

                var letterTitle = string.IsNullOrEmpty(title) ? "Letter".Translate() : title.Translate(pawn.LabelShort);
                var letterDesc = string.IsNullOrEmpty(desc)
                    ? string.Empty
                    : desc.Translate().Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
                Find.LetterStack.ReceiveLetter(letterTitle, letterDesc, letterDef, pawn);
            }

            LogStoryAck(eventId, sessionId, nameof(SyncCqfSendLetter));
        }
    }
}
