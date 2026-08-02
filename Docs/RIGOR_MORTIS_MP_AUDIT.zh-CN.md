# MoeLotl: Rigor Mortis 联机兼容审计

> 状态：进行中。本文是 `fxz.moelotlzombie.update` 的来源、覆盖面与分阶段测试证据索引；只有通过文末全部门槛后才可称为完整联机兼容。

## 1. 目标身份与源码权威性

- 安装路径：`H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\3454053400`
- 标题：`MoeLotl: Rigor Mortis`
- 作者：`Feng Xinzi`
- packageId：`fxz.moelotlzombie.update`
- Workshop ID：`3454053400`
- 支持版本：RimWorld 1.5、1.6；当前审计目标为 1.6。
- 主程序集：`1.6/Assemblies/RigorMortis.dll`
- 程序集身份：`RigorMortis, Version=1.0.0.0`，无强名称公钥标记。
- 主程序集 SHA-256：`BF4D79001802B55B32D3B7AB3DF52CCB7495D0B515ADE89213A7962B5E1A05FF`
- 主 PDB SHA-256：`633345EC986D8C68AC346CED597A3F948CCF9D387D0727BF752F09966711CA4F`
- 随包源码：`1.6/Source/RigorMortis`，296 个 `.cs` 文件，约 23,450 行。
- 权威性证明：发布的 `1.6/Assemblies/RigorMortis.dll` 与源码目录的 `1.6/Source/RigorMortis/obj/x64/Debug/RigorMortis.dll` 哈希完全一致；发布 PDB 与对应 `obj/x64/Debug` PDB 也完全一致。因此随包源码可作为当前已安装 1.6 二进制的精确权威来源。

### 条件程序集

| 激活条件 | 程序集 | SHA-256 |
|---|---|---|
| `Nals.FacialAnimation` | `1.6/Mods/FacialAnimation/Assemblies/RigorMortis.FA.dll` | `802F2E661E0728CF378D9F1F17936B4F501060232A22FABF0F63B2B5503B7D91` |
| `HenTaiLoliTeam.Axolotl.FactionExpand` | `1.6/Mods/MoelotlCrimson/Assemblies/RigorMortis.Crimson.dll` | `E733277E23E9C554BE9C38FEDD3BEEE94D44022B7255A0562ED6CC6928B741EE` |

`MoelotlCrimson` 文件夹还捆绑了 `Axolotl.FactionExpand.dll`；它属于依赖模组代码，不在本模组补丁的默认写入范围，但会在条件加载组合测试中冻结哈希并检查交互。

## 2. 已确认依赖与加载变体

硬依赖：

- `brrainz.harmony`
- `HenTaiLoliTeam.Axolotl`
- `HaiLuan.CustomQuestFramework`

条件加载变体：

- 无 Facial Animation、无 MoeLotl Faction Expand；
- 仅 Facial Animation；
- 仅 MoeLotl Faction Expand；
- 两者同时启用。

完整性结论必须覆盖基础组合，并对两个条件程序集进行静态审计；涉及其功能的补丁必须在对应组合下完成双实例定向测试。

## 3. 审计与验证门槛

1. 枚举 296 个源码文件中的全部状态变更入口、UI 回调、目标选择、随机数、无序枚举、序列化、组件构造、周期 Tick、任务/领主/事件/任务链和世界/地图切换路径。
2. 将每条路径映射为：本地 UI、Multiplayer 已覆盖、需要同步、需要确定性隔离、仅调试、或需要运行证据。
3. 逐项核对本地 `Multiplayer-master` 的真实实现，避免重复同步。
4. 每批补丁必须构建成功，并用元数据/反编译确认注册目标与实际签名一致。
5. 隔离 host/client 必须使用相同的目标 DLL、补丁 DLL、配置、模组顺序和世界输入哈希。
6. 从非主机客户端执行真实入口；断言双方都执行且状态相等。
7. 依次通过启动/加入、约 10k–20k shared ticks 定向冒烟、多地图/异步时间/三次冷重连，以及至少 120k shared ticks 长时 soak。
8. 最终发行 DLL 必须与通过长测的归档候选哈希一致，测试驱动不得残留在发行目录。

## 4. 当前阶段

- [x] 目标身份、依赖、加载变体、程序集及源码权威性已冻结。
- [ ] 全量 mutation/action inventory。
- [ ] Multiplayer 内建覆盖对照。
- [ ] 补丁缺口与最窄同步边界。
- [ ] 分批构建和静态检查。
- [ ] 隔离双实例测试矩阵。
- [ ] 发行完整性与最终报告。

