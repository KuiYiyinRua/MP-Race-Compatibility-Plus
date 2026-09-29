# Desync-249 / 251-256 分析与修复（2026-08-05）

## 证据

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-05-249-251-256/`
- 每个 zip 均复制后校验 SHA-256，并只从冻结副本解压。
- Multiplayer：`0.11.5+a481546`；RimWorld：`1.6.4850 rev646`。
- 所有会话均为：async time 开启、multifaction 开启、3 张地图、2 名玩家。
- 249/251/252 使用 `3.0.115-milira-all-weapons`；253-256 使用
  `3.0.117-config-hot-sync`。253-256 日志中还存在
  `Configs match: False` 后用户手动 `Connecting anyway`，这是会话级风险，
  但最早的 Rand/trace 分叉与配置项无直接对应。

## 各包最终掉线点

| 包 | 最后有效 tick | 掉线类型 | 涉及地图 |
|---|---|---|---|
| 249 | 3541711 | Trace hashes don't match | 新建 map 22（Gravship 落地） |
| 251 | 3560071 | Trace hashes don't match | map 22 附近（Ancot 炮塔/机械体） |
| 252 | 3616141 | Wrong random state on map 22 | map 22 |
| 253 | 3671731 | Trace hashes don't match | map 23（Ancot 炮塔） |
| 254 | 3715651 | Wrong random state on map 24 | map 24（新建，Gravship 落地） |
| 255 | 3725221 | Wrong random state on map 24 | map 24 |
| 256 | 3738412 | Wrong random state on map 1 | map 1；此前 map 24 orphan 命令被丢弃 |

## 最早分叉证据

- 249：host 在同一 tick 生成 `Milira_Race` UniqueID，client 处理
  `TriggerUnfogged893431` 的 DeSpawn；落地后 client 的 TickList 重建先移除了
  `TriggerUnfogged`。
- 251/253：host/client 第一个 trace hash 分叉落在
  `AncotLibrary.Building_SpinTurretGun.TryFindNewTarget`（`Rand.get_Value`）
  对 `UniqueIDsManager.GetNextID`/机械体生成。
- 252：map 22 的 map Rand 先分叉，host 在 `JobGiver_GetJoy` 的
  `TryRandomElementByWeight`，client 在 `UniqueIdsPatch`；同窗口还有
  `ITab_ContentsBase.OnDropThing` 的同步命令 NullReferenceException。
- 254：map 24 的 map Rand 分叉，host 在 `Turret_AncientArmoredTurret`
  `Rand.Range`，client 在 `MiliraBullet_PlasmaMGCharged` `Rand.Chance`。
- 255：两端都进入 `Fire.DoComplexCalcs`，但一个是 `Rand.get_Value`，另一个是
  `GenList.Shuffle -> Rand.get_Int`，说明该 tick 前 map Rand 流已经错位。
- 256：无 call-level trace；日志显示 `Gravship orphaned map command dropped
  handler=noData map=24` 三次，随后 map 25 落地/废弃，最后 map 1 掉线。

## 根因

1. Gravship/Odyssey 落地新地图（22/23/24/25）时，map 内容与本地
   `WorldComponentUpdate` 的 cutscene 结束时机有关；两端不一定在同一共享 tick
   完成 map 生命周期，导致 per-map TickList 成员和 map Rand 流先错位。
2. `Verse.Mote` 字段初始化器直接调用 `Rand.Range`，而 `Mote_ChargingCablesPulse`
   /`Mote_MechCharging`/`Mote_FoodBitVegetarian` 等视觉 mote 又会进入 async
   Normal TickList。mote 是纯视觉、不入 `listerThings`，两端生命周期不同步时，
   其 Rand 消耗只发生在单端。
3. Ancot/Milira 炮塔在 map tick 中消费 `Verse.Rand`：目标选择
   （`TryFindNewTarget`）和空闲旋转（`SpinTurretTop.TurretTopTick`）。当 map
   TickList 已因落地/mote 错位，第一个被 trace 到的分叉经常落在这里。
4. `Patch_GravshipOrphanCommands` 原先只在本地 `TakeoffEnded`/`AbandonMap`
   之后开始丢弃 abandoned-map 命令；慢端 cutscene 尚未结束时，命令队列内容
   与快端不一致，存在“一端执行、另一端丢弃”的窗口。

## 修改

- `Patch_DeterministicTickList.cs`：async TickList 重建、pending 合并、当前
  bucket 校正和候选扫描全部排除 `Verse.Mote`，视觉 mote 不再进入 per-map
  TickList。
- `Patch_MoteConstructionRandIsolation.cs`（新增）：对所有
  `ThingMaker.MakeThing` 重载加前缀/终结器；当 ThingDef 的 thingClass 是
  `Verse.Mote` 时，在确定性 Rand scope 中构造，结束后恢复 map Rand 流。
- `Patch_AncotTurretRandIsolation.cs`（新增）：对
  `AncotLibrary.Building_SpinTurretGun.TryFindNewTarget` 和
  `AncotLibrary.SpinTurretTop.TurretTopTick` 使用按 map+turret ID 推导种子的
  确定性 Rand scope，结束即恢复同步流。
- `Patch_GravshipOrphanCommands.cs`：在同步的 `InitiateTakeoff` postfix 中，
  若旧地图不保留 grav anchor，立即 `MarkAbandoned(mapId)`，使 orphan 命令清理
  从共享 takeoff tick 开始，而不是等本地 cutscene 结束。

## 验证状态

- `dotnet build`（Release，输出到 `C:\tmp\mp-build-249-256\bin`）：0 警告，
  0 错误。
- 候选 DLL SHA-256：
  `2F5E88134384ABDCA98F13EF8FA5507FE5996F57A168F59047F3FF843D23783D`
- `ilspycmd -l c` 确认 `Patch_AncotTurretRandIsolation` 与
  `Patch_MoteConstructionRandIsolation` 已进入候选程序集。
- 按用户要求：未部署正式版 DLL，未运行 runtime 测试。后续仍需要一次真实
  host/client async-time、multifaction、3 地图的 targeted smoke + 长 soak 来
  验证这些静态修复。
