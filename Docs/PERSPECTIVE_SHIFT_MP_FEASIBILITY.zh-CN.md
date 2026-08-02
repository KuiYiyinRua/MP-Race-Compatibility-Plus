# Perspective Shift 多人兼容可行性与实施方案

## 1. 结论

目标模组：

- 名称：Perspective Shift
- Package ID：`ferny.PerspectiveShift`
- Workshop ID：`3686618980`
- 作者：`ferny`
- RimWorld：`1.6`
- 本地源码提交：`2202db5d1b6a3fb837e0c1bb2cf2d90e5c2378b7`
- 已安装程序集：`PerspectiveShift.dll`
- 程序集 SHA-256：`85A9C0A9ACF1238DFFD0E032A0EF1A79345F574260D75CA53243D46F0C2A737E`

结论分为两层：

1. **逐帧复刻原模组的连续移动手感不可取。** 原实现从 Unity 本地输入和
   `Time.deltaTime` 直接计算位置，然后写入 `Pawn.Position`。若保持此算法，就必须实现输入预测、
   回滚和位置校正，复杂度与风险都不适合作为兼容补丁。
2. **主要玩法可以通过“全局唯一 Avatar + 工作驱动移动适配器”落地。**
   WASD 不再直接写坐标，而是发送低频同步移动意图，在同步命令中生成短程 `Goto` 工作，
   由 RimWorld 和 Multiplayer 已有的确定性寻路、工作和命令队列执行。

推荐兼容语义是：一局游戏仍只有一个 Perspective Shift Avatar，与原模组的单例模型一致；
该 Avatar 同一时间由一名 Multiplayer 玩家持有控制权，其他玩家仍可正常使用导演模式。
控制权可以同步转让或强制释放。

在此语义下，整体可行性评估为 **中等偏高**。建议先完成移动、控制权、战斗和常用工作交互，
储物拖拽界面放在后续阶段。若第一阶段无法通过双端 10,000 tick 验证，应立即停止，
不进入储物事务和社交对话改造。

## 2. 已验证的技术事实

### 2.1 目标模组的风险面

静态审计结果：

- 67 个源码文件读取 `State.Avatar`、`State.IsActive` 或 `IsAvatar()`。
- 2 处直接写 `Pawn.Position`。
- 14 处调用 `TryTakeOrderedJob`。
- 1 处直接调用 `StartJob`。
- 31 处直接拆分、生成、销毁、携带或转移物品。
- 1 处显式 `Rand` 调用，位于主动聊天选项。

核心风险：

- `Avatar_Movement.ProcessMovement` 使用 `Time.deltaTime`、本地键盘状态和本地帧率直接写
  `Pawn.Position`，并调用 `Notify_Teleported`。
- `Avatar_Combat` 根据本地鼠标位置直接进行近战或调用 `Verb.TryStartCastOn`。
- `State.SetAvatar` 会结束当前工作、停止寻路、创建等待工作和移除 Lord 成员关系。
- `Dialog_StorageMenu` 用窗口私有的 `cursorItem` 直接拆分和转移真实物品。
- `State.Avatar` 是静态单例，并被大量模拟逻辑补丁读取。因此让每个客户端拥有不同 Avatar
  会立即产生模拟分支。

### 2.2 Multiplayer 已有覆盖

本地 `Multiplayer-master` 证明以下路径已有同步：

- `Pawn_JobTracker.TryTakeOrderedJob` 已注册同步，并暴露 `Job` 参数。
- `Pawn_JobTracker.TryTakeOrderedJobPrioritizedWork` 已注册同步。
- `Verb.TryStartCastOn(LocalTargetInfo, bool, bool, bool, bool)` 已注册同步。
- 同步命令携带发令玩家、Faction 和 Map，并在统一 tick 的命令队列中执行。
- `SyncMethod` 支持 Map/选择/按键上下文、参数变换、前后回调和取消条件。
- `IPlayerInfo.Id`、`Username` 和当前地图可通过公开 API 查询。
- 在线用户名由服务器拒绝重复，可用用户名作为可重连的 Avatar 所有权键。

因此，不应重复同步普通有序工作和标准远程攻击。兼容补丁只需要覆盖目标模组绕过这些入口的路径。

## 3. 推荐架构

### 3.1 全局唯一 Avatar 租约

新增不依赖 Perspective Shift 编译期类型的 `GameComponent_MpPerspectiveShift`，保存：

- `Pawn controlledPawn`
- `string ownerUsername`
- `int ownershipEpoch`
- `int lastLeaseHeartbeatTick`
- `sbyte moveX`、`sbyte moveZ`
- `bool sprint`、`bool walk`
- `int activeMoveJobId`
- 当前兼容阶段和诊断状态

该组件使用 Verse 的 `Scribe` 保存。只保存稳定引用和整数，不保存窗口、Delegate、鼠标位置或
Unity 对象。

同步方法：

```text
SyncClaimAvatar(username, pawn, requestedMode)
SyncReleaseAvatar(username, ownershipEpoch)
SyncTransferAvatar(oldOwner, newOwner, pawn, ownershipEpoch)
SyncLeaseHeartbeat(username, ownershipEpoch)
```

所有方法在回放时再次校验用户名、Epoch、Pawn 存活状态、Faction 和 Map。

`SyncClaimAvatar` 在同步命令中调用目标模组的 `State.SetAvatar`，使 `State.Avatar` 在所有 peer
上保持完全相同。目标模组的 `IsAvatar()`、AI 抑制和需要处理因而不会因本地玩家不同而分叉。

本地输入与镜头则额外检查：

```text
MP.PlayerName == component.ownerUsername
```

只有所有者执行 Perspective Shift 的镜头、准星、键盘和鼠标界面代码。非所有者继续使用普通
RimWorld UI，但仍在模拟层看到同一个受控 Pawn。

租约每 300 个共享 tick 发送一次心跳，600 tick 未更新时允许其他玩家接管。暂停状态下可通过
同步的“强制释放”Gizmo 解除，不依赖本地玩家列表变化时间。

### 3.2 工作驱动的 WASD 移动

在 Multiplayer 中始终阻止 `Avatar.UpdatePhysics` 原有的直接位置写入。

所有者本地只采样输入，量化为 8 个方向和停止：

```text
(-1,-1) (-1,0) (-1,1)
( 0,-1)  STOP   ( 0,1)
( 1,-1) ( 1,0)  ( 1,1)
```

方向、疾跑或潜行变化时发送：

```text
SyncSetMoveIntent(username, pawn, epoch, moveX, moveZ, sprint, walk)
```

同步执行器：

1. 验证所有权。
2. 将量化输入写入共享组件以及所有 peer 相同的 `State.Avatar` 字段。
3. 从 Pawn 当前格沿方向寻找 6–12 格内最远可达格。
4. 创建 `JobDefOf.Goto`，设置 `playerForced = true`。
5. 在同步命令内部调用 `TryTakeOrderedJob`。
6. 记录生成后的 Job ID，供停止命令精确识别。

按键释放时发送：

```text
SyncStopMove(username, pawn, epoch, activeMoveJobId)
```

仅当当前 Job ID 与适配器记录一致时才结束工作，避免误取消玩家刚下达的其他工作。

持续按住方向时，在距目标 2 格以内或每 90 tick 发送下一段移动意图。方向未变化时不逐帧发包。
路径、门、地形和碰撞全部交给原版确定性寻路处理。

疾跑和潜行仍写入所有 peer 相同的 Avatar 字段，让原模组现有的移动费用、休息和食物消耗补丁
继续工作。关键 Perspective Shift 设置必须依赖 Multiplayer 原生配置匹配流程；启动诊断同时
输出这些设置的紧凑哈希。

这种实现与单机原版的差异是移动按格寻路，不是连续物理滑动；但不会需要预测或回滚，网络延迟
也只影响输入开始和转向，不会影响确定性。

### 3.3 战斗

远程攻击：

- Multiplayer 已同步 `Verb.TryStartCastOn`。
- 为了验证所有权并避免按住鼠标每帧发送命令，补丁应阻止原始手动射击路径，在本地检测到
  “目标变化”或 Verb 再次可用时调用自有的 `SyncFire`。
- `SyncFire` 参数只包含 Pawn、`LocalTargetInfo`、武器 Thing ID、Verb 索引和 Epoch。
- 真正的 `TryStartCastOn` 在同步命令内部执行，随机数也因此位于同步区间。

近战攻击：

```text
SyncMeleeAttack(username, pawn, targetThing, epoch)
```

同步执行器验证距离和敌对关系后调用 `pawn.meleeVerbs.TryMeleeAttack`。不要直接注册所有
`TryMeleeAttack` 调用，以免影响其他模组在 tick 中发起的正常攻击。

旋转和准星：

- 准星、鼠标位置和屏幕特效保持本地。
- 会影响命中方向或姿态的 `Pawn.Rotation` 只能在同步攻击或同步移动命令中更新。
- `aimAngle` 只用于绘制时可以保持本地，但不得被模拟逻辑读取。

### 3.4 普通工作与左键交互

目标模组产生的绝大多数工作最终进入 `TryTakeOrderedJob`，已有 Multiplayer 原生覆盖。
第一阶段只需确认真实 UI 链路确实到达该注册方法，避免重复注册。

直接模拟变更必须单独处理：

- `TryStartCarry`
- `TryDropCarriedThing`
- 炮弹装填
- 直接加油
- 床上放置 Pawn
- 机械充电器转移
- 容器转移
- Bill 材料直接放置
- 直接修改 Storage ThingFilter

第一阶段的安全策略：

- 阻止这些本地直接变更。
- 给出一次性的“该直接操作尚未启用，请使用普通右键工作”提示。
- 仍允许会生成标准 Job 的交互。

第二阶段再为高频路径提供窄的同步事务方法。每个事务都从 Pawn、目标 Thing、目标 Cell 和数量
重新验证当前状态；不序列化窗口的 `cursorItem` 或闭包。

### 3.5 主动社交与强制结果对话

主动聊天的随机选择必须移动到同步命令中：

```text
SyncSocialInteraction(username, initiator, recipient, requestedKind, epoch)
```

当 `requestedKind` 为聊天时，在同步命令内执行 `Rand.Chance` 并选择 Chitchat/DeepTalk。
冷却字典也只在同步命令成功后写入。

求婚和示爱结果对话：

- 模拟 tick 只为 Avatar 所有者打开本地选择窗口。
- 所有者提交 `SyncForcedInteractionResult`。
- 同步执行器临时设置 `forcedInteraction` 和 `skipDialog`，执行完整互动，然后在 `finally`
  中恢复静态字段。
- 需要注册 Multiplayer pause lock，避免选择窗口存在期间共享模拟继续前进。
- 窗口关闭、声音和消息保持本地；关系、记忆、信件和随机数在同步命令中产生。

### 3.6 建筑限制与 Seek-at-will

`CompPlayerOnly.mode` 和 `State.seekAtWillPawns` 都影响模拟，不能由 Gizmo 闭包直接修改。

新增：

```text
SyncSetPlayerOnlyMode(List<Thing> parents, int mode)
SyncSetSeekAtWill(Pawn pawn, bool enabled)
```

同步执行器通过父 Thing 重新取得 Comp。父 Thing 列表按 `thingIDNumber` 排序后执行，避免多选
顺序差异。切换 Store/Use 时的 `WideFilter` 也必须在同一命令中执行。

### 3.7 储物窗口

原窗口的 `cursorItem` 是最危险的路径，不应尝试同步窗口实例。

推荐最终设计是“事务式储物会话”：

- 本地窗口只显示快照和操作意图。
- 共享组件维护 `MpStorageSession`，键为所有者用户名。
- 会话持有 Storage、Pawn、Epoch 和一个专用 `ThingOwner` 临时容器。
- 拾取、放置、拆分、合并、装备、穿戴、收入库存和开始携带均为独立同步命令。
- 每次命令重新验证 Thing ID、stackCount、Slot、过滤器、距离和所有权。
- 关闭窗口或租约失效时，同步地把临时物品放回原 Storage；失败时放到 Pawn 附近。
- `CompStorageSlotOrder` 的 `SetItemSlot`、`AddGap` 和 `RemoveGap` 与真实物品事务在同一命令中执行。

该阶段工作量最大，应放在移动、战斗和普通工作验证通过之后。若不实施此阶段，兼容模式应禁用
自定义储物窗口并引导玩家使用原版 Storage UI，不能保留会单边修改物品的窗口。

## 4. 代码组织

建议新增：

```text
Source/MP_MeowOnlineShop/
  Patch_PerspectiveShiftMp.cs
  PerspectiveShiftMpState.cs
  PerspectiveShiftMpMovement.cs
  PerspectiveShiftMpStorageSession.cs       # 第三阶段
Docs/
  PERSPECTIVE_SHIFT_MP_TEST.md
```

`Patch_PerspectiveShiftMp` 只通过 `AccessTools.TypeByName` 和精确签名解析目标类型，避免让
Perspective Shift 变成补丁程序集的硬依赖。

在现有 `MpMeowOnlineShopBootstrap.ApplyPatch` 中调用：

```text
Patch_PerspectiveShiftMp.Apply(Harmony)
```

应用条件：

```text
ModsConfig.IsActive("ferny.PerspectiveShift")
```

`About.xml` 的 `loadAfter` 增加：

```xml
<li>ferny.PerspectiveShift</li>
```

启动时一次性输出：

- 目标 Assembly 版本和 SHA-256（运行时可只输出版本，构建验证记录文件哈希）。
- 每个必需类型、字段和方法是否精确解析。
- 已注册同步方法数量。
- Perspective Shift 关键设置哈希。
- 当前启用的兼容阶段。

任一必需移动或所有权目标缺失时 fail closed：禁用 Perspective Shift 的 Multiplayer 输入路径，
显示明确错误，不允许退回原本的直接坐标写入。

## 5. 分阶段实施和验收门槛

### 阶段 0：解析与只读诊断

内容：

- 包 ID 和程序集验证。
- 解析 `State`、`Avatar`、`SetAvatar`、`UpdatePhysics`、`OnGUI`、`TryTakeOrderedJob` 等目标。
- 输出设置哈希和目标解析摘要。

门槛：

- `dotnet build` 零错误。
- 安装目标缺失时补丁安全跳过。
- 目标存在时所有 required target 精确解析。

预计：0.5–1 个工作日。

### 阶段 1：可玩的最小闭环

内容：

- 全局唯一 Avatar 租约。
- 禁止原始逐帧位置写入。
- 工作驱动 WASD 移动和停止。
- 远程、近战同步。
- 普通 `TryTakeOrderedJob` 交互。
- 同步 PlayerOnly 和 Seek-at-will。
- 暂时禁用自定义储物窗口和未覆盖的直接物品操作。

门槛：

- 非主机玩家取得、释放和重新取得控制至少 3 次。
- WASD 八方向、停止、转向、门、不可通行格各重复 3 次。
- 远程和近战各重复 3 次。
- 主机和客户端 Pawn 的 Position、Job ID、Drafted、Rotation、Verb 状态一致。
- 10,000 shared ticks 无命令失败、随机状态错误或 desync。

预计：2–4 个工作日。

**停止条件：** 如果 Goto 适配器无法提供连续可用的控制体验，或 State 单例仍在非 UI 路径产生
客户端分支，就停止项目，不进入后续阶段。

### 阶段 2：社交和直接交互事务

内容：

- 主动聊天/辱骂。
- 强制互动结果对话及 pause lock。
- 拾取、丢下、加油、装填和常用容器事务。
- 重开窗口、取消和失效目标处理。

门槛：

- 所有动作由非主机发起并重复 3 次。
- 关系、记忆、库存、携带物、目标 stackCount、信件数量一致。
- 10,000 shared ticks 定向 smoke 通过。

预计：2–4 个工作日。

### 阶段 3：事务式储物窗口

内容：

- `MpStorageSession`。
- 拆分、合并、交换、放回、装备、穿戴、库存和携带。
- 关闭、断线、Pawn 死亡、Storage 销毁和地图切换恢复。

门槛：

- 首次打开、关闭、第二次打开、取消和确认。
- 左/右键拆分、跨 Slot 交换、过滤器拒绝、满库存和超重。
- 会话中断后无丢失、复制或悬空 Thing。
- 双地图情况下只修改目标 Map。

预计：3–5 个工作日。

### 阶段 4：发布验证

内容：

- 精确 loadout 的 host/client 启动和加入。
- 目标动作短 smoke。
- 120,000 shared tick 正常设置 soak。
- 记录最终 DLL SHA-256，并确认发布 DLL 与测试归档一致。

预计：1–2 个工作日，不含问题修复。

## 6. 总体可行性与风险

| 功能 | 方案 | 可行性 | 风险 |
|---|---|---:|---:|
| 单一 Avatar 所有权 | 同步租约 + 用户名 | 高 | 低 |
| WASD 移动 | 短程 Goto 工作适配 | 中高 | 中 |
| 原样连续物理移动 | 输入预测/回滚 | 低 | 极高 |
| 普通工作交互 | 复用 TryTakeOrderedJob 同步 | 高 | 低 |
| 远程攻击 | 复用 Verb.TryStartCastOn | 高 | 中低 |
| 近战攻击 | 窄同步包装器 | 高 | 中低 |
| 主动社交 | 在同步命令内随机 | 高 | 中 |
| PlayerOnly/SeekAtWill | 同步稳定父 Thing/Pawn | 高 | 低 |
| 直接物品操作 | 独立事务命令 | 中 | 中高 |
| 自定义储物窗口 | 共享事务会话 | 中 | 高 |
| 每位玩家各自一个 Avatar | 改写 67 个状态依赖文件 | 低 | 极高 |

推荐完整路线的预计工作量为 **8–16 个工作日**，其中阶段 1 是决定项目是否继续的关键验证点。
不建议一次性实现全部阶段。

## 7. 明确不采用的方案

- 不同步 `Window`、闭包、Delegate、`Event.current` 或私有 `cursorItem`。
- 不逐帧发送 Pawn 坐标。
- 不在客户端本地执行后再做部分回滚。
- 不通过固定 Rand 种子掩盖未同步的 UI 行为。
- 不让不同 peer 的 `State.Avatar` 指向不同 Pawn。
- 不对所有 `StartJob`、`TryMeleeAttack` 或物品方法做全局粗暴同步。
- 不在目标解析失败时继续运行原模组的直接位置写入路径。

## 8. 建议决策

可以进入实施，但只批准阶段 0 和阶段 1。阶段 1 通过双端定向 smoke 后，再决定是否投入社交、
直接物品事务和储物窗口。

如果接受“移动改为短程寻路、全局一次只允许一名玩家控制 Avatar”这两个兼容语义，方案可落地。
如果要求完全保留逐帧连续移动，或者要求每位玩家同时拥有独立 Avatar，则不建议在当前兼容补丁
中实施，应改为 Perspective Shift 上游源码级多人重构。

## 9. 多玩家独立 Avatar 与主机权威扩展评估

### 9.1 每名玩家同时控制不同 Avatar

**可以真实实现，但不应继续使用目标模组当前的 `State.Avatar` 单例作为模拟状态。**

需要将状态拆成两层：

```text
本地显示层
  LocalViewAvatar
  - 当前客户端的镜头目标
  - 本地键盘、鼠标、准星和预测绘制位置
  - 不参与保存、AI、寻路、战斗或随机数

共享模拟层
  ControlledPawnRegistry
  - ownerUsername -> Pawn
  - 每个 Pawn 的移动意图、控制 Epoch 和最后确认 tick
  - 在所有 peer 上完全一致并参与保存
```

原来的：

```text
pawn.IsAvatar() == State.Avatar.pawn == pawn
```

必须在模拟路径中改为：

```text
ControlledPawnRegistry.Contains(pawn)
```

而镜头和 UI 路径改为：

```text
LocalViewAvatar.pawn == pawn
```

这是必要条件。若不同客户端继续让 `State.Avatar` 指向各自的 Pawn，目标模组的 AI、工作、
Need、战斗和储物补丁会对同一个世界执行不同分支。

当前源码有 67 个文件读取 Avatar 单例状态。仅靠兼容 DLL 可以用 Harmony 前缀和 transpiler
逐项替换，但升级脆弱且难以证明完整。更可靠的落地方式是维护一个很小的 Perspective Shift
源码分支，将 `State.Avatar` 拆成 `LocalViewAvatar` 和 `ControlledPawnRegistry`，兼容补丁负责
Multiplayer 命令注册、所有权和验证。

多 Avatar 推荐的数据结构：

```text
Dictionary<string, ControlledAvatarState>

ControlledAvatarState:
  Pawn pawn
  int epoch
  sbyte moveX
  sbyte moveZ
  bool sprint
  bool walk
  int activeMoveJobId
  int lastAcceptedInputTick
```

同一个 Pawn 只能绑定一个用户名；同一个用户名只能绑定一个 Pawn。所有列表按用户名和
`thingIDNumber` 排序后 tick，避免字典迭代顺序影响模拟。

### 9.2 “真实 Pawn 位置只大概同步”不可行

RimWorld Multiplayer 是确定性锁步，而不是服务器保存唯一世界、客户端接收实体快照的传统
主机权威模型。Pawn 的实际位置会立即影响：

- 寻路、门和碰撞；
- 射程、视线、掩体和近战距离；
- Reservation、工作目标和区域判定；
- Projectile 命中和爆炸范围；
- Pawn tick 顺序及其后续随机数调用；
- 搬运、容器、床、充电器和地图边缘退出。

因此，即使位置只差一格，也可能在几 tick 后变成不同的工作、伤害、随机状态或 Thing 集合。
不能让主机和客户端各自保存“大概一致”的 `Pawn.Position`。

可以近似的只有显示：

```text
authoritative Pawn.Position    所有 peer 完全一致，参与模拟
predictedDrawPosition          仅本地存在，用于自己控制时的流畅绘制
interpolatedRemoteDrawPosition 仅本地存在，用于显示其他玩家角色
```

目标 Pawn 的 `DrawPos`、Tweener 或 Renderer 可以读取预测位置；所有游戏规则仍读取严格一致的
`Pawn.Position`。收到下一条权威移动结果后，绘制位置在 100–200 ms 内平滑收敛，不能直接把预测
值写回 Pawn。

### 9.3 在现有 Multiplayer 上能做到的“主机权威”

现有 Multiplayer 服务器主要负责排序和广播命令。普通 `SyncMethod` 由客户端发送后，会在所有
peer 的同一 tick 回放；服务器不会先运行 RimWorld 模拟再广播实体快照。

因此有两个层级：

#### A. 服务器排序的锁步输入（推荐）

```text
客户端采样输入
  -> 发送带 owner、Pawn、Epoch 的同步命令
  -> Multiplayer 服务器排序
  -> 所有 peer 同 tick 验证并执行
```

这不是传统意义上的“只有主机计算世界”，但服务器决定命令顺序，真实状态严格一致。配合本地
预测绘制，可以获得接近主机权威动作游戏的手感，也是无需修改 Multiplayer 网络协议的最稳方案。

#### B. 两阶段主机批准（可做，但需额外工程）

```text
客户端 RequestInput
  -> 所有 peer 收到请求，但不修改模拟
  -> 主机在下一个界面帧发送 HostApplyInput
  -> HostApplyInput 标记为 host-only
  -> 所有 peer 执行批准后的输入
```

Multiplayer 服务器会拒绝非主机发送的 host-only SyncMethod，因此第二阶段可以保证只有主机提交
最终状态变更。

但公开 API 没有向兼容模组暴露“当前正在回放的命令由哪个 playerId 发出”。若需要防止客户端
伪造其他用户名，而不只是合作式校验，则必须：

1. 给 Multiplayer 增加只读的 `CurrentExecutingCommandPlayerId` API；或
2. 使用私有反射/自定义 Multiplayer 构建；或
3. 增加独立网络包和服务器处理器。

推荐选择第 1 项并向 Multiplayer 上游提交小型 API 扩展。没有该扩展时可以做合作式所有权检查，
但不能声称具备抗恶意客户端的服务器权威安全性。

两阶段批准会额外增加一轮网络延迟。必须配合 `predictedDrawPosition`，否则非主机的 WASD 会明显
迟滞。

### 9.4 推荐的多 Avatar 最终结构

```text
Client A local input ─┐
Client B local input ─┼─> ordered input/request commands
Client C local input ─┘
                              |
                              v
                  host validation (optional)
                              |
                              v
               authoritative shared command queue
                              |
              +---------------+---------------+
              v               v               v
           Host sim       Client A sim     Client B sim
           exact Pawn     exact Pawn       exact Pawn
           positions      positions        positions
              |               |               |
              v               v               v
           local render    predicted own    interpolated
                           avatar render     remote render
```

真实 Pawn、工作、战斗和物品状态在三端仍完全一致；不同的只有视觉插值。

### 9.5 修订后的可行性

| 目标 | 可行性 | 建议 |
|---|---:|---|
| 每名玩家同时控制不同 Pawn | 中 | 需要拆分本地视图和共享注册表 |
| 多 Avatar + Goto 工作移动 | 中高 | 最先验证 |
| 多 Avatar + 确定性固定 tick 连续移动 | 中低 | 仅在 Goto 手感失败后考虑 |
| 本地视觉预测/远端插值 | 高 | 不得写入 Pawn 状态 |
| 合作式“两阶段主机批准” | 中 | 可由兼容补丁实现 |
| 抗恶意客户端的主机权威 | 中低 | 需要 Multiplayer API/服务器扩展 |
| 各端实际 Pawn 位置只大概一致 | 不可接受 | 会破坏锁步模拟 |

推荐先制作一个仅包含两个 Pawn、两个玩家、八方向 Goto 移动和本地预测绘制的技术原型。
原型不包含储物、社交或复杂交互。若两个非主机/主机玩家可同时控制不同 Pawn，并在 10,000
shared ticks 后保持 Position、Job、Rand 和 trace 一致，再扩展战斗和交互。

## 10. 3.0.31 实施记录

当前补丁已实现第一版可运行架构：

- `PerspectiveShiftMpComponent` 保存 `owner -> Pawn` 的共享所有权、epoch、移动意图、活动移动 Job 和 Avatar 私有状态；
- `State.Avatar` 仅作为各客户端自己的视角/UI 指针，`State.IsAvatar` 在模拟层改为查询共享注册表；
- 认领、释放、WASD 意图和地图点击均通过 Multiplayer `SyncMethod` 排序；
- WASD 转换为短距离 `Goto` Job，停止命令只中断该 Avatar 自己记录的移动 Job；
- 每 45 shared ticks 发送移动心跳，120 ticks 无心跳自动清零，避免断线后持续移动；
- `physicsPosition` 只用于本地绘制预测，最大领先真实 Pawn 1.25 格，并平滑收敛；
- 禁用了目标模组中会把本地预测坐标带入 `CellFinder` 的补丁；
- 将 Job 完成回调切换到对应受控 Pawn 的运行时 Avatar，避免远端 Pawn 错用本地单例；
- 存档共享每个 Avatar 的 Lord、门交互、休息状态、待拾取物和需求提示状态；
- 禁用了依赖各机本地设置并会改变模拟结果的冲刺需求消耗、瞄准延迟、自动拾取和若干单例补丁；
- 将倾斜和装备额外绘制等目标补丁限制为本地 Avatar，避免远端/旁观视角读取本地空单例。
- 多格储物建筑在联机中不再打开持有私有 `cursorItem` 的本地拖放窗口；改为确定性直接存入/取出，
  格位按 `(x,z)`、物品按 `thingIDNumber` 排序。单格储物、蓝图安装、拆除和卸载仍沿用原行为。

已完成的验证：

- Release 编译：0 error、0 warning；
- 最小模组列表真实启动；
- 游戏日志确认加载 `3.0.31.0 / 3.0.31-perspective-shift-mp-r3`；
- 游戏日志确认 Perspective Shift MP 注册完成，未出现 Harmony/目标解析异常。
- 两个隔离 peer 完成 3,000 shared-tick Gate A：双方均为两名玩家、单地图、`desynced=False`。
- 最终动作级测试由非主机驱动两名玩家分别认领 Pawn 462/465，并完成三轮同时移动/停止、
  六次近战、六次远程攻击、六轮多格储物存取/打包及六轮 `TransportPod` 内部容器转移；
- 主机和客户端均记录 Pawn 462 从 `(127,0,117)` 到 `(113,0,124)`、Pawn 465 从
  `(132,0,118)` 到 `(113,0,128)`；`movementPasses=3`、`meleePasses=6`、
  `rangedPasses=6`、`storagePasses=6`、`containerPasses=6`，全部 PASS；
- 最终动作测试运行至客户端 10,000 shared ticks：客户端 `desynced=False`、实测 56.60 TPS，
  主机在客户端正常关闭后以 `peerClosedAtTerminal=True` 完成；
- 最终发布 DLL 与候选归档 SHA-256 均为
  `A022F1D14FE6375883394381508A437FE70C821EE954775E93ADE44C8EE7F608`；
- 最终双端日志未出现根级异常、`NullReferenceException`、同步错误、命令失败或 desync，发布目录中的测试 harness 已移除。

尚未完成的验证：

- 真实键盘输入与本地预测观感（自动测试直接调用了同一同步执行器）；
- 冷重连、多地图和 120,000 shared-tick 长时间 soak。

因此 3.0.31 当前应标记为“已通过双端多 Avatar 认领、移动/停止、近战、远程攻击、
确定性储物与复杂容器操作及 10,000 shared-tick 动作级 smoke；冷重连、多地图与
120,000 shared-tick 长 soak 未执行”。多格储物的原始拖放窗口不是联机共享事务，
正式版采用已验证的直接存取降级语义。

## 11. 3.0.61 本地控制归属修复

手工联机反馈确认旧实现存在本地体验错误：Avatar 的 `SetAvatar/ClearAvatar` 若由另一个已同步
Gizmo 命令间接调用，回放阶段读取的是每台机器自己的 `MP.PlayerName`，而不是该命令的真实发起者。
这会让主机与客户端对本地视角归属作出不同判断，并使 WASD 找不到本机 owner 对应的共享状态。

3.0.61 在 Multiplayer 的地图/世界命令执行边界读取 `ScheduledCommand.playerId`，再从当前会话的
玩家表解析稳定用户名。同步回放中的认领和释放统一使用该发起者；无法解析时直接拒绝动作，不再
回退到主机或当前机器用户名。`State.Avatar` 仍只在 owner 与本机用户名一致时绑定。

本地绑定 Avatar 时同时清除 `State.CameraLockPosition` 并使 `State.IsActive` 帧缓存失效，避免
继承旧镜头锁点以及首帧输入路径继续判定为未激活。

Release 编译为 0 error、0 warning，DLL SHA-256 为
`7B12D356F2BCB9AD1C7A14A138B5BCA5DD0EC3AB3ABF89D282B40B253EFBDF54`。
本次遵照停止测试要求，没有启动新的双端运行测试；主机/客户端分别认领、镜头跟随和 WASD
仍需在实际联机中复核后才能标记为运行验证通过。

## 12. 3.0.62 镜头锁点与 WASD 输入修复

进一步手工反馈表明 owner 修复后，Multiplayer 在同步命令结束时恢复地图/相机上下文，会调用
`CameraDriver.PanToMapLoc/JumpToCurrentMapLoc`。Perspective Shift 将这种内部相机恢复误判成玩家
主动锁定镜头，并重新写入 `CameraLockPosition`。

3.0.62 在同步命令回放期间跳过上述两个镜头锁点回调；玩家真实的本地镜头操作仍保留原行为。
WASD 采样不再直接使用包含 Multiplayer 常驻 UI 焦点的 `State.ControlsFrozen`，而只拦截世界视图、
搜索框焦点和不可中断 Job。检测到移动键时会清除遗留镜头锁点，并立即结束认领阶段生成的
`Wait/Wait_Combat` Job，再派发确定性的 `Goto` Job。

同时修正地图命令执行器的运行时类型名为 `Multiplayer.Client.AsyncTimeComp`，使地图和世界两类
同步命令都能解析真实发起者。Release 编译为 0 error、0 warning，DLL SHA-256 为
`DE03DD65FADDD434A99F7458E737E7C4C5BDD41BDC0F96E18E67FE638E8FB839`。本轮未启动自动双端测试。

## 13. 3.0.97 Perspective Shift 事件输入与镜头跟随修复

2026-08-02 的本地运行日志显示，客户端已正确完成本地 Avatar 绑定（owner、Pawn 与地图均可解析），
但整段会话没有出现一次“local movement input accepted”。同时鼠标右键下达的原生移动 Job 可以正常执行，
因此故障边界位于 Perspective Shift 的本地键盘采样，而不是 owner 归属或同步移动执行器。

3.0.97 在 `PerspectiveShift.State.OnGUI` 的前缀中直接捕获 Perspective Shift 自身绑定的
KeyDown/KeyUp 事件，维护本机的前进、后退、左右移动、冲刺与步行按键状态。物理更新继续通过既有的
有序同步移动命令派发意图，并保留 `KeyBindingDef.IsDown` 作为兼容回退；失去窗口焦点、解除认领或切换
本地 Avatar 时会清空按键状态，避免粘键。

镜头方面，只要本地 Avatar 存在移动输入或其原生 `Pawn_PathFollower` 正在移动，就清除
`State.CameraLockPosition`。这同时覆盖 WASD 移动和鼠标右键移动，使 Perspective Shift 按其原有
`physicsPosition/Pawn.Position` 路径持续跟随角色，而不修改共享 Pawn 坐标或远端模拟状态。

正式 DLL 已部署到 `1.6/Assemblies`，文件版本为 `3.0.97.0`，产品版本为
`3.0.97-perspective-shift-event-input-camera-follow`，SHA-256 为
`1333086988C8D65D5C63BD86D8B8B522819488EAFA0D84CEF78BAE7F58FA4AD3`。
按发布要求，本轮终止游戏后未重新启动，也未执行自动或双端运行测试；WASD、镜头跟随及主机/客户端
分别认领仍需使用同一正式包进行一次手工联机复核。

## 14. 3.0.98 本地 Avatar 视图恢复候选

2026-08-02 03:25 开始的双进程手工会话实际加载了 3.0.97。当前 `Player.log` 证明
`Player2075 -> Pawn 500` 已进入共享认领表，而且 `(1,0)` 与 `(0,-1)` 两次键盘输入均被接受；日志中
没有对应的共享释放、同步错误或 desync。结合“本地立即退回导演视角、其他端仍提示已被控制”的现象，
故障边界确定为进程本地 `PerspectiveShift.State.Avatar` 指针丢失，而共享 owner 记录仍然有效。

3.0.98 候选在 `State.Update`、`State.Tick` 与 `State.OnGUI` 进入前检查本机用户名对应的共享认领；
若共享 owner 仍存在但静态 Avatar 为空或指向错误对象，就重新绑定同一 `runtimeAvatar`、清除镜头锁点并
使 `State.IsActive` 帧缓存失效。同步地图点击、受控 Avatar Tick 和 Job 回调使用临时 Avatar 上下文后，
也以本机共享 owner 为恢复依据。只有共享 Release 真正移除 owner 后，本地视图才允许保持为空。

候选编译版本为 `3.0.98.0 / 3.0.98-perspective-shift-local-avatar-recovery`，中间产物 SHA-256 为
`391061902D20D4C32825C8F687F8C080EF3C99A7F59FC642205480C8355F28DB`。分析时两个用户游戏进程仍在
运行，正式目录继续保持 3.0.97（SHA-256
`1333086988C8D65D5C63BD86D8B8B522819488EAFA0D84CEF78BAE7F58FA4AD3`），`About.xml` 也保持
3.0.97，避免运行中的版本与磁盘元数据不一致。该候选尚未部署或运行验证。
