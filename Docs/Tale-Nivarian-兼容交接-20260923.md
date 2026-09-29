# Tale of Milira / 涅瓦莲联机兼容交接文档

整理日期：2026-09-23。范围：创意工坊 3477405110、3624805128。本文件是工作交接，不是完整兼容或发布合格声明。

## 2026-09-23 续办进度

### 2026-09-25 R190 袭击世界对话的存档边界

`G:\RWTale0925\RaidDialogStorageR190` 在限定 18 项模组、双基地异步短局完成一次原生补给袭击与两阶段对话，主客袭击回执逐字一致，1,206 共享 tick、`desynced=False`，测试进程全退。新增只读探针在非主机实际点击前分别记录第一、第二阶段对话：两次均为 `mapContext=none persistent=none`。本机 MP 源码 `Multiplayer.MapContext` 只取当前地图 tick 或地图命令，`CancelDialogNodeTree` 在 MapContext 为空时不会建立可序列化 `PersistentDialog`；正式 `TaleSupplyDialog` 的袭击事件 ID 则存在进程内 `ConditionalWeakTable`。因此**已打开的世界袭击对话随新客户端加入/存档恢复的行为尚未由当前补丁证明**；需要专门的中途加入测试与持久化执行边界设计。R190 只证明正常连续点击同步，不能认定打开窗口时加入也兼容。审计摘要：`BuildValidation/TaleNivarianResume_20260923/AuditR190/raid-dialog-storage-verdict.json`。正式 DLL 未改。

### 2026-09-25 R188–R189 招募角色冷加入功能通过

R188 的四类原生地点对话与留存招募回执在双基地异步短局通过；新客户端冷加入后，真实商队、招募角色和成员列表在主机与客户端冻结状态中一致。后续六项测试动作被夹具的“阶段必须从 6 开始”断言挡住，不计完整冷加入功能通过。已将测试专用驱动改为允许聚焦局从阶段 0 开始，并修正三项动作各自分配到两张地图；正式兼容 DLL 未改。

R189 使用修正后的探针再次完成四类地点对话、接受/拒绝与并窗路由，主客招募角色和真实商队回执相同，初局 1,208 共享 tick、`desynced=False`。一位全新客户端从新加入点加载后，主客冻结状态逐字相同；随后地图 0 和地图 1 各三项同步动作（阶段 0–5）逐字相同，至少 1,200 共享 tick 后最终状态逐字相同，留存角色仍在商队。运行中的 `ColdJoin.ps1` 仍以旧阶段 6–11 筛选，故其 `cold-result.json` 为**测试控制器误判 FAIL**；已把控制脚本改为可指定起始阶段，且 `BuildValidation/TaleNivarianResume_20260923/AuditR189/Verify.ps1` 直接核验原始状态、六条动作、模组哈希、静音隐藏启动与进程退出，`recruit-cold-verdict.json` 为功能证据 PASS。此结果只证明已完成招募后的持久化，不覆盖对话窗口正打开时的保存/加入。按用户要求不做长测或追查偶发不同步。

### 2026-09-25 R187 招募角色留存与测试范围调整

`G:\RWTale0925\CombinedR187RecruitPersist` 在双基地异步综合局完成四类地点对话、48 项补给奖励及既有功能门禁；新增的原生招募角色留在真实商队中，主客 `TALE_OTHER_DIALOG_RETAINED` 回执相同，综合阶段 `desynced=False`。随后测试夹具在主机冷加入冻结前要求一处旧自建核心仍存在，断言失败，因此 **R187 没有取得冷加入结论**。正式 DLL 未改。按用户最新要求，后续不追查偶发错路由或此类偶发夹具异常；集中验证确定可触发的功能入口。保留角色的冷加入改由独立四对话短局验证。

### 2026-09-25 R182–R186 补给路由诊断与一次短冷加入

R182 为诊断探针自身空值错误，尚未到达目标动作，不计补丁结果；已给探针加防护。R183 重跑完整综合局 PASS：48 项补给奖励、四类地点招募及原有门禁通过。为缩短低频错路由的复现时间，新增 `-SupplyOnly`：R184 与 R185 各在双基地异步短局连续通过 48 项奖励、96 次非主机点击身份检查；正式补丁未改。R185 在正式拦截与原版 MP 按序号拦截之间加只读前缀，所见放行均处于 `inInterface=False` 的同步重放，未发现本地点击放行。运行时 Harmony 栈确认正式拦截优先级 800，高于原版 MP 两个优先级 400 的前缀。

`G:\RWTale0925\CombinedR186RouteCold` 将新诊断并回完整局：三轮袭击、48 奖励、四类地点、任务、模块、原生搬运和建筑放置等共 19,371 共享 tick PASS；非主机 96 次点击身份全部可解析，本地放行 0。随后保留主机，新增一位客户端冷加入，冻结/终态一致，1,200 共享 tick 和双基地六项动作 PASS。`BuildValidation/TaleNivarianResume_20260923/AuditR186/combined-route-cold-verdict.json` 核验同一正式 DLL、18 项加载表、隐藏窗口、五项音量零和测试进程退出。**R179 偶发错路由尚未解释**；这些通过的重复运行不等于已修复。此轮四类招募夹具在结算后清理了角色与商队，冷加入不证明已招募角色的原生持久化；R187 已为此保留真实商队成员并将其加入冷加入快照门禁。

### 2026-09-25 R181 四类地点对话合并回归通过

`G:\RWTale0925\CombinedR181` 使用同一正式 DLL SHA256 `BB4EA3FE…CE9697D` 与限定 18 项加载表，在双基地异步局完成 18,937 共享 tick。三轮补给袭击、两种教堂、SOS、48 项补给奖励、原生模块安装/卸载、自建建筑材料搬运和十种菜单放置、12 项任务生成及 8 项原生结算、三轮魔法少女对话，连同四类新地点招募对话，均通过主客回执门禁。四类地点分别执行接受、拒绝、接受、拒绝，并分别点击无关诊断窗口；主客对话回执逐字一致。补给对话的 96 次点击前身份检查全识别目标原生回调与地点/商队，夹具进入新对话前两端各清理 48 个旧诊断窗口。控制器及 `BuildValidation/TaleNivarianResume_20260923/AuditR181/four-world-dialog-combined-verdict.json` 独立审计 PASS，双端 `desynced=False`，零失步信号；五项音量为零、隐藏窗口、测试进程全退，用户进程未动。

**R179 的偶发补给按序号误点仍未解释**；R181 的成功重复不能排除它。尚需进一步定位该时序缺口，并继续检查保存期间窗口生命周期、可选冬季炮塔、外部联动及其余 P2 分支。因此不宣称两目标模组已完全兼容。

### 2026-09-25 R180 功能阶段通过，收尾夹具重复清理失败

R180 的双基地异步综合局完成 48 项 Tale 补给奖励、前序模块/自建建筑、12 项任务、三轮魔法少女对话和新增四类地点对话；四类对话的接受/拒绝与干扰窗口回执在主客一致。R179 的第 31 轮补给错路由未重现，测试身份日志显示本轮所有奖励点击的原生回调、地点和商队都被正式补丁识别。收尾 `TaleSupplyProbe.Complete` 因四类地点夹具已提前清掉 48 个补给诊断窗口，错误地要求再清理至少一个而失败；**R180 控制器 FAIL，不计正式综合 PASS**。已把收尾门禁改为核对早期清理数量，R181 复验；R179 的间歇错路由仍需后续判因。

### 2026-09-25 R179 补给对话第 31 轮路由缺口待定位

R179 在综合局运行到第 31 项 Tale 补给奖励时，非主机两次点击原生奖励离开选项，随后主客都执行了原版 MP 的 `SyncDialogOptionByIndex(0)`，该按序号命令选中并存的 `Routing diagnostic decoy`，使无关回调运行；夹具按预期中止。前 31 项奖励已完成，无 `Desynced after` 信号，但本轮 **不计综合 PASS**。R178、R176 的 48 项曾通过，因此该缺口呈现为时序相关的间歇情况；须记录每次点击时正式补丁识别到的回调、地点/商队身份后判断原因。R180 已增加测试探针身份日志并重跑；正式候选未改。

### 2026-09-25 R178 合并对话夹具误选旧窗口

R178 的前序三轮补给袭击、教堂、SOS、48 补给奖励、模块/自建建筑、12 项 Tale 任务及三轮魔法少女对话运行后，新四类地点对话的第一轮目标点击已进入正式补丁路由，但测试夹具按 `Dialog_NodeTreeWithFactionInfo` 类型取唯一窗口时抓到前序测试保留的诊断窗口，触发 `TALE_OTHER_DIALOG_ASSERT decoy identity`。双端未出现失步信号；这属于夹具窗口识别失败，**R178 不计综合 PASS**。夹具现按诊断文本清理旧测试窗口、按目标文本识别新窗口；R179 正在用相同正式候选重新运行综合门禁。

### 2026-09-25 R177 四类真实地点招募对话通过

`G:\RWTale0925\OtherDialogsR177Focused` 在限定 18 项模组、双基地异步局内，依次由非主机从原生世界菜单打开四种地点招募对话：摧毁前哨站及其米莉拉版、战场及其米莉拉版。每类对话均经过原生地点结算产生角色，实际点击招募或拒绝，再单独点击同时打开的无关对话，检查地点移除、角色派系/商队、信件和回调路由。第 0、2 轮接受，第 1、3 轮拒绝；主客四条完成回执逐字相同，非主机各有四次世界菜单派发、目标点击和无关窗口点击。控制器与 `BuildValidation/TaleNivarianResume_20260923/AuditR177/other-dialog-verdict.json` PASS，双端 `desynced=False`，零失步信号。正式 DLL SHA256 `BB4EA3FE…CE9697D` 未改；配置 SHA256 `52E955FD…977BF1`，测试窗口隐藏、五项音量为零、测试进程全退。此为四类功能短测，综合回归另测；保存期间窗口生命周期、可选冬季炮塔和其余 P2 内容仍未据此覆盖。

### 2026-09-25 R176 任务结算与魔法少女对话合并功能通过

`G:\RWTale0925\TaleDialogQuestCombinedR176` 在 R174 的双基地异步合并局末尾增加 12 项 Tale 原生任务定义的生成、地点/派系/标签核验，其中 8 项通过同步测试执行器调用原生商队到达结算并检查任务成功、奖励物品 ID/数量。另在非主机实际交谈菜单中进行三轮魔法少女对话：友善、敌对、友善。每轮先开窗取消并确认没有加入，再重开遍历对话图的只读链接，邀请回调重复触发而最终仅加入一次；主客三条角色 ID、阵营和加入次数回执逐字相同。SOS 医疗并窗路由、三次袭击、两种教堂、48 个补给奖励、模块原生安装/卸载、自建建筑与 31 次搬运、十种建筑菜单放置及穿梭机原生发射仍在同局通过，共 18,900 共享 tick，双端 `desynced=False`。控制器与 `BuildValidation/TaleNivarianResume_20260923/AuditR176/tale-dialog-quest-combined-verdict.json` 独立审计 PASS；正式 DLL SHA256 `BB4EA3FE…CE9697D`，配置 SHA256 `52E955FD…977BF1`，隐藏窗口、五项音量零、测试进程全退。

任务批量结算通过的是原生执行器及双端结果，**没有**逐项走真实世界菜单对话；魔法少女遍历只读链接并实际取消/重开/邀请，仍没有穷尽所有对话选择和保存期间的窗口生命周期。可选冬季炮塔与限定加载表之外的联动也仍待审查；不据此宣称完全兼容。R175 所遇发射地图暂停没有在 R176 重现，本轮发射正常到达，新增的发射后有界恢复分支未被触发，不能据 R176 宣称该夹具分支已验证。

### 2026-09-25 R175 新 Tale 批量阶段未到达，发射地图暂停夹具失效

R175 试图在现有合并局尾部加入 12 项 Tale 原生任务生成/结算和三轮魔法少女对话。三轮袭击、48 奖励、SOS 干扰窗口、模块安装与卸载、自建等前序门禁运行到穿梭机阶段后，地图速度从 Superfast 变为 Paused；主客共享/世界 tick 继续前进，但发射地图 tick 同为 92833、飞行器同停在 27/220。两端零失步信号，新增 Tale 阶段尚未到达。保存 `BuildValidation/TaleNivarianResume_20260923/AuditR175/fixture-stall.json` 后主动结束仅本轮测试进程；R175 不计功能 PASS，也不算 Tale 补丁失败。测试专用 `NativeLaunchProbe` 已增加发射后有界的地图速度恢复，后续需完整重测。正式 DLL 未改。

### 2026-09-25 R174 SOS 对话干扰窗口合并测试通过

`G:\RWTale0925\SosDialogCombinedR174` 在医疗型 SOS 原生世界菜单打开对话前，另建一个不同 `Dialog_NodeTree` 子类的无关窗口。非主机点击 SOS 接受选项后，无关回调保持 0；待两名医疗学生实际加入后再单独点击无关窗口，其回调才增至 1。主客 `TALE_SOS_COMPLETE` 回执逐字一致，包含成员 ID、类型、派系及 `decoy=1`；真实窗口均关闭。其余完整合并门禁同局通过：三轮袭击、两种教堂、48 奖励、三次模块安装/卸载、31 次自建材料原生搬运、十种非主机建筑菜单核心、发射等，共 18,377 共享 tick。控制器及独立 `BuildValidation/TaleNivarianResume_20260923/AuditR174/sos-dialog-combined-verdict.json` PASS，未见失步。正式候选 SHA256 `BB4EA3FE…CE9697D` 未改；18 项配置 SHA256 `52E955FD…977BF1`，窗口隐藏、五项音量为零，测试自有进程全退。此轮只新增医疗型 SOS 的并窗路由证据，不代表全部 Tale 对话分支已覆盖。

### 2026-09-25 R173 派生模块与完整功能合并、冷加入短测通过

`G:\RWTale0925\DerivedCombinedR173Cold` 使用正式候选 SHA256 `BB4EA3FEA0157B939FD9C1C483C8AF73B68D458D76538106BEA31E71ACE9697D`，在限定 18 项模组、双基地异步局内完成 18,269 共享 tick 的完整功能合并。三次 Tale 补给袭击、48 奖励、教堂/SOS、原生模块搬运与三次安装及一次卸载、无线电力/电池塔、十种建筑菜单放置、八种自建目标 31 次原生材料搬运、穿梭机及原生发射均通过双端逐字回执门禁。新增派生模块经原生寻路信号验证燃料型短距传送（75→74）和零燃料不瞬移、机控覆写植入体及 tracker 安装/卸载、反应装甲受伤和近战反伤（敌人生命比例 1→0.9625，燃料 74→73）；再把三个派生模块与燃料、冷却刻留在实际存档中。

同一主机随后接纳一个全新客户端，完成 1,200 共享 tick、跨两个基地六项同步动作。冷客户端载入的 `moduleInstall`、`derived`、原生建筑与世界状态进入冻结基线；解除冻结后的完整末态与主机逐字相同。控制器 `result.json`、`cold-result.json` 与独立审计 `BuildValidation/TaleNivarianResume_20260923/AuditR173/combined-derived-cold-verdict.json` 均 PASS；两端没有失步信号，测试自有进程全退。实际测试探针 SHA256 `A299100B…921AB1`；两端/冷客户端同一模组配置 SHA256 `52E955FD…977BF1`，隐藏窗口启动、五项音量均为零。R173 证明这些功能在本场景中的同步及一次冷加入状态保持；仍未覆盖其余 Tale 对话家族、可选冬季炮塔和限定加载表之外的联动，也不宣称完全兼容。按用户要求未做长时间空跑。

### 2026-09-25 R167–R170 短距传送、近战反伤与留存状态

R167 的聚焦夹具给寻路者写了临时 `curJob` 却没有 `JobDriver`，到达格子时在原版 `PatherArrived` 双端空引用；R169 的完整组合夹具又在已有清扫作业的机械体上直接 `StartJob`，使同刻寻路切换并传送两次。两轮均是双端相同的测试作业生命周期失败，不作补丁失步证据。R168 已先证明燃料型短距传送有燃料时从 `(45,0,43)` 到 `(55,0,43)`、燃料 75→74，燃料置零时不瞬移；主客回执一致，原生模块搬运、安装和卸载也完成。

`G:\RWTale0925\DerivedFocusedR170Reflect` 用初始化完整的原版作业触发 `StartPath`，在双基地异步 11,493 共享刻通过。机控覆写植入体/tracker 安装与卸载、开发传送、燃料型短距传送有燃料/零燃料、反应装甲受伤消耗、一次近战反伤（敌方生命比例 1→0.925，装甲燃料 74→73）、原生搬运/安装/卸载退款，以及末态重新安装的机控覆写/短距传送/反应装甲（燃料 74、传送冷却刻 22999），主客回执逐字一致。测试只含 18 项限定模组、确切正式补丁 SHA256 `BB4EA3FE…CE9697D`，隐藏窗口、五项音量零、测试进程全退，无失步。

反伤的 8 点切割伤在后续完整组合局有被护甲完全挡下的情况；R172 双端同样消耗了装甲燃料，但没有造成实际伤害，因此原生伤后反击按目标代码不会发生。已把夹具伤害设为明确穿甲并记录双方生命值，待完整合并局验证。派生 worker 冷加入仍待验证；此处不能据聚焦 PASS 宣称完整兼容。

### 2026-09-25 R166 反应装甲原生受伤触发通过

`G:\RWTale0925\ReactiveArmorR166ModuleOnly` 在 R165 的原生寻路信号、机控覆写安装/卸载场景中，额外安装反应装甲并让机械体实际执行一次 `TakeDamage(Blunt, 8)`。目标模组 `Comp_MechModule.PostPreApplyDamage` 触发 worker，燃料主客都从 75 降为 74，`MODULE_DERIVED_ARMOR` 回执逐字相同。此后原生材料搬运、15,000/15,000 工时安装、3,840/3,750 工时卸载及返料 2/2 也双端逐字相同。双基地异步 10,119 共享刻，控制器 PASS，两端 `desynced=False`；18 项限定模组、确切正式 DLL SHA256 `BB4EA3FE…CE9697D`、隐藏窗口、五项音量零，测试进程全退，用户进程未操作。此轮验证受伤触发和燃料消耗，尚未覆盖近战攻击者反伤、燃料型短距传送和派生 worker 冷重连。

### 2026-09-25 R165 原生寻路信号触发传送通过

安装版 Nivarian 的 `Patch_Pawn_PathFollower_TeleportModule.Prefix` 在机械体 `StartPath` 时发送 `NiraMech_StartPath`；R165 将 R164 的直接 worker 调用改成同步夹具内原生 `Pawn_PathFollower.StartPath`。`G:\RWTale0925\NaturalTeleportR165ModuleOnly` 双基地异步 PASS：主客均从 `(53,0,42)` 传至 `(45,0,43)`，worker 冷却记录均为地图刻 67，`MODULE_DERIVED_TELEPORT_PATH` 回执逐字相同。机控覆写安装/卸载、原生搬运、15,000 工时模块安装和 3,840/3,750 工时卸载返料 2/2 也逐字相同；11,112 共享刻、`desynced=False`。18 项限定模组、确切正式 DLL SHA256 `BB4EA3FE…CE9697D`、隐藏窗口、五项音量零，隔离进程全退，未操作用户进程。仍需验证燃料型短距传送及其冷重连、反应装甲受伤触发。

### 2026-09-25 R163–R164 派生模块短测通过

在既有控制中心原生安装夹具中新增机控覆写与开发传送模块检查，沿用双基地异步、18 项限定模组和确切正式 DLL SHA256 `BB4EA3FE…CE9697D`。R163 因夹具目标格离机械体只有 3 格却要求超过 7 格，在双端相同断言处中止；这不能证明目标缺陷。R164 修正坐标后，`G:\RWTale0925\DerivedWorkersR164ModuleOnly` 控制器 PASS，主客 `MODULE_DERIVED_OVERRIDE` 两条和 `MODULE_DERIVED_TELEPORT` 一条逐字相同：安装机控覆写后植入体与 mechanitor tracker 均出现，卸载后均消失；开发传送 worker 从 `(53,0,42)` 移至 `(45,0,43)`，冷却刻双端均为 71。随后仍经原生材料搬运、15,000/15,000 工时安装及 3,840/3,750 工时卸载，返料 2/2。10,901 共享 tick，两端 `desynced=False`，无测试进程残留。两端五项音量零且隐藏窗口，用户进程未操作。

此轮传送从同步夹具直接调用目标 worker 的 `TryTeleportOnStartPath`，验证了传送和时间状态的双端执行；尚未验证真实寻路 signal 的入口、燃料型短距传送、伤害触发的反应装甲及派生 worker 状态冷重连。目标继续进行，不宣称完全兼容。

### 2026-09-25 R162 现行 Tale 袭击对话回归与全功能合并通过

历史 R173 候选 SHA256 `EA6A8404…A502D0` 在第一次袭击对话点击时抛出 `Supply raid dialog lacks its event identity`；那一版试图在闭包构造时依赖线程静态事件上下文打标。现行 `TaleSupplyDialog` 改为在 `Outcome_Raid` 实际创建窗口后按回调/商队查找并标记第一阶段闭包，第一阶段同步回放后再标记第二阶段。R143 当时受测旧正式 DLL SHA256 `693B08E8…B84913A` 已在双基地组合局成功执行三次原生袭击；将该 DLL 与当前正式 SHA256 `BB4EA3FE…CE9697D` 分别反编译 `TaleSupplyDialog` 后，输出文件 SHA256 **均为** `A4F3BF7CAE24163CFCE0AD13AF948FE0AF9B402AE5958A846E007EBCA84E80E5`，说明这一类型的当前实现与 R143 受测实现一致。不能从此推断其它类型也逐一不变。

`G:\RWTale0925\FullCombinedR162Menu` 再用当前确切正式候选完成双基地异步合并功能局，18,257 共享 tick。主客各有相同的三次补给袭击完成回执（两阶段对话、重复点击、无关窗口、实际敌人和遭遇地图），48 条补给奖励、两种教堂、SOS、双基地操作与评分、模块队列/安装/卸载、自然电力/电池塔、穿梭机生命周期/原生发射、八种额外自建目标的 31 次原生搬运/工时，以及非主机十种真实建筑菜单核心载荷 15–150。控制器与 `BuildValidation/TaleNivarianResume_20260923/AuditR162/full-combined-menu-verdict.json` 独立审计 PASS，两端无不同步信号；18 项模组清单 SHA256 `52E955FD…977BF1`、五项音量零、窗口隐藏、测试进程全退。正式 DLL SHA256 `BB4EA3FE…CE9697D` 未改。这是功能合并验证，不是长时间浸泡，也不覆盖其他 Tale 对话家族的全部窗口生命周期。
### 2026-09-25 R161 十种自建核心同格竞争短测通过

`G:\RWTale0925\SameCellMenuRaceR161` 在双基地异步局中，对十种生成核心逐一让主机与非主机从真实建筑菜单对**同一格**分别预检可放置并各提交一次原生命令，主机/客户端各 10 次预检和 10 次点击。同步处理后每格恰好一个已生成核心，双方十条 Def/Thing ID/位置/载荷回执逐字一致；本轮十格的最终载荷均来自非主机请求，角度 20–155。1200 共享 tick，控制器及独立 `BuildValidation/TaleNivarianResume_20260923/AuditR161/same-cell-menu-race-verdict.json` PASS，零不同步信号，18 项模组清单同 SHA256 `52E955FD…977BF1`、五项音量零、窗口隐藏、测试进程全退。受测正式 DLL SHA256 `BB4EA3FE…CE9697D` 未改。

夹具先让两端各自完成可放置预检，再提交请求；真实网络排序可能导致另一种获胜顺序，因此此结果只证明本轮的重叠请求被确定性处理，并不枚举所有命令排序。此轮没有对竞争状态做新客户端冷重连；可选冬季炮塔、派生 worker、外部联动及其余 P2 入口仍待处理。
### 2026-09-25 R160 第二玩家基地十核心冷重连通过

`G:\RWTale0925\SecondMapPayloadColdR160` 在 R159 的第二玩家派系 20、地图 1、十个真实建筑菜单核心（载荷角度 15–150）基础上保留主机，另起一位全新客户端冷加入。主机冻结、新客户端加载及 1000 共享 tick 后终态都从地图 1 的 `thingGrid` 重新枚举十个原生核心，逐一检查 Def、Thing ID、格子、派系及载荷，和保存回执完全相同；冷加入后两基地六项操作通过。初局与冷加入控制器、独立 `BuildValidation/TaleNivarianResume_20260923/AuditR160/second-map-payload-cold-verdict.json` 均 PASS，零不同步信号，三实例 18 模组清单 SHA256 `52E955FD…977BF1`，五项音量均 0、隐藏窗口，所有测试自有进程退出。正式 DLL 与本轮候选均为 SHA256 `BB4EA3FE…CE9697D`。同格竞争、可选冬季炮塔内容、派生 worker、外部联动及其余 P2 入口仍未覆盖。
### 2026-09-25 R157–R159 第二玩家基地菜单放置验证

R157 因 `Run.ps1` 沿用预编译探针 DLL，新增地图开关未生效，虽控制器 PASS，但仍在地图 0 放置，不能作为第二地图证据。R158 重编译后确认非主机目标地图 1、派系 20，却被现有夹具强制保持派系 19，原生放置检查失败；这是测试身份不匹配，不能归为补丁缺陷。R159 将发起玩家切至地图 1 所属的派系 20，再以真实建筑菜单连续放置十个生成核心。

`G:\RWTale0925\SecondMapMenuPayloadR159` 及 `BuildValidation/TaleNivarianResume_20260923/AuditR159/second-map-menu-payload-verdict.json` PASS：双基地异步、非主机派系 20 → 地图 1，十个核心各自的原生 Def/ID/格子/派系和载荷角度 15–150 主客逐项相同，1200 共享 tick，无不同步信号，测试进程全部退出。精简 18 模组配置 SHA256 `52E955FD…977BF1`，两端五项音量均 0、隐藏窗口。受测正式补丁 SHA256 仍为 `BB4EA3FE…CE9697D`，目标模组 DLL 未改。此轮不含第二地图冷重连、同格竞争或可选冬季炮塔。
### 2026-09-25 R150–R156 建筑菜单放置载荷修复、冷重连通过并部署

R150 首次菜单夹具因十项研究未解锁而无效；R151 在同步完成研究后确认十个生成核心都出现在真实建筑菜单。R152 只让非主机选菜单并放置十个核心，复现了明确的状态分歧：两端 Def、ID、位置相同，但主机的 `CompSelfBuilding.Payload` 全为 `null`，客户端全有角度 0。安装版 Nivarian 在本地 `DesignatorPlace.Selected` 后缀设置静态 `PayloadPlacementState.Current`，再由放置后缀复制到核心；原生 MP 地图命令不携带这个本地值。

新增 `NivarianPlacementPayload.cs`：界面层捕获菜单设计器、地图、格子和载荷，以 MP 同步方法显式发送；同步重放时临时设置 Nivarian 的当前载荷并调用原生放置及 `Finalize(true)`，然后恢复本地静态值。R153 暴露 Harmony 优先级低于 MP 前缀的问题；修正为 `Priority.First+2` 后，R154 十个非主机菜单放置角度 0 双端一致。R155 进一步验证 15–150 度非零值及新客户端冷重连；R156 包含原生 Finalize 行为，复测同一完整短路径通过。

R156：`G:\RWTale0925\NonzeroPayloadColdR156Final`，双基地异步，十项菜单可见、仅非主机十次真实设计器点击，十个核心本机/对端载荷角度 15–150 逐项相同；一位全新客户端重连后从地图 `thingGrid` 直接重新枚举十个原生核心，其冻结与终态载荷、ID、位置、派系均与主机及保存回执一致，另有 1000 共享 tick 与六项双基地操作。控制器和独立审计 PASS、零不同步信号，18 项加载表 SHA256 `52E955FD…977BF1`，五项音量均为 0、隐藏窗口，所有测试自有进程退出。正式 `1.6/Assemblies/Meow.TaleNivarianCompatibility.dll` 已更新为**确切受测** SHA256 `BB4EA3FEA0157B939FD9C1C483C8AF73B68D458D76538106BEA31E71ACE9697D`；旧版 `693B08E8…B84913A` 保存在 `BuildValidation/TaleNivarianResume_20260923/FormalBeforeR156/`，正式目录只留新 DLL。审计和复核脚本：`BuildValidation/TaleNivarianResume_20260923/AuditR156/`。用户正在运行的游戏进程未被关闭；它须在下次自行重启后才会加载新 DLL。

本次仍未覆盖第二玩家地图上的生成核心放置、同格竞争、可选冬季炮塔内容、派生 worker、外部联动及剩余 P2 入口。非零角度由测试夹具在选择菜单后写入 Nivarian 本地载荷，用于验证同步/存档，不等于真实键盘旋转 UI 的可达性。目标继续进行，不宣称两模组已完全兼容。

### 2026-09-25 R149 冷加入后十个原生核心直接枚举通过

`G:\RWTale0925\DualPlacementNativeColdR149` 在 R148 双玩家各五次原生设计器放置短局上增强冷加入门禁：`SelfBuildPlacementProbe.RejoinSnapshot` 从地图 `thingGrid` 逐格重新枚举十个 `Nivarian_AutoBuilder_*` 核心，要求每格恰好一个已生成对象，且 Def、Thing ID、位置、玩家派系与同步放置回执相同。主客首局十条 ITEM 逐字相同；主机冻结后，一位全新客户端加载的**实际地图对象**与保存回执及主机冻结快照相同。随后双基地六项动作、至少 1000 共享 tick 和终态中的原生核心重检均通过，控制器及独立审计 PASS，零脱同步信号。三实例 18 项精简加载表 SHA256 `52E955FD…977BF1`、五项音量均为 0、隐藏窗口，所有测试自有进程退出。正式兼容 DLL 仍为 SHA256 `693B08E8…B84913A`，两目标 DLL 未改。复核脚本和摘要位于 `BuildValidation/TaleNivarianResume_20260923/AuditR149/`。

本轮补齐 R148 所留“只比较序列化回执”的限制；测试核心仍只放在第一玩家地图的不同格子。第二玩家地图放置、同格竞争、建筑菜单与研究可见性、派生 worker 和其他未审入口仍待验证。静态核对另发现目标模组的放置后缀会读取进程本地 `PayloadPlacementState.Current`；当前安装版 XML 未找到它唯一处理的 `Turret_NivarianWinter` Def，需结合实际加载 Def 再判定可达性，不能据此宣称已发现活跃不同步。

### 2026-09-25 R148 双玩家生成核心放置与一次冷加入短测通过

`G:\RWTale0925\DualPlacementColdR148` 沿用双基地异步回放基线、18 项精简加载表及正式同哈希兼容 DLL。主机和非主机在同一场联机局分别从各自原生 `Designator_Build.DesignateSingleCell` 入口发出五次生成核心放置请求，覆盖十种 `Nivarian_AutoBuilder_*`；每次点击后的本地核心仍未即时生成。同步执行后，两端十条核心的 Def、Thing ID、位置和归属回执逐字相同；短局控制器 PASS，目标 1200 共享 tick，无不同步。

保留主机并断开原客户端后，一位全新客户端从新加入点冷加入。冻结地图/世界状态及生成核心**序列化回执**基线相同；随后双基地六项同步动作和至少 1000 共享 tick 的终态逐字相同，`cold-result.json` PASS。三实例加载表同 SHA256 `52E955FD…977BF1`，五项音量均为 0、窗口隐藏；正式兼容 DLL SHA256 `693B08E8…B84913A` 未变，零脱同步信号，所有测试自有进程退出。审计及复核脚本在 `BuildValidation/TaleNivarianResume_20260923/AuditR148/`。

R146 和 R147 因测试驱动先后阻断主机入口、错误等待客户端专用 Setup 标志而主动停止，均未形成兼容性结论；R148 修复后确认两端各有五条本地点击记录。此轮验证的是**不同格子的交错双人放置**，未覆盖同格冲突；冷加入比对保存的放置回执，尚未在新客户端独立枚举十个原生核心对象。建筑菜单选择、研究解锁可见性、同格竞争、派生 worker、外部扩展及其余 P2 清单仍待审查；目标继续进行，不能宣称完全联机兼容。

### 2026-09-25 R145 十种生成自建核心原生设计器放置短测通过

`G:\RWTale0925\PlacementOnlyR145Core` 在双基地异步精简联机局中，由非主机对十种 `Nivarian_AutoBuilder_*` 生成 Def 调用原生 `Designator_Build.DesignateSingleCell`。每次先断言客户端本地核心未即时生成，再等待 Multiplayer 的地图命令；十种结果均在主客端以相同 Def、位置、归属及 Thing ID 出现。十条 ITEM 和一条 COMPLETE 回执双端逐字相同，随后由同步检查命令清理测试核心；控制器 PASS，1207 共享 tick，两端 `desynced=False`，零脱同步信号/压缩包，测试自有进程已退出。加载表双端同 SHA `52E955FD…977BF1`，五项音量为零且窗口隐藏；正式兼容 DLL SHA `693B08E8…B84913A` 未变。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR145/ten-generated-core-placement-verdict.json`。

R144 将放置结果误判为蓝图：安装版 `Buildings_SelfBuilding.xml` 给核心模板设 `WorkToBuild=0`，原版 `Designator_Build.DesignateSingleCell` 因此直接 `GenSpawn` 核心而非创建蓝图；R144 已在无脱同步情况下主动停止，归类为测试预期错误，R145 按真实对象重测通过。此项验证了设计器单格命令的实际同步执行，但未走建筑菜单控件选择、研究解锁可见性、两玩家同时放置或放置后的新一轮冷加入。R143 已另证十种目标的原生材料搬运、工时和成品替换。目标仍需继续审查其它功能入口，不宣称完全兼容。

### 2026-09-25 R143 八种生成自建目标原生搬运与完整工时合并通过

`G:\RWTale0925\CombinedEightHaulR143ParamFix` 使用与 R141 相同的 18 项精简加载表及正式兼容 DLL，在双基地异步组合局中为八种涅瓦莲生成自建核心分别放置真实材料和专用搬运者。由非主机派发同步测试命令下达目标模组原生 `Nivarian_FillUniversalContainer` Job，31 次搬运均经过原生 JobDriver；随后八种核心各自经 `CompTick` 开工、原生工时、材料消耗与成品替换。CryoForge、CryoPrinter、DeepSpaceDecoy、EnergyTower、NiraControlCenter、PowerNetworkIO、RadioTelescope、RebirthMonument 的搬运 Job 次数依次为 6/4/2/4/5/3/4/3，工时回调次数 50/40/5/20/27/10/27/40。八条成品、31 条下达和 31 条原生搬运回执主客逐字一致，地图耗时 2711 tick。另两种 ArchiveTerminal、ParticleCollector 仍经原生搬运和建造，分别 2325、915 地图 tick 完成。

同局 Tale 三轮袭击、48 条补给分支、双基地非零种植区与畜栏营养、三次评分、自然电力/电池塔、三轮控制中心模块安装及一次卸载返料、穿梭机生命周期、引擎世界飞行倍率 `1.20000482`、原生飞船发射及商队到达全部通过。控制器 PASS，17946 共享 tick，两端 `desynced=False`，无 desync 信号/压缩包，测试自有进程全部退出。两端配置 SHA `52E955FD…977BF1`，五项音量均零、隐藏窗口；目标 DLL 与正式兼容 DLL 哈希未变。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR143/eight-natural-haul-combined-verdict.json`。R142 因新增测试 `AssignNext(Map,int)` 注册时漏报 `int` 参数，在主机启动阶段失效，未进入动作；已修正并由 R143 重测通过。

普通放置路径静态审查：当前安装版 `Nivarian_Race.dll` 从十个目标 XML 的 `DefExtension_NivarianAutoBuilder` 生成可在建筑菜单指定的核心 Def，原生 `Patch_BuildCoreIsBuilding` 在 `GenConstruct.CanPlaceBlueprintAt` 检查重复建筑；本机 Multiplayer 源码对 `Designator_Build.DesignateSingleCell` 有地图命令同步和 Def/旋转/材料/戒律的序列化。此为静态边界证据，**尚未从非主机实际点击十个生成 Def 的放置设计器**，见 `AuditR143/placement-boundary-static-review.json`。普通玩家放置入口、同时玩家请求及其余未审功能仍待验证，不据此宣称两模组完全联机兼容。

### 2026-09-25 R141 八种原生自建工时双端合并功能通过

`G:\RWTale0925\CombinedAllSelfBuildWorkR141LaunchResume` 在同一场双基地异步局并行验证八种此前只直接完工的生成自建目标：CryoForge、CryoPrinter、DeepSpaceDecoy、EnergyTower、NiraControlCenter、PowerNetworkIO、RadioTelescope、RebirthMonument。各核心在材料已进入原生容器后，经目标模组 `CompTick` 自行开工、执行 50/40/5/20/27/10/27/40 次原生 `DoBuildingWork`，消耗材料并替换为正确成品；地图耗时 1566 tick，八条 ITEM 与一条 COMPLETE 回执主客逐字一致。ArchiveTerminal、ParticleCollector 另经真实原生搬运 Job 和完整工时，分别 2725、995 地图 tick 完工。八种新增目标的材料是夹具直接放入原生容器，**各自的搬运 Job 与普通玩家放置仍未验证**。

本轮同时通过 Tale 三轮袭击、48 条补给分支、双基地非零种植区/畜栏营养与三次评分、自然电力和电池塔、三次控制中心模块安装及一次卸载返料、八种旧直接完工回调、穿梭机生命周期和 1.200005 倍世界飞行速度。发射前夹具同步恢复地图速度后，涅瓦莲原生穿梭机从燃料 200→140、冷却液 10→9，最终携驾驶员和飞船生成商队 231；到达回执主客一致。控制器 PASS，18167 共享 tick，两端 `desynced=False`，未发现 desync 包，测试自有进程全部退出。主客端同一 18 项精简加载表 SHA `52E955FD…977BF1`，五项音量均零、窗口隐藏；正式兼容 DLL 未改，SHA `693B08E8…B84913A`。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR141/eight-native-work-combined-verdict.json`。其余派生 worker、外部扩展和未审 P2 入口仍待处理，目标保持进行中；不安排长时间空跑。

### 2026-09-25 R137–R140 八种自建完整工时与发射地图暂停诊断

新增 `CombinedP2Harness/SelfBuildWorkBatchProbe.cs`，在同一双基地异步组合局中并行放置八种此前只直接调用完工方法的涅瓦莲生成核心，按各自原生配方把材料加入原生容器，随后由模组自身 `CompTick` 开工、逐次 `DoBuildingWork`、消耗材料并替换成品。R138–R140 均取得八种主客端逐字相同的成品和工时回执；R140 工时调用次数依次为 CryoForge 50、CryoPrinter 40、DeepSpaceDecoy 5、EnergyTower 20、NiraControlCenter 27、PowerNetworkIO 10、RadioTelescope 27、RebirthMonument 40，地图工时窗口 1577 tick。**这八种材料由测试命令直接放进原生容器，尚未各自验证原生搬运 Job**；另两种 ArchiveTerminal、ParticleCollector 仍经原生搬运和完整工时，在 R140 分别 2695、950 地图 tick 完成。

R137 因核心布点未预留成品尺寸，成品互相覆盖，两端同样断言失败；R138 的测试专用 `StartBuild` 后置观察钩子与既有同步方法交互，使旧开发按钮断言出现客户端即时变更，移除此钩子后 R139 旧断言通过。R139 飞船原生发射后未等到商队，不能记作组合 PASS。R140 新增只读双端进度日志，确认共享 tick 18454→19822、世界 tick 91702→98542 时，发射地图 tick 固定 92833、离图飞行器固定 -32/220，因此到达等待的直接原因是夹具未恢复发射地图时间。R140 在取得诊断后写入退出标记，测试自有双端进程均已退出；无目标脱同步包，用户正在运行的游戏未被操作。审计见 `BuildValidation/TaleNivarianResume_20260923/AuditR140/self-build-work-and-launch-diagnostic.json`。R141 恢复地图速度后的完整组合已通过；正式补丁 DLL 未改，SHA256 仍为 `693B08E8…B84913A`。

### 2026-09-25 R136 双基地非零畜栏与种植区评分合并功能同步验证

`G:\RWTale0925\CombinedBarnMetricsR136` 在两个玩家基地各搭一圈原版围栏、一个畜栏标记和一头所属派系的牦牛，先由原生 `AnimalPenUtility.GetCurrentPenOf(animal, false)` 确认其位于封闭畜栏，再读涅瓦莲原生 `BarnAnimalFood`。两端动物 ID、各基地 16.8000011 营养值及总增量 33.6000023 逐字一致；同局 R135 的双基地成熟马铃薯 `GrowingZoneFood=1.1` 也再次通过。三次评分窗口刷新后 `colonyOperationScore=0.3363932`，主客端评分摘要和地图管理器恢复断言一致。Tale 三轮袭击、48 补给奖励、自然供电、控制中心模块安装/卸载、两种自然自建与八种额外目标原生完工调用仍在同局通过。控制器 PASS，17460 共享 tick，零 desync 包，自有进程全部退出；两端同一 18 项精简加载表 SHA `52E955FD…977BF1`，五项音量为零、窗口隐藏启动，正式补丁 SHA `693B08E8…B84913A` 未变。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR136/two-map-barn-nutrition-verdict.json`。

此项只覆盖各玩家基地一处封闭畜栏与一头牦牛的非零输入，其他动物、畜栏变化和自然生命周期仍未覆盖；正式兼容源码和 DLL 未改。后续继续审查派生 worker、外部扩展和其余 P2 入口，按用户要求只做功能性同步测试，不安排长时间空跑。

### 2026-09-25 R135 双基地非零种植区评分合并功能同步验证

`G:\RWTale0925\CombinedGrowingMetricsR135OwnerZone` 在两个玩家基地各建一个真实种植区、放一株成熟马铃薯，并在各地图所有者派系的地图管理器下登记。涅瓦莲原生 `GrowingZoneFood` 从 0 增至 1.1，主客端输入、增量及三次原生评分窗口刷新回执逐字一致；刷新后地图管理器全部恢复。同局继续完成 Tale 三轮袭击、48 补给奖励、自然供电、控制中心模块安装/卸载、两种原生搬运与逐 tick 自建，以及另外八种生成目标的原生完工调用。控制器 PASS，17483 共享 tick，两端 `desynced=False`，零 desync 包，自有进程全部退出。18 项精简加载表双端 SHA `52E955FD…977BF1`；五项音量均为零、窗口隐藏启动，正式补丁仍为 SHA `693B08E8…B84913A`。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR135/growing-zone-two-map-verdict.json`。

R134 首次夹具在同步世界命令的旁观者地图管理器下登记种植区，导致两端 `GrowingZoneFood=0`、断言失败；无 desync，归类为夹具无效，不作兼容结论。R135 使用 `PushFaction(map, map.ParentFaction, true)` 建立区和作物，`finally` 恢复上下文后通过。审计 `AuditR135/r134-fixture-failure.json`。当前只验证每基地一格成熟马铃薯的非零输入；畜栏动物营养仍缺非零场景，正式兼容源码和 DLL 本轮未改。

### 2026-09-25 R133 十种生成自建目标合并功能同步验证

`G:\RWTale0925\CombinedAutoBuilderFinishR133` 在双基地异步完整组合局继续验证 ArchiveTerminal、ParticleCollector 两种原生材料搬运与逐 tick 建造，分别用 2165、980 地图 tick 完成；另从非主机客户端发出同步测试命令，对其余八种目标逐一运行目标模组的原生 `CompSelfBuilding.FinishBuilding`，核对每种成品 Def、玩家派系和稳定 ID。八条成品回执及两条自然建造回执均主客逐字相同，既有 Tale 三轮袭击、48 补给奖励、自然供电、控制中心模块安装/卸载等同局门禁通过，17465 共享 tick、两端 `desynced=False`。控制器 PASS，零 desync 包，自有进程全部退出；两目标 DLL、正式补丁与 R132 同哈希，双端加载表 SHA `52E955FD…977BF1`、五项音量零、窗口隐藏启动。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR133/ten-target-finish-batch-verdict.json`。

八种追加目标仅验证同步调用原生完工函数的成品生成副作用，**未验证每种建筑的自然材料搬运、完整工时、用户放置路径**；品质/载荷分支在当前精简加载表下仍无可触发 Def。用户要求只做功能性同步，不再安排长时间空跑或长冷加入。正式兼容源码和 DLL 未改，目标仍未完成全部功能入口审查。

### 2026-09-24 R132 两种自建建筑全长冷加入两轮通过、第三轮夹具超时

`G:\RWTale0924\CombinedTwoSelfBuildR132FullCold` 再次在双基地异步完整组合完成 ArchiveTerminal 和 ParticleCollector 原生搬运/建造，两端回执相同，17329 共享 tick 时无脱同步。第一、第二位全新客户端各运行至少 10000 共享 tick，冻结基线、两基地六项动作及保存的两座成品摘要均双端一致。第三轮**没有启动客户端**：主机在第二轮后无法生成比 39453 更新的 joinpoint，`workTicks` 固定为 39453 而共享 timer 继续增长，控制器等待 `host.cold.joinpoint.ready` 240 秒后失败。此项分类为冷加入夹具/主机加入点准备失败；无目标 desync 包，自有进程清理完毕。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR132/two-buildings-full-cold-partial-verdict.json`。根据用户最新要求，不再为这两个已通过功能安排长测试；剩余精力转向更多功能入口的合并同步验证。

### 2026-09-24 R131 两种自建建筑三轮短冷加入诊断通过

`G:\RWTale0924\CombinedTwoSelfBuildR131Diagnostic` 使用同一正式补丁和精简加载表，再次在完整双基地异步组合局完成 ArchiveTerminal、ParticleCollector 两种原生材料搬运与建造；两端成品回执相同，17682 共享 tick、`desynced=False`，`result.json` PASS。随后三位全新客户端依次从更新加入点各运行 1000 共享 tick，每轮冻结基线相同、跨两基地六项动作回执相同、终态保留两座成品摘要并逐字一致；`cold-result.json` PASS，零 desync 包，自有进程全退出。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR131/two-buildings-short-cold-verdict.json`。该短轮表明较早加入点握手可用；R132 对前两轮完成全长复测，第三轮因夹具加入点超时未验证。

### 2026-09-24 R130 两种自建建筑同局验证与第二轮冷加入停滞

`G:\RWTale0924\CombinedTwoSelfBuildR130` 用与 R129 相同的正式兼容 DLL 哈希 `693B08E8…B84913A`，在同一双基地异步完整组合局连续测试 ArchiveTerminal 和 ParticleCollector 两种生成自建建筑。各自走目标模组原生搬运 Job、材料入容器、CompTick 工时与成品替换；ArchiveTerminal 完成用时 2195 地图 tick、成品 ID 42914，ParticleCollector 用时 1005 地图 tick、成品 ID 42926，累计七次原生搬运、五次同步下达剩余材料 Job。两端两条完成回执逐字相同，所有既有组合门禁与双基地动作通过，`result.json` PASS，完成于 17349 共享 tick，未报脱同步。第一位新客户端完成 10000 tick 冷加入，冻结基线及终态一致，并保留两种成品摘要。

第二位客户端在新加入点附近停于双方 `Deep-saving destroyed thing` 日志之后，客户端日志从 14:42:58 到 15:01:48 均无后续游戏记录，尽管进程仍响应并消耗 CPU。为避免长时间占用用户机器，通过测试控制器写入失败标记，清理本轮全部自有进程；`cold-result.json` 为 `FAILED`，**仅第二轮加入未验证，未记录目标 desync，第三轮未执行**。R129 的同一正式补丁已三轮通过，因此目前不能把 R130 停滞归因于新增成品或正式补丁，也不能把 R130 计为三轮冷加入通过。四套隔离模组表同 SHA `52E955FD…977BF1`，五项音量均零，窗口隐藏启动，无 desync 包；审计 `BuildValidation/TaleNivarianResume_20260923/AuditR130/two-buildings-partial-cold-verdict.json`。后续先缩小第二轮快照/握手停滞，再补足三轮冷加入；当前正式兼容 DLL 未改。

静态可达性审查：当前两目标 Def XML 中十个带 `DefExtension_NivarianAutoBuilder` 的成品均未配置建筑 `CompQuality`；目标的 `PlacementPayload.TryDealPlaceInput` 仅接受 `Turret_NivarianWinter`，而两目标 Def XML 未定义它。品质随机和该载荷路径在本次精简加载表下尚无可触发对象，不能记为测试通过；如其他必需依赖在最终 DefDatabase 注入相关 Def 或组件，仍需另做运行时验证。

### 2026-09-24 R129 第二基地自建建筑原生材料、完整工时与三轮冷加入

`G:\RWTale0924\CombinedSelfBuildNaturalR129` 在两基地异步完整组合中，为涅瓦莲生成的 `Nivarian_AutoBuilder_Nivarian_ArchiveTerminal` 放入 Steel 60、ComponentIndustrial 2。目标模组原生 `Nivarian_FillUniversalContainer` JobDriver 搬运三次，其中一次由同步测试命令给测试殖民者下达剩余 39 Steel 的原生 Job；其余搬运由工作分配触发。材料入容器后由原生 `CompTick` 自动开工、逐 tick 完成、消耗材料并替换成 ArchiveTerminal。第二基地经过 2190 地图 tick 后，主客端回执逐字相同，成品 ID 均为 42748。与三轮 Tale 袭击、48 补给奖励、24 模块队列、模块补充、自然无线、电网 IO、电池塔、三轮控制中心原生安装及一次自然卸载退款等同局门禁合并通过，17312 共享 tick，双方无脱同步。

随后三位全新客户端依次从新加入点冷加入，每轮双基地六项同步动作、至少 10000 共享 tick，冻结基线及终态逐字一致；自建完成摘要 `done=True,haul=3,ordered=1` 每轮保留。三轮终态 timer 为 32765、48757、64608；`cold-result.json` 为 PASS，无 desync 包，自有游戏进程全部退出。五套隔离配置的模组表 SHA256 同为 `52E955FD…977BF1`，五项音量为 0，所有游戏窗口隐藏启动。正式兼容 DLL 未改，SHA256 仍为 `693B08E8…B84913A`；测试探针 SHA256 `06D4DF61…E1399A2`。审计见 `BuildValidation/TaleNivarianResume_20260923/AuditR129/natural-self-building-cold-verdict.json`。

R121/R122 因材料自动搬运未完整分配导致测试夹具超时；R124 第三轮误用旧加入点，R125 定向自建局未执行到冷加入动作所需的阶段 5，R126 第二轮主机过早冻结使握手停滞，R128 第二轮旧基线断言误要求已自然消失的测试丝袜对象引用仍存在。这些都不算目标功能通过；修正后 R127 三轮 1000 tick 加入点/握手诊断 PASS，R129 完成上述完整复测。当前证据覆盖无品质、无载荷的 ArchiveTerminal；其它自建目标、品质/载荷、暂停地图与同时完成请求仍待验证。目标保持进行中。

### 2026-09-24 R120 控制中心自然卸载、材料返还与三轮冷加入

`G:\RWTale0924\CombinedModuleUninstallR120` 在第二玩家基地由非主机客户端调用既有同步注册的原生安装、卸载请求。原生搬运 Job、机甲等待及 `CompTick` 依次安装搬运、清扫、高性能三种模块；随后自然卸载高性能模块，原生进度 4560/4500，材料按配方返还 3/3，控制中心回到 `Idle`、容器为空，机甲保留搬运和清扫模块。安装累计四次原生搬运 Job、三次完成回调，四条主客回执逐字相同。48 补给奖励、24 模块队列、三轮模块补充、自然无线、实体电网 IO、电池塔等既有门禁合并通过，16747 共享 tick、双方无 desync。

三位全新客户端依次冷加入，每轮至少 10000 共享 tick、两基地六项配对动作；加入点依次刷新为 21310、37820、39424，每轮主客冻结基线与终态逐字相同。保存后的原生状态始终为三次安装、一次卸载、返还 3/3、`Idle`、空容器、搬运与清扫模块仍在。`cold-result.json` 为 PASS，无 desync 包，自有进程全部退出。五套隔离配置加载表 SHA256 均为 `52E955FD…977BF1`，五项音量均为 0，游戏隐藏启动。正式与候选兼容 DLL SHA256 均为 `693B08E8…B84913A`，本轮只改测试探针和冷加入控制器。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR120/natural-module-uninstall-cold-verdict.json`。

R115 初始组合及前两轮冷加入通过，第三轮错误载入上一轮旧加入点，触发真实 MP desync；R116 因主机重复刷新加入点造成基线不符；R117 第二轮握手停在客户端连接、主机未发送快照；R118 控制器在下一轮请求前等待尚不会生成的文件。这些均按夹具/握手无效归档，不充作目标兼容通过。修正后 R119 三轮 1000 tick 冷加入诊断 PASS，再由 R120 完成上述全量复测。R115/R116 证据见 `AuditR115-116/cold-joinpoint-verdict.json`。其余派生模块行为、自然生成和外部扩展等 P2 入口尚未穷尽，目标保持进行中。

### 2026-09-24 R111 控制中心自然模块安装与冷加入

`G:\RWTale0924\CombinedModuleNaturalR111` 在双基地异步完整组合中，第二玩家基地放置目标模组原生控制中心、机甲、搬运者和三轮真实材料。测试夹具先通过原生 `UninstallModule` 移除机甲自带的搬运与清扫模块，再由非主机客户端逐轮调用既有同步注册的原生 `EnqueueModuleInstallation`。原生搬运 Job 将材料送入控制中心容器，机甲 AI 到中心等待，原生 `CompTick` 依次完成搬运、清扫、高性能三种模块安装。双方三轮回执逐字一致：原生搬运 Job 累计 1、2、4 次，安装回调累计 1、2、3 次，安装进度分别达 15000/15000、15000/15000、18000/18000；容器每轮清空，模块真实存在于机甲。与 48 补给奖励、24 模块队列、三轮模块补充、剩余回调、自然无线、实体电网 IO、三块电池自然安装等门禁合并测试，16300 共享 tick、双方无 desync。

保留主机后，三位全新客户端依次冷加入，各运行 10000 共享 tick、完成两基地六项配对动作。三轮终态主客逐字相同，直接读取的原生控制中心均为 `Idle`、容器为空，机甲保存了三种已安装模块；无 desync 包，自有游戏进程已退出。五套隔离配置的加载表 SHA256 为 `52E955FD…977BF1`、五项音量均为 0，游戏隐藏启动。正式补丁与本轮候选 DLL SHA256 均为 `693B08E8…B84913A`；仅扩展测试探针，正式兼容源码和 DLL 未修改。审计见 `BuildValidation/TaleNivarianResume_20260923/AuditR111/natural-module-cold-verdict.json`。仍需验证自然卸载/返还、派生模块行为、自然生成与外部扩展等 P2 清单，目标保持进行中。

### 2026-09-24 R105 电池塔自然搬运安装与冷加入

`G:\RWTale0923\CombinedBatteryTowerR105Replay` 沿用双基地异步基线、18 项精简加载表和正式同 SHA256 `693B08E8…B84913A` 的补丁。在第二玩家基地放置三块预充至 25%／50%／75% 的缩小原版电池，由非主机客户端通过已同步的目标模组原生 `ToggleBatteryTarget` 逐块选定；原生 WorkGiver 与角色 AI 自然分配搬运和安装 Job，没有强制启动 Job。目标模组原生 `NotifyBatteryInstalled` 回调三次，两端回执逐字相同：每次电池被消耗、目标清空、电量上限增加 600；原生上限从 1000 升至 2800，储能增量分别约为 149.42、299.52、449.49（已计入运行中的自然耗电），每次经历至少 240 地图 tick。完整组合中既有 48 补给奖励、24 轮模块队列、三轮模块补充、剩余回调、自然无线与实体电网 IO 门禁均通过，累计 11011 共享 tick，双方无 desync。

保留主机后，三位全新客户端依次冷加入，各运行 10000 共享 tick，并完成两基地各三项配对动作。三轮终态主客逐字一致，直接从原生 GameComponent 读取的电池塔状态均为三次安装、容量 2800；储能随运行自然下降。`cold-result.json` 为 PASS，无 desync 包。五套隔离配置的加载表 SHA256 均为 `52E955FD…977BF1`，五项音量均为 0，游戏隐藏启动，测试进程均已退出。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR105/battery-tower-cold-verdict.json`。本轮仅扩展测试探针，正式兼容源码和 DLL 未修改。控制中心模块的自然获取／安装、自然发电来源、外部扩展入口及其余 P2 清单仍未穷尽，目标保持进行中。

### 2026-09-24 R96–R97 实体电网 IO 吸收、输出与冷加入

在第二玩家基地用原版 VanometricPowerCell、PowerConduit、Heater 和两台 `Nivarian_PowerNetworkIO` 建立实体电网，非主机客户端反转两台 IO 在原生网络中的注册顺序，然后通过现有 RaceTrio 同步注册调用目标模组原生 `SetTargetPowerOutput`。R97 先将两台 IO 设为 -500，经原生 `CompTick` 从电网吸收，原生网络储能增加 `17.7109356`；移除发电源后将两台设为 +500，向仍工作的 Heater 输出，储能减少 `2.05543518`。两段双端回执逐字相同，实体连接和 Heater 通电断言通过。沿用 18 项精简加载表、已核验双基地存档及正式同 SHA256 `693B08E8…B84913A` 的补丁，原有 48 个补给奖励、模块队列、补充、剩余回调、自然无线等门禁一起通过，累计 10614 共享 tick、双方无 desync。

保留主机后，三位全新客户端依次冷加入，每轮运行 10000 共享 tick 并完成双基地六项配对动作；终态逐字相同，三轮直接读取的原生电量表均为 `0.000534993829`，电网 IO 吸收和输出摘要也保持一致。`cold-result.json` 为 PASS，无 desync 包。五个隔离实例加载表 SHA256 均为 `52E955FD…977BF1`，五项音量均为 0，游戏窗口隐藏启动，测试进程均已退出。证据在 `BuildValidation/TaleNivarianResume_20260923/AuditR97/physical-power-io-cold-verdict.json` 与 `G:\RWTale0923\CombinedPowerIOR97Replay`。R96 的原生行为回执一致，但测试日志误记进程内 `PowerNet.GetHashCode()`，致跨端字符串门禁误报；R97 移除不稳定日志字段后重跑整套门禁。正式兼容源码和 DLL 未修改。仍需覆盖自然发电来源、模块自然获取/安装及其余外部扩展入口，不能据此宣称两模组完全兼容。

后续原生入口已定位：`JobDriver_HaulBatteryToEnergyTower.MakeNewToils` 的安装 Toil 从真实电池读取容量和储能，调用 `GameComp_NivarianGlobalPowerTransmitter.SetMaxEnergy`、`AddPower`，再调用塔组件 `NotifyBatteryInstalled`；当前 R97 仅由实体电网 IO 输入储能，未走该搬运 Job。另一个缺口是控制中心 `CompNiraControlCenter.EnqueueModuleInstallation` 后的 `WaitingForModule → InstallingModule → CompleteModuleInstallation`，需要容器收到真实材料、机甲到达中心并经自然 `CompTick` 完成；既有 24 轮模块队列测试故意阻止自然进度，模块补充测试则直接装入了测试模块。下一组合测试应覆盖这两条真实执行链，同时保留 R97 全部门禁。

### 2026-09-24 R95 三种无线适配设备与原生电量表冷加入

`G:\RWTale0923\CombinedNaturalGaugeR95Replay` 沿用双基地基线和正式同哈希 `693B08E8…B84913A` 补丁，将自然无线探针的三轮设备分别换为 AtmosphericHeater、Heater、StandingLamp；第二玩家基地各有两台目标设备，另一玩家基地各有一台其他派系设备。客户端反转这些设备在原生网络集合中的插入顺序，由目标模组自身 `GameComponentTick` 进行供电。三轮主客回执逐字相同，分别观察 220/160/155 次自然 tick；每轮先有目标设备通电，电量从 0.25 降至接近零后出现双设备断电，另一派系设备通电次数均为 0。包含原有三轮 Tale 袭击、两教堂、SOS、48 补给奖励及其他涅瓦莲动作的双基地异步完整组合 PASS，10083 共享 tick，双方 `desynced=False`。

本轮把冷加入基线改为**直接从当前游戏的 `GameComp_NivarianGlobalPowerTransmitter.CurrentEnergy` 读取原生电量表**，不再只比较探针保存的末次读数。保留主机后，三位全新客户端依次加载新加入点，冻结基线及各轮终态中的原生电量表均为 `0.000499911839`，等于双方探针末次读数；每轮执行两基地共六项动作、至少 10000 共享 tick，主客终态逐字一致。`cold-result.json` 为 PASS，无 desync 包；五个隔离实例的加载表哈希都是 `52E955FD…977BF1`，五项音量均为 0，隐藏窗口启动，测试进程已退出。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR95/natural-gauge-cold-verdict.json`。此项补齐 R94 的原生电量表保存验证，并扩大到三种适配设备；**未覆盖自然发电来源、实际电网 IO、模块自然获取安装或全部外部扩展入口**。正式兼容源码和 DLL 未修改。

### 2026-09-24 R94 自然无线供能与三轮冷加入

`G:\RWTale0923\CombinedNaturalR94Replay` 使用同一双基地基线、18 项精简加载表和与正式部署 DLL 同 SHA256 `693B08E8…B84913A` 的候选。完整异步组合局 PASS：三轮 Tale 袭击、两种教堂、SOS、48 补给奖励、机甲模块和 R91 剩余回调等旧门禁仍通过。新增探针在**第二玩家基地**各放两台 AtmosphericHeater 无线适配设备，第一基地放一台另一派系设备；客户端故意改变三个适配器在网络集合中的插入顺序，随后由目标模组自身 `GameComponentTick → TickWirelessAdapters` 连续运行，不手动调用分配方法。三轮双端回执逐字相同：分别观察 235/185/150 次自然 tick，每轮两台目标设备同时通电 5 tick、电量从 0.25 降至约 `7.45E-09`，后续两台均断电，另一派系设备通电次数 0。控制器完整组合 PASS、双方无 desync。

保留主机后，三位全新客户端依次从新加入点冷加入；每轮冻结地图/世界 tick 与 Rand 基线相同，各执行两基地共六项动作并运行至少 10000 共享 tick，主客终态逐字相同，包含供能探针的保存摘要。`cold-result.json` 为 PASS，未生成 desync 包。五个隔离实例加载表哈希均为 `52E955FD…977BF1`、五项音量均为 0，隐藏启动且测试进程已退出。审计 `BuildValidation/TaleNivarianResume_20260923/AuditR94/natural-wireless-cold-verdict.json`。本轮只用一种适配器 Def 和夹具预置电量；冷加入比较了持久化的探针摘要，**没有直接读取重载后的原生电量表**，也未覆盖自然发电来源、模块自然获取安装和全部外部扩展入口。正式兼容源码和 DLL 本轮未修改。

R92/R93 是同一新夹具的电量预算错误，双端均自然耗电但在预设观察窗口内未耗尽，故断言失败；无目标 desync，不计作兼容失败或通过。证据分别在 `AuditR92/natural-threshold-verdict.json`、`AuditR93/natural-energy-verdict.json`。R94 根据实测消耗速率将初始电量降至 0.25 并完成耗尽与断电检查。

### 2026-09-24 R89–R91 第二基地剩余回调组合与冷加入

`G:\RWTale0923\CombinedRemainingR91Replay` 使用已核验的双基地基线存档 SHA256 `C074095E6C272F1B766ED62221B987B4D77A56800E072B26E2B24F1FD8B1595C`、正式同哈希补丁 `693B08E8…B84913A` 和 18 项精简加载表，在异步时间下完成整套既有组合，并从非主机客户端对**第二玩家基地**执行三轮容器弹出、普通机甲无线充电、无线模块开关、丝袜 denier 调整和机甲 glow/rainbow 外观设置。三轮 `REMAINING_COMPLETE` 回执双端逐项相同，伤口肖像生成不推进模拟 Rand；完成于 10007 共享 tick，双方 `desynced=False`。随后三位全新客户端依次从新加入点冷加入，每轮冻结地图/世界 tick/Rand 与上述五类保存状态一致，执行两基地各三项动作并运行至少 10000 共享 tick，终态逐字相同。三轮都保存容器 ID 42157 且为空、机甲 ID 42158、无线开、模块无线关、丝袜 ID 42160/denier 80、glow 0.4/rainbow 开。控制器 `cold-result.json` 为 PASS，无 desync 包；所有隔离实例五项音量 0、窗口隐藏，测试进程已退出。摘要 `BuildValidation/TaleNivarianResume_20260923/AuditR91/combined-cold-verdict.json`。这验证当前正式候选的上述回调和存档恢复；**没有验证自然无线能量传输/耗电、自然模块获取安装或全部外部扩展入口**。

当前源码 `TaleSupplyDialog.cs` 已在 `Outcome_Raid` 前后标记首个袭击对话，并在同步选择首个选项后标记第二个对话。R91 的合并局沿用 `TaleRaidProbe`，三轮均经原生世界菜单进入、两阶段各发送重复选择命令，断言旁置的不同子类对话未被误点、遭遇地图与敌方 Lord 生成。它覆盖了 R173 所报身份缺失的正常现场路径；**仍未覆盖袭击对话打开期间保存/重连、其他外部调用路径或全部窗口生命周期**。第 4 节 P0 记录保留的是 R173 当时的失败，不应视为当前正式候选仍在正常三轮现场重现。

前两次启动均未形成新增功能结论：R89 快速世界生成中原版 `Tribal_ChiefRanged` 连续 120 次失败并返回 null，后续 Tale 袭击在失效世界抛错；没有目标 desync，见 `AuditR89/worldgen-verdict.json`。R90 从同一已核验基线加载，既有 48 奖励和模块补充阈值已通过，但外层夹具每帧强制第一玩家派系，与新增探针请求第二派系冲突，反复切换而未派发新增动作；主动结束自有测试进程，见 `AuditR90/faction-loop-verdict.json`。R91 修正夹具派系协调，**正式兼容源码和 DLL 未修改**。

### 2026-09-24 R88 机甲模块阈值冷加入验证通过

`G:\RWTale0923\CombinedRefuelColdR88` 使用正式同哈希 `693B08E8…B84913A` 和修正后的夹具，双基地异步初始完整组合 PASS（10006 共享 tick）；机甲反应装甲与传送模块三轮原生 gizmo 阈值回执均主客相同。随后保留主机，三位全新客户端依次从新加入点冷加入，每轮冻结地图/世界 tick 与 Rand、模块阈值快照完全相同；各执行两基地六项同步动作，运行至少 10000 共享 tick，最终状态逐字一致。三轮终态均保留同一机甲 ID 37866、两个模块阈值 `0.4`、目标燃料 `30`。控制器 `cold-result.json` 为 PASS，未生成 desync 包，测试自有进程已全部退出；隔离五项音量 0、窗口隐藏。摘要 `BuildValidation/TaleNivarianResume_20260923/AuditR88/refuel-cold-verdict.json`。本轮覆盖了两个模块阈值的真实回调、保存恢复和连续冷加入；P2 其余功能仍按第 4 节清单继续审查。

### 2026-09-24 R86 机甲模块补充阈值合并验证

`G:\RWTale0923\CombinedRefuelR86` 在当前正式同哈希补丁 `693B08E8…B84913A` 上 PASS：双基地异步、三轮袭击、两种教堂、SOS、48 补给奖励、涅瓦莲既有组合动作和原生穿梭机流程仍逐项通过。另从非主机客户端对机甲 `ModuleWorker_ReactiveArmor` 与 `ModuleWorker_Teleport` 的**原生缓存 gizmo 回调**各执行三轮阈值设置；同步命令执行前本地状态不变，主客端 3 条模块回执相同，阈值依次为 0.2/0.7/0.4、目标燃料为 15/52.5/30，原生需要补充的判定一致。完成于 10009 共享 tick，双方 `desynced=False`、无 desync 包，测试自有游戏进程已退出。隔离配置五项音量均为 0、窗口隐藏。摘要 `BuildValidation/TaleNivarianResume_20260923/AuditR86/combined-refuel-verdict.json`。

R87 在相同内容上再次完成 10008 tick 合并局 PASS；第一位全新客户端从加入点加载出的两基地 tick/Rand 及两个模块的 `0.4/30` 快照与冻结主机相同。冷加入比较仍报 `COLD_BASELINE_LOAD_DRIFT`：主机写了完整组合夹具字段，带 `fpcrossonly` 的新客户端只写精简字段。这是测试快照格式不一致，未开始冷轮动作，**R87 冷加入无效**，没有目标补丁 desync。摘要 `AuditR87/baseline-schema-verdict.json`。现让主机在冷冻结阶段也使用精简基线，保留地图/世界 Rand 和模块值，R88 静音隔离重跑中。

### 2026-09-24 R85 实际服装抽选乱序修复已部署

R85 在双基地异步合并局初始流程及第一位全新客户端 3000 共享 tick 均 PASS。第二位全新客户端的**测试专用**前缀在原版 `GenerateWorkingPossibleApparelSetFor` 实际加权抽选前，仅于客户端倒序 81 项服装候选；正式 `NormalizeApparelCandidates` 随后将两端候选顺序校正为逐项相同。同步生成的原版 Beggar（ID 48671）人物属性和 Rand 状态一致，1728 项全局服装表顺序也一致；事件后继续 3000 共享 tick，两轮各六项跨基地动作及最终状态均通过，未生成 desync 包。此前 R80 在同一候选上完成三位全新客户端各 10000 共享 tick、强制原版 Beggar 任务生成及第三次携带任务状态冷加入，R75 完整合并局和第一轮 10000 tick 亦通过。所有运行使用隔离的 18 项目标及依赖加载表、隐藏窗口、五项音量 0，测试自有游戏进程均已退出。证据 `BuildValidation/TaleNivarianResume_20260923/AuditR85/draw-list-repair-verdict.json`、`G:\RWTale0923\CombinedNativeLaunchR85DrawListShuffle\cold-result.json`。

已将**与 R85 受测候选逐字节同哈希**的正式 `1.6/Assemblies/Meow.TaleNivarianCompatibility.dll` 部署为 SHA256 `693B08E808789A2570404CC9F27F4440C62954BF2E808201E57A71904B84913A`；部署前 SHA256 `717CB11D13522FC2190A31B79FB4E7827DED918884B0D5A9E68E72AB3E893AD8` 备份于 `BuildValidation/TaleNivarianResume_20260923/FormalBeforeR85/Meow.TaleNivarianCompatibility.dll`。两目标模组原 DLL 未改。该结果验证了 R74 的实际服装候选乱序故障条件及上述组合/重连范围；第 4 节 P2 剩余功能尚未全部验证，**仍不能宣称两个模组完全联机兼容**。

### 2026-09-24 R74 服装候选顺序诊断与 R75 候选

`G:\RWTale0923\CombinedNativeLaunchR74Apparel` 使用正式 SHA `717CB11D...` 的组合动作首轮及首次 10000 tick 冷加入均 PASS；第二位全新客户端在共享 tick 35640 真实不同步，最后一致 tick 35581。双方首名原版 Beggar（ID 75240）生成时角色属性、随机状态 `3263638239444`、19 个服装候选的集合，以及 1728 项全局服装对的集合相同；**两个列表的排列顺序不同**。加权抽取依赖顺序，解释了 R73 中第 1023 次随机调用开始分叉。证据见 `BuildValidation/TaleNivarianResume_20260923/AuditR74/apparel-order-verdict.json`，原始日志及反同步包保留在 G 盘 R74 运行目录。

R9 补丁曾用 `allApparelPairs` 的 List 引用判断是否需要排序；R74 证实列表可在原对象内重排。现已在 `TaleSupplyDialog` 修改为每次使用全局表前排序，并在 `GenerateWorkingPossibleApparelSetFor` 加权选择前排序实际候选列表。正式 DLL **尚未替换**；新候选 SHA `62EE13E97BE2F72DE039F96AAEF1830AA4A0CB631E32E9FB7B585D3B12E41E4B` 编译 0 警告、0 错误，R75 静音隔离组合与连续冷加入验证进行中。此时不能宣称问题已修复或完全兼容。

R75 后续结果：初始双基地异步组合 PASS（含 48 个奖励分支、原生穿梭机发射），第一位全新客户端 10000 tick 冷加入 PASS。第二位客户端停在世界快照处理阶段，双端日志均未出现 desync；此时系统提交内存约 87/89 GB，用户自行运行的 RimWorld 进程占较大内存，测试自有两进程各约 5.8 GB。为避免影响用户，已只停止本轮测试客户端并由控制器清理测试主机，第二轮记为**环境未验证**，不是补丁通过或实际 desync。证据在 `AuditR75/combined-and-memory-verdict.json`。候选 DLL 未部署。下一轮 R76 采用较轻的既有双基地基线和仅在乱序时才排序全局表的源码，SHA `693B08E808789A2570404CC9F27F4440C62954BF2E808201E57A71904B84913A`，当前定点测试中。

R76 使用 R64 存档做精简跨基地重放，两端 stage 3–5 动作回执一致，但旧存档的无人机缓存暖快照在两端同时报 `expected=42594 actual=42599`，测试控制器提前中止；这是夹具与旧存档状态不匹配，不能验证冷加入。见 `AuditR76/replay-harness-verdict.json`。R77 改用全新精简双基地世界、仍只加载 18 项相关模组和 R76 候选，静音隔离运行中。

R77 的新建世界初始跨基地动作 PASS、共享 tick 1010、`desynced=False`；第一位冷加入时精简夹具仍调用仅完整组合会初始化的 `DroneRepairProbe.Snapshot()`，两端同步报 `DRONE_REPAIR_ASSERT missing graph`，故冷加入结果无效。见 `AuditR77/cross-only-harness-verdict.json`。现已让 CrossOnly 冻结基线只比较两地图及世界的 tick/Rand（完整组合的所有附加快照保持原样），R78 用同一 R76 兼容候选重跑中。

R78 新建精简世界的初始跨基地动作 PASS，共享 tick 1011；第 1 位全新客户端连续运行 14000 tick PASS，stage 6–11 双端动作及最终地图/世界状态一致，tick 21923 无 desync。此世界地图 1 按约 1:1 推进，短版三轮仍无法到达原始故障的 mapTick 193000；第二位客户端刚启动时已停止本轮自有测试进程，第二轮**未验证**。见 `AuditR78/cold1-verdict.json`。R79 改为在第二次冷加入的同步命令中执行原版 `GiveQuest_Beggars` 事件，要求双端真实生成 Beggar、记录同一服装候选序列，并继续 10000 tick 冷加入；测试探针改动不进入正式补丁。

R79 初始精简双基地与第一位 10000 tick 冷加入均 PASS；第二位完成 stage 12–17 动作后，强制事件夹具调用 `IncidentWorker.TryExecute`，因第二玩家基地按原版 `FreeColonistsSpawnedCount` 被视为 0，直接返回 true 而没有运行事件生成体，双端同步报 `forced Beggar generated no apparel candidates`。这轮事件测试无效，不是补丁不同步。见 `AuditR79/forced-quest-harness-verdict.json`。R80 的测试探针改为对同一原版 `IncidentWorker_GiveQuest.TryExecuteWorker` 执行同步调用，兼容候选仍是 R76 SHA `693B08E8...`；静音隔离重跑中。

R80 **PASS**：新建双基地异步初始跨基地动作通过；3 位全新客户端依次冷加入，每轮 10000 共享 tick、两基地各三项动作和最终状态一致。第二轮在同步命令中执行原版 Beggar 任务生成，双方在同一 tick 生成 ID 23702 的 Beggar，人物属性与 Rand 状态一致，88 个实际服装候选及 1728 项全局服装表逐行相同；随后继续 10000 tick 无 desync。第三轮携带任务状态再次冷加入并运行 10000 tick 也通过。`cold-result.json` 为 PASS，证据摘要 `AuditR80/forced-beggar-cold-verdict.json`。本轮证明定点原版任务路径和重连稳定；尚需故意扰乱单边列表顺序，以直接检验 R74 的乱序条件。正式 DLL 仍未替换。

R81 两轮短冷加入与 Beggar 任务均 PASS，但客户端倒序钩子作用于事件生成前的 5687 项缓存，实际生成 Beggar 时该缓存已被重建为 1728 项，故不能当作 R74 的乱序压力证明。见 `AuditR81/precache-perturbation-verdict.json`。R82 改在 `GenerateStartingApparelFor` 入口、正式排序前倒序客户端实际 1728 项缓存，并由控制器核对扰动日志、双端完整候选顺序和事件回执；运行中。

R82 初始和首次短冷加入 PASS；第二位客户端加载到地图 1 tick 15865，但主机仍冻结在 tick 9248，`COLD_BASELINE_LOAD_DRIFT` 阻止后续同步命令。尚未执行倒序或 Beggar 生成，属于冷加入点协调失败，不能判定兼容性。见 `AuditR82/joinpoint-drift-verdict.json`。R83 用相同兼容候选和已修正的生成入口扰动夹具重跑，将短轮提高到 3000 tick 以稳定加入点。

R83 初始和首次 3000 tick 冷加入 PASS，第二位客户端同步生成 Beggar 且双端状态一致；但 `GenerateStartingApparelFor` 入口仍看到 5687 项服装表，在最终加权抽取前被其他代码重建为 1728 项，故该倒序仍未作用于实际使用的候选列表，控制器按设计拒绝验收。见 `AuditR83/generation-cache-rebuild-verdict.json`。R84 将单边倒序直接施加在 `GenerateWorkingPossibleApparelSetFor` 收到的 `apparelCandidates` 上，位于正式补丁 `NormalizeApparelCandidates` 之前，并核对扰动项数等于真正抽取项数；运行中。

R84 初始和第一轮 3000 tick 冷加入 PASS。第二轮运行栈确认测试倒序前缀位于正式 `NormalizeApparelCandidates` 前，但首名 Beggar 的实际候选列表为空，测试探针索引第 1 项时抛 `ArgumentOutOfRangeException`，事件生成被夹具打断；不是兼容补丁异常。见 `AuditR84/empty-draw-list-verdict.json`。R85 改为跳过空列表，在第一份至少两项的实际候选列表上倒序并逐项核对；运行中。

本轮已恢复补丁与短测工作。当前正式 `Meow.TaleNivarianCompatibility.dll` 仍是 1.1.1.0、SHA256 `11CF5059EA423B1A205F7ED65B305C11CA5F5D8894CA4D46BF176C71EC04E016`；未被覆盖。两目标主 DLL 与本文件第 2 节记录的历史哈希一致。

R173 的失败原因已缩小：首次袭击奖励选项并没有得到构造期 `ConditionalWeakTable` 标记，首次点击就因缺事件 ID 抛异常。当前源码改为在 `Outcome_Raid` 生成结果窗口后，根据本次新窗口给首阶段闭包绑定事件 ID；同步执行首阶段选项后，根据该动作新建的窗口给第二阶段闭包绑定同一 ID。匹配同时使用商队 ID、回调方法和窗口新旧集合，保留缺失/歧义时失败，不回退到 MP 的首窗口索引。工程引用改用实际 H 盘 Mods 目录，按 H 盘游戏 1.6.4850 编译为 0 警告、0 错误。

冻结候选：`BuildValidation/TaleNivarianResume_20260923/Candidate/1.6/Assemblies/Meow.TaleNivarianCompatibility.dll`，SHA256 `D3961DD985D87C092F642D5239C6EB9C3539C8E53A5FF221053C9AFB78639BD3`。隔离双端运行 `C:\RWTale0923\CombinedR2` 在 18 项精简加载表下 PASS：客户端真实世界菜单触发三轮补给袭击，每轮两阶段选项、重复点击、无关对话不误路由；双端核对旅行者、遭遇地图、敌人及 Lord。相同三轮同步命令还依次验证涅瓦莲援助冷却、无人机轨道状态和 Tale 特殊角色加入，`host.suite` 与 `client.suite` 三行完全一致。双方共享 tick 1209，最终 `desynced=False`，候选与测试部署 SHA 一致。前一版候选 `300DB263...` 的 `SupplyRaidR1` 也通过三轮袭击路由，但已被当前更精确的窗口绑定版本取代。

双基地异步轮次 `C:\RWTale0923\CombinedAsyncR3` 在 Prepatcher 阶段停滞，未到 `Server started.`；核验该轮专属 PID 和命令行后结束进程，控制器标记 `Early peer exit`。这是无效启动，不能计为异步兼容失败或通过。当前候选尚未做冷重连、其他 Tale 对话的同目标路由、剩余功能逐项动作及完整验收；**不得宣称两个模组已完全联机兼容或把候选发布为正式补丁**。

### 后续候选与组合验证

`CandidateR4` 将精确路由扩展到 Tale 原生结果对话的 124 个闭包回调；启动日志双端均解析 124 条。候选 DLL SHA256 `59A41FAFBF05E7A9B583C392EC3B9C68199E1EB8A09368A8DF0C9817E2B50173`，正式目录仍未替换。`C:\RWTale0923\GenericR4` 双端三轮补给袭击、两种教堂对话及三项附加动作通过，共享 tick 2510。

`C:\RWTale0923\FullDialogsR5` 在同一精简 18 项加载表和同一候选上，将三轮袭击、两种教堂、SOS 医疗加入、48 个补给奖励分支及涅瓦莲援助冷却/无人机轨道/Tale 加入合并为一次双端运行；双方每项记录匹配，最终共享 tick 10011、`desynced=False`。`C:\RWTale0923\FullDialogsAsyncR6` 同组动作在异步时间开启时通过，共享 tick 10009；但该轮从单基地历史存档加载，`-Maps 2` 没有创建第二基地，不能作为双基地证据。两轮测试部署的 Tale DLL SHA 均与 CandidateR4 相同。

真正的双基地夹具 `C:\RWTale0923\TwoBaseAsyncR7` 已生成 2 张基地地图、4 名角色；客户端冻结基线包含地图 0/1。该轮在派发目标动作前，因测试驱动尚未等待客户端从 Spectator 切换到玩家派系而报 `Sequence contains no matching element`。已修正夹具，在双端玩家派系就绪后才记录/执行；R7 不计补丁通过或失败。

修正后 `C:\RWTale0923\TwoBaseAsyncR8` 确实以两张基地地图和异步时间运行，双端冻结基线相同，并完成第一轮补给袭击及涅瓦莲援助动作；其后在第二张新遭遇地图（地图 3）发生 **Wrong random state**，最后一致 tick 为 271。`Client/MpDesyncs/Desync-01.zip` 的首个不同 trace 是 tick 321 主机先给地图 3 的 Gazelle 生成一次 Job ID，客户端没有对应条目，随后 Human 进食随机序列错一项。此轮是实际不同步，不能计通过；目前未证实由 Tale 回调、涅瓦莲状态还是异步新图 tick 顺序造成。下一步应先以相同候选做双基地异步空跑，再逐段加回袭击链定位。

`C:\RWTale0923\TwoBasePassiveR9` 使用相同 CandidateR4、两张基地地图、异步时间、相同 18 项加载表进行无交互基础轮次；双方冻结基线相同，跑完共享 tick 1011，`desynced=False`。它排除了“仅双基地持续 tick 即必然不同步”，但不覆盖新遭遇地图或具体动作。后续优先复现第二次袭击建立地图 3 的随机状态差异。

`C:\RWTale0923\TwoBaseFirstR10` 从 R8 的双基地存档重播，只做第一轮原生袭击、精确窗口路由和涅瓦莲援助，之后让遭遇地图继续模拟；双方记录一致，共享 tick 1008，`desynced=False`。单次新图及其持续运行也不是必现故障。正在以同一双基地存档做前两次袭击的隔离轮次。

`C:\RWTale0923\TwoBaseTwoRaidsR11` 同基线隔离前两次袭击：第一轮双方相同，第二轮均生成地图 3，未见 desync；但测试驱动等待的第二名旅行者未在 180 秒动作期限内 `Spawned`，最终 `TALE_RAID_ASSERT action deadline`。因此不能计为第二轮通过；需要记录旅行者的 Map/Spawned、商队成员和地图角色数，区分原生遭遇转移失败与夹具观察条件。该轮候选 DLL 未变。

`C:\RWTale0923\TwoBaseTwoRaidsDiagR13` 查明该超时的夹具原因：第二轮地图 3 建立时旅行者 `Spawned=True`、商队/事件已消失；遭遇战继续后旅行者变成 `Spawned=False`，测试驱动仍把存活作为后续检查前提，导致超时。已把检查移到入图后 2 tick，并按新遭遇地图验证，而不等待旅行者继续存活。R11/R12/R13 的超时不得当作补丁逻辑失败；R8 的实际随机状态 desync 仍需单独解决。

`C:\RWTale0923\TwoBaseTwoRaidsR14` 以修正后的夹具、相同 CandidateR4 和真实双基地异步存档，通过连续两轮袭击；双方地图 2/3、旅行者、敌人/Lord、窗口路由及涅瓦莲附带动作记录一致，共享 tick 1009，`desynced=False`。目前 R8 的 map 3 Rand desync 为一次实际失败，但不是连续两次袭击的必现结果；需要完整组合复跑及 trace 对照。

`C:\RWTale0923\TwoBaseFullR15` 同候选完整组合推进到三次袭击、两种教堂、SOS 医疗加入及补给奖励 round 0–27；随后 round 28 的夹具显式点击无关对话时，原生 MP 索引路由选中了后来出现的**普通商队相遇窗口**，未执行夹具无关回调，双方同一同步命令断言失败。此普通商队窗口不是 Tale/Nivarian 内容；R15 未见 desync，但未完成组合验收。测试驱动现仅在无关诊断窗口为 MP 首个 NodeTree 时点击它；若普通环境窗口抢先，则双端同步记录跳过该可选诊断，仍验证 Tale 补给窗口闭合、信件和奖励。

`C:\RWTale0923\TwoBaseFullR16` 再次在补给 round 27 遇到普通商队对话抢占 MP 的原生索引路由；即使客户端本地看到诊断窗口为首个 NodeTree，主机的窗口顺序仍可能不同，故“客户端首窗”不能作为执行保证。已进一步将**所有多地图轮次**的可选无关窗口显式点击同步跳过；每一轮仍断言 Tale 结果没有提前执行该无关回调，并完整比较补给奖励/信件及目标回调。单基地 R5 已完成 48 次显式无关点击验证。R15/R16 是夹具断言失败，不能当作 Tale/Nivarian 候选通过。

`C:\RWTale0923\TwoBaseFullR17` 使用相同 CandidateR4、真实双基地存档和异步时间，完整通过三轮袭击、两种教堂、SOS 医疗加入、48 个补给奖励分支及三项附带涅瓦莲/Tale 动作；双方各 48 条补给记录一致，共享 tick 10011，`desynced=False`，控制器 PASS。多地图轮次的可选无关窗口**未显式点击**，但每项都验证未被 Tale 动作提前触发；该显式点击已由单基地 R5 覆盖。R8 的一次真实随机状态 desync 未复现，不能仅凭 R17 抹去；还需跨两个玩家基地分别操作、冷重连及后续独立轮次。

`C:\RWTale0923\TwoBaseCrossMapR18` 在 R17 全部流程后增加第二派系基地的涅瓦莲援助冷却、无人机轨道与 Tale 特殊角色加入。前两项在双方一致通过，第三项双端均报 `SUITE_ASSERT special recruitment state`；原生 `JoinIfPossible` 使用 `Faction.OfPlayer`，夹具原先直接调用原生方法，尚需用生产补丁入口 `Tale.Join` 的短轮次验证派系上下文，才能归因与修补。R18 不计跨基地通过。

`C:\RWTale0923\CrossJoinR19` 直接调用生产 `Tale.Join` 复现：同步 map=1 属派系 20，但双方 `Faction.OfPlayer=19`，旧入口前置判断拒绝加入。修复 `Tale.Join` 为目标地图的玩家派系临时切换完整 MP 上下文，执行后恢复地图管理器。新隔离候选 `CandidateR5` Tale DLL SHA256 `911AACC580534EAC2669C2B07AB5EFF6842EB2989FB61899FF20995FD9DB4C24`；正式安装文件未变。`C:\RWTale0923\CrossJoinR20` 在同一双基地异步存档通过第二基地援助冷却、轨道和 Tale 加入，加入角色派系 20，双方记录一致，共享 tick 1010，`desynced=False`。仍需对 R5 重跑完整组合。

`C:\RWTale0923\FullCrossR21` 已在 R5 上完成完整重跑：两张玩家基地、异步时间、三轮 Tale 袭击、两种教堂、SOS 医疗加入、48 个补给奖励分支，以及第二基地涅瓦莲援助冷却/无人机轨道/Tale 加入。双方 48 条奖励和 6 条附带动作记录逐项相同；第二基地加入角色归派系 20。共享 tick 10011，最终 `desynced=False`，控制器 PASS。该结果不覆盖冷重连和全部剩余回调；R8 一次 map 3 Rand desync 风险仍需复现/排除。

### 冷重连诊断

`C:\RWTale0923\ColdCyclesR22` 的短版双基地异步流程通过，但第一个全新客户端遗漏测试参数 `-fpcrossonly`，重放了历史完整流程；这是启动脚本错误，未形成冷重连验收。`ColdCyclesR23` 修复参数后，第 1 次冷重连和两基地各三项操作通过；第 2 次双方游戏状态及 6 条操作实际一致，但控制器按主机日志字节偏移切割，受 Unity 延迟写日志影响而误报。脚本改为按每轮单调 stage 核对。

`C:\RWTale0923\ColdCyclesR24` 前两次冷重连均通过，第三次两基地操作 stage 18–23 双端一致；随后预置涅瓦莲 Hediff 自然到期，`ColdFinish` 的夹具快照因未记录到期而抛错。旧到期诊断未注册，且把旧存档的 Hediff ID 223/224 写死，不能识别本轮 ID 126/127。已修正测试驱动：注册移除钩子，按定义与角色验证自然到期及 caster/cache，并把验证记录存入可序列化的 `FocusProbeState` 供新客户端加载。R24 不计第三次通过，也没有证据指向 Tale/Nivarian 补丁不同步；候选 R5 DLL 未因上述夹具问题修改。

`C:\RWTale0923\ColdCyclesR25` 使用 CandidateR5 和相同两基地异步存档，初始双端短版跨基地流程 PASS；随后保留主机，连续 3 个全新客户端各自冷加入、比对冻结基线、执行两基地各三项同步动作，并运行至少 1000 共享 tick。三轮最终状态和每轮 6 条双端回执完全相同，控制器 `cold-result.json` 为 PASS。第三轮自然移除 `Nivarian_Hediff_CryobladeOn`（mapTick 10001）和 `Nivarian_Hediff_HealingBooster`（mapTick 10801），主客机的定义、ID、角色、年龄、caster 记录一致。此结果是短版重连验证，未达到技能建议的每轮 10000 tick 长版；R8 的一次 map 3 Rand 不同步仍需处理。正式 DLL 仍未替换。

`C:\RWTale0923\ColdCyclesR26` 尝试每轮提高到 10000 tick，但新客户端回放初始 `Finish` 时错误地用自己的 10000 tick 参数校验主机历史 1000 tick 命令，报 `short shared run`；没有进入长版动作。已把初始目标 tick 作为 `Begin(int target)` 同步参数写入可保存状态，`Finish` 使用该历史目标，冷加入后另以新客户端的 10000 tick 参数控制本轮结束。R27 使用修正驱动重跑；这不是候选 DLL 缺陷。

`C:\RWTale0923\ColdCyclesR27` 的 `cold-result.json` 为 PASS：CandidateR5 哈希 `911AACC5…4C24`，两基地异步时间，3 个全新客户端依次冷加入，每轮冻结基线相同、执行两个玩家基地各三项目标动作、至少 10000 共享 tick，主客机最终状态和每轮 6 条回执一致；三轮末共享 timer 分别为 11210、21937、32674，未产生 desync 包。此项满足三轮 10000 tick 冷重连门槛。仍需重复 R8 多遭遇地图随机状态场景并处理其一次真实不同步，且未完成全部剩余回调的逐项核验。

`C:\RWTale0923\FullCrossR28` 再次以 R8 的两基地存档、CandidateR5、异步时间运行完整组合：三轮 Tale 袭击、两种教堂对话、SOS 医疗加入、48 个补给奖励分支以及两基地各三项涅瓦莲/Tale 动作。主客机记录相同，10,000 共享 tick，控制器 PASS、`desynced=False`，未产生 desync 包。这是 R8 后第二次完整异步组合通过，仍不能解释 R8 那次 tick 321 在地图 3 上主机独有的 Gazelle Job ID 申请。`BuildValidation/TaleNivarianResume_20260923/R8Desync` 保存了该包解压内容，首个不同 trace 之前的 45 条一致。

`C:\RWTale0923\FullCrossNormalR29` 只关闭异步时间、其余保持 R28 的两基地存档、18 项加载表和 CandidateR5，完整组合也 PASS：三轮袭击、两种教堂、SOS、48 个补给奖励及两基地六项目标动作的双端回执相同，10,000 共享 tick、`desynced=False`，没有 desync 包。普通时间模式的双基地门槛已覆盖；仍需消除 R8 一次异步随机状态风险。

`C:\RWTale0923\FullCrossAsyncR30` 对 R28 的异步完整组合再次独立运行，仍 PASS：48 个奖励分支、两基地附带动作和 10,000 共享 tick 双端一致，未产生 desync 包。R8 的旧驱动 DLL SHA256 `AA68D124…388F` 已复制到 `BuildValidation/TaleNivarianResume_20260923/R8ReproHarness`，保留旧动作顺序并让脚本部署 CandidateR5，准备直接核查 R8 当时的过程。R31 因旧驱动快照缺 `mod-folders.json` 在游戏启动前退出，目录为无效启动；已补齐同哈希加载表，R32 正在运行。

`C:\RWTale0923\R8SequenceR32` 用旧驱动重载 R8 保存的双基地基线，第二次袭击地图生成后未发生 desync，但旧驱动等待战斗中已离场的旅行者，最终 `TALE_RAID_ASSERT action deadline`；它不能计完整通过。R8 原运行没有重载存档，而是从生成中的新游戏直接开房。按该路径运行的 `C:\RWTale0923\R8FreshSequenceR33` 使用 CandidateR5 **重现真实 desync**：第二次袭击后，客户端报 `Desynced after last valid tick 271: Trace hashes don't match`，与 R8 的最后一致 tick 相同。R33 世界种子是 `eugene`（R8 为 `yoshiko`），因此故障不是只有某一世界种子才出现；但 R33 的客户端在诊断包落盘前因旧控制器立即清理而退出，缺少新 trace。已让复现控制器在检测到 desync 后保留双端 20 秒，再以新种子运行 R34 捕获包。**R33 证明当前 R5 仍有未解决的双基地异步新游戏路径问题，不得发布为完全兼容。**

`C:\RWTale0923\R8FreshTraceR34` 在另一个新世界种子 `dice` 上用旧驱动完成三轮袭击、48 个补给分支和 10,000 共享 tick，控制器 PASS、无 desync；R33 故障与某些生成结果或起始状态有关。为尽量重建 R8 原始条件，已在旧驱动增加仅用于快速开局窗口的种子参数，固定 `GenText.RandomSeedString` 返回 `yoshiko`；重新编译的驱动与旧动作顺序相同，R35 正在以该种子运行。此种子钩子只服务诊断，不属于发布补丁。

`C:\RWTale0923\R8SeedYoshikoR35` 在 CandidateR5 上再次复现 `Wrong random state on map 3`，最后一致 tick 271，诊断包 `Client/MpDesyncs/Desync-01.zip` 已保存并解压到 `BuildValidation/TaleNivarianResume_20260923/R35Desync`。R8 与 R35 的各自 trace 历史前 40 条一致，首个差异均为主机在遭遇地图上给野生动物创建 Job，客户端则先在人的进食逻辑消耗 Rand。两轮地图 3 的单步 TickList 指纹也已出现差异，表明持续 tick 前地图对象/次序已不一致。原模组第二阶段袭击选项通过 `LongEventHandler.QueueLongEvent` 推迟地图生成，使其脱离同步命令边界；本轮据此在 `TaleSupplyDialog` 的**该闭包**中把联机地图生成改为同步命令内执行，保留单机原生 LongEvent。新隔离 `CandidateR6` Tale DLL SHA256 `ED6EFF77407711364EE7CE4AC9416ECF350F47BFBCB8B006F29FE92CE80BD34B`，编译 0 警告/错误；正式 DLL 未变。

`C:\RWTale0923\R8SeedYoshikoR36` 使用 CandidateR6 和旧动作顺序完整 PASS：快速开局种子 `yoshiko`、两玩家基地、异步时间，三轮遭遇地图 2/3/4、两种教堂、SOS、48 个补给奖励、三项涅瓦莲/Tale 动作及 10,000 共享 tick 双端一致，`desynced=False`，没有 desync 包；新地图首轮 TickList 指纹也相同。R35 与 R36 虽同种子，世界存档哈希不同，因此这是一轮强针对性回归，但还不是同初始状态的严格 A/B。R6 仍需双基地普通模式、跨第二派系基地和三轮冷重连重新验证。

`C:\RWTale0923\R8SeedEugeneR37` 在 CandidateR6 上经过第二张遭遇地图和 7000 多共享 tick 未 desync，但旧夹具等待战斗中已离场旅行者而超时；不能计完整通过。修正夹具后 `C:\RWTale0923\R8SeedEugeneR38` 的三轮袭击和地图 2/3/4 均通过，随后 SOS 夹具在基地紧邻地块已被三张地图占据时找不到空地，双方报同一 `Sequence contains no matching element`；也不是目标补丁不同步。SOS 夹具已改为按确定性广度优先顺序向外找空地，并在旧驱动和主夹具重新编译；R39 正在以相同种子完整复跑。上述 R37/R38 不能计通过，但都未见 R33 的 map 3 Rand desync。

`C:\RWTale0923\R8SeedEugeneR39` 使用 CandidateR6 在第三次袭击生成地图 4 时发生真实世界 Rand desync，最后一致 tick 301。包 `Client/MpDesyncs/Desync-01.zip` 解压于 `BuildValidation/TaleNivarianResume_20260923/R39Desync`。双方前 40 条历史 trace 一致；首个差异位于原版 `PawnApparelGenerator.GenerateWorkingPossibleApparelSetFor`，相同 Rand 状态下主机进入下一轮 `Rand.Value`，客户端仍在加权选择服装。由此不能把 CandidateR6 视为完整修复。为定位输入，另建隔离诊断 `CandidateR7`，Tale DLL SHA256 `3FDE336CDA947DD7F10AAB1AC0EDD2A64AD69C8597D93D1D9CAE2D44FC65CE1E`，仅在联机袭击地图生成时记录全局服装表、实际候选序列、角色派系/预算及结果；正式 DLL 未替换。

`C:\RWTale0923\R8SeedEugeneR40` 用 R7 跑过前两次袭击、相同双基地异步配置，第三张遭遇地图没有敌人，旧探针将其判失败。主客端此前未脱同步；全局服装表均为 1728 项但顺序指纹不同，实际候选指纹及服装结果相同。已让探针允许空遭遇地图并明确记录敌人数量；R40 只算夹具失败，不算战斗覆盖。

`C:\RWTale0923\R8SeedEugeneR41` 继续用 R7 复现真实世界 Rand desync，最后一致 tick 181，诊断包保留于 `Client/MpDesyncs/Desync-01.zip`。逐行比对第一次袭击的服装生成日志：前五次调用的实际候选指纹相同；第六次同一 pawn 24426（`Grenadier_Destructive`）、派系 4、预算 624.1203，主机候选 237 项指纹 `90C1FFF4`，客户端同为 237 项但指纹 `143764AE`，随即生成的服装不同。两端全局表指纹一直分别为 `6B72C1F0` 与 `4FC8B1B4`。原版 `GenerateStartingApparelFor` 按该全局表顺序筛选候选，之后按候选顺序加权随机；故优先修复联机服装表顺序。`CandidateR8` 在联机调用原版服装生成前按 `thing.defName`、`stuff.defName` 序数序稳定排序全局表，并在原版 Reset 更换表时失效缓存，单机路径不改。R8 Tale DLL SHA256 `421B62465FB45DE7883829CA647CFDDD978E7DABE6F80647F81CE62AFC748539`，编译 0 警告/错误；`C:\RWTale0923\R8SeedEugeneR42` 正在用相同种子重测。当前**没有**证明 R8 已解决脱同步，也未重做正式验收。

`C:\RWTale0923\R8SeedEugeneR42` 在 CandidateR8 上通过：同一双基地异步新游戏路径，三轮袭击地图 2/3/4 均生成敌人及 Lord，两端全局服装表指纹均为 `E52E0530`、记录的实际候选相同。两种教堂、SOS、48 个补给奖励分支及目标附带动作继续通过；共享 tick 10009，主客端均 `FOCUSPROBE COMPLETE`、`desynced=False`，控制器 `result.json` 为 PASS。此轮仍加载了诊断补丁，因此已从源码去除服装日志钩子，保留稳定排序和袭击地图同步命令修复，生成隔离 `CandidateR9` Tale DLL SHA256 `717CB11D13522FC2190A31B79FB4E7827DED918884B0D5A9E68E72AB3E893AD8`，编译 0 警告/错误。`C:\RWTale0923\R8SeedEugeneR43` 正在用 R9 重跑；R42 不能直接替代 R9 验收。

`C:\RWTale0923\R8SeedEugeneR43` 在去诊断版 CandidateR9 上 PASS：相同双基地异步新游戏路径，三轮袭击均生成敌人及 Lord，双方地图、角色清单相同；教堂、SOS、48 奖励分支及附加动作通过，10,013 共享 tick 后两端 `desynced=False`。控制器确认实际测试 Tale DLL SHA256 `717CB11D…93AD8`。主夹具已重新编译并引用 R9；`C:\RWTale0923\FullCrossR44` 正在用历史双基地存档对两玩家派系分别执行组合流程。R9 的普通时间模式和冷重连仍需重做，正式 DLL 未替换。

`C:\RWTale0923\FullCrossR44` 在 CandidateR9 上 PASS：从 R8 双基地存档加载，异步时间，三轮袭击均生成敌人及 Lord；两种教堂、SOS、48 个补给奖励分支、两个玩家基地各三项涅瓦莲/Tale 动作全部主客一致，第二基地派系为 20。共享 tick 10012，两端 `FOCUSPROBE COMPLETE desynced=False`，控制器确认 Tale DLL 哈希 `717CB11D…93AD8`。普通时间 `C:\RWTale0923\FullCrossNormalR45` 正在同存档/同 DLL 重跑；冷重连仍需重做。

`C:\RWTale0923\FullCrossNormalR45` 同一 CandidateR9、双基地存档及 18 项加载表，关闭异步时间后完整 PASS：三轮袭击、两种教堂、SOS、48 奖励分支和第二基地三项操作主客一致；共享 tick 10011，双方 `desynced=False`。`C:\RWTale0923\ColdCyclesR46` 正在保留主机并准备 R9 的三轮全新客户端冷加入，每轮目标 10000 共享 tick。正式 DLL 仍未替换。

`C:\RWTale0923\ColdCyclesR46` 在同一 CandidateR9 上完成冷重连 PASS：保留双基地异步主机，3 位全新客户端顺次加入，各运行至少 10000 共享 tick，并在两个玩家基地各执行三项目标操作。每轮 6 条主客回执、冻结/结束状态一致；结束计时分别 11110、21825、32525。治疗加速与冰刃 Hediff 的自然到期定义、ID、角色、tick、caster 记录双端一致；没有 desync 包。控制器 `cold-result.json` 确认 R9 Tale DLL SHA256 `717CB11D…93AD8`。单基地普通时间 `C:\RWTale0923\OneBaseNormalR47` 正在同 DLL 跑完整组合。正式 DLL 仍未替换。

回调目录增量复核保存在 `BuildValidation/TaleNivarianResume_20260923/AuditR9/ui-callbacks-delta.json`，对历史 89 条 `UNREVIEWED` 逐项记录旧清单索引、回调、分类和证据级别：23 条直接只读/窗口局部，9 条本地 UI 构造或空回调，4 条已有同步执行器，1 条当前目标 DLL 无创建点，22 条抵达菜单本地选择/构造，12 条抵达内部只读条件，18 条抵达内部结果转发委托。此增量复核不改旧审计快照，也不把静态路径当作 R9 当前运行时逐分支验证；18 条结果转发和其他非 UI 路径仍按历史短测限制审查。

`C:\RWTale0923\OneBaseNormalR47` 用 R9 完成三轮袭击及补给 round 0–24 后，在夹具额外点击无关诊断窗口时触发原生 MP 的首窗索引路由；当时已有普通商队相遇窗口抢先，主客机都在 `MpTradeSession.TryCreate` 抛同一空引用。目标补给命令此前未误触发无关诊断回调，故 R47 是夹具可选动作被环境窗口抢占，不计补丁通过也不等于目标模组不同步。主夹具已改为所有地图数都同步记录跳过可选无关点击，保留 48 分支、重复命令、信件、奖励和未误触发断言；`C:\RWTale0923\OneBaseNormalR47b` 正在重跑。另将历史 R156 的 12 类结果路径夹具迁至本轮 `RemainingOutcomesHarness`，指向 R9 并编译通过，待单基地轮次结束后测试。

`C:\RWTale0923\OneBaseNormalR47b` 在第一轮袭击地图偶发无敌人时被旧夹具 `TALE_RAID_ASSERT no enemies` 阻断，主客端同样报错且无脱同步证据。夹具已允许原生空袭击并记录敌人数，继续断言地图数量、事件清理、路由命令和有敌人时的 Lord。用户要求测试静音，故三个本轮 `Run.ps1` 都在隔离 Host/Client `Prefs.xml` 将 master/game/music/ambient/UI 音量置零，窗口隐藏；日常游戏配置不改。`C:\RWTale0923\OneBaseNormalR47c` 在此配置和 CandidateR9 下 PASS：单基地普通时间，三轮袭击、两种教堂、SOS、48 个补给奖励分支及目标附带动作主客一致，10000 共享 tick，无 desync 包。历史 R72 存档 SHA256 已复核为 `2C4879841A00B9D40220FA2DCC1E1C90A9F5125BB0A30562B31AEEA6B66EC511`；`C:\RWTale0923\RemainingOutcomesR48` 正以同一候选验证 12 类 Tale 结果路径。

`C:\RWTale0923\RemainingOutcomesR48` 实际生成双端各 36 条相同结果回执，1216 共享 tick 后两端 `desynced=False`，但历史控制器仍硬编码 `1.1.0 READY`，把当前 `1.1.1 READY` 错判为失败。修正该测试门槛后，静音独立重跑 `C:\RWTale0923\RemainingOutcomesR48b` 正式 PASS：8 类原生 NoThing 和 4 类哨站 TooLate 路径各三次，库存 ID、奖励、信件和原生调用次数双端一致，1200 共享 tick 目标达成，无 desync 包。

已将 CandidateR9 的 **唯一变化 DLL** `Meow.TaleNivarianCompatibility.dll` 部署到正式 `1.6/Assemblies`，部署后 SHA256 为 `717CB11D13522FC2190A31B79FB4E7827DED918884B0D5A9E68E72AB3E893AD8`。旧正式 DLL 原始 SHA256 `11CF5059EA423B1A205F7ED65B305C11CA5F5D8894CA4D46BF176C71EC04E016` 已备份于 `BuildValidation/TaleNivarianResume_20260923/FormalBeforeR9/Meow.TaleNivarianCompatibility.dll` 并校验。候选包其余 10 个 DLL 与正式文件哈希相同，未替换。R9 通过单基地/双基地、普通/异步时间、三个连续冷客户端 10000 tick 轮次和上述 Tale 路径；未在自然长游戏中穷举所有罕见物件、AI/开发入口，后续发现新的脱同步需保留存档和 desync 包继续定位。

### 继续补齐 P2 组合覆盖

本轮另建 `BuildValidation/TaleNivarianResume_20260923/CombinedP2Harness`，在 R9 上把现有评分、模块队列、穿梭机生命周期、自建建筑探针串入同一双基地异步测试，继续保留三轮 Tale 袭击、两种教堂、SOS、48 奖励和两基地动作。`C:\RWTale0923\CombinedP2R49` 前两轮袭击生成地图 2/3，但旧夹具误以为 `Find.Maps.Count` 必须每轮加一；第一张遭遇地图被原生清理时总数不增加，主客端均报 `duplicate map generation`。这轮未脱同步，但夹具失败不算通过。已将断言改为按开始时地图 ID 集合检查恰好一张新图，并用 R49 原始双基地存档复跑。

`C:\RWTale0923\CombinedP2R49b` 正式 PASS：两基地异步，三轮袭击地图 2/3/4、48 奖励、教堂、SOS、第二基地三项操作，以及涅瓦莲评分窗口三次刷新、模块安装/卸载队列 24 步、穿梭机原生卸载退款与安装材料消耗 9 步、自建建筑开发动作均在主客端回执一致。两端完成时共享 tick 10010、`desynced=False`，无 desync 包；测试模组表 SHA256 同为 `52E955FD88261FA183AB3619178615236DB48973CB1953D8299C49D41F977BF1`。四类新增操作的限制：评分刷新未构造非零种植区/畜栏输入；穿梭机仅覆盖货舱 I 与无人机舱 I，未飞行验证引擎速度倍率；自建建筑使用当前无品质组件的对象，未覆盖品质/载荷、同时请求或重新加入；模块队列覆盖原生建造/采矿模块及取消操作，未覆盖所有派生 worker。用户日常配置未改，所有启动使用隔离 Prefs 五项音量 0 和隐藏窗口。

后续冷加入夹具排障：`CombinedP2ColdR50` 在主机冻结前因测试探针假定冰块仍存在而失败；已允许自然消失。`CombinedP2ColdR50b` 的原有合并局 PASS，但主机 `autoJoinPoint=0`，新客户端从开局快照重放约 9500 tick 的测试命令，测试静态计数/窗口状态未随存档恢复，最终不同步；这不是新鲜快照冷加入的有效验证。`CombinedP2ColdR51` 的原有合并局再次 PASS，启用 `AutoJoinPointFlags.Join` 后服务器发送了新快照，但夹具仅由新客户端请求冻结，主机保持解冻，双方未到状态比对。`CombinedP2ColdR52` 原有合并局再次 PASS（共享 tick 9491）；新加入点创建和上传后主机按新的工作 tick 主动冻结，但主机 SaveAndReload 清除了已销毁工程无人机的测试引用，`DRONE_REPAIR_ASSERT missing graph` 阻断了基线生成。已在测试组件持久化无人机完成时的 ID、血量和机库数量，并允许已销毁支援信标的引用为空；正式 R9 补丁未因此改变。以上冷加入轮次均未取得有效三轮通过结论。

`CombinedFlightColdR53b` 双端执行引擎航线测试准备时，旧夹具假定当前小星球存在大于 2 弧度的航线；双方同报夹具断言，未进入飞行进度检查。`C:\RWTale0923\CombinedFlightColdR53c` 使用当前星球最远有效格点（球面距离 1.573178）重新完成同局组合测试：原有全部动作仍双端一致；两艘涅瓦莲穿梭机由原生 `TravellingTransporters` 世界 tick 推进，普通/引擎 I 进度分别为 `0.0476741`/`0.05720921`，两端记录相同，速度比 `1.200006`。该探针直接构造合法旅行世界物体及其穿梭机载荷，验证实际世界旅行速度应用；未经过发射按钮、燃料消耗或落地路径。R53c 的完成局正在进行新快照冷加入，尚未填写结论。

R53c 首轮冷加入的新快照确已完成，主机按新的工作 tick 冻结；但支援信标在完成局中自然销毁时效果计数实际仍为 0，旧探针误要求 `applied==1` 才允许销毁后引用为空。该轮阻断于 `PENDING_SUPPORT_ASSERT flare missing`，未到双端状态比对。测试组件改为单独持久化 `wasDestroyed`，保留 `applied=0,destroyed=True` 的真实基线；`CombinedFlightColdR54` 正在重跑整套动作与新快照冷加入。正式补丁仍是 R9 原 SHA。

`C:\RWTale0923\CombinedFlightColdR54` 整局 PASS（共享 tick 9456，引擎 I 实际速度比 1.2），新加入点也已成功创建；但首位冷客户端在共享 tick 29101 报地图 1 随机状态不同步，未到客户端基线标记。其加载日志先有 48 次诊断窗口匿名回调 `TaleSupplyProbe.<>c::<Setup>b__24_2` 不允许反序列化；IL 已精确核对为夹具创建的“Unrelated option”测试回调。原夹具在 48 轮补给后保留了这些窗口。已改为完成轮次时关闭全部精确匹配的诊断窗，再跑 `CombinedFlightColdR55`；R54 不算有效冷加入通过，也不把随机状态差异预先归咎于正式补丁。若 R55 再发生不同步，冷加入控制器会保留 20 秒以待 MP 写出首个证据包。

R55 的原有组合局 PASS，但 `WindowStack` 清理数为零；MP 将这类窗口保存在地图 `mapDialogs`，所以冷加入仍读到 47 个诊断回调。首次 R56 因夹具直接引用 `PersistentDialog` 类型而在预加载阶段抛 `ReflectionTypeLoadException`，未开始测试。改用运行时反射访问 `mapDialogs` 后，`C:\RWTale0923\CombinedFlightColdR56b` 原有组合局 PASS，并在双端各清理 47 个精确匹配的诊断窗口；引擎 I 实测速度比 `1.19999528`。R56b 冷客户端无委托反序列化错误、无已报不同步，但在控制器 600 秒内只达到 `COLD_VIEW`，尚未追到主机冻结 tick，因此第一轮超时，不能算冷加入通过。夹具现记录客户端当前/目标 tick 及冻结状态，并将每轮控制器上限设为 1800 秒；`CombinedFlightColdR57` 正在重新运行。正式 R9 DLL 未变，隔离测试仍用隐藏静音启动。

`C:\RWTale0923\CombinedFlightColdR57` 原有组合局 PASS（共享 tick 9521，双端各清理 47 个测试诊断窗，引擎 I 原生旅行进度比 `1.20000243`）；首轮冷客户端进入地图后记录 `timer=15759,frozenAt=15082,serverFrozen=False`。主机在客户端进入 `ClientPlaying` 前便冻结，MP 的 `FreezeManager` 只在状态改变时广播，后来加入的客户端因未收到冻结包而永远无法写出 `COLD_CLIENT_LOADED_FROZEN`。本轮已主动以夹具错误结束并清理进程，未构成正式补丁不同步证据。现要求客户端先写 `client.cold.view`，主机看到该标记和新加入点后才冻结；`CombinedFlightColdR58` 将重跑。R56b 的 600 秒超时也可由同一缺失冻结广播解释，无需单纯延长等待。

`C:\RWTale0923\CombinedFlightColdR58` 原有组合局 PASS（共享 tick 9698、引擎 I 实测比 `1.20000124`）；修正后的首轮冷加入同时写出客户端冻结基线和主机 `COLD_BASELINE_MATCH`，首次真正跨过新快照状态比对。随后的共享 tick 17251 报 trace hash 不同；证据包 `BuildValidation/TaleNivarianResume_20260923/AuditR58/Desync01` 首个 Thing ID 栈显示冷客户端在 `SuiteActions.Run` 内多执行 `SuiteCacheChecks.Prepare`，主机因原进程静态 `pending/Complete` 已保留而跳过。这是测试驱动的进程本地静态状态未随存档恢复，与正式补丁无关。现将缓存临时对象仅限初始阶段创建，冷加入同步动作不再调用该准备函数；`CombinedFlightColdR59` 待复测。证据包不能作为目标模组缺陷归因。

`C:\RWTale0923\CombinedFlightColdR59` 原有组合局 PASS（共享 tick 9634，三轮袭击、两种教堂、SOS、48 补给分支、涅瓦莲扩展队列/生命周期/飞行和两基地操作；原生引擎 I 飞行速度比 `1.19999778`，47 个测试诊断窗已清理）。已保留主机，三轮全新客户端、每轮 10000 共享 tick 的冷加入正在运行；正式补丁仍为 R9 原 SHA。

R59 冷加入第 1 轮已 PASS：新快照冻结基线一致、两基地六项动作回执一致、最终共享 timer 26489 且相距冷加入起点超过 10000 tick，两端无不同步。第 2 轮主客机都在 `CasterStateProbe.Snapshot` 的 `pawns[0].Map.listerThings` 空引用，发生于新加入点创建后、冻结基线写入前；IL 偏移 `0x01e6` 精确对应取 Map 的字段。首轮长跑后原测试施法者已不在地图上，冰块也已自然消失，继续借该施法者的 Map 搜索冰块是不成立的夹具前提。已改为扫描当前所有地图，以继续检查冰块与施法者引用（若冰块仍存在）；R59 第二轮不算目标补丁失败，`CombinedFlightColdR60` 待重跑。正式 R9 DLL 未变。

`C:\RWTale0923\CombinedFlightColdR60` **合并局及三轮全新客户端冷加入 PASS**。合并局双基地异步、三轮 Tale 袭击、两种教堂、SOS、48 补给奖励、第二基地三项动作、涅瓦莲评分/模块队列/穿梭机生命周期/自建动作及引擎 I 原生世界飞行均主客一致；共享 tick 8934，飞行速度比 `1.19999123`，双方各清理 48 个测试诊断窗。随后三位全新客户端依次获得新快照，冻结基线分别匹配，逐轮两基地六项同步动作回执一致，每轮至少 10000 共享 tick，最终 timer 为 25963、43004、59774。控制器 `cold-result.json` 为 PASS，未产生 desync 包，所有隔离游戏进程已退出。实际加载表 18 项且初始主客哈希均为 `52E955FD…977BF1`，五个隔离实例的五项音量均为 0，隐藏窗口启动。正式 Tale 兼容 DLL SHA256 仍为 `717CB11D…93AD8`。审计摘要见 `BuildValidation/TaleNivarianResume_20260923/AuditR60/combined-cold-verdict.json`；这通过了本批新增组合内容的三轮冷重连门槛，但原生发射/燃料/落地、自然材料交付、所有派生 worker、自建品质/载荷、评分其余输入和全部直接回调等 P2 范围尚未完全验证。

下一组在同一组合夹具新增 `NativeLaunchProbe`：构造真实涅瓦莲穿梭机与驾驶员，在联机 UI 中调用已由 MP 注册的原生 `CompLaunchable.TryLaunch`，检查单端立即不改游戏、主客共享命令燃料与冷却剂消耗、离场 skyfaller、最终商队携带驾驶员与原穿梭机，以及重新加入后的商队/燃料保存状态。`C:\RWTale0923\CombinedNativeLaunchR61` 正在运行；该新增探针尚未有运行结论，正式 DLL 未变。

`C:\RWTale0923\CombinedNativeLaunchR61` 的第一条原生发射命令确在主客机执行，回执均为化学燃料 `200→140`、冷却剂 `10→9`、`FlyShipLeaving=True`，客户端 UI 提交前两项燃料数值未变。但测试窗口在关闭生效前第二次绘制并重复提交 `TryLaunch`，双端随后的同步命令都因夹具 `duplicate native launch` 断言失败；未到商队着陆或冷加入。已给测试窗口加单次提交标记，R61 不能记为完整发射通过，`CombinedNativeLaunchR62` 将复测；正式 R9 DLL 不变。

`C:\RWTale0923\CombinedNativeLaunchR62` **原生发射合并局 PASS**：原有双基地异步、三轮袭击、两种教堂、SOS、48 补给、涅瓦莲队列/生命周期及引擎飞行均继续通过；发射前客户端 UI 不单边修改燃料，原生 `CompLaunchable.TryLaunch` 共享命令在主客机都使化学燃料 `200→140`、冷却剂 `10→9` 并生成离场飞船。随后原生到达动作形成同 ID 商队 225，携带驾驶员 66336 与原穿梭机 66335，双方 `NATIVE_LAUNCH_COMPLETE` 回执逐字相同。整局共享 tick 9476、`desynced=False`，引擎 I 在途倍率 `1.20000339`。已保留主机进行包含商队/燃料状态的新三轮冷加入；该阶段尚未得出结论。

R62 冷加入前两轮均 PASS：新快照基线含 `nativeLaunch=caravan=225,pilot=66336,shuttle=66335,fuel=140,coolant=9` 且双方一致，每轮六项跨基地动作与 10000 tick 后终态也一致。第 3 名客户端从第 2 轮以前的旧加入点载入，正确重放了阶段 12–17 的历史命令，但服务器未自动完成新的加入点；主机按夹具新快照门槛持续等待，第三轮未到可比对基线，已主动结束并清理进程。历史 `ColdFinish` 在新客户端重放时还写出过期完成文件，属于夹具副作用。已改为仅在本轮冷客户端准备完毕后写完成文件，并在自动加入点缺席时由主机请求一次新的 MP 加入点，记录 `workTicks`/创建状态；`CombinedNativeLaunchR63` 待复跑。前两轮通过不等于三轮全部通过，正式 DLL 仍未改。

`C:\RWTale0923\CombinedNativeLaunchR63` 合并局 PASS：共享 tick 9593，引擎 I 速度比 `1.19999862`，原生发射后商队 231、驾驶员 38011、原穿梭机 38010，燃料 140、冷却剂 9。冷加入第 1、2 轮各完成六项跨基地操作及至少 10000 共享 tick，快照重新生成、基线和终态主客一致，timer 分别为 24839 和 42746。第 3 名新客户端在地图载入过渡帧、第二次 `Game loaded` 之前，由测试探针 `FocusProbe.Update` 读取尚未完成交叉引用的 `S.pawns[0]` 时空引用；未进入冷加入基线比较，不是已证实的目标模组不同步。`cold-result.json` 为 FAILED，两个游戏进程均已清理，审计摘要为 `BuildValidation/TaleNivarianResume_20260923/AuditR63/native-launch-cold-verdict.json`。已在探针中等待序列化 pawn 可用，编译通过；`CombinedNativeLaunchR64` 静音隐藏窗口复测中。R63 不能记作三轮通过，正式 DLL 未改。

`C:\RWTale0923\CombinedNativeLaunchR64` 初始合并局 PASS：原生引擎 I 飞行倍率 `1.19999766`，原生发射后商队 226、驾驶员 65076、原穿梭机 65075，燃料 140、冷却剂 9；共享 tick 9626、`desynced=False`。冷加入第 1 轮六项跨基地操作及至少 10000 共享 tick 通过，timer 26749。第 2 轮新加入点 31617 和冻结基线均匹配，阶段 12–17 六项回执完成，但于 tick 35164 报真实 `Trace hashes don't match`，最后有效 tick 35101。`ClientCold2/MpDesyncs/Desync-01.zip` 已保留并解包至 `BuildValidation/TaleNivarianResume_20260923/AuditR64/Desync01`；最早记录差别是地图 1 的 Muffalo42555 于 tick 35155 多出一次原生 `GetNextJobID` 路径，tick 35138 双方相同。此时尚不能认定根因或目标补丁责任；整轮 `cold-result.json` 为 FAILED，第 3 轮未执行，测试进程已清理。后续先复现并定位地图动物/job 状态分歧，再修补、重测，不可把 R64 记为三轮通过。

`C:\RWTale0923\CombinedNativeLaunchR65Replay` 使用 R64 原始 `FocusBaseline.rws`（SHA256 `B5A79E74…8E900`）重新运行相同组合局，隔离探针只增加地图 tick/Rand 状态及地图 1 Muffalo 工作结束的只读诊断日志。合并局 9451 共享 tick PASS，第 1 轮冷加入 10000 tick PASS，timer 25037。第 2 轮新加入点和冻结基线匹配、六项跨基地操作执行完毕，主客地图 tick/Rand 在 35000、36000 均逐项一致；于共享 tick 36840 再次真实 desync，最后有效 tick 36781。双方在 tick 36812 的 Milira_Race JobID 轨迹相同；客户端 Muffalo42556 在 tick 36833/36835 多出 `GotoWander`/`Wait_MaintainPosture` 工作结束，宿主没有，随后 tick 36839 双方 Muffalo42549 轨迹相同。原包解压至 `BuildValidation/TaleNivarianResume_20260923/AuditR65/Desync01`，摘要为同目录 `replay-desync-verdict.json`。这复现了冷加入后的原生动物路径/工作差异，尚不能证明根因属于目标模组。

本机 MP 源码 `Multiplayer-master/Source/Client/Patches/PathFinderPatch.cs` 有一段 `#if false` 的跨 tick 路径计算竞态说明：批量路径作业可能在 pawn tick 同时读取实时位置。R65 的同地图时间、额外动物到达/工作结束与该风险相符，但只是根因假设。已建立未部署候选 `C:\RWTale0923\CandidatePathJobsR66\Assemblies`，仅在 MP 下于 `PathFinder.ScheduleBatchedPathJobs` 后调用游戏自身 `ForceCompleteScheduledJobs`，使该批路径工作在 pawn tick 前完成；候选 Tale DLL SHA256 `4AC4410B…6B8D02`。`CombinedNativeLaunchR66PathJobs` 正用同一初始存档静音隐藏窗口运行合并局和三轮冷加入；正式 DLL `717CB11D…93AD8` 未变。需核对竞态是否消失及同步路径完成的 TPS 代价，未通过前不能部署。

R66 初始合并局已 PASS：共享 tick 9554，原生引擎 I 速度倍率 `1.19999826`，原生发射商队 226、驾驶员 70330、穿梭机 70329、燃料 140、冷却剂 9。总耗时 503.26 秒；R65 同存档未修复局为 433.52 秒，这只是端到端一次运行的对照，包含启动/加载，不能直接当作 TPS 损失百分比。三轮冷加入现正运行。

R66 冷加入第 1 轮 PASS，timer 26741；第 2 轮新快照与基线匹配后于共享 tick 35462 再次真实 desync（最后有效 tick 35401），`Wrong random state on map 1`。包在 `C:\RWTale0923\CombinedNativeLaunchR66PathJobs\ClientCold2\MpDesyncs\Desync-01.zip`，解压至 `AuditR66/Desync01`。第一个共同轨迹是 tick 35438 的 Muffalo42549 JobID，之后宿主 Human70969 在 tick 35452 原生漫游寻位耗用随机数，客户端没有对应路径结束/工作转换。**同步完成路径工作候选无效，不得部署**；已撤销生产源码的该候选改动，正式 DLL 从未变动。

下一诊断 `C:\RWTale0923\CombinedNativeLaunchR67PathReset` 使用相同原始存档、正式 R9 补丁与新隔离探针。探针将在冷加入冻结时比较各基地移动 pawn、当前路径对象和待完成路径请求数；基线匹配后向所有客户端发送按 map/thing ID 排序的同步 `Pawn_PathFollower.ResetToCurrentPosition`，再解冻并跑三轮冷加入。此操作仅在测试夹具中，用于验证“加入方重算路径而宿主仍持有旧路径”的假设，不是正式修复。

R67 初始合并局 9380 共享 tick PASS，原生发射形成商队 226、驾驶员 71930、穿梭机 71929，燃料 140、冷却剂 9。三轮冷加入现已启动，正式 DLL 哈希保持 `717CB11D…93AD8`。

R67 冷加入第 1 轮新加入点/基线匹配；同一冻结基线双方路径计数完全一致：地图 0 移动/当前路径/待完成请求 `26/26/0`，地图 1 为 `7/6/1`。测试专用路径重置也在双端执行。但夹具把首轮 `coldTarget=11` 错算成等待第 2 轮重置，导致客户端始终不发六项动作；已主动写失败标记结束并清理两个测试进程，`cold-result.json` 为 FAILED_HARNESS，不能算修复通过或兼容失败。现已把门槛改为 `coldTarget/6`，并追加每个移动 pawn 的当前位置、下一格、剩余移动成本和路径节点只读日志，`CombinedNativeLaunchR68PathReset` 将用同一初始存档重测。

R68 初始合并局 PASS（9519 共享 tick，原生穿梭机商队 226、燃料 140、冷却剂 9）；冷加入控制器在复制隔离 `ClientCold1/Config` 时遇到 C 盘空间不足，客户端游戏尚未启动。控制器清理了本轮专属宿主。该轮冷加入无效，不计 desync；正式 DLL 未改。为避开 C 盘空间压力，R69 将同一基线和静音隐藏窗口配置移至空余较多的 G 盘，18 项加载表与正式 R9 DLL 哈希保持一致。

`G:\RWTale0923\CombinedNativeLaunchR69PathReset` 初始双基地异步合并局 PASS：三轮袭击、48 个补给奖励分支、评分/模块/自建、原生引擎速度与穿梭机发射等双端回执一致；正式 Tale DLL 仍为 `717CB11D…93AD8`。第 1 位新客户端的 10000 多共享 tick 与六项跨基地动作 PASS。第 2 位新客户端在冻结基线后也执行了六项动作；**冷加入前两轮的移动 pawn 完整路径详情分别 30/30、23/23 条逐项与宿主完全一致**。测试专用同步 `ResetToCurrentPosition` 在双方执行，但第 2 轮于共享 tick 37020 仍发生真实 `Wrong random state on map 1`，最后有效 tick 36961。desync 包在 `G:\RWTale0923\CombinedNativeLaunchR69PathReset\ClientCold2\MpDesyncs\Desync-01.zip`，已解压至 `BuildValidation/TaleNivarianResume_20260923/AuditR69/Desync01`。首次不同 trace 为客户端 Human71006 在 tick 37007 经原生 `JobGiver_Wander / RandomWanderDestFor` 消耗地图随机数，宿主对应首条是 tick 37008 的 Muffalo42555 `GetNextJobID`。第三轮未执行；专属进程已清理。路径内容在冻结点并未分叉，路径重置也未修复后续原生漫游/job 差异；不能部署该测试方案。审计摘要为 `AuditR69/path-reset-replay-verdict.json`。仍需找出首个 pawn 工作/到达时间差异及其来源，不能据此归责 Tale 或涅瓦莲补丁，更不能宣称完全兼容。

`G:\RWTale0923\CombinedNativeLaunchR70PawnState` 仍使用同一基线与正式 R9 DLL，只在隔离探针的 `GameComponentTick` 每 10 共享 tick 记录地图 1 的 pawn 位置、当前 job 与寻路进度。初始合并局和第 1 位冷客户端 PASS；第二位在完成六项动作后真实脱同步，报告 tick 35523、最后有效 tick 35461。**角色状态最早差异已前移到 tick 35110**：35100 时双方逐 pawn 一致；随后新 Human 组双端生成，但宿主 ID `70982/70985/70989` 及位置约 `(39,74)/(34,71)`，客户端 ID `70982/70986/70989` 及位置约 `(71,47)/(74,41)/(77,47)`，其余已有 pawn 在该采样仍一致。地图 tick 同为 193037。晚到的首个不同 Rand trace 是宿主 Human70989 在 tick 35499 的原生漫游选点，客户端无对应调用。这表明新组生成/落点差异先于报警，尚不清楚该组来自哪个 incident 或任务。原包在 `G:\RWTale0923\CombinedNativeLaunchR70PawnState\ClientCold2\MpDesyncs\Desync-01.zip`，解压在 `AuditR70/Desync01`，摘要 `AuditR70/pawn-state-desync-verdict.json`。下一步应在新 pawn 的 `SpawnSetup`/生成处记录来源、派系、kind、map/Rand 上下文及调用栈，找到实际生成路径后再考虑生产修补；正式 DLL 未改。

R71 在游戏载入早期主动终止：生成栈时间窗过窄，已核对专属 PID 并清理；它没有兼容性结论。扩宽窗口的 `G:\RWTale0923\CombinedNativeLaunchR71bSpawnTrace` 初始合并局 9568 tick PASS，第 1 轮冷加入 PASS。第 2 轮于 tick 36213 再次真实 desync，最后有效 tick 36151。生成栈确定差异来自**原生乞丐任务**：`IncidentWorker_GiveQuest → QuestUtility.GenerateQuestAndMakeAvailable → QuestPart_PawnsArrive → PawnsArrivalModeWorker_EdgeWalkIn`，不是先前怀疑的动物 AI。本轮地图 1 的 `Nivarian_Villager` 原生旅行者先在 tick 33822/mapTick 182000 双端相同生成；随后乞丐组在 tick 36022/mapTick 193000 双端同刻到达，宿主 Beggar ID `71003/71006/71009/71012`、入口约 `(35,2)`，客户端 ID `71003/71006/71007/71009`、入口约 `(71,32)`。`Pawn.SpawnSetup` 时双方地图 Rand 记录相同 `32514512465248`，不能据此断言生成时所用的活动 Rand 或世界 Rand 相同。原包在 `G:\RWTale0923\CombinedNativeLaunchR71bSpawnTrace\ClientCold2\MpDesyncs\Desync-01.zip`，解压至 `AuditR71b/Desync01`；摘要 `AuditR71b/beggar-arrival-verdict.json`。第三轮未执行，专属进程已清理，正式 DLL 未改。下一轮应在 `IncidentWorker_GiveQuest`、乞丐 pawn 生成及边缘入场前后记录活动/世界/地图随机状态与 `spawnCenter`，再决定是否需要针对冷加入后的 quest 上下文修补。

`G:\RWTale0923\CombinedNativeLaunchR72QuestRand` 初始合并局和第 1 轮冷加入 PASS；第 2 轮于 tick 35911 真实 desync，最后有效 tick 35851。诊断把首因继续前移：tick 35537/mapTick 193000 的 `GiveQuest_Beggars` 入口，双端**活动 Rand `614724498`、世界 Rand `80173110113128`、地图 Rand `32535987301728` 完全相同**。第一名 Beggar（双方均 ID `70835`）的 `PawnGenerator.GeneratePawn` 返回后，活动 Rand 已变为宿主 `6400115995538`、客户端 `6700763706258`，而世界/地图保存态仍相同。边缘入场前宿主/客户端活动 Rand 已进一步分开，故 `spawnCenter` 被选为 `(79,0,16)` / `(0,0,32)`；入口差异是下游结果，不能只补边缘格。原包在 `G:\RWTale0923\CombinedNativeLaunchR72QuestRand\ClientCold2\MpDesyncs\Desync-01.zip`，解压至 `AuditR72/Desync01`，摘要 `AuditR72/quest-rand-verdict.json`。正式 DLL 未改。下一诊断需在第一名 Beggar 生成期间比较每次 Rand 调用与调用点，找出首次多耗/少耗的分支；还须核对同方法上的 AlienRace、Tale、Nivarian 后置补丁，不能凭乞丐是原生角色就排除模组影响。

`G:\RWTale0923\CombinedNativeLaunchR73RandCalls` 初始合并局、第 1 轮冷加入 PASS；第 2 轮 tick 36334 真实 desync，最后有效 tick 36271。仅包围第一名 Beggar 的生成记录 `Rand.Int`、`Rand.Value`、Push/Pop 及调用栈：宿主 1099、客户端 1324 条。前 1022 条调用点、返回值、活动 Rand 状态一致；**第 1023 条开始走不同分支**，同一 `Rand.Value=0.4506548` 时，宿主在 `PawnApparelGenerator.GenerateWorkingPossibleApparelSetFor → GenCollection.TryRandomElementByWeight` 选择服装候选，客户端已回到外层 `GenerateStartingApparelFor`，跳过该次加权选择。之后宿主给服装初始化颜色、客户端继续候选抽取，随机消耗逐步拉开。原包在 `G:\RWTale0923\CombinedNativeLaunchR73RandCalls\ClientCold2\MpDesyncs\Desync-01.zip`，解压在 `AuditR73/Desync01`，摘要 `AuditR73/beggar-rand-call-verdict.json`。现有 R9 `TaleSupplyDialog.NormalizeApparelPairs` 已按 Def 名排序 `allApparelPairs`，但不能仅凭此认定 `tmpApparelCandidates` 或可用服装集合相同；下一轮应在第一名 Beggar 的 `GenerateWorkingPossibleApparelSetFor` 入口比较双端候选清单、相关 pawn 属性和原始全局清单，定位为什么客户端跳过加权选择。正式 DLL 未改。

## 1. 接手先读

兼容工作尚未完成。以下段落记述 2026-09-23 原始交接时的状态：用户曾要求在 R173 后暂停，当时仅整理资料；**该暂停已由本次 `/goal` 指令解除**，上方“后续候选与组合验证”是恢复后的新进度。测试仍采用短期隔离组合轮次，不处理显卡驱动。

必须区分四种证据：历史候选上通过的实际双端动作、仅静态审查的代码、成功复现缺陷的诊断、当前正式安装文件。它们不能互相替代。本文“已验证”均受具体用例、候选哈希和测试限制约束。

**当前文件已发生外部变化。** 2026-09-20 暂停时，本任务未部署候选，历史正式补丁是 1.0.1；2026-09-23 实测正式 `Meow.TaleNivarianCompatibility.dll` 已为 **1.1.1.0**，修改时间 2026-09-22 18:48:39，SHA256 为 `11CF5059EA423B1A205F7ED65B305C11CA5F5D8894CA4D46BF176C71EC04E016`。当前 Bootstrap 也增加了兼容分类开关。该文件不等于 R170 或 R173，不能用本文历史结果直接认证其稳定性。接手后先核对后续发布记录、源码差异和所有依赖 DLL；不要覆盖现有正式文件。

## 2. 路径与版本基线

所有相对路径均以项目根目录为基准：

`H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\MP-meow-online-shop`

- Tale 安装目录：同级 `3477405110`；包名 `Pakerwot.MiliraEventandStortExpandTheTaleofMilira`。
- 涅瓦莲安装目录：同级 `3624805128`；包名 `keeptpa.NivarianRace`。
- 补丁源码：`Source/TaleNivarianCompatibility`。
- 本轮系列证据根：`BuildValidation/TaleNivarianFullCompat_20260914`，下文简称 V。
- 精确回调目录与判定：`V/Audit/ui-callbacks.json`。
- 原始双端运行：`C:\RWTale0914` 下各轮目录；包含 host.log、client.log、assertions、result.json、inputs.json 和进程记录。
- 原模组反编译：`BuildValidation/DesyncEvidence/Desync68-84_20260914/Decompiled`。
- 旧 `V/STATUS.md` 和 `Audit/feature-status.json` 有停留在较早轮次的内容；不能作为最新总状态。

历史原模组权威哈希：Tale `FFC361B1ED0B7EF101A760D4559BC530C533F3826ECE627942D30479820030CB`；涅瓦莲 `1F0100B0E8FFCA1A176D8EB2B297F7F852B5A78300D0FD5FF8783900AA3D39C2`。恢复前需重算当前安装文件，不能假定三天内未更新。

历史冻结 Trio 1.1.0：`2842DF47F3D29711CE3E5061ABC308A8A5500F3AF0ADDC11AF3B68853C24E89E`。本任务测试要求使用冻结的核心/Trio，不重编译它们；当前正式套件可能已被其他工作更新，须先做版本对账。

R170：`V/CandidateR170/1.6/Assemblies/Meow.TaleNivarianCompatibility.dll`，SHA256 `46055C2A3AC9CCBEE932D4EF0B3E609C2D87091F13732781705EAD06B2308AEF`，本次重新核验一致；R172 使用此候选通过补给奖励测试。

R173：同结构 `CandidateR173`，SHA256 `EA6A8404C972C1B9058E3BE698667A32B39B1B160640A64F98ADE40C32A502D0`，本次重新核验一致；**已知失败，不是可发布版本**。每个候选目录内 SourceSnapshot 用于追溯，当前工作源码不等于这些快照。

## 3. 已有兼容与验证结果

### 3.1 Tale of Milira

- 剧情事件和到达流程：已有 `Tale.cs`、`TaleArrival.cs` 阵营/地图上下文处理；历史 R8b/R9 覆盖组合动作及部分重连。R110/R111 补充上下文恢复断言，但单独的 scope 断言不代表每个剧情已实测。
- 任务生成、解决和奖励：R75、R78、R79 覆盖原生奖励/任务执行器；R80 覆盖两种教堂任务的到达、指定对话与成功结束；R82 覆盖两种补给任务及部分奖励。执行器测试与真实菜单测试必须分别阅读。
- 战场、哨站、SOS 袭击：R139/R140 有真实到达和遭遇地图/敌人/Lord 检查；R143/R147 覆盖对应加入分支；R149 覆盖普通与 `_m` SOS、四种人员分支、接受/拒绝共 16 个遭遇场景。多张遭遇地图不等于两个玩家基地、多图异步或重连验证。
- 教堂：R150 覆盖两变体 NormalEnd/NoThing、两选项、各三次；R151 覆盖 SPMilira 与 MiliraJion 分支、46 个回调追踪、奖励与加入结果。R152 是“重复退出可重复发信”的成功诊断，**不是该行为已修复**：原生退出缺少 resolveTree，双端均能再次执行。暂未证明这是不同步，不应为了消除诊断结果任意改原模组玩法。
- 其他空结果/迟到结果：R156 覆盖战场、毁坏哨站、SOS、废墟哨站及 `_m` 的 12 条路径，每条三次，比较库存、信件、归属和事件解决。
- 补给奖励对话定点路由：R169 三次证明 MP 仅同步选项索引，可能执行先出现的其他 NodeTree 子类窗口。R170 新增 `TaleSupplyDialog.cs`，以事件 ID、商队 ID、回调 token 精确查找选项。
- **R172 是补给奖励最新通过记录**：48 用例，含 33 个 SupplyPack 用例（11 分支各三次），以及 SolarCrystal、SunLightFuel、MealSurvivalPack、MedicinePack、NoThing 各三次。每例发送两条 Choose，验证旧重复命令无副作用；保留无关 faction-info 对话，再明确点击它，确认不误路由；两种任务定义交替，比较任务成功、物品 ID/数量和事件信件。8058 共享 tick，双方最终 desynced=False。高阶奖励仅四个铭牌/材料配对，不是全部 16 交叉组合。

以上不能证明其他剧情对话也解决了相同的窗口误路由，亦不能证明 R173 袭击扩展正确。

### 3.2 涅瓦莲（Nivarian）

- 常用 UI、设置和控制：已有容器取出、无线切换、模块切换、袜厚、光色/彩虹等同步；R1 与 R10–R15 等包含相应断言。设置页面部分使用注入原生控件返回值，不是物理鼠标操作，也不代表每个设置的完整下游玩法。
- 档案：历史金币、物品投送与重复领取检查；R114 开发入口，R136 标签菜单，R138 自定义标签及删除。各轮只覆盖声明的入口，不能合并为所有档案生命周期均完成。
- 随机数、视觉与保存状态：已有渲染/可选视觉、植物成熟、护盾、反弹弹体、施法者、寿命、任务等补丁；R16–R21、R37–R44 等分别留有结果。R49/R53/R54/R55/R57 包含圆环、无人机缓存/所有权/搜索载荷/维修和炮塔历史的保存或初始加入检查；人工状态图的保留不等于完整战斗 AI 验证。
- 灵术/状态及支援：R65 组合修复；R68 三系实际施法与部分经验阈值；R70 已有状态加入与能力 ID/缓存恢复；R86 星落相关短测。并非所有阈值、施法中重连、所有持续效果已覆盖。
- 进度、研究、评分与货币：`NivarianProgressOwnership`、`NivarianUplinkProgressSignal`、`NivarianResearchCache`、`NivarianMetricsRefresh`、`NivarianMetricInputs`、`NivarianCurrencyCollectors`。R89/R91/R93/R94 覆盖归属及信号/研究扫描；R96/R101/R102/R105/R106 覆盖评分输入、部分双派系与开发操作；R115 覆盖收集器。非零种植区/畜栏输入、观战者防御排除、完整 Nira 事件流等仍有缺口。
- 生产和模块：R109 配方剪贴板；R119 模块队列；R121 上行面板；R124 一次接近完成的原生研究；R125/R130 穿梭机队列、材料阻塞、安装与卸载返还；R155 建造/采矿模块的 14 个原生 tick 检查；R160 指定生产菜单。部分测试冻结 CompTick 或预置进度，不证明运输材料、完整工时、质量掷骰和自然生产链。
- 开发动作：R107/R108/R112、R134/R135 覆盖若干无人机、建筑、故事及窗口工具；缺失 HeavyExplosive 定义只验证拒绝非法任务，没有验证正向轰炸。R165 原生订婚/投送调试菜单六次执行通过，属于 MP 已有同步路径，不能据此扩展为自然恋爱流程。
- 自建建筑：`NivarianSelfBuilding.cs` 将 godMode+本地选中建造工具影响模拟的行为转为同步完成命令。R162 六次完成短测通过：三次本地即时完成、三次预置临近完成后原生 tick 完成。全部为无 CompQuality 的 ArchiveTerminal；品质、载荷、暂停地图、同时请求、重连和双基地待验证。
- R158 组合 Gizmo 覆盖补充植物成熟与开关动作；未验证完整自然生长或真实能量传输。

## 4. 未完成事项与接手优先级

### P0 历史项：R173 失败已由后续实现和 R162 回归覆盖

1. 先对比当前 1.1.1.0、当前源码、R170 和 R173 快照，确认哪些代码经其他任务合入。不要直接把 R170 覆盖当前正式套件，也不要重建核心/Trio来“恢复基线”。
2. R173 原始运行：`C:\RWTale0914\SupplyRaidRoutingR173_0920`。208.47 秒结束，控制器 exit1；错误 `System.InvalidOperationException: Supply raid dialog lacks its event identity`。历史已确认测试进程退出，候选未由本任务部署。
3. R173 尝试给 Outcome_Raid 的共享闭包增加事件 ID：线程上下文记录事件，构造函数后置钩子写入 ConditionalWeakTable，两个 UI 回调读取。实际点击时缺失标记。**尚未确定缺失的根因**；先追踪原生闭包构造与钩子执行顺序，不能凭猜测把异常吞掉或回退到旧的首窗口索引。
4. 即使修复构造期标记，还需处理其不序列化的问题：加载/加入/重连、MP 之前已创建的窗口，以及后续阶段对话是否能重建稳定标识。
5. 最新失败审计为 `Audit/R173-supply-raid-paused-verdict.json`；`R173-supply-raid-dialog-patch.json` 中早先 BUILT_RUNTIME_PENDING 不代表当前状态。
6. 现行判定见本文件顶部 R162：当前正式实现与 R143 已测试 TaleSupplyDialog 反编译逐字一致，且 R162 在当前正式候选上又通过三次实际袭击对话。R173 保留为历史失败证据，不再作为当前袭击路径阻断；其它对话家族仍属 P1。

### P1：其他 Tale 对话与窗口生命周期

- `tale-dialog-bindings-20260920.json` 记录 125 个 action 赋值位置、124 个直接解析的唯一回调、一个传入委托辅助入口，81 个原生窗口构造均是 base Dialog_NodeTree；它只是静态清单。
- `tale-close-option-callers-20260920.json`：19 个关闭辅助调用，17 个 null，2 个调用捕获 onComplete；已追踪的魔法少女完成链最终到 JoinIfPossible。外部调用者未穷尽。
- 不能只看 action 非空：null 关闭选项、linkLateBind、对话替换、手动关闭也可能与 MP 全局窗口标记交互。需要给真正修改游戏状态的动作建立稳定执行边界。
- 同类型 WindowStack.Add 会替换旧窗口；并发窗口夹具要用真实不同子类并确认两者都在，不能用两个 base 窗口伪造并存。
- 区分“重复操作是原生设计/缺陷但双端一致”和“执行错目标或单边修改造成不同步”。

### P2：剩余功能与跨生命周期覆盖

- 当前回调目录共 708 条，89 条 UNREVIEWED，完整名称见附录。条数不是功能数或完成百分比；其余条目还包含只读、无活动 Def、静态边界、历史有限验证等状态。
- 未审查条目之外仍有直接按钮、自动 tick、存档恢复和事件路径，不能以清空 89 条替代完整兼容验收。
- 无活动 Def 的多方块、某些龙蛋/开发入口只能标记当前加载组合不可达；其他扩展或旧存档使其可达时需要重新审查。
- 按功能补双基地/多派系上下文、异步暂停、冷加入/重连、同时发起/过期命令。历史某轮重连通过不代表后增所有补丁都具备保存兼容。
- 自建建筑品质/载荷，模块派生 worker 与自然材料交付，穿梭机速度模块，无人机派生类型/自主 AI，实际电网条件、评分其余输入、开发权限拒绝等按原证据 limits 补齐。

## 5. 测试与发布约束

恢复需用户明确指令。仅启用两个目标、必要依赖、MP、候选补丁、隔离探针；历史最小组为 18 个 activeMods 条目，含官方 DLC。启动前核对真实配置，不按目录名推测；不同机器和历史版本可能需更新加载清单。

沿用独立 Host/Client savedatafolder 与日志，官方连接进入 ClientPlaying 后才操作；优先非主机真实菜单入口，多项目合并但每项有独立断言。检查对象 ID、归属、库存、信件、地图/任务状态和实际命令次数，不能只看没有掉线窗口。

用户已取消长回归，不要自动恢复 120000 tick 或大型全模组测试。短测窗口按具体用例设置，保存失败原始证据；不要因夹具错误反复重启整组。

R173 夹具：`V/ShortSupplyRaidRoutingHarness`；R172 夹具：`V/ShortSupplyAllRewardsCleanupHarness`。历史 R72 存档：`C:\RWTale0914\SosMedicalAttachedR72_0920\Host\Saves\FocusBaseline.rws`，SHA256 `2C4879841A00B9D40220FA2DCC1E1C90A9F5125BB0A30562B31AEEA6B66EC511`。恢复时先核验存在和哈希。

冻结候选后再启动，运行中不改 DLL 或探针。只清理确认为该测试启动的 PID。退休测试商队须先移出成员、通过 WorldPawns 原生 GC 清理并断言注销，最后销毁空商队；R171 裸 Destroy 导致 world-pawn tick 异常，该轮作废。R163/R164/R168 等夹具失败也不能计入稳定性通过。

正式发布前必须对账候选、源码快照、全部依赖和部署后哈希；本交接没有授权部署，也没有为当前正式 1.1.1.0 作新增实测背书。

## 6. Desync-85～93 的历史范围

原修补记录：`BuildValidation/DesyncEvidence/Desync85-93_20260914/修补记录.md`。历史补丁处理头像/伤口缓存随机、SweepPlane 渲染初始化轨迹以及 DateNotifier 上下文 finalizer 顺序；有 12012 共享 tick、两地图异步的定向通过记录。旧全模组长回归由用户取消，不能计为完整通过。接受涅瓦莲援助相关的单边原因未独立全部闭环；不能宣称九份压缩包逐一完整复现并修复。

## 7. 证据阅读规则

后附索引保留每轮原状态、范围、限制与原文件链接。PASS_DIAGNOSTIC、R152 的 PASS、只读输入诊断、部分阶段通过均不等于生产补丁通过。早轮的“未部署”“仍是1.0.1”和待办可能被后轮取代，只描述当时情况；以具体功能的最新证据和本文件版本对账为准。完整原始回执、SHA、shared ticks 请打开对应 JSON，未在索引重复全部对象数据。

## 附录 A：回调状态计数（原目录快照）

- ADD_QUEUE_STATIC_BOUNDARY_VERIFIED：1 条。
- BUILTIN_DEBUG_SYNC_RUNTIME_VERIFIED：2 条。
- BUILTIN_ORDERED_JOB_STATIC_VERIFIED：1 条。
- BUILTIN_ORDERED_JOB_SYNC_BOUNDARY_VERIFIED：1 条。
- CURRENT_DEF_UI_UNREACHABLE：1 条。
- CURRENT_DEF_UI_UNREACHABLE_SYNC_REGISTERED：1 条。
- CURRENT_LOADOUT_NO_DEF_MAPPING：24 条。
- DEBUG_ONLY_EMPTY_ACTION：1 条。
- DEBUG_ONLY_TREE_MENU_ALREADY_SYNCED_BY_INSTALLED_TRIO：2 条。
- HISTORICAL_SUPPLY_PATH_REVIEWED_LIMITED：10 条。
- LOCAL_APPAREL_TUNER_REVIEWED：1 条。
- LOCAL_DIALOG_BUILTIN_RENAME_RUNTIME_VERIFIED：1 条。
- LOCAL_DIALOG_NAVIGATION_RUNTIME_VERIFIED：40 条。
- LOCAL_DIALOG_OPEN_WITH_SYNCED_COMPLETION_RUNTIME_VERIFIED：1 条。
- LOCAL_DIALOG_OPTIONS_WITH_SYNCED_COMPLETION_RUNTIME_VERIFIED：2 条。
- LOCAL_DISPLAY_SELECTOR_OR_COMPARER：6 条。
- LOCAL_LABEL_DRAW：3 条。
- LOCAL_MAIN_TAB_SELECTION：1 条。
- LOCAL_MENU_WITH_SYNCED_SELECTION：2 条。
- LOCAL_MENU_WITH_SYNCED_SELECTION_RUNTIME_VERIFIED：1 条。
- LOCAL_MODULE_LIST_PRESENTATION_REVIEWED：18 条。
- LOCAL_NODE_BACKGROUND_DRAW：3 条。
- LOCAL_PROGRESS_SUPPORT_PRESENTATION_REVIEWED：15 条。
- LOCAL_RECIPE_FILTER_REVIEWED：11 条。
- LOCAL_RESEARCH_SELECTION：2 条。
- LOCAL_SELECTED_NODE：1 条。
- LOCAL_SETTINGS_NAVIGATION_REVIEWED：3 条。
- LOCAL_TEXT_FORMATTING_STATIC_VERIFIED：25 条。
- LOCAL_TOOLTIP_ONLY：1 条。
- LOCAL_UI_OPEN：1 条。
- LOCAL_UPLINK_SELECTION_REVIEWED：25 条。
- LOCAL_WIKI_NAVIGATION_REVIEWED：13 条。
- LOCAL_WINDOW_OPEN_ONLY：1 条。
- LOCAL_WINDOW_WITH_SYNCED_APPLY：1 条。
- LOCAL_WORK_ARCHIVE_QUERY_REVIEWED：11 条。
- MONUMENT_REFRESH_STATIC_BOUNDARY_VERIFIED：1 条。
- NATIVE_CLIENT_JOB_AND_RITUAL_COMBINED_MP_PASS_R86：1 条。
- NATIVE_MENU_FIELD_WATCH_RUNTIME_VERIFIED：5 条。
- PATCH_RUNTIME_VERIFIED：144 条。
- PATCHED_EXACT_SUPPLY_DIALOG_SHORT_PASS：6 条。
- READ_ONLY_GETTER_CHAIN：27 条。
- READ_ONLY_PREDICATE：32 条。
- READ_ONLY_PROJECTION：79 条。
- SYNC_ACTION_RUNTIME_VERIFIED：3 条。
- SYNC_CLAIM_REPLACEMENT_SOURCE_VERIFIED：2 条。
- SYNC_COMPLETION_FORWARDER_RUNTIME_VERIFIED：2 条。
- SYNC_EXECUTOR_SOURCE_VERIFIED：4 条。
- SYNC_EXECUTOR_VERIFIED_INSTALLED_BINARY：6 条。
- SYNC_EXECUTOR_VERIFIED_INSTALLED_BOOTSTRAP：1 条。
- SYNC_FIELD_WATCH_SOURCE_VERIFIED：7 条。
- SYNC_GIZMO_REPLACEMENT_RUNTIME_VERIFIED：2 条。
- SYNC_GIZMO_REPLACEMENT_SOURCE_VERIFIED：5 条。
- SYNC_MENU_REPLACEMENT_RUNTIME_VERIFIED：2 条。
- SYNC_RECIPE_SELECTION_SOURCE_VERIFIED：2 条。
- SYNC_WRAPPER_VERIFIED_INSTALLED_BINARY：1 条。
- TESTED_NATIVE_ARRIVAL_FORWARDER：42 条。
- TESTED_NATIVE_DEFERRED_RAID：8 条。
- TESTED_NATIVE_OUTPOST_JOIN_CHOICE：4 条。
- UNREVIEWED：89 条。

## 附录 B：89 条未审查回调（逐项交接）


### B.1 Nivarian_Race.dll

入口：System.Void TestIconWindow::DrawProgrammableSpawnList(System.Single&,System.Single,System.Single)

回调：System.Boolean TestIconWindow/<>c::<DrawProgrammableSpawnList>b__23_0(Verse.ThingDef)


### B.2 Nivarian_Race.dll

入口：System.Void Nivarian.Helper.NivarianVisualHelper::DrawConeCell(Verse.IntVec3,System.Single,System.Single,System.Single,UnityEngine.Color)

回调：System.Boolean Nivarian.Helper.NivarianVisualHelper/<>c__DisplayClass30_0::<DrawConeCell>b__0(Verse.IntVec3)


### B.3 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.UI.Gizmo_MultipleTarget::StartTarget()

回调：System.Void Nivarian_Race.Code.UI.Gizmo_MultipleTarget::<StartTarget>b__1_0(Verse.LocalTargetInfo)


### B.4 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.UI.UplinkProgressTabPanel::Draw(UnityEngine.Rect)

回调：System.Boolean Nivarian_Race.Code.UI.UplinkProgressTabPanel/<>c__DisplayClass3_0::<Draw>b__0(Nivarian_Race.Code.Defs.ProgressNodeDef)


### B.5 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.ShuttleUpgradeSystem.Comp_ShuttleUpgrade/<CompGetGizmosExtra>d__56::MoveNext()

回调：System.Void Nivarian_Race.Code.ShuttleUpgradeSystem.Comp_ShuttleUpgrade::<CompGetGizmosExtra>b__56_0()


### B.6 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.NivarianRefrigerator.Building_NivarianRefrigerator::DrawStoredContents(System.Single)

回调：System.Boolean Nivarian_Race.Code.NivarianRefrigerator.Building_NivarianRefrigerator/<>c::<DrawStoredContents>b__3_0(Verse.Thing)


### B.7 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.NivarianRefrigerator.Building_NivarianRefrigerator::DrawStoredContents(System.Single)

回调：Verse.IntVec3 Nivarian_Race.Code.NivarianRefrigerator.Building_NivarianRefrigerator/<>c::<DrawStoredContents>b__3_1(Verse.Thing)


### B.8 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.NivarianRefrigerator.Building_NivarianRefrigerator::DrawStoredContents(System.Single)

回调：System.Collections.Generic.List`1<Verse.Thing> Nivarian_Race.Code.NivarianRefrigerator.Building_NivarianRefrigerator/<>c::<DrawStoredContents>b__3_2(System.Linq.IGrouping`2<Verse.IntVec3,Verse.Thing>)


### B.9 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.NivarianMapComponent.MapComp_StanceAlternativeDrawer::MapComponentTick()

回调：System.Boolean Nivarian_Race.Code.NivarianMapComponent.MapComp_StanceAlternativeDrawer/<>c::<MapComponentTick>b__7_0(Nivarian_Race.Code.Interface.INivarianOffScreenStance)


### B.10 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.MechModuleSystem.ModuleWorker_HighPerformance/<GetGizmos>d__2::MoveNext()

回调：System.Boolean Nivarian_Race.Code.MechModuleSystem.ModuleWorker_HighPerformance/<>c__DisplayClass2_0::<GetGizmos>b__0()


### B.11 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianAidIncidentDebug::Refresh()

回调：System.Boolean Nivarian_Race.Code.DEBUG.Window_NivarianAidIncidentDebug/<>c::<Refresh>b__38_0(RimWorld.IncidentDef)


### B.12 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianAidIncidentDebug::Refresh()

回调：System.Boolean Nivarian_Race.Code.DEBUG.Window_NivarianAidIncidentDebug/<>c::<Refresh>b__38_6(Verse.AI.IAttackTarget)


### B.13 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianAidIncidentDebug::Refresh()

回调：System.ValueTuple`2<RimWorld.IncidentDef,Nivarian_Race.Code.Incidents.NivarianChoiceLetterStat> Nivarian_Race.Code.DEBUG.Window_NivarianAidIncidentDebug/<>c::<Refresh>b__38_2(Nivarian_Race.Code.Incidents.ChoiceLetter_NivarianAid)


### B.14 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::DoWindowContents(UnityEngine.Rect)

回调：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::RefreshSearchFilter()


### B.15 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::RefreshSearchFilter()

回调：System.Boolean Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::MatchesRangedSearch(Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow)


### B.16 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::RefreshSearchFilter()

回调：System.Boolean Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::MatchesMeleeSearch(Nivarian_Race.Code.DEBUG.WeaponMeleeDpsRow)


### B.17 Nivarian_Race.dll

入口：Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow>[] Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::BuildRangedColumns()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c::<BuildRangedColumns>b__42_23(Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow)


### B.18 Nivarian_Race.dll

入口：Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow>[] Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::BuildRangedColumns()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c::<BuildRangedColumns>b__42_33(Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow)


### B.19 Nivarian_Race.dll

入口：Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow>[] Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::BuildRangedColumns()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c::<BuildRangedColumns>b__42_35(Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow)


### B.20 Nivarian_Race.dll

入口：Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow>[] Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::BuildRangedColumns()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c::<BuildRangedColumns>b__42_37(Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow)


### B.21 Nivarian_Race.dll

入口：Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow>[] Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::BuildRangedColumns()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c::<BuildRangedColumns>b__42_39(Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow)


### B.22 Nivarian_Race.dll

入口：Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow>[] Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::BuildRangedColumns()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c::<BuildRangedColumns>b__42_41(Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow)


### B.23 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::CopyCsvToClipboard()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c__DisplayClass48_0::<CopyCsvToClipboard>b__2(Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponRangedDpsRow>)


### B.24 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug::CopyCsvToClipboard()

回调：System.String Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/<>c__DisplayClass48_1::<CopyCsvToClipboard>b__3(Nivarian_Race.Code.DEBUG.Window_NivarianWeaponDataDebug/ColumnDef`1<Nivarian_Race.Code.DEBUG.WeaponMeleeDpsRow>)


### B.25 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.ThingComps.Comp_FlyActivator/<CompGetGizmosExtra>d__9::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.ThingComps.Comp_FlyActivator::SwitchMode()


### B.26 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister/<CompFloatMenuOptions>d__17::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister/<>c__DisplayClass17_0::<CompFloatMenuOptions>b__0()


### B.27 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister/<CompGetGizmosExtra>d__16::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister::<CompGetGizmosExtra>b__16_0()


### B.28 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister/<CompGetGizmosExtra>d__16::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.ThingComps.ThingComp_ExpCanister/<>c::<CompGetGizmosExtra>b__16_3()


### B.29 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.CompArchiveTerminal/<CompGetGizmosExtra>d__15::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.CompArchiveTerminal::<CompGetGizmosExtra>b__15_0()


### B.30 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.CompCryoForge/<CompGetGizmosExtra>d__21::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.CompCryoForge::<CompGetGizmosExtra>b__21_0()


### B.31 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.CompCryoPrinter/<CompGetGizmosExtra>d__46::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.CompCryoPrinter::<CompGetGizmosExtra>b__46_0()


### B.32 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.CompNiraControlCenter/<CompGetGizmosExtra>d__122::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.CompNiraControlCenter::<CompGetGizmosExtra>b__122_0()


### B.33 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.CompSelfBuilding/<CompGetGizmosExtra>d__32::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.CompSelfBuilding::<CompGetGizmosExtra>b__32_0()


### B.34 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.CompSelfBuilding/<CompGetGizmosExtra>d__32::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.CompSelfBuilding::StartBuild()


### B.35 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.CompUplinkResearch/<CompGetGizmosExtra>d__54::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.CompUplinkResearch::<CompGetGizmosExtra>b__54_0()


### B.36 Nivarian_Race.dll

入口：System.Boolean Nivarian_Race.Code.Comps.BuildingComps.Comp_ContiniumBeacon/<CompGetGizmosExtra>d__24::MoveNext()

回调：System.Void Nivarian_Race.Code.Comps.BuildingComps.Comp_ContiniumBeacon/<>c::<CompGetGizmosExtra>b__24_0()


### B.37 Nivarian_Race.dll

入口：System.Void Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianPowerNetworkIO::OpenSettingsWindow()

回调：Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianPowerNetworkIO Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianPowerNetworkIO/<>c::<OpenSettingsWindow>b__22_0(Verse.ThingWithComps)


### B.38 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheBattlefield_m)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.39 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheBattlefield_m)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield_m TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.40 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheChurchAndMilira_m)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.41 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheChurchAndMilira_m)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira_m TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.42 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheChurchAndMilira)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.43 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheChurchAndMilira)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira TheTaleofMilira.CaravanArrivalAction_VisitTheChurchAndMilira/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.44 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheDestroyOutpost_m)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.45 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheDestroyOutpost_m)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost_m TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.46 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.47 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal_m TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.48 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.49 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSOSSignal/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.50 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheBattlefield)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.51 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheBattlefield)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield TheTaleofMilira.CaravanArrivalAction_VisitTheBattlefield/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.52 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheDestroyOutpost)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.53 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheDestroyOutpost)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost TheTaleofMilira.CaravanArrivalAction_VisitTheDestroyOutPost/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.54 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSupply::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheMiliraSupply)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSupply/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.55 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSupply::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheMiliraSupply)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSupply TheTaleofMilira.CaravanArrivalAction_VisitTheMiliraSupply/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.56 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheRuinOutpost_m)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.57 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost_m::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheRuinOutpost_m)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost_m TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost_m/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.58 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheRuinOutpost)

回调：RimWorld.FloatMenuAcceptanceReport TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__0()


### B.59 TheTaleofMilira.dll

入口：System.Collections.Generic.IEnumerable`1<Verse.FloatMenuOption> TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost::GetFloatMenuOptions(RimWorld.Planet.Caravan,TheTaleofMilira.TaleOfMilira_TheRuinOutpost)

回调：TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost TheTaleofMilira.CaravanArrivalAction_VisitTheRuinOutPost/<>c__DisplayClass11_0::<GetFloatMenuOptions>b__1()


### B.60 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Boolean TheTaleofMilira.TaleOfMilira_TheBattlefield_m/<>c::<Notify_CaravanArrived>b__7_0(Verse.Pawn)


### B.61 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Int32 TheTaleofMilira.TaleOfMilira_TheBattlefield_m/<>c::<Notify_CaravanArrived>b__7_1(Verse.Pawn)


### B.62 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__4()


### B.63 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__5()


### B.64 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__6()


### B.65 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheChurchAndMilira_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Boolean TheTaleofMilira.TaleOfMilira_TheChurchAndMilira_m/<>c::<Notify_CaravanArrived>b__5_0(Verse.Pawn)


### B.66 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheChurchAndMilira_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Int32 TheTaleofMilira.TaleOfMilira_TheChurchAndMilira_m/<>c::<Notify_CaravanArrived>b__5_1(Verse.Pawn)


### B.67 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__1()


### B.68 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__2()


### B.69 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Boolean TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m/<>c::<Notify_CaravanArrived>b__7_0(Verse.Pawn)


### B.70 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Int32 TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m/<>c::<Notify_CaravanArrived>b__7_1(Verse.Pawn)


### B.71 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__5()


### B.72 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__6()


### B.73 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__1()


### B.74 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost_m::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost_m/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__2()


### B.75 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheChurchAndMilira::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Boolean TheTaleofMilira.TaleOfMilira_TheChurchAndMilira/<>c::<Notify_CaravanArrived>b__5_0(Verse.Pawn)


### B.76 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheChurchAndMilira::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Int32 TheTaleofMilira.TaleOfMilira_TheChurchAndMilira/<>c::<Notify_CaravanArrived>b__5_1(Verse.Pawn)


### B.77 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Boolean TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal/<>c::<Notify_CaravanArrived>b__7_0(Verse.Pawn)


### B.78 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Int32 TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal/<>c::<Notify_CaravanArrived>b__7_1(Verse.Pawn)


### B.79 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__5()


### B.80 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheMiliraSOSSignal/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__6()


### B.81 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Boolean TheTaleofMilira.TaleOfMilira_TheBattlefield/<>c::<Notify_CaravanArrived>b__7_0(Verse.Pawn)


### B.82 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Int32 TheTaleofMilira.TaleOfMilira_TheBattlefield/<>c::<Notify_CaravanArrived>b__7_1(Verse.Pawn)


### B.83 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__4()


### B.84 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__5()


### B.85 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheBattlefield/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__6()


### B.86 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__1()


### B.87 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheDestroyOutpost/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__2()


### B.88 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__1()


### B.89 TheTaleofMilira.dll

入口：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost::Notify_CaravanArrived(RimWorld.Planet.Caravan)

回调：System.Void TheTaleofMilira.TaleOfMilira_TheRuinOutpost/<>c__DisplayClass7_0::<Notify_CaravanArrived>b__2()


## 附录 C：历轮判定与限制索引


### R1-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R1-verdict.json>)

- status：PASS_TARGETED
- scope：Tale/Nivarian plus required dependencies and MP; 3 client rounds of container/mech/module/stocking/glow/rainbow actions; 3 aid accepts; 50 faction scopes and research alternation per round
- limitations：Scope helper assertions do not invoke all original Tale outcomes. No plane or Privacy was loaded/tested, despite stale copied description strings in original result.json. Full content audit still incomplete.

### R4-partial-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R4-partial-verdict.json>)

- status：TARGET_ACTIONS_PASSED_RUN_INCOMPLETE

### R6-partial-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R6-partial-verdict.json>)

- status：PARTIAL_ONLY_RUN_FAILED

### R8b-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R8b-verdict.json>)

- status：PASS_SHORT_COMBINED_NOT_FULL_COMPATIBILITY
- limitations：No warm rejoin in this run; no exhaustive feature coverage

### R9-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R9-verdict.json>)

- status：PASS_SHORT_COMBINED_WITH_REJOIN
- limitations：Full objective still under audit: settings UI/config lifecycle, mechanical backdoor transitions, further caravan/dialogue and other content paths

### R10-settings-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R10-settings-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Single map short smoke only. No rejoin, full load lifecycle, other settings, or broad R10 regression. Slider results are automated, not physical mouse input.

### R11-settings-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R11-settings-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Automated slider returns in real settings UI; actual Need.SetInitialLevel and beacon pure settings getters. No actual beacon recharge simulation, boss fight, rejoin, or broad regression.

### R12-settings-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R12-settings-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Native settings drawing with automated CheckboxLabeled ref values and SliderLabeled results; synthetic draft gizmo with actual pawn and Drones flag. Does not prove every switch downstream incident/trait/cooking/combat effect, rejoin, or full mod compatibility.

### R13-settings-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R13-settings-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Automated native checkbox/slider results. Native eligibility checked, not a fresh full aid arrival/acceptance replay or rejoin. Full mod compatibility remains incomplete.

### R14-settings-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R14-settings-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R15-settings-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R15-settings-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：One actual Verdant Def, unspawned ThingMaker component getter; not full plant spawning/growth ticks, ability execution, full save/rejoin or complete content audit.

### R16-mothership-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R16-mothership-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Does not exercise the mothership window or targeting callback; these three effects invoke the actual worker executor in a synchronized test wrapper. Full content coverage and rejoin remain incomplete.

### R17-module-refuel-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R17-module-refuel-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Automated native callback invocation with real installed modules; no physical mouse drag, complete refueling job, cold reload or full-mod completion claim.

### R18-optional-visual-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R18-optional-visual-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Fog interval forced to 1 to exercise spawn branch; drone chatter reader hooks installed but no active attached ThingDefs in current loadout, so no actual speech/scheduler outcome claim. Full compatibility still incomplete.

### R19-plant-maturity-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R19-plant-maturity-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R20-shield-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R20-shield-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R21-return-shot-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R21-return-shot-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R22-recruitment-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R22-recruitment-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R23-recruitment-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R23-recruitment-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R24-recruitment-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R24-recruitment-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R25-recruitment-replay-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R25-recruitment-replay-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R26-dialogue-fixture-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R26-dialogue-fixture-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R27-dialogue-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R27-dialogue-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R28-supply-fixture-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R28-supply-fixture-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R29-supply-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R29-supply-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R30-supply-lifespan-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R30-supply-lifespan-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R31-supply-remaining-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R31-supply-remaining-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R32-raid-fixture-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R32-raid-fixture-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R33-stale-driver-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R33-stale-driver-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R34-raid-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R34-raid-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R35-mothership-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R35-mothership-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R36-lent-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R36-lent-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R37-gene-life-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R37-gene-life-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R38-self-build-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R38-self-build-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R39-sos-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R39-sos-verdict.json>)

- status：NATIVE_SOS_JOIN_AND_DEFERRED_RAID_SHORT_PASS
- limitations：One forced outcome variant; not all SOS branches or persistent multi-map coverage

### R40-caster-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R40-caster-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R41-ability-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R41-ability-verdict.json>)

- status：PASS
- limits：["One cast each for three concrete abilities","Caster identity and cooldown checked; not direct healing throughput or melee damage","No explicit PsionicAttunement severity fixture; experience gain not established"]

### R42-ability-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R42-ability-verdict.json>)

- status：PASS
- limits：Shield/verdant creation and caster identity, battery actual stored energy; not full plant growth or barrier lifetime coverage

### R43-robe-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R43-robe-verdict.json>)

- status：PASS
- limitations：Three new pawn/robe pairs; no mid-cast reload or same-item cooldown re-equip test

### R44-drake-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R44-drake-verdict.json>)

- status：SAVED_LOG_GATES_PASS_AFTER_CONTROLLER_COUNT_FIX
- limitations：Not a rerun; original result preserved. No damage target or mid-breath reload test

### R45-church-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R45-church-verdict.json>)

- status：FAILED_PRESERVED

### R46-startup-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R46-startup-verdict.json>)

- status：INVALID_GRAPHICS_STARTUP
- scope：No fog diagnostic execution; cannot assess candidate or R45 root cause

### R47-startup-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R47-startup-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage

### R49-circle-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R49-circle-join-verdict.json>)

- status：PASS
- scope：isolated Tale/Nivarian required-dependency loadout; generated world; not full user replay or complete UI coverage
- limitations：Circle tasks saved in test GameComponent, not actual moving drone trajectory. R45 intermittent fog assertion not reproduced; original missing spawn cause remains unproven. Not full compatibility proof.

### R50-drone-ownership-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R50-drone-ownership-verdict.json>)

- status：PASS
- scope：Unspawned actual ThingDef graph roundtrip; no flight, combat, cold rejoin cycles or full compatibility claim

### R51-drone-cache-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R51-drone-cache-verdict.json>)

- status：FAILED_FIXTURE_PRECONDITION

### R53-drone-cache-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R53-drone-cache-join-verdict.json>)

- status：PASS
- scope：Engineering payload populated, stationary source passed to native worker; no moving drone or populated enemy/heal/claim proof

### R54-drone-payload-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R54-drone-payload-verdict.json>)

- status：PASS
- scope：Actual initial join preserves populated enemy/heal/engineering payload graph, ordered hostiles, unspawned heldSteel37708, pawn37678 and one target claim. Artificial graph verifies persistence, not combat/healing gameplay.

### R55-drone-repair-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R55-drone-repair-verdict.json>)

- status：PASS
- scope：Native hub SpawnDrone and Engineer.CompTick warmup13 before hosting, then ordinary full simulation after actual client join. Initial/final equality and all combined gates passed; no intermediate per-tick movement trace claim.

### R56-turret-history-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R56-turret-history-verdict.json>)

- status：FAILED

### R57-turret-score-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R57-turret-score-verdict.json>)

- status：PASS
- scope：Actual initial join retains turret history; native score comparisons on temporarily spawned actual turret. Not firing combat or full mod compatibility.

### R58-pending-support-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R58-pending-support-verdict.json>)

- status：FAILED

### R59-cargo-trace-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R59-cargo-trace-verdict.json>)

- status：PASS_WITH_OPEN_FINDINGS

### R60-cargo-replay-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R60-cargo-replay-verdict.json>)

- status：FAILED_REPRODUCED

### R61-pod-fix-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R61-pod-fix-verdict.json>)

- status：FAILED_LATER_SHIELD_ASSERT

### R62-healing-assert-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R62-healing-assert-verdict.json>)

- status：FAILED_TEST_ASSERTION
- scope：No normal-healing pass claimed; shield action not reached.

### R63-healing-shield-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R63-healing-shield-verdict.json>)

- status：FAILED_FINAL_FIXTURE_SNAPSHOT

### R64-ice-lifecycle-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R64-ice-lifecycle-verdict.json>)

- status：FAILED_FIXTURE_COLLISION_CONFIRMED

### R65-combined-fix-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R65-combined-fix-verdict.json>)

- status：PASS
- scope：R58 generated baseline replay; isolated combined short test, not full mod coverage

### R66-attunement-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R66-attunement-verdict.json>)

- status：FAILED_TARGET_VALIDATION

### R67-attunement-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R67-attunement-verdict.json>)

- status：FAILED_TEST_ENERGY_EXPECTATION
- scope：Experience and threshold partial evidence; overall FAILED retained

### R68-attunement-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R68-attunement-verdict.json>)

- status：PASS
- scope：Three tree casts with actual experience and Serenity .2 threshold, combined short checks. Not all thresholds or active attunement rejoin.

### R69-attunement-setup-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R69-attunement-setup-verdict.json>)

- status：FAILED_PREHOST_SETUP

### R70-attunement-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R70-attunement-join-verdict.json>)

- status：PASS
- scope：Initial join of existing three .25 attunement states, retained ability IDs and rebuilt caches; combined short actions. Not all thresholds or reconnect mid-cast.

### R71-sos-medical-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R71-sos-medical-verdict.json>)

- status：FAILED

### R72-sos-medical-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R72-sos-medical-verdict.json>)

- status：FAILED

### R73-pause-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R73-pause-verdict.json>)

- status：FAILED

### R74-sos-medical-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R74-sos-medical-verdict.json>)


### R75-tale-reward-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R75-tale-reward-verdict.json>)

- status：PASS
- scope：Non-host synchronized test wrapper invokes native reward executors; per-item IDs/defs/stack counts and one immediate letter each match. Does not prove each actual arrival menu or all random reward extremes.

### R76-quest-faction-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R76-quest-faction-verdict.json>)

- status：FAILED

### R77-quest-prerequisite-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R77-quest-prerequisite-verdict.json>)

- status：FAILED

### R78-quest-generation-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R78-quest-generation-verdict.json>)

- status：PASS
- scope：Native quest generation via synchronized test wrapper in map context, CanRun prerequisites, autoaccept, world site faction/IDs/tile and questTags. Not natural storyteller scheduling, multifaction ownership or visit completion.

### R79-quest-resolution-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R79-quest-resolution-verdict.json>)

- status：PASS
- scope：Native arrival executor with controlled reward selector, native Resolved signal, site removal, Quest.EndedSuccess and per-item rewards match. No menu/other branches/natural storyteller claim.

### R80-church-quest-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R80-church-quest-verdict.json>)

- status：PASS
- scope：Both Church quest variants, native generation and actual client arrival menu/five native SP dialog activations, rewards and EndedSuccess. Other dialogue branches not implied.

### R81-supply-quest-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R81-supply-quest-verdict.json>)

- status：STOPPED_INCOMPLETE

### R82-supply-quest-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R82-supply-quest-verdict.json>)

- status：PASS
- scope：Actual quest generation, client native arrival/leave dialogs; supply pack, medicine, no reward; both quest variants; success and specific arrival/leave letters. Other supply branches covered separately, not all task/dialog pair combinations.

### R83-starfall-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R83-starfall-verdict.json>)

- status：FAILED

### R84-starfall-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R84-starfall-verdict.json>)

- status：FAILED

### R85-effect-removal-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R85-effect-removal-verdict.json>)

- status：FAILED

### R86-starfall-expiry-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R86-starfall-expiry-verdict.json>)

- status：PASS

### R87-progress-context-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R87-progress-context-verdict.json>)

- status：FAILED_FIXTURE

### R88-progress-owner-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R88-progress-owner-verdict.json>)

- status：PASS_DIAGNOSTIC_GAP_REPRODUCED
- scope：Both peers identical native count results. Full existing combined controller gates pass. Demonstrates ambient faction inventory scan gap, not desync root cause or all progression paths.

### R89-progress-fix-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R89-progress-fix-verdict.json>)

- status：PASS
- limitations：["Inventory branch only; research context unpatched","One player map in multifaction mode; no cross-owner multi-map quantity test","Not full mod compatibility or Desync85-93 root-cause proof","Candidate not formally deployed"]

### R90-uplink-signal-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R90-uplink-signal-verdict.json>)

- status：FAILED_SHARED_STATE_DIVERGENCE_REPRODUCED

### R91-uplink-signal-fix-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R91-uplink-signal-fix-verdict.json>)

- status：PASS

### R92-research-scan-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R92-research-scan-verdict.json>)

- status：PASS_DIAGNOSTIC_GAP_REPRODUCED
- scope：Native RunProgressScan explicitly called under spectator then owner for diagnosis. Owner call completes node; not automatic completion proof. Other combined gates pass.

### R93-research-scan-fix-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R93-research-scan-fix-verdict.json>)

- status：PASS

### R94-research-owner-control-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R94-research-owner-control-verdict.json>)

- status：PASS
- scope：Closes R93 owner-only positive automatic-control gap. R93 separately verifies spectator-only negative exclusion. Does not prove all mod contents or multi-owner map combinations.

### R95-nira-metrics-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R95-nira-metrics-verdict.json>)

- status：FAILED_SHARED_SCORE_DIVERGENCE_REPRODUCED
- scope：Actual UI refresh mutates saved shared scores only on client; synchronized check sees unequal scores. No claim of attribution to a user desync zip.

### R96-nira-metrics-fix-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R96-nira-metrics-fix-verdict.json>)

- status：PASS
- limits：UI RefreshMetrics executor covered. Native daily AssessAllMetrics path under active Nira storyteller and multi-owner colony scoring are not covered. Formal deployment unchanged; full compatibility incomplete.

### R97-nira-world-inputs-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R97-nira-world-inputs-verdict.json>)

- status：PASS
- limits：Read-only native input diagnostic; does not execute active Nira daily assessment. Equal peer inputs are not a desync reproduction. No production changes or formal deployment in this run; full compatibility incomplete.

### R98-R99-nira-daily-fixture-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R98-R99-nira-daily-fixture-verdict.json>)

- status：FAILED_FIXTURE

### R100-nira-daily-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R100-nira-daily-verdict.json>)

- status：PASS_DIAGNOSTIC_CONFIRMED_GAP
- limits：No production fix yet. Not a desync reproduction. Active feature via shared switch; complete Nira storyteller event flow and multi-owner scoring unverified. Candidate/formal deployment unchanged.

### R101-nira-metric-inputs-fix-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R101-nira-metric-inputs-fix-verdict.json>)

- status：PASS
- limits：One real player faction/one map. Multi-owner pooling, nonzero food/growing/barn, spectator-owned defense exclusions and full Nira storyteller events still need coverage. Formal deployment unchanged; full compatibility incomplete.

### R102-nira-two-faction-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R102-nira-two-faction-verdict.json>)

- status：PASS
- limits：Single map; second faction temporarily owns one pawn, no second home map. Nonzero food/growing/barn and spectator defenses still unverified. CandidateR103 debug force-evaluation addition was not loaded.

### R103-nira-force-startup-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R103-nira-force-startup-verdict.json>)

- status：FAILED_FIXTURE_STARTUP
- limits：No gameplay/button validation occurred in R103.

### R104-nira-buttons-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R104-nira-buttons-verdict.json>)

- status：FAILED_FIXTURE
- limits：R104 partial force-evaluation evidence does not prove total combination or Add button success.

### R105-nira-developer-buttons-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R105-nira-developer-buttons-verdict.json>)

- status：PASS
- limits：Add uses unrolled-die UI branch; rolled branch calls same native method but not directly clicked. Full Nira event flow, nonzero food/growing/barn and other remaining audit surfaces still incomplete. No formal deployment.

### R106-inventory-food-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R106-inventory-food-verdict.json>)

- status：PASS
- limits：No claim that old manager leak itself was a desync reproduction. Nonzero growing-zone/barn nutrition, spectator defense exclusions and remaining audit work not completed; formal deployment unchanged.

### R107-drone-developer-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R107-drone-developer-verdict.json>)

- status：PASS
- limits：["Only ShuttleOrbitAttacker; fixture Tick suppressed to isolate command behavior.","FixedWing virtual override and autonomous AI not runtime-tested in this run.","Negative developer permission rejection not tested.","Formal deployment unchanged; full compatibility incomplete.","Runner result generic scope mentions generated world; this run actually uses specified replay baseline."]

### R108-building-developer-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R108-building-developer-verdict.json>)

- status：PASS
- limits：["Fixture component ticking paused and internal states prepared for branch isolation; no claim of complete normal production lifecycle.","Developer negative permission rejection not tested.","Formal deployment unchanged; full compatibility incomplete.","Runner generic scope says generated world; actual input is specified replay baseline."]

### R109-bill-clipboard-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R109-bill-clipboard-verdict.json>)

- status：PASS
- limits：["Existing installed Trio Bills.Paste wrapper validated; no new production patch in this run.","Fixture tick paused, source queues seeded; not full production job lifecycle.","Runner generic result scope generated-world text is stale; actual replay baseline specified above.","Formal deployment unchanged; full compatibility incomplete."]

### R110-tale-map-restore-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R110-tale-map-restore-verdict.json>)

- status：PASS
- limits：["Scope probe constructs closures and invokes patch boundaries only; does not execute every native outcome.","No old-candidate runtime desync claim; root cause established from installed MP PopFaction semantics and missing reference capture.","Singlemap synchronous replay baseline R72; not fullmulti-map or fullcompat proof.","Formal deployment unchanged."]

### R111-shared-map-restore-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R111-shared-map-restore-verdict.json>)

- status：PASS
- limits：["Wireless scope invoked with valid generic ThingComp parent; not new end-to-end power allocation scenario.","Other scope checks do not prove full incident lifecycle; singlemap synchronous R72 replay baseline.","No claim this fixes every desync or all UI callbacks. Formal deployment unchanged/fullcompat incomplete."]

### R112-story-developer-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R112-story-developer-verdict.json>)

- status：PASS
- limits：["Fixture beacon/Story ticks paused, initialguards suppressed; no fullraid lifecycle claim.","Uplink actual gizmo tested; decryption panel/native debug action share executor but not separately clicked.","Negative permission rejection not exercised.","Existing InputLegacyModule ReflectionOnly preload warning also presentR111; no new fatal startup failure.","Formal deployment unchanged/fullcompat incomplete."]

### R113-archive-developer-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R113-archive-developer-verdict.json>)

- status：FAILED_FIXTURE

### R114-archive-reachable-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R114-archive-reachable-verdict.json>)

- status：PASS
- limits：["Fixture archive/collector/egg CompTick suppressed, egg empty; no full hatch or automatic collector lifecycle claim.","Egg registration resolves but its command execution was not tested because current native UI is unreachable.","Developer permission rejection not tested.","Isolated one-map R72 replay baseline; original runner generated-world scope text is stale.","Original R113 failed fixture retained. Pre-existing ReflectionOnly InputLegacyModule warning remains.","Formal installed supplement unchanged; complete two-mod compatibility still pending."]

### R115-currency-collectors-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R115-currency-collectors-verdict.json>)

- status：PASS
- limits：["One actual map, second registered player faction and temporarily reassigned pawns; no second loaded home map.","Test seeds collector remainder100 to forcepositivecredit, controlsresearchprogress and suppliesPowerOn atfixture tick entry; notphysical power-network validation.","Probe suppresses only fixture uplink-research ticks and freezes selectedcollector afterone executor perphase; natural colony/particle tick scheduling is verified.","Singleplayer guards source-verified, no separate singleplayer runtime test.","R72 replaybaseline used; runner generated-world scope text isstale.","Formal deployed supplement unchanged; fulltwo-modcompatibility remainsincomplete."]

### R116-active-content-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R116-active-content-verdict.json>)

- status：PASS
- scope：Loaded Thing/ThingComp/HediffComp types declaring Gizmo-named methods in target assemblies, plus eight explicit audit targets. Not a complete UI/window/event/quest inventory.
- limits：["No active mapping is current-loadout applicability evidence, not synchronization proof for enabling these types later.","Spawned-instance scan covers map listerThings and direct comps; not all inventories/world holders or arbitrary old saves.","Zero directnewobj does not exclude reflection or third-party factories; audit scoped to exact currenttarget DLL hashes and loadedtypes.","No production source orcandidate changed this run. Formal deployment unchanged; fullcompat incomplete.","Original runner generated-world scope text stale; actualinput is R72 replaybaseline."]

### R117-scale-controls-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R117-scale-controls-verdict.json>)

- status：PASS
- limits：["Slider field staged in clientWindow then actualnative ApplySliderChange invoked, notphysical mouse drag/DrawExtra rendering.","Two generated realNivarianpawns withactiveScale_Cover; onlytheirScaleControl.CompTickpaused forstable receipts.","No natural scalegrowth/harvest/missing-part/save-rejoin coverage fromthisprobe.","Negative developer permission rejection not exercised.","No new production change; CandidateR115 remainsauthority and formal installedsupplement unchanged.","R72 replaybaseline used; runner generated-world scope text stale. Fulltwo-modcompat incomplete."]

### R118-tale-dialogue-graph-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R118-tale-dialogue-graph-verdict.json>)

- status：PASS
- limits：["Traversal invokesactual linkLateBind callbacks afteropeningnative talk dialog; notmanual screen-clicking eachnode.","Fixture setsrelations directly andgenerates/registersspecialpawn; nativearrival scheduling nottested bythisprobe.","One actual map/playerowner. Noarbitrarycaller onComplete callback safety claim.","No production code/candidate change; formalinstallation unchanged/fullcompat incomplete.","ActualinputR72replaybaseline, runnergenerated-worldscope text stale."]

### R119-module-queue-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R119-module-queue-verdict.json>)

- status：PASS
- limits：["Control-center CompTick paused for fixture, not full material delivery/work/install completion","Two fixture-generated Nivarian mech pawns, one control center, same player faction and map","Basic research completed by native FinishProject in synchronized fixture setup; uninstall modules preinstalled","Native panel rendered with test-only button return injection; not physical mouse automation or original parent window lifecycle","No concurrent issuers, stale-index stress, cold rejoin or second home-map claim","Original runner generic reason/scope text retained; this report gives actual module-test scope"]

### R120-uplink-panel-fixture-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R120-uplink-panel-fixture-verdict.json>)

- status：FAILED_FIXTURE_SETUP

### R121-uplink-panel-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R121-uplink-panel-verdict.json>)

- status：PASS
- limits：["Native panel in test window, injected button return; original parent-window lifecycle not newly covered","Fixture-created native beacon49757, fixture CompTickpaused","ResearchNira_MechModule_Advanced; no natural power/antenna/work/research-completion test","Test materials seeded directly into container and destroyed after refund assertion","One map, no rejoin/concurrent issuer/second faction test","Original runner reason/scope remains generic; this report records actual test scope"]

### R122-uplink-completion-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R122-uplink-completion-verdict.json>)

- status：FAILED_RUNTIME_TEST_GATE
- finding：Both peers completed setup; client deadline expired with zero UPLINK_COMPLETION_NATIVE records. Shared checkpoints continued through7000 and desynced=False.

### R123-uplink-tick-diagnostic-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R123-uplink-tick-diagnostic-verdict.json>)

- status：FAILED_TICK_REACHABILITY

### R124-uplink-completion-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R124-uplink-completion-verdict.json>)

- status：PASS
- limits：["PowerOn is controlled by fixture at the eligible native tick; not an end-to-end electrical network test","Research progress prepared to2499.99 against baseCost2500; prerequisite/material preparation is fixture-controlled","One project, one completion, one map; no rejoin or second-home-map proof","Native antenna and terminal spawned in isolated fixture; natural long-duration research throughput not tested","No production DLL changed; original generic runner reason/scope left untouched"]

### R125-shuttle-queue-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R125-shuttle-queue-verdict.json>)

- status：PASS
- limits：["Synthetic native button results, not physical mouse input","Target CompTick frozen; active installation and installed module fixtures prepared","No natural installation completion, uninstall refund, rejoin or second map validation","No production DLL change or formal deployment; generic original runner scope retained"]

### R126-shuttle-lifecycle-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R126-shuttle-lifecycle-verdict.json>)

- status：FAILED

### R127-shuttle-lifecycle-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R127-shuttle-lifecycle-verdict.json>)

- status：FAILED

### R128-shuttle-container-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R128-shuttle-container-verdict.json>)

- status：FAILED

### R129-shuttle-trace-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R129-shuttle-trace-verdict.json>)

- status：FAILED

### R130-shuttle-focused-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R130-shuttle-focused-verdict.json>)

- status：PASS
- limits：["Progress and installed module prepared by fixture; not full duration crafting","Fixture shuttles forbidden, active test component gated between receipt checks","Only one map; no save/rejoin or speed-module multiplier coverage","Prior broad combination container contamination did not recur; this run does not identify its cause","No production change or formal deployment"]

### R131-debug-tools-missing-def-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R131-debug-tools-missing-def-verdict.json>)

- status：FAILED

### R132-debug-tools-fixture-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R132-debug-tools-fixture-verdict.json>)

- status：FAILED
- limits：Only one negative bombardment and one assault spawn completed; no three-round proof yet

### R133-debug-global-closure-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R133-debug-global-closure-verdict.json>)

- status：FAILED

### R134-debug-tools-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R134-debug-tools-verdict.json>)

- status：PASS
- limits：["Synthetic button/cursor input, not physical input","One map, debug permission enabled; negative permission rejection not runtime tested","Bombardment definition absent: negative guard only, no positive bombardment execution claim","Spawn results captured then despawned/cleaned by fixture; no long autonomous lifetime coverage","Two direct quest buttons still pending; no full mod compatibility or formal deployment claim"]

### R135-window-quest-buttons-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R135-window-quest-buttons-verdict.json>)

- status：PASS
- limits：["Synthetic button results; one map, no reconnect, no negative developer permission test","Aid map component ticks suspended during fixture to separate button execution from automatic events","Only task creation tested here; accepting/completing generated quests is separate scope","Original runner scope text retained; this audit explicitly records six additional quest-button actions","Full mod compatibility incomplete; no formal deployment"]

### R136-archive-tag-menus-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R136-archive-tag-menus-verdict.json>)

- status：PASS
- limits：["Menu creation and actions invoked through reflection, not physical input","One map; no rejoin; custom text entry and removal UI excluded","No production change or formal deployment; full compatibility incomplete"]

### R137-archive-driver-startup-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R137-archive-driver-startup-verdict.json>)

- status：FAILED_HARNESS_STARTUP

### R138-archive-custom-remove-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R138-archive-custom-remove-verdict.json>)

- status：PASS
- limits：["Synthetic button results and prefilled text buffer, not physical keyboard/mouse","One map, no rejoin, no long regression","No new production changes; formal release not deployed"]

### R139-battlefield-raids-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R139-battlefield-raids-verdict.json>)

- status：PASS
- limits：["Arrival test seed and Intellectual6 fixture; three executions per variant use same branch seed","One initial home, async off; generated encounters do not prove two home colonies or rejoin","Letter existence asserted, not exact letter payload; inventory reward contents not asserted","No formal deployment or full compatibility claim"]

### R140-outpost-sos-raids-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R140-outpost-sos-raids-verdict.json>)

- status：PASS
- limits：["Fixed test arrival seed; one initial home map; async off; no rejoin","Only raid branches tested; exact letter payload and other outcome branches excluded","No full compatibility claim or formal deployment"]

### R141-outpost-join-letter-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R141-outpost-join-letter-verdict.json>)

- status：FAILED_ASSERTION_UNRESOLVED

### R142-outpost-join-letter-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R142-outpost-join-letter-verdict.json>)

- status：FAILED_HARNESS

### R143-outpost-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R143-outpost-join-verdict.json>)

- status：PASS
- limits：["One map and player faction; seeded arrival selects same generated pawn family; no rejoin","No formal deployment/full compatibility claim","R141/R142 failed evidence retained; global letter total includes unrelated letters and some have null LookTargets"]

### R144-battlefield-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R144-battlefield-join-verdict.json>)

- status：FAILED_BRANCH_SELECTION_UNRESOLVED

### R145-battlefield-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R145-battlefield-join-verdict.json>)

- status：FAILED_FIXTURE_EFFECTIVE_SKILL

### R146-battlefield-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R146-battlefield-join-verdict.json>)

- status：FAILED_UNEXPECTED_ENCOUNTER

### R147-battlefield-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R147-battlefield-join-verdict.json>)

- status：PASS
- limits：["Completed fixture caravans/pawns destroyed after each check; priorR146encounter source not reproduced or identified","Onehome asyncfalse/no rejoin; seeded selection; no formaldeployment/fullcompatclaim"]

### R148-sos-join-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R148-sos-join-verdict.json>)

- status：PASS
- limits：["Completedfixturecaravans destroyed; onehome asyncfalse/no rejoin","Outcome_MiliraandRaid is separate and not covered by this run","No formaldeployment/fullcompatclaim"]

### R149-sos-join-raid-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R149-sos-join-raid-verdict.json>)

- status：PASS
- scope：Actual nonhost native caravan arrival and DiaOption.Activate; SOS normal/m x four pawn branches x accept/reject once each. Effective Int16 and deterministic outcome seed are fixture inputs. Both choices recruit one pawn and queue native raid. Each map has exactly two owner pawns, native hostile enemies with lords and one ThreatBig map letter; native choice executes exactly once and immediate local snapshot remains unchanged.
- limits：["One initial home map; generated16 encounter maps do not prove two-home behavior","No rejoin/async-time/extended battle stability; recruitment-letter payload and full inventory not compared after raid","CandidateR135 unchanged; no formal deployment or full compatibility claim"]

### R150-church-rewards-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R150-church-rewards-verdict.json>)

- status：PASS
- scope：Native client caravan arrival and DiaOption.Activate; 2 church variants x NormalEnd/NoThing x both options x3. Native callback counts, immediate-local unchanged, all reward counts (random predicted without stream mutation), full inventory IDs/defs/stacks, one related PositiveEvent letter, player pawn faction checked. Effective skills16/4 and outcome seeds fixture-only.
- limits：["Fixture-created sites; native quest creation/completion not exercised here","No cold join/async/two-home proof; nested dialog cleanup and repeated click abuse not separately asserted","CandidateR135 unchanged; formal deployment and full compatibility incomplete"]

### R151-church-dialogue-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R151-church-dialogue-verdict.json>)

- status：PASS
- scope：Native client arrival and DiaOption.Activate;2church variants x3SPMilira reward paths and5MiliraJion paths. All46callbacks observed in matching traces; one execution per path step, immediate inventory/pawn/letter unchanged; exact predicted feather rewards/sword, acceptedSister ownership19 or no recruit, full pawn/inventory IDs and onePositiveEvent letter.
- limits：["Fixture-created sites; no native quest lifecycle/rejoin/async/two-home proof","Root MiliraJion leave callback lacks native resolveTree=true; repeat-click/window cleanup not asserted in this run, requires separate lifecycle review","No formal deployment/fullcompatibility claim; CandidateR135 unchanged"]

### R152-church-exit-diagnostic-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R152-church-exit-diagnostic-verdict.json>)

- status：PASS
- meaning：Diagnostic successfully reproduced native repeatable exit; this does not claim exit behavior repaired.
- finding：Both native church root leave options have resolveTree=false and no next node. Actual delayed second click invokes b__14 twice and yields exactly2PositiveEvent letters on both peers. Window and MP dialog-open flag remain present/true before synchronized fixture cleanup.
- decision：Native dialogue behavior, not a demonstrated desync. No production patch added solely to change native behavior. Further UI concurrency/manual-close behavior outside this diagnostic.
- limits：["One initial home map/no rejoin/async/manual client-only close","Same CandidateR135; no formal deployment/fullcompatibility claim"]

### R153-nira-module-lifecycle-fixture-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R153-nira-module-lifecycle-fixture-verdict.json>)

- status：FAILED

### R154-nira-module-lifecycle-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R154-nira-module-lifecycle-verdict.json>)

- status：FAILED

### R155-nira-module-lifecycle-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R155-nira-module-lifecycle-verdict.json>)

- status：PASS
- scope：14actual map-scheduled60tick CompTick gates: Construction+Mining installation queues, missing material/distance waiting and progress gates, native completion and queue advancement; installed worker/hediff IDs, work cache, priority3/skill10; exact consumed material IDs; two uninstall completions/full cost refunds and locations; baselevelInt retained10 while public disabledLevel0. All14ordered peer receipts identical.
- limits：["Positions and near-completion work progress seeded by fixture; component stepped at selected native hash ticks","No actual mech job approach, hauling delivery, full work-duration or derived module-worker coverage","One initial home, no cold join/async/two-home; no message payload comparison","CandidateR135 unchanged; no formal deployment or fullcompatibility claim"]

### R156-tale-remaining-outcomes-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R156-tale-remaining-outcomes-verdict.json>)

- status：PASS
- scope：Actual nonhost native world menu arrival;8NoThing battlefield/destroy/SOS/ruin variants and4Toolate destroy/ruin variants, three times each. Native outcome executor exactlyonce, immediate unchanged, expected random/fixed reward quantities, all inventory IDs, one letter ID/type/label, pawn ownership and site resolution match.
- limits：["EffectiveInt4 and outcome seed are fixture inputs; sites created directly, not actual quest lifecycle","No rejoin/async/two-home or fulllettertext/negative-negotiator branch proof","CandidateR135 unchanged; no formaldeployment/fullcompatibility claim"]

### R157-combined-gizmos-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R157-combined-gizmos-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required dependencies; copied R72 baseline; moonbloom mature, ordinary mech wireless, module wireless, container eject x3; native gizmos and immediate state/settled state assertions

### R158-combined-gizmos-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R158-combined-gizmos-verdict.json>)

- status：PASS
- limits：["Fixture creates plants/mechs/building, installs wireless module directly and finishes research. Does not prove natural acquisition/install/research lifecycle.","Three repeats on one home map with asyncoff, debugEveryone and multifaction enabled; no coldrejoin/secondhome/longsoak.","Plant reset to0.5 via synchronized setup; no natural full growth-duration claim.","No claim of actual wireless energy transfer or power depletion.","R157 inaccessible-plant failure preserved; precise object-loss cause not proven.","Candidate archived only; formal deployment unchanged."]

### R159-production-menus-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R159-production-menus-verdict.json>)

- status：FAILED
- scope：isolated Tale/Nivarian required dependencies; copied R72 baseline; seven quality choices, native rename dialog, four repeat choices on printer and forge x3; close/reopen and selected-object decoy; settings only

### R160-production-menus-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R160-production-menus-verdict.json>)

- status：PASS
- limits：["Fixtures create devices and seed one bill each. Printer/forge CompTick suppressed for fixture objects, so no completed production/material consumption/generated item-quality claim.","No full research/menu navigation coverage; this run covers selected menu actions only.","One home map, async off, debugEveryone/multifaction enabled; no longsoak/coldrejoin/secondhome.","R159 startup failure retained separately; candidate unchanged fromR157, no production changes this test.","Formal DLL remains1.0.1; candidate not deployed."]

### R161-self-building-selection-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R161-self-building-selection-verdict.json>)

- status：LOCAL_SELECTION_BRANCH_ASYMMETRY_REPRODUCED
- limits：["FinishBuilding intercepted before destructive body; no actual desync induced.","Ordinary fixture work paused; not natural construction completion verification.","Harmony owners logged at probe installation, not a late full patch census.","One home map, async off, dev commands allowed, no rejoin.","Production candidate unchanged; not a repair pass."]

### R162-self-building-completion-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R162-self-building-completion-verdict.json>)

- status：SHORT_COMPLETION_PASS
- limits：["All six final buildings ArchiveTerminal without CompQuality; no quality-roll coverage.","Normal completion uses preseeded buildingStarted/progress; no full construction/hauling duration.","No payload transfer, paused maps, concurrent requests, rejoin or two-home-map test.","No formal deployment."]

### R163-native-debug-menus-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R163-native-debug-menus-verdict.json>)

- status：HARNESS_NODE_LOOKUP_FAILED

### R164-native-debug-menus-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R164-native-debug-menus-verdict.json>)

- status：HARNESS_SECOND_TOOL_STALLED

### R165-native-debug-menus-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R165-native-debug-menus-verdict.json>)

- status：SHORT_NATIVE_DEBUG_PASS
- limits：["Debug-mode single home map, async off, two target pawns Tick/TickInterval paused only while fixture active.","Native node Enter, ToolMapForPawns click, native lister option and pod tool callbacks executed; no direct final executor invocation.","Relations seeded Lover before each engagement; no natural romance or moving-target guarantee.","No rejoin, multi-home-map or long regression. No production code changes or formal deployment in this run."]

### R170-supply-routing-fixed-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R170-supply-routing-fixed-verdict.json>)

- status：SHORT_ROUTING_FIX_PASS
- scope：Three SupplyPack native leave operations with earlier native faction-info dialog; verify target closes with exactly2event letters and unrelated action remains zero, then explicitly click unrelated native option and require1onboth. Diagnostic SUPPLY_ROUTE_WRONG log name retained for fixture counter but now occurs only after explicit unrelated click.
- limits：["Only SupplyPack callback runtime exercised, other five reward/no-reward callbacks resolved at startup but pending runtime.","R166/R167 remaining33case SupplyPack run must be repeated against R170.","No raid callback fix, other Tale dialogue families audit pending.","No rejoin, duplicate-click or formal deployment; singlehome debug/multifaction harness async off."]

### R172-supply-all-rewards-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R172-supply-all-rewards-verdict.json>)

- status：SHORT_ALL_SUPPLY_REWARDS_PASS
- limits：["48 cases:33SupplyPack variants plus3each SolarCrystal/SunLightFuel/MealSurvivalPack/MedicinePack/NoThing, both quest definitions alternate.","Each case has2actual synchronized Choose commands, one successful supply leave and one harmless stale request; one explicit unrelated native dialog action after supply closure.","Branches seeded; high-tier four nameplate/material pairs rather than all16cross-products; no unbounded stochastic coverage.","Test members detached and discarded through native WorldPawns cleanup after result recording; no persistent ownership/rejoin proof.","Singlehome map, async off, debug/multifaction harness. Raid and other Tale dialogues remain outside new patch. No formal deployment."]

### R173-supply-raid-paused-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/R173-supply-raid-paused-verdict.json>)

- status：FAILED_PAUSED_BY_USER
- error：System.InvalidOperationException: Supply raid dialog lacks its event identity
- nextStep：Inspect raid closure event identity registration before any retest. User requested pause after this round; wait for explicit next instruction.

### circle-state-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/circle-state-verdict.json>)

- status：OFFLINE_AND_REAL_INITIAL_JOIN_SHORT_PASS
- scope：Actual native Scribe Saving/LoadingVars with invalid target; patch-only later phases; no full cross-reference/Unity join

### pending-queue-offline-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/pending-queue-offline-verdict.json>)

- status：OFFLINE_QUEUE_PASS
- limits：No map/thing reference tasks, full Unity load or actual multiplayer rejoin

### pending-wait-offline-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/pending-wait-offline-verdict.json>)

- status：OFFLINE_NATIVE_WAITTASK_PASS
- scope：Actual installed WaitTask scalar Scribe roundtrip; manual ResolvingCrossRefs/PostLoadInit preserve id/progress/waitTick/elaspedTick; original and loaded instances advance identically through completion
- limits：Not TaskQueue deep serialization, map references, full Unity loading, or multiplayer rejoin

### WarmR8-verdict

[原始证据](<H:/下载/元之雨RJW全种族拓展/rim/rim/rimworld/Mods/MP-meow-online-shop/BuildValidation/TaleNivarianFullCompat_20260914/Audit/WarmR8-verdict.json>)

- status：PASS_SHORT_WITH_NATIVE_REJOIN
- limitations：Not exhaustive feature coverage; Forge override new fix is not part of this candidate






## 2026-09-25 世界事件对话框保存/加入修复与正式验证（R191–R202）

- 根因：Tale 世界事件窗口在 MP 全局命令执行时没有 `MapContext`，原先未进入 `PersistentDialog`。袭击事件地点在打开窗口后销毁；冷加入时原生闭包也不保存该地点 ID。R194 复现原客户端在新加入点重载后窗口消失；R195 进一步发现 MP 拒绝反序列化 Tale 原生选项委托。
- 修复：`TaleSupplyDialog` 对确认属于 Tale 事件的窗口建立 MP 持久对话包装，选项仍按事件、旅队和回调身份派发；仅对白名单中的 Tale 原生委托放行 MP 反序列化。新增 `TaleWorldDialogState`，保存袭击窗口 ID 与已销毁事件地点 ID 的对应关系。源码版本升至 1.1.3。
- 三端 R197（候选 11 DLL）及 R200（正式精确 12 DLL）均 PASS：原客户端打开两阶段原生袭击窗口，主机建立新的加入点，第三端加入，随后两次选项点击在三端只生成同一遭遇地图；2 张初始玩家地图、异步时间、1200 共享 tick，无不同步及跨引用错误。正式证据：`BuildValidation/TaleNivarianResume_20260923/AuditR200/formal-exact-raid-join-verdict.json`。
- 正式精确 12 DLL 组合的四个世界事件对话 R201 PASS：接受、拒绝各分支在双端一致。证据：`BuildValidation/TaleNivarianResume_20260923/AuditR201/formal-exact-world-dialogs-verdict.json`。
- 正式精确组合的 R202 完整合并回归 PASS：双端各完成 3 次袭击、48 个补给分支、3 次原生模块安装、3 轮聊天树、4 个世界事件对话，并通过既有教会、SOS、供电、建造与搬运门槛；无本轮不同步或跨引用错误。证据：`BuildValidation/TaleNivarianResume_20260923/AuditR202/full-combined-formal-verdict.json`。
- R198 使用旧主补丁 DLL 且少一个正式炮台 DLL，在第 3 轮旧聊天树探针断言处中止；正式精确组合 R202 同参数通过该阶段并完整 PASS。R191/R196 的空引用属于夹具在存档重载后读取已销毁地点对象，已修正。R192/R193 是旧夹具前置断言，R195 是修复前委托序列化错误。
- 正式 `1.6/Assemblies/Meow.TaleNivarianCompatibility.dll` 已替换为已测 1.1.3，SHA256=`FB3FBBAB56A742C2E792076EA482FE04A7834210D54843106C6A46371C68C6EB`。旧正式 DLL SHA256=`BB4EA3FEA0157B939FD9C1C483C8AF73B68D458D76538106BEA31E71ACE9697D` 已备份在 `BuildValidation/TaleNivarianResume_20260923/CandidateR196WorldDialogDelegate/FormalBeforeR200/`。测试仅使用两个目标模组及所需 DLC、依赖与联机组件；窗口隐藏，全部游戏音量为 0。按用户要求只做功能短测，不做长期压力测试，也不追查偶发不同步。
