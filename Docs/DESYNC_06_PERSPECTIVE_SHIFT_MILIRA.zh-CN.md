# Desync-06 Perspective Shift + 米莉拉武器分析（2026-08-05）

## 证据

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-05-Desync-06-PerspectiveShift-MiliraWeapon/`
- `Desync-06.zip` SHA-256：
  `248AB2950126CE15F32D4085DF48A358527A5917A9A64DC15234CC771C50F220`
- Multiplayer：`0.11.5+a481546`；RimWorld：`1.6.4850 rev646`
- async time 开启、multifaction 开启、2 张地图、2 名玩家
- 最后有效 tick：`3825047`；掉线 tick：`3825094`
- 掉线类型：`Wrong random state on map 1`

## 最早分叉

两端都执行同一个同步命令
`Patch_PerspectiveShiftMp.SyncSetMoveIntent`
-> `PerspectiveShiftMpComponent.SetMoveIntent`
-> `EnsureMovementJob`。在该命令里：

- host：`EndCurrentJob` -> 思考树选出 `JobGiver_Orders`（Wait_Combat）
- local：`EndCurrentJob` -> 思考树选出 `JobGiver_MoveToStandable`

随后 `Pawn_JobTracker.StartJob` 触发米莉拉
`MilianPatch_Pawn_FlightTracker_Notify_JobStarted.Prefix`，为每次 job 启动分配
一个 HediffID 并消耗地图 Rand。思考树先选出的“中间 job”不同，HediffID 和
地图随机流立即错开。

## 根因

`EnsureMovementJob` 原来的写法是：

```text
EndCurrentJob(JobCondition.InterruptForced);
Job job = JobMaker.MakeJob(JobDefOf.Goto, destination);
pawn.jobs.TryTakeOrderedJob(job);
```

`EndCurrentJob` 和 `TryTakeOrderedJob` 都会在真正启动 Goto job 前让
`Pawn_JobTracker.TryFindAndStartJob` 跑一遍思考树，产生一个中间 job。该
中间 job 的选择依赖 pawn 的征召状态、武器/飞行组件状态等；米莉拉武器 pawn
在两端征召状态不一致时，一端选 `JobGiver_Orders`、另一端选
`JobGiver_MoveToStandable`，于是同一 tick 的 JobID/HediffID/地图 Rand 分叉。

## 修复

修改 `PerspectiveShiftMpState.cs` 与 `Patch_PerspectiveShiftMp.cs`：

1. `EnsureMovementJob` 不再 `EndCurrentJob` + `TryTakeOrderedJob`，改为直接
   `pawn.jobs.StartJob(gotoJob, JobCondition.InterruptForced)`，原子替换当前
   job，不让思考树在同步命令里生成中间 job。
2. `PreparePawnForControl` 的初始 Wait job 同样改用 `StartJob`。
3. `StopMovementJob` 停止移动时用确定性 Wait job 的 `StartJob` 替换当前
   movement job。
4. 同步右键打断与储物拖放路径里的 `EndCurrentJob` 改为
   `startNewJob: false`，避免在命令内部触发思考树选 job。

这样两端在 Perspective Shift 移动命令里只会分配同一个 Goto/Wait JobID，
米莉拉飞行追踪器的 Hediff 创建也固定为同一次。

## 验证状态

- `dotnet build`（Release，输出到 `C:\tmp\mp-build-desync06\bin`）：0 警告，
  0 错误。
- 候选 DLL SHA-256：
  `7263F72AE802BAABD622DDC6C9E6A999FACFB18E92CBA8A7C47915DEE60F3FF5`
- 程序集版本仍为 `3.0.117-config-hot-sync`；`ilspycmd` 确认
  `PreparePawnForControl`/`StopMovementJob`/`EnsureMovementJob` 均改为
  `StartJob`。
- 按用户要求：未部署正式版 DLL，未运行 runtime 测试。后续仍需真实
  host/client 的 Perspective Shift + 米莉拉武器移动/射击 targeted smoke +
  长 soak 验证。
