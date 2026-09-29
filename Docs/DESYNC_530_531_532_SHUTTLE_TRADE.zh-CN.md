# Desync-530/531/532 分析：穿梭机移动 + 派系交易 → 异步世界 tick 暂停不对称 / 穿梭机内容分叉（2026-08-20）

## 结论

530/531/532 是**同一会话**（`9146739` 重连后）连续三次掉线，触发场景与用户描述一致：
**派系定居点交易 + 穿梭机移动**。三次掉线机制不同但同源：

- **530（`Wrong random state for the world`）**：证据确凿——在同一 MP tick（t=9158365），
  **local 执行了一次世界 tick**（`GameComponent_Anomaly.GameComponentTick -> Rand.Range`，
  消耗世界 Rand），而 **host 完全没执行世界 tick**（host 的 trace 窗口内 0 个世界域事件，
  全是地图 BT7724 的 MTB）。即 host 的异步世界 tickable 速率 = 0（世界被暂停），local = 1。
  host 在 t=9153244–9154491 执行了一次**派系定居点交易**（`MpTradeSession.TryExecute`，
  negotiator 是车队/世界小人 → `MpTradeSession.Map == null` → `IsCurrentlyPausing(null)==true`
  → **暂停 host 的世界 tick**），随后 t=9155059 `CaravanShuttleUtility.LaunchShuttle`（装载并
  发射穿梭机）。**该暂停交易会话只在 host 一侧存在**（local 的 JIT 窗口内无任何
  `MpTradeSession` 活动），于是 host 世界暂停、local 世界继续跑，世界 Rand 状态分叉
  （local 比 host 多推进了若干世界 tick），最终在恢复后按“世界 Rand 状态不一致”报出。
- **531（`Trace hashes don't match`）**：同步命令 `CompTransporter.CancelLoad`（t=9164326）
  把穿梭机内容倒出来；前 12 个物品两端一致，第 13 个起**银币堆叠的 thingID 分叉**：
  host `Silver3069059/…060/…061/…` vs local `Silver3069075/…076/…077`（差 16 个 ID），
  且 local 还多出旧的 `Silver3067823`、器官、基因包。即**穿梭机装载时银币堆叠的切分
  两端不同**（`LoadCaravanItemsIntoContainer` / 搬运按 `FirstUnloadableThing` 顺序
  `TryTransferToContainer` 切分），源头是交易后车队/库存的银币堆叠/顺序两端不一致。
- **532（`Trace hashes don't match`）**：`DropShuttle` 落地（t=9193177）时两端在同一索引
  分叉——host 先 `Thing.SplitOff -> GetNextThingID`（倒出时切分堆叠），local 先
  `TransportShip.ArriveAt -> ShipJobMaker.MakeShipJob -> GetNextShipJobID`，且生成的
  `PassengerShuttleIncoming` thingID 也差 2。是 531 穿梭机内容分叉的下游表现。

共同根因：**派系交易会话在两端不对称 + 穿梭机装载切分两端不一致**，在穿梭机
（装载/取消/落地）路径上被放大成世界 Rand 或地图 Rand 分叉。**上一轮补丁已部署
（19:34:55）但仍掉线**，说明 530 的“一端暂停世界”并非单纯 tradeables 不一致，而更可能
是**会话存在性/类型在两端不对称**（例如一端是定居点交易=暂停世界，另一端无会话或
会话类型不同），以及 531/532 的**穿梭机装载堆叠切分**路径，二者都超出了上一轮
“交易物对齐”的覆盖范围。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-20-530-531-532-shuttle-trade/`
- `Desync-530.zip` SHA-256 `6B6C60858C6A4B77A0437E63D2F6DEFB23CC0BAB2A47B6B62ADC48A7617DDCF2`
- `Desync-531.zip` SHA-256 `F2C2F1357E8D688F34C6F7D9412C8699462B17755F2D0431916459A9C3418F06`
- `Desync-532.zip` SHA-256 `E061ECA7F15CB324009C0C2BD4AB83AF2F51CD55B0C5601AFF853C27D0CB0A8A`
- Multiplayer `0.11.5+a481546`；RimWorld `1.6.4850 rev646`；async time 开、multifaction 开、
  2 名玩家、2 张地图。运行程序集 `1.6/Assemblies/MP_MeowOnlineShop.dll`（2026/8/20 19:34:55 部署，
  SHA-256 `3A053E9E…`）**已包含上一轮补丁**（`Patch_TradeSessionRejoinMp.TryCreatePostfix`/
  `TryExecuteReconcilePrefix` 与 `Patch_JobEndDiagnostic`，日志确认 boundaries=5 active）。
  因此 530/531/532 证明上一轮“交易会话 tradeables 对齐”补丁**不足以**阻止穿梭机路径掉线。

## 每个包的首个分叉

| 包 | 最后有效 tick | 首个分叉（host / local） |
|---|---:|---|
| 530 | 9158311 | host `HoldingSpot2546945 MTB`（地图域）/ local `GameComponent_Anomaly.GameComponentTick -> Rand.Range`（世界域）；host 窗口内 0 个世界域事件 |
| 531 | 9164281 | `CompTransporter.CancelLoad` 倒货：host `Silver3069059` / local `Silver3069075`（差 16 个 thingID），随后 local 出现旧银币/器官/基因包 |
| 532 | 9193141 | `DropShuttle`：host `GetNextThingID(SplitOff)` / local `GetNextShipJobID(MakeShipJob)`；`PassengerShuttleIncoming` thingID 差 2 |

## 时间线（同一会话）

- `t≈9146739` 重连。
- `t≈9153244–9154491`：host 打开并执行派系定居点交易（`MpTradeSession` 计数命令、
  `TryExecute`、`RemoveTradeSession`）；local JIT 窗口内无任何交易活动（一端会话）。
- `t≈9155059`：host `CaravanShuttleUtility.LaunchShuttle`（把车队物品装进穿梭机并发射）。
- `t≈9158365`：**530 首个分叉**（local 世界 tick vs host 地图 tick）；`t≈9158370` 报
  `Wrong random state for the world`。
- 重连后 `t≈9163016`：host 处理交易计数命令（`TradeSessionGetTransferablePostfix`），
  local 无；`t≈9163664` 两端都执行 `DropShuttle`（落地）；`t≈9164326` 两端执行
  `CancelLoad`（531 分叉）。
- 重连后 `t≈9193177`：532 `DropShuttle` 分叉；`t≈9193203` 报掉线。

## 根因

1. **530（世界 tick 暂停不对称）**：`AsyncWorldTimeComp.TickRateMultiplier` 在
   `Multiplayer.WorldComp.sessionManager.IsAnySessionCurrentlyPausing(null)` 为真时返回 0
   （世界暂停）。派系定居点交易的 negotiator 是车队小人（世界小人），
   `MpTradeSession.Map == null`，因此该会话**只暂停世界、不停地图**。当该会话只在
   host 存在（一端会话，与 519/520/522 同源），host 的世界 tickable 被跳过而 local 继续跑，
   世界 Rand 状态分叉 → `Wrong random state for the world`。
2. **531/532（穿梭机内容分叉）**：交易把银币/器官等放进车队（两端交易物/库存堆叠
   不一致），随后 `CaravanShuttleUtility.LaunchShuttle -> LoadCaravanItemsIntoContainer`
   按 `FirstUnloadableThing` 顺序把物品转移进穿梭机并切分堆叠（`TryTransferToContainer`
   -> `SplitOff` 分配 thingID）。两端车队库存堆叠/顺序不同 → 切分不同 → 穿梭机内容
   差 16 个 thingID → `CancelLoad`/`DropShuttle` 倒出/落地时地图 Rand 分叉。

## 修复（仅源码，未部署、未测试）

1. `Patch_TradeSessionRejoinMp.cs`（上一轮）保留：`TryCreate` 冲突时对既有会话执行
   `Recache` + 计数归零；`TryExecute` 前缀在库存为空时强制确定性重建。注意：该补丁
   已被用户部署（19:34:55）并随 530/531/532 一起运行，未能阻止本批掉线；本轮在其上
   叠加以下两点，并新增诊断以确认 530 的一端世界暂停到底来自哪个会话。
2. **新增（本轮）**：`TryCreate` 冲突对齐时，把既有会话的 `trader`/`playerNegotiator`
   字段也设成请求的值——`MpTradeSession.Map` 由 negotiator 决定，settlement 交易
   （世界小人 negotiator）会暂停世界 tick；对齐后两端的“世界暂停”行为一致，
   直接针对 530 的一端世界暂停。
3. **新增 `Patch_WorldPauseDiagnostic.cs`**（`MP_WORLD_PAUSE_DIAG=1`，最多 8 次）：
   每次异步世界 tick 因会话暂停时打印 `worldPaused=true` + 会话列表 + 每个会话的
   `IsCurrentlyPausing(null)`，下次复现可直接对比两端是哪个会话在一端暂停世界。

所有新钩子 fail-open、仅 MP 生效、不新增同步命令、不改单机行为。

## 构建状态

- `dotnet build -c Release -p:OutputPath=TestValidation\CompileCheck_20260820_530_532`：
  0 警告、0 错误。
- 候选 DLL SHA-256 `0AC5F98E01B3FEC090A93032BA74088D5C9A43F2403AA0218F24AF5F4B3E9193`
  （ilspycmd 已确认 `Patch_WorldPauseDiagnostic.TickRateMultiplierPrefix`、
  `Patch_TradeSessionRejoinMp.TryCreatePostfix/TryExecuteReconcilePrefix` 均注册）。
- 未复制任何 DLL 到 `1.6/Assemblies`；已部署 DLL 未变（`3FF71FFC…`）。

## 剩余风险与验证建议

- 530 的“一端世界暂停会话”具体是哪个会话、为何只在一端，包内无法最终确认（local
  JIT 窗口从 t=9154851 开始，晚于交易窗口）。建议下次复现 host/client 都设
  `MP_WORLD_PAUSE_DIAG=1` + `MP_TRADE_DIAG=1` + `MP_JOB_END_DIAG=1`，对比
  `[MP-WORLD-PAUSE-DIAG] worldPaused=true …` 行确认哪端有、是哪个会话。
- 531/532 的银币堆叠切分分叉源头在“交易后车队库存堆叠/顺序”，本补丁通过交易会话
  对齐间接覆盖；若重连后无交易、仅穿梭机装载仍复现，则需检查
  `LoadCaravanItemsIntoContainer`/搬运的 `FirstUnloadableThing` 顺序确定化。
- 未运行 host/client smoke/soak；需按 runtime-testing 门禁做 targeted 复现
  （派系定居点交易 + 穿梭机装载/发射/落地 + 取消装载）。
