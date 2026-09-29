# Desync-11 / 12：选中增益的单端状态写入

2026-09-10。源码已调整，合并候选 `Meow.RaceTrioCompatibility 1.1.2` 编译通过，0 警告、0 错误。按用户要求没有运行测试；没有替换正式 DLL。

## 证据与根因

- 两包均为 RimWorld 1.6.4850 rev646 / MP 0.11.5+4a3be27-dirty，2 玩家、2 地图，异步时间与多派系启用。元数据仍记录补充程序集 1.1.0、核心 3.0.125，不是本次候选的运行结果。
- 11：最后有效 tick 177361；两端上下文 trace 369–408 头部一致。409 在 tick 177405 首次不同：客户端由 `Nivarian.MapComp_NivarianSelectionBoost.EnsureSelectionBoostHediff` 调用 `GetNextHediffID`，主机已继续正常地图随机调用。客户端 JIT 在相同 tick、`i:False` 首次进入这个添加函数。
- 12：最后有效 tick 180991；上下文 trace 174–213 头部一致。214 时客户端在 tick 181028 经同一函数创建 Hediff，主机下一个记录为 tick 181029 的正常工作 ID 分配。
- 两包本地 trace 总数分别为 850、807，主机为 848、805。这是非空调用级证据，而非根据最后一条报错推测。
- 两包 local_logs.txt 与 07/09/10 的累积、截断日志 SHA-256 完全相同。没有对应新 last-valid 的完整报错行，故不能用旧报错计算本次检测延迟；不将旧近战渲染异常当作这次新增根因。

实际 DLL 的 `MapComponentTick` 枚举 `Find.Selector.SelectedPawns`，为本地图上选中的 Nivarian 创建 `Nivarian_SelectionBoost` 并增加严重度。选中对象是本机 UI 状态，两端可以不同，所以一端额外创建 Hediff、分配 ID，并且以后每 tick 增益幅度也会不同。只同步第一次创建、或隔离随机数都无法修复后续严重度变化。

目标包为 `keeptpa.NivarianRace`，作者 keeptpa，modVersion `20260823162750`。本次再次核对目标 DLL SHA-256，仍为 `1F0100B0E8FFCA1A176D8EB2B297F7F852B5A78300D0FD5FF8783900AA3D39C2`，对应前次保存的实际 DLL 反编译源码。原 Hediff XML 的每天 -2 衰减、原 Tick 的增长和上限保持不变。

## 修改

新增 `Source/RaceTrioCompatibility/NivarianSelectionBoost.cs`：

- 仅替换原地图 Tick 中的 SelectedPawns 读取，原来的创建、增加严重度及阈值处理仍由原模组执行。单机保留原本机选择读取。
- 本地 Update 将关注列表变化作为同步命令发送，携带目标地图、玩家 ID 和 Pawn 引用；不在界面中修改 Hediff。
- 每张地图保存各玩家的关注列表、派系、剩余有效 tick 及本地图计数。同步执行时核对地图所属派系、Pawn 所在地图和种族；不存在或死亡的对象不进入增益列表。
- 同一角色被多名玩家关注时去重，并按 Thing ID 排序，保证一次地图 Tick 只累计一次增益。
- 取消选择/切换地图发送空列表；持续关注每 60 地图 tick 续期，180 地图 tick 无续期后自动停止。断线处理不在 Tick 中读取各端到达时间不同的玩家列表。地图暂停时计数和过期均暂停。极端网络延迟下可能暂时过期，但该过期结果在各端一致。
- 快照保存整个模拟状态；本地已发送记录不保存。重连后重新提交本机选择，回放/追帧期间不发新输入；旧玩家 ID 的记录有限期失效。

注册入口在 Bootstrap，版本更新为 1.1.2。此 DLL 同时包含前次六类援助的决策/冷却/信件修复，以及 Brothel 床价缓存随机数修复，无须叠放多个版本的同名 DLL。

同类直接选择读取检索覆盖了本体反编译目录。另有 Gizmo 批量操作、无人机菜单与覆盖层绘制读取选择；没有发现第二个以该模式每地图 Tick 添加增益的本体组件。此次不把菜单或纯绘制选择自动改成全局共享选择，也不宣称所有扩展的 UI 已逐项运行验证。

## 交付与限制

最终候选：`BuildValidation/DesyncEvidence/Desync11_20260910/CombinedCandidate/Meow.RaceTrioCompatibility.dll`。

SHA-256：`B627EA361CC43703F84D6788143C21964AB6136316FB87DBBCECABEBC54F2CBA`。

两包冻结副本、提取内容、哈希清单和候选反编译检查位于各自 `Desync11_20260910`、`Desync12_20260910` 证据目录。源压缩包与冻结副本哈希相同。

只完成编译与静态审查；未启动游戏、未运行联机/重连/性能测试，未验证实际 Harmony 安装结果。正式 `1.6/Assemblies` 仍是原文件。后续启用候选需要双方替换同名补充 DLL 并重启，使用此前一致的存档；补丁不能逆向消除已经发生的单端状态差异。
