# 涅瓦莲本体更新联机兼容 — 2026-10-11

目标为本机 `3624805128`（keeptpa.NivarianRace，About 版本 20260922151508，RimWorld 1.6）。依据模组提供的 17 条更新日志（2026-08-05 至 2026-09-22）、实际 Def/XML 与安装 DLL 的反编译/IL 核验补齐；未把日志中未实际发布的类型当作已安装功能。

## 交付范围

兼容代码在 MP-meow-online-shop，目标模组本体 DLL/Defs 不作修改。最终候选 Candidate14：RaceTrioCompatibility 1.1.7，TaleNivarianCompatibility 1.1.6。

| 更新涉及功能 | 兼容处理 |
|---|---|
| 锁定增援人数、来访者配额与指定落点 | 本地选点和取消不写共享来信；选定 Map/Letter/IntVec3 后同步回放原生投放；重复接受受 Pending/过期/所有权检查保护，保留原生人数与名单 |
| 来访者接待、拒绝、隐藏来信及离开 Lord | 同步原生接待/拒绝，共享归档的 Pending 来信不被本地 UI 清理；拒绝使用当前原生好感规则 |
| 多地图来信倒计时 | 倒计时、过期判定与共享归档清理使用目标地图时钟；在目标地图的时间、派系、Rand 上下文中执行原生移除与 Lord 通知 |
| 弹射炮、输入扫描半径、仅需燃料设施与物流订单 | 同步原生开关/半径，节点、需求、库存等候选稳定排序；不重写投放和订单完成规则，避免重排缓存每 tick 无条件失效 |
| 武装动物警戒点、半径和取消 | 原生指令同步，保存时修正时间戳，取消保留原生队列与命令清理 |
| 集尘器石块选择、容器工作范围与避雷针 | 原生 UI 选择同步；随机候选按 DefName/ThingID 排序；工作范围同步原生 setter 与重新扫描通知；避雷针冷重连恢复当前父建筑的电力组件引用，保留储能和计时 |
| 事件开关和同盟要求 | 将已删除的全局字段适配为当前每事件字典；共享状态保存，房主初始导入；设置界面使用共享副本并恢复各端本地偏好 |
| 借用殖民者、机械臂/工程无人机与旧补丁目标变化 | 更新已搬移的借用记录判定，保留原生人形/重复/销毁检查；重定位当前编译器回调；移除已删除组件字段的硬依赖 |
| 母舰模组联用 | 两个随机调用已由经 DLL/IL 核验的 NMC 0.9.6 处理时，保留该处理，避免新 Meow DLL 的 MVID 与母舰旧白名单冲突；仅对经文件 SHA256 核验的固定 MVID、确切 owner/type/method 匹配生效 |
| 重复机械臂蓝图、负自建工期、穿梭机燃料白名单、NPC 社交与材质修复 | 当前本体已经实现；核验共享模拟路径和现有同步入口，不复制本体修复 |

## 精确产物

- `BuildValidation/NivarianUpdate_20261011/Candidate14/Meow.RaceTrioCompatibility.dll`
  SHA256 `38338B32C4B955628E0FD8A9CE9FADFCFFF34A700A58DAB5133BF1D6467EE0CF`
- `BuildValidation/NivarianUpdate_20261011/Candidate14/Meow.TaleNivarianCompatibility.dll`
  SHA256 `24992CCB026620515AC51824B27D3A7BB5A0FB711B3040B78F9B6B266779724D`
- 被测本体 `Nivarian_Race.dll` SHA256 `A58F00EBC704AB03163BFF94181B3C6901D2B86F9F500FB0C4D470D7A10CBFF3`
- 审核后保留的 NMC Multiplayer DLL SHA256 `52FC4AE9B77AB11B9286615C8E78DCB98B87AB149FB568FE81CFF0FDA9C48E85`

候选归档包含源码和 build-hashes.json。运行目录的 inputs.json、原生命令断言、冻结状态（完整世界/地图 Rand 状态）、进程命令行及 result.json 为原始证据。测试 Probe 仅在隔离游戏目录加载，不部署到正式模组。

## 验证与边界

最终三组双端回归均通过，测试与正式部署均使用同一份 Candidate14 DLL。

| 已接受回归 | 地图/模式 | 非房主原生操作 | 冷重连 | 最后浸泡 | 用时 |
|---|---|---:|---:|---:|---:|
| NU27 / Accepted/Normal | 单图、母舰联用 | 192 | 3 | ≥120,000 世界 tick | 2228.62 秒 |
| NU31 / Accepted/Async | 双图异步、不同速度、简体中文客户端、非空来访者夹具 | 384 | 3 | ≥120,000 世界 tick | 2518.03 秒 |
| NU33 / Accepted/Representative | 实际 247 模组存档副本、单图 | 48 | 0 | ≥10,000 世界 tick | 1221.93 秒 |

三组均通过逐条双方原生命令回执、冻结的完整世界/地图 Rand 状态、共享字段、原生燃料 Delivered 订单、模块/本体哈希与成对有序 ModsConfig 核验。实际存档验证保留原生异步场景切换，仅将后续数据读取改为主线程以避免后台图形加载崩溃；测试模式还解析原生 Managed DLL 的 ReflectionOnly 依赖。这些装置改动未进入生产兼容 DLL。

已复制完整接受日志、输入哈希、控制器/Probe 源码与 Probe DLL、被测运行包 DLL、配置、进程命令行和冻结断言至 `BuildValidation/NivarianUpdate_20261011/Accepted/`，不用依赖 G 盘临时目录复核结果。

每图每轮从非房主调用 48 个原生 UI 动作；各轮覆盖开关、滑条、警戒点/取消、反复开关来信、重复接待/拒绝、集尘器产品、容器范围、避雷针开关、增援预览/取消/重复落点以及每事件设置。额外检查仅需燃料设施的原生 Delivered 回执、缓冲孤儿订单货物守恒及闭单幂等。最新夹具还创建原生飞行货物后销毁目标并重复调用完成链，检查货物落地守恒与 DestinationGone；该项不等同于全程等待实际飞行动画落地。

实际存档源只读复制为 RepresentativeInput/NivarianInput.rws，SHA256 `1D593D6FE4C8ACC1EA5272088A51F42AE99AF56B1F2CCB990003DEB798239FD2`。保持源存档原样，保存加载测试只在独立 savedatafolder 执行。

需要明确保留的限制：

1. 日志的回收猎犬在当前 DLL/Defs 中没有实际类型或定义，不能验证尚未存在的功能。
2. 当前 Raven 1.0.6 缺少来访者定义期望的 `Raven_FusangEliteResponse`。原生来访者配额因而为空；NU31 仅在测试内将其指向已安装 `Raven_FusangGuard` 以检验非空名单的原生生成、保存和重连。本次未修改生产 Def 或伪造缺失 PawnKind。
3. 借用记录过滤已验证原生入口；完整借用任务从借入到穿梭机归还的端到端流程不在本次断言范围内。多玩家派系的专门双派系场景没有完成专项回归。
4. 旧候选和失败夹具不计为最终通过：NU14 找到冷重连电力缓存空引用（已修复）；NU16/17 的浸泡前仍关闭弹射炮服务导致燃料耗尽（夹具修复后重测）；NU19 找到母舰 MVID 重叠补丁问题（Candidate14 已避开重复接管）。NU20/21 因候选被替代主动结束；NU18/23/24 是启动前装置错误；NU26 发现 Prepatcher 内存加载的 Assembly.Location 为空，最终候选改用已审查 MVID 判定；NU22/25 在候选替换后主动结束；NU29 在内存压力下延期；NU30/32 为存档装置加载错误，最终区分异步场景切换与主线程数据读取后 NU33 通过；NU28 经过前两次冷重连后快速地图已推进 280,934 tick，原先 100 木材不足覆盖剩余浸泡，主动停止并用充足的合法堆叠库存在 NU31 重跑，补丁 DLL 未变。所有失败证据保留。

## 部署

只替换正式 `1.6/Assemblies/Meow.RaceTrioCompatibility.dll`（1.1.7.0）与 `Meow.TaleNivarianCompatibility.dll`（1.1.6.0），哈希与 Candidate14/三组接受回归完全一致。备份位于 `BuildValidation/NivarianUpdate_20261011/BeforeDeployment/`；ReleaseManifest 仅更新这两个 DLL 的条目，并追加本次独立验证记录，保留旧核心验证历史。

其他运行 DLL 在部署前与冻结候选逐个比较，无变化。被测实际核心 SHA256 为 `29888D9BD3A8821DCFBDEC963DBEB218E1C5A61B176803FA0B985A30AF509BAA`，与 Manifest 旧记录中的核心 F4B0… 分开记录；本次没有重建或覆盖核心。涅瓦莲本体、原始存档和母舰文件未改动。测试进程已退出，测试 Probe 未部署到正式目录。
工作区另有并行莫诺琳任务修改了 RaceTrio 的 Bootstrap 与五个 Monolyn 源文件，已保留。这些后续源码不在本次冻结 DLL 中；复现本次通过产物应使用 Candidate14/Sources。差异哈希记录见 concurrent-source-drift.json。

## 使用

所有联机玩家更新相同兼容包文件并完全退出、重启游戏。本次不会替用户结束正在玩的游戏。以后本体或母舰 DLL 更新，须重新核验反射目标与随机补丁接管，不应将本次通过推广为任意未来版本或全部模组组合的保证。
