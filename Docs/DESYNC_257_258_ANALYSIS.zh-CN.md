# Desync-257 / 258 分析与修复（2026-08-05）

## 证据

- 冻结目录：`BuildValidation/DesyncEvidence/2026-08-05-257-258-meow-online-shop/`
- 原始包 SHA-256：
  - `Desync-257.zip` = `653F986C4EBDE216D85AE2BDC703E649B539758F69B1D4E2FABBD3A9BF6089DB`
  - `Desync-258.zip` = `9A579A1F840F138567B5E926E7853CB9F5EDA7092084801C3952B295D9854083`
- Multiplayer `0.11.5+a481546`，RimWorld `1.6.4850 rev646`
- 会话配置均为：2 名玩家、async time 开、multifaction 开、2 张地图
- 加载的程序集版本：`3.0.117.0` / `3.0.117-config-hot-sync`

## 257：map 21 的推进相位分叉

- 日志：`Player6540 3739890 Desynced after last valid tick 3739831: Wrong random state on map 21`
- trace：local 42 条，host 15 条；前 15 条 tick/hash/Rand 完全一致，host 在
  trace 14（`GasGrid.TryDiffuseGases -> GenList.Shuffle -> Rand.RangeInclusive`，
  global tick 3739873）后停止，client 继续到 3739889
- 结论：不是某个 Rand 取值不同，而是 map 21 在两个端每共享 global tick 推进的
  次数不同，导致 map 随机状态序列长度/内容分叉
- 根因：`AsyncTimeComp.TimeToTickThrough` 是纯运行时调度累加器，未写入
  `ExposeData`。长期运行的主机保留自己的相位余数，冷加入/重连的客户端从 0
  起步；`TickPatch.TickTickable` 的 `while (>= 0)` 循环让这个相位直接改变每
  global tick 的地图 tick 数

## 258：同一堆叠 SplitOff 分支不同

- 日志：`Player6540 3747272 Desynced after last valid tick 3747211: Trace hashes don't match`
- trace：两端各 70 条，0..68 完全一致；第一条分叉在 index 69：
  - local：`Thing.DeSpawn`（`SplitOff` 取走整叠，直接销毁原物体）
  - host：`Thing.PostMake / UniqueIDsManager.GetNextThingID`（`SplitOff` 新建子叠）
  - 同一 `Axolotl467273`、同一 rngState，深度 28 vs 31
- 结论：该时刻 `Axolotl467273.stackCount` 在两端已经不同；这是之前一个不产生
  trace 的静默堆叠/任务分叉的下游表现
- 关联：259 的 host 第一条分叉落在 `Patch_TransportShipUnloadMp.MapPreTickPostfix`，
  说明运输船/客机卸货自动征召的 world-tick 与 map-tick 顺序分叉会在卸货后把
  pawn 任务、搬运 SplitOff 推到不同分支

## 修复

1. 新增 `Patch_AsyncTickSchedulerPhase`：在多人普通游玩（非 replay、非
   simulating）的每次 `TickPatch.TickTickable` 前把 `TimeToTickThrough` 归一化为
   `-timePerTick`，使每端每 global tick 精确推进相同次数，消除重连相位分叉。
   已在 `Patch_SellSlingshot.ApplyPatch` 注册，并加入 csproj 编译项。
2. 258/259 的运输船卸货征召顺序问题由既有未提交改动
   `Patch_TransportShipUnloadMp` 覆盖：排队边界从 `Drafted` setter 移到
   `UnloadThingFromShuttle`，按 map 的 `MapPreTick` 确定性回放，并应用 home-map
   与 player-controlled 过滤。

## 验证状态

- `dotnet build -c Release -p:OutputPath=C:\tmp\mp-build-257-258\bin`
  ：0 警告 0 错误
- 候选 DLL：`C:\tmp\mp-build-257-258\bin\MP_MeowOnlineShop.dll`
  SHA-256 = `43186A684AAB0854D247F24078407834359038BD47DA3CE50386DDF2DF7359B4`
- 未部署正式版 DLL，未运行 host/client runtime 测试
- 正式版 `1.6\Assemblies\MP_MeowOnlineShop.dll` 已恢复为 16:24:11 快照版本
  （SHA-256 = `07990DB06BFB2375C4D357773E7FA874B0DD2E4859CB2EAF5857228E0562261E`），
  即 257/258 运行时对应的正式构建；包含 `Patch_AsyncTickSchedulerPhase` 的新候选
  仅留在 `C:\tmp\mp-build-257-258\bin`，未放入正式目录

## 剩余风险

- 相位归一化改变了 async scheduler 的每 tick 计数方式，需要一次真实
  host/client 双端 async-time + multifaction + 双地图的 smoke/soak 验证
- 258 的堆叠分叉只被关联证据支持，未做最小复现；运输船卸货修复是否完全消除
  该路径仍需 runtime 验证
