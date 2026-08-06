# Desync-262 ~ 273 分析与修复（2026-08-06）

## 证据

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-06-262-273-meow-online-shop/`
- 每个 zip 均先复制到冻结目录并校验 SHA-256，再只从冻结副本解压。
- 原始包 SHA-256：

| 包 | SHA-256 |
|---|---|
| Desync-262 | `4E8C4BC5E8F7704C159983C75CBE14C13A0345B93AF92A60A6034CAA1A1222CA` |
| Desync-263 | `4B550BC90C9B12A82D93481C13AAC1B5014715F497F350490785ED77EDF2AC83` |
| Desync-265 | `0525B1C4C72D006FFADF509FC971B9B923BD6A771E8C40C157D1B3EE5E03829C` |
| Desync-266 | `3F03B54C53410F63C5A77A9013E2ED956CCB5E9E39E5CCC7636EDBE0694F8467` |
| Desync-267 | `CE24664666F93DBD127A5D5F1D2DA6A039C4DF19AA7ACFC6B68371BAFBA4222B` |
| Desync-268 | `67D94AA2A0B223900A22293846EBBD84B149DE57C134187C3AA7B7001F121EEB` |
| Desync-269 | `E608E1EA71332FA3F91B4C8AB24043CA601A8BF658BCFD040B7E3F8053D2AF15` |
| Desync-270 | `1C8BDA166CCEA7BFC56148683158E610AA84B0CF1DF95A9EA3F40BF091DBFF7C` |
| Desync-271 | `FA64971300C08F724E6FC37E6876078BD02F58DFB9FB8FF6D04F00BF675FEAAA` |
| Desync-272 | `8AC94AC7901E20C24AD668F95368BA45CA66802635A9CF0424B44AE8E1B07005` |
| Desync-273 | `E190CDC5D6862933F20E05D604D8C52DC8E24473637A545B106EDA79EE2148DF` |

- `Desync-264.zip` 不在工作区中，本次无法分析该编号。
- 所有包均为：Multiplayer `0.11.5+a481546`，RimWorld `1.6.4850 rev646`，
  2 名玩家、async time 开启、2 张地图，加载程序集身份
  `3.0.117-tps-fix-nullguard`。
- 所有包的日志都出现 `Mod list diff: WrongOrder`、`Configs match: False` 后
  `Connecting anyway`；这是会话级风险，但最早的分叉并不直接落在配置项上。

## 每包最终掉线点

| 包 | 最后有效 tick | 掉线类型 | 最早分叉要点 |
|---|---|---|---|
| 262 | 12661 | Trace hashes don't match | 同一 tick 两端给不同 pawn 分配 JobID：host `JobGiver_ForceSleepNow`（Mech_Scorcher），local `EndCurrentJob`（Bluebird） |
| 263 | 17821 | Trace hashes don't match | 同一 tick 同一 pawn，host/local 在 `JobGiver_ForceSleepNow` 路径的 Rand 状态已不同 |
| 265 | 3849541 | Wrong random state on map 21 | 同一 `Axolotl784652`：host 走到 `JobGiver_UnloadYourInventory` 建 job，local 在 `ThinkNode_PrioritySorter` 先消费 `Rand.Range`；Rand 高 32 位相差 1 |
| 266 | 3865081 | Wrong random state on map 21 | local 在 `GasGrid.TryDiffuseGases -> GenList.Shuffle` 消费 map Rand，host 在下一 tick 建 job；两端 map tick 相差 1 |
| 267 | 3867931 | Wrong random state on map 21 | 同一 `Boomalope`、同一调用栈，Rand 高 32 位相差 -2，说明分叉先于该 trace |
| 268 | 3869708 | Random state from commands doesn't match | host 执行 `GodHandSync.SyncGodHandMeleeHit`，`DamageInfo`/`TaleRecorder` 在命令内消费 Rand；local 无 trace |
| 269 | 3873331 | Trace hashes don't match | 同一 `Ratkin957060`、同一 tick、同一 rngState，但栈深度 31 vs 29（Harmony 包装帧不同） |
| 270 | 3877734 | Trace hashes don't match | 同上，`Ratkin957060` 同一 rngState，栈深度 31 vs 29 |
| 271 | 3881357 | Trace hashes don't match | 重连后仍复现 269/270 的 trace-hash-only 分叉 |
| 272 | 6695 | Trace hashes don't match | 新重连短会话，`Ratkin957060` 同一 rngState，栈深度 31 vs 29 |
| 273 | 9181 | Trace hashes don't match | 同上，重连后再次复现 |

## 根因分级

1. 已证实：GodHand 同步命令用 `map.Index` 作为地图标识。
   `TickPatch.RunCmds/DoTick` 会把 `Find.Maps` 临时按 `uniqueID` 排序，
   而 UI 发令时读的是未排序列表的 `Map.Index`。两端地图列表顺序不同时，
   同一个 `mapIndex` 会解析到不同地图，GodHand 命令（例如 268 的
   `SyncGodHandMeleeHit`）就会在错误地图上消耗 Rand 或直接单端执行。
2. 强支持：GodHand drag/wrench 会话只存在进程内静态字典，未写入存档或
   Multiplayer snapshot。断线重连后 host 可保留旧会话，重连客户端没有；
   随后同一 playerId 的 GodHand 命令只在一端生效，最终表现为
   job 生成顺序/Rand 命令状态分叉（262/263/272/273 均为重连后的短会话）。
3. 强支持：265~267 是 async-time 下 map 21 的随机流/调度相位漂移。
   现有的 `Patch_AsyncTickSchedulerPhase`、`Patch_CommandOrderDeterminism`、
   `Patch_DeterministicTickList` 覆盖这一类问题，但 262~273 包没有证明
   该候选构建已经消除全部路径。
4. 未解决：269~273 只有 `Trace hashes don't match`，且两端在同一 tick、
   同一 thing、完全相同的 rngState 下栈深度不同（host 31 / local 29）。
   这更像 host 调试构建/Prepatcher 包装帧与 release 客户端不一致造成的
   trace 伪差异，而不是可验证的模拟状态分叉。按技能要求不为此扩大补丁，
   也不通过关闭 trace 比较来掩盖真实分叉。

## 本次代码修改（仅源码，未部署 DLL）

- `Patch_GodHands.cs`
  - `FindMap(int)` 改为按 `map.uniqueID` 解析，不再按列表位置。
  - GodHand drag/wrench 会话键统一承载 `uniqueID`。
  - Poke 确定性种子从 `map.Index` 改为 `map.uniqueID`。
  - `GodHandSync` 新增 `RemovePlayerSessions` / `ResetAllSessions`。
- `Patch_GodHandsControllers.cs`、`Patch_GodHandsDesignators.cs`、
  `Patch_GodHandsTurretCommands.cs`
  - 所有 GodHand 同步命令发令点从 `map.Index`/`Find.CurrentMap.Index`/
    `head.Map.Index` 改为对应 `uniqueID`。
- 新增 `Patch_GodHandsSessionLifecycle.cs`
  - 服务器端 `PlayerManager.SetDisconnected` postfix：把断开玩家的
    GodHand/Wrench 会话移除动作排到主线程，避免与命令执行竞争。
  - `Multiplayer.StopMultiplayer` postfix：本地会话停止时清空全部会话。
  - 已加入 `MP_MeowOnlineShop.csproj` 编译项。

## 验证状态

- `dotnet build -c Release -p:OutputPath=C:\tmp\mp-build-262-273\bin`
  ：0 警告，0 错误。
- 候选 DLL：`C:\tmp\mp-build-262-273\bin\MP_MeowOnlineShop.dll`
  SHA-256：`BE7E98C68202769640A7986614028EE40236A04717941B1426502EAEC17DB7C2`
- 按用户要求：未复制 DLL 到正式 `1.6/Assemblies`，未运行 host/client
  smoke 或 soak。
- 工作区中 `Patch_DeterministicTickList.cs`、`Patch_InvalidMapIndexSafety.cs`、
  `Patch_MiningDiscoveryMp.cs`、`Patch_QuestAndIdeologyMp.cs`、
  `DeterministicRandScope.cs` 存在既有未提交改动，本次未回退、未归属。

## 剩余风险

- 269~273 的 trace-hash-only 分叉未做代码级抑制；建议后续用相同
  Multiplayer 构建类型（host 不要开 debug 构建）复测。
- `Desync-264.zip` 缺失。
- 所有包都带 `Configs match: False` + `Connecting anyway`，应先让双端
  配置一致，否则任何补丁都无法排除配置差异的贡献。
- 需要一次真实 host/client 的 async-time + multifaction + 双地图
  targeted smoke 与长 soak 验证本批静态补丁。
