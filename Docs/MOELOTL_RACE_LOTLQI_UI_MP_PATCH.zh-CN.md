# MoeLotl Race 螈气总览 UI 联机补丁说明

日期：2026-08-16  
目标：修复 MoeLotl Race 专属 `Gizmo_LotlQiOverView` 按钮在联机下不同步的问题，不测试、不部署。

## 源权记录

- 模组：MoeLotl Race  
- packageId：`HenTaiLoliTeam.Axolotl`  
- 工坊 ID：3292351432  
- 已安装程序集：`1.6/Assemblies/Axolotl.dll`  
- 程序集 SHA-256：`2ED1E8B1370F36790D82D9E2CD676DA14421A98DB4E018954445F8C327CAC1F5`  
- 附带源码：`1.6/Source/Axolotl/Thing/ThingComp/ThingComp_MoeLotl/ThingComp_LotlQi/`  
- 反编译：`TestValidation/ilspy-20260816-moelotl-ui/`（ILSpy 10.1.1，针对已安装 DLL）

附带源码与反编译结果一致：UI 点击逻辑位于 `Gizmo_LotlQiOverView.GizmoOnGUI` 内的本地函数，调用 `CompAxolotlEnergy` 的属性和 Action 方法。

## 根因

现有 `Patch_AxolotlWeaponMode.LotlQiOverviewGizmoTranspiler` 只作用在 `GizmoOnGUI` 主体。Roslyn 会把本地函数编译成独立的实例方法，因此按钮回调里的实际调用位于这些方法中，主体 transpiler 看不到：

| 已安装 DLL 中的方法 | 对应按钮 | 原始调用 |
| --- | --- | --- |
| `<GizmoOnGUI>g__Do|34_4` | 自动结晶 | `set_IsAutoSaveCrystal(bool)` |
| `<GizmoOnGUI>g__Do|34_0(bool)` | 手动结晶 | `Action_AddEnergyCrystal(bool)` |
| `<GizmoOnGUI>g__Do|34_1` | 释放结晶 | `Action_ReduceEnergyCrystal(bool)` |
| `<GizmoOnGUI>g__Do|34_5` | 自动充能 | `set_IsAutoResetShield(bool)` + `AutoResetShield()` |
| `<GizmoOnGUI>g__Do|34_3` | 手动充能 | `Action_ShieldReSet()` |

拖动护盾阈值条的直接调用仍在 `GizmoOnGUI` 主体内，原有 `set_TargetShieldCount` + `AutoResetShield` 替换已覆盖。

## 补丁

`Source/MP_MeowOnlineShop/Patch_AxolotlWeaponMode.cs` 的 `ApplyLotlQiOverviewSyncPatch` 现在会：

1. 继续对 `GizmoOnGUI` 应用 `LotlQiOverviewGizmoTranspiler`。
2. 通过反射枚举 `Gizmo_LotlQiOverView` 上名称以 `<GizmoOnGUI>g__Do` 开头、带 `CompilerGeneratedAttribute` 的实例方法。
3. 对每个本地动作方法应用同一 transpiler，把 `set_IsAutoSaveCrystal`、`Action_AddEnergyCrystal`、`Action_ReduceEnergyCrystal`、`set_IsAutoResetShield`、`AutoResetShield`、`Action_ShieldReSet` 替换为已有 `MaybeSync*` 包装。
4. 启动日志输出解析到的本地方法数量；数量为 0 时明确告警，避免静默漏补。

同步命令仍使用 mapIndex + pawn ThingID 定位 `CompAxolotlEnergy`，沿用已注册的 `SyncSetAutoSaveCrystal`、`SyncAddEnergyCrystal`、`SyncReduceEnergyCrystal`、`SyncSetAutoResetShield`、`SyncShieldReset`、`SyncSetTargetShieldAndMaybeAutoReset`。

## 验证

- 编译：`dotnet build -c Release` 独立输出目录，0 警告、0 错误。  
- 静态检查：反编译构建产物确认本地方法枚举与告警分支已进入 IL。  
- 按用户要求未进行主机/客户端运行测试，也未部署到模组程序集目录。
