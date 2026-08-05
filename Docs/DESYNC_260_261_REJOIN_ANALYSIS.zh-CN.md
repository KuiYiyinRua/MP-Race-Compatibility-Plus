# Desync-260/261 重连阶段分析（2026-08-05）

## 证据

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-05-259-260-pause-tps0/`
- `Desync-260.zip` SHA-256：`CAF293FA4454249F8091C7B9C9C0BF015EB7BA8DD74440CCC7D0236FCA78F23E`
- `Desync-261.zip`：本次工作区中无法定位原始文件，仅有最初的条目清单（大小 169018 字节），未完成冻结、哈希和内容分析。
- Multiplayer：`0.11.5+a481546`；RimWorld：`1.6.4850 rev646`
- 会话：2 名玩家，async time 开启，multifaction 开启，2 张地图
- 260 运行的程序集：`3.0.117-config-hot-sync`（日志中的 build identity）

## 260 日志摘要

- `Player1576 3804180 Desynced after last valid tick -1: Map instances don't match`
- 第一次重连后：
  `Player1576 3812490 Desynced after last valid tick -1: Random state from commands doesn't match`
- `local_traces.txt`：`No traces (remote: 0, local: 0)`
- `host_traces.txt`：`Trace count: 0`，首个不同 map random state 为 `-1`

## 根因

260 运行的是 `-timePerTick` 版本的异步调度相位补丁。`TickPatch.DoTick`
在调用 `TickTickable` 前已经执行 `TimeToTickThrough += 1f`，因此把累加器写成
`-timePerTick` 会让 vanilla 的 `while (TimeToTickThrough >= 0)` 直接跳过全部
map/world tick。这会造成 TPS 0，并让重连追赶阶段的相位与命令随机状态错位。

随后的 `Map instances don't match` 来自 `ClientSyncOpinion.CheckForDesync`：
它按 `mapStates` 的列表顺序比较 `mapId`。重连后两端枚举地图的顺序可能不同，
即使地图集合相同也会误报。`Random state from commands doesn't match` 是相位错位
在命令随机状态上的下游表现。

## 安全补丁

以下改动只涉及源码，不部署 DLL，不做 runtime 测试：

1. `Patch_AsyncTickSchedulerPhase`：在 `TickPatch.DoTick` 的 `+1f` 之后把累加器
   归一化为 `1f - timePerTick`，让每个全局 tick 稳定执行 `1 / timePerTick` 次
   map/world tick；replay 跳过，追赶阶段同样生效。
2. `Patch_MapStateOrderNormalizer`：在 `TryAddMapRandomState` 之后和
   `CheckForDesync` 比较前，把两端 `mapStates` 都按 `mapId` 排序，消除重连后的
   枚举顺序误报。
3. `Patch_TransportShipUnloadMp`：把运输船卸货自动征召的入队点移到
   `UnloadThingFromShuttle` 参数本身，按 pawn 而不是按 shuttle 本地地图排队，
   去掉 home-map 守卫，并在 pawn 实际地图的 `MapPreTick` 按 `thingIDNumber`
   排序重放。该路径修复 Desync-259 的 job/Rand 顺序分叉，与 260 的重连
   随机状态错位属于同一类根因。
4. `UnityInputCompat` + God Hands 控制器补丁：移除对
   `UnityEngine.InputLegacyModule` 的编译期依赖，避免 Prepatcher 反射加载失败；
   并为 `ResetMouseRelease` 增加同步入口，避免仅主机侧执行 God Hand 本地状态变化。

## 验证状态

- 当前工作树只有源码修改；未复制任何 DLL 到正式 `1.6/Assemblies`。
- 当前源码静态构建 `C:\tmp\mp-build-260-261-check\bin\MP_MeowOnlineShop.dll`
  SHA-256：`EBDD0926D15724E0666B551465087D9D40F99CA40784C952BC47B6A349994D15`
- 更早的 `C:\tmp\mp-build-tps-fix-nullguard\bin\MP_MeowOnlineShop.dll`
  SHA-256：`5058A9C8A9E6F7EF1F318BE557775D0B5A8260D66981012914A41FB3C727EF5E`
- 未运行 host/client smoke 或 soak。
- `Desync-261.zip` 缺失，因此该包未被分析；需要重新提供后才能完成 261 的
  根因核对与最终验证。
