# Desync-480 分析：匪徒营地 BogHound 分支复现（2026-08-18）

## 结论

480 是 477 的“BogHound 单端建 Job”分支复现，不是 478 的 bucket 顺序分叉。现有
`Patch_TickListOrderNormalizer` 对 478 有效，但没有解决 480 的 pawn Job/路径状态差异。

480 的 local 日志确认客户端已加载补丁（`TickList order normalized` / `TickList bucket order
fingerprint`），但首个分歧仍是：

| 端 | 首个分歧（num=30，tick=8470350） |
|---|---|
| host | `BogHound2814349` `GetNextJobID`（路径到达后换 Job） |
| local | `PowerNet.DistributeEnergyAmongBatteries`（MapPostTick，之前无 JobID trace） |

这与 477 完全相同（BogHound 路径到达只在 host 端发生），因此不能把 480 当作补丁失败的新证据，
除非能证明 host 也运行了同一候选 DLL。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-18-480-bandit-camp-r2/`
- `Desync-480.zip` SHA-256：`2EE83B864312023E8D788F7968D8553E13179A9E1834EB19D08D0E796F3CE1AF`
- 环境：RimWorld `1.6.4850 rev646`，Multiplayer `0.11.5+a481546`，async time + multifaction，
  3 张地图，2 名玩家。
- 掉线：`Player254 8470352 Desynced after last valid tick 8470291: Trace hashes don't match`

## 补丁状态与日志修正

本工作区继续采用 `Patch_TickListOrderNormalizer.cs`，并修正了一个诊断缺口：

- 之前 `AsyncTimeComp.FinalizeInit` 把 bucket 标记为已排序后，`TickPrefix` 不再输出
  Normal bucket 的 fingerprint，480 的 local 日志只有 Rare/Long 的 `members=0` 指纹。
- 现在 `TickPrefix` 在**第一次真正 tick 时无论 bucket 是否已排序**都会输出
  `TickList bucket order fingerprint`，因此下一次重跑能直接对比两端 Normal bucket 的成员数与
  FNV 指纹，区分“成员不一致”还是“成员相同但 Job/路径状态分叉”。
- fingerprint 日志新增 `missingSpawned`：从该 map 的 `listerThings` 统计“应有但不在 TickList
  里的 ID 事物数”。若 481 的 Normal fingerprint 显示 `missingSpawned=0`，则 477/480 是
  Job/路径状态分叉；若 `missingSpawned>0`，则先修 TickList 成员同步。

候选 DLL（重新构建）SHA-256：
`D80663D63A8B5C10AA6FE99419C5EC3256149178475A01C383945C8998B3998D`

## 下一步

1. host/client 必须使用同一候选 DLL，且启动后都出现补丁日志。
2. 重进匪徒营地后对比两端 Normal bucket fingerprint：
   - fingerprint/成员数一致 -> 477/480 是 Job/路径状态分叉，下一步查 pawn 路径恢复；
   - fingerprint 不一致 -> 先修 TickList 成员同步，再谈顺序。
3. 需要 host 日志才能确认 host 端 BogHound 是否确实不存在于 local 的 TickList/listerThings。

本次未部署 DLL，未运行 host/client 测试。
