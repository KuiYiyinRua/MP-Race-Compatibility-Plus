# Desync-434/435/436 分析：奥德赛穿梭机 + 定居点交易（2026-08-16）

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-16-434-435-odyssey-shuttle-frozen/`
- `Desync-434.zip` SHA-256：`7729C9580DF9423D5771CDF0CCDB3778EDFCC584F94C6D674E9D9FE5F4319C90`
- `Desync-435.zip` SHA-256：`9086FCAD4A1154A1729C7AD569ADFD01AAE355DA36BA4504F476C09A5051770C`
- `Desync-436.zip` SHA-256：`76BF2E061270DB69D76BDEC6D815E7F70B69C6A3F9FC40085FCC8499CE9ECD27`
- Multiplayer：`0.11.5+a481546`；RimWorld：`1.6.4850 rev646`
- async time 开启，multifaction 开启，2 张地图，2 名玩家
- 运行时程序集：`MP_MeowOnlineShop 3.0.121`（`1.6/Assemblies/MP_MeowOnlineShop.dll`
  SHA-256 `C3A878D80551D6FB389A4738CD90E927802649B7E4C4EBB43FCFF666A3FD2862`）

## 掉线记录

| 包 | 最后有效 tick | 掉线 tick | 类型 |
|---|---:|---:|---|
| 434 | 7815781 | 7815903 | Trace hashes don't match |
| 435 | 7819591 | 7819650 | Wrong random state for the world |
| 436 | 7822785 | 7823582 | Random state from commands doesn't match |

## 时间线

- `t≈7815001`：host 首次 JIT `Caravan.<GetLaunchGizmo>b__124_1`，调用方是
  `Settlement.<GetShuttleFloatMenuOptions>b__1`，即玩家在定居点打开穿梭机发射菜单。
- `t≈7815031`：`TransferableUIUtility.OpenSorterChangeFloatMenu`（交易窗口排序器）。
- `t≈7815811`：434 的首次分叉落在同步命令 `MpTradeSession.TryExecute ->
  TradeDeal.TryExecute` 内：
  - host 对同一 `Tradeable` 不执行 `Thing.SplitOff`，直接进入
    `CaravanInventoryUtility.FindPawnToMoveInventoryTo`；
  - local 多执行了一次 `Thing.SplitOff -> ThingMaker.MakeThing ->
    UniqueIDsManager.GetNextThingID`。
  说明两端 `countToTransfer` 或定居点库存堆叠数量不一致。
- 同一时刻附近日志：`[MP-MeowOnlineShop] Entered map removal boundary: map=58.`、
  `Can't transfer things because there is nothing left.`
- `t≈7819650`：435 为纯世界随机数分叉；两端 map trace hash 相同、
  “first desynced map random state” 为 `-1`。
- 436 重连后立刻出现三次：
  `Sync Error: Error reading type: Multiplayer.Client.Persistent.MpTransferableReference`
  + `World cmd exception (Sync): System.NullReferenceException`
  栈顶为 `SyncDictMultiplayer.<.cctor>b__1_9` 的
  `session.GetTransferableByThingId(thingId)`。

## 根因

436 栈顶的 NRE 发生在 Multiplayer 反序列化 `MpTransferableReference` 时：
`Multiplayer.game.GetSessions(map).FirstOrDefault(s => s.SessionId == id)`
在重连后的客户端上找不到对应的 `MpTradeSession`（或会话在
`SessionManager.ExposeSessions` 的 `PostLoadInit` 阶段因
`IsSessionValid=false` 被移除），随后
`session.GetTransferableByThingId` 直接空引用。该命令在 host 已应用，
在 client 被丢弃，于是下一次 `MpTradeSession.TryExecute` 的
`countToTransfer/stackCount` 两端不同，出现 434 的额外 `SplitOff`，
并进一步演化为 435 的世界随机数分叉和 436 的命令随机数分叉。

穿梭机/定居点交互（`Settlement.GetShuttleFloatMenuOptions`、
`TransportShip.ArriveAt`、地图移除）是触发场景：交易会话跨越了
重连/地图移除边界，Multiplayer 原生重放路径对会话生命周期的保护不足。

## 补丁

新增 `Source/MP_MeowOnlineShop/Patch_TradeSessionRejoinMp.cs`，并在
`Patch_SellSlingshot.ApplyPatch` 中于 `Patch_TraderStockDeterminism.Apply`
之后调用：

1. `MpTradeSession.get_IsSessionValid` 前缀：`Scribe.mode == PostLoadInit`
   时保持会话有效，避免重连加载阶段被 `SessionManager.ExposeSessions` 移除。
2. `MultiplayerGame.GetSessions` 后置：追加 `Multiplayer.WorldComp.trading`
   中尚未返回的交易会话，作为按 `SessionId` 查找的兜底。
3. `MpTradeSession.GetTransferableByThingId` 前缀：当 `deal.tradeables`
   为空时先执行 `MpTradeDeal.Recache()` 重建并直接返回命中的 `Tradeable`，
   避免在同步反序列化中途抛 NRE。

三个钩子均 fail-open，不新增同步命令，不改变单机行为。

补丁还包含一个环境变量门控诊断（`MP_TRADE_DIAG=1`，最多 8 次）：
在 `MpTradeSession.TryExecute` 前打印每个 `Tradeable` 的 colony/trader
thingID 与 `CountToTransfer`，在 `GiveSoldThingToPlayer/Trader` 前打印
`thingID/def/stack/count/split`。下一次复现时两端日志可以直接对比
到底是哪个 tradeable 的计数或库存堆叠分叉。

## 构建与验证状态

- `dotnet build -c Release -p:OutputPath=C:\tmp\mp-build-434-436`：0 警告，0 错误。
- 候选 DLL：`C:\tmp\mp-build-434-436\MP_MeowOnlineShop.dll`
- 候选 SHA-256：`A212138369AE795E44411C147ABAC9A247BE3923D88D80729B7849E0138AB133`
- `ilspycmd` 已确认程序集包含 `Patch_TradeSessionRejoinMp` 及
  `GetSessionsPostfix`、`IsSessionValidPrefix`、`GetTransferablePrefix`、
  诊断钩子 `TryExecuteDiagPrefix`/`GiveSoldThingTo*DiagPrefix`
  Harmony 注册点。
- 未部署到 `1.6/Assemblies/`，未运行 host/client smoke/soak；
  后续仍需要真实存档的 async-time + 穿梭机发射 + 定居点交易 + 重连回放验证。

## 剩余风险

- 若两端 `thingID` 计数在会话创建前已经分叉，仅靠会话保活和
  `Recache` 仍可能无法让 client 用 host 的 `thingId` 命中相同 `Tradeable`。
- 435 的首次世界随机数分叉尚未能通过本次包内 trace 直接定位到具体方法，
  需要 `MP_RAND_DIAG=1` 的针对性复现来确认是否还有独立的世界随机数来源。

## 复现与验证建议

1. host/client 都设置 `MP_RAND_DIAG=1` 和 `MP_TRADE_DIAG=1` 再启动。
2. 复现路径：定居点交易窗口保持打开，同时执行奥德赛穿梭机发射/落地，
   或直接在穿梭机落地后重连一次再继续交易。
3. 对比两端日志：
   - `[MP_RAND_DIAG] worldTick ... rand=... low=... high=...`
     找第一个 world tick 的 Rand 状态分叉；
   - `[MP-TRADE-DIAG] TryExecute ... count=...` 与
     `GiveSold... stack=... count=... split=...`
     确认哪个 tradeable 的计数或库存堆叠先分叉。
4. 用首个有效 desync 包核对：若 `MpTransferableReference` 不再 NRE、
   且 `TryExecute` 两端 `split` 一致，则本补丁覆盖了 436 的重放路径。
