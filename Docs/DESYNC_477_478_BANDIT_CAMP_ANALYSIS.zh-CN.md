# Desync-477 / 478 分析：匪徒营地任务点重连循环（2026-08-17）

## 结论

477/478 都发生在“第三个地图（map uniqueID `19812000`，对应匪徒营地/WorkSite 任务点）已生成后，
客户端读档重连”的会话里；掉线类型均为 `Trace hashes don't match`，地图/世界/命令 Rand 状态在采样点上
仍一致，真正分叉的是 UniqueID/Job 流。

采用的稳定补丁是已在本工作区落地的 `Patch_TickListOrderNormalizer`：

- 不改 TickList 成员、不改 bucket 分配，只把每个 bucket 内 tick 顺序按 `thingIDNumber` 归一化；
- `AsyncTimeComp.FinalizeInit` 后立即排序全部 per-map TickList；
- Gravship/WorkSite 地图生命周期边界只排序、不再按 `listerThings` 重建成员。
- 每个 TickList 第一次真正 tick 时输出一次 `TickList bucket order fingerprint`（bucket 数、当前
  index、成员数、FNV 指纹），用于双端同版本重跑时直接对比 bucket 顺序。

478 的 trace 直接证明了这个补丁对应的规律；477 还有一条未完全闭合的“host 端 BogHound 建 Job、
local 端没有路径到达”分支，需要 host 日志或双端同一 DLL 的重跑才能确认是否仍存在。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-17-477-478-bandit-camp/`
- `Desync-477.zip` SHA-256：`CEBC1CDF0C454C144A0D7F0B1B9B11513B2B9B8199F6C4184074D5E97BFCF9AA`
- `Desync-478.zip` SHA-256：`51DA057A9D6B378A014FC441514726FB021A40475B33BD431496341115BFBDAD`
- 环境：RimWorld `1.6.4850 rev646`，Multiplayer `0.11.5+a481546`，async time + multifaction，
  3 张地图，2 名玩家；host debug 模式，client release。
- `Desync-479.zip` 也在工作区，但本次请求只覆盖 477/478，未纳入。

## 触发点

host/local 的 jitted methods 都在 t=8468701 编译并执行了：

```text
RimWorld.StorytellerComp_WorkSite.MakeIntervalIncidents
RimWorld.StorytellerComp_WorkSite+<MakeIntervalIncidents>d__2.MoveNext
RimWorld.QuestGen.QuestNode_Root_WorkSite.BestAppearanceFrequency
```

目标均为 map `19812000`，也就是进入匪徒营地任务点后生成的第三张地图。t=8468701 执行了 WorkSite
间隔事件，随后约 500 tick，首个 trace 分歧在 t=8469213（477）出现。

## 478：已证实的 TickList 顺序分叉

首个公共分歧记录（num=5，tick=8474736）：

| 端 | 内容 |
|---|---|
| host | `Rat2814348` `GetNextJobID`（Pawn.Tick -> PathFollower -> EndCurrentJob） |
| local | `Axolotl467273` `GetNextJobID`（同一栈） |

随后 local 在同一 tick 的 num=6 才 tick `Rat2814348`。也就是说两端都拥有这两个 pawn，且都在同一
Normal bucket，只是顺序不同：

- host：Rat 先，Axolotl 后；
- local：Axolotl 先，Rat 后。

按 `thingIDNumber` 排序后，`Axolotl467273 < Rat2814348`，两端都应先 tick Axolotl。这正是
`Patch_TickListOrderNormalizer` 消除的差异。477/478 的 local 日志中已经出现
`TickList order normalized`，说明客户端加载了该补丁；host trace 仍显示旧顺序，因此**双端 DLL
是否一致是本次判定的关键前提**。若 host 仍跑旧 DLL，客户端单独排序只会让两端差异更明显。

## 477：剩余的不闭合分支

首个公共分歧记录（num=2，tick=8469213）：

| 端 | 内容 |
|---|---|
| host | `BogHound2814349` `GetNextJobID`（路径到达后换 Job） |
| local | `PowerNet.DistributeEnergyAmongBatteries`（MapPostTick，之前没有 JobID trace） |

两端在此 tick 之前的 PowerNet Rand 完全一致（num=0/1），所以不是随机流先分叉；更像 host 端
BogHound 存在并正在寻路/到达，local 端该 pawn 的 Job/路径状态不同或不在同一 TickList 成员集合。
单纯 bucket 排序无法修复“一端有路径到达、另一端没有”的状态差异。477 需要 host 日志确认该 pawn
在 host/client 的 `listerThings` 与 TickList 成员是否一致，或者用双端同一候选 DLL 重跑后再看。

## 为什么采用现有补丁、不新增第二层

- 478 的规律已直接对应 `Patch_TickListOrderNormalizer`，且补丁不重建成员、不移动 bucket，
  避开了 342/343 已验证的 `listerThings` 重建坑。
- 477 缺少 host 日志和双端同版本证据，无法证明是“成员缺失”还是“Job/路径状态分叉”；按
  “只补已验证边界”的要求，不再新增按 `listerThings` 添加成员的未验证逻辑。
- WorkSite 间隔事件本身已在 `Patch_StorytellerIntervalDeterminism` 的确定性 Rand scope 内执行；
  477/478 的首分歧不是该 scope 内的 Rand 分歧，而是后续 pawn tick 的 JobID/顺序分歧。

## 代码状态

- 本批次采用 `Patch_TickListOrderNormalizer.cs`（已注册到 `Patch_SellSlingshot.ApplyPatch`）。
- `Patch_DeterministicTickList.RebuildLifecycleTickList` 已改为只调用 `NormalizeAllBuckets`。
- 静态校验：`dotnet build -c Release -p:OutputPath=C:\tmp\mp-build-462-475-tickorder\bin` 成功
  （0 警告 0 错误）；候选 DLL SHA-256：
  `885B2A307393DBB8D00A9F7A46C7AABEEFD4756D6D8968CB251BA5F29260DE50`。
- 未部署 DLL，未运行 host/client 测试。

## 下一步验证

1. host/client 使用同一候选 DLL（SHA-256 一致），重新进入匪徒营地任务点并重连。
2. 对比两端启动日志的 `TickList order normalized`、`TickList bucket order fingerprint` 与
   `Patch_AsyncRandStateDiagnostic` 的 `mapTicks/rand`。
3. 若 478 不再出现且 477 的 BogHound 不再单端建 Job，则本补丁覆盖该批次；若 477 仍单端出现，
   再用 host 日志定位该 pawn 在两端 TickList/listerThings 的成员差异。
