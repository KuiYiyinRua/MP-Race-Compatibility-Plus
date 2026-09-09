# Nivarian 四项扩展联机兼容

本补丁针对 RimWorld 1.6、rwmt.Multiplayer，扩展现有 Nivarian 本体兼容。四项扩展均须先安装 Nivarian Race；Mental Harness 还需要 Royalty。

- Nivarian Mental Harness：3720877013，`keeptpa.NivarianRace.MentalHarness`。
- Nivarian: Apparel Store：3747540804，`keeptpa.NivarianRace.ApparelConvenienceStore`。
- Nivarian Race: Draconiture：3686517288，`keeptpa.NivarianDraconiture`。
- Nivarian Race: DraconicMilitary：3735573834，`keeptpa.NivarianDraconicMilitary`。

## 实现

新增 `Source/RaceTrioCompatibility/NivarianExpansions.cs`，由 `Bootstrap` 在 Multiplayer 启用时按目标包注册。增补程序集版本为 `Meow.RaceTrioCompatibility 1.1.0`；核心程序集继续使用 3.0.125。

家具扩展同步工程无人机维修阵营/灭火设置、水培灯开关，以及重塑舱确认和取消。同步完整方法，包含派生的电力、照明、无人机设置传播和治疗材料队列更新。治疗选择使用 Multiplayer 已提供的 Hediff 序列化。

军事扩展同步能量护盾、灵术耳饰、轮椅开关、冬幕炮塔射界确认、信标命名与传送。传送仅传递 Pawn、地图编号和坐标，再查找当前信标；重命名同时更新建筑与登记表。保留旧式无人机开关及群体选中目标同步，选择列表在发出命令前按 Thing ID 排序。当前 XML 中的军事攻击枢纽使用 Nivarian 本体的 `NivarianDroneHubComp`，沿用已有核心同步。

旧式悬浮无人机的移动半径在原 `SpawnSetup` 中会覆盖存档值并重新调用 Unity 随机数，因此多人模式改用 Thing ID 派生的稳定值；扫射接近路径的随机角度改用 Verse 随机流。单人模式保留原行为。

`Mothership.cs` 补充炮艇支援：本地收集目标格，再同步母舰请求、地图和格子，双端重新检查条件后执行生成及消耗。原本的选格支援基类继续覆盖军事轰炸支援。

Mental Harness 的施法继续通过原有能力/下达工作同步，效果在模拟中执行。服装扩展的穿脱、制作和心情使用原版流程。粉笔图案选择已写入下达工作的 `job.count`，无需额外同步本地选择。显示、音效、书柜视觉偏移及开发者操作不额外同步。

## 使用与验证范围

加载顺序：Multiplayer、Nivarian 本体、所需扩展、官方 Multiplayer Compatibility（如使用），最后加载本补丁。所有玩家使用相同模组版本和游戏相关设置。

本次按需求采用主要功能的双端快速组合测试，不以长期压力测试或所有模组组合保证为目标。以下两组实机结果对应本次发布产物。

测试通过原模组实际 Gizmo 回调、对话框结果方法与炮艇选格回调发出命令，并检查双方模拟状态。灵能检查通过测试专用同步入口调用三种能力的实际效果执行器，不代表完整施法界面逐项覆盖；服装检查实际生成与穿戴。未逐项实测全部服装配方、所有武器战斗、耳饰、旧式无人机目标、治疗资源消耗至结束，也未进行重连/异步多图/多派系/极端或长期压力测试。测试程序集不会发布。

## 2026-09-09 快速实机记录

隔离游戏：RimWorld 1.6.4871 rev591、Multiplayer 0.11.5+a481546、Harmony、Prepatcher、HAR、全部官方 DLC，Nivarian 本体与上述四扩展，并合载 Monolyn / Voiceroid。单地图、同派系、同步时间；两个进程使用独立配置和存档目录，正式直连达到 ClientPlaying 后才发出动作。

- `combined-r7-20260909-202238`：18 个 activeMods 条目，客户端发起操作。双方六轮扩展断言、六轮原三种族回归、三轮炮艇支援均通过；随机样本、灵灯/炮艇 ID、炮塔角度及治疗队列逐行一致。地图各推进 22,965 tick，`desynced=False`。

- `official-r8-20260909-202729`：加入官方 Multiplayer Compatibility，19 个 activeMods 条目，主机发起操作。双方同样通过六轮扩展断言、六轮原三种族回归及三轮炮艇支援，状态与 ID 逐行一致；地图各推进 22,965 tick，`desynced=False`。官方兼容 DLL SHA256 为 `E5096C0D40A21A97C5792AE8BBBEEED7305D325239FE30E9BCC00CD2FD32566B`。

验证代码与原始证据保存在本地 `BuildValidation/NivarianExpansions_20260909`，每轮归档输入配置/存档/About/候选 DLL，并记录精确进程及文件哈希。前期 r1–r6 中存在测试夹具错误或主动中止（未使用类型、Gizmo 选择上下文、转向完成时机、限时炮艇累计计数），不计为通过。修正测试后仍使用相同生产候选，未为通过测试修改原模组行为。启动中的既有素材/反射警告保留在日志，不把“无不同步”写成“零日志警告”。

发布候选：`Meow.RaceTrioCompatibility.dll` 1.1.0.0，SHA256 `2842DF47F3D29711CE3E5061ABC308A8A5500F3AF0ADDC11AF3B68853C24E89E`，MVID `d1e9c1c2-b563-4a7b-af6c-c2efa0ae3f17`。核心 DLL 3.0.125 未重编译。生产项目与独立仓库克隆均编译零警告、零错误。

## 二进制来源

依据安装包元数据、XML 与对应 DLL 的 ILSpy 反编译。未找到随附 C# 源码或匹配的公开源码仓库；不把近似源码当作已安装版本。

- Mental Harness，20260807103447：`571BF3270843151DA07F979EF5A3D51CED16DC582898444A2F43E49075150717`。
- Apparel Store，20260821100558：`48138BDFBAD25181BAB24AC2A8EFDE5E9A949CDDDDE13360384148A417D62679`。
- Draconiture，20260821100558：`E99A1B35D2C3F36A634BEC3411399EEB4563755F0603C78BF3398C7776C97E92`。
- DraconicMilitary，20260821150719：`420E15E6A23E5D6B35F04E9A64D11CAAE5E745433DD3E5DC9C7E81C6B45C7AB4`。

## English summary

Adds targeted Multiplayer synchronization for the Nivarian furniture and military expansions, retaining the existing ability and vanilla apparel/job synchronization for Mental Harness and Apparel Store. Covers engineering settings, hydroponic lighting, reshaping selections, equipment toggles, turret cone confirmation, beacon naming/teleportation and gunship support. Multiplayer movement randomness is made repeatable. The add-on assembly is version 1.1.0; the core assembly remains 3.0.125. All peers must use matching mod versions and gameplay settings. Validation is scoped to short combined host/client tests, not exhaustive or long-duration testing.
