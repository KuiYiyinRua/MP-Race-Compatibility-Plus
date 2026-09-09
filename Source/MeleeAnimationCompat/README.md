# Melee Animation 联机兼容模块

此模块针对 `co.uk.epicguru.meleeanimation`（Workshop `2944488802`）的 RimWorld 1.6 版本。合集通过根目录 `LoadFolders.xml` 条件加载 `MeleeAnimation/Assemblies`，并在目标模组之后初始化。目标模组的原始文件无需修改。

## 构建

使用 .NET SDK 构建 `MeleeAnimationCompat.csproj`。可通过 MSBuild 属性 `GameRoot`、`TargetRoot`、`HarmonyPath` 指定游戏、目标模组目录和 Harmony DLL。所有游戏、Harmony、Multiplayer API 和目标模组引用均设为 `Private=false`，不随补丁分发。

输出目录为仓库的 `BuildValidation/MeleeAnimation/Candidate`。验证后将其中的 `MP_MeowOnlineShop.MeleeAnimation.dll` 放入 `MeleeAnimation/Assemblies`。验证用 `Meow.MeleeValidation.dll` 不能放入正式安装包。

## 实现边界

- 处决、套索、引导技能、决斗和决斗点可见性通过 Multiplayer 指令执行；自动选项使用字段监视和按小人引用解析的数据对象。
- 会改变小人、伤害或任务的动画由所属地图的模拟 tick 推进；绘制不再推进这些动画或重复执行事件。纯视觉随机调用保存并恢复随机状态。
- 套索和击退飞行物的绘制不再改写地图格位置；技能的绝对冷却时间戳随小人的地图／世界时间迁移调整。
- 自动目标枚举、并发扫描、动画结束回调和重载注册使用稳定顺序；冷却分别跟随地图或世界时间。
- 战斗设置在创建联机房间时从主机捕获并随存档保存，客户端本地设置不会替换存档规则。
- 房间创建后修改本地战斗设置，应在下次创建房间时使用；当前存档继续使用其保存的规则。
- 保存恢复保留动画参与者、处决结果、任务参数和技能目标，并重建引导动画结束回调。套索占用从有效任务实时计算。
- 旧版套索存档可从任务中修复重复的参与者引用，并读取旧矩阵文本；旧版未保存的处决结果使用 `Down` 默认值，动画自身的固定结果仍优先。
- 动画排练调试器和武器调整编辑器会直接改写运行状态，联机期间不可使用；应在开局前离线编辑，并在所有客户端保持相同的模组内容。

## 源码依据与验证

目标安装 DLL 的 SHA-256：`C9F29DEC35CFF8195BAC607AAF27613C7F16E9F400F3864DC73BEAB97D228664`。上游源码依据为 Epicguru/Melee-Animation 提交 `8381fdab489d18453469c2ffc865e5035cede718`，关键反射目标和 IL 操作数另与已安装程序集核对。

验证脚本与日志位于 `BuildValidation/MeleeAnimation`，采用隔离存档的真实主机和客户端。每轮归档候选 DLL、记录 SHA-256，并核对进程实际加载的程序集 MVID。不同构建的通过记录不能替代最终构建验证。

2026-09-06 已完成当前安装版本的验收：RimWorld `1.6.4871 rev590`、Multiplayer `0.11.5+a481546`，14 组隔离主机／客户端测试全部通过。覆盖手动及自动处决、走近处决、套索与套索后接处决、击退绘制和落地、特殊武器及过期指令拒绝、手动决斗及取消、决斗点显示开关、自动决斗与观战、暂停时的选项循环、异步地图冷却迁移和两类冷重连。

技能冷重连长测中，主机运行 `120002` 个地图模拟 tick，新客户端运行 `120000` 个；三次技能和昏迷回调记录一致。自动决斗测试调用真实 JoyGiver 和工作链，并核验观战预订与胜负记忆；它不单独断言自然 ThinkTree 的选择权重。关闭目标模组的独立启动检查也已通过。

正式 DLL 已放入 `MeleeAnimation/Assemblies`，SHA-256 为 `2FC449C67ECC944AC37099806068474B29C2CA19EECA8DF643DA53F44201BD32`。汇总验收记录为 `BuildValidation/MeleeAnimation/acceptance.json`，其中列出每轮归档、候选与测试程序集哈希、输入存档及逐地图 tick 覆盖。编译成功且隔离工作树构建的 IL 与验收候选一致。

实测范围为上述版本及记录中的十一模组配置。第三方武器、种族、载具和战斗框架组合已做源码层面的相关路径审查，未穷尽所有组合的运行测试。
