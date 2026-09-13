# [MP] 联机兼容补丁

联机交流群：432965131

适用于 RimWorld 1.6 Multiplayer，补充官方兼容包。以下为已有补丁覆盖的模组，受版本和功能范围限制：

- Meow Framework / Meow Online Shop
- MoeLotl Race
- MoeLotl: Rigor Mortis
- Raven Race
- Wolfein Race
- Wolfein Race GFI Expand
- Wolfein Allegiance
- Wolfein Black Science Expand
- Milira Race
- Milira Tech: Milian Modification
- Milira Event Story Expand: The Tale of Milira
- Milira Faction: Milira Imperium
- Xiyue's Milira Expanded
- 米莉拉角色拓展 / MiliraXian NeiyuLaw
- Milira Expansion: YaoYao
- Milira Addon: Fianchetto Variation
- Milira: Wings of Democracy
- Sariel Milira Kiiro Attire Expaned
- Valkyrie Gunship
- ExileBrandLib
- ExileBrandTaskExend
- Ancot Library
- Ariandel Library
- ChezhouLib
- Kiiro Race
- Kiiro Story: Events Expanded
- NewRatkinPlus
- Ratkin Weapons+
- Ratkin Knights+
- Ratkin Anomaly+
- Ratkin Underground+
- [OA] Ratkin Faction: Oberonia aurea
- [OA] Oberonia Aurea Framework
- [OA] Ratkin Scenario: Snowstorm Orphan
- Maru Race
- Nivarian Race
- Nivarian Mental Harness
- Nivarian: Apparel Store
- Nivarian Race: Draconiture
- Nivarian Race: DraconicMilitary
- Monolyn Race
- Sylvie Race
- Dragonian Mix
- Smelted Loong
- Insect Girls
- Secretary Nexus a clone race
- Cinders of the Embergarden
- kemomimihouse Kz
- kemomimihouse HardworkingKz
- Voiceroid as Animal
- Shella Backgrounds
- RimJobWorld
- RimJobWorld Pedophilia Extension
- RimJobWorld - Extension
- RJW Sexperience
- RJW Genes
- RJW Animal Gene Inheritance
- RJW Menstruation Cycle
- ElToros RJW Menstruation - Resources
- Cumpilation
- Family Overhaul
- Peculiar Institution
- RimJobWorld - Brothel Colony
- RJW Ero Traders
- RJW-Events
- RJW Consensual Non-Consent
- Privacy, Please!
- RimJobWorld - Onahole Extension
- RJW Now with balls! . . . and Ovaries I guess.
- RJW-SexSlaveCraft
- RJW Unleashed Framework
- Humpmaker Dryad
- RaddusX's Demons
- Nudity Matters More
- Equal Milking
- Romance On The Rim PE
- RimWorld Animations
- Ultimate Animation Pack (With Voice)
- Sized Apparel
- Melee Animation
- Perspective Shift
- PA's God Hands
- Achtung! 4.1.14
- Draft Anything 2.0
- Down For Me
- Defensive Positions
- Search and Destroy (Continued)
- [XND] Targeting Modes (Continued)
- Vanilla Melee Modes
- Tactical Crawling
- AutoBlink
- Sandevistan Implant
- Smart Pistol
- Cluster Projection
- The Dead Man's Switch
- The Dead Man's Switch - Power Armor Expanded
- [RH2] Rimmu-Nation² - Security
- True Shooting-Wall
- Show Weapon Tallies
- Visual Brutality
- Blood Animations
- RW Beheading
- COF's Execute cotinue / More Torture
- [QW] Archotech Implants Expanded
- Eternal Pawns
- WVC - Work Modes
- Auto Dissector
- Auto Cutter
- Hospitality (Continued)
- Go Explore!
- I will be back
- Elite Raid
- Ancient Amorphous Threat
- Quarry
- Utility Columns
- Vanilla Plants Expanded - Mushrooms
- Static Quality
- OgreStack
- Adaptive Storage - Global Settings
- Designator Shapes
- Blueprints / Blueprints Forked - 1.6
- Dubs Mint Menus
- Nice Bill Tab
- Vehicle Framework
- Tactical Fulton Extraction System
- Almost There! Fork
- RPG Style Inventory Revamped
- RPG Dialog
- [NL] Facial Animation - WIP
- [NL] Dynamic Portraits
- Simple FX: Splashes
- Performance Optimizer

- ReGrowth 2
- Yet another Optimizer / Kingfisher

## 3.0.128 更新（2026-09-13）

- ReGrowth 2：隔离秋季落叶判定对本地绘制缓存的依赖，修复不同视角可能造成的生成物与 Thing ID 差异。
- 多派系外交：减少上下文反射调用、临时分配和重复查询，保留派系顺序、外交规则及恢复计时；不降低模拟 Tick 频率。
- 新增独立并行绘制兼容模块：将 Multiplayer 绘制上下文改为线程局部、支持嵌套恢复，并校验目标补丁结构；不等于允许任意多线程模拟优化。
- 保留近战动画的组件查询优化，以及 YaOpt / Kingfisher 物品索引移除边界修复。

测试范围：RimWorld 1.6.4850 rev646、Multiplayer 0.11.5+4a3be27-dirty。316 模组组合在同机双进程、双地图、多派系、异步关闭条件下运行超过 120,000 个地图/世界 Tick，最终随机数状态、Thing ID 及 28 份角色/外交快照一致；包含三次非主机招募。ReGrowth 使用另一套 318 模组、三地图、异步开启组合完成 10,008 个共享 Tick 的短测。未完成跨电脑、冷重连或 ReGrowth 长测，不代表所有模组的所有功能均已验证。

Dubs Performance Analyzer 固定工作量采样中，外交自然恢复热点耗时约降低 29–31%，记录查询约降低 27%；整体 TPS 约 153–155，未证明整体 TPS 提升。分析器仅用于测量，不属于本模组发布内容。

已知限制：该组合仍有 Kiiro 剧情空地图告警、Defensive Positions 旧 Multiplayer API 告警及 EliteRaid 补丁告警。已有补丁覆盖不等于这些告警已全部修复。建议所有玩家统一版本后完全重启游戏。


## 多派系外交（3.0.127）

- 同一种开局建立的不同派系分别保存外交状态，A 的和解、赠礼和交战不会覆盖 B。
- 普通开局保留 Milira 的永久敌对；Milira、Kiiro 开局免除此限制。Milira 对教会的敌对只影响对应玩家。
- 移除定期强制中立，按派系分别计算好感度上限、后台重算和自然恢复计时。
- 旧存档保留基础好感度，新版恢复计时从零开始；无法归属的旧和解状态不自动复制，普通派系可能重新受敌对限制。
- 此项覆盖外交隔离，不代表 Milira 全部剧情、角色和任务已按玩家拆分。请所有玩家更新并重启游戏。

## 额外功能

- 战斗速度解锁：取消战斗强制 1 倍速，遵循联机统一速度控制。
- TPS 设置：多档预设，强度和参数可调。
- 多地图调度：按战斗、警戒和空闲状态调整更新频率，减少多基地开销；实验功能，默认关闭。
- 界面与日志减负：减少警报检查、重复 UI 操作及卡顿日志。

兼容性受版本、加载顺序及组合影响，无法保证任意整合包均不失步。

需要 Multiplayer；本模组放在列表末尾。所有玩家使用相同模组及版本；不要同时启用其他 Tick、TPS 或时间膨胀调度模组。

作者：尹怨怨

GitHub：https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus

详细兼容及验证记录：https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus/blob/main/Docs/releases/3.0.128.md
