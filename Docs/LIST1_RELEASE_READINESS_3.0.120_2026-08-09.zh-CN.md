# list1.xml 联机兼容稳定发布复核（3.0.120）

> 复核日期：2026-08-09  
> 输入文件：`H:/桌面/list1.xml`  
> 输入 SHA-256：`B4E0167FA6B01AC6DD6159AC2EC00AE23B1C75C3CF38B2B1EF93A22A4214AF4B`  
> 清单规模：458 个去重后的 `activeMods`（XML 中 464 行，6 个 Ludeon 核心/DLC 重复）  
> RimWorld：`1.6.4850 rev645`  
> 最终候选：`3.0.120-list1-stability-candidate-r5`  
> 候选 DLL SHA-256：`F0AC2884286C64CBA304825578EA5AD18519F93BB2BF0058962DAFAF25688E73`

## 1. 结论

本轮已经修复原审计中能够由本兼容项目负责、且有明确源码或运行时证据的补丁缺口。最终候选能够零警告、零错误构建；完整 list1 的最终双端启动/加入结果见第 3 节。

按用户要求，本轮没有把 120k 长稳、双地图、异步时间和反复重连列为放行条件。因此，“能正常启动并加入、补丁能够完整注册”可以确认；“458 个模组的每一种玩法入口都经过长时间动态覆盖”不能据此宣称。剩余风险见第 4 节，不应被隐藏为已经实测通过。

## 2. 已解决问题

| 原问题 | 修复结果 |
| --- | --- |
| Gravship 生命周期保护是未编译死代码 | 已加入项目并在 bootstrap 显式注册；运行日志确认 3 个保护目标生效。 |
| Ratkin Weapons transpiler 丢失 IL label | 改为原位修改指令，保留 labels/exception blocks；2/2 随机入口与 Bayonet/Trap 同步目标均解析。 |
| Axolotl 1.6 VerbTracker 字段漂移 | 同时兼容 `verbs` 与 `allVerbs`；启动确认补丁生效。 |
| RJW UAP 与 Menstruation 类型/签名漂移 | 修正 `rjw.Genital_Helper.has_male_bits` 与 `RJW_Menstruation.HybridExtension`；两项启动解析均为 true。 |
| AcceptJoiner PersistentDialog 命名空间漂移 | 兼容当前 `Multiplayer.Client.PersistentDialog`；点击已路由至全局命令队列。 |
| Hospitality 误把汉化包当作本体 | `nagisa.orion.hospitality` 已确认是无程序集、无本体依赖声明的纯翻译包；仅在真正的 `Orion.Hospitality` 激活时安装模拟补丁。 |
| Multiplayer world VTR 类型漂移 | 增加当前 `Multiplayer.Client.AsyncTime.AsyncWorldTimeComp` 解析路径。 |
| Storage priority 闭包层级漂移 | 按 RimWorld 1.6 的双层闭包解析 `StorageSettings` 与 `StoragePriority`。 |
| 猜测式 ChoiceLetter 白名单 0/27 | 已停止注册不存在的隐藏回调，保留 MP `PersistentDialog.Click` 正常同步边界和 AcceptJoiner 的定向例外。 |
| Caravan 诊断误补继承方法 | 只处理 `DeclaredOnly` 方法，避免对继承基类目标重复安装。 |
| 整体 `World.Tick` Rand 包装过宽 | 已移除；保留有明确执行器证据的窄作用域补丁，避免掩盖真实分叉。 |
| Shella Backgrounds 误报警 | 原始 list1 不含 `stm.ShellaBackgrounds`；旧兼容层无条件执行才产生失败日志。现已按真实 packageId 门控，并为真正启用时增加主菜单阶段的延迟 API 解析。 |
| RJW Genes 日志 `12/13` 被误判为遗漏 | 反编译核实为 12 个方法中的 13 个无参 `System.Random` 构造点，运行时 12/13 即完整覆盖。 |
| Show Weapon Tallies/MVCF 误导性跳过日志 | 当前 Show Weapon Tallies 1.6 未加载 MVCF 运行时；现明确记录为“不需要 MVCF 兼容”，仅部分 API 命中才报警。 |
| Ancot/Milira projectile 补丁默认关闭 | 移除反射虚调用递归路径，改用 Harmony reverse patch 调用原始基类 `Impact`；兼容保护改为始终安装。 |
| 版本与产物漂移 | About、AssemblyVersion/FileVersion 统一为 3.0.120；候选 DLL/PDB 同批次生成并以 SHA-256 冻结。 |

## 3. 验证证据

### 3.1 构建

- `dotnet build -c Release`：0 warning / 0 error。
- DLL 与 PDB 时间戳、构建批次一致。
- 最终部署文件必须与本页候选 SHA-256 相同。

### 3.2 完整清单短测

- 已完成前一候选的 10,000 tick 双端短测：主机与客户端均加入同一局，`players=2`，直到完成均为 `desynced=False`。
- 最终 r5 使用原始 list1 文件启动；控制器记录的主客机 ModsConfig SHA-256 均为 `B4E0167F...214AF4B`。
- 最终 r5：主机正常启动服务器；主机与客户端均记录 `JOINED`、`players=2`、`maps=1`、`desynced=False`，世界种子均为 `tree`。
- 主机在测试 Harness 终点正常完成，`elapsed=2847`、`desynced=False`；客户端在主机到达终点前完成世界载入并记录 JOINED，随后收到预期的 `ServerClosed`。这满足本轮“完整清单双端启动/加入”验收，不作为长线或动作覆盖证据。
- 主客机均确认投射物保护为 `saveClearing=1, ancotImpacts=2, baseImpactReversePatches=True`；Gravship、VTR、Storage、AcceptJoiner、Ratkin、RJW P1 与 Axolotl 关键目标均成功解析。

## 4. 非阻断但必须公开的剩余风险

1. **未执行长线矩阵。** 按用户要求，没有对 r5 执行 120k soak、双地图、async on/off 或三次冷重连。历史候选出现过 map Rand 长线失步；本轮已移除最可疑的整体 `World.Tick` 包装并完成短测，但不能把短测外推成长线证明。
2. **没有逐一触发 458 个包的所有动作。** 纯 XML、贴图、翻译和本地 UI 包通常不需要同步补丁；带程序集也不等于必然存在模拟写入口。原始全量审计附录中的逐包覆盖分类仍是静态证据边界，而不是 458 个包全部玩法的动态 PASS。
3. **投射物修复只做了静态与启动验证。** Ancot 1.6 的两个实际 `Impact` 方法已反编译核对，递归原因已经消除；本轮未安排实弹发射或重连中飞行投射物场景。
4. **本地有重复 packageId。** `fxz.ratkin.facialanimation` 同时存在于 `2852881266` 和 `3625762924`。主客机若选择不同副本，程序集、XML 或资源哈希可能不同；发布整合包应只保留同一明确版本。
5. **第三方内容加载告警不属于本兼容 DLL。** 完整清单日志仍包含 RJW Menstruation 对缺失 `Yuran_Race`/`Alien_Moyo` 的 PatchOperation 失败、若干缺失 SoundDef，以及至少一个 short-hash collision。它们需要由对应内容包或整合包清单修复，不能通过同步注册消除。
6. **性能改写组合仍应保守。** 清单同时包含多个 Pawn tick/TPS/缓存优化模组。本项目对已知不安全入口做了禁用或门控，但没有在本轮证明所有第三方版本组合的长期确定性。

## 5. 发布目录要求

正式目录 `1.6/Assemblies` 只应保留同批次的：

- `MP_MeowOnlineShop.dll`
- `MP_MeowOnlineShop.pdb`

`0Harmony.dll`、`0Harmony.pdb`、`MP_MeowOnlineShop.RjwP1Harness.dll` 和任何 LongRun/Test Harness 都是依赖副本或测试载荷，必须移出正式发布目录。

最终清理结果：以上文件已移动到 `TestValidation/ReleaseArtifactsRemoved_3.0.120_r5` 留档；正式目录仅剩同批次的 DLL 与 PDB，部署 DLL SHA-256 与最终候选完全一致。

## 6. 发布判定口径

- **允许表述：** 3.0.120 在当前机器、当前完整 list1 下可构建、可加载、可双端启动并加入；已识别的 required target 漂移均已修复或证明为不适用。
- **不允许表述：** 已经动态验证 458 个模组的全部功能、长线绝不失步、双地图/异步时间/重连矩阵全部通过。

更早的全量发现、逐包分类和历史失败证据见 `Docs/LIST1_FULL_MP_COMPAT_AUDIT_2026-08-09.zh-CN.md`；本文件是修复后的发布复核增量结论。
