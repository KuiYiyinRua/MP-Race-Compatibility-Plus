# [MP] 联机兼容补丁

## 3.0.142（2026-09-30）

全量重新构建并发布全部 22 个生产模块及匹配源码，包含所有现有试验性功能。包含持久化房主配置、据点交易修复、DesyncBatch 1.0.7，以及炮台休眠、渡鸦工业与现有性能优化模块。试验功能保留现有开关及默认值。

验证：22 个项目编译通过；分类设置 363 项断言、DesyncBatch 60 项离线行为检查通过。据点交易两个修复文件与此前实测源码逐字一致；核心其他配置代码已变化，因此此前专项测试不代表本次完整组合通过。最终 3.0.142 全模块组合未重新完成双端、多地图、冷重连与 120,000 共享 tick 长测，HugsLib 持久化配置运行验证仍待完成。所有玩家应安装相同完整版本并完全重启。

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

## 3.0.128 历史更新

保留 ReGrowth 秋季落叶确定性、线程局部绘制上下文、外交查询优化及 YaOpt/Kingfisher 修复。历史性能采样及其独立测试条件见仓库发布记录，不作为本次修补的长测通过证明。

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

详细兼容及验证记录：https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus/blob/main/Docs/releases/3.0.136.md
