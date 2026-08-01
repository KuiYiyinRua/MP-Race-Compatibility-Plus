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
    /// <summary>调试用：联机通讯台流程日志。仅当 ModDebug.EnableCommsVerbose 为 true 时输出，默认关闭避免高频刷屏。</summary>
    internal static class CommsConsoleDebug
    {
        public const string Tag = "[MP-MeowComms.Debug]";

        public static void Log(string msg) { if (ModDebug.EnableCommsVerbose) Verse.Log.Message(Tag + " " + msg); }
        public static void Warn(string msg) { if (ModDebug.EnableCommsVerbose) Verse.Log.Warning(Tag + " " + msg); }
    }

    /// <summary>联机下包装 Meow CommsShop，CommFloatMenuOption 直接返回调用 OpenMeowCommsShop 的选项，避免 GiveUseCommsJob。</summary>
    internal sealed class CommsShopMpProxy : ICommunicable
    {
        private readonly object _inner;
        private readonly string _defName;
        private readonly string _label;
        private readonly UnityEngine.Texture2D _texture;
        private readonly UnityEngine.Color _color;

        /// <summary>创建 Proxy；defName 应由 GetMeowCommsShopDefName(innerCommsShop) 传入，避免反射取不到。</summary>
        public CommsShopMpProxy(object innerCommsShop, string defName = null)
        {
            _inner = innerCommsShop;
            var sf = AccessTools.Property(innerCommsShop?.GetType(), "shopFaction")?.GetValue(innerCommsShop);
            if (!string.IsNullOrEmpty(defName))
                _defName = defName;
            else
            {
                _defName = Patch_CommsConsole.GetMeowCommsShopDefName(innerCommsShop)
                    ?? AccessTools.Property(sf?.GetType(), "defName")?.GetValue(sf) as string
                    ?? AccessTools.Field(sf?.GetType(), "defName")?.GetValue(sf) as string ?? "";
            }
            _label = AccessTools.Method(innerCommsShop?.GetType(), "GetCallLabel")?.Invoke(innerCommsShop, null) as string ?? _defName;
            _texture = AccessTools.Property(sf?.GetType(), "Texture")?.GetValue(sf) as UnityEngine.Texture2D;
            _color = (UnityEngine.Color?)(AccessTools.Property(sf?.GetType(), "Color")?.GetValue(sf)) ?? UnityEngine.Color.white;
        }

        public string GetCallLabel() => _label;
        public string GetInfoText() => "Meow.ExpressDesc".Translate(_label.CapitalizeFirst());
        public Faction GetFaction() => null;
        public string GetUniqueLoadID() => "CommsShopMpProxy_" + _defName;

        public void TryOpenComms(Pawn negotiator) => Patch_CommsConsole.OpenMeowCommsShopLocal(negotiator, _defName);

        public FloatMenuOption CommFloatMenuOption(Building_CommsConsole console, Pawn negotiator)
        {
            var pawn = negotiator;
            var defName = _defName;
            CommsConsoleDebug.Log($"CommsShopMpProxy.CommFloatMenuOption: label={_label} defName={defName} negotiator={negotiator?.LabelShort ?? "null"} consoleMap={console?.Map?.Index ?? -1}");
            // 联机下不创建 Job（避免 ICommunicable 序列化），点击时在本地通过 LongEvent 打开界面，确保在主线程/正确上下文执行。
            return new FloatMenuOption(_label, () =>
            {
                var p = pawn;
                var d = defName;
                if (!UiActionBatcher.Allow("comms.proxy.click." + d, cooldownTicks: 2))
                    return;
                CommsConsoleDebug.Log($"CommsShopMpProxy action clicked: pawn={pawn?.LabelShort ?? "null"} defName={defName}");
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    CommsConsoleDebug.Log($"CommsShopMpProxy LongEvent: opening shop pawn={p?.LabelShort} defName={d}");
                    Patch_CommsConsole.OpenMeowCommsShopLocal(p, d);
                });
            }, _texture, _color, MenuOptionPriority.InitiateSocial);
        }
    }

    /// <summary>
    /// 联机下使用通讯台联系喵喵电商时，不创建会触发 Multiplayer 序列化的 UseCommsConsole Job，
    /// 改为通过同步方法直接打开电商界面，避免 ICommunicable 序列化导致的 InvalidCastException。
    /// </summary>
    internal static class Patch_CommsConsole
    {
        private static Type _commsShopDefType;
        private static bool _commsShopDefTypeResolved;
        private const int CommsUiWorldSeedOffset = 0x41C7;
        private const int StateStaticRand = 1;
        private const int StateMapRand = 2;
        private const int StateWorldRand = 4;
        [ThreadStatic] private static int _commsUiRandScopeDepth;
        private static readonly Func<object> WorldRandGetter = TryGetWorldRandGetter();
        private static readonly Dictionary<Type, PropertyInfo> MapRandPropertyCache = new Dictionary<Type, PropertyInfo>();
        private static readonly Dictionary<Type, FieldInfo> MapRandFieldCache = new Dictionary<Type, FieldInfo>();
        private static readonly Dictionary<Type, MethodInfo> MapRandPushMethodCache = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo> MapRandPopMethodCache = new Dictionary<Type, MethodInfo>();
        private static readonly object RandReflectionCacheLock = new object();

        /// <summary>是否为喵喵电商的 ICommunicable（通过类型名、Def 类型判断，避免强引用 Meow 程序集）。</summary>
        public static bool IsMeowCommunicable(object communicable)
        {
            if (communicable == null) return false;
            var t = communicable.GetType();
            var name = t.FullName ?? "";
            // 类型名包含 CommsShop / CommsShopDef
            if (name.IndexOf("CommsShop", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("Meow", StringComparison.OrdinalIgnoreCase) >= 0 && name.IndexOf("Comms", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // 通过 Def 是否为 CommsShopDef 判断
            var def = GetDefFromCommunicable(communicable);
            if (def != null && IsCommsShopDef(def))
                return true;
            return false;
        }

        private static bool IsCommsShopDef(Def d)
        {
            if (d == null) return false;
            if (!_commsShopDefTypeResolved)
            {
                _commsShopDefType = AccessTools.TypeByName("Meow.CommsShopDef");
                _commsShopDefTypeResolved = true;
            }
            return _commsShopDefType != null && _commsShopDefType.IsAssignableFrom(d.GetType());
        }

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

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int hash = 0;
            foreach (char c in value)
                hash = Gen.HashCombineInt(hash, c);
            return hash;
        }

        private static int DeterministicTypeHash(Type type)
        {
            if (type == null) return 0;
            return DeterministicStringHash(type.FullName ?? type.Name ?? "");
        }

        private static int BuildStableContextKey(object context)
        {
            if (context == null) return 0;
            try
            {
                if (context is ILoadReferenceable lr)
                    return DeterministicStringHash(lr.GetUniqueLoadID());
            }
            catch { }
            try
            {
                var t = context.GetType();
                foreach (var name in new[] { "thingIDNumber", "ID", "Tile", "tile", "uniqueID" })
                {
                    var pi = AccessTools.Property(t, name);
                    if (pi != null && pi.PropertyType == typeof(int))
                        return (int)pi.GetValue(context);
                    var fi = AccessTools.Field(t, name);
                    if (fi != null && fi.FieldType == typeof(int))
                        return (int)fi.GetValue(context);
                }
            }
            catch { }
            return DeterministicTypeHash(context.GetType());
        }

        private static bool PushMapRand(Map map, int seed)
        {
            if (map == null) return false;
            try
            {
                object mapRand;
                if (!TryGetMapRandInstance(map, out mapRand) || mapRand == null)
                    return false;

                MethodInfo push;
                var randType = mapRand.GetType();
                lock (RandReflectionCacheLock)
                {
                    if (!MapRandPushMethodCache.TryGetValue(randType, out push))
                    {
                        push = randType.GetMethod("PushState", new[] { typeof(int) });
                        MapRandPushMethodCache[randType] = push;
                    }
                }

                if (mapRand == null) return false;
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
                object mapRand;
                if (!TryGetMapRandInstance(map, out mapRand) || mapRand == null)
                    return;

                MethodInfo pop;
                var randType = mapRand.GetType();
                lock (RandReflectionCacheLock)
                {
                    if (!MapRandPopMethodCache.TryGetValue(randType, out pop))
                    {
                        pop = randType.GetMethod("PopState", Type.EmptyTypes);
                        MapRandPopMethodCache[randType] = pop;
                    }
                }

                if (mapRand == null) return;
                pop?.Invoke(mapRand, null);
            }
            catch { }
        }

        private static bool TryGetMapRandInstance(Map map, out object mapRand)
        {
            mapRand = null;
            if (map == null)
                return false;

            var type = map.GetType();
            PropertyInfo prop;
            FieldInfo field;
            lock (RandReflectionCacheLock)
            {
                if (!MapRandPropertyCache.TryGetValue(type, out prop))
                {
                    prop = AccessTools.Property(type, "Rand") ?? AccessTools.Property(type, "rand");
                    OptimizationCacheUtility.EnsureBound(MapRandPropertyCache, 128);
                    MapRandPropertyCache[type] = prop;
                }
                if (!MapRandFieldCache.TryGetValue(type, out field))
                {
                    field = AccessTools.Field(type, "Rand") ?? AccessTools.Field(type, "rand");
                    OptimizationCacheUtility.EnsureBound(MapRandFieldCache, 128);
                    MapRandFieldCache[type] = field;
                }
            }

            try
            {
                if (prop != null)
                    mapRand = prop.GetValue(map);
                if (mapRand == null && field != null)
                    mapRand = field.GetValue(map);
            }
            catch
            {
                mapRand = null;
            }

            return mapRand != null;
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

        private static int BuildCommsUiSeed(object context, string scopeTag, Map map)
        {
            int seed = Gen.HashCombineInt(DeterministicTypeHash(context?.GetType()), DeterministicStringHash(scopeTag ?? ""));
            seed = Gen.HashCombineInt(seed, BuildStableContextKey(context));
            if (map != null)
                seed = Gen.HashCombineInt(seed, map.Index);
            return seed;
        }

        public static bool BeginCommsUiRandScope(object context, string scopeTag, Map map, out int state, out Map mapForPop)
        {
            state = 0;
            mapForPop = null;
            if (!MP.IsInMultiplayer || map == null)
                return false;

            _commsUiRandScopeDepth++;
            if (_commsUiRandScopeDepth > 1)
            {
                if (ModDebug.EnableCommsRandTrace)
                    CommsConsoleDebug.Log($"RandScope nested skip: scope={scopeTag} depth={_commsUiRandScopeDepth}");
                return false;
            }

            int seed = BuildCommsUiSeed(context, scopeTag, map);
            try
            {
                DeterministicRandScope.Begin(map, seed, CommsUiWorldSeedOffset, ref state, out mapForPop);

                if (ModDebug.EnableCommsRandTrace)
                    CommsConsoleDebug.Log($"RandScope push: scope={scopeTag} map={map.Index} state=0x{state:X} seed={seed}");
                return true;
            }
            catch (Exception e)
            {
                DeterministicRandScope.End(state, mapForPop);
                state = 0;
                if (ModDebug.EnableCommsRandTrace)
                    CommsConsoleDebug.Warn($"RandScope push failed: scope={scopeTag} err={e.Message}");
                _commsUiRandScopeDepth = 0;
                return false;
            }
        }

        public static void EndCommsUiRandScope(string scopeTag, bool ownsScope, int state, Map mapForPop)
        {
            if (!MP.IsInMultiplayer)
                return;
            if (!ownsScope)
            {
                if (_commsUiRandScopeDepth > 0)
                    _commsUiRandScopeDepth--;
                return;
            }
            try
            {
                DeterministicRandScope.End(state, mapForPop);
                if (ModDebug.EnableCommsRandTrace)
                    CommsConsoleDebug.Log($"RandScope pop: scope={scopeTag} state=0x{state:X}");
            }
            catch (Exception e)
            {
                if (ModDebug.EnableCommsRandTrace)
                    CommsConsoleDebug.Warn($"RandScope pop failed: scope={scopeTag} err={e.Message}");
            }
            finally
            {
                _commsUiRandScopeDepth = 0;
            }
        }

        private static Def GetDefFromCommunicable(object communicable)
        {
            if (communicable == null) return null;
            if (communicable is Def def)
                return def;
            var t = communicable.GetType();
            // Meow.CommsShop 用 shopFaction (CommsShopDef)，不是 def
            var shopFactionProp = AccessTools.Property(t, "shopFaction");
            if (shopFactionProp != null)
            {
                var d = shopFactionProp.GetValue(communicable) as Def;
                if (d != null) return d;
            }
            var shopFactionField = AccessTools.Field(t, "shopFaction");
            if (shopFactionField != null)
            {
                var d = shopFactionField.GetValue(communicable) as Def;
                if (d != null) return d;
            }
            var defProp = AccessTools.Property(t, "def") ?? AccessTools.Property(t, "Def");
            var d2 = defProp?.GetValue(communicable) as Def;
            if (d2 != null) return d2;
            var defField = AccessTools.Field(t, "def") ?? AccessTools.Field(t, "Def");
            return defField?.GetValue(communicable) as Def;
        }

        /// <summary>从 ICommunicable 获取 CommsShopDef 的 defName（用于跨端同步）。def 为 null 时从 shopFaction 反射取 defName。</summary>
        public static string GetMeowCommsShopDefName(object communicable)
        {
            var def = GetDefFromCommunicable(communicable);
            if (def != null)
                return def.defName;
            var t = communicable?.GetType();
            var sf = AccessTools.Property(t, "shopFaction")?.GetValue(communicable) ?? AccessTools.Field(t, "shopFaction")?.GetValue(communicable);
            if (sf == null) return null;
            var tn = sf.GetType();
            return AccessTools.Property(tn, "defName")?.GetValue(sf) as string ?? AccessTools.Field(tn, "defName")?.GetValue(sf) as string;
        }

        /// <summary>本地打开喵喵电商界面（不经过 MP 同步，确保点击时窗口在本地打开）。</summary>
        public static void OpenMeowCommsShopLocal(Pawn pawn, string commShopDefName)
        {
            CommsConsoleDebug.Log($"OpenMeowCommsShopLocal ENTER: pawn={pawn?.LabelShort ?? "null"} defName={commShopDefName ?? "null"} CurrentMap={Find.CurrentMap?.Index ?? -1}");
            if (string.IsNullOrEmpty(commShopDefName))
            {
                CommsConsoleDebug.Warn("OpenMeowCommsShopLocal: empty defName, abort.");
                return;
            }
            // 联机下 Gizmo/右键可能在没有选中小人时显示，此时尝试用当前地图任意殖民者
            if (pawn == null || pawn.Map == null)
            {
                var map = Find.CurrentMap;
                if (map == null)
                {
                    CommsConsoleDebug.Warn("OpenMeowCommsShopLocal: No current map, cannot open shop.");
                    return;
                }
                pawn = map.mapPawns.FreeColonists.FirstOrFallback();
                if (pawn == null)
                {
                    CommsConsoleDebug.Warn("OpenMeowCommsShopLocal: No negotiator pawn, cannot open shop.");
                    return;
                }
                CommsConsoleDebug.Log($"OpenMeowCommsShopLocal: resolved pawn to {pawn.LabelShort}");
            }
            if (pawn.Map == null)
            {
                CommsConsoleDebug.Warn("OpenMeowCommsShopLocal: Pawn has no Map, cannot open shop.");
                return;
            }
            try
            {
                var defType = AccessTools.TypeByName("Meow.CommsShopDef");
                if (defType == null)
                {
                    CommsConsoleDebug.Warn("OpenMeowCommsShopLocal: Meow.CommsShopDef type not found.");
                    return;
                }
                var dbType = typeof(DefDatabase<>).MakeGenericType(defType);
                var getNamed = AccessTools.Method(dbType, "GetNamed", new[] { typeof(string), typeof(bool) });
                if (getNamed == null)
                {
                    CommsConsoleDebug.Warn("OpenMeowCommsShopLocal: GetNamed not found.");
                    return;
                }
                var def = getNamed.Invoke(null, new object[] { commShopDefName, false });
                if (def == null)
                {
                    CommsConsoleDebug.Warn($"OpenMeowCommsShopLocal: CommsShopDef '{commShopDefName}' not found.");
                    return;
                }
                CommsConsoleDebug.Log($"OpenMeowCommsShopLocal: calling OpenMeowCommsWindowFromDef pawn={pawn.LabelShort}");
                // CommsShopDef 非 ICommunicable，需创建 CommsShop(label, def) 并调用 TryOpenComms
                OpenMeowCommsWindowFromDef(pawn, def);
                CommsConsoleDebug.Log("OpenMeowCommsShopLocal: OpenMeowCommsWindowFromDef returned.");
            }
            catch (Exception e)
            {
                Log.Error($"[MP-MeowOnlineShop] CommsConsole OpenMeowCommsShopLocal failed: {e}");
            }
        }

        /// <summary>联机已注册的同步方法入口：与本地打开相同，仅在有显式同步回放时由 MP 调用；UI 点击请始终用 <see cref="OpenMeowCommsShopLocal"/>，避免所有客户端同时弹窗。</summary>
        public static void OpenMeowCommsShop(Pawn pawn, string commShopDefName)
        {
            OpenMeowCommsShopLocal(pawn, commShopDefName);
        }

        /// <summary>从 CommsShopDef 创建 CommsShop 并打开 Dialog_OnlineShop（Meow 源码：CommsShop.TryOpenComms）。</summary>
        private static void OpenMeowCommsWindowFromDef(Pawn pawn, object commShopDef)
        {
            CommsConsoleDebug.Log($"OpenMeowCommsWindowFromDef ENTER: pawn={pawn?.LabelShort ?? "null"} def={commShopDef?.GetType().Name ?? "null"}");
            if (pawn == null || commShopDef == null) return;
            try
            {
                var defType = commShopDef.GetType();
                var label = AccessTools.Property(defType, "label")?.GetValue(commShopDef) as string ?? (commShopDef as Def)?.label ?? "";
                var commShopType = AccessTools.TypeByName("Meow.CommsShop");
                if (commShopType == null)
                {
                    CommsConsoleDebug.Warn("OpenMeowCommsWindowFromDef: Meow.CommsShop type not found.");
                    return;
                }
                var ctor = AccessTools.Constructor(commShopType, new[] { typeof(string), defType });
                if (ctor == null)
                {
                    CommsConsoleDebug.Warn("OpenMeowCommsWindowFromDef: CommsShop constructor not found.");
                    return;
                }
                var commShop = ctor.Invoke(new object[] { label, commShopDef });
                var tryOpen = AccessTools.Method(commShopType, "TryOpenComms", new[] { typeof(Pawn) });
                if (tryOpen != null)
                {
                    CommsConsoleDebug.Log("OpenMeowCommsWindowFromDef: invoking TryOpenComms(pawn).");
                    tryOpen.Invoke(commShop, new object[] { pawn });
                    CommsConsoleDebug.Log("OpenMeowCommsWindowFromDef: TryOpenComms returned.");
                    return;
                }
                // 备用：直接 new Dialog_OnlineShop(pawn, commShop)
                CommsConsoleDebug.Log("OpenMeowCommsWindowFromDef: TryOpenComms not found, trying Dialog_OnlineShop.");
                var dialogType = AccessTools.TypeByName("Meow.Dialog_OnlineShop");
                if (dialogType != null)
                {
                    var dialogCtor = AccessTools.Constructor(dialogType, new[] { typeof(Thing), commShopType });
                    if (dialogCtor != null)
                    {
                        var window = dialogCtor.Invoke(new object[] { pawn, commShop }) as Window;
                        if (window != null)
                        {
                            Find.WindowStack.Add(window);
                            CommsConsoleDebug.Log("OpenMeowCommsWindowFromDef: Dialog_OnlineShop added to WindowStack.");
                            return;
                        }
                    }
                }
                CommsConsoleDebug.Warn("OpenMeowCommsWindowFromDef: Could not open Meow shop window.");
            }
            catch (Exception e)
            {
                Log.Error($"[MP-MeowOnlineShop] CommsConsole OpenMeowCommsWindowFromDef: {e}");
            }
        }
    }

    /// <summary>Prefix + Finalizer：在 TryTakeOrderedJob 层面拦截 UseCommsConsole Job；Finalizer 兜底捕获 MakeDriver NullRef。</summary>
    internal static class Patch_CommsConsole_TryTakeOrderedJob
    {
        private static string _useCommsConsoleDefName;

        private static bool IsUseCommsConsoleJob(Job job)
        {
            if (job?.def == null) return false;
            if (_useCommsConsoleDefName == null)
            {
                try
                {
                    var jd = DefDatabase<JobDef>.GetNamed("UseCommsConsole", false);
                    _useCommsConsoleDefName = jd?.defName ?? "UseCommsConsole";
                }
                catch
                {
                    _useCommsConsoleDefName = "UseCommsConsole";
                }
            }
            return string.Equals(job.def.defName, _useCommsConsoleDefName, StringComparison.OrdinalIgnoreCase);
        }

        private static ICommunicable GetCommTargetFromJob(Job job)
        {
            if (job == null) return null;
            var t = job.GetType();
            foreach (var name in new[] { "commTarget", "CommTarget", "targetComm" })
            {
                var f = AccessTools.Field(t, name);
                if (f != null)
                {
                    try
                    {
                        var v = f.GetValue(job) as ICommunicable;
                        if (v != null) return v;
                    }
                    catch { }
                }
            }
            return null;
        }

        private static Pawn GetPawnFromTracker(Pawn_JobTracker tracker)
        {
            if (tracker == null) return null;
            var p = AccessTools.Property(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn;
            return p ?? AccessTools.Field(typeof(Pawn_JobTracker), "pawn")?.GetValue(tracker) as Pawn;
        }

        public static bool Prefix(Job job, Pawn_JobTracker __instance, ref bool __result)
        {
            if (!MP.enabled || !MP.IsInMultiplayer)
                return true;
            var pawn = GetPawnFromTracker(__instance);
            if (job == null || pawn == null)
                return true;
            if (!IsUseCommsConsoleJob(job))
                return true;

            var comm = GetCommTargetFromJob(job);
            if (comm != null && Patch_CommsConsole.IsMeowCommunicable(comm))
            {
                string defName = Patch_CommsConsole.GetMeowCommsShopDefName(comm);
                if (!string.IsNullOrEmpty(defName))
                    Patch_CommsConsole.OpenMeowCommsShopLocal(pawn, defName);
                __result = false;
                return false;
            }
            if (comm == null)
            {
                Log.Warning("[MP-MeowOnlineShop] CommsConsole: UseCommsConsole job has null commTarget (likely Meow desync), rejecting.");
                __result = false;
                return false;
            }
            return true;
        }

        public static Exception Finalizer(Job job, Exception __exception)
        {
            if (__exception is NullReferenceException && job != null && IsUseCommsConsoleJob(job))
            {
                Log.Warning("[MP-MeowOnlineShop] CommsConsole: Suppressed NullRef in UseCommsConsole job (Meow desync).");
                return null;
            }
            return __exception;
        }
    }

    /// <summary>Prefix：联机下在 CommFloatMenuOption 层面直接替换为调用 OpenMeowCommsShop，从源头避免 GiveUseCommsJob。</summary>
    internal static class Patch_CommsConsole_CommFloatMenuOption
    {
        public static bool Prefix(object __instance, Building_CommsConsole console, Pawn negotiator, ref FloatMenuOption __result)
        {
            var mpOk = MP.enabled && MP.IsInMultiplayer;
            CommsConsoleDebug.Log($"CommFloatMenuOption Prefix: __instance={__instance?.GetType().FullName ?? "null"} negotiator={negotiator?.LabelShort ?? "null"} MP.enabled={MP.enabled} IsInMultiplayer={MP.IsInMultiplayer}");
            if (!mpOk) return true;
            var commShop = __instance;
            if (commShop == null || negotiator == null)
            {
                CommsConsoleDebug.Log("CommFloatMenuOption: skip (commShop or negotiator null), run original.");
                return true;
            }
            var t = commShop.GetType();
            if (t.FullName?.IndexOf("CommsShop", StringComparison.OrdinalIgnoreCase) < 0)
            {
                CommsConsoleDebug.Log($"CommFloatMenuOption: skip (not CommsShop type {t.FullName}), run original.");
                return true;
            }
            var shopFaction = AccessTools.Property(t, "shopFaction")?.GetValue(commShop);
            if (shopFaction == null)
            {
                CommsConsoleDebug.Log("CommFloatMenuOption: skip (shopFaction null), run original.");
                return true;
            }
            var defName = (shopFaction as Def)?.defName ?? AccessTools.Property(shopFaction.GetType(), "defName")?.GetValue(shopFaction) as string;
            if (string.IsNullOrEmpty(defName))
            {
                CommsConsoleDebug.Log("CommFloatMenuOption: skip (defName empty), run original.");
                return true;
            }
            int randState;
            Map mapForRandPop;
            bool ownsRandScope = Patch_CommsConsole.BeginCommsUiRandScope(commShop, "CommsShop.CommFloatMenuOption", console?.Map ?? negotiator?.Map, out randState, out mapForRandPop);
            try
            {
                var getCallLabel = AccessTools.Method(t, "GetCallLabel");
                var label = getCallLabel?.Invoke(commShop, null) as string ?? defName;
                var texture = AccessTools.Property(shopFaction.GetType(), "Texture")?.GetValue(shopFaction) as UnityEngine.Texture2D;
                var color = (UnityEngine.Color?)(AccessTools.Property(shopFaction.GetType(), "Color")?.GetValue(shopFaction)) ?? UnityEngine.Color.white;
                var pawn = negotiator;
                CommsConsoleDebug.Log($"CommFloatMenuOption: REPLACING with our option label={label} defName={defName}");
                // 联机下必须在点击端本地打开窗口，通过 LongEvent 确保在主线程/正确上下文执行。
                var p = pawn;
                var d = defName;
                __result = new FloatMenuOption(label, () =>
                {
                    if (!UiActionBatcher.Allow("comms.floatmenu.click." + d, cooldownTicks: 2))
                        return;
                    CommsConsoleDebug.Log($"CommFloatMenuOption action clicked: pawn={p?.LabelShort ?? "null"} defName={d}");
                    LongEventHandler.ExecuteWhenFinished(() =>
                    {
                        Patch_CommsConsole.OpenMeowCommsShopLocal(p, d);
                    });
                }, texture, color, MenuOptionPriority.InitiateSocial);
                return false;
            }
            finally
            {
                Patch_CommsConsole.EndCommsUiRandScope("CommsShop.CommFloatMenuOption", ownsRandScope, randState, mapForRandPop);
            }
        }
    }

    /// <summary>Postfix：联机下为通讯台 Gizmos 添加喵喵电商按钮。当 Def Patch 未生效（无 CompMeowCommsConsole）时作为备用。
    /// 联机下若仅调用了 Pawn 的 GetGizmos（例如选中顺序或 MP 只传 Pawn），则当选中集中包含通讯台时也注入按钮。</summary>
    internal static class Patch_CommsConsole_GetGizmos
    {
        // 仅当 __instance 为通讯台时打一次“已添加 gizmo”日志，避免 Pawn 每帧 GetGizmos 刷屏 99+ 次。
        private static int _getGizmosAddLogCount;

        public static void Postfix(Thing __instance, ref IEnumerable<Gizmo> __result)
        {
            var isComms = __instance is Building_CommsConsole;
            // 不对 Pawn 打任何日志（每帧调用会导致高频刷屏）；仅通讯台路径在需要时打一次。
            if (!MP.enabled || !MP.IsInMultiplayer)
                return;

            Building_CommsConsole console = null;
            if (isComms)
            {
                console = (Building_CommsConsole)__instance;
                var hasComp = console.GetComp<CompMeowCommsConsole>() != null;
                if (hasComp)
                    return;
            }
            else if (__instance is Pawn)
            {
                // 联机下有时只对 Pawn 调用 GetGizmos，选中通讯台时底部栏仍显示 Pawn 的 Gizmo。若选中集中有通讯台，则也注入喵喵按钮。
                var sel = Find.Selector?.SelectedObjects;
                if (sel != null)
                {
                    foreach (var o in sel)
                    {
                        if (o is Building_CommsConsole c && c.Map != null)
                        {
                            console = c;
                            break;
                        }
                    }
                }
                if (console == null)
                    return;
            }
            else
                return;

            if (console.Map == null)
                return;
            var map = console.Map;
            var pawn = map.mapPawns.FreeColonists.FirstOrFallback();
            int randState;
            Map mapForRandPop;
            bool ownsRandScope = Patch_CommsConsole.BeginCommsUiRandScope(console, "CommsConsole.GetGizmos", map, out randState, out mapForRandPop);
            try
            {
                var defType = AccessTools.TypeByName("Meow.CommsShopDef");
                if (defType == null)
                    return;
                var dbType = typeof(DefDatabase<>).MakeGenericType(defType);
                var allDefs = AccessTools.Method(dbType, "AllDefs");
                if (allDefs == null) return;
                var defs = allDefs.Invoke(null, null) as System.Collections.IEnumerable;
                if (defs == null) return;

                var list = __result?.ToList() ?? new List<Gizmo>();
                var defCount = 0;
                foreach (var def in defs)
                {
                    if (def == null) continue;
                    var defName = (def as Def)?.defName ?? AccessTools.Property(def.GetType(), "defName")?.GetValue(def) as string;
                    if (string.IsNullOrEmpty(defName)) continue;
                    defCount++;
                    var label = AccessTools.Property(def.GetType(), "label")?.GetValue(def) as string ?? defName;
                    string displayLabel = (def as Def)?.LabelCap ?? label;
                    var commShopType = AccessTools.TypeByName("Meow.CommsShop");
                    if (commShopType != null)
                    {
                        var ctor = AccessTools.Constructor(commShopType, new[] { typeof(string), defType });
                        var getCallLabel = AccessTools.Method(commShopType, "GetCallLabel");
                        if (ctor != null && getCallLabel != null)
                        {
                            var temp = ctor.Invoke(new object[] { label, def });
                            displayLabel = getCallLabel.Invoke(temp, null) as string ?? displayLabel;
                        }
                    }
                    var texture = AccessTools.Property(def.GetType(), "Texture")?.GetValue(def) as UnityEngine.Texture2D;
                    var p = pawn;
                    var d = defName;
                    list.Add(new Command_Action
                    {
                        defaultLabel = displayLabel,
                        defaultDesc = "Meow.ExpressDesc".Translate(displayLabel.CapitalizeFirst()),
                        icon = texture,
                        action = () =>
                        {
                            if (!UiActionBatcher.Allow("comms.gizmo.click." + d, cooldownTicks: 2))
                                return;
                            CommsConsoleDebug.Log($"GetGizmos Command_Action clicked: pawn={p?.LabelShort ?? "null"} defName={d}");
                            LongEventHandler.ExecuteWhenFinished(() => Patch_CommsConsole.OpenMeowCommsShopLocal(p, d));
                        }
                    });
                }
                if (list.Count > 0)
                {
                    if (ModDebug.EnableCommsVerbose && isComms && System.Threading.Interlocked.Increment(ref _getGizmosAddLogCount) <= 2)
                        CommsConsoleDebug.Log($"GetGizmos Postfix: added {list.Count} gizmos for CommsConsole.");
                    __result = list;
                }
            }
            finally
            {
                Patch_CommsConsole.EndCommsUiRandScope("CommsConsole.GetGizmos", ownsRandScope, randState, mapForRandPop);
            }
        }
    }

    /// <summary>Postfix：联机下将 GetCommTargets 中的 Meow CommsShop 替换为 CommsShopMpProxy，从源头避免 GiveUseCommsJob，同时确保选项可见。</summary>
    internal static class Patch_CommsConsole_GetCommTargets
    {
        public static IEnumerable<ICommunicable> Postfix(IEnumerable<ICommunicable> __result)
        {
            if (!MP.enabled || !MP.IsInMultiplayer)
            {
                foreach (var c in __result) yield return c;
                yield break;
            }
            var list = __result?.ToList() ?? new List<ICommunicable>();
            var replaced = 0;
            foreach (var c in list)
            {
                if (c != null && Patch_CommsConsole.IsMeowCommunicable(c))
                {
                    replaced++;
                    var defName = Patch_CommsConsole.GetMeowCommsShopDefName(c);
                    yield return new CommsShopMpProxy(c, defName);
                }
                else
                    yield return c;
            }
            if (replaced > 0)
                CommsConsoleDebug.Log($"GetCommTargets Postfix: total={list.Count} replaced Meow with Proxy={replaced}");
        }
    }

    /// <summary>Prefix：联机下若为喵喵电商通讯，不创建 Job，改为调用同步方法打开界面（兜底）。</summary>
    internal static class Patch_CommsConsole_GiveUseCommsJob
    {
        public static bool Prefix(Pawn negotiator, ICommunicable target)
        {
            if (!MP.enabled || !MP.IsInMultiplayer)
                return true;
            if (target == null)
                return true;
            if (!Patch_CommsConsole.IsMeowCommunicable(target))
                return true;

            CommsConsoleDebug.Log($"GiveUseCommsJob Prefix: INTERCEPT Meow comm pawn={negotiator?.LabelShort ?? "null"} commType={target.GetType().FullName}");
            // 关键：一律跳过原始 GiveUseCommsJob，避免创建会同步的 Job（客户端 MakeDriver 会 NullRef）。
            // 即使无法打开窗口，也不能 return true，否则会触发 TryTakeOrderedJob 同步崩溃。
            string defName = Patch_CommsConsole.GetMeowCommsShopDefName(target);
            if (!string.IsNullOrEmpty(defName))
            {
                Patch_CommsConsole.OpenMeowCommsShopLocal(negotiator, defName);
            }
            else
            {
                CommsConsoleDebug.Warn("GiveUseCommsJob: Could not get defName for Meow communicable, skipping to avoid sync crash.");
            }
            return false;
        }
    }
}
