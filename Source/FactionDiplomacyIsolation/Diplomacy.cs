using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Meow.FactionDiplomacy
{
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        static Bootstrap()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("multifaction")) return;
            if (!MP.enabled) return;
            // Force the original bootstrap to queue its installer before this installer.
            var core = AccessTools.TypeByName("MP_MeowOnlineShop.MpMeowOnlineShopBootstrap");
            if (core != null) System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(core.TypeHandle);
            LongEventHandler.ExecuteWhenFinished(() => Patch_MiliraMultifactionRelations.Apply(null));
        }
    }
    // Stored on the game, never on FactionDef or a process-global whitelist.
    public sealed class MiliraDiplomacyRecord : IExposable
    {
        public Faction faction;
        public bool startExempt, churchEnemy, pendingFriend, friend, corrected;
        public void ExposeData()
        {
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref startExempt, "startExempt");
            Scribe_Values.Look(ref churchEnemy, "churchEnemy");
            Scribe_Values.Look(ref pendingFriend, "pendingFriend");
            Scribe_Values.Look(ref friend, "friend");
            Scribe_Values.Look(ref corrected, "corrected");
        }
    }

    public sealed class FactionGoodwillRecoveryRecord : IExposable
    {
        public Faction player, other;
        public int timer;
        public void ExposeData()
        {
            Scribe_References.Look(ref player, "player");
            Scribe_References.Look(ref other, "other");
            Scribe_Values.Look(ref timer, "timer");
        }
    }

    internal static class Patch_MiliraMultifactionRelations
    {
        private const string Owner = "meow.milira.diplomacy";
        private static Type componentType;
        private static MethodInfo pushFaction, popFaction;
        private static Func<Faction, bool, Faction> push;
        private static Func<Faction> pop;
        private static PropertyInfo worldComponent;
        private static FieldInfo spectatorField, multiplayerGameField;
        [ThreadStatic] private static Map tickMap;
        private static readonly FieldInfo[] flags = new FieldInfo[3];
        private static readonly string[] flagNames = { "turnToFriend_Pre", "turnToFriend", "goodWillCorrected" };
        private static readonly Dictionary<FactionDef, bool> originalPermanent = new Dictionary<FactionDef, bool>();
        private static readonly Dictionary<FactionDef, List<FactionDef>> originalExceptions = new Dictionary<FactionDef, List<FactionDef>>();
        internal static bool TargetAvailable { get; private set; }
        private static FactionDiplomacyState State =>
            FactionDiplomacyState.CurrentState();
        private static bool Active => TargetAvailable && (MP.IsInMultiplayer || State?.initialized == true);

        internal static void Apply(Harmony ignored)
        {
            if (!MP.enabled || TargetAvailable) return;
            componentType = AccessTools.TypeByName("Milira.MiliraGameComponent_OverallControl");
            if (componentType == null) return;
            var h = new Harmony(Owner);
            var restoreLegacy = new List<Action>();
            try
            {
                for (int i = 0; i < flags.Length; i++)
                    flags[i] = AccessTools.Field(componentType, flagNames[i]) ??
                        throw new MissingFieldException(componentType.FullName, flagNames[i]);
                foreach (var def in DefDatabase<FactionDef>.AllDefsListForReading)
                {
                    originalPermanent[def] = def.permanentEnemy;
                    originalExceptions[def] = def.permanentEnemyToEveryoneExcept == null ? null :
                        new List<FactionDef>(def.permanentEnemyToEveryoneExcept);
                }

                Patch(h, AccessTools.Method(typeof(GoodwillSituationWorker_PermanentEnemy), "ArePermanentEnemies",
                    new[] { typeof(Faction), typeof(Faction) }), postfix: nameof(PermanentEnemies));
                Patch(h, AccessTools.Method("Multiplayer.Client.HostUtil:HostServer"), prefix: nameof(BeforeHost));
                Patch(h, AccessTools.Method("Multiplayer.Client.Factions.FactionCreator:NewFactionWithIdeo"), postfix: nameof(NewFaction));
                Patch(h, AccessTools.Method(componentType, "CheckMiliraPermanentEnemyStatus"), prefix: nameof(CheckStatus));
                Patch(h, AccessTools.Method(componentType, "CheckPermanentEnemyChurchIfPlayerIsMilira"), prefix: nameof(SkipGlobalMutation));
                Patch(h, AccessTools.Method("Milira.GoodwillSituationWorker_SlightThaw:Active"), postfix: nameof(SlightThaw));
                // The original manager caches by NPC only. A player's current view must not
                // consume another player's maximum or create relation changes during drawing.
                Patch(h, AccessTools.PropertyGetter(typeof(Faction), "CanEverGiveGoodwillRewards"), postfix: nameof(CanReward));
                Patch(h, AccessTools.Method(typeof(Faction), "CanChangeGoodwillFor"), prefix: nameof(CanChange));
                Patch(h, AccessTools.Method(typeof(Faction), "CheckReachNaturalGoodwill"), prefix: nameof(RecoverGoodwill));
                Patch(h, AccessTools.Method(typeof(GoodwillSituationManager), "RecalculateAll"), prefix: nameof(RecachePlayers));
                Patch(h, AccessTools.Method(typeof(GoodwillSituationManager), "GetMaxGoodwill"), postfix: nameof(PlayerMaximum));
                var initial = typeof(Faction).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                    .Single(m => m.Name.Contains("GetInitialGoodwill") && m.GetParameters().Length == 2);
                Patch(h, initial, prefix: nameof(InitialGoodwill));
                Patch(h, AccessTools.Method(typeof(GoodwillSituationManager), "GetSituations"),
                    prefix: nameof(GetSituations));

                pushFaction = AccessTools.Method("Multiplayer.Client.FactionContext:Push");
                popFaction = AccessTools.Method("Multiplayer.Client.FactionContext:Pop");
                multiplayerGameField = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "game");
                worldComponent = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "WorldComp");
                spectatorField = AccessTools.Field(worldComponent?.PropertyType, "spectatorFaction");
                if (worldComponent == null || spectatorField == null || multiplayerGameField == null) throw new MissingMemberException("MP spectator");
                if (pushFaction == null || popFaction == null) throw new MissingMethodException("MP FactionContext");
                push = (Func<Faction, bool, Faction>)Delegate.CreateDelegate(typeof(Func<Faction, bool, Faction>), pushFaction);
                pop = (Func<Faction>)Delegate.CreateDelegate(typeof(Func<Faction>), popFaction);
                h.Patch(AccessTools.Method(componentType, "GameComponentTick"),
                    prefix: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(BeforeTick)),
                    finalizer: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(AfterScope)));
                foreach (string worker in new[] { "Milira.IncidentWorker_MiliraCluster", "Milira.IncidentWorker_Milian_SmallCluster_SingleTurret" })
                    h.Patch(AccessTools.Method(worker + ":CanFireNowSub"),
                        prefix: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(BeforeIncident)),
                        finalizer: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(AfterScope)));
                foreach (string method in new[] { "GoodwillWith", "TryAffectGoodwillWith", "CanChangeGoodwillFor", "Notify_GoodwillSituationsChanged" })
                    h.Patch(AccessTools.Method(typeof(Faction), method),
                        prefix: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(BeforePair)) { priority = Priority.First },
                        finalizer: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(AfterScope)));
                int rewrites = 0;
                foreach (var type in componentType.Assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                            .OrderBy(m => m.MetadataToken))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null ||
                        (type == componentType && method.Name == "ExposeData")) continue;
                    var instructions = PatchProcessor.GetOriginalInstructions(method);
                    bool readsMap = type == componentType && instructions.Any(i => i.Calls(AccessTools.PropertyGetter(typeof(Find), "CurrentMap")));
                    if (!readsMap && !instructions.Any(i => i.operand is FieldInfo f && flags.Contains(f))) continue;
                    if (instructions.Any(i => i.opcode == OpCodes.Ldflda && i.operand is FieldInfo f && flags.Contains(f)))
                        throw new InvalidOperationException("Unsupported diplomacy flag address: " + method);
                    h.Patch(method, transpiler: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(RewriteFlags)));
                    rewrites++;
                }
                // Multiplayer captures the original hand-over callback, including its pawn,
                // relation and goodwill side effects, as one world command.
                Type settlement = AccessTools.TypeByName("Milira.WorldObjectCompMiliraSettlement");
                Type closure = AccessTools.Inner(settlement, "<>c__DisplayClass7_0");
                MethodInfo callback = AccessTools.DeclaredMethod(closure, "<GetCaravanGizmos>b__0");
                if (callback == null || AccessTools.Field(closure, "caravan") == null ||
                    AccessTools.Field(closure, "<>4__this") == null)
                    throw new MissingMethodException("Milira hand-over closure changed");

                h.Patch(callback, prefix: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(ValidateHandOver)),
                    postfix: new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), nameof(AfterHandOver)));
                DisableLegacy(h, restoreLegacy);
                MP.RegisterSyncDelegate(settlement, closure.Name, callback.Name,
                    new[] { "<>4__this", "caravan" }, Type.EmptyTypes).CancelIfAnyFieldNull();
                TargetAvailable = true;
                Log.Message("[MP-MeowOnlineShop] Milira pair diplomacy ready; flag executors=" + rewrites +
                    "; no periodic goodwill reset; no per-player Def mutation.");
            }
            catch (Exception e)
            {
                h.UnpatchAll(Owner);
                foreach (var restore in restoreLegacy) restore();
                TargetAvailable = false;
                Log.Error("[MP-MeowOnlineShop] Milira diplomacy REQUIRED TARGET FAILURE: " + e);
            }
        }

        private static void DisableLegacy(Harmony h, List<Action> restore)
        {
            var legacy = AccessTools.TypeByName("MP_MeowOnlineShop.Patch_MiliraMultifactionRelations");
            var migration = AccessTools.TypeByName("MP_MeowOnlineShop.MiliraMultifactionRelationMigrationComponent");
            if (legacy == null || migration == null) throw new MissingMemberException("Legacy diplomacy compatibility");
            var hooks = Harmony.GetAllPatchedMethods().SelectMany(method =>
                Harmony.GetPatchInfo(method).Postfixes.Where(p => p.PatchMethod.DeclaringType == legacy)
                    .Select(p => new { method, patch = p })).ToArray();
            if (hooks.Length != 2) throw new InvalidOperationException("Expected 2 installed legacy diplomacy postfixes, got " + hooks.Length);
            Patch(h, AccessTools.Method(migration, "GameComponentTick"), prefix: nameof(SkipLegacyTick));
            foreach (var hook in hooks)
            {
                var patch = hook.patch;
                restore.Add(() => new Harmony(patch.owner).Patch(hook.method, postfix: new HarmonyMethod(patch.PatchMethod)
                    { priority = patch.priority, before = patch.before, after = patch.after }));
                h.Unpatch(hook.method, patch.PatchMethod);
            }
            Log.Message("[Meow.FactionDiplomacy] Replaced 2 legacy diplomacy hooks; other core patches unchanged.");
        }        private static bool SkipLegacyTick() => !TargetAvailable;
        private static void Patch(Harmony h, MethodInfo target, string prefix = null, string postfix = null)
        {
            if (target == null) throw new MissingMethodException("Milira diplomacy target");
            h.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Patch_MiliraMultifactionRelations), postfix) { priority = Priority.Last });
        }

        internal static bool IsMiliraFaction(Faction f) => f?.def?.defName == "Milira_Faction";
        internal static bool IsEligiblePlayerFaction(Faction f) => f?.IsPlayer == true &&
            (f.def.defName == "Milira_PlayerFaction" || f.def.categoryTag == "Kiiro_PlayerFaction");

        internal static MiliraDiplomacyRecord DefaultRecord(Faction f) => new MiliraDiplomacyRecord
        {
            faction = f,
            startExempt = IsEligiblePlayerFaction(f),
            churchEnemy = f?.def?.defName == "Milira_PlayerFaction",
            friend = IsEligiblePlayerFaction(f)
        };

        internal static MiliraDiplomacyRecord Record(Faction f, bool create = false)
        {
            if (!RealPlayer(f) || State == null) return null;
            var record = FindRecord(State, f);
            if (record != null) return record;
            record = DefaultRecord(f);
            // Queries, including local UI, never write authoritative state.
            if (create) State.records.Add(record);
            return record;
        }

        private static void BeforeHost() => Initialize();
        internal static void Initialize()
        {
            if (!TargetAvailable || State == null || Find.FactionManager == null) return;
            if (!State.initialized)
            {
                // A legacy single-player story can be attributed only to its original
                // player faction. Never copy its global flags into every joining faction.
                var primary = Faction.OfPlayerSilentFail;
                var component = Current.Game.GetComponent(componentType);
                foreach (var faction in Find.FactionManager.AllFactionsListForReading
                            .Where(RealPlayer).OrderBy(f => f.loadID))
                {
                    var r = Record(faction, true);
                    if (!MP.IsInMultiplayer && faction == primary && !r.startExempt && component != null)
                    {
                        r.pendingFriend = (bool)flags[0].GetValue(component);
                        r.friend = (bool)flags[1].GetValue(component);
                        r.corrected = (bool)flags[2].GetValue(component);
                    }
                }
                State.initialized = true;
            }
            RestoreDefinitions();
        }

        internal static void RestoreDefinitions()
        {
            // Remove only the two known single-player mutations before hosting / loading
            // an isolated save. The immutable baseline was captured after XML patches.
            foreach (string name in new[] { "Milira_Faction", "Milira_AngelismChurch" })
            {
                var def = DefDatabase<FactionDef>.GetNamedSilentFail(name);
                if (def == null || !originalPermanent.ContainsKey(def)) continue;
                def.permanentEnemy = originalPermanent[def];
                var list = originalExceptions[def];
                def.permanentEnemyToEveryoneExcept = list == null ? null : new List<FactionDef>(list);
            }
        }

        private static void NewFaction(Faction __result)
        {
            if (!Active || !RealPlayer(__result)) return;
            Record(__result, true);
            // Initial relations were constructed with the symmetric pair predicate.
            // Do not replace them: their initial range and later player actions matter.
        }

        private static void PermanentEnemies(Faction a, Faction b, ref bool __result)
        {
            if (!Active || a == null || b == null || a.IsPlayer == b.IsPlayer) return;
            Faction player = a.IsPlayer ? a : b;
            Faction npc = a.IsPlayer ? b : a;
            var r = Record(player);
            if (r == null) return;
            if (IsMiliraFaction(npc))
            {
                // A starts with a story lock, B has an exemption, C negotiated a thaw.
                // Removing Milira's lock must not remove a lock owned by the player def.
                __result = (!r.startExempt && !r.friend && !r.pendingFriend) ||
                    DefForcesEnemy(player.def, npc);
            }
            else if (npc.def.defName == "Milira_AngelismChurch")
            {
                __result = r.churchEnemy || DefForcesEnemy(player.def, npc) ||
                    DefForcesEnemy(npc.def, player);
            }
        }

        private static bool DefForcesEnemy(FactionDef def, Faction other)
        {
            bool permanent = originalPermanent.TryGetValue(def, out var p) ? p : def.permanentEnemy;
            List<FactionDef> exceptions = originalExceptions.TryGetValue(def, out var e) ? e : def.permanentEnemyToEveryoneExcept;
            return permanent || (def.permanentEnemyToEveryoneExceptPlayer && !other.IsPlayer) ||
                (exceptions != null && !exceptions.Contains(other.def));
        }

        private static bool ManagedPair(Faction a, Faction b) => Active && a != null && b != null &&
            a.IsPlayer != b.IsPlayer && (IsMiliraFaction(a.IsPlayer ? b : a) ||
                (a.IsPlayer ? b : a).def.defName == "Milira_AngelismChurch");
        private static void CanReward(Faction __instance, ref bool __result)
        {
            var player = Faction.OfPlayerSilentFail;
            if (ManagedPair(__instance, player))
            {
                bool enemy = false;
                PermanentEnemies(__instance, player, ref enemy);
                __result &= !enemy;
            }
        }
        private static bool InitialGoodwill(Faction __0, Faction __1, ref int __result)
        {
            if (!ManagedPair(__0, __1)) return true;
            bool enemy = false;
            PermanentEnemies(__0, __1, ref enemy);
            __result = enemy ? -100 : __0.def.naturalEnemy ? -80 : 0;
            return false;
        }
        private static bool CanChange(Faction __instance, Faction other, int goodwillChange, ref bool __result)
        {
            if (!ManagedPair(__instance, other)) return true;
            bool enemy = false;
            PermanentEnemies(__instance, other, ref enemy);
            __result = __instance.HasGoodwill && other.HasGoodwill && !__instance.defeated && !other.defeated &&
                __instance != other && !enemy &&
                !(goodwillChange > 0 && ((__instance.IsPlayer && SettlementUtility.IsPlayerAttackingAnySettlementOf(other)) ||
                    (other.IsPlayer && SettlementUtility.IsPlayerAttackingAnySettlementOf(__instance)))) &&
                !QuestUtility.IsGoodwillLockedByQuest(__instance, other);
            return false;
        }
        private static void BeforePair(Faction __instance, Faction other, out Scope __state)
        {
            __state = null;
            if (!Active || __instance == null || other == null || __instance.IsPlayer == other.IsPlayer) return;
            __state = new Scope { previous = tickMap, pushed = true };
            push(__instance.IsPlayer ? __instance : other, false);
        }
        private static bool RealPlayer(Faction faction)
        {
            if (faction?.IsPlayer != true) return false;
            if (multiplayerGameField?.GetValue(null) == null) return true;
            var world = worldComponent.GetValue(null, null);
            return world == null || !ReferenceEquals(faction, spectatorField.GetValue(world));
        }
        private static List<Faction> Players()
        {
            // Rebuild from live records on every query. Faction defeat/ownership and
            // list replacement must remain immediately visible on every peer.
            var records = State.records;
            // Keep the original O(n log n) path for unusually large sessions.
            if (records.Count > 16)
                return records.Select(r => r.faction).Where(f => RealPlayer(f) && !f.defeated).Distinct().OrderBy(f => f.loadID).ToList();
            var result = new List<Faction>();
            foreach (var record in records)
            {
                var faction = record.faction;
                if (!RealPlayer(faction) || faction.defeated || result.Contains(faction)) continue;
                int index = result.Count;
                while (index > 0 && result[index - 1].loadID > faction.loadID) index--;
                result.Insert(index, faction);
            }
            return result;
        }
        private static FactionGoodwillRecoveryRecord FindRecovery(FactionDiplomacyState state, Faction player, Faction other)
        {
            foreach (var record in state.recovery)
                if (record.player == player && record.other == other) return record;
            return null;
        }
        private static MiliraDiplomacyRecord FindRecord(FactionDiplomacyState state, Faction faction)
        {
            foreach (var record in state.records)
                if (record.faction == faction) return record;
            return null;
        }
        private static void PlayerMaximum(Faction other, ref int __result)
        {
            if (Active && other?.IsPlayer == true) __result = 100;
        }
        private static bool RecachePlayers(GoodwillSituationManager __instance, bool canSendHostilityChangedLetter)
        {
            if (!Active) return true;
            if (MP.InInterface || Current.ProgramState == ProgramState.Entry) return false;
            // GetSituations already computes each pair without the NPC-only cache.
            // Call the relation notification directly: recursively calling RecalculateAll
            // would let the legacy core prefix replace every player with the spectator.
            foreach (var player in Players())
            {
                push(player, false);
                try
                {
                    foreach (var other in Find.FactionManager.AllFactionsListForReading
                                .Where(f => f != player && f.HasGoodwill).OrderBy(f => f.loadID))
                        player.Notify_GoodwillSituationsChanged(other, canSendHostilityChangedLetter, null, null);
                }
                finally { pop(); }
            }
            return false;
        }        private static bool RecoverGoodwill(Faction __instance)
        {
            if (!Active) return true;
            if (__instance.IsPlayer || !__instance.HasGoodwill || MP.InInterface) return false;
            foreach (var player in Players())
            {
                if (player.RelationWith(__instance, true) == null) continue;
                push(player, false);
                try
                {
                    var record = FindRecovery(State, player, __instance);
                    if (record == null)
                    {
                        record = new FactionGoodwillRecoveryRecord { player = player, other = __instance };
                        State.recovery.Add(record);
                    }
                    if (GoodwillSituationWorker_PermanentEnemy.ArePermanentEnemies(player, __instance))
                    { record.timer = 0; continue; }
                    int value = __instance.BaseGoodwillWith(player);
                    int natural = __instance.NaturalGoodwill;
                    if (value >= natural - 50 && value <= natural + 50) { record.timer = 0; continue; }
                    if (++record.timer < 3000000) continue;
                    int delta = value < natural - 50 ? Math.Min(10, natural - 50 - value) :
                        -Math.Min(10, value - natural - 50);
                    __instance.TryAffectGoodwillWith(player, delta, true, !__instance.temporary, HistoryEventDefOf.ReachNaturalGoodwill);
                    record.timer = 0;
                }
                finally { pop(); }
            }
            return false;
        }
        private static bool SkipGlobalMutation() => !Active;
        private static bool CheckStatus()
        {
            if (!Active) return true;
            var r = Record(Faction.OfPlayerSilentFail, !MP.InInterface);
            if (r != null && !MP.InInterface && r.pendingFriend && !r.startExempt) r.friend = true;
            return false;
        }
        private static void SlightThaw(Faction other, ref bool __result)
        {
            if (!Active || !IsMiliraFaction(other)) return;
            var r = Record(Faction.OfPlayerSilentFail);
            __result = r != null && !r.startExempt && r.friend;
        }
        private static bool GetSituations(Faction other, ref List<GoodwillSituationManager.CachedSituation> __result)
        {
            if (!Active || Faction.OfPlayerSilentFail?.IsPlayer != true || other == null || other.IsPlayer) return true;
            __result = new List<GoodwillSituationManager.CachedSituation>();
            if (!other.HasGoodwill) return false;
            foreach (var def in DefDatabase<GoodwillSituationDef>.AllDefsListForReading)
            {
                int max = def.Worker.GetMaxGoodwill(other);
                int natural = def.Worker.GetNaturalGoodwillOffset(other);
                if (max < 100 || natural != 0)
                    __result.Add(new GoodwillSituationManager.CachedSituation { def = def, maxGoodwill = max, naturalGoodwillOffset = natural });
            }
            return false;
        }

        private static IEnumerable<CodeInstruction> RewriteFlags(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(AccessTools.PropertyGetter(typeof(Find), "CurrentMap")))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Patch_MiliraMultifactionRelations), nameof(ComponentMap));
                }
                int index = instruction.operand is FieldInfo f ? Array.IndexOf(flags, f) : -1;
                if (index >= 0 && (instruction.opcode == OpCodes.Ldfld || instruction.opcode == OpCodes.Stfld))
                {
                    bool write = instruction.opcode == OpCodes.Stfld;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(Patch_MiliraMultifactionRelations), (write ? "Set" : "Get") + index);
                }
                yield return instruction;
            }
        }
        private static bool Read(object component, int index)
        {
            if (!Active) return (bool)flags[index].GetValue(component);
            var r = Record(Faction.OfPlayerSilentFail);
            if (r == null) return false;
            return index == 0 ? r.pendingFriend : index == 1 ? r.friend : r.corrected;
        }
        private static void Write(object component, bool value, int index)
        {
            if (!Active) { flags[index].SetValue(component, value); return; }
            if (MP.InInterface) throw new InvalidOperationException("Unsynchronized Milira diplomacy write");
            var r = Record(Faction.OfPlayerSilentFail, true);
            if (r == null) throw new InvalidOperationException("Milira diplomacy has no executing faction");
            if (index == 0) r.pendingFriend = value;
            else if (index == 1) r.friend = value;
            else r.corrected = value;
        }
        private sealed class Scope
        {
            internal Map previous;
            internal bool pushed;
        }
        private static Map ComponentMap() => Active ? tickMap ??
            Find.Maps.Where(m => m.ParentFaction?.IsPlayer == true).OrderBy(m => m.uniqueID).FirstOrDefault() : Find.CurrentMap;
        private static bool BeforeTick(out Scope __state)
        {
            __state = null;
            if (!Active) return true;
            var map = ComponentMap();
            if (map == null) return false;
            __state = new Scope { previous = tickMap, pushed = true };
            tickMap = map;
            push(map.ParentFaction, false);
            return true;
        }
        private static void BeforeIncident(IncidentParms parms, out Scope __state)
        {
            __state = null;
            var map = parms.target as Map;
            if (!Active || map?.ParentFaction?.IsPlayer != true) return;
            __state = new Scope { previous = tickMap, pushed = true };
            tickMap = map;
            push(map.ParentFaction, false);
        }
        private static void AfterScope(Scope __state)
        {
            if (__state == null) return;
            try { if (__state.pushed) pop(); }
            finally { tickMap = __state.previous; }
        }
        private static bool ValidateHandOver(object __instance, out bool __state)
        {
            __state = false;
            if (!Active) return true;
            var caravan = AccessTools.Field(__instance.GetType(), "caravan").GetValue(__instance) as Caravan;
            var comp = AccessTools.Field(__instance.GetType(), "<>4__this").GetValue(__instance) as WorldObjectComp;
            var original = Current.Game.GetComponent(componentType);
            var pawn = AccessTools.Field(componentType, "pawn").GetValue(original) as Pawn;
            return __state = caravan != null && caravan.Faction == Faction.OfPlayerSilentFail &&
                comp?.parent?.Faction?.def?.defName == "Milira_Faction" && pawn != null && caravan.ContainsPawn(pawn);
        }
        private static void AfterHandOver(bool __state)
        {
            if (!__state || !Active || MP.InInterface) return;
            Faction player = Faction.OfPlayerSilentFail;
            Faction npc = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(IsMiliraFaction);
            if (npc == null || player == null) return;
            var forward = npc.RelationWith(player, true);
            var reverse = player.RelationWith(npc, true);
            if (forward == null || reverse == null) return;
            reverse.baseGoodwill = forward.baseGoodwill;
            reverse.kind = forward.kind;
            foreach (Map map in Find.Maps.OrderBy(m => m.uniqueID))
            {
                map.attackTargetsCache.Notify_FactionHostilityChanged(npc, player);
                map.attackTargetsCache.Notify_FactionHostilityChanged(player, npc);
            }
        }
        private static bool Get0(object c) => Read(c, 0);
        private static bool Get1(object c) => Read(c, 1);
        private static bool Get2(object c) => Read(c, 2);
        private static void Set0(object c, bool v) => Write(c, v, 0);
        private static void Set1(object c, bool v) => Write(c, v, 1);
        private static void Set2(object c, bool v) => Write(c, v, 2);
    }

    // Keep the legacy component type so existing saves can resolve it. Its former
    // migration/version and unsaved 1000-tick repair loop are intentionally retired.
    public sealed class FactionDiplomacyState : GameComponent
    {
        private static readonly ConditionalWeakTable<Game, FactionDiplomacyState> states =
            new ConditionalWeakTable<Game, FactionDiplomacyState>();
        internal static FactionDiplomacyState CurrentState()
        {
            var game = Current.Game;
            if (game == null) return null;
            if (states.TryGetValue(game, out var state)) return state;
            return CacheState(game);
        }

        private static FactionDiplomacyState CacheState(Game game)
        {
            var state = game.GetComponent<FactionDiplomacyState>();
            // Construction can query before components are present. Never cache a miss.
            return state == null ? null : states.GetValue(game, _ => state);
        }

        public bool initialized;
        public List<FactionGoodwillRecoveryRecord> recovery = new List<FactionGoodwillRecoveryRecord>();
        public List<MiliraDiplomacyRecord> records = new List<MiliraDiplomacyRecord>();
        public FactionDiplomacyState(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "meowMiliraPairDiplomacy");
            Scribe_Collections.Look(ref recovery, "meowPairGoodwillRecovery", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && recovery == null) recovery = new List<FactionGoodwillRecoveryRecord>();
            Scribe_Collections.Look(ref records, "meowMiliraDiplomacyRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && records == null) records = new List<MiliraDiplomacyRecord>();
        }
        public override void LoadedGame()
        {
            // Loading can replace a component on the same Game. Cache only its identity;
            // records, flags and timers remain live save-owned state.
            var game = Current.Game;
            if (game != null) states.Remove(game);
            if (initialized) Patch_MiliraMultifactionRelations.RestoreDefinitions();
        }
    }
}
