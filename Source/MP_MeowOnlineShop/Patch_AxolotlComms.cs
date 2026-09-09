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
    internal static class Patch_AxolotlComms
    {
        private const string AxolotlFactionDefName = "AxolotlWanderingDynasty";
        private const string AxolotlSlavePawnKindDefName = "Axolotl_Slave";
        private const string AxolotlThingDefName = "Axolotl";
        private const string AxolotlBambooDefName = "Axolotl_Bamboo";
        private const string AxolotlCoreDefName = "Axolotl_CoreOfHeavenReachingCauldron";
        private const string AxolotlAlchemyResearchDefName = "Axolotl_Research_Alchemy";
        private const int AxolotlTradeWorldSeedOffset = 0x5E23;

        private static readonly Type AxolotlPatchType =
            AccessTools.TypeByName("Axolotl.MoeLotlFriendlyFactionTalkPatch")
            ?? AccessTools.TypeByName("Axolotl.AxolotlPatch+MoeLotlFriendlyFactionTalkPatch");
        private static readonly Type AxolotlGameComponentUtilityType = AccessTools.TypeByName("Axolotl.AxolotlGameComponentUtility");
        private static readonly Type AxolotlTradeUtilityType = AccessTools.TypeByName("Axolotl.AxolotlUtility+AxolotlTradeUtility");
        private static readonly MethodInfo RequestTraderOptionMethod = AccessTools.Method(typeof(FactionDialogMaker), "RequestTraderOption", new[] { typeof(Map), typeof(Faction), typeof(Pawn) });
        private static readonly MethodInfo RequestMilitaryAidOptionMethod = AccessTools.Method(typeof(FactionDialogMaker), "RequestMilitaryAidOption", new[] { typeof(Map), typeof(Faction), typeof(Pawn) });
        private static readonly MethodInfo GetFactionTradeCooldownMethod = AccessTools.Method(AxolotlGameComponentUtilityType, "GetFactionTradeCooldown", new[] { typeof(string) });
        private static readonly MethodInfo SetFactionTradeCooldownMethod = AccessTools.Method(AxolotlGameComponentUtilityType, "SetFactionTradeCooldown", new[] { typeof(string), typeof(int) });
        private static readonly MethodInfo AmountSendableThingMethod = AccessTools.Method(AxolotlTradeUtilityType, "AmountSendableThing", new[] { typeof(Map), typeof(ThingDef) });
        private static readonly MethodInfo SpawnDropPodThingDefMethod = AccessTools.Method(AxolotlTradeUtilityType, "SpawnDropPod", new[] { typeof(Map), typeof(ThingDef), typeof(int) });
        private static readonly MethodInfo SpawnDropPodThingMethod = AccessTools.Method(AxolotlTradeUtilityType, "SpawnDropPod", new[] { typeof(Map), typeof(Thing) });
        private static readonly Type MultiplayerRuntimeType = AccessTools.TypeByName("Multiplayer.Client.Multiplayer");
        private static readonly FieldInfo MultiplayerExecutingCmdsField = MultiplayerRuntimeType != null
            ? AccessTools.Field(MultiplayerRuntimeType, "ExecutingCmds")
            : null;
        private static readonly PropertyInfo MultiplayerExecutingCmdsProperty = MultiplayerRuntimeType != null
            ? AccessTools.Property(MultiplayerRuntimeType, "ExecutingCmds")
            : null;
        private static readonly Dictionary<string, FieldInfo> FieldAccessorCache = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, PropertyInfo> PropertyAccessorCache = new Dictionary<string, PropertyInfo>();
        private static readonly object MemberAccessorCacheLock = new object();
        private static int _diagRemaining = 140;
        private static bool _loggedAxolotlXenotypeMissing;

        internal static ISyncMethod SyncAxolotlTradeMethod;
        internal static ISyncMethod SyncAxolotlRequestTraderMethod;
        internal static ISyncMethod SyncAxolotlRequestMilitaryAidMethod;

        internal enum VanillaRequestKind
        {
            TraderCaravan,
            MilitaryAid
        }

        internal static void Apply(Harmony harmony)
        {
            // 先保证关键同步方法注册，避免前面任何 Harmony patch 失败导致后续注册被短路。
            EnsureSyncMethodsReady("Apply:pre");
            try
            {
                bool hookCommFloatMenuOption = false;
                bool hookGiveUseCommsJob = false;
                bool hookFactionTryOpen = false;
                MethodInfo addThingTrade = null;
                MethodInfo addPawnTrade = null;
                if (AxolotlPatchType != null)
                {
                    addThingTrade = AccessTools.Method(AxolotlPatchType, "AddTradeDiaOption_Thing",
                        new[] { typeof(Pawn), typeof(Faction), typeof(ThingDef), typeof(int), typeof(float), typeof(int) });
                    addPawnTrade = AccessTools.Method(AxolotlPatchType, "AddTradeDiaOption_Pawn",
                        new[] { typeof(Pawn), typeof(Faction), typeof(PawnKindDef), typeof(float), typeof(int) });
                }

                Log.Message($"[MP-MeowOnlineShop] AxolotlComms resolve targets: patchType={(AxolotlPatchType != null ? AxolotlPatchType.FullName : "null")}, addThing={(addThingTrade != null)}, addPawn={(addPawnTrade != null)}.");
                if (AxolotlPatchType == null || addThingTrade == null || addPawnTrade == null)
                {
                    Log.Warning("[MP-MeowOnlineShop] AxolotlComms patchTargetResolveFailed: MoeLotl trade methods not fully resolved, patchedTradeActionOnly may be disabled.");
                }

                var thingPostfix = AccessTools.Method(typeof(AddTradeDiaOptionThing_Patch), nameof(AddTradeDiaOptionThing_Patch.Postfix));
                var pawnPostfix = AccessTools.Method(typeof(AddTradeDiaOptionPawn_Patch), nameof(AddTradeDiaOptionPawn_Patch.Postfix));
                var activatePrefix = AccessTools.Method(typeof(DiaOption_Activate_Diagnostics_Patch), nameof(DiaOption_Activate_Diagnostics_Patch.Prefix));
                var commFloatMenuPrefix = AccessTools.Method(typeof(Faction_CommFloatMenuOption_Axolotl_Patch), nameof(Faction_CommFloatMenuOption_Axolotl_Patch.Prefix));
                var factionTryOpenPrefix = AccessTools.Method(typeof(Faction_TryOpenComms_Axolotl_Patch), nameof(Faction_TryOpenComms_Axolotl_Patch.Prefix));
                if (addThingTrade != null && thingPostfix != null)
                    harmony.Patch(addThingTrade, postfix: new HarmonyMethod(thingPostfix) { priority = Priority.First });
                if (addPawnTrade != null && pawnPostfix != null)
                    harmony.Patch(addPawnTrade, postfix: new HarmonyMethod(pawnPostfix) { priority = Priority.First });
                if (activatePrefix != null)
                {
                    var activate = AccessTools.Method(typeof(DiaOption), "Activate");
                    if (activate != null)
                        harmony.Patch(activate, prefix: new HarmonyMethod(activatePrefix) { priority = Priority.Last });
                }
                if (commFloatMenuPrefix != null)
                {
                    var commFloatMenu = AccessTools.Method(typeof(Faction), "CommFloatMenuOption",
                        new[] { typeof(Building_CommsConsole), typeof(Pawn) });
                    if (commFloatMenu != null)
                    {
                        harmony.Patch(commFloatMenu, prefix: new HarmonyMethod(commFloatMenuPrefix) { priority = Priority.First });
                        hookCommFloatMenuOption = true;
                    }
                }
                var giveUseCommsJob = AccessTools.Method(typeof(Building_CommsConsole), "GiveUseCommsJob",
                    new[] { typeof(Pawn), typeof(ICommunicable) });
                if (giveUseCommsJob != null)
                {
                    var giveUseCommsJobPrefix = giveUseCommsJob.ReturnType == typeof(void)
                        ? AccessTools.Method(
                            typeof(CommsConsole_GiveUseCommsJob_Axolotl_Patch),
                            nameof(CommsConsole_GiveUseCommsJob_Axolotl_Patch.PrefixVoid))
                        : AccessTools.Method(
                            typeof(CommsConsole_GiveUseCommsJob_Axolotl_Patch),
                            nameof(CommsConsole_GiveUseCommsJob_Axolotl_Patch.PrefixJob));
                    if (giveUseCommsJobPrefix != null)
                    {
                        harmony.Patch(giveUseCommsJob, prefix: new HarmonyMethod(giveUseCommsJobPrefix) { priority = Priority.First });
                        hookGiveUseCommsJob = true;
                    }
                }
                if (factionTryOpenPrefix != null)
                {
                    var factionTryOpen = AccessTools.Method(typeof(Faction), "TryOpenComms", new[] { typeof(Pawn) });
                    if (factionTryOpen != null)
                    {
                        harmony.Patch(factionTryOpen, prefix: new HarmonyMethod(factionTryOpenPrefix) { priority = Priority.First });
                        hookFactionTryOpen = true;
                    }
                }

                Log.Message($"[MP-MeowOnlineShop] Axolotl comms MP patch applied: patchedTradeActionOnly={(addThingTrade != null || addPawnTrade != null ? "yes" : "no")}, customUiInjection=enabled(commFloatMenuOption={hookCommFloatMenuOption},giveUseCommsJob={hookGiveUseCommsJob},factionTryOpen={hookFactionTryOpen}), vanillaRequestsResolved(trader={RequestTraderOptionMethod != null},military={RequestMilitaryAidOptionMethod != null}), syncMethodsReady(trade={SyncAxolotlTradeMethod != null},trader={SyncAxolotlRequestTraderMethod != null},military={SyncAxolotlRequestMilitaryAidMethod != null}), runtimeDiag={(ModDebug.EnableAxolotlCommsTrace ? "enabled" : "disabled")}.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] Axolotl comms MP patch failed: {e}");
            }
            finally
            {
                // 再次兜底，确保即使中途抛错也能拿到可用的 SyncMethod 句柄。
                EnsureSyncMethodsReady("Apply:finally");
            }
        }

        internal static bool EnsureSyncMethodsReady(string source = null)
        {
            if (SyncAxolotlTradeMethod == null)
                SyncAxolotlTradeMethod = RegisterSyncMethodSafe(nameof(RequestAxolotlTrade));
            if (SyncAxolotlRequestTraderMethod == null)
                SyncAxolotlRequestTraderMethod = RegisterSyncMethodSafe(nameof(RequestAxolotlTraderCaravan));
            if (SyncAxolotlRequestMilitaryAidMethod == null)
                SyncAxolotlRequestMilitaryAidMethod = RegisterSyncMethodSafe(nameof(RequestAxolotlMilitaryAid));

            bool ready = SyncAxolotlTradeMethod != null
                && SyncAxolotlRequestTraderMethod != null
                && SyncAxolotlRequestMilitaryAidMethod != null;
            if (!ready)
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms sync registration incomplete (source={source ?? "unknown"}): trade={SyncAxolotlTradeMethod != null}, trader={SyncAxolotlRequestTraderMethod != null}, military={SyncAxolotlRequestMilitaryAidMethod != null}.");
            }
            return ready;
        }

        private static ISyncMethod RegisterSyncMethodSafe(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
                return null;

            try
            {
                var method = AccessTools.Method(typeof(Patch_AxolotlComms), methodName);
                if (method != null)
                {
                    var byMethod = MP.RegisterSyncMethod(method, null);
                    if (byMethod != null)
                        return byMethod;
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms RegisterSyncMethod(MethodInfo) failed: {methodName}, err={e.Message}");
            }

            try
            {
                return MP.RegisterSyncMethod(typeof(Patch_AxolotlComms), methodName);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms RegisterSyncMethod(type+name) failed: {methodName}, err={e.Message}");
                return null;
            }
        }

        private static void LogDiag(string message)
        {
            if (!ModDebug.EnableAxolotlCommsTrace || _diagRemaining <= 0)
                return;
            _diagRemaining--;
            Log.Message(message);
        }

        private static string BuildTradeIdForThing(ThingDef thingDef, int count)
        {
            var defName = thingDef?.defName;
            if (string.IsNullOrEmpty(defName))
                return null;

            if (string.Equals(defName, AxolotlBambooDefName, StringComparison.OrdinalIgnoreCase) && count == 225)
                return "base:bamboo";
            if (string.Equals(defName, AxolotlCoreDefName, StringComparison.OrdinalIgnoreCase) && count == 1)
                return "base:core";
            if (defName.StartsWith("Axolotl_Book", StringComparison.OrdinalIgnoreCase))
                return "book:" + defName;
            return null;
        }

        private static string BuildTradeIdForPawn(PawnKindDef pawnKindDef)
        {
            var defName = pawnKindDef?.defName;
            if (string.IsNullOrEmpty(defName))
                return null;
            if (string.Equals(defName, AxolotlSlavePawnKindDefName, StringComparison.OrdinalIgnoreCase))
                return "base:pawn";
            return null;
        }

        internal static bool IsAxolotlFaction(Faction faction)
        {
            return faction?.def != null && string.Equals(faction.def.defName, AxolotlFactionDefName, StringComparison.OrdinalIgnoreCase);
        }

        private static Faction TryGetDialogFaction(Dialog_NodeTree dialog)
        {
            if (dialog == null)
                return null;
            if (!(dialog is Window))
                return null;
            var window = (Window)dialog;
            return TryGetMemberValue(window, "faction", "Faction", "talkerFaction") as Faction;
        }

        private static string GetDiaOptionLabel(DiaOption option)
        {
            if (option == null)
                return "null";
            var text = TryGetMemberValue(option, "text", "Text", "label") as string;
            return string.IsNullOrEmpty(text) ? "(empty)" : text;
        }

        internal static void OpenAxolotlWindow(Pawn negotiator, Faction faction)
        {
            if (negotiator == null || faction == null) return;
            CloseRelatedCommsWindows(negotiator, null, "open_axolotl_mp");
            if (!HasOpenWindowFor(negotiator.thingIDNumber))
                Find.WindowStack.Add(new Dialog_MpAxolotlTrader(negotiator, faction));
        }

        internal static bool HasOpenWindowFor(int negotiatorId)
        {
            if (negotiatorId <= 0 || Find.WindowStack == null) return false;
            foreach (var window in Find.WindowStack.Windows)
            {
                if (window is Dialog_MpAxolotlTrader dialog && dialog.Negotiator?.thingIDNumber == negotiatorId)
                    return true;
            }
            return false;
        }

        internal static int CloseRelatedCommsWindows(Pawn negotiator, Window initiator, string source)
        {
            if (Find.WindowStack == null) return 0;
            int negotiatorId = negotiator?.thingIDNumber ?? -1;
            if (negotiatorId <= 0) return 0;
            int closed = 0;
            var windows = Find.WindowStack.Windows?.ToList();
            if (windows == null) return 0;

            foreach (var window in windows)
            {
                if (window == null || window == initiator)
                    continue;

                bool shouldClose = false;
                if (window is Dialog_MpAxolotlTrader mpWindow)
                {
                    shouldClose = mpWindow.Negotiator?.thingIDNumber == negotiatorId;
                }
                else if (IsAxolotlNegotiationDialog(window, negotiatorId))
                {
                    shouldClose = true;
                }

                if (!shouldClose || !Find.WindowStack.IsOpen(window))
                    continue;

                try
                {
                    window.Close(false);
                    closed++;
                }
                catch { }
            }

            if (closed > 0)
                Log.Message($"[MP-MeowOnlineShop] AxolotlComms closed {closed} windows (source={source ?? "unknown"}).");

            return closed;
        }

        private static bool IsAxolotlNegotiationDialog(Window window, int negotiatorId)
        {
            var type = window.GetType();
            var fullName = type.FullName ?? type.Name ?? "";
            if (fullName.IndexOf("Dialog_Negotiation", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            var wNegotiator = TryGetMemberValue(window, "negotiator", "Negotiator", "pawn") as Pawn;
            if (wNegotiator == null || wNegotiator.thingIDNumber != negotiatorId)
                return false;

            var faction = TryGetMemberValue(window, "faction", "Faction") as Faction;
            return IsAxolotlFaction(faction);
        }

        private static object TryGetMemberValue(object instance, params string[] names)
        {
            if (instance == null || names == null) return null;
            var type = instance.GetType();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                try
                {
                    var f = GetCachedFieldAccessor(type, name);
                    if (f != null) return f.GetValue(instance);
                }
                catch { }
                try
                {
                    var p = GetCachedPropertyAccessor(type, name);
                    if (p != null) return p.GetValue(instance);
                }
                catch { }
            }
            return null;
        }

        private static FieldInfo GetCachedFieldAccessor(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            string key = (type.FullName ?? type.Name ?? "unknown") + "::f::" + name;
            lock (MemberAccessorCacheLock)
            {
                FieldInfo field;
                if (FieldAccessorCache.TryGetValue(key, out field))
                    return field;

                field = AccessTools.Field(type, name);
                FieldAccessorCache[key] = field;
                return field;
            }
        }

        private static PropertyInfo GetCachedPropertyAccessor(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
                return null;

            string key = (type.FullName ?? type.Name ?? "unknown") + "::p::" + name;
            lock (MemberAccessorCacheLock)
            {
                PropertyInfo property;
                if (PropertyAccessorCache.TryGetValue(key, out property))
                    return property;

                property = AccessTools.Property(type, name);
                PropertyAccessorCache[key] = property;
                return property;
            }
        }

        internal static float GetMarketFactor(Faction faction)
        {
            if (faction == null) return 1.5f;
            if (faction.PlayerRelationKind == FactionRelationKind.Ally) return 1.2f;
            return 1.5f;
        }

        internal static int GetFactionTradeCooldown(string key)
        {
            if (string.IsNullOrEmpty(key) || GetFactionTradeCooldownMethod == null) return 0;
            try { return (int)GetFactionTradeCooldownMethod.Invoke(null, new object[] { key }); }
            catch { return 0; }
        }

        internal static void SetFactionTradeCooldown(string key, int cooldownTick)
        {
            if (string.IsNullOrEmpty(key) || cooldownTick <= 0 || SetFactionTradeCooldownMethod == null) return;
            try { SetFactionTradeCooldownMethod.Invoke(null, new object[] { key, cooldownTick }); }
            catch { }
        }

        internal static int GetSendableSilver(Map map)
        {
            if (map == null || AmountSendableThingMethod == null) return 0;
            try { return (int)AmountSendableThingMethod.Invoke(null, new object[] { map, ThingDefOf.Silver }); }
            catch { return 0; }
        }

        private static bool TryResolveMapAndNegotiator(int mapIndex, int negotiatorThingId, out Map map, out Pawn negotiator, out string reason, out string resolvePath)
        {
            map = Find.Maps.FirstOrDefault(m => m != null && m.Index == mapIndex);
            negotiator = null;
            if (map == null)
            {
                reason = "map_not_found";
                resolvePath = "map_index_missing";
                return false;
            }

            if (negotiatorThingId <= 0)
            {
                reason = null;
                resolvePath = "map_only:invalid_negotiator_id";
                return true;
            }

            negotiator = map.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == negotiatorThingId);
            if (negotiator != null)
            {
                reason = null;
                resolvePath = "map_spawned";
                return true;
            }

            negotiator = map.mapPawns?.AllPawns?.FirstOrDefault(p => p != null && p.thingIDNumber == negotiatorThingId);
            if (negotiator != null)
            {
                reason = null;
                resolvePath = "map_allpawns";
                return true;
            }

            foreach (var otherMap in Find.Maps)
            {
                if (otherMap == null || otherMap == map)
                    continue;

                negotiator = otherMap.mapPawns?.AllPawnsSpawned?.FirstOrDefault(p => p != null && p.thingIDNumber == negotiatorThingId);
                if (negotiator != null)
                {
                    reason = null;
                    resolvePath = "other_map_spawned";
                    return true;
                }

                negotiator = otherMap.mapPawns?.AllPawns?.FirstOrDefault(p => p != null && p.thingIDNumber == negotiatorThingId);
                if (negotiator != null)
                {
                    reason = null;
                    resolvePath = "other_map_allpawns";
                    return true;
                }
            }

            reason = null;
            resolvePath = "map_only:negotiator_missing";
            return true;
        }

        private static int CountSpendableSilver(Map map)
        {
            if (map?.listerThings == null)
                return 0;

            var silverStacks = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            if (silverStacks == null)
                return 0;

            int total = 0;
            for (int i = 0; i < silverStacks.Count; i++)
            {
                var silver = silverStacks[i];
                if (silver == null || silver.Destroyed || !silver.Spawned || silver.stackCount <= 0)
                    continue;
                total += silver.stackCount;
            }

            return total;
        }

        private static bool TryConsumeSilverDeterministically(Map map, int silverCost, out int availableAtStart)
        {
            availableAtStart = CountSpendableSilver(map);
            if (silverCost <= 0)
                return true;
            if (map?.listerThings == null || availableAtStart < silverCost)
                return false;

            int remaining = silverCost;
            var silverStacks = map.listerThings.ThingsOfDef(ThingDefOf.Silver)?
                .Where(t => t != null && !t.Destroyed && t.Spawned && t.stackCount > 0)
                .OrderBy(t => t.thingIDNumber)
                .ToList();
            if (silverStacks == null || silverStacks.Count == 0)
                return false;

            for (int i = 0; i < silverStacks.Count && remaining > 0; i++)
            {
                var silver = silverStacks[i];
                int take = Math.Min(remaining, silver.stackCount);
                if (take <= 0)
                    continue;

                if (take >= silver.stackCount)
                {
                    remaining -= silver.stackCount;
                    silver.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    silver.stackCount -= take;
                    remaining -= take;
                }
            }

            return remaining == 0;
        }

        private static bool TryDropThingsDeterministically(Map map, List<Thing> things, string tradeId, out string reason)
        {
            reason = null;
            if (map == null || things == null || things.Count == 0)
            {
                reason = "invalid_drop_args";
                return false;
            }

            IntVec3 dropCell;
            bool usingCenterFallback = false;
            try
            {
                dropCell = DropCellFinder.TradeDropSpot(map);
            }
            catch (Exception e)
            {
                usingCenterFallback = true;
                dropCell = map.Center;
                reason = "drop_spot_failed:" + e.GetType().Name;
            }

            try
            {
                DropPodUtility.DropThingsNear(dropCell, map, things, 110, false, false, true);
                if (usingCenterFallback)
                    Log.Warning($"[MP-MeowOnlineShop] AxolotlComms dropSpot fallback used: tradeId={tradeId}, map={map.Index}, reason={reason}.");
                return true;
            }
            catch (Exception e)
            {
                reason = "drop_failed:" + e.GetType().Name;
                return false;
            }
        }

        internal static bool SpawnDropPodThingDef(Map map, ThingDef thingDef, int count, out string reason)
        {
            reason = null;
            if (map == null || thingDef == null || count <= 0)
            {
                reason = "invalid_args";
                return false;
            }
            if (SpawnDropPodThingDefMethod == null)
            {
                reason = "spawn_method_missing";
                return false;
            }
            try
            {
                SpawnDropPodThingDefMethod.Invoke(null, new object[] { map, thingDef, count });
                return true;
            }
            catch (Exception e)
            {
                reason = "spawn_invoke_failed:" + e.GetType().Name;
                return false;
            }
        }

        internal static bool SpawnDropPodThing(Map map, Thing thing, out string reason)
        {
            reason = null;
            if (map == null || thing == null)
            {
                reason = "invalid_args";
                return false;
            }
            if (SpawnDropPodThingMethod == null)
            {
                reason = "spawn_method_missing";
                return false;
            }
            try
            {
                SpawnDropPodThingMethod.Invoke(null, new object[] { map, thing });
                return true;
            }
            catch (Exception e)
            {
                reason = "spawn_invoke_failed:" + e.GetType().Name;
                return false;
            }
        }

        internal static string BuildThingTradeKey(Faction faction, ThingDef thingDef, int count)
        {
            return faction?.GetUniqueLoadID() + "T" + thingDef?.defName + "C" + count;
        }

        internal static string BuildPawnTradeKey(Faction faction, PawnKindDef pawnKindDef)
        {
            return faction?.GetUniqueLoadID() + "PK" + pawnKindDef?.defName;
        }

        internal static int CalculateThingPrice(ThingDef thingDef, int count, float factor)
        {
            if (thingDef == null || count <= 0) return int.MaxValue;
            return (int)(thingDef.GetStatValueAbstract(StatDefOf.MarketValue) * count * factor);
        }

        internal static int CalculatePawnPrice(float factor)
        {
            var axolotlThing = DefDatabase<ThingDef>.GetNamedSilentFail(AxolotlThingDefName);
            if (axolotlThing == null) return int.MaxValue;
            return (int)(axolotlThing.GetStatValueAbstract(StatDefOf.MarketValue) * factor);
        }

        internal static bool IsTradeBlocked(Pawn negotiator, Faction faction, out string reason)
        {
            reason = null;
            if (negotiator == null || negotiator.skills == null)
            {
                reason = "invalid_negotiator";
                return true;
            }
            if (negotiator.Map == null)
            {
                reason = "invalid_map";
                return true;
            }
            if (faction == null || !IsAxolotlFaction(faction))
            {
                reason = "invalid_faction";
                return true;
            }
            if (faction.PlayerRelationKind == FactionRelationKind.Hostile)
            {
                reason = "hostile";
                return true;
            }
            if (negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
            {
                reason = "social_disabled";
                return true;
            }
            return false;
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

        private static int BuildAxolotlTradeSeed(int mapIndex, int negotiatorThingId, string tradeId)
        {
            int seed = Gen.HashCombineInt(mapIndex, negotiatorThingId);
            return Gen.HashCombineInt(seed, DeterministicStringHash(tradeId));
        }

        private static bool TryApplyThingTrade(Map map, Faction faction, string thingDefName, int count, int cooldownTick, float factor)
        {
            var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            if (map == null || thingDef == null || count <= 0)
                return false;

            var tradeKey = BuildThingTradeKey(faction, thingDef, count);
            if (GetFactionTradeCooldown(tradeKey) >= Find.TickManager.TicksGame)
                return false;

            int needSilver = CalculateThingPrice(thingDef, count, factor);
            if (CountSpendableSilver(map) < needSilver)
                return false;

            if (!TryConsumeSilverDeterministically(map, needSilver, out int availableAtStart))
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms deterministic silver consume failed: tradeId={thingDefName}, map={map.Index}, need={needSilver}, available={availableAtStart}.");
                return false;
            }

            if (!SpawnDropPodThingDef(map, thingDef, count, out string spawnReason))
            {
                var fallback = ThingMaker.MakeThing(thingDef);
                fallback.stackCount = count;
                if (!TryDropThingsDeterministically(map, new List<Thing> { fallback }, thingDefName, out string dropReason))
                {
                    Log.Warning($"[MP-MeowOnlineShop] AxolotlComms thing trade drop failed: tradeId={thingDefName}, spawnReason={spawnReason}, dropReason={dropReason}.");
                    return false;
                }
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms thing trade used fallback drop path: tradeId={thingDefName}, spawnReason={spawnReason}.");
            }
            SetFactionTradeCooldown(tradeKey, cooldownTick);
            return true;
        }

        private static bool TryApplyPawnTrade(Map map, Pawn negotiator, Faction faction, string pawnKindDefName, int cooldownTick, float factor)
        {
            var pawnKind = DefDatabase<PawnKindDef>.GetNamedSilentFail(pawnKindDefName);
            if (map == null || pawnKind == null || negotiator == null)
                return false;

            var tradeKey = BuildPawnTradeKey(faction, pawnKind);
            if (GetFactionTradeCooldown(tradeKey) >= Find.TickManager.TicksGame)
                return false;
            if (faction.PlayerRelationKind != FactionRelationKind.Ally)
                return false;

            int needSilver = CalculatePawnPrice(factor);
            if (CountSpendableSilver(map) < needSilver)
                return false;

            if (!TryConsumeSilverDeterministically(map, needSilver, out int availableAtStart))
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms deterministic silver consume failed: tradeId={pawnKindDefName}, map={map.Index}, need={needSilver}, available={availableAtStart}.");
                return false;
            }
            var pawnFaction = negotiator.Faction ?? Faction.OfPlayer;
            if (pawnFaction == null)
                return false;

            var generatedPawn = GenerateTradePawn(pawnKind, pawnFaction);
            if (generatedPawn == null)
                return false;

            if (!SpawnDropPodThing(map, generatedPawn, out string spawnReason))
            {
                if (!TryDropThingsDeterministically(map, new List<Thing> { generatedPawn }, pawnKindDefName, out string dropReason))
                {
                    Log.Warning($"[MP-MeowOnlineShop] AxolotlComms pawn trade drop failed: tradeId={pawnKindDefName}, spawnReason={spawnReason}, dropReason={dropReason}.");
                    return false;
                }
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms pawn trade used fallback drop path: tradeId={pawnKindDefName}, spawnReason={spawnReason}.");
            }
            SetFactionTradeCooldown(tradeKey, cooldownTick);
            return true;
        }

        private static Pawn GenerateTradePawn(PawnKindDef pawnKind, Faction faction)
        {
            var forcedXenotype = DefDatabase<XenotypeDef>.GetNamedSilentFail("Axolotl_Xenotype_MoeLotlBase");
            bool hasForcedXenotype = forcedXenotype != null;
            if (!hasForcedXenotype && !_loggedAxolotlXenotypeMissing)
            {
                _loggedAxolotlXenotypeMissing = true;
                Log.Warning("[MP-MeowOnlineShop] AxolotlComms forced xenotype not found, falling back to default pawn generation path.");
            }

            try
            {
                if (hasForcedXenotype)
                    return PawnGenerator.GeneratePawn(new PawnGenerationRequest(pawnKind, faction, forcedXenotype: forcedXenotype));
                return PawnGenerator.GeneratePawn(pawnKind, faction);
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms pawn generation failed: kind={pawnKind?.defName ?? "null"}, err={e.GetType().Name}");
                return null;
            }
        }

        public static void RequestAxolotlTrade(int mapIndex, int negotiatorThingId, string tradeId)
        {
            if (!MP.IsInMultiplayer || string.IsNullOrEmpty(tradeId))
                return;

            if (!TryResolveMapAndNegotiator(mapIndex, negotiatorThingId, out var map, out var negotiator, out string resolveReason, out string resolvePath))
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms trade resolve failed: mapIndex={mapIndex}, negotiatorId={negotiatorThingId}, tradeId={tradeId}, reason={resolveReason}.");
                return;
            }

            var faction = Find.FactionManager?.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail(AxolotlFactionDefName));
            bool requireNegotiator = string.Equals(tradeId, "base:pawn", StringComparison.Ordinal);
            if (IsTradeBlockedForRequest(map, negotiator, faction, requireNegotiator, out string blockReason))
            {
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms trade blocked: tradeId={tradeId}, mapIndex={mapIndex}, negotiatorId={negotiatorThingId}, resolvePath={resolvePath}, reason={blockReason}.");
                return;
            }

            int randState = 0;
            Map randMapForPop = null;
            var randMap = negotiator?.Map ?? map;
            int randSeed = BuildAxolotlTradeSeed(randMap?.Index ?? mapIndex, negotiatorThingId, tradeId);
            const string randScope = "Axolotl.RequestAxolotlTrade";
            LogDiag($"[MP-MeowOnlineShop] AxolotlComms entering rand scope: tradeId={tradeId}, seed={randSeed}, map={randMap?.Index ?? -1}, negotiatorId={negotiatorThingId}, resolvePath={resolvePath}.");
            Patch_WorldRandStabilizer.TryEnterDeterministicRandScope(
                randScope,
                randSeed,
                randMap,
                AxolotlTradeWorldSeedOffset,
                ref randState,
                out randMapForPop);

            try
            {
                float factor = GetMarketFactor(faction);
                bool ok = false;
                if (tradeId == "base:pawn")
                    ok = TryApplyPawnTrade(map, negotiator, faction, AxolotlSlavePawnKindDefName, 5 * 60000, factor);
                else if (tradeId == "base:bamboo")
                    ok = TryApplyThingTrade(map, faction, AxolotlBambooDefName, 225, 0, factor);
                else if (tradeId == "base:core")
                    ok = TryApplyThingTrade(map, faction, AxolotlCoreDefName, 1, 8 * 60000, factor);
                else if (tradeId.StartsWith("book:", StringComparison.Ordinal))
                    ok = TryApplyBookTrade(map, faction, tradeId.Substring("book:".Length), factor);

                if (ok)
                    Log.Message($"[MP-MeowOnlineShop] AxolotlComms hostExecutedTrade: tradeId={tradeId}, mapIndex={map.Index}, negotiatorId={negotiatorThingId}, resolvePath={resolvePath}, negotiator={negotiator?.LabelShort ?? "null"}.");
                else
                    Log.Warning($"[MP-MeowOnlineShop] AxolotlComms trade execution failed: tradeId={tradeId}, mapIndex={map.Index}, negotiatorId={negotiatorThingId}, resolvePath={resolvePath}.");
            }
            finally
            {
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms exiting rand scope: tradeId={tradeId}, state=0x{randState:X}, map={(randMapForPop != null ? randMapForPop.Index : -1)}.");
                Patch_WorldRandStabilizer.ExitDeterministicRandScope(randScope, randState, randMapForPop);
            }
        }

        public static void RequestAxolotlTraderCaravan(int mapIndex, int negotiatorThingId)
        {
            RequestAxolotlVanillaSupport(mapIndex, negotiatorThingId, VanillaRequestKind.TraderCaravan);
        }

        public static void RequestAxolotlMilitaryAid(int mapIndex, int negotiatorThingId)
        {
            RequestAxolotlVanillaSupport(mapIndex, negotiatorThingId, VanillaRequestKind.MilitaryAid);
        }

        private static void RequestAxolotlVanillaSupport(int mapIndex, int negotiatorThingId, VanillaRequestKind kind)
        {
            if (!MP.IsInMultiplayer)
                return;

            if (!TryResolveMapAndNegotiator(mapIndex, negotiatorThingId, out var map, out var negotiator, out string resolveReason, out string resolvePath))
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms vanilla request resolve failed: mapIndex={mapIndex}, negotiatorId={negotiatorThingId}, kind={kind}, reason={resolveReason}.");
                return;
            }
            if (negotiator == null)
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms vanilla request blocked: mapIndex={mapIndex}, negotiatorId={negotiatorThingId}, kind={kind}, resolvePath={resolvePath}, reason=negotiator_required.");
                return;
            }

            var faction = Find.FactionManager?.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail(AxolotlFactionDefName));
            if (faction == null || !IsAxolotlFaction(faction))
                return;
            if (IsTradeBlocked(negotiator, faction, out _))
                return;

            if (!TryCreateVanillaRequestOption(negotiator, faction, kind, out var option, out _))
                return;
            if (option == null)
                return;

            bool disabled = false;
            try { disabled = (bool)TryGetMemberValue(option, "disabled", "Disabled"); }
            catch { }
            if (disabled || option.action == null)
                return;

            try
            {
                option.action.Invoke();
                Log.Message($"[MP-MeowOnlineShop] AxolotlComms hostExecutedVanillaRequest: kind={kind}, negotiator={negotiator.LabelShort}.");
            }
            catch (Exception e)
            {
                Log.Warning($"[MP-MeowOnlineShop] AxolotlComms vanilla request failed: kind={kind}, err={e.Message}");
            }
        }

        internal static bool TryGetVanillaRequestUiData(Pawn negotiator, Faction faction, VanillaRequestKind kind, out string label, out string disabledReason)
        {
            label = null;
            disabledReason = null;
            if (!TryCreateVanillaRequestOption(negotiator, faction, kind, out var option, out _))
                return false;
            if (option == null)
                return false;

            label = GetDiaOptionLabel(option);
            bool disabled = false;
            try { disabled = (bool)TryGetMemberValue(option, "disabled", "Disabled"); }
            catch { }
            disabledReason = TryGetMemberValue(option, "disabledReason", "DisabledReason") as string;
            // 无论是否可点击都返回 true，这样 UI 至少能显示选项和禁用原因。
            return true;
        }

        private static bool TryCreateVanillaRequestOption(Pawn negotiator, Faction faction, VanillaRequestKind kind, out DiaOption option, out string reason)
        {
            option = null;
            reason = null;
            var map = negotiator?.Map;
            if (negotiator == null || map == null || faction == null)
            {
                reason = "invalid_context";
                return false;
            }

            var method = kind == VanillaRequestKind.TraderCaravan ? RequestTraderOptionMethod : RequestMilitaryAidOptionMethod;
            if (method == null)
            {
                reason = "vanilla_method_missing";
                return false;
            }

            try
            {
                option = method.Invoke(null, new object[] { map, faction, negotiator }) as DiaOption;
                if (option == null)
                {
                    reason = "option_null";
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                reason = "invoke_failed:" + e.GetType().Name;
                return false;
            }
        }

        private static bool TryApplyBookTrade(Map map, Faction faction, string thingDefName, float factor)
        {
            if (string.IsNullOrEmpty(thingDefName))
                return false;
            return TryApplyThingTrade(map, faction, thingDefName, 1, 0, factor);
        }

        internal static bool IsAlchemyFinished()
        {
            var def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(AxolotlAlchemyResearchDefName);
            return def != null && def.IsFinished;
        }

        internal static bool IsHostAuthority()
        {
            return MpRuntimeInfo.IsHostAuthority();
        }

        private static bool IsTradeBlockedForRequest(Map map, Pawn negotiator, Faction faction, bool requireNegotiator, out string reason)
        {
            reason = null;
            if (map == null)
            {
                reason = "invalid_map";
                return true;
            }
            if (faction == null || !IsAxolotlFaction(faction))
            {
                reason = "invalid_faction";
                return true;
            }
            if (faction.PlayerRelationKind == FactionRelationKind.Hostile)
            {
                reason = "hostile";
                return true;
            }

            if (!requireNegotiator)
                return false;

            if (negotiator == null || negotiator.skills == null)
            {
                reason = "invalid_negotiator";
                return true;
            }
            if (negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
            {
                reason = "social_disabled";
                return true;
            }
            return false;
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
            }
            return false;
        }

        private static Action BuildSyncedTradeAction(Pawn negotiator, string tradeId)
        {
            return delegate
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return;

                int mapIndex = negotiator?.Map?.Index ?? -1;
                int negotiatorId = negotiator?.thingIDNumber ?? -1;
                if (mapIndex < 0 || negotiatorId < 0 || string.IsNullOrEmpty(tradeId))
                {
                    Log.Warning($"[MP-MeowOnlineShop] AxolotlComms syncedTradeAction blocked: invalid args (tradeId={tradeId}, mapIndex={mapIndex}, negotiatorId={negotiatorId}).");
                    return;
                }

                string chainId = tradeId + "|" + mapIndex + "|" + negotiatorId;
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms chain buy_click: chainId={chainId}.");

                if (IsExecutingMultiplayerCommand())
                {
                    LogDiag($"[MP-MeowOnlineShop] AxolotlComms chain replay_execute: chainId={chainId}.");
                    RequestAxolotlTrade(mapIndex, negotiatorId, tradeId);
                    return;
                }

                EnsureSyncMethodsReady("BuildSyncedTradeAction");
                if (SyncAxolotlTradeMethod == null)
                {
                    Log.Warning($"[MP-MeowOnlineShop] AxolotlComms syncedTradeAction blocked: SyncAxolotlTradeMethod null (chainId={chainId}).");
                    return;
                }

                try
                {
                    SyncAxolotlTradeMethod.DoSync(null, mapIndex, negotiatorId, tradeId);
                    LogDiag($"[MP-MeowOnlineShop] AxolotlComms chain dosync_sent: chainId={chainId}.");
                }
                catch (Exception e)
                {
                    Log.Warning($"[MP-MeowOnlineShop] AxolotlComms syncedTradeAction dosync failed: chainId={chainId}, err={e.Message}.");
                }
            };
        }

        internal static bool IsBookMainType(ThingDef bookDef)
        {
            if (bookDef == null) return false;
            try
            {
                var comp = bookDef.comps?.FirstOrDefault(c => c != null && (c.GetType().Name?.Contains("CompProperties_MoeLotlSkillBook") ?? false));
                if (comp == null) return false;
                var parms = AccessTools.Field(comp.GetType(), "moeLotlSkillBookParms")?.GetValue(comp);
                var giveDef = AccessTools.Field(parms?.GetType(), "giveMoeLotlQiSkillDef")?.GetValue(parms);
                var typeObj = AccessTools.Field(giveDef?.GetType(), "type")?.GetValue(giveDef);
                return string.Equals(typeObj?.ToString(), "Axolotl_MoeLotlQiSkillType_Main", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static class AddTradeDiaOptionThing_Patch
        {
            public static void Postfix(Pawn negotiator, Faction faction, ThingDef thingDef, int Count, ref DiaOption __result)
            {
                if (__result == null || __result.action == null || negotiator == null || !IsAxolotlFaction(faction))
                    return;
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return;
                var tradeId = BuildTradeIdForThing(thingDef, Count);
                if (string.IsNullOrEmpty(tradeId))
                {
                    LogDiag($"[MP-MeowOnlineShop] AxolotlComms skipNonTargetThingTrade: def={thingDef?.defName ?? "null"}, count={Count}.");
                    return;
                }

                __result.action = BuildSyncedTradeAction(negotiator, tradeId);

                var method = __result.action?.Method;
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms wrapThingTradeOption: tradeId={tradeId}, label={GetDiaOptionLabel(__result)}, actionDecl={method?.DeclaringType?.FullName ?? "null"}, actionName={method?.Name ?? "null"}, mpNodeTreeSyncPath=true.");
            }
        }

        internal static class CommsConsole_GiveUseCommsJob_Axolotl_Patch
        {
            public static bool PrefixVoid(Pawn negotiator, ICommunicable target)
            {
                return PrefixCore(negotiator, target);
            }

            public static bool PrefixJob(Pawn negotiator, ICommunicable target, ref Job __result)
            {
                bool runOriginal = PrefixCore(negotiator, target);
                if (!runOriginal)
                    __result = null;
                return runOriginal;
            }

            private static bool PrefixCore(Pawn negotiator, ICommunicable target)
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return true;
                if (!(target is Faction faction) || !IsAxolotlFaction(faction))
                    return true;

                OpenAxolotlWindow(negotiator, faction);
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms intercepted GiveUseCommsJob: negotiator={negotiator?.LabelShort ?? "null"}.");
                return false;
            }
        }

        internal static class Faction_CommFloatMenuOption_Axolotl_Patch
        {
            public static bool Prefix(Faction __instance, Building_CommsConsole console, Pawn negotiator, ref FloatMenuOption __result)
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return true;
                if (!IsAxolotlFaction(__instance))
                    return true;

                var faction = __instance;
                string label = null;
                try { label = faction.GetCallLabel(); }
                catch { }
                if (string.IsNullOrEmpty(label))
                    label = "CallOnRadio".Translate();

                __result = new FloatMenuOption(
                    label,
                    () => OpenAxolotlWindow(negotiator, faction),
                    MenuOptionPriority.InitiateSocial);

                LogDiag($"[MP-MeowOnlineShop] AxolotlComms intercepted Faction.CommFloatMenuOption: negotiator={negotiator?.LabelShort ?? "null"}.");
                return false;
            }
        }

        internal static class Faction_TryOpenComms_Axolotl_Patch
        {
            public static bool Prefix(Faction __instance, Pawn negotiator)
            {
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return true;
                if (!IsAxolotlFaction(__instance))
                    return true;

                OpenAxolotlWindow(negotiator, __instance);
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms intercepted Faction.TryOpenComms: negotiator={negotiator?.LabelShort ?? "null"}.");
                return false;
            }
        }

        internal static class AddTradeDiaOptionPawn_Patch
        {
            public static void Postfix(Pawn negotiator, Faction faction, PawnKindDef pawnKind, ref DiaOption __result)
            {
                if (__result == null || __result.action == null || negotiator == null || !IsAxolotlFaction(faction))
                    return;
                if (!MP.enabled || !MP.IsInMultiplayer)
                    return;

                var tradeId = BuildTradeIdForPawn(pawnKind);
                if (string.IsNullOrEmpty(tradeId))
                {
                    LogDiag($"[MP-MeowOnlineShop] AxolotlComms skipNonTargetPawnTrade: pawnKind={pawnKind?.defName ?? "null"}.");
                    return;
                }

                __result.action = BuildSyncedTradeAction(negotiator, tradeId);

                var method = __result.action?.Method;
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms wrapPawnTradeOption: tradeId={tradeId}, label={GetDiaOptionLabel(__result)}, actionDecl={method?.DeclaringType?.FullName ?? "null"}, actionName={method?.Name ?? "null"}, mpNodeTreeSyncPath=true.");
            }
        }

        internal static class DiaOption_Activate_Diagnostics_Patch
        {
            public static void Prefix(DiaOption __instance)
            {
                if (!MP.enabled || !MP.IsInMultiplayer || __instance == null)
                    return;

                var dialog = __instance.dialog as Dialog_NodeTree;
                if (dialog == null)
                    return;

                var faction = TryGetDialogFaction(dialog);
                if (!IsAxolotlFaction(faction))
                    return;

                var method = __instance.action?.Method;
                var curNode = TryGetMemberValue(dialog, "curNode", "CurNode") as DiaNode;
                int optionCount = curNode?.options?.Count ?? -1;
                LogDiag($"[MP-MeowOnlineShop] AxolotlComms DiaOptionActivate: label={GetDiaOptionLabel(__instance)}, actionDecl={method?.DeclaringType?.FullName ?? "null"}, actionName={method?.Name ?? "null"}, curNodeOptions={optionCount}.");
            }
        }
    }
}
