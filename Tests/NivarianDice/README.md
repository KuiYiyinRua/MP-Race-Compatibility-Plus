# 涅拉每日骰子专项驱动

驱动只在 `-nivarianupdateprobe` 下运行，不随生产 DLL 部署。使用隔离存档、独立日志、真实图形设备和 Multiplayer 官方 localhost 加入路径。

先构建 `dotnet build Tests/NivarianDice/Probe.csproj -c Release`。`Run.ps1` 必须指定唯一、短且不存在的 RunRoot，以及冻结的 Tale 候选和 RaceTrio DLL。例：

```powershell
./Tests/NivarianDice/Run.ps1 -RunRoot G:\DiceFresh01 -Candidate ./BuildValidation/DesyncEvidence/Desync469-476_20261013_Dice/Candidate119/Meow.TaleNivarianCompatibility.dll -Trio ./1.6/Assemblies/Meow.RaceTrioCompatibility.dll -Maps 2 -Cycles 3 -SoakTicks 30000 -Port 31181 -AsyncTime
```

每轮在共享命令内设置原生 Nira 激活条件和五级测试等级，用原生每日刷新生成五枚骰子（没有调用增加骰子调试入口）。非主机通过原生面板选择回调反复刷新，并调用已被实际 UI 使用、已注册同步的原生投掷、跳过和使用方法。该驱动未自动点击绘制的 Widgets 按钮，也未验证面板画面布局。

两端逐项断言生成五个选项、rolled/consumed 状态；同一服务器 world tick 目标后生成 joinpoint、冻结并比较世界/地图 tick、Rand 及完整骰子选项。原生等级设置仅用于测试副本，不修改玩家存档。

`-IncludeCargo` 加载玩家实际的鼠族及地下扩展依赖，发射三个原生货舱投射物，断言最终货舱建筑数量和投射物消失；异步时第二张地图暂停，客户端切换焦点。普通时间测试关闭高容量 desync traces；异步专项开启诊断 trace。

`-BaselineSave` 只复制指定存档和配置，保留原文件；对于 MP 快照合并 XML，需要保留来源记录，不能声称合并后的单机入口恢复了原来的完整 MP 异步时间上下文。

保持每轮 RunRoot 中的源码、驱动 DLL、候选 DLL 和输入不可变。构建未来驱动版本时不要覆盖已启动实例的冻结副本。Prepatcher 内存加载下 Assembly.Location 可为空，启动记录使用控制器固定候选路径 SHA 加实际加载版本/MVID。

控制器结果、双端原始日志、冻结快照和输入哈希必须共同审查。控制器记录缺失时独立终止审计需明确记录缺失，不写回伪造 PASS。
