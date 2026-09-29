# Milian Modification (3496629836) Multiplayer 兼容审计

> 性质：静态源码审计与代码补丁。按用户要求只调整代码，不部署正式版，也不启动游戏测试。

## 来源权威

- 模组名：Milira Tech: Milian Modification
- 作者：Ancot
- packageId：`Ancot.MilianModification`
- 工坊 ID：`3496629836`
- 安装目录：`H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\3496629836`
- 目标 DLL：`1.6\Assemblies\MilianModification.dll`
- DLL SHA-256：`F1B4EA35DF4B5EB6C42C599DDE33AAE678D4FC5F5595C418B1181636DC3B82C3`
- 反编译工作区：`TestValidation\Decompiled\MilianModification_3496629836_1.6_20260807`
- 反编译工具：`ilspycmd`，输出为可读 C# 工程

未发现模组自带源码或官方源码仓库，因此以安装 DLL 的反编译结果作为行为依据；未修改目标 DLL。

## 盘点结论

模组功能按以下类别完成盘点：

- 组件安装/卸载/配方：`CompModification`、`MilianModificationUtility`、两个 ITab、Pawn 列、工作 Giver、安装/卸载 JobDriver。
- 研究配方与历史：`MiliraGameComponent_MilianComponentRecipe`、`Recipe_MilianComponentResearch`、`MilianComponentResearchUtility`、研究 ITab。
- 能力：`Command_MilianAbility`、`CompAbilityTransitionDrive`、`CompAbilityPortableCable`、`CompAbilityShieldEnergyPulse`、`CompAbilityProjectShield_Paladin`、`CompAbilityCounterStorm`、`CompAbilityPowerCoreExplode`、AI Cast 系列。
- 皇家许可：`RoyalTitlePermitWorker_DropMilianComponent`。
- 任务与事件：Mermeister 对话、教会合作学习世界对象、GenStep、SymbolResolver、QuestNode/QuestPart。
- 建筑与战斗：组件工作台、CIWS/Plasma 堡垒、弹体与投射物、备份电池、便携电缆、幻影投影等 Hediff。
- 随机与 Tick：GameComponent Tick、Pawn 生成、尸体屠宰、研究记录、教堂合作研究、世界对象生成。

## 已确认的 MP 原生覆盖

- `Ability.QueueCastingJob` 与 `Verb_CastAbility.OrderForceTarget`：Multiplayer 原生同步，自定义能力 Gizmo 仍走该入口。
- `Pawn_JobTracker.TryTakeOrderedJob`、`BillStack.AddBill/Delete`：普通任务与配方工作台账单由 MP 同步。
- `DiaOption.Activate` 与 `Dialog_NodeTree`：通过 MP PersistentDialog 同步选项索引；本模组对话的持久化依赖该机制。
- `CaravanArrivalActionUtility` 的通用浮菜单闭包：MP 的 `SyncActions` 对世界对象车队菜单做动作包装。
- `ITargetingSource.OrderForceTarget`：仅覆盖 `Assembly-CSharp` 中的实现，第三方 DLL 的皇家许可 worker 需要单独补丁。

## 新增补丁

### `MiliraAddons\MilianModification_StateSync.cs`

- 注册批量卸载同步：`AddSlotsToUninstallRecipeSync` 的 UI 调用改为 `SyncedBatchUninstall(Pawn, int[])`，远程按 `ComponentSlot` 重建并执行。
- 同步研究工作台技能下限 `minResearchSkill`。
- 同步 `tmpResources`（研究配方输入）。
- 同步研究配方预设新增/删除与历史记录删除。
- 同步备份电池 Gizmo 的 `BatteryChargeOnce`。
- 同步皇家许可 worker 的地图目标 `OrderForceTarget` 与车队 `CallResourcesToCaravan`，并设置 `caller/map/faction/free` 上下文。

### `MiliraAddons\MilianModification_WorldDeterminism.cs`

- 隔离 `MiliraGameComponent_MilianComponentRecipe.GameComponentTick` 的随机翻转。
- 隔离 `MermeisterDialogUtility` 建树时的随机选项生成。
- 隔离 `CompMilianComponentData.Initialize` 与 `QuestNode_FindMilianComponentForTradeRequest.TryFindRandomRequestedComponent`。
- 为教会合作学习的世界对象事件建立 `Faction.OfPlayer` 与稳定地图上下文，并隔离随机。
- 隔离 `Milira_MilianPawnGenerator_Patch.Postfix`。
- 将研究历史 Guid 改为确定性的 tick/计数 ID。
- 用确定性 Rand 作用域包裹 `Corpse.ButcherProducts` 的枚举结果，覆盖屠宰随机掉落。

### `MiliraAddons\MilianModification_Compat.cs`

- `GenerateRecipeDef` 的同步前缀增加 `MP.InInterface` 门控，避免模拟 Tick 中双方重复广播同步命令。

## 残留风险

- 按用户要求未进行主机/客户端运行验证；反射目标解析、命令序列化和实际玩家动作链仍需运行时验证。
- 补丁针对当前安装的 `MilianModification.dll` 反编译签名编写；模组更新后需重新核对类型、方法与字段名。
- Mermeister 对话动作依赖 MP 的 PersistentDialog 选项索引同步；若玩家用非 Dialog_NodeTree 窗口打开同一节点，选项动作需另行补丁。
