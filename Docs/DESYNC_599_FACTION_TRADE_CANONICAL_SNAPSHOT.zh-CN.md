# Desync-599 分析：派系交易成交状态分叉与成交快照修复（2026-08-22）

> 607 更正：本页记录的第一版成交快照在 RW 1.6 上没有真正安装。它错误地从
> `Transferable` 查找 `countToTransfer` 字段，而该字段实际由 `Tradeable` 实现；
> 全量目标检查因此失败并回退到原生交易。修订后的实现与证据见
> `DESYNC_607_TRADE_SNAPSHOT_NOT_INSTALLED.zh-CN.md`。

## 结论

`Desync-599.zip` 直接确认了派系交易是本次不同步的触发点。两端在
`tick=10929905` 之前的 trace 完全对齐；随后 local 执行：

`MpTradeSession.TryExecute -> TradeDeal.TryExecute ->
Faction.Notify_PlayerTraded -> Faction.TryAffectGoodwillWith ->
Messages.Message -> UniqueIDsManager.GetNextMessageID`

host 在同一 trace 位置没有进入成交/好感链，仍然执行
`Building_HoldingPlatform.Tick -> Rand.MTBEventOccurs`。local 因此比 host 多分配一个
message ID，成为日志可见的第一个分叉。

这说明问题不是 `Notify_PlayerTraded` 自身随机，也不是普通地图 tick 顺序漂移；同一个
Multiplayer 成交命令在两端看到的 `TradeDeal` 状态不同，一端完成成交，另一端拒绝或
没有产生实际交易。

## 证据

- 原始包：根目录 `Desync-599.zip`
- SHA-256：`2998D3C3E597B2CC7FAB9158C991E050BB8B42E7EFB9B2B74EBDA08316CBC7FA`
- 解析目录：`DesyncEvidence-599-analysis/`
- Multiplayer `0.11.5+a481546`；RimWorld `1.6.4850 rev646`
- async time 开、multifaction 开、2 名玩家、2 张地图
- 最后有效 tick：`10929871`
- local trace 数 `183`，host trace 数 `182`；多出的一条正是成交好感消息 ID
- 日志与 Harmony 元数据确认旧补丁已实际加载：
  - `Patch_TradeSessionRejoinMp.TryExecuteReconcilePrefix`
  - `TradeTryExecuteFactionPrefix`
  - `TradeDealTryExecuteFactionPrefix`
  - `NotifyPlayerTradedFactionPrefix`

因此 599 不是“旧 DLL 没含修复”，而是现有修复模型仍有缺口。

## 现有补丁为什么不彻底

Multiplayer 原生交易把一次成交拆成两类命令：

1. 调整每个交易项数量时，发送带 `MpTransferableReference` 的字段命令；该引用用
   `sessionId + concrete thingId` 定位交易项。
2. 点击接受时，`MpTradeSession.TryExecute` 只同步 session，并不再次携带最终的完整
   `countToTransfer` 集合。

重连后，即使两端都能按 `sessionId` 找到会话，它们仍可能保留不同的旧 tradeable、
thingId 或 count。旧补丁只在列表为空、查找失败、TryCreate 冲突或 stock 为 null 时
尝试修复，无法保证接受按钮触发时的最终报价完全相同。

另有一个确定的实现遗漏：标准定居点交易的 `MpTradeSession.trader` 是
`RimWorld.Planet.Settlement`，现有 `TryExecuteReconcilePrefix` 却只把该字段直接转换为
`Settlement_TraderTracker`。因此它声称的“成交前 settlement stock 修复”对标准派系
定居点路径实际不会运行。

## 修复方案

### 1. 修正 settlement tracker 解析

`Patch_TradeSessionRejoinMp.ResolveSettlementTracker` 同时支持：

- trader 本身就是 `Settlement_TraderTracker`；
- trader 是 `Settlement`，通过 `settlement.trader` 获取 tracker。

原 `TryExecuteReconcilePrefix` 改用该统一解析函数。

### 2. 新增 canonical trade snapshot

新增 `Patch_TradeExecutionSnapshot.cs`，只接管 Multiplayer `TradingWindow` 中的接受
边界，不改普通单机或其它直接 `TradeDeal.TryExecute` 路径：

1. 玩家点击接受时，从发起端当前 deal 生成最终交易意图；只包含非零交易项。
2. 交易项不用本地化文本和 thingId，而用类型、def、stuff、quality、stack、HP、
   pawn kind/name/gender 等语义字段形成稳定键。
3. 一条自定义 sync command 一次性发送 `sessionId + trader key + negotiator +
   giftMode + final intent`。negotiator 参数同时让 Multiplayer 自动选择与原生交易一致的
   map/world 命令队列。
4. 共享执行 tick 上每端都：
   - 在谈判者实际玩家派系上下文中运行；
   - 对 settlement stock 做确定性重建；
   - 清空旧 tradeables，重新创建 permanent silver 并调用原生 `AddAllTradeables`；
   - 确定性排序并清零全部旧 count；
   - 应用同一份最终 intent；
   - 调用原生 `MpTradeSession.TryExecute` 完成价格校验、物品转移、好感与 session 移除。
5. 任一关键目标未解析或 intent 无法映射时，在产生游戏状态变异前 fail-closed，阻止
   回落到已知会制造 599 的“只同步 session”路径。

旧的逐项 count 命令仍可用于 UI 即时显示，但不再是最终成交的权威输入。

## 修改文件

- `Source/MP_MeowOnlineShop/Patch_TradeExecutionSnapshot.cs`（新增）
- `Source/MP_MeowOnlineShop/Patch_TradeSessionRejoinMp.cs`
- `Source/MP_MeowOnlineShop/Patch_SellSlingshot.cs`
- `Source/MP_MeowOnlineShop/MP_MeowOnlineShop.csproj`

## 验证边界

- 仅执行编译检查：0 warning、0 error。
- 候选编译输出：`BuildOutput/Desync599Compile/MP_MeowOnlineShop.dll`
- 候选 SHA-256：`E581F2E40091B135DDA12567DB4C8C0ADBBE12258A3C9C926DDB38F9594C9895`
- 已部署 DLL SHA-256 仍为
  `7208FF5AED270A066F13F189E4E5765436397BADDCC84B98114EF8A0B1827076`。
- 未复制 DLL 到 `1.6/Assemblies`，未启动游戏，未做 host/client 测试。

## 剩余风险

- 语义键刻意不含本地化文本；极少数第三方 Tradeable 若把关键等价性只藏在自定义字段
  中，可能无法映射。此时补丁会在成交变异前阻止交易并打印一次 warning，而不是继续
  执行不一致成交。
- 本次按要求未做运行时测试。首次部署后的最小验证应覆盖：标准 settlement trade、
  重连后继续交易、地图 trader pawn、world caravan negotiator、gift mode，以及两名玩家
  分别发起交易。
