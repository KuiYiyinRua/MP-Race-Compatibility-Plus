# Desync-462 ~ 475 批次分析（2026-08-17）

## 结论

本批次 12 个包不是同一个可验证入口的连续复现，而是一条“客户端反复重连、读档后几十 tick 内掉线”的
循环。现有源码补丁（async 调度相位归一化、命令地图顺序、Caravan 地图移除、Gravship orphan 命令清理等）
均已生效，但未能阻止该循环。反复出现的规律是：**重连后客户端用读档/SpawnSetup 顺序重建 per-map
TickList，而长驻 host 保留历史注册顺序，同一 Normal bucket 内谁先 tick 不同，几十 tick 内就把 Job、
投射物、炮塔目标等路径带偏**。

本次采用一个“不改成员、不改 bucket 分配、只稳定 bucket 内 tick 顺序”的源码补丁：
`Patch_TickListOrderNormalizer.cs`。该补丁只改 `Source/` 代码，**未部署 DLL，未运行 host/client
测试**；命令 Rand 分叉和读档污染基线仍按“未验证”处理，不额外补模拟代码。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-17-462-475-meow-online-shop/`
- 原始包与冻结副本 SHA-256 全部相等；目录内仅解压冻结副本。
- 缺失编号：`Desync-466.zip`、`Desync-474.zip` 不在工作区，本批次无法分析。
- 环境：RimWorld `1.6.4850 rev646`，Multiplayer `0.11.5+a481546`，async time + multifaction，
  2 名玩家；host 为 debug 模式，client 为 release（`Multiplayer Debug Build - Client=False`）。
- 本批次日志中未再出现此前 445-454 批次的 `Configs match: False` / `Connecting anyway`；
  `Configs match` 基线问题已通过 XML 配置热同步关闭。

| 包 | SHA-256 |
|---|---|
| Desync-462 | `A3E34A8F6D81E033E5B132B740C5441EB2DB50259463996AF1622385EBA6668B` |
| Desync-463 | `EF877D35A0E5E8A83B7AF64B5AEA127EA42B94EA03485D0CD305254584632D3A` |
| Desync-464 | `CFFE15419E6F18ECB3774EB6AA2E3AA44545692C014DF3161A9E69781DC72FEC` |
| Desync-465 | `0F2578817C67F9794410B8CB213225819AD7DAB7493D6F926F6BE751A44C9B9B` |
| Desync-467 | `32A9F269655748E7319A06B08806DD4FFDF87D825E8C77FE917DCBE74E329816` |
| Desync-468 | `5A3C38E205F968D8FBB2A74ACECCAAD52E6C42B3BB972DC693AC9EE65273224B` |
| Desync-469 | `0E3C8924BE234E0BFD5DACE048742FBCA18D362A3724C4A10A1636CC3A04B788` |
| Desync-470 | `9109D0C97CCFA500554BAD52140737069ACB8F3FAB0C55805E09179903E02FFA` |
| Desync-471 | `00441391AD215FF737D14EAE4D05E5B0B12CF9E0DC0A778A18B3D9B6810D2B8E` |
| Desync-472 | `893D0DA3A9B00DB4B30961B31498FB4B2C881D1844818895544DE6B16B14FC7C` |
| Desync-473 | `B7B86712DAD42889811E8F92A771071CCEB24197F27EA7FCCD474BD8440C7394` |
| Desync-475 | `B97FECB4B3E123C5EB240F55866316D13F92AD0619C9194FB9A4EF7AAA4C414D` |

## 每包最终会话

`local_logs.txt` 是同一客户端的累计日志，因此每个包只取最后一次 `Game loaded` 之后到该包
`Desynced after last valid tick` 的最终会话，不复用旧会话记录。

| 包 | 最后有效 tick | 掉线 tick | 掉线类型 | 首个分歧（最终会话） |
|---|---:|---:|---|---|
| 462 | 8268751 | 8268817 | Trace hashes | host 为 Boomalope `GetNextJobID`，local 为 `Building_HoldingPlatform.Tick -> MTBEventOccurs` |
| 463 | 8286781 | 8286865 | Random state from commands | EliteRaid 袭击后在 map 56 执行；host/local 39 条 map trace 完全相等 |
| 464 | 8300731 | 8300790 | Wrong random state on map 1 | 两端同一 tick 生成不同 thingID 的 Milira 粒子弹 |
| 465 | -1 | 8304360 | Random state from commands | trace 0 条；读档后立即掉线 |
| 467 | 8290021 | 8290083 | Trace hashes | host `Thing.SpawnSetup`，local `HoldingPlatform` map Rand |
| 468 | 8297161 | 8297220 | Random state from commands | host `GetNextThingID`，local `Thing.DeSpawn` |
| 469 | 8306941 | 8307002 | Wrong random state on map 56 | host `Building_TurretGun.TryFindNewTarget`，local Milira 火箭炮 `GetNextThingID` |
| 470 | 8309063 | 8310843 | Random state from commands | caravan `CaravanArrivalAction_Enter` 附近；host/local 同一 JobID 栈但 rngState 已不同 |
| 471 | -1 | 8313900 | Wrong random state on map 56 | host `HoldingPlatform` Rand，local Milira 重粒子炮 `GetNextThingID` |
| 472 | 8336131 | 8336192 | Wrong random state on map 62 | Gravship 落地 map 62 后；host Milira 火箭炮 `GetNextThingID`，local `HoldingSpot` Rand |
| 473 | -1 | 8338590 | Random state from commands | 重连后有 23 条 map 命令；host/local 同一 JobID 栈但 rngState 不同 |
| 475 | 8426701 | 8426761 | Wrong random state on map 64 | host `Rand.get_Int`（Spectator），local Axolotl `GetNextThingID` |

## 根因分类

### 1. Trace-only 分歧（462、467，以及 475 日志中较早的 8370513）

`CheckForDesync` 在检查命令/世界/地图 Rand 状态之后才比较 trace hash；报 `Trace hashes don't match`
说明采样到的地图/世界/命令 Rand 状态仍一致。这类包只证明 trace 栈或 trace 数量不同，未证明模拟状态
已分叉。结合 host debug 构建、client release 构建、Harmony 包装帧深度不同，历史上 269-273 已把同类
现象归为“trace-only 伪差异”，本次同样不能据此补模拟代码。

### 2. 命令 Rand 状态分叉（463、465、468、470、471、473）

`Random state from commands doesn't match` 表示某一端的地图命令未执行、多执行或顺序不同。触发点包括
EliteRaid 袭击、caravan 进入、Gravship 落地、重连后 map 命令队列（`Init map with cmds 18/23`）。
这些包没有 host 日志，且多数 trace 窗口为空或完全相同，无法把首分歧定位到具体命令。已有
`Patch_CommandOrderDeterminism` 只保证命令遍历的地图顺序，不能修复“命令本身缺失/重复/被拒绝”的分歧。

### 3. 地图 Rand 状态分叉（464、469、472、475）

首分歧集中在 per-map TickList 的 tick 顺序/成员差异：同一 tick 一端在给 pawn 建 Job 或生成投射物，
另一端在 tick `Building_HoldingPlatform` / `Building_HoldingSpot`。这类现象与“重连后 TickList bucket
内注册顺序不同”一致。此前 342/343 已证明：用 `listerThings` 在客户端重建 TickList 会把 joiner 的
bucket 成员改得与长驻 host 不同，因此当前工作区已把 `Patch_DeterministicTickList` 的运行时重建关闭。
关闭后本批仍出现同类分叉，说明“直接重建成员”不是稳定方案；仅排序已有 bucket 是否足以修复，缺少
host 日志和干净重连基线，不能判定为已验证的稳定边界。

## 共同污染源

最终会话读档阶段反复出现：

- `Tried to register the same load ID twice: null, pathRelToParent=/codedPawn, parent=RK_InfernoGrenadeLauncher...`
  与 `Could not get load ID ... /codedPawn`：Ratkin Weapons+ 的武器内置 coded pawn 每次读档重复注册，
  是一个长期存在的第三方存档/加载问题。
- `Could not resolve reference to object with loadID Map_56 / TransportShip_52 / GameCondition_ForceWeather_*`
  与 `WorldObject_* is referenced ... but is not deep-saved`：快照/存档引用图不干净。
- `Could not find think node with key -1937076157`：某个人物存档 Job 的 `lastJobGiverKey` 在客户端
  `PostLoadInit` 无法解析，`jobGiver` 保持 null，而长驻 host 仍持有原节点，人物后续换 Job/开火决策
  可能两端不同。这是 445-454 分析中已标记的重连污染源，本次仍在 463/465/473 等会话出现。

## 补丁边界（只覆盖顺序分叉）

1. 地图 Rand 分叉的最窄候选是 TickList 顺序；本次补丁只做顺序归一化，不复用 342/343 已证明会产生
   host/client 差异的“按 `listerThings` 重建成员”逻辑，因此不会改变成员关系或 bucket 分配。
2. 命令 Rand 分叉缺少 host 日志和命令级 trace，不能确认是 EliteRaid/caravan/Gravship 中哪一个命令
   在一端被丢；本次不补。
3. `Could not find think node`、`Map_56` 悬空引用、coded pawn 重复 load ID 都发生在读档阶段，
   属于快照/存档引用污染；本次不补，需在快照生成端清理。

## 本次代码修改

新增 `Source/MP_MeowOnlineShop/Patch_TickListOrderNormalizer.cs`，并在
`Patch_SellSlingshot.ApplyPatch` 中于 `Patch_DeterministicTickList.Apply` 之后调用：

- 只作用于 multiplayer，不改单机行为。
- 在 `TickList.Tick` 的 prefix 中，先把 `thingsToRegister` 按 vanilla `Thing.GetHashCode()`
  的 bucket 规则预合并到原 bucket，并插入到稳定位置，再清空 pending，避免 vanilla 重复追加。
- 对即将执行的 bucket，在 vanilla tick 前按 `thingIDNumber` 排序；`thingIDNumber == -1` 的
  视觉 mote 保留在 ID 事物之后，避免 peer-local object hash 重排模拟相关 ticker。
- 额外 hook `AsyncTimeComp.FinalizeInit`：重连读档创建 per-map 组件后立即把
  `tickListNormal/Rare/Long` 的全部 bucket 排序，保证第一批命令和 tick 也处于稳定顺序。
- Gravship 起飞边界的 `Patch_DeterministicTickList.RebuildLifecycleTickList` 不再按
  `listerThings` 重建成员，改为调用本补丁的 `NormalizeAllBuckets` 只排序；移除 342/343
  已判定会分叉的最后一条成员重建路径，同时保留生命周期边界的顺序归一化。
- 不重建 `thingLists`，不从 `listerThings` 推导成员，不移动任何事物到别的 bucket。

静态校验：`dotnet build -c Release -p:OutputPath=C:\tmp\mp-build-462-475-tickorder\bin` 成功
（0 警告 0 错误）；`ilspycmd` 确认程序集包含 `Patch_TickListOrderNormalizer` 的
`Apply/TickPrefix/MergePending/InsertSorted/FinalizeInitPostfix/NormalizeAllBuckets`。
候选 DLL SHA-256：`2150692F236B53C6A4556D3E0B051D5B7F5F817C5C90E0B5F94FD40E13F20C61`。

## 补丁覆盖核对（静态）

对地图 Rand 分叉类包，按 trace 中首个分歧事物的 `thingIDNumber` 排序后，两端应落在同一 tick 顺序：

| 包 | 首分歧对 | 排序后先 tick |
|---|---|---|
| 462 | host `Boomalope2707289` / local `HoldingPlatform2227404` | `HoldingPlatform2227404`（与 local 原顺序一致） |
| 464 | host/local 都生成 `MiliraBullet`，但 thingID 不同 | 上游射击者 tick 顺序一致后，投射物 ID 不再错位 |
| 467 | host `Axolotl2483033` / local `HoldingPlatform2227404` | `HoldingPlatform2227404` |
| 469 | host `AT_ErodedSniperBase2705064` / local `MiliraTurret2422042` | `MiliraTurret2422042` |
| 471 | host `HoldingPlatform2227404` / local `MiliraTurret1933462` | `MiliraTurret1933462` |
| 472 | host `MiliraTurret2427173` / local `HoldingSpot2546945` | `MiliraTurret2427173` |
| 475 | host 为非 thing 上下文的 Spectator Rand / local `Axolotl467273` | 首条 trace 不是 bucket 成员，属上游顺序分叉的下游表现 |

命令 Rand 分叉类（463/465/468/470/471/473）不直接落在 bucket 排序上；本补丁通过消除
“同一 Normal bucket 内先 tick 谁”的上游错位，降低它们被再次触发的概率，但不承诺直接覆盖。

## 475 的独立缺陷（未作为掉线根因采用）

475 最终会话之前的会话中反复抛出：

```text
SyncMethod MP_MeowOnlineShop.Patch_PerspectiveShiftMp::SyncSetMoveIntent(...): map mismatch (1 and 64)
SyncMethod MP_MeowOnlineShop.Patch_PerspectiveShiftMp::SyncAvatarMapClick(...): map mismatch (1 and 64)
```

原因是 PerspectiveShift 兼容层把同步方法注册为 `SyncContext.CurrentMap`，而 `Pawn` 参数序列化时会把
命令地图改成 `pawn.Map`；当本地当前地图（1）与 avatar pawn 所在新地图（64）不同时，`DoSync` 在发包前
抛异常。这是已证实的功能性缺陷，会持续吞掉该玩家的移动/点击输入。但本批次没有证明该异常导致最终
desync，且去掉 `CurrentMap` 后鼠标格是否仍按 avatar 地图解释未经验证，因此本次不把它作为“解决不同步”
的补丁采用。

## 后续建议

1. 先获得同一会话的 host 日志，并确认两端加载同一 `MP_MeowOnlineShop.dll` 哈希、同一 Multiplayer 构建
   类型（host 不再使用 debug 构建）。
2. 用配置一致、无 `Could not find think node`、无 `Map_56/TransportShip_52` 悬空引用的干净存档重跑
   重连循环，确认是否仍掉线。
3. 运行验证时，对比两端启动后的 `TickList order normalized: buckets=..., ided=..., unided=...`
   一次性日志，以及现有 `Patch_AsyncRandStateDiagnostic` 的 `mapTicks/rand` 记录；若顺序分叉消失但
   仍掉线，下一步应把重点移到命令 Rand 分叉和快照引用污染。
4. 若证明是成员/引用分叉，则优先修快照生成端（coded pawn 去重、清理已删除地图引用、无法解析的
   `jobGiverKey`），而不是再包住线。

本次仅调整 `Source/` 代码，未部署 DLL，未运行 host/client 测试。
