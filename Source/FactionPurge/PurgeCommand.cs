using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using HarmonyLib;
using LudeonTK;
using Multiplayer.API;
using Multiplayer.Client;
using Multiplayer.Client.Comp;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

namespace Meow.FactionPurge
{
    [StaticConstructorOnStartup]
    public static class PurgeCommand
    {
        internal static ISyncMethod Sync;
        internal static string LastResult;
        internal static bool Failed;
        internal static bool Pending;
        internal static string BackupPath;
        internal static bool Mutated;
        private static int queuedId, queuedHost;
        private static string queuedFingerprint;
        private static Game requestGame;
        internal static readonly List<string> RemovedLoadIds = new List<string>();
        private static object maintenanceServer;
        [ThreadStatic] private static List<string> errors;
        static PurgeCommand()
        {
            if (!MP.enabled) return;
            Sync = MP.RegisterSyncMethod(typeof(PurgeCommand), nameof(Execute)).SetHostOnly();
            var harmony = new Harmony("meow.factionpurge.errors");
            harmony.Patch(AccessTools.Method(typeof(Log), "Error", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(PurgeCommand), nameof(CaptureError)));
            harmony.Patch(AccessTools.Method(typeof(Log), "Warning", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(PurgeCommand), nameof(CaptureError)));
            harmony.Patch(AccessTools.Method(typeof(Multiplayer.Common.PlayerManager), "OnPreConnect"),
                postfix: new HarmonyMethod(typeof(PurgeCommand), nameof(BlockNewConnections)));
            harmony.Patch(AccessTools.Method(typeof(Multiplayer.Common.ServerJoiningState), "RunState"),
                prefix: new HarmonyMethod(typeof(PurgeCommand), nameof(BlockJoiningState)));
            harmony.Patch(AccessTools.Method(typeof(Mp), "StopMultiplayer", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(PurgeCommand), nameof(ResetLocalState)));
            harmony.Patch(AccessTools.Method(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI)),
                prefix: new HarmonyMethod(typeof(PurgeCommand), nameof(AllowColonistBar)));
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.AsyncTime.ColonistBarTimeControl"), "DrawButtons"),
                prefix: new HarmonyMethod(typeof(PurgeCommand), nameof(AllowColonistBar)));
            Log.Message("[Meow.FactionPurge] 0.1.0 ready; host-only global command; Debug actions: MP Purge player faction.");
        }
        private static void CaptureError(string text) { if (errors != null && errors.Count < 30) errors.Add(text); }
        // The maintenance world is deliberately paused until rehosting. Existing map-removal
        // guards can retain old bar entries until a simulation tick that will never arrive.
        // Keep this presentation-only window closed; StopMultiplayer restores normal drawing.
        private static bool AllowColonistBar() => !(Mutated && ReferenceEquals(Current.Game, requestGame)
            && Mp.LocalServer != null && ReferenceEquals(Mp.LocalServer, System.Threading.Volatile.Read(ref maintenanceServer)));
        private static void ResetLocalState()
        {
            System.Threading.Volatile.Write(ref maintenanceServer, null);
            requestGame = null;
            queuedFingerprint = null;
            Pending = false;
            LastResult = null;
            Mutated = false;
            Failed = false;
            BackupPath = null;
            RemovedLoadIds.Clear();
        }
        private static void BlockNewConnections(object __instance, ref Multiplayer.Common.MpDisconnectReason? __result)
        {
            var server = System.Threading.Volatile.Read(ref maintenanceServer);
            if (server != null && ReferenceEquals(AccessTools.Field(__instance.GetType(), "server").GetValue(__instance), server))
                __result = Multiplayer.Common.MpDisconnectReason.ServerFull;
        }
        private static bool BlockJoiningState(object __instance, ref System.Threading.Tasks.Task __result)
        {
            var server = System.Threading.Volatile.Read(ref maintenanceServer);
            if (server == null || !ReferenceEquals(AccessTools.Property(__instance.GetType(), "Server").GetValue(__instance, null), server)) return true;
            var player = AccessTools.Property(__instance.GetType(), "Player").GetValue(__instance, null);
            AccessTools.Method(player.GetType(), "Disconnect", new[] { typeof(Multiplayer.Common.MpDisconnectReason), typeof(byte[]) })
                .Invoke(player, new object[] { Multiplayer.Common.MpDisconnectReason.ServerFull, null });
            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }

        private static void VerifyServerAlone()
        {
            // A connection can exist server-side before the client-visible player list is broadcast.
            // Lock first, then fence the server queue; a pre-existing handshake makes us refuse.
            var finished = new System.Threading.Tasks.TaskCompletionSource<int>();
            Mp.LocalServer.queue.Enqueue(() => finished.TrySetResult(Mp.LocalServer.playerManager.Players.Count));
            if (!finished.Task.Wait(5000) || finished.Task.Result != 1)
                throw new InvalidOperationException("服务器仍有连接或正在加入的玩家，请让所有其他连接退出后重试。");
        }

        [DebugAction("Multiplayer local", "MP Purge player faction / 清理玩家派系",
            allowedGameStates = AllowedGameStates.Playing)]
        public static void Open()
        {
            if (!CanMaintain(out var reason)) { Show(reason); return; }
            var options = new List<FloatMenuOption>();
            foreach (var f in Find.FactionManager.AllFactionsListForReading.Where(x => x.IsPlayer).OrderBy(x => x.loadID))
            {
                int id = f.loadID;
                if (id == HostFactionId() || f == Mp.WorldComp.spectatorFaction) continue;
                options.Add(new FloatMenuOption(f.Name + " [Faction " + id + "]", () => Preview(id)));
            }
            if (options.Count == 0) { Show("没有可清理的其他玩家派系。"); return; }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        public static void Preview(int id)
        {
            try
            {
                if (!CanMaintain(out var reason)) { Show(reason); return; }
                var plan = PurgePlan.Build(id, HostFactionId());
                if (!plan.Allowed) { Show("未执行清理：\n" + string.Join("\n", plan.Blockers.Distinct().Take(20))); return; }
                Find.WindowStack.Add(new ConfirmPurgeWindow(plan));
            }
            catch (Exception ex) { Show("检查失败，未执行清理：" + ex.Message); Log.Error("[Meow.FactionPurge] Preview failed: " + ex); }
        }

        internal static int HostFactionId()
        {
            // Host-only UI call: never read LocalServer as a simulation decision.
            object data = AccessTools.Field(Mp.LocalServer.GetType(), "worldData").GetValue(Mp.LocalServer);
            return (int)AccessTools.Field(data.GetType(), "hostFactionId").GetValue(data);
        }

        internal static bool CanMaintain(out string reason)
        {
            reason = null;
            if (!MP.IsInMultiplayer || !MP.IsHosting || Mp.LocalServer == null || Mp.session.replay)
                reason = "只能由正在开房的服务器主执行。";
            else if (Pending) reason = "已有清理操作在等待完成。";
            else if (ReferenceEquals(System.Threading.Volatile.Read(ref maintenanceServer), Mp.LocalServer))
                reason = "本房间已进入清理维护，请退出并从清理后的存档重新开房。";
            else if (Mp.session.players.Count != 1) reason = "派系清理是维护操作，请让其他玩家（含观战者）先退出服务器。";
            else if (Mp.RealPlayerFaction?.loadID != HostFactionId()) reason = "请先切回服务器主派系，再执行清理。";
            else if (Sync == null) reason = "同步指令未注册，不能执行。";
            else reason = LegacyPauseBlocker();
            return reason == null;
        }

        // Legacy PauseLockSession is a permanent registry, not an operation holding entities.
        // Its callbacks may read local UI: evaluate them only at host UI admission, never in Execute.
        private static string LegacyPauseBlocker()
        {
            try
            {
                foreach (var manager in PurgePlan.Managers())
                    foreach (var session in (IEnumerable)AccessTools.Property(manager.GetType(), "AllSessions").GetValue(manager, null))
                        if (session.GetType() == typeof(Multiplayer.Client.Persistent.PauseLockSession))
                        {
                            var legacy = (Multiplayer.Client.Persistent.PauseLockSession)session;
                            bool active = legacy.IsCurrentlyPausing(null);
                            foreach (var map in Find.Maps) if (legacy.IsCurrentlyPausing(map)) active = true;
                            if (active) return "有模组暂停锁正在生效，请先关闭相关窗口或完成操作后重试。";
                        }
                return null;
            }
            catch (Exception ex) { return "无法验证模组暂停锁，未执行清理：" + ex.Message; }
        }

        public static void Request(int id, string fingerprint)
        {
            if (!CanMaintain(out var reason)) { Show(reason); return; }
            int host = HostFactionId();
            PurgePlan current;
            try { current = PurgePlan.Build(id, host); }
            catch (Exception ex) { Show("检查失败，未执行清理：" + ex.Message); return; }
            if (!current.Allowed || current.Fingerprint != fingerprint) { Show("目标状态已变化，请重新预览。"); return; }
            Pending = true;
            requestGame = Current.Game;
            // A volatile host transport gate also blocks loopback and connects attempted during backup.
            // It does not modify simulation data and belongs to this server instance only.
            System.Threading.Volatile.Write(ref maintenanceServer, (object)Mp.LocalServer);
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    VerifyServerAlone();
                    string pauseBlocker = LegacyPauseBlocker();
                    if (pauseBlocker != null) throw new InvalidOperationException(pauseBlocker);
                    if (Mp.session.players.Count != 1) throw new InvalidOperationException("其他玩家已加入，请重新维护。");
                    var name = "Before-FactionPurge-" + id + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                    var file = new FileInfo(Path.Combine(Mp.ReplaysDir, name + ".zip"));
                    if (file.Exists) throw new IOException("备份文件已存在，拒绝覆盖。");
                    WriteCheckedReplay(file, SnapshotChecked());
                    BackupPath = file.FullName;
                    Log.Message("[Meow.FactionPurge] BACKUP " + BackupPath);
                    LastResult = null;
                    Failed = false;
                    Mutated = false;
                    // ShouldSync is false inside a long event. Dispatch from the next interface update.
                    queuedId = id;
                    queuedHost = host;
                    queuedFingerprint = fingerprint;
                }
                catch (Exception ex)
                {
                    Pending = false;
                    System.Threading.Volatile.Write(ref maintenanceServer, null);
                    Show("未执行清理：" + ex.Message);
                    Log.Error("[Meow.FactionPurge] Request failed: " + ex);
                }
            }, "MpSaving", false, null);
        }

        internal static void DispatchQueued()
        {
            if (queuedFingerprint == null || LongEventHandler.AnyEventNowOrWaiting) return;
            if (!ReferenceEquals(requestGame, Current.Game) || !MP.IsHosting)
            {
                queuedFingerprint = null;
                Pending = false;
                System.Threading.Volatile.Write(ref maintenanceServer, null);
                return;
            }
            if (!Mp.ShouldSync) return;
            string fingerprint = queuedFingerprint;
            queuedFingerprint = null;
            string pauseBlocker = LegacyPauseBlocker();
            if (pauseBlocker != null)
            {
                Failed = true;
                LastResult = pauseBlocker;
                System.Threading.Volatile.Write(ref maintenanceServer, null);
                return;
            }
            if (!Sync.DoSync(null, queuedId, queuedHost, fingerprint))
            {
                Failed = true;
                LastResult = "同步指令未发送，未执行清理。";
            }
        }

        // IDs and the previewed entity list are the complete synchronized payload.
        // No file IO, local authority, UI focus, clock time, or connection count controls this executor.
        public static void Execute(int id, int protectedFactionId, string fingerprint)
        {
            if (!MP.IsExecutingSyncCommand) throw new InvalidOperationException("Purge requires a synchronized host command.");
            Mutated = false;
            try
            {
                var plan = PurgePlan.Build(id, protectedFactionId);
                if (!plan.Allowed || plan.Fingerprint != fingerprint)
                {
                    LastResult = "未执行清理：目标已变化或前置检查未通过。\n" + string.Join("\n", plan.Blockers.Distinct().Take(20));
                    return;
                }
                var removedMaps = plan.Bases.Where(b => b.Map != null).Select(b => b.Map).ToList();
                RemovedLoadIds.Clear();
                RemovedLoadIds.Add(plan.Target.GetUniqueLoadID());
                RemovedLoadIds.AddRange(plan.Bases.Select(b => b.GetUniqueLoadID()));
                RemovedLoadIds.AddRange(plan.Pawns.Select(p => p.GetUniqueLoadID()));
                RemovedLoadIds.AddRange(plan.Quests.Select(q => q.GetUniqueLoadID()));
                RemovedLoadIds.AddRange(removedMaps.Select(m => m.GetUniqueLoadID()));
                errors = new List<string>();
                Mutated = true;
                RemoveArchives(plan);
                foreach (var quest in plan.Quests)
                {
                    quest.CleanupQuestParts();
                    MultiplayerAsyncQuest.TryRemoveCachedQuest(quest);
                    Find.QuestManager.Remove(quest);
                }
                foreach (var map in removedMaps)
                {
                    var async = Mp.game.asyncTimeComps.First(c => c.map == map);
                    MultiplayerAsyncQuest.TryRemoveCachedMap(async);
                    // Vanilla only notifies the currently installed storyteller. MP has one per faction.
                    foreach (var entry in Mp.WorldComp.factionData)
                        entry.Value.storyteller.incidentQueue.Notify_MapRemoved(map);
                }
                foreach (var settlement in plan.Bases)
                {
                    if (!Find.WorldObjects.AllWorldObjects.Contains(settlement)) continue;
                    var async = settlement.Map == null ? null : Mp.game.asyncTimeComps.First(c => c.map == settlement.Map);
                    async?.PreContext();
                    try
                    {
                        if (settlement.Map != null)
                            foreach (var pawn in settlement.Map.mapPawns.AllPawns.ToArray())
                                if (pawn.HostFaction == plan.Target && pawn.Faction != plan.Target) pawn.guest.SetGuestStatus(null);
                        settlement.Destroy();
                    }
                    finally { async?.PostContext(); }
                }
                foreach (var pawn in plan.Pawns)
                {
                    if (pawn.Discarded) continue;
                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                    else
                    {
                        if (pawn.Corpse != null && !pawn.Corpse.Destroyed) pawn.Corpse.Destroy(DestroyMode.Vanish);
                        if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                        if (!pawn.Discarded) pawn.Discard(true);
                    }
                }
                // Use the game's removal notifications: relations, logs, quests, ideologies,
                // tales, reservations, building caches and map events. Removal requires temporary=true.
                plan.Target.temporary = true;
                AccessTools.Method(typeof(FactionManager), "Remove", new[] { typeof(Faction) }).Invoke(Find.FactionManager, new object[] { plan.Target });
                RemoveDiplomacyRecords(plan.Target);
                // Native map/quest callbacks can produce letters after the first cleanup.
                RemoveArchives(plan);
                Mp.WorldComp.factionData.Remove(id);
                var retainedArchive = new HashSet<IArchivable>();
                foreach (var data in Mp.WorldComp.factionData.Values)
                    retainedArchive.UnionWith(data.history.archive.ArchivablesListForReading);
                if (Find.LetterStack.LettersListForReading.Any(l => !retainedArchive.Contains(l)))
                    throw new InvalidOperationException("共享信件列表存在无档案引用，请恢复备份。");
                foreach (var comp in Mp.game.mapComps.ToArray())
                {
                    comp.factionData.Remove(id);
                    comp.customFactionData.Remove(id);
                    if (removedMaps.Contains(comp.map)) Mp.game.mapComps.Remove(comp);
                }
                foreach (var comp in Mp.game.asyncTimeComps.ToArray())
                    if (removedMaps.Contains(comp.map)) Mp.game.asyncTimeComps.Remove(comp);
                // Factions share the cross-reference table across maps; native removal doesn't unregister it.
                AccessTools.Method(Mp.game.sharedCrossRefs.GetType(), "Unregister").Invoke(Mp.game.sharedCrossRefs, new object[] { plan.Target });
                if (Find.FactionManager.GetById(id) != null || Find.WorldObjects.AllWorldObjects.Any(w => w.Faction == plan.Target)
                    || Find.Maps.Any(m => removedMaps.Contains(m)) || Mp.WorldComp.factionData.ContainsKey(id)
                    || PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Any(p => p.Faction == plan.Target))
                    throw new InvalidOperationException("清理后的核心状态检查未通过。");
                if (errors.Count > 0) throw new InvalidOperationException("原版/模组清理回调产生错误：" + errors[0]);
                LastResult = "已清理派系 " + plan.Target.Name + " [" + id + "]：" + plan.Bases.Count + " 个基地、"
                    + plan.Pawns.Count + " 个人物、" + plan.Quests.Count + " 个关联任务。";
                Log.Message("[Meow.FactionPurge] EXECUTED faction=" + id + " bases=" + plan.Bases.Count + " pawns=" + plan.Pawns.Count + " quests=" + plan.Quests.Count);
            }
            catch (Exception ex)
            {
                Failed = true;
                LastResult = (Mutated ? "清理过程中发生错误。请勿继续使用当前世界或覆盖原存档；退出后恢复 Before-FactionPurge 备份。\n" : "检查失败，未执行清理：\n") + ex.Message;
                Log.Error("[Meow.FactionPurge] FAILED; restore backup. " + ex);
            }
            finally
            {
                errors = null;
                if (Mutated)
                {
                    Mp.AsyncWorldTime.DesiredTimeSpeed = TimeSpeed.Paused;
                    foreach (var comp in Mp.game.asyncTimeComps) comp.DesiredTimeSpeed = TimeSpeed.Paused;
                }
            }
        }

        private static void RemoveArchives(PurgePlan plan)
        {
                foreach (var entry in Mp.WorldComp.factionData)
                {
                    var archive = entry.Value.history.archive;
                    foreach (var item in archive.ArchivablesListForReading.ToArray())
                    {
                        // Removing a faction's History also removes its entire archive. The active
                        // LetterStack is shared across factions and must not retain those references.
                        if (entry.Key != plan.Target.loadID && !PurgePlan.Touches(item, plan)) continue;
                        if (item is Letter letter && Find.LetterStack.LettersListForReading.Contains(letter)) Find.LetterStack.RemoveLetter(letter);
                        RemovedLoadIds.Add(item.GetUniqueLoadID());
                        archive.Remove(item);
                    }
                }
        }

        private static void RemoveDiplomacyRecords(Faction faction)
        {
            foreach (var component in Current.Game.components)
            {
                if (component.GetType().FullName != "Meow.FactionDiplomacy.FactionDiplomacyState") continue;
                foreach (var name in new[] { "records", "recovery" })
                {
                    var list = (IList)AccessTools.Field(component.GetType(), name).GetValue(component);
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var record = list[i];
                        if (record != null && record.GetType().GetFields().Any(f => f.FieldType == typeof(Faction) && ReferenceEquals(f.GetValue(record), faction)))
                            list.RemoveAt(i);
                    }
                }
            }
        }

        internal static void VerifySerializedReferences(XmlDocument doc)
        {
            var ids = new HashSet<string>(RemovedLoadIds);
            var leftovers = new List<string>();
            foreach (XmlNode node in doc.SelectNodes("//*[not(*)]"))
                if (ids.Contains(node.InnerText)) leftovers.Add(node.ParentNode.Name + "/" + node.Name + "=" + node.InnerText);
            if (leftovers.Count > 0) throw new InvalidOperationException("仍有已删除对象的存档引用，拒绝输出清理后存档：" + string.Join("; ", leftovers.Take(12)));
        }

        internal static TempGameData SnapshotChecked()
        {
            var previous = errors;
            errors = new List<string>();
            try
            {
                var snapshot = SaveLoad.SaveGameData();
                if (errors.Count != 0) throw new InvalidOperationException("存档序列化出现警告或错误：" + errors[0]);
                return snapshot;
            }
            finally { errors = previous; }
        }

        internal static void WriteCheckedReplay(FileInfo file, TempGameData data)
        {
            if (file.Exists) throw new IOException("拒绝覆盖已有存档：" + file.Name);
            var temporary = new FileInfo(file.FullName + ".pending");
            if (temporary.Exists) throw new IOException("临时存档已存在：" + temporary.Name);
            Replay.ForSaving(temporary).WriteData(SaveLoad.CreateGameDataSnapshot(data, false));
            temporary.Refresh();
            var replay = Replay.ForLoading(temporary);
            if (!replay.LoadInfo()) throw new IOException("MP 存档索引校验失败。");
            var loaded = replay.LoadGameData(0);
            if (loaded.GameData == null || loaded.GameData.Length == 0) throw new IOException("MP 世界存档为空。");
            temporary.MoveTo(file.FullName);
        }

        internal static void Show(string text) => Find.WindowStack.Add(new Dialog_MessageBox(text));
    }

    internal sealed class ConfirmPurgeWindow : Window
    {
        private readonly PurgePlan plan;
        private string typed = "";
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(650, 480);
        public ConfirmPurgeWindow(PurgePlan plan) { this.plan = plan; doCloseX = true; absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect rect)
        {
            string summary = "彻底清理玩家派系\n\n" + plan.Target.Name + " [Faction " + plan.Target.loadID + "]\n"
                + string.Join("\n", plan.Bases.Select(b => b.Label + " / worldID=" + b.ID))
                + "\n人物：" + plan.Pawns.Count + "；关联任务：" + plan.Quests.Count
                + "\n" + string.Join("\n", plan.Quests.Select(q => q.name))
                + "\n\n将删除整个派系、以上所有基地地图、其人物、关联任务及 MP 派系管理数据。其他派系的文化可能共用，文化定义将保留。"
                + "\n执行前自动保存 MP 备份。完成后请从新存档重新开房。\n\n请输入派系数字 ID 确认：";
            var view = new Rect(0, 0, rect.width - 20, Math.Max(280, Text.CalcHeight(summary, rect.width - 20)));
            Widgets.BeginScrollView(new Rect(0, 0, rect.width, 280), ref scroll, view);
            Widgets.Label(view, summary);
            Widgets.EndScrollView();
            typed = Widgets.TextField(new Rect(0, 285, 220, 32), typed);
            if (Widgets.ButtonText(new Rect(0, 350, 250, 40), "备份并清理") && typed == plan.Target.loadID.ToString())
            { Close(); PurgeCommand.Request(plan.Target.loadID, plan.Fingerprint); }
        }
    }

    public sealed class PurgeCompletion : GameComponent
    {
        public PurgeCompletion(Game game) { }
        public override void GameComponentUpdate()
        {
            PurgeCommand.DispatchQueued();
            if (!PurgeCommand.Pending || PurgeCommand.LastResult == null || !MP.IsHosting) return;
            PurgeCommand.Pending = false;
            Log.Message("[Meow.FactionPurge] COMPLETION queued");
            string message = PurgeCommand.LastResult;
            PurgeCommand.LastResult = null;
            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    if (!PurgeCommand.Failed && PurgeCommand.Mutated)
                    {
                        Log.Message("[Meow.FactionPurge] OUTPUT snapshot begin");
                        var data = PurgeCommand.SnapshotChecked();
                        Log.Message("[Meow.FactionPurge] OUTPUT snapshot ready");
                        PurgeCommand.VerifySerializedReferences(data.SaveData);
                        var name = "After-FactionPurge-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                        var file = new FileInfo(Path.Combine(Mp.ReplaysDir, name + ".zip"));
                        PurgeCommand.WriteCheckedReplay(file, data);
                        Log.Message("[Meow.FactionPurge] OUTPUT saved " + file.FullName);
                        message += "\n新存档：" + file.FullName + "\n请退出并从这个新存档重新开房，再让其他玩家加入。";
                    }
                }
                catch (Exception ex) { message = "清理后验证或保存失败。请退出并恢复清理前备份，不要继续使用当前世界。\n" + ex.Message; Log.Error("[Meow.FactionPurge] Output failed: " + ex); }
                PurgeCommand.Show(message + "\n清理前备份：" + PurgeCommand.BackupPath);
            }, "MpSaving", false, null);
        }
    }
}
