# Desync-513/514/515/516 分析：与派系交易状态下的 Axolotl 征召/工作分歧（2026-08-20）

## 结论

513–516 是同一类掉线：**同一个 Axolotl 殖民者（`Axolotl1028470`，515 里是
`Axolotl1124`）在两端的征召（Drafted）状态不同步**，导致只有一端的小人走
“已征召”思维树、在模拟中多分配一个 JobID，而另一端同一 tick 仍在正常执行
Anomaly 建筑（`Building_HoldingPlatform` / `HoldingSpot`）的 MTB 随机数。

这不是地图 Rand 分叉：两端 `rngState` 序列完全一致，只是被多出来的那次
`UniqueIDsManager.GetNextJobID` 在 trace 序列里错位了一个索引。真正分叉的是
`Pawn_DraftController.draftedInt` / 工作状态，进而让 JobID 计数器两端永久差 1。

上游直接证据：

- 515 本地第一个分叉 trace：`JobGiver_Orders.TryGiveJob -> JobMaker.MakeJob(Wait_Combat, pawn.Position)`
  —— 原版 `JobGiver_Orders` **只在 `pawn.Drafted == true` 时**返回 `Wait_Combat`，
  因此该端小人处于“已征召”状态。
- 513/514/516 的第一个分叉 trace：`Pawn_JobTracker.EndCurrentJob -> JobMaker.MakeJob(JobDef,int,bool)`
  （`Wait_MaintainPosture`/`Wait`，即该端小人先结束了当前工作并立刻开新工作，消耗一个 JobID），
  对端同一 tick 是 `HoldingPlatform/HoldingSpot.Tick` 的 MTB 随机数。
- 同一小人（`Axolotl1028470`）在 513/514/516 反复作为分叉对象出现；每次重连后
  ~5–17k tick 再次复发 —— 说明“征召状态只在一端”的差异在每次重连后都会被
  重新制造出来，而不是从旧存档带进来的。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-20-513-516-faction-trade/`
- 包 SHA-256：
  - `Desync-513.zip` `9FF6BB74ADC7C32034B154CB56B19D1187C1001EEA64EAFEE64479E49343A534`
  - `Desync-514.zip` `8D6BE0D77632B60837CEBC84300B1DC80544BF38246B72FCE1F262CFD57E02AC`
  - `Desync-515.zip` `E1FA1DA000175DA47403F7D5276988474ACCC3C81BD95A666E68144A17DA0671`
  - `Desync-516.zip` `9ACCDB0CBF0AFCC872C6E3374D9AF5A7A1CC09D4131C265C95E8FD29D1322409`
- Multiplayer `0.11.5+a481546`；RimWorld `1.6.4850 rev646`；async time 开、multifaction 开、
  2 名玩家；513/514 为 2 张地图，515/516 为 3 张地图。
- 运行程序集：`1.6/Assemblies/MP_MeowOnlineShop.dll`（3.0.122）
  SHA-256 `17C1370D110F1682D7B94C8FEA7C020951154CA54E5D7FCFB73A64EA96ACEEAD`。

## 每包首个分叉

| 包 | 最后有效 tick | 掉线 tick | 首个分叉（host / local） |
|---|---:|---:|---|
| 513 | 8899021 | 8899091 | host `Axolotl1028470 GetNextJobID(EndCurrentJob)` / local `HoldingPlatform MTB` |
| 514 | 8905381 | 8905440 | host `HoldingPlatform MTB` / local `Axolotl1028470 GetNextJobID(EndCurrentJob)` |
| 515 | 8910901 | 8910963 | host `HoldingSpot MTB` / local `Axolotl1124 GetNextJobID(JobGiver_Orders -> Wait_Combat)` |
| 516 | 8923051 | 8923093+ | host `HoldingPlatform MTB` / local `Axolotl1028470 GetNextJobID(EndCurrentJob)` |

513–515 是同一会话连续三次（每次掉线后重连继续），516 是其后的再一次。
local_logs 显示每次掉线后立即 `Initial sync opinions: ...` 重连。

## 根因：穿梭机卸货自动征召的延迟队列存在 `Faction.OfPlayer` 相关门控

这类“征召/工作分歧 + JobID 分叉”在本项目已有两次记录：

- Desync-259（Odyssey 穿梭机下机）：`ShipJob_Unload.UnloadThingFromShuttle`
  在原版里会立即 `pawn.drafter.Drafted = true`，而穿梭机在世界时间域 tick、
  目标地图在自己的地图时间域 tick（async time）。世界域执行征召 setter 会让
  “结束当前工作 + 选下一个工作”发生在错误的 Rand/UniqueID 上下文里。
  `Patch_TransportShipUnloadMp` 因此把自动征召延迟到目标地图下一次
  `MapPreTick` 再重放。
- Desync-141/142、153/154/155/156：`CaravanArrivalAction_VisitSite.DoEnter` /
  `CaravanArrivalAction_Enter.Arrived` 用 `mapParent.Faction.HostileTo(Faction.OfPlayer)`
  决定 `draftColonists`，multifaction 下 `Faction.OfPlayer` 可能在某一端是
  spectator，导致一端征召、另一端不征召（`JobGiver_Orders`(已征召) vs
  `JobGiver_OptimizeApparel`(未征召)）。

513–516 复现的是 259 那条路径，但卡在一个新洞上：

`Patch_TransportShipUnloadMp.UnloadPrefix` 用 `pawn.IsPlayerControlled` 决定是否把
小人放进延迟征召队列，而 `IsPlayerControlled` 是 `Faction.OfPlayer` 相对判断。
multifaction/async 会话里如果卸货那一瞬间某一端的 `Faction.OfPlayer` 是
spectator，就会出现：

- 一端 `IsPlayerControlled == true`：入队 → `MapPreTick` 重放完整 setter
  （清队列、`EndCurrentJob`、选新工作）→ 该端小人被征召；
- 另一端 `IsPlayerControlled == false`：不入队 → 原版 setter 因同一原因
  也不被调用 → 该端小人从未被征召。

于是同一小人两端征召状态永久不一致；被征召端每隔一段时间就由思维树
（`JobGiver_Orders`/`EndCurrentJob`）多分配一个 JobID，最终触发
`Trace hashes don't match`。515 的 local JIT 里掉线前 8 tick 还有穿梭机
gizmo/卸载活动（`CompTransporter.CancelLoad`、`ShipJob_Unload`），与
“派系交易 + 穿梭机/运输舱往返”的触发场景一致。

## 修复（仅源码，未部署、未测试）

`Source/MP_MeowOnlineShop/Patch_TransportShipUnloadMp.cs`：

1. 入队判据改为确定性判据 `IsDeferredPlayerPawn`：
   `pawn.Faction?.def?.isPlayer == true && pawn.drafter != null`，
   不再使用 `pawn.IsPlayerControlled`（去掉 `Faction.OfPlayer` 依赖）。
2. 入队增加 async-time 门控（与 `DraftedPrefix` 一致）：async time 关闭时
   原版 setter 立即执行，不再入队，避免 MapPreTick 二次征召。
3. `UnloadFinalizer` 在成功下机后对所有已入队小人做**确定性镜像**
   （`draftedInt = true`），不依赖原版 `IsPlayerControlled`/`IsPlayerHome`
   是否在某一端提前返回；完整 setter 副作用仍只在目标地图下一次
   `MapPreTick` 按 thingID 稳定顺序重放一次。
4. `MapPreTickPostfix` 的重放过滤同步换成 `IsDeferredPlayerPawn`，保证重放集合
   也两端一致。

所有分支 fail-open；不新增同步命令；单机行为不变。

## 构建状态

- `dotnet build -c Release -p:OutputPath=.../build-check`：0 警告、0 错误。
- 未复制任何 DLL 到 `1.6/Assemblies`；已部署 DLL 时间戳/哈希未变
  （`17C1370D...`）。
- 修改后源码 SHA-256：`6B57DB5CEA73CDAA4A66E2D8E2E70D41F8669697053EEF87DB2FE529708C8C7E`。
- 修改前备份：`TestValidation/DesyncEvidence/2026-08-20-513-516-faction-trade/Patch_TransportShipUnloadMp.cs.before`。

## 剩余风险与验证建议

- 如果 513/514/516 的征召差异实际来自另一条 `Faction.OfPlayer` 相对路径
  （例如 PerspectiveShift 的 seek-at-will，见 Desync-490），本补丁只能覆盖
  穿梭机卸货这条路径。建议下一次复现时：
  1. host/client 都部署本候选；打开交易窗口并执行“穿梭机/运输舱抵达派系定居点
     后下机 + 交易”的复现路径；
  2. 确认两端日志都出现一次
     `[MP-MeowOnlineShop] Replayed deferred transport-ship auto-draft at MapPreTick`
     （此前只在单端出现/完全不出现）；
  3. 确认 `Axolotl1028470` 不再单独在某一端进入 `JobGiver_Orders/Wait_Combat`
     或 `EndCurrentJob` 分叉；
  4. 若仍出现 515 那种无穿梭机 JIT 痕迹的 `Wait_Combat` 分叉，再排查
     PerspectiveShift seek-at-will / 手动征召同步路径。
- 未运行 host/client smoke/soak；需要按 runtime-testing 门禁做 targeted 复现。
