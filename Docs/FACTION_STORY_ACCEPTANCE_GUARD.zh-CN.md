# 多派系任务接取反向校验

2026-09-12，接续源头路由隔离。候选隔离模块 0.2.0，未部署、未进行真实游戏双端验证。

后续显示规则调整（0.2.1）：按用户要求，开启隔离时仍可查看其他玩家派系任务，并覆盖 MP 的“隐藏其他玩家任务”过滤；接取权限保护保持。仅绕过安装 DLL 的 `MainTabWindow_QuestsShouldListNowPatch.Prefix`，不强制原版 `ShouldListNow` 返回 true，因此任务自身隐藏及页签筛选仍有效，不修改玩家 MP 配置文件。关闭隔离恢复 MP 原设置。当前 44 项离线断言、9 项安装元数据签名检查及双项目构建通过，未部署或进行真实游戏验证；证据见 `BuildValidation/FactionStoryVisibility_20260912`。下文保留 0.2.0 的实施和哈希记录。

## 行为

任务在已核验的联机模拟创建入口 `Quest.MakeRaw` 返回时保存创建派系，早于 QuestGen 的 `root.Run`。界面视角、任务显示在哪个玩家列表、接取按钮传入哪个 Pawn，都不能改写这份归属。

开启现有隔离设置后：

- A 的任务由 A 接取；B 的命令即使传入 A 的 Pawn 也被拒绝。
- A 不能用 B 的 Pawn 接取任务；同派系正常接取、原版重复接取保护仍保留。
- 标准手动 `Quest.Accept(Pawn)` 和自动 `SetInitiallyAccepted()` 都检查归属。
- 本地界面使用真实本机派系做预检查并显示拒绝原因；模拟执行使用 MP 命令/叙事者上下文中的派系重新检查，不读取本机视角作为模拟依据。
- 没有保存归属、未知数据版本、观察者均不能接取。旧档不会按当前基地、定位目标、当前加载玩家或房主猜测归属。
- 关闭开关后恢复原接取行为。已知归属仍会保存；多派系模拟创建任务时即便开关关闭也记录归属，便于以后重新开房启用保护。

设置仍为同一个房主开房时捕获并保存到 GameComponent 的开关，默认关闭；没有新增客户端独立影响模拟的开关。旧任务迁移尚未实现，因此无记录旧任务在开启保护时会被拒绝。已经接取的历史任务不会倒带或清除。

此处阻止“接取”，不保证隐藏任务通知或列表，也不把归属未知的任务视为公共任务。

## 同步与副作用边界

已核验本地 MP 的 `SyncMethods` 注册了 `Quest.Accept`，本次没有重复注册同步命令。地图和世界命令执行器在调用前通过 `cmd.GetFaction()` 建立派系上下文。

原版 `Accept` 首个状态副作用是 `QuestPart.PreQuestAccept`。本补丁在 `Accept` 前缀校验，优先于 owner `multiplayer`，拒绝时不进入原版正文。

离线 Harmony 测试发现：即使前缀返回 false，MP 的 `SetContextForAccept.Prefix` 仍可能执行并写入任务缓存/切换地图时间。因此对安装 DLL 的 `Multiplayer.Client.Comp.SetContextForAccept.Prefix(Quest, ref Multiplayer.Client.AsyncTimeComp)` 另外安装相同准入。线程局部拒绝标志覆盖传入错误 Pawn 等拒绝原因，通过 finalizer 配对恢复，异常不被吞掉。

归属记录使用弱引用对象附加表，并在真实 `Quest.ExposeData` 上保存 `meowAcceptanceOwnerVersion` 和 `meowAcceptanceOwnerFactionId`。缺字段默认为版本 0、owner -1；不是从客户端设置重新初始化。记录中的日志去重集合不保存、不参与模拟决策。

## 验证

- 两份生产程序集编译：零警告、零错误。
- 38 项离线断言通过：其中接取回归使用安装的 Harmony 2.4.1，在 net48 下实际挂钩游戏替身，验证跨派系、伪造 Pawn、自动接取、所有者正常完成、重复点击、UI 视角不同、关闭开关、保存/读取替身、未知版本、异常恢复与观察者。
- 8 项安装 DLL 反射元数据签名核验通过，包括私有 MP 接取上下文入口和 `GetMap` 入口。脚本为 `Tests/FactionStoryIsolation/VerifyInstalled.ps1`，需 Windows PowerShell 5.1 的 ReflectionOnly API。
- 初次 .NET 8 测试运行遇到安装 Harmony 的反射初始化不兼容，改为 net48。随后测试揭示 MP 上下文前缀仍执行，补充对应保护后通过。最终证据没有将这些失败尝试计作通过。

上述测试不等于 Unity 启动验证、实际 Scribe 存档恢复或真实 MP 双端验证。下一步仍需在 A1/A2/B1 场景通过非房主真实接取、同派系竞争接取、自动接取、保存重载、三次冷重连和异步时钟冒烟/长测，并对人物与奖励作两端断言。

证据目录：`BuildValidation/FactionStoryAcceptance_20260912`。安装游戏 PID 21372 保持运行，未干预或替换正式程序集。

- 候选隔离 DLL：`577DBD142BEC05F66EEC0952440A472F1980AEEB8304AAD86224A89B3D414C39`
- 候选核心 DLL：`ED4F6602C694CDEE264F5956E861E61269CA4030A287F7C3A898810F2BC82C20`
- 正式核心 DLL 保持：`61ED42CBD292A495F23F1C8F7D177770E8B5C0F5ADBC0AC4915D3B65277979F4`

## 剩余边界

这是接取权限保护，不是完整任务生命周期隔离。第三方直接改 `initiallyAccepted`、绕过标准接取函数、在任务生成期或自定义后台逻辑直接产生副作用，仍须专用适配。创建归属依赖已建立的 MP 模拟派系；尚未实现父任务生命周期上下文继承、收件目标图验证、旧档迁移、延迟队列或奖励隔离。若生成器进入入口前已丢失原始来源，不能仅凭这一层反推出真实来源。

新字段只用于接取归属，不宣称已经交付原设计中的完整严格模式。正式部署需要同时更新核心设置 DLL 和隔离模块，并完成真实联机验证。
