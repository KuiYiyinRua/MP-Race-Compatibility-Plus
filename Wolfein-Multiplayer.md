# Wolfein 系列联机兼容 — 3.0.124

2026-09-11 能力补充：已针对 H 盘指定的本体安装版本补上独立启用时的冲锋同步；能力范围及本轮验证见 [Wolfein / Achtung 说明](Docs/Wolfein-Achtung-Multiplayer.md)。以下内容保留原六模组专项的历史版本和测试记录。

本次实现针对下列六个模组的实际安装版本，未修改原模组文件。

## 版本与来源

- **Wolfein Race**：严格使用 `G:\Steam\steamapps\workshop\content\294100\3473140562`，包 ID `MelonDove.WolfeinRace`。
- 其余模组均来自 `H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods`：
  - `3699095346`：Wolfein Race GFI Expand，`JL.WolfeinGFIExpanded`。
  - `3473178683`：Wolfein Race Gene Patch，`Ancot.WolfeinRaceGenePatch`。
  - `3707497233`：Wolfein Allegiance，`leopoko.wolfeinallegiance`。
  - `3717539116`：Wolfein Black Science Expand，`WolfeinExpand.BlackScience`。
  - `3675031461`：Wolfein - Imperial Data Analysis Tweaks，`eddie.wolfein.imperialdataanalysis.tweaks`。

未找到能证明与这些 DLL 完全对应的公开源码版本；C# 审计依据上述安装文件的反编译结果。Gene Patch 与 Imperial Data Analysis Tweaks 是 XML 补丁，没有独立 C# 程序集；它们随完整六模组配置一起加载验证。

完整路径、目标程序集哈希见 [provenance.json](BuildValidation/WolfeinSuite_20260906/provenance.json)。测试使用 RimWorld **1.6.4850 rev646**、Multiplayer **0.11.5+a481546 / API 0.6**，并加载 Harmony、Prepatcher、Humanoid Alien Races、Ancot Library 及相应 DLC。

## 实现内容

- **本体**：同步武器工具组切换的完整回调、能量盾充能消耗、人工月亮开关、炮塔连射/弹药/瞄准与维修容器取消操作；取消原来只同步单个字段的重复入口。允许 Multiplayer 在通信对话回载时恢复本体购买帝国数据所用的指定委托。
- **GFI**：同步芯片及配件清单、载机与无人机操作、僚机工作模式和充电阈值、附加炮塔及人工月亮操作、发型字段等。取消清单以组件、槽位、类型和物品定义定位；只拦截界面输入，AI 清理直接在模拟中执行。充电阈值纳入存档，读取界面不再单端初始化保存字段。
- **重复配件组件**：同一机械体上的第二组 `CompLoadOutSlots` 使用独立存档键，按组件顺序兼容旧的重复 XML 节点，避免两组清单相互覆盖。
- **Allegiance**：11 种定点许可携带调用者、地图、派系、免费标记及双点目标；执行时新建许可工作器，避免覆盖其他玩家的本地选点。另同步突击队、无人机群、运输艇劫持、情报、补给任务、阵营选择及离场动作。
- **Allegiance 存档**：保存援军编组、运输艇限制、待登船人员、胜利运输艇、NPC 运输艇标记、离场人员及倒计时；清理并恢复静态任务登记。正规军、夜袭和任务准备效果的延迟回调保存实际捕获参数，并按目标地图时间执行。
- **Black Science**：同步囚犯舱装载/取消/工作模式、武器模式、炮塔操作、天门神工卸载与发射。世界选点暂存在本地，最终提交地图格时一次同步完整目标。延迟连击和处决队列纳入存档，并在所属地图的时钟与随机数环境中执行。
- **界面随机数与设置**：隔离 GFI 粒子界面和黑科技护盾绘制的随机数。服装限制与忠诚任务间隔使用主机创建联机会话时的快照，并随存档传递；本地设置窗口仍保留各自偏好，不改当前联机会话规则。

主要代码为 `Patch_WolfeinToolsMp.cs`、`Patch_WolfeinGfiMp.cs`、`Patch_WolfeinAllegianceMp.cs`、`Patch_WolfeinBlackScienceMp.cs`、`WolfeinCombatQueueState.cs`、`WolfeinAllegianceState.cs`、`WolfeinScheduledActions.cs`、`WolfeinSessionSettings.cs`。已增加六个包的加载顺序，并独立初始化各扩展补丁。

## 实机验证

所有下列有效证据均对应同一个程序集 SHA-256：

`AC713471ED9B5D7B14DAB270B28B6CFC295E5B7C22DDB406F3AE9E9CB3596D1F`

测试使用本机两个独立游戏进程、独立用户数据目录及真实 Multiplayer 主客连接。客户端通过生产同步入口发出操作，两端分别断言状态；测试驱动单独编译，未随正式补丁发布。

1. **动作冒烟通过**：`expanded-actions-r11-20260906-231401`。两端各完成六轮基础动作与六轮扩展动作断言，包含三次切换循环；运行 **11,955 地图 tick**。两端故意使用不同本地设置，实际任务间隔与原始服装限制方法均采用主机值。
2. **三次冷加入与异步暂停检查通过**：`queues-async-r12-20260906-231836`。待执行连击、处决、援军回调、运输艇状态、第二组配件清单及充电阈值的快照三次一致。另一张地图推进 **7,128 tick** 时，暂停地图的相关状态不变。此轮末尾的旧“伤害队列必须为空”断言失败，未把整轮标记为全通过。
3. **恢复执行复测通过**：`queues-completion-r14-20260906-233640`。按原模组状态机修正断言后，两端回载并继续运行 **10,000 共享 tick**，原始连击完成、处决进入 `Done`、目标受到伤害，延迟援军回调创建第二组编组，未失步。原模组会保留 `Done` 处决记录，因此“队列非空”本身不代表动作未完成。
4. **双地图长测通过**：`soak-async-r15-20260906-234348`。两端各完成全部六轮基础/扩展断言，地图 0 推进 **120,004 tick**，地图 1 推进 **242,040 tick**，均无失步。

编译为 0 警告、0 错误。各轮 `manifest.json`、`assessment.json`、主客日志、冷加入快照及候选 DLL 保留在 [验证目录](BuildValidation/WolfeinSuite_20260906)。开发阶段的失败夹具和中止记录亦保留，不以它们替代最终通过证据。

## 验证范围与使用

上述为已列明场景的实机证据，并非每个剧情分支、全部许可菜单、胜利结局与天门神工世界发射 UI 的逐项穷举测试；这些入口完成了对应安装版本的代码审计与兼容实现。当前 GFI 没有启用的 ThingDef 使用 `CompExtraTex` 或 `CompWeaponSwitch`，其中全息建筑 XML 被注释；这些类的注册不计作实机操作覆盖。

启动时存在目标模组原有的 `EMP_Activate` 缺失、DefOf 初始化/数值解析提示及隔离环境的 `UnityEngine.InputLegacyModule` 反射加载提示。它们在基线中同样出现；没有将这些提示归因于本次兼容代码，也没有据此忽略新的运行异常。

主客双方使用相同的六模组版本及本补丁。正式程序集为 `1.6/Assemblies/MP_MeowOnlineShop.dll`；发布时复制已测试的归档文件，不重新构建。旧程序集备份与最终部署哈希记录保存在验证目录的 `ReleaseBefore` 和 `release.json`。
