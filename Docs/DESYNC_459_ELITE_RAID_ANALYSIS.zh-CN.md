# Desync-459 分析（2026-08-17）

## 结论

Desync-459 是 `Trace hashes don't match`，最早分歧是 host 端在模拟 tick 内为
**玩家派系殖民者** `Human2714609` 惰性创建了一个 `Ability` 并分配
`GetNextAbilityID`；local 端同一 tick 在 `Axolotl1996700` 的思考树里
`ThinkNode_PrioritySorter` 消费地图 Rand。trace 数 host 831 / local 830，地图
Rand 状态没有分叉（掉线类型不是 `Wrong random state`）。

Elite Raid 在掉线前确实执行了
`GenerateAnything_Impl: baseNum=46, maxPawnNum=23, raidFriendly=False`，但：

- 最早分歧栈里没有任何 `EliteRaid.*` 方法；
- 创建 Ability 的 pawn 属于玩家派系，不是被压缩/精英化的袭击者；
- EliteRaid 的 `ThreadSafeRandom` 已被现有补丁 4/4 接入同步 `Verse.Rand`。

因此“Elite Raid 刷新袭击导致本次不同步”的怀疑没有被 trace 证据支持。该包最可能
仍是此前多次出现的 trace-only 类差异：一端的能力创建 trace 被 Rand scope 或
debug/release 包装差异抑制，或能力缓存先于 trace 窗口分叉，但 map/world Rand 一致。

本次**不补模拟代码**。直接包 `Pawn_AbilityTracker.AbilitiesTick` /
`Hediff.get_AllAbilitiesForReading` 会改变 vanilla 能力惰性初始化语义，且没有
验证是哪一端、哪个 mod 先让能力缓存不一致。

## 证据

- 冻结目录：`TestValidation/DesyncEvidence/2026-08-17-459-elite-raid/`
- `Desync-459.zip` SHA-256：
  `7E03F5E4C8A37D004D02976D93EF2FB0A826B44FBC0BF698F069F4DAF4C39F7D`
- Multiplayer `0.11.5+a481546`，RimWorld `1.6.4850 rev646`，async time +
  multifaction，2 张地图。
- 最后有效 tick：`8237791`；掉线 tick：`8237850`。

## 最早分歧

`local_traces.txt`（trace 830）：

```text
585 Tick:8237843
  Verse.Rand.get_Int
  -> ThinkNode_PrioritySorter.TryIssueJobPackage
  -> ... Pawn_JobTracker.EndCurrentJob
  (Axolotl1996700)
```

`host_traces.txt`（trace 831）：

```text
585 Tick:8237843
  UniqueIDsManager.GetNextAbilityID
  -> Ability.Initialize
  -> Ability..ctor
  -> AbilityUtility.MakeAbility
  -> Hediff.get_AllAbilitiesForReading
  -> Pawn_AbilityTracker.get_AllAbilitiesForReading
  -> Pawn_AbilityTracker.AbilitiesTick
  (Human2714609)
```

## 相关日志

- 第 1801/1822 行：`Configs match: False` 后 `Connecting anyway`（本包仍有配置
  不一致基线）。
- 第 2262、2968 行：本包日志内还包含更早的同会话 desync。
- 第 3717-3718 行：掉线前 `IncidentRaidFactionContext` 执行 RaidEnemy，
  `GenerateAnything_Impl: baseNum=46, maxPawnNum=23`。
- 第 5260 行：`Player7182 8237850 Desynced after last valid tick 8237791:
  Trace hashes don't match`。

## 方案

1. 先按上一轮已提交的 XmlMod 热同步方案，确保 `Configs match: True` 后重跑，
   排除配置不一致基线。
2. 若该 trace-only 能力差异在配置一致后仍复现，下一步在 host/client 两侧对
   `Hediff.get_AllAbilitiesForReading` 加临时诊断，记录哪个 HediffDef、哪个
   pawn 在 `AbilitiesTick` 中首次触发 `MakeAbility`，以区分“真实单端创建”还是
   “trace 被 Rand scope 抑制”。
3. 确认是真实单端创建后，再针对授予该 Ability 的具体 hediff/mod 入口做同步，
   而不是直接关闭 vanilla 能力初始化。

本次未修改 `Source/` 代码，未部署 DLL，未运行 host/client 测试。
