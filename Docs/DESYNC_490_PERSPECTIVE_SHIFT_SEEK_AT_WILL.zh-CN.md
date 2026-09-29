# Desync-490 分析：Perspective Shift 的 passively seek 未同步（2026-08-18）

## 结论

490 提供了决定性证据：同一 `Axolotl1028470`、同一 tick、同一 rngState 下，

- host 走 `JobGiver_SeekAllowedArea.TryGiveJob`（原版安全区寻敌）；
- local 走 `PerspectiveShift.JobGiver_SeekAtWill.TryGiveJobInt -> Pawn.TryGetAttackVerb -> ChooseMeleeVerb`。

这说明 PerspectiveShift 的 `State.seekAtWillPawns`（passively seek 开关）只在本端生效：
player 点击 `PS_SeekAtWill` 后，local 的 `ThinkNode_ConditionalSeekAtWill` 返回 true，host 仍返回
false，同一 pawn 于是走出完全不同的 Job/近战 Rand 路径。这是 487-489 里
`Axolotl Job vs HoldingPlatform`、`ChooseMeleeVerb` 分歧的共同上游。

## 修复

`Patch_PerspectiveShiftMp` + `PerspectiveShiftMpState`：

- 新增存档字段 `PerspectiveShiftMpComponent.seekAtWillPawnIds`（`HashSet<int>`）并写入 ExposeData。
- 注册同步命令 `SyncToggleSeekAtWill(owner, Pawn)`，切换两端 `seekAtWillPawnIds` 与
  `PerspectiveShift.State.seekAtWillPawns`。
- `Pawn.GetGizmos` 后置：将 `PS_SeekAtWill` 的 `toggleAction` 改为同步命令，`isActive` 读取存档值。
- `PerspectiveShift.State.ShouldSeekEnemy` 前缀：MP 下改用同步 registry，避免静态 set 只在本端变化。
- `FinalizeInit` 时把组件存档值回填到 `State.seekAtWillPawns`，保证重连后两端一致。

不改动 PerspectiveShift 本体，不改单机行为；所有变更都在 multiplayer 分支内。

## 代码状态

静态构建通过（0 警告 0 错误）。候选 DLL SHA-256：
`278AAF3153C992A3B429912FF069608FB1AF2C502639938666DB647D074F6785`

未部署，未运行 host/client 测试。

## 验证建议

1. host/client 都部署该候选；
2. 在蹒跚怪袭击/普通战斗中点击 `PS_SeekAtWill`；
3. 确认两端 `ShouldSeekEnemy`/`JobGiver_SeekAtWill` 一致，且无 `Wrong random state` / trace 分歧；
4. 若 487-489 的 HoldingPlatform/ChooseMeleeVerb 分歧消失，即证明 seek-at-will 是这批的共同根因。
