# MissileGirl 联机性能方案审计

审计对象：Steam Workshop `3712928623`，包 ID `vr.missilegirl`。本地包自带完整
`Source/RocketMan.sln` 源码；项目仓库为
<https://github.com/ViralReaction/MissileGirl>。

## 来源指纹

| 组件 | SHA-256 |
|---|---|
| `Cosmodrome.dll` | `F1366C5259D701BB2F6C6D8E66DF74894F444EE5DA6ABE9A211FAB6BF2B50ED5` |
| `Gagarin.dll` | `D83EDC9FE3AE0381A71EE460F766563F1CB078F61211D8388614BAB3B0E1250C` |
| `Proton.dll` | `A1C719CF01E71DA7AE181BDDDFDE9F59B12F96DDECB7686DA8A0D6122345579A` |
| `Soyuz.dll` | `FDE657D6AAE1410964CF585BD4BD3A2EC9B8E3BE8C16D751C78A34BC3AFF0EAB` |

MissileGirl 自身在 `About.xml` 与 `RocketRules_Incompatibilities.xml` 中把
`rwmt.Multiplayer` 标为不兼容，仓库 README 也说明 Multiplayer 尚未测试。因此本次
不加载整套 MissileGirl，只审计并移植能证明不会影响同步模拟的思路。

## 风险分级

| 方案 | 决策 | 联机理由 |
|---|---|---|
| XML/Def 启动缓存（Gagarin） | 否决 | 缓存陈旧或载入次序差异会改变 Def 树与联机握手哈希；收益只在启动阶段 |
| 自适应 Stat 缓存 | 否决 | 正确性依赖各模组完整发送 PawnDirty 通知；当前大型模组表无法证明失效覆盖完整 |
| 温度、房间、美观缓存 | 否决 | 同 Tick 内也可能发生装备、Hediff、房间或位置变化；旧值会进入工作分配与 AI |
| Pawn/WorldPawns Tick 降频 | 否决 | 直接改变模拟时序、AI、Hediff 与世界 Pawn 生命周期 |
| `Lord.Notify_PawnDamaged` 跳过 | 否决 | 跳过伤害信号与状态机迁移，属于明确的玩法语义变化 |
| Deep Drill 快速判断 | 否决 | 用 `Biome.hasBedrock` 替代原版当前位置资源查询，结果并不等价 |
| Repairable/Lister 快速路径 | 否决 | 改变集合成员和迭代输入；对工作分配有直接影响 |
| Alert UI 降频 | 有条件采用 | Alert readout 是本地 UI 缓存；可严格限制到中优先级且保留强制移除与同步命令 |

## 采用的候选

`Patch_MpSafeAlertThrottling` 只拦截
`AlertsReadout.CheckAddOrRemoveAlert(Alert, bool)`：

- 只在真实 Multiplayer 会话、且不在同步命令执行期间启用；
- 只降频 `AlertPriority.Medium`；
- `TimeSpeed.Ultrafast` 完全旁路，避免低 UI 帧率时只有查表开销、没有命中收益；
- `High`、`Critical`、`forceRemove=true` 始终执行原版；
- 第一次检查立即执行，后续按共享游戏 Tick 间隔复查；
- 不写入存档，不注册同步命令，不修改 Alert 报告结果；
- 不使用依赖固定 IL 布局的 transpiler；目标签名不匹配时自动保留原版；
- 设置中可一键关闭，间隔限制为 30–600 Tick；
- 遥测输出实际执行、跳过次数、观测耗时与估算节省耗时。

## 验证记录

已完成：

1. 测试器使用固定 Verse Rand `2072701` 生成新世界；两轮世界种子均为“军靴”。
   直接复用旧存档会因当前大型模组表的载入期 Def 变异触发 Multiplayer Def
   mismatch，因此该方法被明确否决，测试器也不会绕过此安全检查。
2. 3,000 Tick 双实例固定种子基线：主机 48.72 TPS、客户端 66.09 TPS，
   `desynced=False`。
3. 初版候选未对 Ultrafast 旁路时：主机 46.72 TPS、客户端 61.23 TPS，
   且隐藏窗口自然更新没有降频命中。该版因负收益被否决。
4. 正常速度压力探针：
   - 100 次中优先级检查：执行 1、跳过 99；
   - 100 次高/严重优先级检查：旁路 100；
   - 1 次 `forceRemove=true`：旁路 1；
   - 两端加入同一“军靴”世界，探针期间无 desync。
5. Ultrafast 全旁路版的同种子 3,000 Tick 回归：主机 49.65 TPS、客户端
   68.66 TPS，两端 `desynced=False`；两端遥测计数均为 0，证明极速模拟路径
   未进入字典或计数逻辑。相对基线约为 +1.9% / +3.9%，处于短测运行波动范围，
   因而结论是“未测得性能回归”，而不是宣称极速模式获得性能提升。

6. 版本 `3.0.18` 最终归档的 120,000 Tick 双实例浸泡：
   - 被测与发布 DLL SHA-256 均为
     `BDE77E45B0003F7A4D17C047E8DF72B74E40FCA98F681DB5E9221E5C6A47CD4C`；
   - 主机在 Tick `125850` 完成，120,000 个共享 Tick 用时 2212.167 秒，
     测得 54.25 TPS；
   - 客户端在 Tick `125857` 完成，120,000 个共享 Tick 用时 2195.822 秒，
     测得 54.65 TPS；
   - 两端全程保持 2 名玩家、1 张地图、`desynced=False`；
   - 日志未出现 `Sync Error`、`Inconsistent player`、`desynced=True`、
     `PacketReadException`、测试不变量失败或提前会话丢失；
   - 压力探针再次确认中优先级执行 1/跳过 99，高/严重优先级旁路 100，
     `forceRemove=true` 旁路 1；
   - 控制器正常结束，临时测试程序集已从发布目录移除。

## 最终结论

采用 `Patch_MpSafeAlertThrottling`，但不加载 MissileGirl 本体，也不采用其
Stat、温度、房间、美观、WorldPawn、战斗通知或启动期 Def/XML 缓存。当前证据
支持“警报 UI 降频在已测负载下没有联机稳定性回归”；性能收益取决于玩家实际
启用的中优先级警报数量与单次计算成本。Ultrafast 路径明确保持原版，因此不把
短测 TPS 波动宣传为模拟性能提升。
