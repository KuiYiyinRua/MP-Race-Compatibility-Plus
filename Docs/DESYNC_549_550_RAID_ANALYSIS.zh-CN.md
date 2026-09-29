# Desync-549 / Desync-550 袭击事件分析

## 证据冻结

原始压缩包已复制并展开到：

`TestValidation/DesyncEvidence/2026-08-21-549-550-raid-analysis`

- `Desync-549.zip` SHA-256：`5D7C93A5D45FEF7E7FC03FA7EC58A4EA14C2EA3AF331EBE6FB32D6599E32901B`
- `Desync-550.zip` SHA-256：`E08E93A808C037F9E3E729322E9BB12DC47D8E52DF7BF62D97DC276C2FE1C384`

两份 bundle 的 `desync_info` 都显示为 Multiplayer `0.11.5+a481546`、RimWorld `1.6.4850 rev646`、双玩家、异步时间开启、多派系开启、两张地图。

## 日志结论

- `Desync-549` 的首个 trace 差异位于本地 tick `9728764` 与主机 tick `9728763` 附近；主机已经出现 `HoldingSpot2546945`，本地仍处于另一组对象分配/顺序。
- `Desync-550` 的首个 trace 差异位于本地 tick `9745862` 与主机 tick `9745863` 附近；两端的 `Bullet_BeamRepeater`、`Fire`、`HoldingSpot` 对象 ID 和生成顺序不同。
- `Desync-550` 的最终 jitted 调用链两端都进入了 `RaidStrategyWorker_Siege`，因此当前证据不能证明“主机和客户端选择了不同的袭击策略”。更可能是同一策略下的派系、Pawn 生成参数、静态缓存或地图上下文已经不同。
- `local_logs.txt` 是累计日志；末尾的 `IncidentRaidFactionContext` 和 `GenerateAnything_Impl` 记录接近检测点，只能作为相关性证据，不能单独证明是最早的分叉点。

## 发现的代码缺口

`Patch_IncidentRaidFactionContext` 原来只补丁化
`RimWorld.IncidentWorker_RaidEnemy.TryExecuteWorker(IncidentParms)`。
bundle 的 metadata 显示 `IncidentWorker_RatkinGuerrillaTunner`、`RatkinGuerrillaTunnerB` 和 `RatkinGuerrillaTunner_Another` 有自己的 `TryExecuteWorker`，它们只命中了通用 Incident Rand 稳定补丁，没有进入地图玩家派系和 `Game.currentMapIndex` 上下文补丁。

这会留下一个具体风险：自定义袭击工作器在 `ResolveRaidFaction`、策略/到达方式解析、Pawn 生成及其 mod postfix 中读取到的 `Faction.OfPlayer`、`Find.CurrentMap` 与事件目标地图不一致。即使最后两端都显示为 `Siege`，也仍可能生成不同的 Pawn/Thing ID，随后表现为 map random/order desync。

## 本次代码调整

`Source/MP_MeowOnlineShop/Patch_IncidentRaidFactionContext.cs` 现在会：

1. 扫描当前已加载程序集中的所有具体 `IncidentWorker_RaidEnemy` 子类。
2. 对每个唯一的 `TryExecuteWorker(IncidentParms)` 方法安装同一套上下文 prefix/finalizer；继承基类方法的类型会按 method handle 去重。
3. 启动时记录补丁数量及少量派生工作器示例。
4. 每个工作器类型最多记录一次目标地图、玩家派系、事件派系、策略、到达方式和点数，便于下一轮验证“袭击种类/参数是否不同”。

## 构建状态

已使用独立输出目录构建候选 DLL：

`BuildOutput/Desync549_550_ContextCandidate/MP_MeowOnlineShop.dll`

SHA-256：`1BBAD14276B96C652132491831FBCBA80D8FE34A5EC6C5B1358A1B3F59F8EA38`

构建结果：0 warning、0 error。该候选 DLL 尚未复制到 `1.6/Assemblies`，也未打包或推送正式 DLL。下一步应使用它做双端运行验证，重点检查启动日志中的 `patched=` / `derived=`，以及每个实际执行的袭击工作器诊断行是否在主机和客户端一致。
