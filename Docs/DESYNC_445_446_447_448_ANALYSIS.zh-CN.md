# Desync-445 ~ 448 分析（2026-08-17）

## 结论

这四个包来自同一条连续会话，且基线本身无效：每次加入/重连前都存在
`Configs match: False`（XML Extensions 启动期配置不一致），客户端每次重连后还会出现
`Could not find think node with key -1937076157`，即至少有一个人物的存档 Job
在客户端无法解析 `jobGiver`。这会让两端的人物的思考树/工作选择不同，进而产生
单端开火、单端 JobID、单端 MessageID 等表象。

按“只补已验证的稳定边界”的要求，本次**不修改任何模拟代码**。稳定可行的方案是先
让两端配置一致（用 Multiplayer 原生 Fix and Restart），确认 `Configs match: True`
且不再出现 `Connecting anyway`，并确认客户端重连后不再出现缺 think node 的警告，
然后重新跑同场景；如果警告仍在，再针对 `Job.jobGiverKey`/ThinkTree save key 的
重连序列化单独补丁。

## 证据

冻结目录：`TestValidation/DesyncEvidence/2026-08-16-445-448-desync/`

| 包 | SHA-256 |
|---|---|
| Desync-445.zip | `3A0E00E87682F8DA6279BC65EF11BECDBD9907218D7F22A29A0DA11965575216` |
| Desync-446.zip | `7F64C010DF223CEF022954509FAF999EE78473B5F1E79993B5A8A23DACC6830E` |
| Desync-447.zip | `24A523008E09CD854716D7646B4586F73E01827B7912A01D2C5ADDF61D2F0655` |
| Desync-448.zip | `3076826E7942C221AC30ABF9C376A4156E41D5A2DE9ECD784A6C2D752056FAFC` |

版本：Multiplayer `0.11.5+a481546`，RimWorld `1.6.4850 rev646`，
async time + multifaction 开启，3 张地图，2 名玩家。

## 会话时间线

| 包 | 最后有效 tick | 掉线 tick | 类型 |
|---|---:|---:|---|
| 445 | 8016871 | 8016931 | Trace hashes don't match |
| 446 | 8020921 | 8020982 | Trace hashes don't match |
| 447 | 8020981 | 8021042 | Wrong random state on map 1 |
| 448 | 8025192 | 8025392 | Trace hashes don't match |

四个包的 `local_logs.txt` 是同一份累积日志，445/446 是 446 重连前的连续两次
trace-only 掉线；447 是 446 重连后的真实地图 Rand 掉线；448 是 447 重连后再次
trace-only 掉线。

## 各自最早分歧

### 445 / 448：PsychicRitualManager 消息 ID

最早分歧都是本地端在 `GameComponent_PsychicRitualManager.GameComponentTick` 中
额外执行了一次 `Messages.Message`，栈为：

```text
UniqueIDsManager.GetNextMessageID
  -> Verse.Message..ctor
  -> Messages.Message
  -> GameComponent_PsychicRitualManager.GameComponentTick
```

另一端在同一个共享 tick 没有这条 trace，直接进入 `Building_HoldingPlatform` 的
地图 Rand。由于报错是 `Trace hashes don't match` 而不是 `Wrong random state`，
世界/地图 Rand 状态序列仍然一致；分歧点是共享 MessageID 流（或一端 trace 被
Rand scope 抑制），不是已证明的 Rand 分叉。

JIT 证据显示两端都在 tick `8016925` 首次编译并执行了
`Dictionary.Remove from GameComponent_PsychicRitualManager.GameComponentTick`，
即冷却条目的移除是同一 tick 执行的，剩余分歧只可能是
`researchPrerequisite.IsFinished` 或消息 trace 采集差异，两者都无法在缺失 host
日志/干净基线时进一步定位。

### 446：Pawn_JobTracker 单端 JobID

最早分歧是 host 端在 vanilla `Pawn_JobTracker.EndCurrentJob` ->
`JobMaker.MakeJob(JobDef,int,bool)` 多分配了一个 JobID，执行者为
`Axolotl784652`；客户端没有这条 trace。根因在 trace 窗口之前，包内没有 host
日志，无法定位是哪一步先让两端的人物 Job 状态不同。客户端重连日志里反复出现的
缺 think node 警告正是一个能解释单端 Job 行为的已知污染源。

### 447：Milian Mechanoid 单端开火

最早分歧是本地端 `Milian_Mechanoid_PawnI2682354` 在
`Verb_LaunchProjectile.TryCastShot -> GenSpawn.Spawn` 里创建投射物，host 端同一
tick 仍在 `GasGrid.TryDiffuseGases`，随后地图 Rand 状态真实分叉。这同样发生在
重连后，且客户端在本次重连后（日志第 7755 行）仍报告缺 think node；因此单端开火
更可能是人物 Job/思考树状态先分叉后的下游结果，而不是一个独立的、已验证的
“射击入口未被同步”问题。

## 共同污染源

1. `Multiplayer: Mod mismatch window open (... Configs match: False)`，
   `imranfish.xmlextensions/XmlMod` 判定为 `RestartRequired`，随后
   `Multiplayer: Connecting anyway`。日志行：445/446 的第 1816-1819、4269-4272
   行；447/448 为同一累积日志，内容一致。
2. 客户端每次重连后 `Could not find think node with key -1937076157`
   （445/446 第 2158、6864、7705 行；447/448 还有第 7735、7755 行）。
   `Job.ExposeData` 的 `PostLoadInit` 无法用 `jobGiverThinkTree` 解析
   `lastJobGiverKey` 时会打印该警告，`jobGiver` 保持 null，人物后续换 Job、开火
   决策就可能两端不同。

## 为什么不打补丁

- 会话是在已知配置不一致 + 客户端 Job think node 解析失败的无效基线上运行的，
  按技能要求不能把这类包当作干净模拟缺陷来补。
- 445/448 只是 MessageID/trace 层面的差异，未证明是真正的模拟状态分叉；
  JIT 反而显示冷却移除两端同 tick 执行。
- 446/447 的单端 Job/开火位于 trace 窗口之前，缺少 host 日志和干净重连基线，
  没有可验证的稳定执行边界可补。直接包 `EndCurrentJob` 或 `TryCastShot` 会掩盖
  真正的 Job 状态漂移并改变 vanilla 语义。

## 稳定可行方案

1. 先解决 `imranfish.xmlextensions/XmlMod` 的配置差异：使用 Multiplayer 原生
   Fix and Restart，确保 `Configs match: True`，不再出现 `Connecting anyway`。
2. 用配置一致的全新会话重跑 445-448 场景，确认客户端不再打印
   `Could not find think node with key -1937076157`。
3. 若警告消失且 445-448 不再复现，则无需代码补丁。
4. 若配置一致后警告仍在，则这是重连快照中 `Job.jobGiverKey` 与 ThinkTree save
   key 的序列化问题，下一步才针对该边界做最小补丁（例如加载后按稳定 key 重建
   `jobGiver`，或在快照前清掉不可解析的 `jobGiverKey`）。

本次仅新增本分析文档，未修改 `Source/` 下任何补丁代码，未部署 DLL，未运行
host/client 测试。
