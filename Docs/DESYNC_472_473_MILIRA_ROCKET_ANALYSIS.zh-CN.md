# Desync-472 / 473 分析（2026-08-17）

## 结论

472/473 不是单一武器入口漏同步，而是 **async 多地图调度/炮塔状态在 trace 窗口
之前已经分叉**，火箭炮塔只是最早暴露 Rand 差异的消费者。

- 472：host 在 tick `8336173` 由 `MiliraTurret_HeavyRocketLauncher` 发射 8 连火箭，
  local 在 tick `8336174` 由 `AncotLibrary.Verb_ChargeShootSustained` 发射连射，
  两端在同一共享 Timer 相邻 tick 各打各的，地图 Rand 状态分叉（
  `Wrong random state on map 62`）。
- 473：`Pawn_DraftController.set_Drafted` 同步命令重放时，local 给 pawn 选
  `WorkGiver_DoBill` 的 Job，host 选 `WorkGiver_Repair` 的 Job，导致
  `Random state from commands doesn't match`；这是人物 Job 状态先分叉的下游结果。

因此不能通过“把某一把火箭炮塔的 TryCastShot 同步”来修，那只是症状。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-17-472-473-milira-rocket-turret/`
- `Desync-472.zip` SHA-256：
  `893D0DA3A9B00DB4B30961B31498FB4B2C881D1844818895544DE6B16B14FC7C`
- `Desync-473.zip` SHA-256：
  `B7B86712DAD42889811E8F92A771071CCEB24197F27EA7FCCD474BD8440C7394`
- Multiplayer `0.11.5+a481546`，RimWorld `1.6.4850 rev646`，async time +
  multifaction，2 张地图。

## 为什么不能直接补炮塔

已核对现有覆盖：

- `Patch_AncotSpinTurretMp`：holdFire SyncField、OrderAttack/ResetForcedTarget
  sync、TryStartShootSomething 确定性 Rand。
- `Patch_AncotTurretRandIsolation`、`Patch_ProjectileLauncherDeterminism`、
  `Patch_MiliraWeaponMode`：覆盖投射物保存/Impact 与 Milira 武器模式。
- `Patch_CommandOrderDeterminism`：RunCmds/DoTick 期间按 uniqueID 排序地图。
- `Patch_AsyncTickSchedulerPhase`：归一化 TimeToTickThrough。

但 472 的 trace 显示两端在同一共享 Timer 上出现在不同地图/不同武器路径，说明
**调度顺序或 mapTicks 偏移先于炮塔开火**；473 则是命令重放时人物 Job 状态已经
不同。两者都没有在 trace 窗口内给出可验证的“单端入口”证据，缺失 host 日志，
无法证明是哪一端先分叉。

## 方案

1. 先确保 host/client 使用**同一个** `MP_MeowOnlineShop.dll`（host 是 debug MP
   构建，客户端是 release），并在两端启动日志确认：
   `Command-order determinism active`、`Async tick scheduler phase guard active`、
   `Ancot spin turret MP patch active`。
2. 在干净配置基线上，用单地图或关闭 async time 重跑火箭炮塔场景，确认是否仍
   复现；如果单地图/非 async 不复现，则可定位为 async 多地图调度问题。
3. 若双地图 async 下仍复现，下一步给两端加命令行诊断：在同一共享 Timer 打印
   `TickPatch.AllTickables` 的地图顺序、每张地图 `mapTicks` 与 `DesiredTimeSpeed`，
   确认是地图迭代顺序不同还是 mapTicks 偏移不同，再针对性补 `AllTickables` 排序
   或重连速度归一化。

本次未修改 `Source/` 代码，未部署 DLL，未运行 host/client 测试。
