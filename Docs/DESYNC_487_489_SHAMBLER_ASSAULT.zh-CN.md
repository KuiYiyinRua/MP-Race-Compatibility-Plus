# Desync-487 / 488 / 489 分析：蹒跚怪袭击（2026-08-18）

## 结论

487-489 都是“蹒跚怪袭击”（Anomaly `ShamblerAssault`）触发后的地图 Rand 分叉，掉线类型转为
`Wrong random state on map X`。三个包不是同一个入口，至少分两类：

- 487 / 489：仍是 per-map TickList bucket 顺序分叉（`Building_HoldingPlatform` 对植物
  DeSpawn / pawn Job），可被现有 `Patch_TickListOrderNormalizer` 覆盖。
- 488：host/local **map tick 差 1**（host tick 8536048 / local tick 8536049），随后 host 在
  `Pawn_MeleeVerbs.ChooseMeleeVerb -> Rand.Chance` 消费地图 Rand，local 仍在
  `HoldingPlatform.Tick`。这属于调度相位/mapTicks 偏移类，不是 bucket 顺序。

487-489 的 local 日志里没有 `missingSpawned`，且始终没有 Normal bucket 的 fingerprint，说明
这些包运行的是**早于最新 D806 候选**的 DLL，不能用来验证最新诊断，也不能用来证明补丁失败。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-18-487-490-stumbling/`
- `Desync-487.zip` SHA-256：`FEBA6021670D29311BFDE06BB2C732FB8B7B22043E363E777AE71426033B867D`
- `Desync-488.zip` SHA-256：`17BDDD2E4B9945586224E8509000B698DD7B2D6D76A118E3DDB4B05386CFD71F`
- `Desync-489.zip` SHA-256：`3CE1166EE89680663D75CF20351706C71D4607A1D4CFA8095746464CFB214B7C`
- `Desync-490.zip` 不在工作区，本次无法分析。
- 环境：RimWorld `1.6.4850 rev646`，Multiplayer `0.11.5+a481546`，async time + multifaction，
  2 张地图，2 名玩家。

## 每包最终会话

| 包 | 最后有效 tick | 掉线 tick | 类型 | 首个分歧 |
|---|---:|---:|---|---|
| 487 | 8520331 | 8520393 | Wrong random state on map 1 | host `Plant.DeSpawn`（SpacedroneIncoming），local `HoldingPlatform.Tick` |
| 488 | 8535991 | 8536052 | Wrong random state on map 64 | host tick 8536048 `ChooseMeleeVerb` Rand.Chance，local tick 8536049 `HoldingPlatform`；mapTicks 差 1 |
| 489 | 8539561 | 8539629 | Wrong random state on map 64 | host `HoldingPlatform.Tick`，local `Axolotl1028470 GetNextJobID` |

## 与蹒跚怪袭击的关系

日志中的“蹒跚怪哈里斯/哈维/诺布/马蒂”以及 `ShamblerAssault` 本地化字符串，确认触发场景是
Anomaly 蹒跚怪袭击。488 的 `JobGiver_AIFightEnemy -> ChooseMeleeVerb` 正是蹒跚怪/人类近战单位
在交战时消费地图 Rand 的路径。

但 488 的 mapTicks 差 1 说明问题可能在“袭击发生时两端的 per-map 时间相位/调度”已经错位，
近战 Rand 只是第一个暴露点。`Patch_AsyncTickSchedulerPhase` 已生效但仍出现该偏移，需要
`AsyncRandStateDiagnostic` 或下一次 D806 的 fingerprint 对比才能定位是 mapTicks 还是 TickList。

## 代码状态

- 继续采用 `Patch_TickListOrderNormalizer.cs`（bucket 顺序归一化 + FinalizeInit 排序 +
  fingerprint/missingSpawned 只读诊断）。
- 不新增“包住 `ChooseMeleeVerb`”的补丁：488 的 Rand 分歧发生在 mapTicks 已经差 1 之后，
  直接隔离 Rand 会掩盖真实调度偏移，不是稳定边界。
- 未部署 DLL，未运行 host/client 测试。

## 下一步

1. host/client 都部署最新候选 DLL：
   `D80663D63A8B5C10AA6FE99419C5EC3256149178475A01C383945C8998B3998D`
2. 重跑蹒跚怪袭击，对比 Normal fingerprint 的 `missingSpawned`：
   - 若 487/489 消失，TickList 顺序补丁覆盖这两类；
   - 若 488 仍存在且 host/local mapTicks 差 1，下一步专攻 async 调度相位/mapTicks 偏移。
