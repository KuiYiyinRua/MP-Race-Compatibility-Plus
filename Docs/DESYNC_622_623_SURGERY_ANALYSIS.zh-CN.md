# Desync-622 / Desync-623 手术边界分析

## 证据

- 原始日志包位于项目根目录，未修改。
- 冻结副本：`TestValidation/DesyncEvidence/2026-08-22-622-623-surgery/`。
- `Desync-622.zip` SHA-256：`31A7D7A910467DF4F6CC38098731B249A8B003900AA720EB813E7BAD6D54CA51`
- `Desync-623.zip` SHA-256：`5F3814AC6A1234481E3FA7179F85461DE2BB341182C1E074C7AEFF14A2C34E8E`
- 两份包均为 Multiplayer `0.11.5+a481546`、RimWorld `1.6.4850 rev646`，2 玩家、2 地图，Async time 与 Multifaction 开启。

## 结论

两份日志都把不同步前的执行路径指向了手术菜单/手术工作边界，但最早可见的差异不是一个孤立的随机数调用：

- 622 在最后有效 tick `11718181` 后，约 40 ticks 内出现手术选项生成/确认路径；首个 trace 差异在 tick `11718212`，一侧进入野猪换 Job 并消耗新的 Job ID，另一侧仍在 HoldingPlatform 的随机 tick 中。
- 623 的首个会话同样先经过手术 UI 路径。重连后的会话在 tick `11726847` 的 `Bill_Medical.Notify_BillWorkStarted` 分支上出现差异：主机进入麻醉并记录 Tale，另一侧先执行药物 `SplitOff`。这是状态已经分叉后的医疗工作边界证据，不能单独当作原始根因。

上游 Multiplayer 已经把 `HealthCardUtility.CreateSurgeryBill` 注册为同步点，并明确避免直接同步手术菜单回调；因此再对手术 FloatMenu 做通用包裹，会把 UI 取消/卸载逻辑叠加到原生手术同步边界上。

项目依赖的 GodHands 补丁还有一个更具体的副作用：`CheckSurgeryFail` 前缀通过 `MapComponent_GodAssistant.GodHandWorker` 属性读取 God Hand pawn，而该属性在字段为空时会懒生成 pawn。这个 getter 会在普通手术检查阶段引入未保存的 pawn、随机数和唯一 ID 副作用，正好可能把手术确认后的后续 Job/医疗分支推向不同状态。

Axolotl 的 `Recipe_RemoveBodyPart.ApplyOnPawn` 前缀也在两端被观察到，但它只写入保存的冷却/hediff 状态，没有直接的随机调用；当前证据不足以把它当作首个分叉点，因此没有扩大补丁范围去盲目重写它。

## 本次调整

1. 新增 `Patch_MedicalSurgeryCompat`：仅在 Multiplayer 下，将 GodHands `CheckSurgeryFail` 前缀内对 `GodHandWorker` 的调用替换为非生成式字段读取。真实 GodHands 手术任务仍会先准备 worker，因此保留其正常行为；普通手术检查不再因 getter 懒生成 pawn。
2. 将该兼容补丁加入启动注册，并纳入项目编译项。
3. 修改 Milian 的通用 FloatMenu 兜底补丁，明确排除 `HealthCardUtility`、`MedicalCareUtility`、`MedicalRecipesUtility` 及 `GenerateSurgeryOption` 相关选项，让原生 Multiplayer 手术同步保持唯一 UI 边界。

本次未构建、未部署、未运行测试；没有修改根目录原始 ZIP，也没有回滚工作区中其他已有改动。
