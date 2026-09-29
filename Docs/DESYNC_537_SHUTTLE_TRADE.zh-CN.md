# Desync-537 分析：穿梭机到定居点交易 → 世界 tick 暂停不对称（2026-08-20）

## 结论

537 与 530 同属一类：**穿梭机把车队送到派系定居点并打开交易窗口时，暂停世界的交易
会话只在 host 一侧生效，导致 host 的世界 tick 停止、local 继续跑，世界 Rand 分叉**
（`Wrong random state for the world`）。同时两端 `UniqueIDsManager` 计数漂移（本包差
**26 个 ID**），在地图上的 Anomaly 实体（`TunnelHiveSpawner`/`Sightstealer`）身上显形。

537 是 530/531/532 所在的**同一会话**的第 8 次掉线（日志可见 530~537 全部记录，
每隔 ~2–4 万 tick 掉一次，类型在“世界 Rand”“地图 Rand”“地图 1 Rand”之间轮换），
证明这是一个**反复被重新制造**的漂移，而不是一次性状态。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-20-537-shuttle-trade/`
- `Desync-537.zip` SHA-256 `049957F3E46F27F25012FA84089409A21CA3CC537DC1949FC0360D0E926A7530`
- Multiplayer `0.11.5+a481546`；RimWorld `1.6.4850 rev646`；async time 开、multifaction 开、
  2 名玩家、2 张地图。
- 运行程序集：`1.6/Assemblies/MP_MeowOnlineShop.dll`（2026/8/20 19:34:55，SHA `3A053E9E…`，
  含上一轮 `TryCreatePostfix/TryExecuteReconcilePrefix` 与 `Patch_JobEndDiagnostic`，
  **不含**本轮新增的 trader/negotiator 对齐与 `Patch_WorldPauseDiagnostic`）。

## 首个分叉（idx 9，t=9340951）

- host `TunnelHiveSpawner3187986` / local `TunnelHiveSpawner3187960`（**thingID 差 26**）；
  随后整组 `TunnelHiveSpawner3187986…3187993` / `3187960…3187967` 与
  `Sightstealer3187994` / `3187968` 全部差 26。
- 两端地图 Rand 完全一致（每个事件的 rngState 相同）、map-local tick 相同（21807242）；
  只有实体 thingID 分叉 —— 说明这是 **UniqueIDsManager 计数漂移**（在本组实体生成前
  已差 26），地图 Rand 本身未分叉。
- 掉线类型 `Wrong random state for the world`：**世界 Rand 分叉**（在 trace 窗口之外，
  本窗口只有地图事件）。

## 触发上下文（host jitted）

- `t≈9337183–9337225`：`ChooseWhereToLand…GetShuttleFloatMenuOptions`、
  `TransportersArrivalAction_Trade..ctor` —— 玩家用穿梭机选择“与定居点交易”。
- `t≈9337479`：`CaravanVisitUtility.TradeCommand -> ReopenTradingWindowLocally`、
  `CanLaunchCaravanShuttle`、`FuelInCaravan`、`TimeIndicateBlockingPause` —— **打开交易
  窗口**（并出现“阻塞暂停”指示）。
- `t≈9338719`：`Dialog_Trade.DoWindowContents` / `FixAcceptOverTraderSilver` —— 交易 UI。
- `t≈9340587`：`Dialog_Trade.FlashSilver` —— 交易因银币不足被拒（窗口仍开着）；
  `t≈9340985` 报 `Wrong random state for the world`。

与 530 完全一致：**host 打开派系定居点交易窗口期间，世界 tick 停止（local 继续），
世界 Rand 分叉**。532（`DropShuttle`）、536（`Wrong random state on map 1`）、
531/533/534/535（地图 Rand）都是该漂移在不同路径上的显形。

## 根因

1. `AsyncWorldTimeComp.TickRateMultiplier` 在
   `Multiplayer.WorldComp.sessionManager.IsAnySessionCurrentlyPausing(null)` 为真时返回 0
   （世界暂停）。派系定居点交易的 negotiator 是车队（世界）小人 → `MpTradeSession.Map==null`
   → 只暂停世界、不停地图。
2. 该“暂停世界”的会话**只在 host 生效**（local 侧要么没有该会话、要么因冲突保留了
   negotiator/Map 不同的旧会话）→ host 世界停、local 世界继续跑 → 世界 tick 数不同 →
   世界 Rand 分叉；世界域操作分配 ID 的位置随之错位 → `UniqueIDsManager` 漂移（本包 26
   个 ID）→ 后续任何批量生成（Anomaly 蜂巢/掠视者、穿梭机倒货、交易银币切分）都以不同
   thingID 落在两端 → 地图 Rand 也最终分叉。
3. 每次重连会重新同步世界 Rand，但下一次“穿梭机+定居点交易”会再次制造同一不对称，
   所以 530~537 反复掉线。

## 修复（仅源码，未部署、未测试）

1. `Patch_TradeSessionRejoinMp.cs`：`TryCreate` 冲突时把既有会话的 `trader`/
   `playerNegotiator` 对齐为请求值（`MpTradeSession.Map` 由 negotiator 决定，settlement
   交易=世界小人 negotiator=暂停世界；对齐后两端“是否暂停世界”一致），随后 `Recache`
   + 计数归零；`TryExecute` 前缀在库存为空时强制确定性重建。
2. `Patch_WorldPauseDiagnostic.cs`（`MP_WORLD_PAUSE_DIAG=1`，最多 8 次）：每当异步世界
   tick 因会话暂停时打印 `worldPaused=true` + 每个会话的类型、`IsCurrentlyPausing(null)`、
   **会话 Map（地图 ID 或 world）**——下次复现可直接确认是哪一端、哪个会话（negotiator
   在世界还是地图）在暂停世界，从而验证“一端会话/negotiator 错配”假设。
3. `Patch_JobEndDiagnostic.cs`（`MP_JOB_END_DIAG=1`）：打印每个 `EndCurrentJob` 的小人/
   工作/世界 tick，辅助定位工作时序分叉。

全部 fail-open、仅 MP 生效、不新增同步命令、不改单机行为。

## 构建状态

- `dotnet build -c Release -p:OutputPath=TestValidation\CompileCheck_20260820_537`：
  0 警告、0 错误。
- 候选 DLL SHA-256 `8715F13AAC7AC848E2D593F43189C37BAB3155ACE354AA71AF37A8AAF5A094AE`
  （ilspycmd 确认 `TickRateMultiplierPrefix` 含会话 Map 输出；`TryCreatePostfix`/
  `TryExecuteReconcilePrefix` 均在）。
- 未复制任何 DLL 到 `1.6/Assemblies`；已部署 DLL 未变（`3A053E9E…`，19:34:55）。

## 剩余风险与验证建议

- “暂停世界的会话为何只在 host 生效”仍有两个候选：A) local 从未创建该会话；B) local
  因冲突保留了 negotiator/Map 不同的旧会话。本补丁直接修 B，并靠 `MP_WORLD_PAUSE_DIAG`
  区分 A/B。若下轮日志显示 A（local 无会话），需要继续检查交易开窗命令在 local 的
  `TryCreate` 为何未执行/失败。
- 531/532/537 的 ID 漂移源头在世界 tick 数分叉；世界 tick 对齐后应消失。若重连后无交易
  仍漂移，再单独检查 `LoadCaravanItemsIntoContainer` 装载顺序与 Anomaly 事件生成。
- 未运行 host/client smoke/soak；需按 runtime-testing 门禁做 targeted 复现。
