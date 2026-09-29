# Desync-608～611：交易分叉复现与 Harmony prefix 绑定失败

## 结论

608～611 不是“canonical trade snapshot 已执行但算法仍然失败”。四包中该补丁一次都没有
实际接管 `TradeDeal.TryExecute`：

- 608～610 启动日志仍是第一版的 `targets unresolved; native trade accept retained`。
- 611 已包含修正字段反射的第二版，但启动时报：
  `Parameter "traded" not found in method ... TryExecute(Boolean& actuallyTraded)`。
- 四包 Harmony 元数据中的 `TradeDeal.TryExecute` 都只有旧派系上下文 prefix 和
  Multiplayer 原生 `TradeDealExecutePatch.Prefix`。
- 四包 JIT 记录都没有 `TradeDealTryExecuteUiPrefix` 或
  `SyncExecuteCanonicalTrade`。

因此当前日志仍然完整复现原生 session-only 成交命令的旧问题，不能用于评价尚未运行过的
semantic intent replay。

## 四包共同证据

- 608：最后有效 tick `11147311`；tick `11147363`、trace `486`，local 进入
  `MpTradeSession.TryExecute -> TradeDeal.TryExecute -> Notify_PlayerTraded ->
  TryAffectGoodwillWith -> GetNextMessageID`，host 在同位置执行
  `GameComponent_Anomaly.GameComponentTick -> Rand.Range`。local 比 host 多一条 trace。
- 609：最后有效 tick `11153641`；tick `11153672`、trace `15`，local 再次进入同一
  成交好感消息链，host 执行 `Building_HoldingPlatform.Tick -> Rand.MTBEventOccurs`。
  local 比 host 多一条 trace。
- 610：最后有效 tick `11161831`；tick `11161863`、trace `27`，local 已进入
  `MakeWorldObject`，host 仍在 `LoadCaravanItemsIntoContainer -> Thing.SplitOff`。
- 611：最后有效 tick `11191561`；tick `11191603`、trace `299`，再次是 local 创建
  shuttle world object、host 拆分商队物品栈。host 比 local 多五条 trace。host JIT 还在
  tick `11191461` 记录到 settlement trade 的
  `Settlement_TraderTracker.GiveSoldThingToPlayer -> MinifiedThing.SplitOff/PreTraded`，仅约
  142 tick 后才发射穿梭机。

这组证据把两种表象串成同一条链：608/609 在交易完成当场可见分叉；610/611 的交易差异
没有立即消耗可追踪 ID，之后在商队装载时因物品栈数量不同才暴露。

原始包 SHA-256：

- `Desync-608.zip`：`81214F4F3C91C022A9EEC298144617DB812DDC04B98A3D91183EDFABD645200A`
- `Desync-609.zip`：`B652EFBC96C5339D82A5CF157D13B62C6FF705B014102DEBBCFFEDB4B26CF716`
- `Desync-610.zip`：`14543A73AD275E0D671FCF8AEA2DDFF2ACDFDF83230673AAEA426DEB402ABB4C`
- `Desync-611.zip`：`7A28FF53003A5839FD5E25A501EEB3E3DD5934EA32C0D38A69EA897CB095787C`

## 第二版为什么仍未安装

RimWorld 的目标方法签名是：

`bool TradeDeal.TryExecute(out bool actuallyTraded)`

第二版 prefix 使用了：

`TradeDealTryExecuteUiPrefix(TradeDeal __instance, ref bool __result,
ref bool traded)`

Harmony 会把普通 patch 参数按原方法参数名绑定。`traded` 不是 `actuallyTraded`，这个错误
不会被 C# 编译器发现，只会在 `harmony.Patch` 时抛出异常。外层 Apply 捕获异常后，原生
Multiplayer prefix 保持唯一的 UI 接管者，所以仍然只发送 `MpTradeSession.TryExecute`。

## 最终修订

### 1. 移除原参数绑定

prefix 改为与 Multiplayer 自己的实现相同的最小签名：

`TradeDealTryExecuteUiPrefix(TradeDeal __instance)`

它只需要在 UI 阶段发送 canonical command 并返回 false，不需要读取或写入原方法的返回值
与 out 参数。即使后续 RimWorld 重命名 `actuallyTraded`，也不会再影响安装。

### 2. 安装后自审计

`harmony.Patch` 返回后立即读取 `Harmony.GetPatchInfo(TradeDeal.TryExecute)`，确认 revision 3
prefix 的 owner 和 MethodInfo 已出现在 Prefixes 中。启动成功日志必须同时包含：

- `revision=3`
- `uiPrefixInstalled=True`
- `unresolved=none`

点击接受与共享 tick replay 各增加一次性日志：

- `Canonical trade intent queued ...`
- `Canonical trade replay completed through native MpTradeSession.TryExecute ...`

后续 desync 包因此能区分“未安装”“已排队但未执行”“已进入原生成交”三个阶段。

### 3. 修正额外的成交状态缺口

- 先把命令携带的 `giftMode` 写回 session，再调用 `SetTradeSession`。否则
  `AddAllTradeables` 会读取旧的静态 `TradeSession.giftMode`，两端预先不一致时仍会重建出
  不同的交易列表。
- currency count 不再进入 payload。它是原生 `UpdateCurrencyCount` 根据实际商品行计算的
  派生值；payload 只携带玩家选择的非货币交易项，避免 UI 缓存值成为第二权威来源。
- 保留 607 已加入的公共 `ForceTo`、语义总量键、完整行校验、session/trader/negotiator
  复合身份和 fail-closed 边界。

## 修改与验证边界

- 主要修改：`Source/MP_MeowOnlineShop/Patch_TradeExecutionSnapshot.cs`
- 分析文档：`Docs/DESYNC_608_611_TRADE_PREFIX_BINDING.zh-CN.md`
- Release 编译：0 warning、0 error。
- 候选 DLL：`BuildOutput/Desync608-611Compile/MP_MeowOnlineShop.dll`
- 候选 SHA-256：`56FC435659DA3DA9C4B4D08A9C8071BE142A586655E1AC63B4CADFCFABC03664`
- 编译时已部署 DLL SHA-256：
  `26B4F8DD2BBF7223876E1762EAB23183A97CC1E7FC38F2658CE83EDA3E356F70`
- 未复制或部署候选 DLL，未启动 RimWorld，未做 host/client 游戏测试。

## 后续：TradeShip 正常冲突被误修复

运行时警告显示 `TradeShip.TryOpenComms -> MpTradeSession.TryCreate` 的 null 结果进入了
`TryCreatePostfix`。这里的 null 并不等价于重连损坏：Multiplayer 的
`MpTradeSession.CanExistWith` 本来就会在“交易者相同 **或** 谈判者相同”时拒绝创建第二个
会话。旧补丁重复使用这个 OR 条件，随后改写现有 session 的 trader/negotiator 并直接
调用 `MpTradeDeal.Recache`，因此把飞船贸易等正常互斥路径错误地当作修复对象。

同时，原生 `TryCreate` 在 finally 中调用 `SetTradeSession(null)`；后置补丁再调用
`Recache` 时没有 `TradeSession.trader/playerNegotiator/deal` 静态上下文，这正是反射调用只
显示 `TargetInvocationException` 的直接原因。

修订后的边界：

- `TradeShip`、pawn trader 及其他非 `Settlement` 交易者立即绕过该修复，保持 Multiplayer
  原生行为；
- 仅当现有 session 的 trader 与 negotiator 都和本次请求为同一对象时，才认定为可修复
  的定居点恢复会话；
- 不再改写现有 session 的 trader/negotiator；
- `Recache` 前调用 `SetTradeSession(existing)`，finally 中恢复此前的 current session；
- 成功恢复只在 `MP_TRADE_DIAG=1` 时输出普通消息；失败警告展开
  `TargetInvocationException.InnerException`，避免再次只得到无意义的外层文本。

本次独立 Release 编译：0 warning、0 error。候选 DLL：
`BuildOutput/TradeSessionConflictScopeCompile/MP_MeowOnlineShop.dll`，SHA-256：
`2E2E1A6ECDB7D7C47E2B6F07902A0790F90ED191FB81B106E21DE926799C91B0`。
编译后已部署 DLL SHA-256 为
`7A42C1C44ED74F9FD79CE39CC39E625F0ECAD6E68041813EEB7CD64D7E7F7FDE`，两者不同；未部署、
未启动 RimWorld、未做 host/client 游戏测试。

## 后续：canonical replay 重复占用 transferables context

revision 3 在同步 replay 中先调用 `SetTradeSession(session)`，以便
`AddAllTradeables` 读取正确的静态 `TradeSession.giftMode`；完成重建后却在该上下文仍被
持有时调用原生 `MpTradeSession.TryExecute`。原生方法第一步也会调用
`SetTradeSession(this)`，而 `SyncSessionWithTransferablesMarker` 明确禁止设置第二个非 null
上下文，因此每次正常成交都会抛出：

`Session with transferables context already set!`

revision 4 将上下文所有权分成两个不重叠阶段：

1. canonical replay 临时持有上下文，重建 tradeables 并应用 canonical intent；
2. replay 清空临时上下文；
3. 调用原生 `MpTradeSession.TryExecute`，由 Multiplayer 自己建立、使用并清空成交上下文。

提前返回或重建异常时，外层 finally 只清理 replay 自己仍持有的上下文；一旦所有权交给
原生 `TryExecute`，外层不再重复清理。

独立 Release 编译：0 warning、0 error。候选 DLL：
`BuildOutput/CanonicalTradeRevision4Compile/MP_MeowOnlineShop.dll`，SHA-256：
`2962D1B9AFD898FB6A90DF3CB8401DF4BF48F097A354D4C6139B92F4FAE234B0`。
编译后已部署 DLL SHA-256 为
`C2A0B64AC468DB66BEEE62710AEC5EF776BD140F1CF0DAE81478AE29BFDE6DCE`，两者不同；未部署、
未启动 RimWorld、未做 host/client 游戏测试。
