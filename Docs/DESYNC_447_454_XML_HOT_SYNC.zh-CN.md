# Desync-447 ~ 454 分析与 XmlMod 热同步（2026-08-17）

## 结论

447-454 与 445-448 是同一类连续会话的延续：每次加入/重连前都是
`Configs match: False`（`imranfish.xmlextensions/XmlMod`），随后
`Connecting anyway`，客户端每次重连后还出现
`Could not find think node with key -1937076157`。

用户要求使用热同步，不采用“禁用 Connect Anyway / 强制 Fix and Restart”的补丁。
因此最终实现是把 XML Extensions 的 `XmlMod` 配置从“启动期必须重启”名单中移出，
让现有标准 ModSettings 热重载路径对它生效：

- `XmlExtensions.XmlModBaseSettings` 是标准 `Verse.ModSettings`，包含
  `dataDict` 等字段，并有 `ExposeData`。
- `XmlExtensions.XmlMod` 继承 `Mod`，使用标准 `WriteSettings()` 保存。
- `Patch_MpConfigHotSync.TryReloadStandardModSettings` 可按
  `modId=imranfish.xmlextensions`、`fileName=XmlMod` 匹配到该实例，原地 Scribe
  加载 host 配置、调用 `WriteSettings()` 并校验字段差异。

因此 `XmlMod` 现在可以走与其它普通模组一致的验证后热同步；只有无法验证或
语义不一致且重载失败时才回退到原生重启流程。

## 证据

冻结目录：`TestValidation/DesyncEvidence/2026-08-17-447-454-desync/`

| 包 | SHA-256 |
|---|---|
| Desync-447.zip | `24A523008E09CD854716D7646B4586F73E01827B7912A01D2C5ADDF61D2F0655` |
| Desync-448.zip | `3076826E7942C221AC30ABF9C376A4156E41D5A2DE9ECD784A6C2D752056FAFC` |
| Desync-449.zip | `A000077211DC6AFB8B81CA6DAC42DD134069BBA0583DEC427A036F4EE8874C1A` |
| Desync-450.zip | `0C24161CD2A5627E87B7A2A4E141ACC23D82CEB0B7936AF5438E5BB0BA9EE8EB` |
| Desync-451.zip | `881A5E0518BF42BD1DEC1F8A12CD79CD2CAB22620B6065CB3FA071EBC8A9E598` |
| Desync-452.zip | `49A9695A6E787C60080009E70CC0B6E00386B9E5815158C47D06102F53BDECDD` |
| Desync-453.zip | `F461D2FD05ECBA59819EF659A143F550F5F327AB2ADD2B63BBDC9E0D351003D9` |
| Desync-454.zip | `76ACB6AC69FE447B7DE040870ADF505FADF3D55C54AC3BABDCB780948C75D60E` |

## 每包最早分歧

| 包 | 最后有效 tick | 类型 | 最早分歧 |
|---|---:|---|---|
| 447 | 8020981 | Wrong random state on map 1 | local 单端 `Verb_LaunchProjectile.TryCastShot` 创建投射物（Milian Mechanoid） |
| 448 | 8025192 | Trace hashes don't match | local 单端 `GameComponent_PsychicRitualManager` 消息 ID |
| 449 | 8014801 | Trace hashes don't match | host 单端 `EndCurrentJob -> JobMaker.MakeJob` JobID |
| 450 | 8040481 | Trace hashes（trace 数 local 多 1） | local 单端 PsychicRitualManager 消息 ID；host `Axolotl.HediffCompWaterTreatToSelf` HediffID |
| 451 | 8052211 | Trace hashes（trace 数极少） | host `Thing.DeSpawn`（Axolotl 尸体）与 local JobID 时间不同，rngState 相同 |
| 452 | 8082811 | Trace hashes（host trace 多 1） | host 单端 `EndCurrentJob -> JobMaker.MakeJob` JobID（Ibex） |
| 453 | 8106368 | Trace hashes（local trace 多 45） | 同一 `Axolotl1612291`、同一 tick、同一 rngState，两端都在 `Thing.PostMake` |
| 454 | 8146921 | Trace hashes（host trace 多 1） | local 单端 `Axolotl2703767` ThingID；host HoldingPlatform Rand |

450-454 的 `local_logs.txt` 只到本会话早前位置（同一份 9957 行日志），不包含各自
最终掉线段；这些包以 trace 文件为准，但它们仍来自同一 `Configs match: False` +
缺 think node 的累积会话。

## 为什么这些差异不直接补模拟代码

- PsychicRitualManager 消息：JIT 显示两端同 tick 执行冷却移除，差异只是共享
  MessageID/trace，未证明是模拟状态分叉。
- 单端 JobID：根因在 trace 窗口之前，且客户端每次重连都缺 think node；直接包
  `EndCurrentJob` 会掩盖真实 Job 状态漂移。
- 单端开火：发生在同一重连污染之后，缺少干净基线和 host 日志，无法验证是射击
  入口漏同步还是 Job 状态先分叉。
- 451/453 的同 rngState、不同栈包装属于历史已知的 trace-only 误报类。

这些包应先验证配置热同步后是否仍复现；若仍复现，再针对当时的第一条真实 Rand
分歧做最小模拟补丁。

## 本次代码修改

`Source/MP_MeowOnlineShop/Patch_MpConfigHotSync.cs`

- 撤销“禁用 Connect Anyway 按钮”的改动。
- `StartupBoundConfigModIds` 不再包含 `imranfish.xmlextensions`。
- XML Extensions 的 `XmlMod` 进入标准 ModSettings 热同步路径：语义相同则原地
  规范化文件；语义不同则 staging + Scribe 热重载 + `WriteSettings` + 字段差异
  校验，验证通过后标记 `HotApplied` 并允许自动继续加入。
- 同一进程内已热同步并验证过的配置再次比对时记为 `Unchanged`，不会因为
  `WrittenThisProcess` 又退回 `RestartRequired`。

## 残余风险

- `XmlMod` 的设置在游戏启动时可能已被 XML 补丁读取；如果 host/client 的
  `dataDict` 实际值不同，而 defs 已按各自旧值加载，热重载只保证后续运行时读取和
  磁盘一致，不会重跑 def 补丁。若这类语义差异导致缺 think node，仍可能需要一次
  干净的 Fix and Restart 来重建 defs。
- 若配置差异只是 XML 字节/格式差异，热同步会直接规范化并通过，不需要重启。

本次未部署 DLL，未运行 host/client 测试。
