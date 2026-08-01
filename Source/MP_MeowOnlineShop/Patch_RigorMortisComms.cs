using System;
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
    /// 联机下仅接管 RigorMortis.ZombieTraderComms 的交易动作同步；
    /// 通讯台 UI 由原版对话流程接管，交易效果通过 <see cref="SyncRigorTraderTrade"/> 同步执行。
    /// </summary>
    internal static class Patch_RigorMortisComms
    {
        private const int OpenWindowCooldownTicks = 20;
        private const int RigorTradeWorldSeedOffset = 0x4D91;
        private static readonly Type ZombieTraderCommsType = AccessTools.TypeByName("RigorMortis.ZombieTraderComms");
        private static readonly Type RMUtilityType = AccessTools.TypeByName("RigorMortis.RMUtility");
        private static readonly Type RMDefOfType = AccessTools.TypeByName("RigorMortis.RMDefOf");
        private static readonly Type AxolotlFactionDefOfType = AccessTools.TypeByName("Axolotl.AxolotlFactionDefOf");
        private static readonly Type MultiplayerRuntimeType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
        private static readonly FieldInfo MultiplayerExecutingCmdsField = MultiplayerRuntimeType != null
            ? AccessTools.Field(MultiplayerRuntimeType, "ExecutingCmds")
            : null;
        private static readonly PropertyInfo MultiplayerExecutingCmdsProperty = MultiplayerRuntimeType != null
            ? AccessTools.Property(MultiplayerRuntimeType, "ExecutingCmds")
            : null;
        private static readonly Dictionary<int, int> LastOpenTickByNegotiator = new Dictionary<int, int>();
        private static bool _startupSummaryLogged;

        /// <summary>由启动代码注册；交易按钮调用 <see cref="DoSync"/>。</summary>
        internal static ISyncMethod SyncRigorTraderTradeMethod;

        private static IEnumerable<Type> EnumerateCommsCandidates()
        {
            var yielded = new HashSet<Type>();
            if (ZombieTraderCommsType != null)
            {
                yielded.Add(ZombieTraderCommsType);
                yield return ZombieTraderCommsType;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name ?? "";
                if (asmName.IndexOf("RigorMortis", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsInterface || t.IsAbstract || yielded.Contains(t))
                        continue;

                    var fullName = t.FullName ?? t.Name ?? "";
                    bool looksLikeZombieTrader = fullName.IndexOf("ZombieTraderComms", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!looksLikeZombieTrader)
                        continue;

                    yielded.Add(t);
                    yield return t;
                }
            }
        }

        private static bool TryResolveCommFloatMenuOption(out MethodInfo method, out bool usedFallback)
        {
            usedFallback = false;
            method = null;

            if (ZombieTraderCommsType != null)
            {
                method = AccessTools.Method(ZombieTraderCommsType, "CommFloatMenuOption",
                    new[] { typeof(Building_CommsConsole), typeof(Pawn) });
                if (method != null)
                    return true;
            }

            foreach (var t in EnumerateCommsCandidates())
            {
                var m = AccessTools.Method(t, "CommFloatMenuOption", new[] { typeof(Building_CommsConsole), typeof(Pawn) });
                if (m != null && m.DeclaringType == t)
                {
                    method = m;
                    usedFallback = true;
                    return true;
                }
            }
            return false;
        }

        private static bool TryResolveTryOpenComms(out MethodInfo method, out bool usedFallback)
        {
            usedFallback = false;
            method = null;

            if (ZombieTraderCommsType != null)
            {
                method = AccessTools.Method(ZombieTraderCommsType, "TryOpenComms", new[] { typeof(Pawn) });
                if (method != null)
                    return true;
            }

            foreach (var t in EnumerateCommsCandidates())
            {
                var m = AccessTools.Method(t, "TryOpenComms", new[] { typeof(Pawn) });
                if (m != null && m.DeclaringType == t)
                {
                    method = m;
                    usedFallback = true;
                    return true;
                }
            }
            return false;
        }

        private static bool TryResolveGetDiaOptions(out MethodInfo method, out bool usedFallback)
        {
            usedFallback = false;
            method = null;

            if (ZombieTraderCommsType != null)
            {
                method = AccessTools.Method(ZombieTraderCommsType, "GetDiaOptions",
                    new[] { typeof(Pawn), typeof(Faction), typeof(bool) });
                if (method != null)
                    return true;
            }

            foreach (var t in EnumerateCommsCandidates())
            {
                var m = AccessTools.Method(t, "GetDiaOptions", new[] { typeof(Pawn), typeof(Faction), typeof(bool) });
                if (m != null && m.DeclaringType == t)
                {
                    method = m;
                    usedFallback = true;
                    return true;
                }
            }
            return false;
        }

        private static object GetRMComponent()
        {
            try
            {
                var prop = AccessTools.Property(RMUtilityType, "TheComponent");
                return prop?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static Pawn GetNpcMerchant(object rmComponent)
        {
            if (rmComponent == null) return null;
            try
            {
                var dict = AccessTools.Field(rmComponent.GetType(), "NPCDict")?.GetValue(rmComponent)
                    as Dictionary<string, Pawn>;
                if (dict != null && dict.TryGetValue("merchant", out var p))
                    return p;
            }
            catch { }
            return null;
        }

        private static Pawn GetNpcTaoist(object rmComponent)
        {
            if (rmComponent == null) return null;
            try
            {
                var dict = AccessTools.Field(rmComponent.GetType(), "NPCDict")?.GetValue(rmComponent)
                    as Dictionary<string, Pawn>;
                if (dict != null && dict.TryGetValue("taoist", out var p))
                    return p;
            }
            catch { }
            return null;
        }

        /// <summary>RigorMortis.RMDefOf.Axolotl_Orbital_ZombieGoods（轨道贸易商定义）。</summary>
        private static object GetAxolotlOrbitalZombieGoodsDef()
        {
            if (RMDefOfType == null) return null;
            try
            {
                var f = AccessTools.Field(RMDefOfType, "Axolotl_Orbital_ZombieGoods");
                return f?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static TradeShip CreateTradeShip(object tradeShipDef)
        {
            if (tradeShipDef == null) return null;
            try
            {
                var ctor = AccessTools.Constructor(typeof(TradeShip), new[] { tradeShipDef.GetType() });
                if (ctor != null)
                    return ctor.Invoke(new[] { tradeShipDef }) as TradeShip;
                return Activator.CreateInstance(typeof(TradeShip), tradeShipDef) as TradeShip;
            }
            catch
            {
                return null;
            }
        }

        private static Faction GetAxolotlWanderingDynastyFaction()
        {
            if (AxolotlFactionDefOfType == null) return null;
            try
            {
                var f = AccessTools.Field(AxolotlFactionDefOfType, "AxolotlWanderingDynasty");
                var def = f?.GetValue(null) as FactionDef;
                if (def == null) return null;
                return Find.FactionManager.FirstFactionOfDef(def);
            }
            catch
            {
                return null;
            }
        }

        private static Pawn FindPawnByThingId(int thingId)
        {
            if (thingId <= 0) return null;
            try
            {
                foreach (var map in Find.Maps)
                {
                    if (map == null) continue;
                    foreach (var p in map.mapPawns.AllPawnsSpawned)
                    {
                        if (p != null && p.thingIDNumber == thingId)
                            return p;
                    }
                    foreach (var p in map.mapPawns.AllPawns)
                    {
                        if (p != null && p.thingIDNumber == thingId)
                            return p;
                    }
                }
            }
            catch { }
            return null;
        }

        private static int DeterministicStringHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;
            int hash = 0;
            foreach (char c in value)
                hash = Gen.HashCombineInt(hash, c);
            return hash;
        }

        private static int BuildRigorTradeSeed(int mapIndex, int negotiatorThingId, string tradeDefName)
        {
            int seed = Gen.HashCombineInt(mapIndex, negotiatorThingId);
            return Gen.HashCombineInt(seed, DeterministicStringHash(tradeDefName));
        }

        private static string TryGetDefName(object defObj)
        {
            if (defObj == null)
                return null;
            try
            {
                var t = defObj.GetType();
                var name = AccessTools.Property(t, "defName")?.GetValue(defObj) as string
                    ?? AccessTools.Field(t, "defName")?.GetValue(defObj) as string;
                return string.IsNullOrEmpty(name) ? t.FullName : name;
            }
            catch
            {
                return defObj.GetType().FullName;
            }
        }

        /// <summary>联机同步：复刻 ZombieTraderComms 交易分支（飞船、来信、冷却、关系）。</summary>
        public static void SyncRigorTraderTrade(int mapIndex, int negotiatorThingId)
        {
            if (!MP.IsInMultiplayer)
                return;

            Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.SyncRigorTraderTrade called: mapIndex={mapIndex}, negotiatorThingId={negotiatorThingId}");
            Map map = mapIndex >= 0 ? Find.Maps.FirstOrDefault(m => m != null && m.Index == mapIndex) : null;
            if (map == null)
            {
                map = Find.Maps != null && Find.Maps.Count == 1 ? Find.Maps[0] : null;
                Log.Warning(
                    "[MP-MeowOnlineShop] RigorMortisComms: mapIndex not found; " +
                    (map != null
                        ? "using the only loaded map."
                        : "multi-map session cannot choose a deterministic target, aborting."));
            }
            if (map == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: no map resolved, abort synced trade.");
                return;
            }

            var negotiator = FindPawnByThingId(negotiatorThingId);
            if (negotiator == null)
            {
                // 不再因 negotiator 丢失直接 return，避免“某端找到/某端找不到”导致单端生成商船。
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: negotiator not found, continue with mapIndex-only execution.");
            }
            if (negotiator != null && negotiator.Map == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: negotiator map missing, fallback to mapIndex/current map.");
                negotiator = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p != null && p.thingIDNumber == negotiatorThingId) ?? negotiator;
            }

            var rm = GetRMComponent();
            if (rm == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: RMUtility.TheComponent null, continuing without cooldown/relationship updates.");
            }

            var taoist = rm != null ? GetNpcTaoist(rm) : null;
            if (negotiator != null && taoist != null && negotiator == taoist)
            {
                Log.Message("[MP-MeowOnlineShop] RigorMortisComms: negotiator is taoist, trade skipped.");
                return;
            }

            if (GetAxolotlWanderingDynastyFaction() == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: AxolotlWanderingDynasty faction missing, continuing trade to keep clients consistent.");
            }

            int tradeCooldown = 0;
            bool cooldownLoaded = false;
            try
            {
                var tc = rm != null ? AccessTools.Field(rm.GetType(), "tradeCooldown") : null;
                if (tc != null)
                {
                    tradeCooldown = (int)tc.GetValue(rm);
                    cooldownLoaded = true;
                }
            }
            catch { }

            if (cooldownLoaded && tradeCooldown >= Find.TickManager.TicksGame)
            {
                // 冷却在按钮可点击时已在 UI 层校验；同步执行阶段不再用冷却直接中止，避免跨端读值差异造成分叉。
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: cooldown says blocked, but continue execution to keep multiplayer determinism.");
            }

            var tradeDef = GetAxolotlOrbitalZombieGoodsDef();
            if (tradeDef == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: Axolotl_Orbital_ZombieGoods def not found.");
                return;
            }

            try
            {
                int randState = 0;
                Map randMapForPop = null;
                var randMap = negotiator?.Map ?? map;
                string randScope = "Rigor.SyncRigorTraderTrade";
                int randSeed = BuildRigorTradeSeed(randMap?.Index ?? mapIndex, negotiatorThingId, TryGetDefName(tradeDef));
                Patch_WorldRandStabilizer.TryEnterDeterministicRandScope(
                    randScope,
                    randSeed,
                    randMap,
                    RigorTradeWorldSeedOffset,
                    ref randState,
                    out randMapForPop);

                try
                {
                    var trader = CreateTradeShip(tradeDef);
                    if (trader == null)
                    {
                        Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: could not construct TradeShip.");
                        return;
                    }
                    var targetMap = negotiator?.Map ?? map;
                    if (targetMap == null)
                    {
                        Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: no target map resolved for AddShip.");
                        return;
                    }
                    targetMap.passingShipManager.AddShip(trader);
                    trader.GenerateThings();

                    // 参考喵喵电商路径：避免在联机同步动作中制造额外 Letter 引用，降低 deep-save 引用污染风险。
                    // 交易商已通过 passingShipManager 可见，不依赖来信也可完成交易流程。

                    if (rm != null)
                    {
                        try
                        {
                            var tcField = AccessTools.Field(rm.GetType(), "tradeCooldown");
                            tcField?.SetValue(rm, Find.TickManager.TicksGame + 60000 * 10);
                        }
                        catch { }

                        try
                        {
                            var relField = AccessTools.Field(rm.GetType(), "relationship");
                            if (relField != null)
                            {
                                var cur = (int)relField.GetValue(rm);
                                relField.SetValue(rm, cur - 1);
                            }
                        }
                        catch { }
                    }
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms hostExecutedTrade: trader spawned and synced successfully for pawn {(negotiator != null ? negotiator.LabelShort : "null")} on map {targetMap.Index}.");
                }
                finally
                {
                    Patch_WorldRandStabilizer.ExitDeterministicRandScope(randScope, randState, randMapForPop);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[MP-MeowOnlineShop] RigorMortisComms SyncRigorTraderTrade: {e}");
            }
        }

        /// <summary>与原版 <c>ZombieTraderComms.TraderDialogFor</c> 正文一致，供 <see cref="Dialog_MpRigorTrader"/> 使用。</summary>
        public static string GetTraderBodyText(Pawn negotiator)
        {
            if (negotiator == null)
                return "";
            var rm = GetRMComponent();
            var merchant = GetNpcMerchant(rm);
            var taoist = GetNpcTaoist(rm);
            bool isTaoist = taoist != null && negotiator == taoist;
            if (!isTaoist)
            {
                if (merchant == null)
                    return "RiM.ContactTrader_DiaNodeText".Translate().Resolve();
                return "RiM.ContactTrader_DiaNodeText".Translate()
                    .Formatted(merchant.Named("PAWN"))
                    .AdjustedFor(merchant)
                    .Resolve();
            }
            return "RiM.ContactTrader_DiaNodeText_Scared".Translate()
                .Formatted(negotiator.Named("PAWN"))
                .AdjustedFor(negotiator)
                .Resolve();
        }

        /// <summary>是否显示「联系后勤部交易」按钮区域（原版：有萌螈派系且非道士）。</summary>
        public static bool ShouldShowTradeOffer(Pawn negotiator)
        {
            if (negotiator == null) return false;
            var rm = GetRMComponent();
            var taoist = GetNpcTaoist(rm);
            if (taoist != null && negotiator == taoist)
                return false;
            return GetAxolotlWanderingDynastyFaction() != null;
        }

        /// <summary>若不可点击交易，返回禁用原因；若可点击则返回 null。</summary>
        public static string GetTradeBlockReason(Pawn negotiator)
        {
            if (negotiator == null)
                return null;
            if (negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
                return "WorkTypeDisablesOption".Translate(SkillDefOf.Social.label);

            var rm = GetRMComponent();
            if (rm == null)
                return null;
            int tradeCooldown = 0;
            try
            {
                var tc = AccessTools.Field(rm.GetType(), "tradeCooldown");
                if (tc != null)
                    tradeCooldown = (int)tc.GetValue(rm);
            }
            catch { }

            if (tradeCooldown >= Find.TickManager.TicksGame)
            {
                return "RiM.ContactTraderInCooldown".Translate(
                    (tradeCooldown - Find.TickManager.TicksGame).TicksToDays());
            }
            return null;
        }

        private static void CleanupOpenTickCache(int nowTicks)
        {
            if (LastOpenTickByNegotiator.Count <= 64)
                return;
            var removeKeys = new List<int>();
            foreach (var pair in LastOpenTickByNegotiator)
            {
                if (nowTicks - pair.Value > 1200)
                    removeKeys.Add(pair.Key);
            }
            foreach (var key in removeKeys)
                LastOpenTickByNegotiator.Remove(key);
        }

        private static bool HasOpenTraderWindowFor(int negotiatorId)
        {
            if (negotiatorId <= 0 || Find.WindowStack == null)
                return false;
            foreach (var window in Find.WindowStack.Windows)
            {
                if (window is Dialog_MpRigorTrader dialog && dialog.Negotiator?.thingIDNumber == negotiatorId)
                    return true;
            }
            return false;
        }

        private static bool IsLikelyZombieTraderType(Type type)
        {
            if (type == null)
                return false;

            if (ZombieTraderCommsType != null && ZombieTraderCommsType.IsAssignableFrom(type))
                return true;

            var fullName = type.FullName ?? type.Name ?? "";
            var asmName = type.Assembly?.GetName().Name ?? "";
            if (fullName.IndexOf("ZombieTraderComms", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (asmName.IndexOf("RigorMortis", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            return typeof(ICommunicable).IsAssignableFrom(type);
        }

        private static object TryGetMemberValue(object instance, params string[] names)
        {
            if (instance == null || names == null)
                return null;

            var type = instance.GetType();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name))
                    continue;
                try
                {
                    var f = AccessTools.Field(type, name);
                    if (f != null)
                        return f.GetValue(instance);
                }
                catch { }

                try
                {
                    var p = AccessTools.Property(type, name);
                    if (p != null)
                        return p.GetValue(instance);
                }
                catch { }
            }
            return null;
        }

        private static bool IsZombieTraderNegotiationDialog(Window window, int negotiatorId)
        {
            if (window == null)
                return false;

            var type = window.GetType();
            var fullName = type.FullName ?? type.Name ?? "";
            if (fullName.IndexOf("Dialog_Negotiation", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var dialogNegotiator = TryGetMemberValue(window, "negotiator", "Negotiator", "pawn") as Pawn;
            if (negotiatorId > 0 && dialogNegotiator != null && dialogNegotiator.thingIDNumber != negotiatorId)
                return false;

            var target = TryGetMemberValue(window, "talker", "communicable", "commTarget", "target", "targetRef");
            if (target != null)
                return IsZombieTraderComms(target);

            // 对非目标或字段名变化保守放行，避免误关宗文/其它通讯流程窗口。
            return false;
        }

        internal static int CloseRelatedCommsWindows(Pawn negotiator, Window initiator = null, string source = null)
        {
            if (Find.WindowStack == null)
                return 0;

            int negotiatorId = negotiator?.thingIDNumber ?? -1;
            var windows = Find.WindowStack.Windows?.ToList();
            if (windows == null || windows.Count == 0)
                return 0;

            int closed = 0;
            foreach (var window in windows)
            {
                if (window == null || window == initiator)
                    continue;

                bool shouldClose = false;
                if (window is Dialog_MpRigorTrader mpWindow)
                {
                    int mpId = mpWindow.Negotiator?.thingIDNumber ?? -1;
                    shouldClose = negotiatorId > 0 && mpId == negotiatorId;
                }
                else if (IsZombieTraderNegotiationDialog(window, negotiatorId))
                {
                    shouldClose = true;
                }

                if (!shouldClose || !Find.WindowStack.IsOpen(window))
                    continue;

                try
                {
                    window.Close();
                    closed++;
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortisComms: failed to close related window {window.GetType().FullName}: {e.Message}");
                }
            }

            if (closed > 0)
            {
                Log.Message($"[MP-MeowOnlineShop] RigorMortisComms: closed {closed} related comm windows (source={source ?? "unknown"}, negotiator={negotiator?.LabelShort ?? "null"}).");
            }
            return closed;
        }

        private static bool TryOpenMpTraderWindow(Pawn negotiator, string source)
        {
            if (negotiator == null) return false;
            try
            {
                int nowTicks = Find.TickManager?.TicksGame ?? 0;
                int negotiatorId = negotiator.thingIDNumber;
                CleanupOpenTickCache(nowTicks);
                if (negotiatorId > 0 && LastOpenTickByNegotiator.TryGetValue(negotiatorId, out int lastTick) &&
                    nowTicks - lastTick <= OpenWindowCooldownTicks)
                {
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms: suppress duplicate open ({source}) for {negotiator.LabelShort}, tickDelta={nowTicks - lastTick}.");
                    return false;
                }

                CloseRelatedCommsWindows(negotiator, null, $"{source}:before_open");
                if (HasOpenTraderWindowFor(negotiatorId))
                {
                    LastOpenTickByNegotiator[negotiatorId] = nowTicks;
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms: window already open ({source}) for {negotiator.LabelShort}, skip reopen.");
                    return false;
                }

                Log.Message($"[MP-MeowOnlineShop] RigorMortisComms: opening MP trader window ({source}) for {negotiator.LabelShort}.");
                Find.WindowStack.Add(new Dialog_MpRigorTrader(negotiator));
                if (negotiatorId > 0)
                    LastOpenTickByNegotiator[negotiatorId] = nowTicks;
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[MP-MeowOnlineShop] RigorMortisComms TryOpenMpTraderWindow({source}): {e}");
                return false;
            }
        }

        internal static bool IsZombieTraderComms(object o)
        {
            if (o == null)
                return false;
            return IsLikelyZombieTraderType(o.GetType());
        }

        private static string NormalizeTradeOptionLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "";
            return raw.Replace(" ", "")
                .Replace("\t", "")
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim()
                .ToLowerInvariant();
        }

        private static string GetDiaOptionLabelForMatch(DiaOption option)
        {
            if (option == null)
                return "";
            try
            {
                string text = TryGetMemberValue(option, "text", "label", "Label", "Text") as string
                    ?? "";
                return text;
            }
            catch
            {
                return "";
            }
        }

        private static bool IsZombieTraderTradeOption(DiaOption option)
        {
            if (option == null || option.action == null)
                return false;

            // 对目标 mod 的 GetDiaOptions 来说，可执行项本质都是交易相关动作；
            // 这里优先按 action 归属识别，避免本地化文本/字段变化导致漏补丁。
            var actionDeclType = option.action.Method?.DeclaringType;
            var declName = actionDeclType?.FullName ?? "";
            if (!string.IsNullOrEmpty(declName) &&
                declName.IndexOf("ZombieTraderComms", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // 次级兜底：按文本/键值匹配，覆盖可能的包装委托或反射调用。
            var normalized = NormalizeTradeOptionLabel(GetDiaOptionLabelForMatch(option));
            var keyNormalized = NormalizeTradeOptionLabel("RiM.ContactTraderTrade".Translate());
            if (!string.IsNullOrEmpty(normalized))
            {
                if (!string.IsNullOrEmpty(keyNormalized) && normalized.Contains(keyNormalized))
                    return true;
                if (normalized.Contains("contacttradertrade") || normalized.Contains("联系后勤部交易"))
                    return true;
            }

            // 最后兜底：只要是可执行选项就包装，保证不会出现“按钮漏同步导致单端执行”。
            return option.action != null;
        }

        private static Action BuildSyncedTradeAction(Action originalAction, Pawn negotiator)
        {
            return delegate
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                {
                    originalAction?.Invoke();
                    return;
                }

                // DiaOption 已经在 MP 命令回放链中执行时，不应再次发起 DoSync，
                // 否则会为同一个用户点击叠加第二层同步命令，导致后续交易状态分叉。
                if (IsExecutingMultiplayerCommand())
                {
                    int replayMapIndex = negotiator?.Map?.Index ?? -1;
                    int replayNid = negotiator?.thingIDNumber ?? -1;
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms patchedTradeActionOnly: executingCmds=true, run SyncRigorTraderTrade directly (pawn={negotiator?.LabelShort ?? "null"}, map={replayMapIndex}, id={replayNid}).");
                    SyncRigorTraderTrade(replayMapIndex, replayNid);
                    return;
                }

                int mapIndex = negotiator?.Map?.Index ?? -1;
                int nid = negotiator?.thingIDNumber ?? -1;
                if (SyncRigorTraderTradeMethod == null)
                {
                    Log.Warning($"[MP-MeowOnlineShop] RigorMortisComms patchedTradeActionOnly: sync method unavailable, abort request to avoid unsynced local execution (pawn={negotiator?.LabelShort ?? "null"}).");
                    return;
                }

                Log.Message($"[MP-MeowOnlineShop] RigorMortisComms patchedTradeActionOnly: request host trade sync (pawn={negotiator?.LabelShort ?? "null"}, map={mapIndex}, id={nid}).");
                SyncRigorTraderTradeMethod.DoSync(null, mapIndex, nid);
            };
        }

        private static bool IsExecutingMultiplayerCommand()
        {
            try
            {
                if (MultiplayerExecutingCmdsField != null && MultiplayerExecutingCmdsField.FieldType == typeof(bool))
                    return (bool)MultiplayerExecutingCmdsField.GetValue(null);
                if (MultiplayerExecutingCmdsProperty != null && MultiplayerExecutingCmdsProperty.PropertyType == typeof(bool))
                    return (bool)MultiplayerExecutingCmdsProperty.GetValue(null);
            }
            catch
            {
                // 反射失败时保持保守行为：返回 false，仍可走原同步兜底路径。
            }
            return false;
        }

        internal static class ZombieTrader_GetDiaOptions_Patch
        {
            public static IEnumerable<DiaOption> Postfix(IEnumerable<DiaOption> __result, Pawn negotiator, object __instance)
            {
                if (__result == null)
                    yield break;

                bool shouldPatch = MP.enabled && MP.IsInMultiplayer && IsZombieTraderComms(__instance);
                if (!shouldPatch)
                {
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.GetDiaOptions Postfix: skipNonZombieTrader (type={__instance?.GetType().FullName ?? "null"}, pawn={negotiator?.LabelShort ?? "null"}).");
                    foreach (var option in __result)
                        yield return option;
                    yield break;
                }

                int patchedCount = 0;
                int candidateCount = 0;
                var patchedLabels = new List<string>();
                foreach (var option in __result)
                {
                    if (option?.action != null)
                        candidateCount++;
                    if (IsZombieTraderTradeOption(option))
                    {
                        var originalAction = option.action;
                        option.action = BuildSyncedTradeAction(originalAction, negotiator);
                        patchedCount++;
                        if (patchedLabels.Count < 5)
                            patchedLabels.Add(GetDiaOptionLabelForMatch(option));
                    }
                    yield return option;
                }

                Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.GetDiaOptions Postfix: candidates={candidateCount}, patchedTradeActionOnly={patchedCount}, labels=[{string.Join(" | ", patchedLabels)}], pawn={negotiator?.LabelShort ?? "null"}.");
            }
        }

        /// <summary>Prefix：联机下替换 FloatMenu 选项，直接打开本地对话。</summary>
        internal static class ZombieTrader_CommFloatMenuOption_Patch
        {
            public static bool Prefix(object __instance, Building_CommsConsole console, Pawn negotiator, ref FloatMenuOption __result)
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return true;
                if (!IsZombieTraderComms(__instance) || console == null || negotiator == null)
                {
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.CommFloatMenuOption Prefix: passthroughNonTarget (type={__instance?.GetType().FullName ?? "null"}, pawn={negotiator?.LabelShort ?? "null"}).");
                    return true;
                }

                var label = "RiM.ContactTrader".Translate();
                try
                {
                    var m = AccessTools.Method(__instance.GetType(), "GetCallLabel");
                    if (m != null)
                        label = m.Invoke(__instance, null) as string ?? label;
                }
                catch { }

                var pawn = negotiator;

                // 不再 DecoratePrioritizedTask，避免额外 Job 路径导致关闭后反向重开。
                __result = new FloatMenuOption(label, () => TryOpenMpTraderWindow(pawn, "CommFloatMenuOption"), MenuOptionPriority.InitiateSocial);
                Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.CommFloatMenuOption Prefix: interceptedZombieTrader (type={__instance?.GetType().FullName ?? "null"}, pawn={pawn.LabelShort}).");

                return false;
            }
        }

        /// <summary>Prefix：联机下不走原版 Dialog 路径（与 CommFloatMenu 一致）。</summary>
        internal static class ZombieTrader_TryOpenComms_Patch
        {
            public static bool Prefix(object __instance, Pawn negotiator)
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return true;
                if (!IsZombieTraderComms(__instance))
                {
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.TryOpenComms Prefix: passthroughNonTarget (type={__instance?.GetType().FullName ?? "null"}, pawn={negotiator?.LabelShort ?? "null"}).");
                    return true;
                }

                Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.TryOpenComms Prefix: interceptedZombieTrader (type={__instance?.GetType().FullName ?? "null"}, pawn={negotiator?.LabelShort ?? "null"}).");
                TryOpenMpTraderWindow(negotiator, "TryOpenComms");
                return false;
            }
        }

        /// <summary>Prefix：拦截 UseCommsConsole Job，仅针对 ZombieTraderComms。</summary>
        internal static class CommsConsole_GiveUseCommsJob_ZombieTrader_Patch
        {
            public static bool Prefix(Pawn negotiator, ICommunicable target)
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return true;
                if (!IsZombieTraderComms(target))
                {
                    Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.GiveUseCommsJob Prefix: passthroughNonTarget (type={target?.GetType().FullName ?? "null"}, pawn={negotiator?.LabelShort ?? "null"}).");
                    return true;
                }

                Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.GiveUseCommsJob Prefix: interceptedZombieTrader (type={target?.GetType().FullName ?? "null"}, pawn={negotiator?.LabelShort ?? "null"}).");
                TryOpenMpTraderWindow(negotiator, "GiveUseCommsJob");
                return false;
            }
        }

        /// <summary>在 <see cref="MpMeowOnlineShopBootstrap"/> 中调用。</summary>
        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
            {
                Log.Warning("[MP-MeowOnlineShop] RigorMortisComms.Apply aborted: harmony instance is null.");
                return;
            }

            bool tradeDiaPatched = false;
            bool floatMenuPatched = false;
            bool tryOpenCommsPatched = false;
            bool giveUseCommsJobPatched = false;
            bool fallbackUsed = false;

            Log.Message($"[MP-MeowOnlineShop] RigorMortisComms.Apply begin: ZombieTraderCommsTypeFound={ZombieTraderCommsType != null}");

            try
            {
                if (TryResolveGetDiaOptions(out var mDia, out var diaFallback))
                {
                    var post = typeof(ZombieTrader_GetDiaOptions_Patch).GetMethod("Postfix",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    harmony.Patch(mDia, postfix: new HarmonyMethod(post) { priority = Priority.First });
                    tradeDiaPatched = true;
                    fallbackUsed |= diaFallback;
                    Log.Message($"[MP-MeowOnlineShop] Patched {mDia.DeclaringType?.FullName}.{mDia.Name} for patchedTradeActionOnly (fallback={diaFallback}).");
                }
                else
                {
                    Log.Warning("[MP-MeowOnlineShop] RigorMortisComms: GetDiaOptions target not found.");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortisComms patchTradeActionOnly failed: {e.Message}");
            }

            try
            {
                if (TryResolveCommFloatMenuOption(out var mFloat, out var floatFallback))
                {
                    var preFloat = typeof(ZombieTrader_CommFloatMenuOption_Patch).GetMethod("Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    harmony.Patch(mFloat, prefix: new HarmonyMethod(preFloat) { priority = Priority.First });
                    floatMenuPatched = true;
                    fallbackUsed |= floatFallback;
                    Log.Message($"[MP-MeowOnlineShop] Patched {mFloat.DeclaringType?.FullName}.{mFloat.Name} for comm float-menu interception (fallback={floatFallback}).");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortisComms patch comm float-menu failed: {e.Message}");
            }

            try
            {
                if (TryResolveTryOpenComms(out var mTryOpen, out var tryOpenFallback))
                {
                    var preTryOpen = typeof(ZombieTrader_TryOpenComms_Patch).GetMethod("Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    harmony.Patch(mTryOpen, prefix: new HarmonyMethod(preTryOpen) { priority = Priority.First });
                    tryOpenCommsPatched = true;
                    fallbackUsed |= tryOpenFallback;
                    Log.Message($"[MP-MeowOnlineShop] Patched {mTryOpen.DeclaringType?.FullName}.{mTryOpen.Name} for TryOpenComms interception (fallback={tryOpenFallback}).");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortisComms patch TryOpenComms failed: {e.Message}");
            }

            try
            {
                var mGiveUseCommsJob = AccessTools.Method(typeof(Building_CommsConsole), "GiveUseCommsJob",
                    new[] { typeof(Pawn), typeof(ICommunicable) });
                if (mGiveUseCommsJob != null)
                {
                    var preGiveUseCommsJob = typeof(CommsConsole_GiveUseCommsJob_ZombieTrader_Patch).GetMethod("Prefix",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    harmony.Patch(mGiveUseCommsJob, prefix: new HarmonyMethod(preGiveUseCommsJob) { priority = Priority.First });
                    giveUseCommsJobPatched = true;
                    Log.Message("[MP-MeowOnlineShop] Patched Building_CommsConsole.GiveUseCommsJob for ZombieTrader interception.");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] RigorMortisComms patch GiveUseCommsJob failed: {e.Message}");
            }

            if (!_startupSummaryLogged)
            {
                _startupSummaryLogged = true;
                Log.Message($"[MP-MeowOnlineShop] RigorMortis comms patch summary: patchedTradeActionOnly={(tradeDiaPatched ? "ok" : "fail")}, uiInterception(floatMenu={floatMenuPatched},tryOpen={tryOpenCommsPatched},giveUseComms={giveUseCommsJobPatched}), fallbackUsed={(fallbackUsed ? "yes" : "no")}");
            }
        }
    }
}
