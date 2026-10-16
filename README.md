# Full release 3.0.159 (2026-10-16)

包含全部 22 个生产模块及此前未发布的累计修改：恢复每张地图的原生电网与环境更新；修正母舰能量场读档后已通电建筑残留断电图标；同步涅拉每日骰子的地图、阵营及世界时钟，隔离钻地货舱视觉随机数；补齐龙霜锻炉队列移动、Monolyn 坩埚开工与援军落点同步，并解除母舰无关重编译对随机数补丁初始化的连带影响。保留其他现有功能与实验开关，已退役的整图节流不再生效。

核心 3.0.159、RaceTrio 1.1.12、TaleNivarian 1.1.10。全部 22 个项目重新编译及方法体比对通过，发布沿用本地已部署 DLL。电力专项单图、双图普通及异步测试通过，双图两种模式各超过 12 万共享世界刻，异步包含三次冷重连；完整存档三种建筑的功能和中文电力信息已核验。其他累计补丁各自验证范围不同，最新锻炉／坩埚／援军修改仅完成编译与静态检查；不宣称完整 248 模组组合长测通过。电力信息缺失未复现，其他独立日志错误未在此一并修复。

GitHub 完整 ZIP 包含运行文件、完整源码、测试和工具；工坊仅提供必要运行文件。自动存档恢复草稿仅保留源码与测试，未加入运行模块。所有联机玩家须更新整包相同文件并完全重启游戏。

[完整发布记录](Docs/releases/3.0.159-full-20261016.md)。

---

以下为历史版本发布时的记录；其中“仅本地／未发布”等表述描述当时状态，相关修改现已纳入上面的全量版本。

# Local patch 3.0.158 (2026-10-13)

停用会跳过整段 `MapPostTick` 的实验地图调度，修复第二张地图原版发电机与电缆已连接、用电器仍不能启动的问题。电网、环境及特效恢复原生逐地图更新；旧存档调度字段兼容保留，设置界面标明已停用。其他独立优化保留。

旧版双端对照已复现 map 0 灯亮、map 1 灯灭。新版单地图对照、双地图同步 120105 共享世界 tick、双地图异步最终 120090 tick 和三次冷重连均通过；各轮电网与 Fleck 更新次数正确，最终供电、世界/地图随机状态一致。测试为本机精简负载，未完成完整 247 模组现有存档回归。原日志角色空引用等独立报错未在本次修复。

已本地部署，未发布到创意工坊或 GitHub。所有玩家须安装相同文件并完全重启。[修复与验证记录](Docs/releases/3.0.158-native-map-power-20261013.md)。[3.0.157 基础上的增量更新包](Releases/NativeMapPower-3.0.158-update.zip)。

# Local patch 3.0.157 (2026-10-13)

2026-10-13 本地补丁 3.0.157：TaleNivarian 1.1.9 为涅拉每日随机事件骰子的刷新、投掷、使用和跳过建立共享世界时钟、原生 home-map 与阵营管理器上下文，补齐 UI 每日刷新同步；修正钻地货舱烟尘读取镜头地图暂停状态及两处显示偏移消耗共享随机数的问题。核心保留 3.0.156，RaceTrio 保留 1.1.11。清洁环境普通时间 120000 世界 tick、双地图异步四轮各 30000 tick 目标与三次冷重连完成；货舱专项双端完成状态、三个原生交付和最终随机状态一致。完整 247 模组现有存档回归未完成，按用户授权先依据专项测试本地更新。Desync-469 闪电与 474 地下通道可达格分歧仍未定位。所有玩家须更新相同文件并完全重启。

[骰子诊断、源码依据与测试范围](Docs/DESYNC_469_476_NIRA_DICE_20261013.zh-CN.md)。此版本仅本地更新，未推送创意工坊或 GitHub。

# Full release 3.0.155 (2026-10-12)

修复涅瓦莲冰雾在交易、更换文化职位窗口打开期间，读取本地镜头地图暂停状态造成的随机数分叉。联机模拟使用当前正在推进的地图上下文，保留本地视觉偏好与冰雾随机数隔离。兼容母舰联机模块 NMC 0.9.8，保留其原生无人机、炮塔的随机数与存档状态管理，避免重复改写导致补丁初始化失败。

RaceTrio 1.1.11、TaleNivarian 1.1.8；核心保持 3.0.151，其余 20 个生产程序集与现有实验默认值不变。双端双图异步简测通过：18 项配对操作、12 次 NPC／世界商队钢材成交、10070 共享 tick，最终世界／地图状态与随机数一致。更换职位覆盖窗口打开、停留、取消，未验收实际授职仪式结果。按用户要求未扩展完整整合包长测或冷重连。所有玩家须更新相同文件并完全重启。

[发布与验证记录](Docs/releases/3.0.155-desync-fog-nmc-20261012.md)。创意工坊与 GitHub 完整包使用同一运行载荷。

# Full release 3.0.153 (2026-10-11)

修复 Desync-452：Monolyn 强制洗礼确认后，联机命令调用原生非虚基类入口，避免重复确认并保留正常进入建筑任务；Replace Stuff 消雾在回调所属地图判断迷雾，避免两端镜头地图不同导致蓝图销毁分叉。RaceTrio 1.1.10、DesyncBatch 1.0.10，其他生产程序集不变。

双端简单功能验证见 [诊断与测试记录](BuildValidation/DesyncEvidence/Desync452_20261011/REPORT.md)。完整 247 模组回放双端因内存不足中止；按用户要求不扩展长测与冷重连矩阵。全部玩家须更新相同文件并完全重启。创意工坊与 GitHub 使用同一运行载荷。

# Full release 3.0.152 (2026-10-11)

针对 Desync-449/450 揭示的武器法典故障加入补丁：兼容母舰 0.9.7 的随机数补丁，并将 Monolyn 初始化独立；同步命令内产生的法典菜单延后在操作玩家界面显示，避免 MP 调试模式用菜单文字哈希转发操作。发布全部 22 个模块与现有实验内容，GitHub 同步完整源码、测试、工具与文档。感谢 AzzyH 的设计参考。

验证：22 项目编译、506 项目标结构检查通过；新增菜单边界 10 项静态检查通过。按用户要求停止运行测试并直接发布，本次新补丁双端运行验证未完成，不声明快速测试或长测通过。实验开关保持现有默认值；所有玩家更新相同文件后彻底重启。


[完整发布记录](Docs/releases/3.0.152-full-20261011.md)。

# Full release 3.0.151 (2026-10-11)

发布当前全部 22 个生产模块和现有实验功能，GitHub 同步完整源码、测试源码、工具及文档。包含 3.0.148–3.0.151 的主角认定、随机数与状态持久化、米莉拉穿梭机返航修复，以及涅瓦莲本体更新兼容和 Monolyn 补充程序集 1.1.8。武器法典同步存入、取出、神圣武器切换、落地取出及机械体切换，保留原生装备与工作行为；感谢 AzzyH 的设计参考。

验证：22 项目编译通过，发布使用当前归档 DLL。Monolyn 基础两图/冷加入专项及法典七项双端操作通过；涅瓦莲历史正常/异步三轮冷加入与长测通过，但该专项使用当时独立候选，不能作为本次全部模块组合验收。完整最新整合包未新增全量双端长测。实验功能保留既有开关与默认值。所有玩家须更新完全相同的文件并彻底重启。


[完整发布记录](Docs/releases/3.0.151-full-20261011.md)。

# Local release 3.0.151 (2026-10-09)

3.0.151：修复米莉拉穿梭机返航后连船带人丢失。联机乘员检查跳过未加载的子地图，避免任务穿梭机阻塞整个运输管理器；已落地的玩家穿梭机不再重放旧的已开始抵达任务，防止降落容器因所有权冲突销毁船体。单端主持测试：两地图、两种穿梭机 12 次带人往返及 10,000 tick 专项通过；最新 320 模组存档副本中地图 2 原 Wyvern/Harrier 均带人往返成功，旧队列清空，另推进 1,000 tick 并成功保存联机快照。完整存档测试仅把读取阶段改为主线程，以绕过后台贴图加载崩溃。原存档未改动。所有玩家需更新相同文件并完全重启游戏。

证据：[存档与验证记录](BuildValidation/MiliraShuttle_20261009/verification.json)。

# Release 3.0.147 (2026-10-03)

375 的妓院通知消息 ID、378–380 的 PitGate 镜头震动随机数、以及此前 355/356/358 的 Ariandel 本地音乐随机数已加入定点兼容补丁。22 个生产模块编译通过；新补丁仍需双端场景验证，376/377 的上游原因未定。所有玩家须安装同一完整版本并完全重启。详见 [3.0.147 发布说明](Docs/releases/3.0.147.md)。

# Local release 3.0.146 (2026-10-01)

Stable Pick Up And Haul equal-distance and unload ordering; deterministic Milian hediff ability initialization during simulation ticks. Targeted two-map native diagnostic smoke passed 10,000 shared server ticks. Full representative validation results and unresolved original causes are documented in [the investigation report](BuildValidation/DesyncEvidence/Desync340-345_20261001/REPORT.md). All players must restart and use the same build.

Previous investigation follows.

# Desync325–328 调查与候选补丁（2026-09-30）

已完成源码修补和配置行为调整，冻结候选主模块 3.0.143。专项双端通过 10,000 共享 tick；完整整合存档在加载阶段失败。尚不能宣称四次不同步全部解决，也没有覆盖正式 3.0.142 DLL。

## 四次日志的最早分歧

共同条件：2 玩家、3 地图、async time=false、multifaction=false；RimWorld 1.6.4850、MP 0.11.5+4a3be27-dirty。主机 debug、客户端开发者模式开启，后续测试关闭开发者模式。

| 包 | 最后有效 tick | 首次采样分歧 tick | 具体证据 |
|---|---:|---:|---|
| 325 | 3148141 | 3148192 | 主机 Milira_Race91594 执行 Raven 补货任务，SplitOff(1) 分配 ThingID，继而结束任务分配 JobID；客户端跳过这两项。后续 Cumpilation/RJW 正常随机调用不是最早原因。|
| 326 | 3149971 | 3150005 | 同一 Explosion2637735、相同输入 Rand=170111391860738，主机处理 Filth_FlammableBile2637499 的伤害舍入，客户端已进入另一目标的随机伤害分支。|
| 327 | 3152251 | 3152285 | 同一 Explosion2637886、相同输入 Rand=196057289295874，主机爆炸已 DeSpawn，客户端多处理一次 ExplosionImpact/伤害。|
| 328 | 3154921 | 3154976 | 同一 Milian_Mechanoid_PawnII2636198、相同输入 Rand=223536490055682；双方此前都执行 Promotion 添加 Knight hediff。主机 AI 经 Hediff.AllAbilitiesForReading 创建 AbilityID，客户端直接进入后续 RJW hediff 分配。|

325/326 本地日志当前检测 tick 分别为 3148204/3150034，均距最后有效 tick 63。327/328 本地日志为旧/截断窗口，不能把旧检测时间当成这次首次分歧。原 ZIP、哈希、完整滚动栈及 JIT 摘录均已保存。

## 已修补的确定性边界及证据强度

1. **已确认代码缺陷：Raven 搬运无人机调度状态未进入快照。** 原生 ExposeData 保存无人机及 nextID，遗漏 dispatchCounter 和两个搜索游标；PostLoadInit 又新建 taskFinder。客户端重建后调度相位与主机不同。新增三个 Scribe 字段，在原生 PostLoadInit 替换 finder 后恢复。旧存档缺字段按原生初值 0；新主机联机快照包含当前值。
   **325 的因果限度：** 轨迹中的直接路径是地面补货管理器，已序列化自身飞行列表。搬运调度缺陷可能改变上游资源/任务，但这段滚动窗口没有证明它独自导致 Milira_Race91594 的任务跳过。
2. **已确认处理顺序风险：原生爆炸遍历 ThingGrid 插入顺序。** 相同目标集合如果装载后顺序不同，会把同一随机流作用于不同目标。将 DamageWorker.ExplosionAffectCell 的目标读取得到的副本按 thingIDNumber 排序，保留网格原顺序，不增加随机隔离范围。专项测试在主机故意反转网格后，三轮实际爆炸的目标、伤害和 Rand 与客户端完全相等。
   **326/327 的因果限度：** 相同随机输入而不同伤害/结束分支明确证明模拟控制流先不同。采样没有完整目标集合、受损物件状态和历史，因此不能排除更早的目标成员/状态差异；排序仅修复顺序边界。
3. **已确认能力 ID 界面污染入口：** MP 保护 Pawn_AbilityTracker 的界面 getter，但第三方可以直接访问 Hediff.AllAbilitiesForReading，其原生懒初始化仍会分配全局 AbilityID。新增 MP.InInterface guard，界面仅返回现有列表或临时空列表，正常模拟保留初始化。双端断言界面访问没有初始化缓存，模拟访问产生同一 Ability_129。
   **328 的因果限度：** 轨迹直接证明能力缓存初始化先后不同；尚未捕获客户端此前是哪次界面/其他调用初始化缓存。

未采用全局 Rand.PushState、强制相同 ID、跳过模拟 tick 或修改 MP 时钟等掩盖差异的方案。

## 配置调整

- 删除本补丁的配置差异强制禁用“仍然连接”及替换连接回调行为；保留原生 MP 协议/Def 不兼容限制。真正需要启动时应用的配置，不再由本补丁强制要求修复并重启。
- 混合批次逐项热加载，已验证成功的热配置不再因另一项需要启动而整批撤销。标准 ModSettings 重读并执行 WriteSettings；实例及静态字段进入验证/回滚；炮台等支持运行期切换的开关立即生效。
- 主机不存在而客户端存在的配置尝试运行期恢复默认；成功后删除长期配置文件，让下次正常启动也维持主机默认状态。
- 启动绑定项或无法验证的热加载项：保留当前运行期值，把主机文件写入长期目录，标记待下次启动，不冒充热同步成功、不自动继续连接。主模块安装期补丁开关、XML Extensions 的 Def-time 设置仍属于此类。
- 原生“修复并重启”在父进程 SaveConfigs 后即持久保存；重启子进程迁移一次后，正常读写指向 SaveData/Config，而不是仅使用 MultiplayerTempConfigs。HugsLib 对应 SaveData/HugsLib/ModSettings.xml；首份原文件备份在 MPMeowHostConfigBackup，待启动状态在 MPMeowHostConfigState.xml。
- `-mpmeowhotcfg=false` 关闭热加载时，仍保留手动连接和长期保存。

热加载覆盖面取决于第三方实际加载机制。标准字段重读不能证明所有第三方启动期缓存/Def 均已重建；HugsLib 专门路径本轮未进行实际完整模组专项回归，不宣称全部配置都能热同步。

## 验证结果

| 测试 | 冻结候选 | 结果 / 范围 |
|---|---|---|
| 编译 | Candidate03 | 0 警告、0 错误 |
| G:/RWDesync325ConfigR4 | 3.0.143 | 原生 JoinDataWindow，14 组断言 PASS；混合热/启动项、父进程保存、模拟 restart child 路径、默认重置、静态字段失败回滚、首次备份及新设置实例读盘。不是实际冷重启进程矩阵。|
| G:/RWDesync325ConfigOptOutR1 | 同一 SHA | 禁用热同步，1 组原生窗口断言 PASS，连接回调可用。|
| G:/RWDesync325NativeR2 | 同一 SHA | 实际两端 ClientPlaying，真实配置不一致加入并热应用炮台；冻结世界/地图 Rand 基线相同；5 条爆炸/能力/无人机断言及 3 条炮台断言双端相等；双方完成共享 tick 10002，desynced=False。单地图、14 正式包加 probe，非整合长测。|
| G:/RWDesync325ReplayR1 | 同一 SHA | 用户确认 Player6554's game.zip，320 正式包加 probe，三地图。FAILED_SETUP：加载尚未启动联机服务器即失败；未完成 ClientPlaying 基线，不能计入 smoke/soak。|

NativeR2 冻结基线：worldTicks=2, worldRand=398780327；mapID=0, mapTicks=2, mapRand=3053216096。三轮爆炸 targets=36903/4/5、36907/8/9、36911/12/13；每轮 health=148,-1,-1；Rand 分别为 26168584103、51938387879、77708191655。新实例无人机状态=13:9:17。完整主客日志和哈希归档 Runtime/。

ReplayR1 首个当前输入异常：host.log:3980–3982，RK_InfernoGrenadeLauncher2159447/2162688/2328696 的 /codedPawn 重复空 load ID；3985 起 RatkinAnomaly.RAComponent.FinalizeInit → ResearchManager.Notify_MonolithLevelChanged → MP FactionRepeater.Template NullReferenceException；3995–3996 GrandThrone1247161/2425534 的 /sourcePrecept 同类异常。原始输入没有删节点、吞异常或替换成新殖民地来制造通过结果。此前还有 ShowHair API 版本及其他启动错误，但这次控制器停止于上述快照加载失败。输入是 03:31 回放，早于下午四个 desync，不等于四次触发时完整状态。

失败尝试保留在 G:/RWDesync325ConfigR1、R3 与 G:/RWDesync325NativeR1：配置夹具次序/首份备份预期以及同格物品互相 wipe 的夹具错误。修正仅测试夹具后从新目录重跑；这些失败不计为 PASS，也不声称是产品原因。

尚未通过用户三地图完整重放、三次冷重连、120,000 共享 tick 长测。按 rimworld-mp-compat runtime-testing.md 的发布门槛“Copy the exact archived candidate tested in Gate D to the release assembly folder.”，此候选尚未覆盖正式 DLL，也未上传 GitHub/工坊。所有测试进程已退出。

## 构建与复核

- 主 DLL：Candidate03/MP_MeowOnlineShop.dll，版本 3.0.143.0，informational=3.0.143-desync325-partial-hot-config。
- SHA256：C46B8AA57970B8E36F3CA54027519205B5B6688CFC8F1189B32CBA6A684951FC。
- MVID：b311926f-f1f7-433a-acd9-5f1b15ad5578。
- 当前正式主 DLL 仍为 3.0.142：6BB1946CD4F231A42CAFFED21D29968AC3B7B65C481CD19D73BA6BBFB71D4F51。
- 当前安装源权威：Raven 1.0.6/zuoyao.ravenrace，SHA74AE9E1D…；原生 Assembly-CSharp SHA8FD7E750…；MP SHA28D2CDA4…；Milira SHA371891B8…。完整 SHA 和路径见 provenance.json。Raven 当前反编译签名与轨迹类型匹配；并未证明每位远端机器 DLL 都等于当前安装哈希。
- source-changes.diff 保存配置、版本、工程、新边界的变更；启动注册在 Patch_SellSlingshot 加一行 Patch_Desync325Boundaries.Apply(harmony)，该文件冻结全文在 SourceSnapshot。测试源和精确 harness DLL 在 HarnessSnapshot；生产包不含 harness。
- 候选增量包：Releases/MP-Race-Compatibility-Plus-3.0.143-Desync325-328-Candidate.zip，基于已安装完整 3.0.142，仅主 DLL/PDB、候选 About、说明和哈希。它不是独立完整模组。

