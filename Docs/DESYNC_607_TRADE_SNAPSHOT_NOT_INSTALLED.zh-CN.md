# Desync-607：交易快照未安装，库存差异在穿梭机装载时暴露

> 608–611 后续更正：修正错误字段反射后，第二版仍因 Harmony prefix 把原方法参数
> `actuallyTraded` 写成 `traded` 而在运行时拒绝安装。最终改为与 Multiplayer 原生
> prefix 相同的最小 `Prefix(TradeDeal __instance)` 签名，详见
> `DESYNC_608_611_TRADE_PREFIX_BINDING.zh-CN.md`。

## 结论

607 的直接失步点是世界商队发射穿梭机时，两端装载的物品栈不同：host 仍在
`LoadCaravanItemsIntoContainer -> Thing.SplitOff` 中拆分一个物品栈，local 已经结束
装载并进入 `WorldObjectMaker.MakeWorldObject`。这证明发射前商队库存或栈数量已经不一致，
穿梭机只是第一个消耗不同 Unique ID 的可见位置。

日志同时证明 599 后新增的 canonical trade snapshot 没有运行。启动日志明确输出：

`Canonical trade snapshot targets unresolved; native trade accept retained.`

Harmony 元数据中 `TradeDeal.TryExecute` 也只有旧的派系上下文 prefix 和 Multiplayer
原生 `TradeDealExecutePatch.Prefix`，没有成交快照 prefix。因此本轮并不能否定“派系交易
导致库存分叉”；相反，日志证明用于封住这条路径的新补丁根本没有安装，交易仍然只同步
`MpTradeSession`，最终 `countToTransfer` 仍依赖此前按 concrete thing ID 发送的逐项命令。

## 证据

- 原始包：根目录 `Desync-607.zip`
- SHA-256：`8182F30BCB3D2E9F237D48C021CC8CE0F27D6B7E4D3A6FCF981C4D949D002715`
- 解析目录：`DesyncEvidence-607-analysis/`
- Multiplayer `0.11.5+a481546`；RimWorld `1.6.4850 rev646`
- async time 开、multifaction 开、2 名玩家、2 张地图
- 最后有效 tick：`11113081`
- 首个分叉：tick `11113127`、trace `388`
- local trace 数 `690`，host trace 数 `692`
- local：`GetNextWorldObjectID -> MakeWorldObject -> LaunchShuttle`
- host：`GetNextThingID -> ThingMaker.MakeThing -> Thing.SplitOff ->
  TryTransferToContainer -> LoadCaravanItemsIntoContainer -> LaunchShuttle`
- JIT 记录还显示此前 tick `11101494` 执行过标准 settlement trade 的
  `TradeDeal.TryExecute`、`Settlement_TraderTracker.GiveSoldThingToPlayer` 和成交后续路径。

607 不包含逐物品状态快照，因此无法从日志反推出具体是哪一个商品栈不同；能确定的是：
库存差异早于穿梭机发射，而本应替代原生交易接受命令的补丁当时未生效。

## 第一版为什么必然失效

第一版把以下查找列为全套补丁的强制目标：

`AccessTools.Field(typeof(Transferable), "countToTransfer")`

RW 1.6 的实际结构是：

- `Transferable` 只声明抽象属性 `CountToTransfer`；
- 私有字段 `countToTransfer` 位于具体类 `Tradeable`；
- 公共写入入口是 `Transferable.ForceTo(int)`。

因此该查找恒为 null，`TargetsResolved` 恒为 false，整套补丁直接 return。旧的
`Patch_TradeSessionRejoinMp` 也犯了同样错误，所以它在 TryCreate 冲突后声称执行的旧
count 清零实际上从未运行。

## 修订方案

### 1. 用公开 API 管理成交数量

- 删除两处对私有 `countToTransfer` 字段的反射。
- 清零和应用最终数量统一调用 `Tradeable.ForceTo`；它还会同步 `EditBuffer`，比直接写
  私有字段完整。
- 在写入任何最终 count 前，先解析全部 payload、拒绝重复行，并用
  `GetMinimumToTransfer/GetMaximumToTransfer` 校验范围，避免半应用状态。

### 2. 接受边界 fail-closed，不再静默回退

- 成交快照 prefix 的优先级提高到高于 Multiplayer 原生
  `TradeDealExecutePatch.Prefix`，保证原生 session-only 命令不能抢先发出。
- 初始化拆成“UI 边界可接管”和“完整 replay 目标可用”两层。
- 即使未来某个 replay 私有目标改名，只要仍能识别 Multiplayer TradingWindow，就会
  接管接受按钮并阻止成交，同时打印具体 unresolved 成员；不会再退回已知会失步的原生
  路径。
- sync method 注册异常也会进入相同的 fail-closed 路径。

### 3. 减少脆弱反射并强化会话身份

- `SessionId` 改为直接使用 Multiplayer API 的 `Session.SessionId`。
- tradeables 改为使用公开的 `TradeDeal.AllTradeables`。
- 只有 `TradeDeal.AddToTradeables/AddAllTradeables` 仍需私有反射，并明确使用
  `AccessTools.DeclaredMethod`。
- sessionId 命中时仍必须同时匹配 trader 和 negotiator；避免重连后 session ID 漂移时
  错把同 ID 的另一场交易当作目标。
- 启动日志会列出每个缺失目标，不再只有笼统的“targets unresolved”。

### 4. 语义键按总量表示，不依赖物理分栈

最终成交意图继续不携带 thing ID。修订后，同一 side 上语义相同的物品会按稳定属性
分组并汇总 stackCount；两个 peer 即使把相同总量保存成不同的物理栈分割，也会得到同一
交易项键。重复项 occurrence 按完整 tradeable 列表计算，避免零 count 项改变非零项的
ordinal。

### 5. 保留原生成交权威

共享命令 tick 仍由每端确定性重建 settlement stock/deal，再调用原生
`MpTradeSession.TryExecute`。价格、资金检查、物品转移、赠礼、好感、灵感和 session
移除仍由 RimWorld/Multiplayer 原逻辑完成；没有在穿梭机阶段伪造或删除库存来掩盖上游
分叉。

## 修改文件

- `Source/MP_MeowOnlineShop/Patch_TradeExecutionSnapshot.cs`
- `Source/MP_MeowOnlineShop/Patch_TradeSessionRejoinMp.cs`
- `Docs/DESYNC_599_FACTION_TRADE_CANONICAL_SNAPSHOT.zh-CN.md`
- `Docs/DESYNC_607_TRADE_SNAPSHOT_NOT_INSTALLED.zh-CN.md`

## 验证边界

- 只做 Release 编译检查：0 warning、0 error。
- 候选输出：`BuildOutput/Desync607Compile/MP_MeowOnlineShop.dll`
- 候选 SHA-256：`E45CD870E745B3CDF5983A95E189282EB8D565B243AE2DBD062010201936AB39`
- 已部署 `1.6/Assemblies/MP_MeowOnlineShop.dll` SHA-256 仍为
  `DE69E29D7029ED0BA9F9EED7C9727F4D4C230C33E68EC9F82F039ECB9A9F2414`。
- 未复制/部署 DLL，未启动 RimWorld，未做 host/client 或游戏内测试。
