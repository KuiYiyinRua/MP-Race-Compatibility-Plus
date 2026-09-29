# Desync-523/524/525/526 分析：战斗 + "人格核心"(AIPersonaCore) 信件的世界随机分叉（2026-08-20）

## 结论

523–526 是同一会话（Player5292，async time 开、multifaction 开、2 玩家、2 地图）的连续掉线：

| 包 | 最后有效 tick | 掉线类型 | 首个分叉 trace |
|---|---:|---|---|
| 523 | -1（重连后立即） | `Map instances don't match` | host 在 tick 9029221 正在 tick 地图 `20918445`(HoldingPlatform/Player65)，local 正在 tick 地图 `21020148`(HoldingSpot/BT7724)——重连时两端地图集合/顺序不一致 |
| 524 | 9038731 | `Wrong random state for the world` | idx100：host `Rand.Chance(0.05f)`（失败）；local `RandomNonHostileFaction→Rand.Range` + `GetNextLetterID`（成功发信）——同一个原版 `GameComponent_OnetimeNotification.GameComponentTick`（人格核心出售信件） |
| 525 | 9062191 | `Wrong random state for the world` | 与 524 完全同签名（idx267，tick 9062235，同组件） |
| 526 | 9068791 | `Wrong random state for the world` | idx78：host `Building_HoldingPlatform.Tick→MTBEventOccurs`（地图 tick）；local `SyncSetTargetReadBook→ApplyTargetReadBookAndInterrupt→EndCurrentJob→ThinkNode_PrioritySorter→Rand.Range`（我们的同步命令在命令阶段消费共享随机） |

## 根因

### 524/525：原版 `GameComponent_OnetimeNotification`（人格核心/`AIPersonaCore` 出售信件）在 async time 下不具确定性

该组件（RimWorld 1.6 原版，Multiplayer 0.11.5 **没有**给它打补丁）每 tick：

```
if (Find.TickManager.TicksGame % 2000 == 0 &&
    Rand.Chance(0.05f) &&
    sendAICoreRequestReminder &&
    ShipRelated.CompletedProjects() >= 2 &&
    !PlayerOrQuestRewardHas(AIPersonaCore) && !PlayerOrQuestRewardHas(Ship_ComputerCore))
{
    Faction f = Find.FactionManager.RandomNonHostileFaction();   // 再消费 N 个世界随机
    if (f != null && f.leader != null) ReceiveLetter(...);       // 再分配一个 LetterID
    sendAICoreRequestReminder = false;
}
```

- `TicksGame % 2000` 是**每端私有**的：async time 下地图 tick 会把 `TickManager.ticksGameInt` 覆盖成各端自己的 `mapTicks`（`AsyncTimeComp.Tick` 里 `Find.TickManager.ticksGameInt = mapTicks`）。
- 因此一旦世界随机流因任何上游错位（519–522 已定位的 JobID 偏移、命令随机、地图 tick 顺序）产生漂移，这个每 2000 tick 触发一次的组件就会在两端走不同分支：一端只消费 1 个 `Rand.Chance`（失败），另一端消费 `Chance + RandomNonHostileFaction(N) + LetterID`（成功发信），世界随机流错位立刻被 desync 检测抓住，报 `Wrong random state for the world`。用户看到的"刷出任务人格核心"就是这封信。

证据（524 idx100/101）：
- host idx100：`Rand.Chance@GameComponentTick`，rngState `204920765140431219`；idx101 再次 `Rand.Chance`（世界 tick 追赶），rngState `204920769435398515`。
- local idx100：`Rand.Range@RandomNonHostileFaction@GameComponentTick`，rngState `204920765140431219`（未推进 → 消费自被隔离/推进别处）；idx101：`UniqueIdsPatch.Postfix→GetNextLetterID→MakeLetter→ReceiveLetter`（发信）。
- 两端 ticksGame 同为 `21026000`（%2000==0），但随机分支结果不同 → 世界随机状态已提前漂移，组件将其放大成可见 desync。

### 526：我们的 `Patch_AxolotlCultivationRead.SyncSetTargetReadBook` 命令消费共享随机

local 在 UI 里选修炼功法书（JIT 证据：`ITab_MoeLotl_Cultivation...FillTab>b__6 → SetTargetReadBookMaybeSync` @t:9068816），随后：

1. `SetTargetReadBookMaybeSync` 在 UI 回调里**先执行了一次本地副作用** `ApplyTargetReadBookAndInterrupt`（`EndCurrentJob(InterruptForced)`）；
2. 再发 `SyncSetTargetReadBook` 命令，命令回放又对所有端（含发起端）执行一次 `ApplyTargetReadBookAndInterrupt`。

`EndCurrentJob → DetermineNextJob → ThinkNode_PrioritySorter → Rand.Range` 会消费 map/world 随机。发起端在 UI 阶段/命令阶段各消费一次、且与 host 的地图 tick 消费点错位（host idx78 是 `Building_HoldingPlatform.MTB`），于是共享随机流错位 → `Wrong random state for the world`。

## 修复（仅源码，未部署、未测试）

### 1. `Source/MP_MeowOnlineShop/Patch_OnetimeNotificationDeterminism.cs`（新增，csproj 已登记，入口 `Patch_SellSlingshot.ApplyPatch` 挂载）

对原版 `GameComponent_OnetimeNotification.GameComponentTick` 加 MP 前缀：
- 门改为 Multiplayer 共享时钟 `Multiplayer.Client.TickPatch.Timer % 2000`（`MpRuntimeInfo.TryGetMpTimerTick`，两端对齐；解析失败 fail-open 回原版）。
- 整个组件体包进 `DeterministicRandScope`（种子 = 共享 tick 窗口 `syncTick/2000`），`Rand.Chance/RandomNonHostileFaction` 全部消费确定性独立随机，**不再触碰共享世界随机**；两端同窗口 → 同分支、同信件、同 LetterID。
- 单机不变；fail-open；只打一次 active/failure 日志。

### 2. `Source/MP_MeowOnlineShop/Patch_AxolotlCultivationRead.cs`

- `SetTargetReadBookMaybeSync`：删除 UI 回调里的本地副作用 `ApplyTargetReadBookAndInterrupt`，只发同步命令，由命令回放在所有端（含发起端）统一执行一次。
- `ApplyTargetReadBookAndInterrupt`：`EndCurrentJob`（触发 `ThinkNode_PrioritySorter` 换工作）在 MP 下包进 `DeterministicRandScope`（种子 = map.Index + pawn.thingIDNumber + 功法书 defName），命令内不再消费共享 map/world 随机。
- 新增 `BuildTargetReadSeed` 与 `TargetReadSeedOffset/TargetReadSeedWorldOffset` 常量。

### 3. `Source/MP_MeowOnlineShop/MpRuntimeInfo.cs`

新增 `TryGetMpTimerTick(out int tick)`：反射 `Multiplayer.Client.TickPatch.Timer`（共享逻辑时钟），供确定性门使用。

### 4. `Source/MP_MeowOnlineShop/MP_MeowOnlineShop.csproj`

登记新文件 `Patch_OnetimeNotificationDeterminism.cs`。

## 构建状态

- `dotnet build -c Release -p:OutputPath=TestValidation\CompileCheck_20260820_523_526`：0 警告、0 错误。
- 候选 DLL：`TestValidation\CompileCheck_20260820_523_526\MP_MeowOnlineShop.dll`
  SHA-256 `74FFBF1736852FF4789251B6C3CD3DD2F69478B17515070544067CCB8A653B62`。
- **未部署**：`1.6/Assemblies/MP_MeowOnlineShop.dll` 仍为 `3FF71FFCD7C35D22225916C5B63846F52F90E99AD43C9656399AE0F40C3D5148`（3.0.121，17:57:20，未变）。
- 未做任何 host/client 运行验证。

## 证据归档

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-20-523-526-personality-core/`
  - `Desync-523.zip` SHA-256 `000029D422151A9EA5D7A21212E6B6027FC99739C09C428D759083D86EB9B6A7`
  - `Desync-524.zip` SHA-256 `270D9ED2BB4C83A30F2D6A5DB4AE616C3529215C50598CBC9AD2185197B1E4DF`
  - `Desync-525.zip` SHA-256 `0C0519C6BB3CF7713AB8E5CBECB1BF1E8C82DF6D6C4868D8691EBC028DE907F9`
  - `Desync-526.zip` SHA-256 `ED09A796CD5D755EDAA374E0112025BF669793897969EA3DF062F17CE5478C27`
- 会话运行程序集（local_metadata）：`MP_MeowOnlineShop 3.0.121`，即已部署 `3FF71FFC…`。
- Multiplayer `0.11.5+a481546`；RimWorld `1.6.4850 rev646`；async time / multifaction 开。

## 剩余风险与建议

- **523（重连 `Map instances don't match`）**：独立的重连期地图集合错位（战斗/远行队临时地图创建或销毁与重连快照竞争），本次未臆造补丁；现有 `Patch_CaravanMapRemovalSafety / Patch_DeterministicWorldPawns / Patch_TradeSessionRejoinMp` 已覆盖邻近路径，建议下次复现时 host/client 对比 `Find.Maps` uniqueID 列表。
- **519–522 的 JobID 偏移根因**（战斗上下文，交易会话状态 + 工作结束各差 1 tick）：已由工作区未提交的 `Patch_TradeSessionRejoinMp` + `Patch_JobEndDiagnostic(MP_JOB_END_DIAG=1)` 覆盖，尚未随本次候选部署验证。
- **多派系残差**：`GameComponent_OnetimeNotification` 里 `PlayerOrQuestRewardHas` 是逐派系查询，多派系下两端可能因各端玩家派系不同而分支不同（信件/ID 残差）。本次确定性作用域已消除“世界随机流”这个主要分叉面，但发信与否仍可能受派系状态影响。
- **其他同步命令**（如 RigorMortis 僵尸技能 `SyncZombieMutantAbilityCast`）走的是"拦截 UI + 命令回放"正确模式，trace 未指向它们，未改动。
- 未运行 host/client smoke/soak；需按 runtime-testing 门禁做 targeted 复现（人格核心窗口 + 修炼选书 + 战斗），再决定部署。
