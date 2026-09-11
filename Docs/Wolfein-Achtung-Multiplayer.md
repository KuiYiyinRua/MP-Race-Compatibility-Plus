# Wolfein 能力与 Achtung 多派系操作

## Wolfein Race（3473140562）

冲锋使用 Ancot 的自定义跳跃 Verb。原先在客户端界面创建的 CastJump 任务会把运行时 Verb 交给 Multiplayer 的 Job 深度序列化，接收端无法恢复 `Verb_Ability_*` 引用，随后在 CastVerb 抛出异常。旧的 Ancot 跳跃兼容只在启用 Milian Modification 时安装，单独启用 Wolfein 不会得到修复。

`Source/WolfeinAchtungCompatibility/WolfeinAbilities.cs` 在 Wolfein 冲锋的界面入口同步原生 Ability 标识和目标，保留队列操作上下文；接收端再用各自的 Verb 创建任务。补丁只处理 Wolfein 的冲锋类型，模拟中的 AI 调用及单机行为保持原流程。继承方法通过声明类型定位，避免 Harmony 拒绝修补 ReflectedType 方法。

本体的其余能力使用原生 Ability 路径：隐身、轨道弩三连发、霰爆、折跃斩击和无人机自爆。装备能力必须随其对应武器测试，不能用无武器的小人直接授予能力替代正常玩法。Multiplayer 原有 Ability 同步负责这些入口，不重复注册同一命令。

## Achtung!（730936602）

本次安装版本为 4.1.14，已经包含原生 MultiplayerSupport，负责征召、移动、强制工作和设置同步。旧版反编译中的两参数 `Tools.SetDraftStatus` 不适用于此版本；当前菜单预览使用三参数方法并在 finally 中恢复征召状态。不要把预览方法整体注册为同步命令，否则会破坏本地菜单计算。

新建最小存档的双派系、独立殖民地测试中，客户端可以选中自己派系的小人，三次生成强制清扫选项，并完成清扫、征召和移动。用户报告的“自己派系小人无法右击”在这组配置中尚未复现，不能据此宣称原故障已修复。

## 构建与分发

使用 .NET Framework 4.8 构建补充程序集：

```powershell
dotnet build Source/WolfeinAchtungCompatibility/WolfeinAchtungCompatibility.csproj -c Release -p:GameRoot="你的 RimWorld 目录"
```

正式文件为 `1.6/Assemblies/Meow.WolfeinAchtungCompatibility.dll`，与原有主程序集共同加载。第三方程序集只作为编译引用，不随补丁复制。测试驱动、反编译文件、日志和开发符号不进入创意工坊内容目录。

## 验证记录

环境：RimWorld 1.6.4850 rev646，Multiplayer 0.11.5+4a3be27-dirty / API 0.6；以用户指定 H 盘安装版本为准。两个独立游戏进程、独立用户数据目录、真实主客连接；从非主机客户端发送操作，两端检查落点、冷却和实际效果。

- 原补丁基线：冲锋无法恢复 Verb 引用，未落地，断言失败。
- 冲锋候选：两个派系、两张地图，三次冲锋均落地，冷却一致。
- Achtung 最小配置：清扫菜单三次生成，强制清扫、征召和移动完成。
- 异步双地图组合：同时加载官方 Multiplayer Compatibility、Dubs Mint Menus、Float Sub-Menus、Melee Animation 及已有补充程序集，采用本机 Achtung 设置。三次冲锋、三次清扫菜单、强制清扫、征召移动通过；隐身 Hediff、三连发的三个弹丸、霰爆伤害、折跃落点及无人机自毁均通过两端断言。

最终组合运行超过 10,000 个共享 tick，两端均输出 `COMPLETE desynced=False`；36 条游戏执行、能力激活和落地检查记录一致。未发生新的模拟异常、同步错误或掉线。该结果支持本次列出的操作，不证明用户原先的 Achtung 故障已定位或修复。

测试候选 SHA-256：`AFB1563CE7B8654D34D2B44A7BA6763E3FF0F8DBA3B8454D0D3AA9C45B99B7B7`。独立 Git 检出中的补充项目也已编译通过，0 警告、0 错误；发布使用实测归档，不用重新构建产物替换。

基线与候选均出现旧主程序集的 `multifaction scenario context` 继承方法初始化告警；该旧补丁未纳入本次修改。无人机自毁后出现一次 `Deep-saving destroyed thing` 提示，已记录，不将提示隐藏或计为新的能力执行失败。开发过程中无效夹具（过新的污渍、未装备对应武器的装备能力）单独标记，不计入通过证据。

按用户要求只进行主要功能的快速组合测试，不以本次结果宣称通过极端场景、全部整合包或长时间压力测试。
