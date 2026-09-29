# Desync-278 分析（2026-08-06）

## 证据

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-06-278-milira-trade/`
- 原始包 SHA-256：`A3513A61BC274DE9C2E2DC7944358F2167D979BB7C23D8B5ACF3A31A75AE4F70`
- 冻结副本 SHA-256：`A3513A61BC274DE9C2E2DC7944358F2167D979BB7C23D8B5ACF3A31A75AE4F70`
- Multiplayer：`0.11.5+a481546`
- RimWorld：`1.6.4850 rev646`
- 会话：2 名玩家，async time 开，multifaction 开，3 张地图
- 加载程序集：`MP_MeowOnlineShop 3.0.117-tps-fix-nullguard`

## 最终掉线点

- `Player5192 4122930 Desynced after last valid tick 4122871: Wrong random state for the world`
- 最终会话从第二次 rejoin 后开始，最终掉线前本地日志连续出现：
  `MpTransferableReference.set_CountToTransfer -> NullReferenceException`

## 最早分歧

两端在同一个同步命令 `MpTradeSession.TryExecute -> TradeDeal.TryExecute` 内首次分叉：

- local：`CaravanInventoryUtility.FindPawnToMoveInventoryTo -> Rand.Range`
- host：`Thing.SplitOff -> ThingMaker.MakeThing -> UniqueIDsManager.GetNextThingID`

同一 tick 内 host 多执行了 `SplitOff` 和额外的 `FindPawnToMoveInventoryTo`，说明 host 的
`Tradeable.CountToTransfer` 比 local 多应用了两笔购买数量。local 日志中的 NRE 正是
`SyncTradeableCount` 反序列化时用 host 的 thingID 在本地 `deal.tradeables` 中找不到
`Tradeable`，导致数量没有写入 local。

## 根因分级

1. 已证实：交易计数同步在 local 端无法解析部分 `Tradeable`，数量只写入了 host。
2. 强支持：同一同步 tick 两端都执行了 `TradeDeal.AddAllTradeables`，但后续
   `MpTradeSession.GetTransferableByThingId` 仍失败，说明会话创建后的
   settlement stock / `deal.tradeables` 在两端出现漂移。
3. 未解决：无法仅凭该包证明漂移来自 stock 内容、thingID 计数，还是 UI
   `Recache` 时序；需要 runtime 复现进一步区分。

## 修复

修改 `Source/MP_MeowOnlineShop/Patch_TraderStockDeterminism.cs`：

- 在 `MpTradeSession.TryCreate` 前缀中，若 trader 是 `Settlement`，且当前处于
  Multiplayer `Ticking` 或 `ExecutingCmds`，强制调用
  `Settlement_TraderTracker.RegenerateStock()`。该调用沿用已有的
  per-settlement 确定性 Rand seed，使两端在会话创建时就拥有相同 stock。
- 为 `MpTradeSession.GetTransferableByThingId` 增加后置修复：当同步命令内查不到
  thingID 时，先以会话为上下文执行 `MpTradeDeal.Recache()`，再按 thingID 重查，
  避免客户端静默丢弃交易数量。

## 验证状态

- `dotnet build -c Release -p:OutputPath=C:\tmp\mp-build-278\bin`：0 警告，0 错误
- 候选 DLL：`C:\tmp\mp-build-278\bin\MP_MeowOnlineShop.dll`
- 候选 SHA-256：`C5B8C70160C533A09FF354410D0DB37D13B88AF5CDD09F8BAA5798570834A21E`
- 已用 `ilspycmd` 确认 `TradeSessionTryCreatePrefix`、
  `TradeSessionGetTransferablePostfix` 和 `FindTradeableByThingId` 进入程序集。
- 未复制正式 `1.6/Assemblies/MP_MeowOnlineShop.dll`，未运行 host/client
  smoke 或 soak。

## 剩余风险

- 若两端 thingID 计数在会话创建前已分叉，仅凭 stock 重建和 `Recache` 仍无法让
  local 用 host 的 thingID 找到 Tradeable；该假设需要 runtime 复现验证。
- 所有相关包都出现过 `Configs match: False` 后手动 `Connecting anyway`，应先让
  两端配置一致。
- 候选补丁只做了静态构建与元数据检查，不能声称已修复；需要一次
  async-time + multifaction + 3 地图的 host/client 定向交易 smoke 与 soak。
