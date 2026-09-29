> 2026-09-27：本模块已按用户要求纳入 3.0.140 全量本地部署。下文的“候选/未部署”描述为历史状态；既有运行验证限制仍保留，详见发布说明。

# 多派系任务与事件隔离：实施进度

2026-09-12。用户已授权按设计开始实施，并要求把补丁开关加入本 mod 配置。

后续进展：已新增任务创建归属保存、手动/自动接取反向校验及 MP 接取上下文保护。当前候选为 0.2.0，38 项离线断言和 8 项安装签名核验通过，仍未部署/双端实测。见 [接取保护记录](FACTION_STORY_ACCEPTANCE_GUARD.zh-CN.md)。以下保留第一轮实施记录与当时哈希。

## 当前状态

第一阶段源码及候选 DLL 已实现；两个生产项目编译均为零警告、零错误，17 项离线行为模型断言通过。尚未做 Unity 内 Harmony 启动验证、双端真实动作、冷重连或长测，未部署到正式 `1.6/Assemblies`。这不是完整严格隔离功能，也不是已验证修复的发布版本。

## 已实现

- 新增独立 `Source/FactionStoryIsolation` 项目，程序集 `Meow.FactionStoryIsolation`，版本 0.1.0。默认构建引用本项目所在 H 盘安装，可用 GameRoot 显式覆盖；输出为项目 bin 或指定候选目录，不自动部署。
- 本 mod 设置中新增“启用多派系任务与事件源头隔离（实验性，第一阶段）”，默认关闭，独立于优化预设。设置字段为 `enableFactionStoryRoutingIsolation`，本地 Scribe 键为 `mp_meow_modcfg_faction_story_routing_isolation`。
- 房主在 `HostUtil.HostServer(ServerSettings, bool)` 入口把偏好复制到 `IsolationSession`，早于 MP 创建加入快照。支持单人存档开房和重开已加载重放。观看重放不读取本地偏好。
- GameComponent 使用 `meowFactionStoryRoutingEnabled` 保存房间规则；构造及旧档缺字段默认关闭。加入者加载房主快照，不导入自己的偏好。运行中编辑设置只影响下次重新开房，不立即修改当前模拟。
- 在原版 `Storyteller.AllIncidentTargets` getter 的 postfix，排在 Harmony owner `multiplayer` 后，以当前 MP 模拟派系过滤其他派系/无主地图及其他派系商队，保留结果顺序和已有调度。仅多派系联机 tick 或命令执行期间生效。
- 在标准 `QuestNode_GetMap.IsAcceptableMap(Map, Slate)` 前增加归属条件，不取代原版环境条件；已有 Slate 地图与新候选都受约束。没有添加同步命令或额外 Rand scope。
- 防止 `RunInt` 找不到候选后残留外派系 Slate 地图：调用已核验的原版 `TryFindMap` 选择一次；确实无候选则在 RunInt 前抛出明确异常终止该入口。正常 TestRun 无候选仍返回失败；绕过 TestRun 的执行不会悄悄继续使用外派系目标。
- 启动先解析必需方法签名和核心设置字段，解析/安装失败则撤销本模块已安装钩子并记录不可用错误。

## 已核验的源头

本地 MP 源码 `Source/Client/AsyncTime/StorytellerPatches.cs`、`Factions/FactionRepeater.cs`、`Networking/HostUtil.cs`；安装 DLL 的 `StorytellerTargetsPatch` 与游戏 DLL 的 `QuestNode_GetMap` 已反编译交叉核对。没有修改 MP DLL。

- 游戏 Assembly-CSharp SHA256：`8FD7E750A38A856B87004AF7AC91363E8FBFE7C6E362030970FD91BD6E7C3505`
- 安装 MP SHA256：`28D2CDA41D78497970565748BE0E4B7E23F6ECE03310A304359A840AB38FAAD1`
- 候选隔离 DLL SHA256：`199BE92AE1BE88D5E98EDD4A40985118662D9316747C1BA4493E6B19799283EB`
- 候选核心 DLL SHA256：`0FB58A93B0C9CB006D83E21DF115E9A80F320C97B14443CE24737207577C9097`
- 正式核心 DLL 保持：`61ED42CBD292A495F23F1C8F7D177770E8B5C0F5ADBC0AC4915D3B65277979F4`

证据在 `BuildValidation/FactionStoryIsolation_20260912`：输入/输出哈希、两份构建日志、离线断言日志、候选反编译结果及编辑前设置源码。该目录没有 Git 元数据；未提交或清理任何已有工作。

## 验证范围与待办

`Tests/FactionStoryIsolation` 编译实际生产源文件，使用最小游戏/MP/Scribe 替身验证逻辑边界；不是 Unity 或真实序列化测试。17 项断言覆盖 A/B 目标、顺序、保留原版环境判断、关闭/单人/单派系/UI 路径不改变结果、命令重放、Slate 替换及失败、房主偏好冻结、读取旧档默认值及重新开房关闭。

正式目录暂未写入：发现玩家游戏 PID 21372 正在运行，未干预该进程。当前游戏不会出现此新增配置项。部署时需同时使用已验证的核心 DLL 与隔离模块；单独换核心 DLL只会出现设置而没有实现。候选核心重编译了现有项目源码，因此正式替换前仍需核对其他已有源码改动的回归。

接下来的第一阶段验收需要隔离测试安装和 A1/A2/B1 多派系夹具：真实叙事者 tick/任务生成、合法与非法目标、开关两态、客户端不同视角、本地偏好相反、保存/加入/重开、异步时钟，双方都断言实际生成位置；先约 10,000 共享模拟 tick 冒烟，再至少 120,000 共享模拟 tick 长测和冷重连。离线测试不能替代这些验收。

第二阶段尚未实现：Quest/队列持久 owner、生成前绑定、完整接取权限、生命周期上下文、奖励/延迟投递、目标角色、迁移、隔离状态、任务显示、严格模式。

第三阶段尚未实现：Kiiro、Milira、Ancot/Wolfein 等逐模组执行器与共享状态适配和验收。

本阶段世界及未知 IIncidentTarget 保持原行为，不能据此称其为“已声明公共事件”。也不保护标准 GetMap 之外的生成器、NPC 冒险地点授权、任务重选地图或全部后续副作用。默认关闭用于保留现有玩法，不能把本开关当作完整严格隔离开关。

## 构建及离线检查

从 mod 根目录运行：

```powershell
dotnet build Source/FactionStoryIsolation/FactionStoryIsolation.csproj -c Release -o BuildValidation/FactionStoryIsolation_20260912/Candidate/Isolation
dotnet build Source/MP_MeowOnlineShop/MP_MeowOnlineShop.csproj -c Release -p:OutputPath=../../BuildValidation/FactionStoryIsolation_20260912/Candidate/Core/
dotnet run --project Tests/FactionStoryIsolation/Offline.csproj -c Release
```

第一阶段没有持久隔离队列，关闭后下次开房恢复原路由，但不会撤销此前已发生的世界变化。后续完整隔离版本不得直接沿用这一回退语义。
