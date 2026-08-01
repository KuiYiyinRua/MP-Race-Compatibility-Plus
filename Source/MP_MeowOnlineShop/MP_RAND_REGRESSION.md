# 联机随机数回归检查清单（MP Rand）

在发布或合并 Rand 相关改动后，建议在**双机**环境下按下列场景验证，确认无 `Wrong random state on map/world` 与 `Random state from commands doesn't match`。

## 环境

- 两台客户端 Mod 列表、顺序、版本**完全一致**（含 Rigor Mortis、VoiceroidAsAnimal、喵喵电商等）。
- 使用与线上一致的 RimWorld / Multiplayer 版本。

## 场景

1. **大规模战斗**  
   - 地图 2+ 上生成大量敌对单位，交火 ≥10 分钟。  
   - 观察：`last valid tick` 连续增长，无随机状态 desync。

2. **战斗中 UI**  
   - 战斗进行时反复选中种植区 / 堆料区 / 钓鱼区，打开 Gizmo。  
   - 观察：无掉线、无随机校验错误。

3. **空投 / 弹射包**  
   - 同一 tick 内触发多次空投落地（电商购买、贸易包等）。  
   - 观察：落地与解包无 map Rand 报错。

4. **中途加入**  
   - 主机在战斗中，客户端加入或重连。  
   - 观察：加入后短时内无 `tick -1` 类随机错误。

5. **Rigor Mortis（若启用）**  
   - 棺材 Tick、恢复工作、Fall 开发者操作、僵死相关 Gizmo。  
   - 观察：无 Rand 栈相关 desync。

## 日志关键字（应避免）

- `Wrong random state on map`
- `Wrong random state for the world`
- `Random state from commands doesn't match`
- `Desynced after last valid tick`

## 可选诊断

- 开启模组内与 Rand 相关的 Trace（如 `ModDebug.EnableRigorMortisTrace`、`EnableDropPodTrace` 等）时，确认 Prefix/Finalizer 成对、无异常吞栈。
