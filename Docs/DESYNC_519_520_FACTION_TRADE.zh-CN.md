# Desync-519/520/522 分析：派系交易/战斗状态下的小人工作/JobID 分叉（2026-08-20）

## 结论

519/520/522 与 513/514/516 是**同一类掉线**（同一个会话连续掉线，522 是重连后第 4 次掉线）：某个在地图上的动物小人（519 是
`GuineaPig3026129`，520 是 `Raccoon3028033/3028040`）在一端执行
`Pawn_JobTracker.EndCurrentJob(JobCondition.Succeeded)`（走完一个“走路/移动”
类工作，经 `Notify_PatherArrived -> EndJobWith -> EndCurrentJob`），多分配一个
`UniqueIDsManager.GetNextJobID`，把该地图的 Rand 流整体错位一个索引；另一端同一
tick 对应的事件是 `HoldingSpot/HoldingPlatform/FleshmassNucleus` 的 MTB 随机数。

**关键事实：513/516 的穿梭机卸货自动征召修复已经部署在运行程序集里
（`IsDeferredPlayerPawn` 存在），但 519/520 仍然以完全相同的工作结束签名掉线。**
因此该修复并没有覆盖这类“派系交易”掉线的真正路径；真正的问题在 Multiplayer 的
`MpTradeSession`/`MpTradeDeal` 交易会话状态两端不一致（交易物列表的成员/thingID/
计数分叉），`TryExecute` 时两端执行了不同的转移，导致被交易小人的工作/派系状态
两端不同，若干 tick 后某个走路工作只在一端自然结束，消耗一个额外 JobID，最终
`Trace hashes don't match`。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-20-519-520-faction-trade/`（含 519/520/522 三个 zip 与分析脚本）
- `Desync-519.zip` SHA-256 `9AD2A15ECEB43DC78A11FB421D0C7B3039B1C947B38B07692D1BC42AD2F1AC63`
- `Desync-520.zip` SHA-256 `2C9BF461058A9F1C43D7A61C0517E3C4602501E5EE41424691244E8F57C0E483`
- `Desync-522.zip` SHA-256 `0F3AFACAEBC1F92F2235A2714718985172CE30022CDEFAFDEAA0671CF4DCC003`
- Multiplayer `0.11.5+a481546`；RimWorld `1.6.4850 rev646`；async time 开、
  multifaction 开、2 名玩家、2 张地图。
- 运行程序集：`1.6/Assemblies/MP_MeowOnlineShop.dll`
  `3.0.121-ion-rifle-explosive-fix`，SHA-256 `3FF71FFCD7C…`（含
  `Patch_TransportShipUnloadMp.IsDeferredPlayerPawn`、`Patch_TraderStockDeterminism`、
  `Patch_TradeSessionRejoinMp`；日志确认这些补丁均 active）。

## 每个包的首个分叉

| 包 | 最后有效 tick | 首个分叉（host / local） |
|---|---:|---|
| 519 | 8969941 | host `HoldingSpot2546945 MTB` / local `GuineaPig3026129 GetNextJobID(EndCurrentJob->Wait_MaintainPosture)` |
| 520 | 8980921 | idx386 bullet `MiliraBullet…3028044`(host) vs `…3028051`(local) 先差 7 个 ID；随后 host `Raccoon3028033 GetNextJobID(EndCurrentJob)` / local `HoldingSpot MTB` |
| 522 | 9025981 | local `Drone_Hunter3055912 GetNextJobID(EndCurrentJob -> JobGiver_AIGotoNearestHostile)` / host `HoldingSpot2546945 MTB`；同一 Drone 的 JobID 在 host 于下一个地图 tick（ml+1）才分配 |

520 的 trace 里同一个 Raccoon 在两端的 thingID 分别是 `3028033` 与 `3028040`
（相差 7），子弹也是相差 7 —— 说明**在 trace 窗口之前两端的 UniqueIDsManager
计数已经差了约 7**，这是更早发生的、只在一端消耗 ID 的延迟/惰性变异造成的。

## 时间线（519，同一次会话也覆盖 520 的第一段）

- `t≈8956321`：会话重连（`Initial sync opinions: 8956321...8956620`）。
- `t≈8967070`：**host** JIT `MpTradeSession..ctor/TryCreate/OpenWindow` +
  `Settlement_TraderTracker.RegenerateStock`（settlement 交易库存按确定性种子重新
  生成）；**local 的交易会话在此窗口之前就已存在**（两端 JIT 文件都是 1500 条上限，
  local 最早条目 t:8967070 是 `WorldObject.Destroy`，未见 ctor）——两端会话创建
  时刻不同。
- `t≈8967727`：两端执行 `MpTradeDeal.Recache -> TradeDeal.TryExecute ->
  GiveSoldThingToPlayer`（同步命令），即交易成交。
- `t≈8967883`：local 惰性访问 `StockListForReading` 重新生成 settlement 库存
  （比 host 晚 ~813 tick；内容受种子确定化补丁约束，但生成时刻/ID 分配时刻不同）。
- `t≈8969982`：首个 trace 分叉（GuineaPig 工作结束）。
- `t≈8970002`：Desync-519 写出；`t≈8969701` 重连，继续。
- 第二次会话：`t≈8980879` local JIT `Dialog_Trade.Close`（+Kiiro postfix），
  `t≈8980965-67` 分叉，`t≈8980980` Desync-520 写出。
- 之后会话继续多次掉线：`t≈8998593` 出现一次 `Wrong random state for the world`
  （世界 Rand 分叉，未写包）；重连后进入**大规模战斗**（RigorMortis 僵尸「谷雨」
  释放 `RM_ClawSkill/RM_ZombieScream`，Anomaly 哨戒/猎手/黄蜂无人机大量被摧毁），
  `t≈9026040` Desync-522 写出。522 的时间窗（JIT 1500 条）内**没有任何交易活动**。

## 根因：交易会话/交易物状态两端不一致（与 434/435/436 同源）

项目已有 `Patch_TradeSessionRejoinMp`（重连后会话保活 + `GetSessions` 兜底 +
空 `tradeables` 重建）与 `Patch_TraderStockDeterminism`（settlement 库存按
settlement.ID 稳定种子生成），但这些只处理了“会话找不到/交易物列表为空”的极端
情况。**如果一端是“新建会话”（host 在 8967070 重新 `TryCreate`，库存按当时
UniqueIDsManager 状态分配了新的 thingID），另一端是“重连恢复的旧会话”（其
`tradeables` 引用重连前库存的旧 thingID），则两端 `tradeables` 的成员/thingID 不同。**
`MpTradeSession.TryExecute`（同步方法）在这两端执行时：
- `deal.Recache()` 只能做“按当前 Goods/ColonyThingsWillingToBuy 增删”的增量修复，
  无法把一端旧会话的 thingID 对齐到另一端新库存的 thingID；
- 后续 count 命令（`GetTransferableByThingId`）按 thingID 命中不同/不存在的交易物；
- 最终 `ResolveTrade -> GiveSoldThingToPlayer/Trader` 两端转移不同的东西
  （例如一端把某动物卖给 settlement、另一端没卖），被交易小人的
  `PreTraded/SetFaction/ClearMind`/随从工作状态两端不同 → 稍后其走路工作只在一端
  结束（多一个 JobID）→ 地图 Rand 流错位。

这与 434/435/436 文档自己列的“剩余风险”一致：*若两端 thingID 计数在会话创建前
已经分叉，仅靠会话保活和 Recache 仍可能无法让 client 用 host 的 thingId 命中相同
Tradeable*。519/520 正是这条路径的复现：**一端新建会话（重新生成库存、分配新
ID），另一端沿用恢复的旧会话（旧 ID）**。

### 522 的单独说明（同类签名、战斗上下文、上游变异未定位）

522 是同一会话的后续掉线：首个分叉同样是“一端的小人 `EndCurrentJob` 多分配一个
JobID”，但小人换成了 Anomaly 猎手无人机（`Drone_Hunter3055912`），上下文是战斗
而非交易。核对过地图 tick：两端地图 X/Y 的 map-local tick 完全对齐（无 async
漂移），两端同一 Drone 的 JobID 分配相差 **1 个地图 tick**（local ml=20917699 / w=9026011，
host ml=20917700 / w=9026012）——与 520 的“Raccoon 工作晚一个世界 tick 结束”是同一
个模式。522 的时间窗只有 3 个世界 tick、41 条 trace，**上游工作状态变异在窗口之外**，
无法从包内直接定位到具体变异方法（不排除战斗类状态：RigorMortis 僵尸技能时序、
Anomaly 无人机唤醒/目标状态，或会话内更早累积的 ID/状态漂移）。因此本次**不臆造**
针对战斗路径的补丁，而是新增 `MP_JOB_END_DIAG=1` 门控诊断，下一次复现时两端日志会
直接打印每个 `EndCurrentJob` 的小人、工作 def、condition、worldTick 与地图，从而把
“哪个小人、哪个工作、差几个 tick”钉死，再定位上游变异。

## 修复（仅源码，未部署、未测试）

`Source/MP_MeowOnlineShop/Patch_TradeSessionRejoinMp.cs`：

1. **`MpTradeSession.TryCreate` 后置（冲突时对齐）**：当 `TryCreate` 因冲突返回
   null（该端已有同 trader/谈判者的会话）时，找到 `WorldComp.trading` 中已存在的
   冲突会话，把其 `deal.recacheColony/recacheTrader` 置真并执行一次
   `MpTradeDeal.Recache()`。由于 `Patch_TraderStockDeterminism.TradeSessionTryCreatePrefix`
   已在本命令 tick 两端用同一 settlement.ID 种子强制重建了库存，冲突端的旧会话
   会借此把 `tradeables` 迁移到**同一个命令 tick 生成的同一批 thingID**，与新建端
   对齐。
2. **`MpTradeSession.TryExecute` 前缀（库存为空的确定性重建）**：当会话的 trader
   是 `Settlement_TraderTracker` 且其 `stock` 为空（惰性重建的触发条件）时，在同步
   命令边界强制 `RegenerateStock`（种子确定），让两端在任何 `Recache` 之前拥有同一
   库存状态。
3. 沿用现有 `MP_TRADE_DIAG=1` 门控诊断（`TryExecute` 前打印每个 tradeable 的
   colony/trader thingID 与 count；`GiveSold*` 前打印 split 判定），用于下一次复现
   时直接对比两端到底哪个 tradeable 分叉。

`Source/MP_MeowOnlineShop/Patch_JobEndDiagnostic.cs`（新增，csproj 已登记，经
`Patch_SellSlingshot.ApplyPatch` 挂载）：

4. `MP_JOB_END_DIAG=1` 门控、最多 32 次的 `Pawn_JobTracker.EndCurrentJob` 前缀诊断：
   打印小人、被结束的工作 def、condition、startNewJob、worldTick、map ID 与征召状态。
   两端日志直接对比即可确认“哪个小人/哪个工作差 1 tick”以及被结束工作的 def，
   用于定位 522 类（以及 519/520 类）掉线的上游变异。

所有新钩子 fail-open、仅在 MP 生效、不新增同步命令、不改单机行为。

## 构建状态

- `dotnet build -c Release -p:OutputPath=TestValidation\CompileCheck_20260820_519_520`：0 警告、0 错误。
- 未复制任何 DLL 到 `1.6/Assemblies`；已部署 DLL 未变（`3FF71FFC…`）。
- 候选 DLL（含交易会话对齐 + Job-end 诊断）SHA-256 `7F635188575EBD37B9DA87626021D705806A4C83825E6CEEC0FF838EE3868731`。

## 剩余风险与验证建议

- 520 的 ~7 个 ID 偏移的**具体逐 tick 来源**仍未最终确认（最可能是 settlement 库存
  重建的时机差异，也可能是其它惰性世界侧变异）。本次补丁让“交易会话/库存”在
  命令边界收敛，但若 ID 偏移来自交易会话之外的路径，需要
  `MP_TRADE_DIAG=1` + `MP_RAND_DIAG=1` 复现定位。
- 515 那类“已征召小人走 `JobGiver_Orders -> Wait_Combat`”的变体仍可能是独立的
  征召状态路径（PerspectiveShift seek-at-will / 手动征召），不在本次补丁覆盖范围。
- 522 类（战斗上下文）的上游变异未定位：建议下次复现 host/client 都设
  `MP_JOB_END_DIAG=1`（配合 `MP_TRADE_DIAG=1`），对比两端 `[MP-JOB-END-DIAG]`
  行确认“哪个小人/哪个工作差 1 tick”，再针对其上游状态变异补丁。
- 建议下次复现：host/client 都设 `MP_TRADE_DIAG=1`；复现路径=派系定居点交易 +
  重连后继续交易 + 穿梭机/运输舱往返；对比两端
  `[MP-TRADE-DIAG] TryExecute tradeables=...` 行，确认首个分叉的 tradeable
  （colonyId/traderId/count），以及本次新增的
  `Reconciled an existing trade session ...` / `Deterministic settlement stock
  regeneration forced at TryExecute boundary` 是否在两端都出现。
- 未运行 host/client smoke/soak；需按 runtime-testing 门禁做 targeted 复现。
