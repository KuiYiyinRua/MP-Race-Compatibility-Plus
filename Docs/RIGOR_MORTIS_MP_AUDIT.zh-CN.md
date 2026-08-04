# MoeLotl: Rigor Mortis 联机兼容审计

> 状态：进行中。本文是 `fxz.moelotlzombie.update` 的来源、覆盖面与分阶段测试证据索引；只有通过文末全部门槛后才可称为完整联机兼容。

## 1. 目标身份与源码权威性

- 安装路径：`H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\3454053400`
- 标题：`MoeLotl: Rigor Mortis`
- 作者：`Feng Xinzi`
- packageId：`fxz.moelotlzombie.update`
- Workshop ID：`3454053400`
- 支持版本：RimWorld 1.5、1.6；当前审计目标为 1.6。
- 主程序集：`1.6/Assemblies/RigorMortis.dll`
- 程序集身份：`RigorMortis, Version=1.0.0.0`，无强名称公钥标记。
- 主程序集 SHA-256：`BF4D79001802B55B32D3B7AB3DF52CCB7495D0B515ADE89213A7962B5E1A05FF`
- 主 PDB SHA-256：`633345EC986D8C68AC346CED597A3F948CCF9D387D0727BF752F09966711CA4F`
- 随包源码：`1.6/Source/RigorMortis`，296 个 `.cs` 文件，约 23,450 行。
- 权威性证明：发布的 `1.6/Assemblies/RigorMortis.dll` 与源码目录的 `1.6/Source/RigorMortis/obj/x64/Debug/RigorMortis.dll` 哈希完全一致；发布 PDB 与对应 `obj/x64/Debug` PDB 也完全一致。因此随包源码可作为当前已安装 1.6 二进制的精确权威来源。

### 条件程序集

| 激活条件 | 程序集 | SHA-256 |
|---|---|---|
| `Nals.FacialAnimation` | `1.6/Mods/FacialAnimation/Assemblies/RigorMortis.FA.dll` | `802F2E661E0728CF378D9F1F17936B4F501060232A22FABF0F63B2B5503B7D91` |
| `HenTaiLoliTeam.Axolotl.FactionExpand` | `1.6/Mods/MoelotlCrimson/Assemblies/RigorMortis.Crimson.dll` | `E733277E23E9C554BE9C38FEDD3BEEE94D44022B7255A0562ED6CC6928B741EE` |

`MoelotlCrimson` 文件夹还捆绑了 `Axolotl.FactionExpand.dll`；它属于依赖模组代码，不在本模组补丁的默认写入范围，但会在条件加载组合测试中冻结哈希并检查交互。

## 2. 已确认依赖与加载变体

硬依赖：

- `brrainz.harmony`
- `HenTaiLoliTeam.Axolotl`
- `HaiLuan.CustomQuestFramework`

条件加载变体：

- 无 Facial Animation、无 MoeLotl Faction Expand；
- 仅 Facial Animation；
- 仅 MoeLotl Faction Expand；
- 两者同时启用。

完整性结论必须覆盖基础组合，并对两个条件程序集进行静态审计；涉及其功能的补丁必须在对应组合下完成双实例定向测试。

## 3. 审计与验证门槛

1. 枚举 296 个源码文件中的全部状态变更入口、UI 回调、目标选择、随机数、无序枚举、序列化、组件构造、周期 Tick、任务/领主/事件/任务链和世界/地图切换路径。
2. 将每条路径映射为：本地 UI、Multiplayer 已覆盖、需要同步、需要确定性隔离、仅调试、或需要运行证据。
3. 逐项核对本地 `Multiplayer-master` 的真实实现，避免重复同步。
4. 每批补丁必须构建成功，并用元数据/反编译确认注册目标与实际签名一致。
5. 隔离 host/client 必须使用相同的目标 DLL、补丁 DLL、配置、模组顺序和世界输入哈希。
6. 从非主机客户端执行真实入口；断言双方都执行且状态相等。
7. 依次通过启动/加入、约 10k–20k shared ticks 定向冒烟、多地图/异步时间/三次冷重连，以及至少 120k shared ticks 长时 soak。
8. 最终发行 DLL 必须与通过长测的归档候选哈希一致，测试驱动不得残留在发行目录。

## 4. 当前阶段

- [x] 目标身份、依赖、加载变体、程序集及源码权威性已冻结。
- [ ] 全量 mutation/action inventory。
- [ ] Multiplayer 内建覆盖对照。
- [ ] 补丁缺口与最窄同步边界。
- [ ] 分批构建和静态检查。
- [ ] 隔离双实例测试矩阵。
- [ ] 发行完整性与最终报告。

## 5. 机器清单与首轮语义复核

审计器使用 Roslyn 解析主、Crimson、FA 三套随包源码，排除 `bin/obj/.vs`；输出保存在本地验证目录 `BuildValidation/RigorMortisAudit/inventory.json`。本轮结果：

- 304 个源文件，1,391 个成员，0 个语法错误；
- 609 个需人工判定的候选成员；
- 其中 simulation mutation 247、lambda/callback 210、Harmony boundary 91、job/lord 90、serialization/load 83、UI action 73、random/process-local 41、tick/scheduler 32、debug-only 6、unordered enumeration 2；
- `CompatMentionsType/Member` 只表示文本命中，不能作为已覆盖证明。每一项仍须阅读真实方法体及 Multiplayer 实现。

### 已人工确认的 UI 分类

| 路径族 | 判定 | 证据/处置 |
|---|---|---|
| `BloodPage`、`IncenseTripod`、三类 artifact、`FiveElementCompass` 的地面菜单 | Multiplayer 内建覆盖 | 回调只构造 `Job` 并调用 `Pawn_JobTracker.TryTakeOrderedJob`；本地 MP 源码已注册该方法并暴露 `Job` 参数。 |
| Guide/Blood Book/Page 导航、资源条、Tooltip、选择高亮 | 本地 UI | 不修改游戏模拟状态；`GetHashCode` 只生成 Tooltip ID。 |
| CQF 故事选项、商人通讯、Fufu/BloodUse 窗口 | 现有专用补丁 | 使用显式同步代理或 commit-on-command 窗口状态。 |
| 法器、铃铛、符咒、灯笼、换尸衣、僵尸能力等命令 | 现有专用补丁 | 已逐个映射真实 `OrderForceTarget`/执行器；仍需非主机实际操作验证。 |
| `Building_CallBoard` 两个 toggle | 确定缺口，已补 | 原回调直接修改存档字段 `active/main`；现改为 `(Thing,bool,bool)` 同步提交。 |
| `TaoistArtifactGizmo` | 确定缺口，已补 | 原 GUI 直接修改 `selectedArtifact/roll`；现在本机先回滚、再用 `(Pawn,Thing,int)` 同步提交。 |
| `ApparelArtifact` / `WeaponArtifact` 直接换装 | 确定缺口，已补 | 点击只同步 `(Pawn,Thing)`；命令执行阶段重新取得并调用原模组动作，避免复制业务逻辑。 |
| Crimson “Gouge Heart” | 条件缺口，已补 | 条件程序集存在时包装真实命令并以 Pawn 同步；执行阶段调用 Crimson 原动作。 |
| `RMSettings` | 确定缺口，已补 | 7 个整数与 9 个布尔值参与伤害、AI、任务、资源和结局；联机 UI 修改回滚后整组同步并按原 UI 范围校验。 |
| `CompYinAndMalevolent` 跳跃 toggle | 确定缺口，已补 | 原动作直接翻转已存档的 `HediffComp_Jumping.jumping`；现在只发送 `(Pawn,label)`，执行阶段重新生成原 Gizmo 并调用原 toggle。 |
| `CompYinAndMalevolent` 九个 DEV Gizmo | 确定缺口，已补 | 八个会改写精神状态、煞气、眩晕、变异/求食/画皮状态，另一个读日志；全部经同一原动作重放器并标记 DebugOnly。 |
| 调试动作 | DebugOnly 补齐 | 委托板两个调试按钮、13 个 DebugAction 与矩形概率刷物均走 DebugOnly 同步。 |

### 已人工确认的随机数/无序集合问题

| 路径 | 风险 | 处置 |
|---|---|---|
| `RMComponent.ExposeData` PostLoadInit | 循环内反复 `cachedNPCs.Clear()`，冷重载后只留下字典最后一项，且“最后”受枚举顺序影响 | Postfix 从 `NPCDict.Values` 完整重建，并按 `thingIDNumber` 排序。 |
| `QuestNode_Root_Apprentice.RunInt` | 从 `Dictionary<Pawn,string>.Keys` 随机选师父，重载后键枚举顺序可能不同 | `TestRunInt/RunInt` 的键读取均转为按 `thingIDNumber` 排序的序列。 |
| `RMComponent.WorldComponentUpdate` | `lanternDict.ElementAt(i)` 依赖字典插入顺序；`nameDict` 还可能在 foreach 中删除空值 | 方法进入前清理无效名字项，并按 Pawn `thingIDNumber` 规范化两个字典；无变化时不重建。 |
| `RMComponent.RecordOneKill` | 以 `damageDict.Keys` 顺序写入击杀记忆 | 键读取转为按 Pawn `thingIDNumber` 排序的序列。 |
| `RMUtility.AnyZombieThreat` 两个重载 | 用进程全局 `tmpHostility/tmpZombieLevel` 缓存带 `Map` 参数的结果；异步多地图会交叉污染，重连首轮还会继承本地默认/旧值 | 替换为按 `map.uniqueID` 与重载/间隔分区的派生缓存；首次访问必重算，后续保持原 60 tick/传入间隔刷新语义，并正确纳入飞行僵尸等级。 |
| `HediffRedString.PostDraw` | 纯渲染路径调用全局 `Rand`；各客户端可见性不同 | 仅在联机中用异常安全的 Push/Pop 隔离渲染随机流。 |
| `HugeLocustTree.Print`、`PreSpawner.DrawAt` | 渲染随机 | 原模组已自行 Push/Seed/Pop；不重复补丁。 |
| 正常 Tick、能力、任务、AI 内的 `Rand` | 同步模拟随机 | 输入和迭代顺序相同即可由 MP 模拟保证；不盲目改成独立随机流，保留运行追踪验证。 |

## 6. 当前实现批次

新增 `Patch_RigorMortisStateActions.cs`，并从 Rigor Mortis 专用 Harmony 初始化路径调用。所有主模组必需符号先整体校验；缺少任一 1.6 符号时该批次不声明 ready。Crimson 作为条件模块单独解析。当前 Release 构建结果：0 warning、0 error。

该批次尚未获得运行时启动/双实例证据；“编译通过”不等于“联机测试通过”。

## 7. 序列化完整性复核

审计器将所有声明 `ExposeData/PostExposeData/CompExposeData` 的类型按“实例字段—Scribe 字段”做差集。16 个有差集的类型逐一复核如下：

| 类型/字段 | 判定 |
|---|---|
| `HediffAbility_TaoistCasting.compass` | 确定缺口，已补。它是该任务生成的精确罗盘引用；原来的懒加载会在重连后错误选择地图上的第一个罗盘。兼容补丁用独立 key `mpMeow_taoistCastingCompass` 追加 `Scribe_References`。 |
| `GameComponent_End.paintSkinTick/sustainer` | 屏幕覆盖倒计时与音频对象，只影响本地呈现；不进入模拟决策。 |
| `HediffInsectBlood.curStage`、`HediffMalevolent.curStage` | 由已保存的 severity/malevolent 重算的阶段缓存。 |
| `PreSpawner_GeneralZombie.ResultSpawnDelay` | 只读常量范围；`sustainer` 及两个 Script spawner 的 `sustainer` 均为音频对象。 |
| `FiveElementCompass.fieldSound/sustainerField/sustainerMoving` | 音频定义/播放对象；模拟字段 `progress/user/cells/ended/...` 均已保存。 |
| `InkMarker.sourceComp` | 从已保存的 `source` Thing 引用派生的组件缓存。 |
| BegForFood/PaintedSkin/WorshipMoon/ZombieCatch/`HediffComp_Zombie`/`HediffMalevolent` 的 `compYin` | 从所属 Pawn 派生的组件缓存。 |
| `CompYinAndMalevolent.compJumping/compZombie/tmpInks` | 组件缓存或临时工作列表；`faceRefreshed` 是图形刷新门闩，`locked` 在当前权威源码中没有读写用途。 |
| `HediffComp_Jumping.cachedPos` | 跳跃绘制位置缓存；实际开关 `jumping` 已保存。 |

源模组的静态差集仍会显示 `compass`，因为修复位于兼容程序集；运行阶段必须以存档 XML/重连行为证明追加引用被写入和恢复。

## 8. 上游事件正确性守卫

`Apprentice_Tale_Patch.Postfix` 与 `Apprentice_Merry_Patch.Postfix` 在只有师傅或徒弟一方参与恋爱/婚礼时，会对事件参与者列表中必然为 `null` 的另一方调用 `needs`，导致确定性空引用。兼容批次以两个前置守卫替换其后置体：从 `RMComponent.apprentice/master` 取得真正的另一方，并按原 Def 语义分别发放“徒弟恋爱/结婚”“师傅恋爱/结婚”或双方彼此关系记忆。此项必须在双实例中同时验证一方参与与师徒彼此参与两种分支。
