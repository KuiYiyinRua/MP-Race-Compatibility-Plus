# Desync-259 奥德赛穿梭机下机不同步分析（2026-08-05）

## 证据

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-05-259-odyssey-shuttle-unload/`
- `Desync-259.zip` SHA-256：
  `88B1BA7EF2BFF20ABAB7A930A65EA7513387FD997D2E9632F02D79E026D21D47`
- Multiplayer：`0.11.5+a481546`；RimWorld：`1.6.4850 rev646`
- async time 开启、multifaction 开启、2 张地图、2 名玩家
- 最后有效 tick：`3821731`；掉线 tick：`3821790`
- 掉线类型：`Trace hashes don't match`

## 最早分叉

两端在 trace index 5（Tick `3821763`）第一次分叉，且都在同一个地图 tick 里
分配 JobID：

- host：`UniqueIDsManager.GetNextJobID`
  -> `JobGiver_Orders.TryGiveJob`
  -> `Pawn_DraftController.set_Drafted`
  -> `MP_MeowOnlineShop.Patch_TransportShipUnloadMp.MapPreTickPostfix`
- local：`UniqueIDsManager.GetNextJobID`
  -> `JobMaker.MakeJob(JobDef,int,bool)`
  -> `Pawn_JobTracker.EndCurrentJob`
  -> `JobDriver.EndJobWith`
  -> `Pawn.Tick`（普通 pawn tick）

即 host 在 `MapPreTick` 执行了延迟的自动征召，client 的同一 pawn 走的是
普通 job 自然结束路径，导致同一个地图 tick 内 JobID/随机数消耗顺序不一致。
日志中还出现 `PassengerShuttle694170` 引用 `TransportShip_17` 无法解析的
跨引用错误，说明 shuttle/transport-ship 生命周期本身有状态偏差。

## 根因

`Patch_TransportShipUnloadMp` 原本以 `Pawn_DraftController.Drafted` setter
作为延迟入队边界：

- 只在 `_shipUnloadDepth > 0`（在 `UnloadThingFromShuttle` 调用栈内）时入队；
- pawn 已处于征召状态、或本地 map 被认为是 home map、或 drafter/pawn 状态
  不同时，setter 路径会被跳过或提前返回。

因此 host/client 只要有一端在 unload 时处于不同本地征召状态，就会出现
“一端延迟到 MapPreTick、另一端立即/自然执行”的不对称，这是
Desync-226-228 的跨时间域问题在奥德赛 PassengerShuttle 上的复现。

## 修复

修改 `Patch_TransportShipUnloadMp.cs`：

1. 入队点改到 `ShipJob_Unload.UnloadThingFromShuttle` 自身：
   `UnloadPrefix(TransportShip, Thing)` 直接用方法参数把
   `IsPlayerControlled` 的 Pawn 写入 `PendingDraftsByMap`，不再依赖
   `Drafted` setter 是否被调用。
2. `UnloadFinalizer` 校验 pawn 是否真的下机；若 `TryDrop` 失败则从 pending
   中移除。
3. `DraftedPrefix` 在 unload 期间仍压制 vanilla setter 的副作用并镜像
   `draftedInt=true`，但不再负责入队。
4. `MapPreTickPostfix` 在目标 map tick 时按稳定 pawn ID 顺序执行
   `draftedInt=false` -> `Drafted=true`，并保留 vanilla 的
   `!map.IsPlayerHome` 语义，两端在同一边界产生相同 JobID/随机顺序。

## 验证状态

- `dotnet build`（Release，输出到 `C:\tmp\mp-build-259\bin`）：0 警告，0 错误。
- 候选 DLL SHA-256：
  `BF0A01CB5FDBCB37AD0CFED34136485C70186F24184840B0F0E311CBDEC197E8`
- 程序集版本仍为 `3.0.117-config-hot-sync`；`ilspycmd` 确认
  `UnloadPrefix(TransportShip, Thing)`、`UnloadFinalizer(...)`、
  `MapPreTickPostfix` 的新逻辑已编入候选。
- 按用户要求：未部署正式版 DLL，未运行 runtime 测试。后续仍需真实
  host/client 的 Odyssey shuttle 下机 targeted smoke + 长 soak 验证。
