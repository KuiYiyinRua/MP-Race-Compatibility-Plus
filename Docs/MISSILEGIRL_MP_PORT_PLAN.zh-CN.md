# MissileGirl 内容移植方案

## 方向

用户明确要求采取“移植”而不是“兼容”：把 MissileGirl 中可验证的优化内容写进
`MP-meow-online-shop`，使用本模组自己的 Harmony owner，不依赖 MissileGirl 本体，
也不把“进入联机后卸载 MissileGirl 补丁”当作目标。

## 已移植

### 1. 成瘾/耐受 Hediff 休眠放宽（Soyuz）

移植 `WorldPawns.DefPreventingMothball` 中关于
`HediffDef.AlwaysAllowMothball` 的修改：成瘾类或 defName 以 `Addiction` /
`Tolerance` 结尾的 Hediff 不再阻止世界 Pawn 休眠。

- 实现：`HediffDef.AlwaysAllowMothball` getter 的 postfix
- 确定性强：只依赖 `HediffDef` 元数据，无进程缓存，重连安全
- 设置：`enableMpMissileGirlMothballPort`，默认开启

### 2. 动态美观采样半径（Cosmodrome）

移植 `BeautyUtility.FillBeautyRelevantCells` 的采样数量调整：Pawn 在床/倒地时
使用小采样半径，静止越久采样半径越大。

- 实现：`FillBeautyRelevantCells` transpiler +
  `Pawn_NeedsTracker.NeedsTrackerTickInterval` prefix/finalizer
- 联机门禁：仅在真实 MP、非同步命令、单地图且未开 Async Time 时生效
- 设置：`enableMpMissileGirlBeautyPort`，默认开启

### 3. 时间表缺失防御（Cosmodrome）

移植 `Pawn_TimetableTracker.GetAssignment` 的 finalizer：当 modded 时间表定义
缺失导致异常时，回退到 `TimeAssignmentDefOf.Anything` 并写回，避免单端崩溃。

- 设置：`enableMpMissileGirlTimetableFix`，默认开启

### 4. Alert UI 降频（Proton）

Proton 的警报降频此前已移植为 `Patch_MpSafeAlertThrottling`，不重复移植。

## 未移植及原因

- `StatWorker` / `StatPart_ApparelStatOffset` 缓存：进程本地缓存，重连后主机与
  客户端缓存状态不同，且脏通知覆盖无法证明完整。
- `GenTemperature` 缓存：单 Tick 内状态可能变化，旧值会进入 AI/工作分配。
- `CorpsesTracker`：使用相机可视区域与 `Rand.Chance`，属于客户端输入驱动的
  模拟变更。
- `CompDeepDrill`：用 `Biome.hasBedrock` 替换原版当前位置资源查询，结果不等价。
- `ListerBuildingsRepairable`：跳过条件与原版集合维护不等价。
- `Lord.Notify_PawnDamaged`：跳过伤害信号，改变战斗状态机。
- Gagarin Def/XML 启动缓存：改变加载期 Def 树，影响 MP 握手哈希。

## 验证

使用用户无红字列表（`H:/桌面/list.xml`，223 个活动模组，未加载 MissileGirl 本体）
做固定快速世界主机/客户端短测，三个移植项全部默认开启：

- 主机：`COMPLETE role=host desynced=False tick=19956 elapsedTicks=11287 maps=1`
- 客户端：`COMPLETE role=client desynced=False tick=19885 elapsedTicks=10000
  realtimeSeconds=337.773 measuredTps=29.61 players=2 maps=1`
- 两端日志：`MissileGirl port initialized: mothball=True, beauty=True,
  timetableFix=True.`
- 未出现 `desynced=True`、`Sync Error`、`Inconsistent player`、
  `PacketReadException`。

已部署 DLL SHA-256：

```text
08E85FDB721586E830FDF5A7A18186D91A4CB4F618AFC382BC6D676D257128CE
```
