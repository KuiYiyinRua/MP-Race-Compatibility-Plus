# 创意工坊参考与本次变更（2026-09-09）

本次针对搜索到的兼容项目进行源码对照，提交两处按钮同步修正的源码及隔离构建产物。不是全部创意工坊模组或当前全部补丁的兼容认证。未修改正式 Assemblies、版本号或用户存档。

## 参考来源与取舍

- **Multiplayer Compatibility**：rwmt 社区维护，创意工坊作者栏列出 Charlotte、Soky、zetrith。[创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=1629973374)，[源码](https://github.com/rwmt/Multiplayer-Compatibility)。本次固定提交 `bcffd46671bd326f1b978d421423e3c5b68305b8`，仓库 MIT 许可。`Source/Mods/AlmostThere.cs` 同步原版三个按钮回调，目标包为 `roolo.AlmostThere` 和 `Chad.Almostthere1.5`；不能原样套用到结构不同的 Duztamva 分支。本次借鉴动作同步边界，独立适配本机实际方法，没有复制其实现。
- **[MP]Milira_MP**：作者 usamiiiiiii!，[创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3725772639)。页面列出的 Milian Modification、帝国、民主之翼、XY、Brant、Nie Yu 等范围，在本项目 `MiliraAddons` 下已有对应模块；`MiliraMpCompatGate` 也已有原模组启用时的避重逻辑。此次未重复导入这些模块，也未重新认定其移植许可证或运行稳定性。
- **Multiplayer Compatibility Fix Pack**：作者 аnаl pain，[创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3720126644)。页面不同时间抓取的支持范围明显不同，不能把旧的列表当成当前二进制保证。本次未取得可固定版本并核验许可的公开源码，不直接复制。仅“隔离随机数”不能补上按钮未发送同步命令的问题。
- **multiplayer-fix-mod**：作者账号 a398276230-debug，[源码](https://github.com/a398276230-debug/multiplayer-fix-mod)，由创意工坊合集链接发现。查看的 `PawnTickRandIsolation.cs` 广泛包裹 `Thing.DoTick` 和 `Map.MapPostTick`；本次不采用这种大范围改变随机流的方式。`TickRandReset.cs` 当前查看内容全部被注释，不能当作生效功能。未找到可确认的再分发许可证。
- 创意工坊评论提及的 `aliceandlamb/Multiplayer-Compatibility` 分支在本次 GitHub 查询中返回 404；不能据此声称已审阅其实现。

## 两处源码变更

`Patch_AlmostThereMp.cs`：Duztamva 的 [Almost There! Fork](https://steamcommunity.com/sharedfiles/filedetails/?id=3515165298)，包 ID `duz.almosttherefork`。对照其[源码提交](https://github.com/duztamva/Almost-There-Fork-1.5-/tree/b89a9c41dbe8d9797a2d8826921ba42bb73ab860)和本机普通/VF 两份 1.6 DLL，确认 `Command_Toggle.toggleAction` 实际绑定 `<GetGizmos>b__5_1`。

旧实现只调用 `RegisterSyncField`，没有 Watch/发送路径。新实现注册实际动作，连续点击由共享命令顺序计算下一状态，不从点击时的旧状态提前算值；初始化和读档不走按钮回调。MP 现有 WorldObjectComp 序列化负责通过父世界对象和组件类型还原目标。

`Patch_MaruTrapMp.cs`：VAMV 的 [Maru Race](https://steamcommunity.com/sharedfiles/filedetails/?id=2817638066)，包 ID `vamv.maruracemod`。未发现可用的上游陷阱源码后，检查本机 1.6 `MaruTrap.dll`，确认自动重新布置按钮绑定 `<GetGizmos>b__15_1`，只翻转 `autoRearm`。同样替换无监视的 SyncField 为动作注册；保留出生默认值和存读档路径。

两处检查声明类型、无参实例 void 签名和 CompilerGenerated 属性；缺失时警告并不猜测其他回调。代码注释均包含作者和固定来源链接。这个缺失目标分支仍会保留原模组行为，不能理解为该版本已经兼容。

## 验证与发布状态

- 全项目 Release 编译通过。
- `BuildValidation/WorkshopReuse_20260909/Verify-Targets.ps1` 检查三份实际 DLL 中的 `ldftn → System.Action → toggleAction` 绑定和生成方法签名，全部通过。
- 对新候选 DLL 反编译，确认两处均为 RegisterSyncMethod，旧的 RegisterSyncField 已移除。
- 候选 SHA-256：`108E96AC6B60B94B35EDDE568CA13CE60C3B2993B67BDF3D6FA7B9158E967248`。
- 候选位置：`BuildValidation/WorkshopReuse_20260909/candidate/MP_MeowOnlineShop.dll`。原文件备份、目标 IL、编译和验证日志位于同一验证目录。
- **尚未进行双端游戏测试或 120,000 tick soak，未部署正式 DLL。** 接下来的运行验收需覆盖非主机连续三次切换、两端状态一致、保存重连、远行队普通/VF 两套加载配置，以及陷阱触发后是否按共同开关重新布置。

另有待核实线索：`Patch_SmeltedLoongMp` 中两个字段也是裸注册；需继续核验实际 gizmo 和其他补丁是否已经同步相同入口，不能仅凭搜索到 RegisterSyncField 就断言缺陷。本次不把它列为已修复项。
