# Desync-07 / 09 / 10：援助事件与床位计价修复

2026-09-10。状态：源码已修改，RaceTrioCompatibility 1.1.1 编译通过（0 警告、0 错误）；按用户要求未运行测试，未替换正式目录中的 DLL。

## 原因与证据

- `desync_info`：最后有效 tick 137101，2 玩家、2 地图，异步时间与多派系均开启；RimWorld 1.6.4850 rev646，MP 0.11.5+4a3be27-dirty。
- 两端上下文 trace 273–312 的头部相同；313 在 tick 137148 首次不同。栈均为 `AM.Processing.MapPawnProcessor.CompileListOfAttackers`，但地图时间/派系/随机状态不同。它是首次捕获的差异，不能将它直接认定为原始错误发生点。
- 客户端 JIT 记录在 **137148，i:True** 执行 `ChoiceLetter_NivarianAid.ExecuteAccept → ExecuteAcceptedAid → SpawnVisitors / GiveGiftsToVisitors / SpawnGuard → NotifyAidAccepted`；主机记录有 136768 的事件发信链，没有该接受链。JIT 单独只证明首次到达；结合实际 DLL 中完整的本地回调、缺少同步注册和同 tick 的 trace 分叉，强烈支持“接受按钮单端修改模拟状态”为本次根因。
- `GetRandomCooldownTicks` 调用 `UnityEngine.Random.Range(float,float)`，会产生独立于 MP 随机流的冷却值。这是源码确认的第二个缺口，不能只同步按钮后忽略。
- `HasPendingLetter` 读取本地 `LetterStack`。本地 MP 源码 `LetterStackReceiveOnlyMyFaction` 会移除其他派系的显示信件，因此它不能作为共享模拟状态的依据。
- 原调试窗口 `DrawActions` 的 Force、Reset CD 直接调用执行器；`CanFireNowSub` 的道歉事件分支甚至在绘制条件预览时写入冷却字典。

压缩包的 `local_logs.txt` 是截断到 10,000 行的累积日志，包含 20657、22737、16879 三个旧 last-valid 记录，没有与 137101 对应的最终报错行。不能拿旧行计算这次的检测延迟。137148 与 137101 相差 47，只是首个捕获差异与最后有效 tick 的间隔。旧日志还含加载阶段提示和近战渲染异常，本次不据此改动无关模块。

包内元数据识别 `Meow.RaceTrioCompatibility 1.1.0`、核心 `MP_MeowOnlineShop 3.0.125`；启动记录 MVID 为 `d1e9c1c2-b563-4a7b-af6c-c2efa0ae3f17`。没有用当前磁盘 DLL 冒充当时进程的 DLL。

## 修改范围

`NivarianAidEvents.cs`：

- 在完整接受执行器和完整拒绝回调之前拦截界面输入，一次发送 Map、Letter、决策。显式 Map 使用 MP 自身的地图队列、时钟及随机状态，避免依赖点击时正看的地图。
- 在执行时核对地图、所属派系及 Pending 状态；重复/过期决策不再生成第二批人物或再次刷新冷却。仍调用原来的完整回调，保留礼物、Lord、通知和信件移除行为。
- 待处理判定按地图和 IncidentDef 查询共享 Archive。未处理的援助信件不能被归档清理；已处理信件的清理资格同样不依赖各端本地信件栏。
- 冷却方法唯一的 Unity Range 调用替换为 MP 会话中的 Verse.Rand；单机保留原实现，永久冷却分支不变。
- 调试 Force/Reset CD 使用 DebugOnly 同步命令，调试提示不写入单端历史。条件预览隔离随机状态，并在 finalizer 中还原道歉事件对冷却字典的预览写入。

`NivarianAidSettings.cs`：把 EnableNivarianAid、RequireAllianceForNivarianAid 保存到主机快照。援助条件及霜龙事件读取会话值，不改写本地配置；旧存档首次升级使用加载它的主机设置，此后重连读取保存的值。

`Bootstrap.cs` 安装新模块；工程版本更新为 1.1.1。

共享信件入口覆盖六个实际 worker：食物援助、冰冻药援助、婚礼援助、废物回收、空投增援、Koelime 道歉援助。共享冷却入口也覆盖使用同一事件扩展的人员借调、避寒任务、月华种子任务与霜龙到访。已追踪访客 Lord 和莲花空投标记：目标地点、货物、延迟 tick 等由原模组保存；不重复实现这些已存在的保存机制。其他剧情任务和原有 Wanderer/IcyCheck 同步逻辑不在本次修改范围。

## 来源与交付

目标为安装目录 `Mods/3624805128`，包 ID `keeptpa.NivarianRace`，作者 keeptpa，About.xml modVersion `20260823162750`。安装目录没有 C# 源码；公开检索未找到可验证的对应源码，因此反编译实际 `1.6/Assemblies/Nivarian_Race.dll`。SHA-256：

`1F0100B0E8FFCA1A176D8EB2B297F7F852B5A78300D0FD5FF8783900AA3D39C2`

冻结的原始压缩包、提取内容、反编译材料、修改前文件和候选位于 `BuildValidation/DesyncEvidence/Desync07_20260910/`。源压缩包与副本 SHA-256 相同，详情见该目录的 `provenance.json`。

最初仅含援助修复的候选保留于 `Candidate/` 和 `AidOnlyCandidate/`。综合三个日志包后的最终候选为 `CombinedCandidate/Meow.RaceTrioCompatibility.dll`，SHA-256：

`10FACE55F060DF52E9732AF9B6EACFB473666AE970DABFAAB4528BDEAF7A5857`

只完成编译与静态检查，没有启动游戏、执行测试或验证 Harmony 实际安装结果。正式 `1.6/Assemblies/Meow.RaceTrioCompatibility.dll` 保持原文件。后续启用候选需双方使用相同文件并重启游戏；已不同步的模拟状态需要从此前一致的存档恢复，代码补丁不会逆向撤销单端已生成的访客。

## 新增 Desync-09 / 10 的共同根因

两包仍为相同游戏/MP 版本、两玩家、两地图、异步、多派系。三包 `local_logs.txt` SHA-256 完全相同（`831DF24434E39D3AE8DF0CCAA5D3E636D04067DCBC7C0D3DFE05D90C6F460751`），因此旧日志不是这两次新异常的独立时间证据；以各包 desync_info 和主客 traces 为准。元数据仍为 RaceTrio 1.1.0，不能视作本次候选的复测。

- **09**：last-valid 152461；首次捕获差异为 152513 的 trace 179。此前相同。主机在 `Toils_LayDown.ApplyBedThoughts → Room.Role → UpdateRoomStatsAndRole → BrothelColony.WhoreBed_Utility.CalculatePriceFactor` 多执行四次 Rand.Int（179–182）；客户端直接进入 Cumpilation 的 Rand.Chance。对应下一个共同调用的压缩随机状态相差 `4 × 2^32`，即相同种子的四次额外抽取。
- **10**：last-valid 156691；首次捕获差异为 156745 的 trace 148。主机这次通过 `rjw.JobGiver_Masturbate.WillingToMasturbateIn → Room.Role` 进入同一床价缓存，额外执行四次 Rand.Int（148–151）；客户端直接分配下一份搬运工作 ID。两种不同正常业务入口暴露同一缓存缺口，不能靠修改 Cumpilation 或 PickUpAndHaul 消除。

目标 `calamabanana.rjw.brothelcolony`，作者 CalamaBanana，安装目录 `RJW_文化玩法_妓院殖民地-1.6-test`；实际程序集 `RimJobWorldBrothelColony.dll` SHA-256 为 `80EB64E342A4653CF96FE7E5A4F503CB9DE9857D13D9DA4B1C2C8FC1879B8B48`。先读取随包 `1.6/Source/Mod/Helpers/Whoring_Bed_Utilities.cs` 和 `Data/BedData.cs`，再以实际 DLL 反编译核对逻辑。

`CalculatePriceFactor` 的缓存命中、缓存续期、缓存重算共三处调用 Rand.Int；缓存字段 lastScoreUpdateTick、scoreUpdateTickDelay、bedScore、roomScore 不在 ExposeData 中，而且房间信息和床位界面也会触发刷新。刷新是否发生、何时发生可随客户端缓存状态变化。仅保存缓存、仅固定延迟、或仅包住 Rand 都不足以保证返回价格不受本地缓存影响。

新增 `BrothelBedPrices.cs`，在 MP 下将计价改为每次从当前房间状态和 Comfort 计算，沿用原 `CalculateRoomFactor` 公式、床位资格及人数条件，绕过所有缓存读写和缓存随机延迟；房间刷新不再逐张写入床价缓存。单机仍执行原方法。客户选床评分中的业务随机不在本次替换范围。此方案增加按需房间计价次数，未做性能或运行测试。

该模块与援助修复同在最终补充 DLL 中，但按 Brothel 包 ID 独立启用。原核心 DLL 的候选 Pawn 排序兼容保持不变。证据保存在本目录下 `Desync09/`、`Desync10/`、`BrothelDecompiled/`，新包原件和冻结副本的哈希均一致。
