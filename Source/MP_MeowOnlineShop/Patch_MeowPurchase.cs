using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// 联机下将喵喵电商的“确认购买”改为同步执行，避免仅在一端执行导致：
    /// 1) SetForbidden 等同步引用到“仅主机存在的”Silver 分割物 -> "Thing Silver-XX is inaccessible"
    /// 2) Letter 仅在一端创建 -> "Letter_XXX is not deep-saved" 与加载时引用解析失败。
    /// 做法：拦截 Dialog_OnlineShop.OnAcceptKeyPressed，收集 (pawn, shopDefName, 购物车)，通过同步方法在双端执行购买逻辑。
    /// </summary>
    internal static class Patch_MeowPurchase
    {
        /// <summary>由启动代码赋值；确认购买时经 <see cref="ISyncMethod.DoSync"/> 在双端执行备用发货逻辑。</summary>
        internal static ISyncMethod SyncApplyMeowPurchaseWithMap;

        private static class PurchaseTrace
        {
            private const string Tag = "[MP-Meow.Purchase]";
            private const int MaxLogs = 120;
            private static int _count;

            public static void Log(string msg)
            {
                if (!ModDebug.EnableMeowPurchaseTrace) return;
                if (System.Threading.Interlocked.Increment(ref _count) > MaxLogs) return;
                Verse.Log.Message($"{Tag} {msg}");
            }
        }

        /// <summary>为 true 时 Prefix 放行原 OnAcceptKeyPressed，避免“双端通过 ApplyMeowPurchase 调用原逻辑”时再次被拦截。</summary>
        [ThreadStatic] private static bool _invokingFromSync;

        /// <summary>缓存 Dialog 类型上用于购物车的字段（按类型扫描一次，避免固定字段名与 Meow 版本不一致）。</summary>
        private static FieldInfo _cachedCartField;

        private static FieldInfo FindCartField(Type dialogType)
        {
            if (_cachedCartField != null) return _cachedCartField;
            if (dialogType == null) return null;
            foreach (var f in dialogType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(List<ThingDefCount>))
                {
                    _cachedCartField = f;
                    return f;
                }
                if (f.FieldType.IsGenericType && !f.FieldType.IsGenericTypeDefinition)
                {
                    var gen = f.FieldType.GetGenericTypeDefinition();
                    var args = f.FieldType.GetGenericArguments();
                    if (args.Length == 1 && args[0] == typeof(ThingDefCount) &&
                        (gen == typeof(List<>) || gen == typeof(IList<>) || gen == typeof(ICollection<>)))
                    {
                        _cachedCartField = f;
                        return f;
                    }
                }
            }
            return null;
        }

        private const int SeedOffsetFallbackDrop = 0x7E4A;
        private const int FallbackWorldSeedOffset = 0x6D12;
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;
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

        private static bool PushMapRandIfExists(Map map, int seed)
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

        private static void PopMapRandIfExists(Map map)
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

        private static bool PushWorldRandIfExists(int seed)
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

        private static void PopWorldRandIfExists()
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

        /// <summary>联机同步方法（旧版签名，保留以兼容）：内部转发到基于 mapIndex 的新实现。</summary>
        public static bool ApplyMeowPurchase(Pawn pawn, string shopDefName, string[] thingDefNames, int[] counts, int totalSilverCost)
        {
            int mapIndex = pawn?.Map != null ? pawn.Map.Index : -1;
            return ApplyMeowPurchaseWithMap(mapIndex, shopDefName, thingDefNames, counts, totalSilverCost);
        }

        /// <summary>
        /// 联机同步方法（推荐）：通过地图 index 执行同一笔购买。
        /// 不再依赖 Pawn，本地从 Find.Maps 中解析 map，走备用路径（扣银+空投），避免 Meow 内部 Letter/银两引用导致掉线。
        /// </summary>
        public static bool ApplyMeowPurchaseWithMap(int mapIndex, string shopDefName, string[] thingDefNames, int[] counts, int totalSilverCost)
        {
            if (!MP.IsInMultiplayer)
                return false;
            // shopDefName 仅用于日志与兼容老版 SyncMethod 签名，本身不参与随机数/价格计算；
            // 若无法从对话框解析出有效名称，则使用一个固定占位字符串，保证多端一致而不再阻塞购买。
            if (string.IsNullOrEmpty(shopDefName))
                shopDefName = "Meow.UnknownShop";
            if (thingDefNames == null || counts == null || thingDefNames.Length != counts.Length || thingDefNames.Length == 0)
                return false;

            // 双端必须使用同一 Map：禁止 Find.CurrentMap / 任意 First 兜底，避免视角不同导致发货到不同地图。
            Map map = null;
            if (mapIndex >= 0)
            {
                try
                {
                    map = Find.Maps.FirstOrDefault(m => m != null && m.Index == mapIndex);
                }
                catch { }
            }
            else if (Find.Maps != null && Find.Maps.Count == 1)
            {
                map = Find.Maps[0];
            }

            if (map == null)
            {
                PurchaseTrace.Log($"ApplyWithMap ABORT no map for mapIndex={mapIndex} (maps={Find.Maps?.Count ?? -1})");
                return false;
            }

            PurchaseTrace.Log($"ApplyWithMap ENTER map={map.Index} tick={Find.TickManager?.TicksGame ?? -1} shop={shopDefName} items={thingDefNames.Length} cost={totalSilverCost}");

            var thingDefCountList = new List<ThingDefCount>();
            for (int i = 0; i < thingDefNames.Length; i++)
            {
                var td = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefNames[i]);
                if (td != null && counts[i] > 0)
                    thingDefCountList.Add(new ThingDefCount(td, counts[i]));
            }
            if (thingDefCountList.Count == 0) return false;

            // 联机下绝不走 Meow 内部逻辑：ProcessOrder/OnAcceptKeyPressed 会创建 Letter、对银两 SetForbidden，
            // 导致 "Letter_XXX is not deep-saved" 与 "Thing Silver-XX is inaccessible"，且可能只在主机执行导致客户端看不到空投。
            // 仅使用备用路径（扣银 + 空投），双端由同步方法统一执行，不创建 Letter。
            if (totalSilverCost > 0)
            {
                try
                {
                    bool ok = ApplyMeowPurchaseFallbackOnMap(map, thingDefCountList, totalSilverCost);
                    PurchaseTrace.Log($"ApplyWithMap EXIT ok={ok} map={map.Index}");
                    return ok;
                }
                catch (Exception e)
                {
                    Log.Error($"[MP-MeowOnlineShop] ApplyMeowPurchaseWithMap fallback failed: {e}");
                }
            }
            PurchaseTrace.Log($"ApplyWithMap EXIT ok=false (no cost) map={map.Index}");
            return false;
        }

        /// <summary>备用：双端自行扣银并空投，不创建 Letter，扣银与落点均用确定性顺序/种子，便于多端一致（基于 Map 而非 Pawn）。</summary>
        private static bool ApplyMeowPurchaseFallbackOnMap(Map map, List<ThingDefCount> cart, int totalSilverCost)
        {
            if (map == null || cart == null || cart.Count == 0 || totalSilverCost <= 0) return false;

            int tick = Find.TickManager?.TicksGame ?? 0;
            int seed = Gen.HashCombineInt(Gen.HashCombineInt(tick, map.Index), SeedOffsetFallbackDrop);
            PurchaseTrace.Log($"Fallback ENTER map={map.Index} tick={tick} seed={seed} cost={totalSilverCost} cart={string.Join(",", cart.Select(x => $"{x.ThingDef?.defName}x{x.Count}"))}");

            // 注意：大多数银锭的 Faction 实际为 null，仅通过 map 和 def 标识归属；
            // 这里不再强制要求 t.Faction == Faction.OfPlayer，否则在有银但无派系标记时 available 会被误判为 0。
            var silverList = map.listerThings.ThingsOfDef(ThingDefOf.Silver)
                .Where(t => t.Spawned && !t.Destroyed)
                .OrderBy(t => t.thingIDNumber)
                .ToList();
            int available = silverList.Sum(t => t.stackCount);
            PurchaseTrace.Log($"Fallback SILVER available={available} stacks={silverList.Count} need={totalSilverCost}");

            // 与 Prefix 一致：银子不足则不扣银、不发货，避免双端“主机扣了/客户端白送”类分叉。
            if (available < totalSilverCost)
            {
                PurchaseTrace.Log($"Fallback BLOCKED insufficient silver available={available} need={totalSilverCost}");
                return false;
            }

            // 注意：不要调用 SplitOff，否则 CompForbiddable.PostSplitOff 会触发 ForbidUtility.SetForbidden，
            // 进而走 Multiplayer 的 SetForbidden 同步路径，导致 "Thing Silver-XX is inaccessible"。
            // 这里直接修改 stackCount 并在清零时 Destroy，避免 PostSplitOff 的副作用。
            int remaining = totalSilverCost;
            foreach (var t in silverList)
            {
                if (remaining <= 0) break;
                if (t == null || t.Destroyed) continue;

                int take = Math.Min(remaining, t.stackCount);
                if (take <= 0) continue;

                if (take >= t.stackCount)
                {
                    remaining -= t.stackCount;
                    // 使用 Vanish，避免多余的视觉/声音效果，同时减少潜在的随机数消耗
                    t.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    t.stackCount -= take;
                    remaining -= take;
                }
            }
            if (remaining != 0)
            {
                PurchaseTrace.Log($"Fallback ABORT silver deduction incomplete remaining={remaining} (race or desync?)");
                return false;
            }
            PurchaseTrace.Log($"Fallback SILVER deducted={totalSilverCost} (availableAtStart={available})");

            var thingsToDrop = new List<Thing>();
            foreach (var tc in cart)
            {
                int stackLimit = tc.ThingDef.stackLimit > 0 ? tc.ThingDef.stackLimit : 1;
                int left = tc.Count;
                while (left > 0)
                {
                    int stack = Math.Min(left, stackLimit);
                    var thing = ThingMaker.MakeThing(tc.ThingDef);
                    thing.stackCount = stack;
                    thingsToDrop.Add(thing);
                    left -= stack;
                }
            }

            IntVec3 dropCell;
            int randState = 0;
            Rand.PushState(seed);
            randState |= StateStaticRand;
            bool mapRandPushed = false;
            if (PushMapRandIfExists(map, seed))
            {
                mapRandPushed = true;
                randState |= StateMapRand;
            }
            if (PushWorldRandIfExists(seed + FallbackWorldSeedOffset))
                randState |= StateWorldRand;
            if (ModDebug.EnableMeowPurchaseTrace)
                PurchaseTrace.Log($"Fallback RAND push state=0x{randState:X} map={map.Index} seed={seed}");
            try
            {
                // TradeDropSpot 与 DropThingsNear 都会消耗 Rand（含 map.Rand）；必须在同一 Push/Pop 内完成，
                // 否则 Pop 后再空投会在双端产生不同的 map 随机消耗顺序 -> "Wrong random state on map X"。
                dropCell = DropCellFinder.TradeDropSpot(map);
                PurchaseTrace.Log($"Fallback DROP cell={dropCell} thingsStacks={thingsToDrop.Count}");
                DropPodUtility.DropThingsNear(dropCell, map, thingsToDrop, 110, false, false, true);
            }
            finally
            {
                if ((randState & StateWorldRand) != 0)
                    PopWorldRandIfExists();
                if ((randState & StateMapRand) != 0 && mapRandPushed)
                    PopMapRandIfExists(map);
                if ((randState & StateStaticRand) != 0)
                    Rand.PopState();
                if (ModDebug.EnableMeowPurchaseTrace)
                    PurchaseTrace.Log($"Fallback RAND pop state=0x{randState:X}");
            }
            PurchaseTrace.Log("Fallback EXIT ok=true");
            return true;
        }

        /// <summary>设置 Dialog 的购物车。优先用 FindCartField 找到的字段，再尝试常见名称。返回 true 表示设置成功。</summary>
        private static bool SetCartViaReflection(object dialog, List<ThingDefCount> cart, Type dialogType)
        {
            if (dialog == null || cart == null || dialogType == null) return false;
            var cartCopy = new List<ThingDefCount>(cart);
            var t = dialog.GetType();

            var f = FindCartField(dialogType);
            if (f != null)
            {
                try
                {
                    if (f.FieldType == typeof(List<ThingDefCount>))
                    {
                        f.SetValue(dialog, cartCopy);
                        return true;
                    }
                    var existing = f.GetValue(dialog);
                    if (existing is List<ThingDefCount> list)
                    {
                        list.Clear();
                        list.AddRange(cart);
                        return true;
                    }
                }
                catch { }
            }

            foreach (var name in new[] { "purchases", "orderList", "order", "selectedItems", "cart", "PurchaseList", "orders" })
            {
                var field = AccessTools.Field(t, name);
                if (field == null) continue;
                if (field.FieldType == typeof(List<ThingDefCount>))
                {
                    try { field.SetValue(dialog, cartCopy); return true; } catch { }
                }
                if (field.FieldType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(field.FieldType))
                {
                    try
                    {
                        var existing = field.GetValue(dialog);
                        if (existing is List<ThingDefCount> list)
                        {
                            list.Clear();
                            list.AddRange(cart);
                            return true;
                        }
                        if (existing is IList listObj && existing.GetType().GetGenericArguments().Length > 0)
                        {
                            var add = existing.GetType().GetMethod("AddRange", new[] { typeof(IEnumerable<ThingDefCount>) })
                                ?? existing.GetType().GetMethod("Add");
                            if (add != null)
                            {
                                var clear = existing.GetType().GetMethod("Clear");
                                clear?.Invoke(existing, null);
                                foreach (var item in cart)
                                    add.Invoke(existing, new object[] { item });
                                return true;
                            }
                        }
                    }
                    catch { }
                }
            }
            return false;
        }

        /// <summary>从 Dialog 反射总价（银）。尝试 totalCost / cost / totalSilver / TotalCost 等。</summary>
        private static int TryGetTotalCost(object dialog)
        {
            if (dialog == null) return 0;
            var t = dialog.GetType();
            foreach (var name in new[] { "totalCost", "cost", "totalSilver", "TotalCost", "Cost", "TotalSilver", "totalPrice", "price" })
            {
                var f = AccessTools.Field(t, name);
                if (f != null && (f.FieldType == typeof(int) || f.FieldType == typeof(float)))
                {
                    try
                    {
                        var v = f.GetValue(dialog);
                        if (v is int i) return i;
                        if (v is float fl) return (int)fl;
                    }
                    catch { }
                }
                var p = AccessTools.Property(t, name);
                if (p != null && (p.PropertyType == typeof(int) || p.PropertyType == typeof(float)))
                {
                    try
                    {
                        var v = p.GetValue(dialog);
                        if (v is int i) return i;
                        if (v is float fl) return (int)fl;
                    }
                    catch { }
                }
            }
            return 0;
        }

        /// <summary>
        /// 专门解析 Meow.Dialog_OnlineShop 内部的 buyData 字段：
        /// Dictionary<ShopGoods, (int, string, int)>。
        /// - Key: ShopGoods（通常持有 ThingDef 或 defName）
        /// - Value: (int, string, int)，string 一般为显示名/标识，两个 int 分别为价格/数量/折扣等。
        /// 我们只关心 (defName, count)：
        /// - defName 优先从 ShopGoods 内的 ThingDef/Def 或带 def/defName 的 string 字段/属性解析，若失败再退回 Value.Item2。
        /// - count 优先取 Value.Item3，若 <=0 再退回 Value.Item1。
        /// </summary>
        private static bool TryGetCartFromBuyData(object dialog, Type dialogType, out string[] defNames, out int[] counts)
        {
            defNames = null;
            counts = null;
            if (dialog == null || dialogType == null) return false;

            var buyDataField = AccessTools.Field(dialogType, "buyData");
            if (buyDataField == null) return false;

            object buyDataObj = null;
            try
            {
                buyDataObj = buyDataField.GetValue(dialog);
            }
            catch
            {
                return false;
            }

            var en = buyDataObj as IEnumerable;
            if (en == null) return false;

            var items = new List<(string defName, int count)>();

            foreach (var kv in en)
            {
                if (kv == null) continue;
                try
                {
                    var kvType = kv.GetType();
                    var keyProp = kvType.GetProperty("Key");
                    var valueProp = kvType.GetProperty("Value");
                    if (valueProp == null) continue;

                    var keyObj = keyProp != null ? keyProp.GetValue(kv, null) : null;
                    var tuple = valueProp.GetValue(kv, null);
                    if (tuple == null) continue;

                    // 调试：在启用 MeowPurchaseTrace 时，打印当前 buyData 条目的关键原始信息，便于从日志推断真正的数量字段。
                    if (ModDebug.EnableMeowPurchaseTrace)
                    {
                        try
                        {
                            var tTypeDbg = tuple.GetType();
                            object i1Dbg = null, i2Dbg = null, i3Dbg = null;
                            var p1 = tTypeDbg.GetProperty("Item1");
                            var p2 = tTypeDbg.GetProperty("Item2");
                            var p3 = tTypeDbg.GetProperty("Item3");
                            if (p1 != null) i1Dbg = p1.GetValue(tuple, null);
                            if (p2 != null) i2Dbg = p2.GetValue(tuple, null);
                            if (p3 != null) i3Dbg = p3.GetValue(tuple, null);

                            PurchaseTrace.Log($"buyData raw tuple: type={tTypeDbg.FullName} Item1={i1Dbg ?? "null"}, Item2={i2Dbg ?? "null"}, Item3={i3Dbg ?? "null"}");

                            // 进一步枚举 tuple 上的所有数值字段/属性，帮助确认真正的“数量”成员名称
                            foreach (var f in tTypeDbg.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                object v = null;
                                try { v = f.GetValue(tuple); } catch { }
                                if (v is int || v is float || v is double)
                                    PurchaseTrace.Log($"buyData TUPLE FIELD {f.Name}: {v}");
                            }
                            foreach (var p in tTypeDbg.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (!p.CanRead) continue;
                                object v = null;
                                try { v = p.GetValue(tuple, null); } catch { }
                                if (v is int || v is float || v is double)
                                    PurchaseTrace.Log($"buyData TUPLE PROP  {p.Name}: {v}");
                            }

                            if (keyObj != null)
                            {
                                var gTypeDbg = keyObj.GetType();
                                foreach (var f in gTypeDbg.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                {
                                    object v = null;
                                    try { v = f.GetValue(keyObj); } catch { }
                                    if (v is int || v is float || v is double)
                                        PurchaseTrace.Log($"ShopGoods FIELD {f.Name}: {v}");
                                }
                                foreach (var p in gTypeDbg.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                {
                                    if (!p.CanRead) continue;
                                    object v = null;
                                    try { v = p.GetValue(keyObj, null); } catch { }
                                    if (v is int || v is float || v is double)
                                        PurchaseTrace.Log($"ShopGoods PROP  {p.Name}: {v}");
                                }
                            }
                        }
                        catch
                        {
                            // 调试输出失败可以忽略
                        }
                    }

                    // 1) 先从 ShopGoods（Key）里解析 ThingDef/defName
                    string defName = null;
                    if (keyObj != null)
                    {
                        var gType = keyObj.GetType();

                        // 1.1 字段里找 ThingDef/Def 或 defName 字符串
                        foreach (var f in gType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            try
                            {
                                if (typeof(ThingDef).IsAssignableFrom(f.FieldType))
                                {
                                    var td = f.GetValue(keyObj) as ThingDef;
                                    if (td != null)
                                    {
                                        defName = td.defName;
                                        break;
                                    }
                                }
                                else if (typeof(Def).IsAssignableFrom(f.FieldType) && string.IsNullOrEmpty(defName))
                                {
                                    var d = f.GetValue(keyObj) as Def;
                                    if (d != null)
                                    {
                                        defName = d.defName;
                                        break;
                                    }
                                }
                                else if (f.FieldType == typeof(string) && string.IsNullOrEmpty(defName))
                                {
                                    var nameLower = f.Name.ToLowerInvariant();
                                    if (nameLower.Contains("def") || nameLower.Contains("id") || nameLower.Contains("defname"))
                                    {
                                        var s = f.GetValue(keyObj) as string;
                                        if (!string.IsNullOrEmpty(s))
                                        {
                                            defName = s;
                                            break;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        // 1.2 属性里再找一遍
                        if (string.IsNullOrEmpty(defName))
                        {
                            foreach (var p in gType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (!p.CanRead) continue;
                                try
                                {
                                    if (typeof(ThingDef).IsAssignableFrom(p.PropertyType))
                                    {
                                        var td = p.GetValue(keyObj, null) as ThingDef;
                                        if (td != null)
                                        {
                                            defName = td.defName;
                                            break;
                                        }
                                    }
                                    else if (typeof(Def).IsAssignableFrom(p.PropertyType))
                                    {
                                        var d = p.GetValue(keyObj, null) as Def;
                                        if (d != null)
                                        {
                                            defName = d.defName;
                                            break;
                                        }
                                    }
                                    else if (p.PropertyType == typeof(string))
                                    {
                                        var nameLower = p.Name.ToLowerInvariant();
                                        if (nameLower.Contains("def") || nameLower.Contains("id") || nameLower.Contains("defname"))
                                        {
                                            var s = p.GetValue(keyObj, null) as string;
                                            if (!string.IsNullOrEmpty(s))
                                            {
                                                defName = s;
                                                break;
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    // 2) 如仍未找到，则退回到 Value.Item2（若是 defName/内部标识）
                    if (string.IsNullOrEmpty(defName))
                    {
                        var tupleType = tuple.GetType();
                        var item2Prop = tupleType.GetProperty("Item2");
                        if (item2Prop != null)
                        {
                            try
                            {
                                defName = item2Prop.GetValue(tuple, null) as string;
                            }
                            catch { }
                        }
                    }

                    if (string.IsNullOrEmpty(defName))
                        continue;

                    // 3) 解析数量：Meow 的 ValueTuple 为 (Item1, Item2, Item3)，
                    //    其中 Item1 = 用户选择购买的数量，Item3 = 该行其它数值（如库存/上限），不能当购买数量。
                    //    日志证实：只买 1 个金块时 Item1=1/Item3=0，未选行 Item1=0 而 Item3 可能为 17/20/15，若用 Item3 会误加多件。
                    //    因此只取 Item1 作为数量；仅当 count > 0 时加入购物车。
                    int count = 0;
                    var tType = tuple.GetType();

                    int TryGetIntFromMember(object target, Type type, string name)
                    {
                        try
                        {
                            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (field != null)
                            {
                                var cObj = field.GetValue(target);
                                if (cObj is int ci) return ci;
                                if (cObj is float cf) return (int)cf;
                            }
                            var prop = type.GetProperty(name);
                            if (prop != null)
                            {
                                var cObj = prop.GetValue(target, null);
                                if (cObj is int ci) return ci;
                                if (cObj is float cf) return (int)cf;
                            }
                        }
                        catch { }
                        return 0;
                    }

                    count = TryGetIntFromMember(tuple, tType, "Item1");

                    // 仅当数量 > 0 时加入购物车；未选商品 Item1=0，不加入
                    if (count > 0)
                        items.Add((defName, count));
                }
                catch
                {
                    // 单个元素解析失败忽略，继续处理后续
                }
            }

            if (items.Count == 0) return false;

            defNames = items.Select(x => x.defName).ToArray();
            counts = items.Select(x => x.count).ToArray();
            return true;
        }

        /// <summary>
        /// 从 Dialog_OnlineShop 实例中取出购物车，转为 (defNames[], counts[])。
        /// 先尝试缓存字段，其次按常见字段名枚举字段，再按属性枚举（兼容新版 Meow 只暴露属性不暴露字段的情况），
        /// 最后在失败时输出详细调试信息，帮助定位实际存储购物车的数据结构。
        /// </summary>
        private static bool TryGetCart(object dialog, out string[] defNames, out int[] counts)
        {
            defNames = null;
            counts = null;
            if (dialog == null) return false;
            var t = dialog.GetType();

            // 0) 针对当前 Meow 版本的专用逻辑：buyData: Dictionary<ShopGoods, (int, string, int)>
            if (TryGetCartFromBuyData(dialog, t, out defNames, out counts))
                return true;

            var cartField = FindCartField(t);
            if (cartField != null)
            {
                try
                {
                    var val = cartField.GetValue(dialog);
                    if (TryParseCartValue(val, out defNames, out counts))
                        return true;
                }
                catch { }
            }

            // 1) 按常见名称在字段上找
            foreach (var name in new[] { "purchases", "orderList", "order", "selectedItems", "cart", "PurchaseList", "orders" })
            {
                var f = AccessTools.Field(t, name);
                if (f == null) continue;
                try
                {
                    var val = f.GetValue(dialog);
                    if (TryParseCartValue(val, out defNames, out counts))
                        return true;
                }
                catch { }
            }

            // 2) 若字段全部失败，再在属性上按同样的名称与类型规则查找（兼容用自动属性保存购物车的实现）
            foreach (var name in new[] { "purchases", "orderList", "order", "selectedItems", "cart", "PurchaseList", "orders" })
            {
                var p = AccessTools.Property(t, name);
                if (p == null || !p.CanRead) continue;
                try
                {
                    var val = p.GetValue(dialog);
                    if (TryParseCartValue(val, out defNames, out counts))
                        return true;
                }
                catch { }
            }

            // 3) 兜底：扫描所有实例属性，挑出类型实现 IEnumerable 且元素结构符合 TryParseCartValue 预期的第一个
            try
            {
                foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!p.CanRead) continue;
                    var pt = p.PropertyType;
                    if (pt == typeof(List<ThingDefCount>) ||
                        typeof(IEnumerable).IsAssignableFrom(pt))
                    {
                        try
                        {
                            var val = p.GetValue(dialog);
                            if (TryParseCartValue(val, out defNames, out counts))
                                return true;
                        }
                        catch
                        {
                            // 忽略单个属性失败，继续尝试其它属性
                        }
                    }
                }
            }
            catch
            {
                // 全局反射失败则保持返回 false
            }

            // 走到这里说明所有启发式解析均失败，为了后续能精确适配当前 Meow 版本，
            // 在调试开关开启时打印该对话框上所有字段/属性的类型信息，便于从日志中找到真正的购物车容器。
            if (ModDebug.EnableMeowPurchaseTrace)
            {
                try
                {
                    PurchaseTrace.Log($"TryGetCart FAILED, dump for dialog type: {t.FullName}");

                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        object val = null;
                        string valType = "null";
                        string extra = "";
                        try
                        {
                            val = f.GetValue(dialog);
                            if (val != null)
                            {
                                valType = val.GetType().FullName;
                                if (val is IEnumerable en)
                                {
                                    int cnt = 0;
                                    var enTypeName = "";
                                    foreach (var o in en)
                                    {
                                        if (o == null) continue;
                                        enTypeName = o.GetType().FullName;
                                        cnt++;
                                        if (cnt >= 3) break;
                                    }
                                    extra = $" IEnumerable count~{cnt} elemType={enTypeName}";
                                }
                            }
                        }
                        catch { }
                        PurchaseTrace.Log($"  FIELD {f.Name} : {f.FieldType.FullName} valueType={valType}{extra}");
                    }

                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!p.CanRead) continue;
                        object val = null;
                        string valType = "null";
                        string extra = "";
                        try
                        {
                            val = p.GetValue(dialog, null);
                            if (val != null)
                            {
                                valType = val.GetType().FullName;
                                if (val is IEnumerable en)
                                {
                                    int cnt = 0;
                                    var enTypeName = "";
                                    foreach (var o in en)
                                    {
                                        if (o == null) continue;
                                        enTypeName = o.GetType().FullName;
                                        cnt++;
                                        if (cnt >= 3) break;
                                    }
                                    extra = $" IEnumerable count~{cnt} elemType={enTypeName}";
                                }
                            }
                        }
                        catch { }
                        PurchaseTrace.Log($"  PROP  {p.Name} : {p.PropertyType.FullName} valueType={valType}{extra}");
                    }
                }
                catch
                {
                    // 调试输出失败可以忽略
                }
            }

            return false;
        }

        private static bool TryParseCartValue(object val, out string[] defNames, out int[] counts)
        {
            defNames = null;
            counts = null;
            if (val is IEnumerable<ThingDefCount> list)
            {
                var l = list.ToList();
                if (l.Count == 0) return false;
                defNames = new string[l.Count];
                counts = new int[l.Count];
                for (int i = 0; i < l.Count; i++)
                {
                    defNames[i] = l[i].ThingDef?.defName ?? "";
                    counts[i] = l[i].Count;
                }
                return true;
            }
            if (val is IEnumerable en)
            {
                var items = new List<(string defName, int count)>();
                Type elementType = null;
                foreach (var o in en)
                {
                    if (o == null) continue;
                    elementType = o.GetType();
                    break;
                }

                if (elementType != null)
                {
                    // 0) 特判 Dictionary<ThingDef,int> / IEnumerable<KeyValuePair<ThingDef,int/float>>
                    if (elementType.IsGenericType &&
                        elementType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                    {
                        var args = elementType.GetGenericArguments();
                        if (args.Length == 2 && typeof(ThingDef).IsAssignableFrom(args[0]) &&
                            (args[1] == typeof(int) || args[1] == typeof(float)))
                        {
                            foreach (var o in en)
                            {
                                if (o == null) continue;
                                ThingDef td = null;
                                int c = 0;
                                try
                                {
                                    td = elementType.GetProperty("Key")?.GetValue(o) as ThingDef;
                                    var vObj = elementType.GetProperty("Value")?.GetValue(o);
                                    if (vObj is int vi) c = vi;
                                    else if (vObj is float vf) c = (int)vf;
                                }
                                catch { }
                                if (td != null && c > 0)
                                    items.Add((td.defName, c));
                            }
                        }
                    }
                    // 1) 若元素本身就是 ThingDefCount（或派生），按属性 ThingDef/Count 解析
                    else if (typeof(ThingDefCount).IsAssignableFrom(elementType))
                    {
                        foreach (var o in en)
                        {
                            if (o == null) continue;
                            var td = AccessTools.Property(elementType, "ThingDef")?.GetValue(o) as ThingDef;
                            var cObj = AccessTools.Property(elementType, "Count")?.GetValue(o);
                            int c = 0;
                            if (cObj is int ci) c = ci;
                            else if (cObj is float cf) c = (int)cf;
                            if (td != null && c > 0)
                                items.Add((td.defName, c));
                        }
                    }
                    else
                    {
                        // 2) 泛用解析：尝试在元素类型上找到 ThingDef 和 数量(int) 字段/属性
                        MemberInfo thingMember = null;
                        MemberInfo countMember = null;

                        foreach (var f in elementType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (thingMember == null && typeof(ThingDef).IsAssignableFrom(f.FieldType))
                                thingMember = f;
                            if (countMember == null && f.FieldType == typeof(int) &&
                                (f.Name.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 f.Name.IndexOf("amount", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 f.Name.IndexOf("qty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 f.Name.IndexOf("quantity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 f.Name.IndexOf("stack", StringComparison.OrdinalIgnoreCase) >= 0))
                                countMember = f;
                        }

                        foreach (var p in elementType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (thingMember == null && typeof(ThingDef).IsAssignableFrom(p.PropertyType))
                                thingMember = p;
                            if (countMember == null && p.PropertyType == typeof(int) &&
                                (p.Name.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 p.Name.IndexOf("amount", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 p.Name.IndexOf("qty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 p.Name.IndexOf("quantity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 p.Name.IndexOf("stack", StringComparison.OrdinalIgnoreCase) >= 0))
                                countMember = p;
                        }

                        if (thingMember != null && countMember != null)
                        {
                            foreach (var o in en)
                            {
                                if (o == null) continue;
                                ThingDef td = null;
                                int c = 0;

                                try
                                {
                                    if (thingMember is FieldInfo tf)
                                        td = tf.GetValue(o) as ThingDef;
                                    else if (thingMember is PropertyInfo tp)
                                        td = tp.GetValue(o, null) as ThingDef;
                                }
                                catch { }

                                try
                                {
                                    object cObj = null;
                                    if (countMember is FieldInfo cf)
                                        cObj = cf.GetValue(o);
                                    else if (countMember is PropertyInfo cp)
                                        cObj = cp.GetValue(o, null);

                                    if (cObj is int ci) c = ci;
                                    else if (cObj is float cf2) c = (int)cf2;
                                }
                                catch { }

                                if (td != null && c > 0)
                                    items.Add((td.defName, c));
                            }
                        }
                    }
                }

                if (items.Count > 0)
                {
                    defNames = items.Select(x => x.defName).ToArray();
                    counts = items.Select(x => x.count).ToArray();
                    return true;
                }
            }
            return false;
        }

        private static bool Prefix(object __instance)
        {
            // 这里不要依赖调试开关，始终打印一条简短日志，便于确认前缀是否真正被调用。
            try
            {
                Log.Message("[MP-MeowOnlineShop] MeowPurchase Prefix RAW enter");
            }
            catch { }

            if (!MP.IsInMultiplayer)
            {
                try { Log.Message("[MP-MeowOnlineShop] MeowPurchase Prefix: not in multiplayer, letting original run."); } catch { }
                return true;
            }
            if (_invokingFromSync)
            {
                _invokingFromSync = false;
                return true;
            }

            try
            {
                var dialog = __instance;
                if (dialog == null)
                {
                    // 联机下拿不到对话框实例，说明当前调用环境异常，直接阻止原逻辑，避免未知状态变更导致不同步。
                    try { Log.Warning("[MP-MeowOnlineShop] MeowPurchase Prefix: dialog instance is null, blocking original to avoid desync."); } catch { }
                    return false;
                }

                // 使用实际运行时类型而不是硬编码的 Meow.Dialog_OnlineShop，避免不同版本/命名空间导致 IsInstanceOfType 失败。
                var dialogType = dialog.GetType();
                if (dialogType == null)
                {
                    // 原逻辑一旦执行就会走 Meow 内部的扣银/Letter/SetForbidden，我们已经知道这会在联机下制造 Silver 不可访问与 Letter 未 deep-save。
                    // 因此在联机环境中一旦无法可靠识别对话框类型，就宁可直接拦截购买，也不再交回原方法执行。
                    try
                    {
                        Log.Warning("[MP-MeowOnlineShop] MeowPurchase Prefix: dialogType is null, blocking original to avoid desync.");
                    }
                    catch { }
                    return false;
                }

                // 优先尝试标准的 negotiator 属性/字段
                Pawn pawn = AccessTools.Property(dialogType, "negotiator")?.GetValue(dialog) as Pawn
                    ?? AccessTools.Field(dialogType, "negotiator")?.GetValue(dialog) as Pawn;

                // 若失败，则在该对话框类型上扫描任意 Pawn 类型的实例字段，尽量找到“谈判者/操作者”。
                if (pawn == null)
                {
                    try
                    {
                        foreach (var f in dialogType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            if (!typeof(Pawn).IsAssignableFrom(f.FieldType)) continue;
                            var val = f.GetValue(dialog) as Pawn;
                            if (val != null)
                            {
                                pawn = val;
                                Log.Message($"[MP-MeowOnlineShop] MeowPurchase Prefix: negotiator resolved via field '{f.Name}'.");
                                break;
                            }
                        }
                    }
                    catch { /* 反射失败则保持 pawn 为 null */ }
                }

                // 尝试从 Pawn 或 Dialog 中解析 Map；若均失败，则在同步方法中再用 CurrentMap/Maps.FirstOrDefault 兜底。
                Map map = pawn?.Map;
                if (map == null)
                {
                    try
                    {
                        map = AccessTools.Property(dialogType, "map")?.GetValue(dialog) as Map
                              ?? AccessTools.Field(dialogType, "map")?.GetValue(dialog) as Map;
                        if (map == null)
                        {
                            foreach (var f in dialogType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                            {
                                if (!typeof(Map).IsAssignableFrom(f.FieldType)) continue;
                                var mv = f.GetValue(dialog) as Map;
                                if (mv != null)
                                {
                                    map = mv;
                                    Log.Message($"[MP-MeowOnlineShop] MeowPurchase Prefix: map resolved via field '{f.Name}'.");
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                int mapIndex = map != null ? map.Index : -1;
                int pawnId = pawn?.thingIDNumber ?? -1;
                PurchaseTrace.Log($"Prefix ENTER pawn={pawnId} map={mapIndex} tick={Find.TickManager?.TicksGame ?? -1}");

                object shopDef = AccessTools.Property(dialogType, "shopDef")?.GetValue(dialog)
                    ?? AccessTools.Property(dialogType, "shopFaction")?.GetValue(dialog)
                    ?? AccessTools.Field(dialogType, "shopDef")?.GetValue(dialog)
                    ?? AccessTools.Field(dialogType, "shopFaction")?.GetValue(dialog);
                string shopDefName = (shopDef as Def)?.defName
                    ?? AccessTools.Property(shopDef?.GetType(), "defName")?.GetValue(shopDef) as string
                    ?? AccessTools.Field(shopDef?.GetType(), "defName")?.GetValue(shopDef) as string;
                if (string.IsNullOrEmpty(shopDefName))
                {
                    // 当前 Meow 版本可能已不再暴露 shopDef / shopFaction，我们无法获取具体商店标识。
                    // 由于该名称仅用于日志与老版 SyncMethod 签名，不影响 Rand 与价格计算，
                    // 这里改为使用一个固定占位符，保证多端一致同时不再阻止购买逻辑。
                    shopDefName = "Meow.UnknownShop";
                    try
                    {
                        Log.Warning("[MP-MeowOnlineShop] MeowPurchase Prefix: shopDefName is null/empty, fallback to 'Meow.UnknownShop'.");
                    }
                    catch { }
                }

                if (!TryGetCart(dialog, out string[] defNames, out int[] counts) || defNames == null || defNames.Length == 0)
                {
                    Log.Warning("[MP-MeowOnlineShop] MeowPurchase Prefix: could not read cart from dialog, blocking original to avoid desync.");
                    return false;
                }
                PurchaseTrace.Log($"Prefix CART shop={shopDefName} items={string.Join(",", defNames.Zip(counts, (d, c) => $"{d}x{c}"))}");

                // 联机模式下，为了绝对稳定，完全忽略 Meow 对话框自身的总价/折扣/运费字段，
                // 统一使用 BaseMarketValue * 数量 作为价格模型，保证在所有客户端与主机上都是确定性的，
                // 且不依赖任何可能包含随机数的 Mod 内部逻辑。
                int totalSilverCost = 0;
                if (defNames != null && counts != null && defNames.Length == counts.Length)
                {
                    for (int i = 0; i < defNames.Length; i++)
                    {
                        if (string.IsNullOrEmpty(defNames[i]) || counts[i] <= 0) continue;
                        var td = DefDatabase<ThingDef>.GetNamedSilentFail(defNames[i]);
                        if (td == null) continue;
                        var mv = td.BaseMarketValue;
                        if (mv > 0f)
                            totalSilverCost += (int)(mv * counts[i]);
                    }
                }

                // 从对话框中读取 Meow 计算好的 availableSilver，用于与 UI 一致地判断“银子是否足够”。
                // 若 availableSilver < totalSilverCost，则直接阻止下单，不再进入备用扣银/发货逻辑。
                float availableSilverUi = 0f;
                try
                {
                    var availField = AccessTools.Field(dialogType, "availableSilver");
                    if (availField != null &&
                        (availField.FieldType == typeof(float) ||
                         availField.FieldType == typeof(double) ||
                         availField.FieldType == typeof(int)))
                    {
                        var v = availField.GetValue(dialog);
                        if (v is float f) availableSilverUi = f;
                        else if (v is double d) availableSilverUi = (float)d;
                        else if (v is int i) availableSilverUi = i;
                    }
                    else
                    {
                        var availProp = AccessTools.Property(dialogType, "availableSilver")
                                       ?? AccessTools.Property(dialogType, "AvailableSilver");
                        if (availProp != null &&
                            (availProp.PropertyType == typeof(float) ||
                             availProp.PropertyType == typeof(double) ||
                             availProp.PropertyType == typeof(int)))
                        {
                            var v = availProp.GetValue(dialog, null);
                            if (v is float f) availableSilverUi = f;
                            else if (v is double d) availableSilverUi = (float)d;
                            else if (v is int i) availableSilverUi = i;
                        }
                    }
                }
                catch
                {
                    // 读取失败保持 availableSilverUi=0，仅依赖我们自己的扣银逻辑
                }

                try
                {
                    Log.Message($"[MP-MeowOnlineShop] MeowPurchase Prefix: totalSilverCost computed from BaseMarketValue = {totalSilverCost}, availableSilverUi={availableSilverUi}.");
                }
                catch { }
                PurchaseTrace.Log($"Prefix COST shop={shopDefName} cost={totalSilverCost} mapIndex={mapIndex} availableUi={availableSilverUi}");

                // 若 Meow 认为银子不足，则与 UI 一致地直接阻止购买，既不扣银也不发货，避免“看起来没钱仍然下单”的困惑。
                if (totalSilverCost > 0 && availableSilverUi + 0.5f < totalSilverCost)
                {
                    Log.Warning($"[MP-MeowOnlineShop] MeowPurchase Prefix: insufficient silver (have={availableSilverUi}, need={totalSilverCost}), blocking purchase.");
                    PurchaseTrace.Log("Prefix EXIT blockedByInsufficientSilver");
                    return false;
                }

                bool applied;
                if (SyncApplyMeowPurchaseWithMap != null)
                {
                    try
                    {
                        // Static sync method: first arg must be target (null), not mapIndex.
                        SyncApplyMeowPurchaseWithMap.DoSync(null, mapIndex, shopDefName, defNames, counts, totalSilverCost);
                        applied = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[MP-MeowOnlineShop] MeowPurchase DoSync failed, blocking local fallback to avoid one-sided state: {e}");
                        PurchaseTrace.Log("Prefix EXIT blockedByDoSyncFailure");
                        try
                        {
                            Messages.Message("MP-MeowOnlineShop: 购买同步失败，已取消本次购买以避免联机失步。请稍后重试。", MessageTypeDefOf.RejectInput, historical: false);
                        }
                        catch
                        {
                        }
                        applied = false;
                    }
                }
                else applied = ApplyMeowPurchaseWithMap(mapIndex, shopDefName, defNames, counts, totalSilverCost);

                var win = dialog as Window;
                if (win != null && Find.WindowStack.IsOpen(win))
                    win.Close();
                if (!applied)
                {
                    PurchaseTrace.Log("Prefix EXIT applied=false (blocked)");
                    return false;
                }

                PurchaseTrace.Log("Prefix EXIT applied=true");
                return false;
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] MeowPurchase Prefix failed, blocking original to avoid desync: {e}");
                return false;
            }
        }
    }
}
