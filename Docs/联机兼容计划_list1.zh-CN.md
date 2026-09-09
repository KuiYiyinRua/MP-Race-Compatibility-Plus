# list1.xml 模组联机兼容计划

## 2026-09-09 更新：Nivarian / Monolyn / Voiceroid

以下三项已补齐主要操作的联机同步，并完成两个组合的双端快速回归。具体代码覆盖面、逐轮证据和未测试范围见 [专项记录](Nivarian-Monolyn-Voiceroid-Multiplayer.md)。本节更新这三项的当前状态，下面原有盘点数量与阶段结论保留为历史记录。

- **Nivarian Race**（3624805128，`keeptpa.NivarianRace`）：新增招募候选生成/购买、加入选择、研究、电网、生产设置、无人机/武器控制、Nira 与穿梭机模块、母舰支援和剧情操作同步。实际快速验证覆盖招募舱落地、流浪者加入、母舰升级、研究、电网与打印机队列/效率/品质。
- **Monolyn Race**（3742031864，`ASEL.MonolynRace`）：完整开关回调同步，补齐滑条、生产选项和光导管字段 Watch，持久化编号改为确定性生成。实际快速验证覆盖工作开关与光导管连接字段的双向状态变化。旧记录中的“注册 SyncField”并不等于已经监视 UI 编辑，本轮补上相应边界。
- **VoiceroidAsAnimal**（2073559411，`hatena.VoiceroidAsAnimal`）：补齐言灵护盾/带宽、九尾技能到 Job 的完整回调、主动技能及复活解除、玩偶修复设置；修复九尾弹种与技能分配的 GUID 随机排序。实际快速验证覆盖言灵切换、三次九尾分身实际生成和随机弹种序列一致性。

验证组合为“三目标 + 必需框架 + 全部官方 DLC”以及“同组合 + 官方 Multiplayer Compatibility”，最终两轮分别由客户端、主机发起操作，双方均通过六阶段状态断言，各推进 15,960 地图 tick，无检测到的不同步。按本次要求不做边缘、极端或长时测试；未宣称逐项技能、完整剧情、异步多地图或任意模组组合均已验证。

补充程序集：`1.6/Assemblies/Meow.RaceTrioCompatibility.dll`；发布身份记录：`BuildValidation/RaceTrio_20260909/release.json`。中英文工坊介绍文档已加入/更新这三个模组的兼容范围。

> 性质：联机兼容设计 + P0/P1 代码补丁 + 逐步回归文档。
> 日期：2026-08-07
> 输入：`H:/桌面/list1.xml`（RimWorld 1.6.4850 rev645）
> 结论状态（2026-08-09）：最新 `list1.xml` 为 464 个 activeMods 条目；已修复 RJW P1 / God Hands 启动解析、Perspective Shift 开火、移动 JobID 与 Avatar 状态恢复（`EnsureState`）、DateNotifier 季变信件 ID、Milian 浮游护盾单元释放；当前候选 `9D99F8D3...` 的 10k 冒烟与 Perspective Shift 双所有者全阶段定向测试均 PASS（`desynced=False`）；120k 复验仍待跑，正式部署待用户确认。

## 1. 目标

针对 `list1.xml` 中 465 个已启用模组：

1. 盘点当前官方 Multiplayer Compatibility、Multiplayer 核心、以及本仓库 `MP-meow-online-shop` 已覆盖的兼容面。
2. 判定为保证联机稳定性“应该兼容”的模组优先级。
3. 结合现有覆盖情况，设计后续兼容补丁、验证和发布计划。
4. 本阶段只交付本 MD 文档，不修改代码。

## 2. 判定原则

RimWorld Multiplayer（下称 MP）复制命令并在各端确定性重放，不持续同步所有状态。因此：

- 新增 Def、贴图、XML Patch 本身通常不需要专项 MP 补丁；风险来自本地 UI 回调、随机事件/生产、世界或地图 Tick、任务/交易、关系/生殖数据、静态缓存和未排序集合。
- 只有“本地 UI 动作最终会改变模拟状态，且未被 MP 核心或官方兼容包捕获”的路径才需要新增 `SyncMethod` / `SyncField` / `SyncWorker`。
- 补丁必须选最窄的稳定执行点，同步动作或结果，不同步窗口、选中状态、动画或音效。
- 参数不能直接序列化时，用 Pawn/Thing ID、Map、Faction 等稳定标识重建上下文。
- 随机必须在同步命令内执行；集合先排序再消费随机数。
- 已有官方/本仓库覆盖的路径不得重复注册，避免双同步。

## 3. 盘点结果

### 3.1 总量

| 指标 | 数量 |
| --- | --- |
| list1.xml activeMods | 465 |
| 本地 Mods 目录可映射 | 458 |
| 未映射 | 7（6 个官方核心/DLC + `local.mp.racecompatibilityplus` 包 ID 不一致） |
| 带 DLL | 259 |
| 无 DLL | 199（458 中） |
| 官方兼容包显式覆盖（列表内） | 13 |
| 本仓库显式包 ID 覆盖（列表内） | 36 |
| 官方与本仓库同时覆盖 | 2（HugsLib、Dubs Mint Menus） |
| 未显式覆盖且带 DLL | 212 |
| 未显式覆盖且无 DLL | 206 |

> 说明：本仓库大量补丁按“类型名”装配，而不是只按包 ID 字符串判断；因此 36 是保守下界。Milira、Kiiro、Insect Girls、Sized Apparel、Rimworld Animations、Blood Animations、Ancient amorphous threat、Elite Raid、Gravship、True Shooting Wall 等疑似已被类型级补丁覆盖，需以启动解析日志确认。

### 3.2 必须立即修正的包 ID 问题

1. `local.mp.racecompatibilityplus`：`list1.xml` 使用该 ID，但本地 `About.xml` 实际为 `local.mp.meowonlineshop.sellslingshot`。联机前必须统一，否则 MP 会按不同包 ID 判定模组不一致。
2. `nagisa.orion.hospitality`：官方兼容与本地补丁键均为 `Orion.Hospitality`，而安装目录的包 ID 是 `Nagisa.Orion.Hospitality`。现有 Hospitality 补丁不会对这个 fork 自动启用，需要补 ID 别名或改用匹配的官方版本。
3. 其余 458 个包 ID 均能在本地 Mods 目录定位到 `About.xml`。

## 4. 现有覆盖清单

### 4.1 官方 Multiplayer Compatibility 覆盖（13）

| 包 ID | 名称 | 备注 |
| --- | --- | --- |
| adaptive.storage.framework | Adaptive Storage Framework | 官方 |
| dubwise.dubsmintminimap | Dubs Mint Minimap | 官方 |
| mlie.prisoncommons | Prison Commons (Continued) | 官方 |
| merthsoft.designatorshapes | Designator Shapes | 官方 |
| usagirei.pocketsand | Pocket Sand | 官方 |
| dubwise.dubspaintshop | Dub's Paint Shop | 官方 |
| haplo.miscellaneous.training | Misc. Training | 官方 |
| syrchalis.processor.framework | [SYR] Processor Framework | 官方 |
| azuraal.choiceofpsycasts | Choice Of Psycasts | 官方 |
| albion.sparklingworlds.full | Sparkling Worlds - Full Mod | 官方 |
| owlchemist.toggleableshields | Toggleable Shields | 官方 |
| unlimitedhugs.hugslib | HugsLib | 官方 + 本仓库 |
| dubwise.dubsmintmenus | Dubs Mint Menus | 官方 + 本仓库 |

### 4.2 本仓库显式包 ID 覆盖（36）

| 包 ID | 名称 | 备注 |
| --- | --- | --- |
| ilyvion.loadingprogress | Loading Progress | 显式引用 |
| arkymn.slowerpawntickrate | Performance - Slower Pawn Tick Rate | 已做 Tick 补丁，仍需回归 |
| rwmt.multiplayer | Multiplayer | 依赖本体，非独立目标 |
| imranfish.xmlextensions | XML Extensions | 配置热同步相关 |
| hailuan.customquestframework | Custom Quest Framework | 显式引用 |
| bbb.ratkinweapon.morefailure | Ratkin Weapons+ | 显式引用 |
| hentailoliteam.axolotl | MoeLotl Race | 显式引用 |
| fxz.moelotlzombie.update | MoeLotl: Rigor Mortis | 显式引用 |
| ferny.perspectiveshift | Perspective Shift | 专项补丁 |
| ancot.milianmodification | Milira Tech: Milian Modification | 显式引用 |
| ancot.kiirostoryeventsexpanded | Kiiro Story: Events Expanded | 显式引用 |
| moo.kemomimihouse.kz | kemomimihouse Kz | 显式引用 |
| rim.job.world | RimJobWorld | 专项补丁 |
| rjw.sexperience | RJW Sexperience | 专项补丁 |
| rimworld.ekss.rjwex | RimJobWorld - Extension | 显式引用 |
| calamabanana.rjw.brothelcolony | RimJobWorld - Brothel Colony | 专项补丁 |
| vegapnk.cumpilation | Cumpilation | 专项补丁 |
| rjw.menstruation | RJW Menstruation Cycle | 专项补丁 |
| vegapnk.rjw.genes | RJW Genes | 专项补丁 |
| rim.job.world.pe | RimJobWorld Pedophilia Extension | 显式引用 |
| rjw.unleashed.framework | RJW_Unleashed_Framework | 显式引用 |
| rim.job.world.onahole.ext | RimJobWorld - Onahole Extension | 专项补丁 |
| dord.nuditymattersmore | Nudity Matters More | 专项补丁 |
| abscon.privacy.please | Privacy, Please! | 显式引用 |
| c0ffee.rjw.events | RJW-Events | 专项补丁 |
| shauaputa.lewdtrader | RJW Ero Traders | 专项补丁 |
| telanda.rjw.animalgeneinheritance | RJW Animal Gene Inheritance | 显式引用 |
| telardo.romanceontherim.chillkill190.pe | Romance On The Rim PE | 专项补丁 |
| teacher.uap | Ultimate Animation Pack (With Voice) | 显式引用 |
| teheeitsme525.rjwgenderorgansmod | RJW Now with balls! | 显式引用 |
| moth.rjw.cnc | RJW Consensual Non-Consent | 显式引用 |
| eltoro.rjw.menstruation.resources | ElToros RJW Menstruation - Resources | 显式引用 |
| arkymn.performanceesmolas | Performance Esmolas | 显式引用，性能类需回归 |
| palpha.godhands | PA's God Hands | 专项补丁 |
| unlimitedhugs.hugslib | HugsLib | 官方 + 本仓库 |
| dubwise.dubsmintmenus | Dubs Mint Menus | 官方 + 本仓库 |

### 4.3 本仓库类型级疑似覆盖（需启动日志确认）

以下包 ID 未在源码中以包 ID 字符串直接出现，但源码中有对应类型锚点：

| 包 ID | 类型锚点/补丁文件 |
| --- | --- |
| ancot.milirarace | `Patch_Milira*`、`MiliraAddons/*` |
| ancot.kiirorace | `Patch_KiiroStoryEventsMp`、Kiiro 类型 |
| pupa.insectgirls | `Patch_InsectGirlPermanentWoundMp` |
| otyoty.sizedapparel | `Patch_RjwSerializationRandIsolation`：`SizedApparel.ApparelRecorderComp` |
| c0ffee.rimworld.animations | `Patch_RjwPeVoiceRandIsolation`：`Rimworld_Animations.VoiceDef` |
| fuu.bloodanimations | `Patch_BloodAnimationsMp` |
| xmb.ancientthreat.mo | `Patch_AncientAmorphousThreatMp` |
| trigger.eliteraid | `Patch_EliteRaidDeterminism` |
| hailuan.gravshipexpanded | `Patch_Gravship*` |
| laayoune.shootingwall | `Patch_TrueShootingWallMp` |

## 5. 应该兼容的优先级

### P0：必须优先做专项兼容

以下模组带 DLL、未显式覆盖、且属于主动玩法/高扇出或已知 desync 风险面。按“最小可联机基线 → 单簇 → 单包”顺序处理。

#### 种族与种族框架

| 包 ID | 名称 |
| --- | --- |
| solaris.ratkinracemod | NewRatkinPlus |
| rku.ratkinunderground | Ratkin Underground+ |
| rkk.ratknights.core | Ratkin Knights+ |
| fxz.ratkinfaction | Ratkin Faction+ |
| oark.oberoniaaurea.framework | [OA] Oberonia Aurea Framework |
| oark.ratkinfaction.oberoniaaurea | [OA] Ratkin Faction: Oberonia aurea |
| oark.ratkinfaction.geneexpand | [OA] Ratkin Gene Expand |
| oark.ratkinfaction.scenarioexpand.snowstorm | [OA] Ratkin Scenario: Snowstorm Orphan |
| roo.antyracemod | Anty the war ant race |
| asel.monolynrace | Monolyn Race |
| secretarynexus.secretarynexusracemod | Secretary Nexus |
| vamv.maruracemod | Maru Race |
| rooandgloomy.dragonianracemod | Gloomy Dragonian race |
| kalospacer.dragonianmix | Dragonian Mix |
| melondove.wolfeinrace | Wolfein Race |
| nsns.tinimar | Tinimar the Harfling |
| ny.smeltedloong | 中式龙娘——冶龍 |
| kalospacer.ahndemi.panieltheautomata | Paniel the Automata |
| leopoko.wolfeinallegiance | Wolfein Allegiance |
| aifeng.sylvierace | Sylvie Race |
| psyche.kemomimihouse | kemomimihouse |
| moo.hardworking.kz | kemomimihouse HardworkingKz |
| hentailoliteam.axolotl.factionexpand | MoeLotl Faction Expand |
| valeries.miliraexpansion | 瓦莱丽的米莉拉扩展包 |
| pakerwot.miliraeventandstortexpandthetaleofmilira | Milira Event Story Expand |
| ancot.milirarace | Milira Race（类型级覆盖，需确认） |
| ancot.kiirorace | Kiiro Race（类型级覆盖，需确认） |
| pupa.insectgirls | 虫娘 Insect Girls（类型级覆盖，需确认） |

#### RJW 附属与玩法扩展

| 包 ID | 名称 |
| --- | --- |
| otyoty.sizedapparel | Sized Apparel for RJW |
| eltoro.rjw.menstruation.fluids | RJW Menstruation - Fluids |
| nugerumon.romancetweaksmoreoptions | Romance Tweaks More Options |
| rjw.fb | RimJobWorld - FB |
| ryuf.rain.rjwaddons | Rains RJW Addons |
| lw.rjw.cumquer | [LW1.6] rjw-cumquer |
| euclidean.s16.core | S16's Extension |
| bep.brothel.signs | [B.E.P] Brothel Signs |
| bep.rjw.brothelcolony.quest | RJW Brothel Colony Quest Patch |
| omgwtfbbq.gendersupremacyxtreme | Gender Supremacy Xtreme |
| c0ffee.rjw.ideologyaddons | C0ffeeRIA |
| archersaiter.rjw.unleashed.crests | RJW_Unleashed_Crests |
| archersaiter.rjw.traits | RJW_Unleashed_Traits |
| shauaputa.rimnudeworldzoo | RJW - Animal Graphics Addon |
| lustlicentiaserums.rjwlabs | RimJobWorld - Licentia Serums |
| alpenglow.canines.animations | Canines Animations |
| eltoro.maddon | ElToros Mechanoid Addon |
| nalzurin.metalhorrorspreadsthroughsex | Metal Horror Spreads Through Sex |
| rjw.fc | RimJobWorld - FC |
| eltoro.stretching | ElToros RJW Stretching |
| sluggandnil.mmcr | Masochists Can't Rebel |
| py.rjwnecrophiliac | py_RJWNecrophiliacPatch |
| py.rjwromancefix | py_RJWRomanceBoostPatch |
| rjw.sexperience.ideology | RJW Sexperience Ideology |
| vegapnk.multipleorgasm | MultipleOrgasm |
| bep.rjwaddon.nurseryfly | NurseryFly |
| yunia.sg00 | Yunia |
| c0ffee.rimworld.animations | Rimworld Animations 2.0（类型级覆盖，需确认） |

#### 事件 / 任务 / 世界

| 包 ID | 名称 |
| --- | --- |
| hailuan.iwbb | I will be back |
| albion.goexplore | Go Explore! |
| aoba.deadmanswitch.core | The Dead Man's Switch |
| aoba.deadmanswitch.ancientcorps | DMS - AncientCorps |
| aoba.deadmanswitch.synthetic | DMS - Synthetic |
| g13.deadmanswitch.synrtex | DMS - Synrtex |
| ziri.deadmanswitch.powerarmorexpanded | DMS - Power Armor Expanded |
| duz.almosttherefork | Almost There! Fork |
| breadmo.cinders | Cinders of the Embergarden |
| xmb.ancientthreat.mo | Ancient amorphous threat（类型级覆盖，需确认） |
| acutus.clusterprojection | Cluster Projection |
| trigger.eliteraid | Elite Raid（类型级覆盖，需确认） |
| krafs.levelup | Level Up! |
| marvinkosh.sometimesraidsgowrong | Sometimes Raids Go Wrong |
| ghxx.techadvancing | Tech Advancing |
| nagisa.orion.hospitality | Hospitality（补丁键需修复） |

#### 存储 / 工业 / 装备

| 包 ID | 名称 |
| --- | --- |
| ogre.ogrestack | OgreStack |
| nuanki.adaptivestorageglobalsettings | Adaptive Storage - Global Settings |
| sbz.neatstorageworkbenchshelf | [sbz] Workbench Shelf |
| sbz.neatstoragefridge | [sbz] Fridge |
| mo.mhscanner | Metalhorror Scanner |
| meltup.advancedpowerplus | Advanced Power Plus |
| lingluo.giantcrop | Giant crop |
| scar.basicfarming | Scar's Basic Farming |
| dismarzero.vgp.vgpgardenmedicine | VGP Garden Medicine |
| sunsetmoderteam.rottenfood | Rottenfood |
| mlie.showmeyourhands | Show Me Your Hands |
| mlie.realistichumansounds | Realistic Human Sounds |

#### 战斗 / 义体 / 伤害

| 包 ID | 名称 |
| --- | --- |
| donald.vcr | Vanilla Combat Reloaded |
| cat.cqc | Close Quarters Control |
| np.tacticalcrawling | Tactical Crawling |
| talesoftherim.draftanything | Draft Anything 2.0 |
| aliza.vanillameleemodes | Vanilla Melee Modes |
| mlie.xndtargetingmodes | [XND] Targeting Modes |
| vis.staticquality | Static Quality |
| mlie.allturretscansetforcedtarget | All Turrets Can Set Forced Target |
| rabiosus.smartpistol | Smart Pistol |
| rabiosus.halorailgun | ARC-920 Railgun |
| rabiosus.autoblink | AutoBlink |
| moistestwhale.gitscyberneticequipment | GiTS Cybernetic Equipment |
| qw.archotechimplantsexpanded | [QW] 超凡仿生体扩展 |
| jamlik.sandevistan | Sandevistan Implant |
| vat.epoeforked | EPOE Forked |
| lts.i | Integrated Implants |
| sambucher.adogsaidanimalprosthetics2 | A Dog Said... Animal Prosthetics 2 |
| fxz.appareldura | Infinite apparel durability |
| ab.beheading | Cutting out Head |
| thumb.goremod2 | Visual Brutality |
| roltonsmods.fultonextraction | Tactical Fulton Extraction System |
| spray.transfer | Spray Transfer |

### P1：带 DLL，需要定向审计

其余 212 个未显式覆盖的 DLL 模组按“先证伪”处理：阅读源码或反编译，列出现有动作入口，与 MP 核心/官方兼容包对照；只有发现未被捕获且会改模拟状态的路径才写补丁。

典型方向：

- UI/视觉类：`jaxe.rimhud`、`nals.customportraits`、`nals.dynamicportraits`、`creeper.betterinfocard`、`zeracronius.dynamictradeinterface`、`kahdeg.killfeed`、`m00nl1ght.mappreview` 等。默认预期无补丁，但需确认按钮/浮菜单回调不直接改模拟状态。
- 工作/建筑辅助类：`falconne.bwm`、`unlimitedhugs.defensivepositions`、`defi.blueprints.fork`、`memegoddess.replacestuff`、`automatic.autolinks`、`wvc.sergkart.biotech.moremechanoidsworkmodes` 等。
- 存档/配置类：`arandomkiwi.rimsaves`、`owlchemist.midsaversaver`、`madeline.modmismatchformatter` 等，需确认不绕过 MP 存档协议。
- 其他带 DLL 的包以启动解析日志和动作清单为准。

### P2：无 DLL / XML / Def / 汉化 / 贴图

共 206 个无 DLL 包，默认不需要专项同步代码；只要主机与客户端包 ID、版本、文件和加载顺序一致，并纳入生成、装备、交互、存档/重连冒烟即可。

### P3：不建议与 MP 同开或需要单独禁用

以下性能/TPS/时间类模组与 MP 确定性 Tick 或本仓库 TPS 调度冲突风险高，默认不加入联机载荷，或单独验证后决定：

| 包 ID | 名称 | 原因 |
| --- | --- | --- |
| chokey.tpsoptimizer | TPS Optimizer | 全局 Tick 重写 |
| yuki.fpsandtps | FPS And TPS | TPS 控制 |
| dubwise.dubsperformanceanalyzer.steam | Dubs Performance Analyzer | 分析器，非确定性载荷 |
| arkymn.performanceesmolas | Performance Esmolas | 性能类，已有本地引用仍需回归 |
| arkymn.slowerpawntickrate | Slower Pawn Tick Rate | Tick 频率重写，已做补丁但风险高 |
| jaeger972.turretperformancetweaks | Turret Performance Tweaks | 炮塔 Tick 优化 |
| secsky2.gearstatoptimization | Gear Stat Optimization | 静态缓存风险 |
| owlchemist.midsaversaver | Mid-saver Saver | 存档流程改写 |
| defi.blueprints.fork | Blueprints Forked | 蓝图创建/删除/重命名/XML 导入导出会打开本地窗口并读写本地文件；联机中建议仅主机或建图前使用。旋转/翻转已由 `Patch_BlueprintsMp.cs` 同步 |

## 6. 实施计划

### 阶段 0：锁基线（0.5–1 天）

1. 修正包 ID：统一 `local.mp.racecompatibilityplus` 与 `About.xml`，处理 `Nagisa.Orion.Hospitality` 补丁键。
2. 从实际 `ModsConfig.xml` 导出测试子集，不使用假定全量。
3. 记录每个包 ID、版本、加载顺序、DLL SHA-256，以及 RimWorld / MP / Harmony 版本。
4. 建立隔离的 host/client `-savedatafolder`，两端只使用复制出的相同文件。
5. 先用最小 P0 组合完成“可进服、两人 `ClientPlaying`、无 Def mismatch”的基准存档。

### 阶段 1：P0 核心簇（约 4–8 天）

按以下簇逐一审计并测试，前一簇无确定性问题后再加入下一簇：

1. RJW 核心 + Sexperience + Animations + 已覆盖附属（先回归现有补丁）。
2. 剩余 RJW 附属簇：Sized Apparel、Menstruation Fluids、Romance Tweaks、Unleashed、Brothel Quest、S16 等。
3. 种族簇：Ratkin 系列、Milira/Kiiro/Insect Girls（确认类型级覆盖）、其余种族。
4. 事件/任务簇：DMS、Go Explore、Cluster Projection、Level Up、Elite Raid 等。
5. 存储/工业簇：OgreStack、Adaptive Storage Global Settings、Neat Storage、MH Scanner 等。
6. 战斗/义体簇：VCR、CQC、Tactical Crawling、Targeting Modes、义体包等。
7. Hospitality 补丁键修复与回归。

每个簇的工作产物：源码权威/程序集哈希记录、动作清单、MP 已有覆盖结论、所需补丁、补丁目标解析日志、测试记录。

### 阶段 2：P1 定向审计（约 3–6 天）

对每个已启用 P1 包，优先审计非主机玩家发起的 UI 与事件路径。若所有状态变更均由已有同步工作/命令触发，记录为“兼容，无新增代码”；否则按最窄执行点补丁。

### 阶段 3：内容回归与缩减（约 1–3 天）

逐批加入 P2 无 DLL 包与 P3 风险判定结果，每批完成：

- 生成新 Pawn/种族，装备相关物品，触发可用设施或 Def；
- 存档/重载、断线重连；
- 若一批出错，二分拆分到具体包并提升至 P1 审计。

### 阶段 4：长期验证与发布（约 2–5 天）

- 全量目标载荷运行短冒烟（约 10,000 个共享 tick）。
- P0 核心簇运行至少 120,000 个共享 tick 的 normal-settings soak。
- 归档最终 DLL，记录 SHA-256；部署前复核哈希。
- 扫描最终日志中的 `Sync Error`、`Inconsistent player`、`desynced=True`、包错误和意外断线。

## 7. 每个补丁的验收门槛

1. 编译通过，目标解析在两端全部成功，且没有重复同步或反射命中错误重载。
2. 两端使用相同候选 DLL 哈希；主机出现 `Server started.`，非主机达到 `ClientPlaying` 且玩家数为 2。
3. 由非主机走真实 UI 链执行目标动作至少 3 次；包含首次/关闭重开/取消后重开；涉及地图或世界上下文时覆盖两者。
4. 两端断言相关 Pawn/Thing ID、Hediff/关系/库存/银币/物品生成/信件/任务状态一致。
5. 目标动作后运行约 10,000 个共享 tick 冒烟；P0 核心簇再运行至少 120,000 个共享 tick soak。任何 `Sync Error`、包错误、断线、状态断言失败或 desync 都是失败。
6. 最终测试过的 DLL 归档并复核 SHA-256；代码或 DLL 改动后重新测试。

## 8. 风险与回退

- 最大风险是同时加入全部 RJW/种族/事件扩展：核心状态机、周期 Tick、任务/交易、关系修改叠加后难以定位。坚持“最小可联机基线 → 单簇 → 单包”。
- 本仓库按类型名装配的补丁，在目标模组更新后可能静默跳过；阶段 0 必须建立目标解析日志基线。
- 性能/TPS 类模组默认保持禁用；已做的 `SlowerPawnTickRate`、`PerformanceEsmolas` 等补丁只证明有适配代码，不替代 soak。
- 未通过 P0 的簇在正式联机包中保持禁用。

## 9. 数据来源与局限

- `list1.xml`：465 个 activeMods。
- 本地 `Mods` 目录：读取 1031 个含 `About.xml` 的目录，映射到 458 个列表包 ID；DLL 判定为递归扫描目录内任意 `.dll`。
- 官方覆盖：`G:\Steam\steamapps\common\RimWorld\Mods\Multiplayer-Compatibility\Assemblies\Decompiled` 中 `MpCompatFor` 属性，共 202 个官方 ID，列表内命中 13。
- 本仓库覆盖：`Source/MP_MeowOnlineShop` 中包 ID 字符串 + `About.xml` loadAfter，共命中 36；类型级覆盖未纳入该计数。
- 局限：本计划是静态盘点与设计，不把“无 DLL”或“无显式补丁”等同于“已证明兼容”；所有结论以启动解析日志和主机/客户端实测为准。

## 10. P0/P1 实施进度

> 状态：代码编写阶段；尚未正式部署，也未进行游戏内主机/客户端测试。
> 更新：2026-08-07（目标扩展为完成 P0 后继续 P1）
> 运行状态：候选 DLL 已编译（0 警告 0 错误），但 10.16 的 465 模组冒烟未通过，本批补丁暂不满足部署门槛。

### 10.1 本批已完成并编译通过的补丁

| 补丁文件 | 目标模组 | 同步边界 |
| --- | --- | --- |
| `Patch_TargetingModesMp.cs` | `mlie.xndtargetingmodes` | UI 浮菜单设置 `SetTargetingMode` 走同步；`CompTick`/Pawn 生成重置保持本地确定性 |
| `Patch_TacticalCrawlingMp.cs` | `np.tacticalcrawling` | `CompGetGizmosExtra` 的爬行开关回调替换为同步命令；TickRare/生成/撤销保持本地 |
| `Patch_VanillaMeleeModesMp.cs` | `aliza.vanillameleemodes` | Auto 开关与模式循环两个 Gizmo 回调走同步；`CompTick` 自动模式保持本地 |
| `Patch_DraftAnythingMp.cs` | `talesoftherim.draftanything` | `Pawn.GetGizmos` 中自定义征召开关回调走同步，重放 `isControled`/组件分配/`Drafted` |
| `Patch_AutoBlinkMp.cs` | `rabiosus.autoblink` | 玩家可见字段注册 SyncField；`BlinkToCellDirect` 仅由 Gizmo 目标回调调用，改为同步重放，`CompTick` 调度路径不受影响 |
| `Patch_SmartPistolMp.cs` | `rabiosus.smartpistol` | `Projectile_SmartBullet.InitRandOffset` 懒初始化贝塞尔偏移；改为按 Thing ID + Map ID 推入确定性 Rand 作用域 |
| `Patch_ComeBackColonyMp.cs` | `hailuan.iwbb` | 每次 `TryExecuteWorker` 前清空未存档的静态 `candidates` 缓存，保证两端从相同世界状态重建候选 |
| `Patch_OgreStackMp.cs` | `ogre.ogrestack` | 联机中禁止设置变更触发的运行时 `ModifyStackSizes`/`ResourceCounter.ResetDefs` 本机重写；启动加载仍一致应用 |
| `Patch_GoExploreMp.cs` | `albion.goexplore` | `Building_AncientStorageUnitLGE.EjectContents` 覆写了 MP 已注册的基类 `Building_Casket.EjectContents`；补注册派生覆写，使古代储存箱清空操作同步 |
| `Patch_HospitalityInteractions.cs`（修改） | `nagisa.orion.hospitality` | 兼容 `Nagisa.Orion.Hospitality` 与 `Orion.Hospitality` 两个包 ID，恢复随机互动排序补丁 |
| `Patch_AlmostThereMp.cs` | `duz.almosttherefork` | 注册 `CompNightRestControl.almostThere` 为 SyncField；Gizmo 界面写入广播，`Caravan_PostAdd` 默认模式写入保持本地确定性 |
| `Patch_DeadMansSwitchMp.cs` | `aoba.deadmanswitch.core` | 自定义皇家许可 `RoyalTitlePermitWorker_RewardShuttle.OrderForceTarget` 前缀转同步，重放 `CallShuttle`；文档处理/召怪仍在确定性 Job/事件路径 |
| `Patch_ClusterProjectionMp.cs` | `acutus.clusterprojection` | 主控制台装卸/组装/发射、快返标记、步兵舱与载具架对话框、投影信标发射统一走同步；目标器与窗口生命周期保持本地 |
| `Patch_FultonExtractionMp.cs` | `roltonsmods.fultonextraction` | 注册 `CompExtractSelf.StartSelfExtraction`；穿戴式 Fulton 目标仍走 `TryTakeOrderedJob`，由 MP 原有同步覆盖 |
| `Patch_SandevistanMp.cs` | `jamlik.sandevistan` | 注册控制器 Gizmo 的 `ToggleActive`；自动激活/倒下停用在确定性 Tick 内保持不变 |
| `Patch_QwArchotechImplantsMp.cs` | `qw.archotechimplantsexpanded` | 注册 `Hediff_SuperRegeneration.ToggleScarHealing`，同步疤痕修复开关 |
| `Patch_DmsPowerArmorMp.cs` | `ziri.deadmanswitch.powerarmorexpanded` | 注册 `CompApparelGiveHediff.Notify_Used`；Overseer 直接关系增删在界面上下文内转同步重放 |
| `Patch_RatkinKnightsMp.cs` | `rkk.ratknights.core` | 注册月光冲锋 `DashAct` 与 Atlar 命名的召唤/追回/启程/回调执行器；MP 仅界面 `ShouldSync` 时广播，TickRare 保持本地 |
| `Patch_AselMonolynMp.cs` | `asel.monolynrace` | `MonolynConsumer.work` 注册 SyncField；`PhoneBooth.CreateCorpseStockpile` 与 `TowerOfLightBroken.OrderForceTarget` 注册 SyncMethod |
| `Patch_AselMonolynMp.cs`（补充） | `asel.monolynrace` | `Building_MonolynProducer.selectedOption` 与 `CompLightConduit.connectedConduit` 注册 SyncField；`StartWork`/`Cancel` 注册 SyncMethod，覆盖生产 ITab 与电网连接 |
| `Patch_OberoniaSnowstormMp.cs` | `oark.ratkinfaction.scenarioexpand.snowstorm` | `Building_IceCrystalCollector.unloadingEnabled` 注册 SyncField；`EjectContents` 注册 SyncMethod |
| `Patch_SmeltedLoongMp.cs` | `ny.smeltedloong` | `AC_GiveHediffDual.changeHediff` 与 `AC_GeneEditing.changeMode` 注册 SyncField，能力后续 Apply 在同步能力路径内 |
| `Patch_DragonianMixMp.cs` | `kalospacer.dragonianmix` | 自定义皇家许可 `OrderForceTarget` 与 `CallResourcesToCaravan` 转同步，重放落货/消耗许可与好感 |
| `Patch_SylvieRaceMp.cs` | `aifeng.sylvierace` | 注册 `SylvieRace_CompNurseHeal.TryUseAbility`，同步治疗、麻痹与冷却写入 |
| `Patch_AdaptiveStorageGlobalSettingsMp.cs` | `nuanki.adaptivestorageglobalsettings` | 全局/单建筑设置、倍数应用、重置与导入后整包快照 ASGSSettings 并重放 `ApplyAllSavedSettings`，使 Def 重写与建筑师菜单两端一致 |
| `Patch_CustomChoiceLettersMp.cs` | `oark.oberoniaaurea.framework`、`aifeng.sylvierace` | 注册 Oberonia 加入邀请与 Sylvie 出售信件的 Accept/Reject 两个 Choices lambda，接受/拒绝/交易在两端重放 |
| `Patch_HardworkingKzMp.cs` | `moo.hardworking.kz` | `GetGizmosExtra` 中的取消当前工作与近战攻击回调改为同步重放；近战攻击复用原 `GetMeleeAttackAction` 保证 Job 与 `curMeleeAttackInt` 两端一致 |
| `Patch_MaruItemFormChangeMp.cs` | `vamv.maruracemod` | 装备/衣物形态转换 Gizmo 改为携带 TransformData 索引的同步命令，重放销毁旧物/创建并穿戴新物/冷却写入 |
| `Patch_MaruTrapMp.cs` | `vamv.maruracemod` | `MaruTrap.Building_Trap.autoRearm` 注册 SyncField，派生自动重装开关同步 |
| `Patch_WolfeinAllegianceMp.cs` | `leopoko.wolfeinallegiance` | 两个自定义皇家许可 `OrderForceTarget` 转同步并重放落货/夜间空袭；两个补给任务的 `Fulfill` 注册 SyncMethod |
| `Patch_LingCuterMp.cs` | `LingLuo.ItemCuter`、`LingLuo.PawnCuter` | ItemCuter 切割开关/效率/工作时间注册 SyncField；PawnCuter 的 `pairsAs`/`modeCBodyparts`/`AdvHediffs`/`cutAdvHediffs`/`workMode` 注册 SyncField |
| `Patch_MoreTortureMp.cs` | `cof.moretorture` | 注册 StartProgress/StopProgress、upStage/downStage、ReleaseVictim/SetVictim/startExecuteProgress/stopExecuteProgress |
| `Patch_EternalPawnsMp.cs` | `helldan.eternalpawns` | 记忆窗口/清空数据库采用“绘制前快照 + 绘制后回滚 + SyncMethod 重放” |
| `Patch_QuarryMp.cs` | `Ogliss.TheWhiteCrayon.Quarry` | 注册 MineModeResources/Blocks/Chunks 与 ToggleAutoHaul |
| `Patch_DownForMeMp.cs` | `aRandomKiwi.DownForMe` | 注册强制倒地同步方法，并改写 ForceDown/ForceUndown 两个 Gizmo 的 action |
| `Patch_MoreMechanoidsWorkModesMp.cs` | `wvc.sergkart.biotech.MoreMechanoidsWorkModes` | 注册 `ModeSwitch` 同步方法，并改写 Comp/Zone Gizmo 的 restrictZoneByGroup/escortTarget/allow* 写入 |
| `Patch_BlueprintsMp.cs` | `Defi.Blueprints.fork` | 为 `Blueprints.Blueprint` 注册按名称解析的 SyncWorker，并同步 `Rotate(RotationDirection)`/`Flip()` |
| `Patch_AchtungMp.cs` | `brrainz.achtung` | 同步 `Tools.SetDraftStatus` 与 `ForcedWork.Remove/RemoveForcedJob` 稳定执行子集；ForceAction/拖拽状态仍为残留风险 |
| `Patch_ShowWeaponTalliesMvcfMp.cs` | `kathanon.ShowWeaponTallies`（MVCF） | 为 `ManagedVerb` 注册按 LoadID 解析的 SyncWorker，并同步 `Toggle()`；停止强制攻击 Gizmo 改为 `SyncStopForceAttack`；`Verb.OrderForceTarget` 前缀仅对 MVCF 管理动词清空 `CurrentVerb` |
| `Patch_RimmuNationSecurityMp.cs` | `CP.RimmuNation.2.Security` | 同步 `CompProjectileSprayer.Fire()`；`CompExplosive.StartWick` 已由 MP 核心覆盖 |
| `Patch_VanillaMushroomsMp.cs` | `VanillaExpanded.VPlantsEMushrooms` | 注册 `allowSow`/`allowCut` 同步方法，并改写 `Zone_GrowingMushroom` 的两个 Command_Toggle |
| `Patch_RimWorldColumnsMp.cs` | `nephlite.orbitaltradecolumn` | `Gizmo_ClaymoreSafetySettings.DrawOptionFor` 三组复选框“快照 + 回滚 + SyncMethod 重放”，同步 `ClaymoreCharge.settings[]` |
| `Patch_EqualMilkingMp.cs` | `akaster.equalmilking.fix` | 五个 `PawnColumnWorker` 的 `SetValue` 前缀改写为同步设置方法；`Window_AssignFeeder.DrawRow` 采用快照/回滚并同步 assignedFeeders 添加/移除；`Mod.WriteSettings` 后对 `EqualMilkingSettings` 整包快照同步 |
| `Patch_RatkinRaceMp.cs` | `solaris.ratkinracemod` | SyncField 同步 BFR 弹种与脉冲步枪模式；游商定居对话框 `AcceptPawn`/`AcceptAllSettlers` 前缀改为同步重放 |
| `Patch_RatkinUndergroundMp.cs` | `rku.ratkinunderground` | SyncField 同步 `isSearchJob` 与钻地车炮塔 `holdFire`；`TriggerDialogueEvents`/`ExecuteDialogueEvent` 前缀转为同步命令，远端重建仅用于重放的 `Dialog_RKU_Radio`，并刷新本地已打开电台窗口 |
| `Patch_RaceTogglesMp.cs` | `melondove.wolfeinrace`、`oark.ratkinfaction.oberoniaaurea`、`ny.smeltedloong` | 将炮塔开火、月光装置、重力信标、电路修复、龙头炮塔开火等已存档布尔开关注册为 SyncField |
| `Patch_WolfeinToolsMp.cs` | `melondove.wolfeinrace` | 注册 `RimWorld.CompShield.Reset()` 与 `RimWorld.CompApparelVerbOwner.UsedOnce()`；工具切换 `currentGroupIndex` 注册 SyncField，`ApplyCurrentToolGroupVerbs` 注册 SyncMethod |
| `Patch_RatkinBackpackRadioMp.cs` | `rku.ratkinunderground` | `LaunchBunkerBuster`/`LaunchDrillerGun` 仅在 UI 同步上下文转同步命令，并同步冷却 tick；Tick 自动发射保持本地确定性 |
| `Patch_OberoniaScienceShipMp.cs` | `oark.ratkinfaction.oberoniaaurea` | 注册 `CompCrashedScienceShip.MakeGravityAdjustmentJob`，同步 `gravityAdjuestType` 写入与重力调整 Job |
| `Patch_AselTogglesMp.cs` | `asel.monolynrace` | 星标信标/光之塔/自动光束/锻造阵列/MNC/自动热斩/抽取器/重力柱/引力炮模式等 SyncField；引力炮 `MakeGunR/MakeGunA`、退款 `Refund`/`RefundAndTransfer`、光分配器 `RegisterReceiver`/`UnregisterReceiver`、频段节点/格式化监督者 `TuneTo`、熔炉散热/陨石引导/光建造器取消注册 SyncMethod |
| `Patch_OberoniaFrameMp.cs` | `oark.oberoniaaurea.framework` | 注册 `CategoryTradeRequestComp.Fulfill`、`SaleRequestComp.Fulfill`、`FixedCaravan.PreConvertToCaravanByPlayer` 与 `ConvertToCaravan` |
| `Patch_AselTargetingMp.cs` | `asel.monolynrace` | 构造/传送/变形目标器回调在 UI 同步上下文转同步命令，并写入 `lastUsedTick` |
| `Patch_SmeltedLoongMp.cs` | `ny.smeltedloong` | 在原有能力模式 SyncField 上，新增 BlackBird 三个皇家许可 `OrderForceTarget` 的 SyncMethod |

编译结果：`dotnet build -c Release` 通过，0 错误、0 警告（已含上述全部补丁）。因本机 RimWorld 正在运行锁定 `1.6/Assemblies/MP_MeowOnlineShop.dll`，最终验证使用独立输出目录 `obj/ReleaseVerify/`，代码编译与链接均成功。最新验证 DLL SHA-256：`027880CFC4CE733FB213D8445519313CA2521D93BF026BA1D066D6C0D7471AE2`。

### 10.2 已审计、结论为暂不需要新增补丁的 P0 候选

| 模组 | 结论依据 |
| --- | --- |
| `cat.cqc` | 浮菜单动作最终都走 `pawn.jobs.TryTakeOrderedJob`（MP 已同步有序工作）；门锁/阵型是 UI 与确定性工作流，未发现额外本地状态写入 |
| `donald.vcr` | 战斗公式与 `Rand.Chance` 位于 MP 已同步的 `Verb_MeleeAttack.TryCastShot` 重放路径；主要剩余风险是两端设置不一致，需启动/加入时校验 |
| `krafs.levelup` | 技能学习/降级在同步的 `SkillRecord.Learn` 路径上执行；mod 只追加视觉/消息动作，`Time.time` 限流缓存为本地展示 |
| `murmur.tcu.kotobike` | `TickRare` 与 `SpawnSetup` 均为确定性地图/建筑逻辑，无独立 UI 状态入口 |
| `mlie.allturretscansetforcedtarget` | 仅放宽 `CanSetForcedTarget` 判定；强制目标命令本身是原版已同步命令 |
| `rabiosus.halorailgun` | 仅追加弹道拖尾、音效与冲击特效，全部为视觉/音频；无独立模拟状态写入 |
| `mlie.showmeyourhands` | 纯渲染与 ModSettings 配置，不写游戏模拟状态 |
| `krafs.levelup`（补充） | `SkillRecord.Learn` 后置仅为视觉/消息；`Time.time` 限流缓存是本地展示 |
| `marvinkosh.sometimesraidsgowrong` | `IncidentWorker_RaidEnemy` 覆写在 MP 已同步的事件重放路径内；Rand 消耗与生成顺序确定性一致 |
| `ghxx.techadvancing` | 模组自带 `MP.RegisterAll()` 与同步注册；`DateTime.Now` 仅用于本地提示时机 |
| `mo.mhscanner` | `Building_MHScanner` 检测为确定性 Tick 扫描并保存 `triggered`；无 UI 状态写入 |
| `lingluo.giantcrop` | 巨作物合并在确定性 `CompTickLong`/Rand 路径；`GameComp_GiantCrop` 的延迟生成状态未存档但两端 Tick 同步执行 |
| `spray.transfer` | 在同步的 `Verb.TryCastNextBurstShot` 内重算目标；两端在相同射击上下文执行同一 `AttackTargetFinder` 查询 |
| `sunsetmoderteam.rottenfood` | 腐烂进度/伤害在确定性 Tick 与摄入路径；启动期 Def 生成两端相同 |
| `meltup.advancedpowerplus` | 仅补丁 `CompPowerPlantWater` 的确定性缓存重建，无 UI 状态写入 |
| `araeotu.rimendel` | 出生后置使用 `Rand.Value` 与静态父本引用，出生路径确定性同步执行；暂无需新增补丁 |
| `rabiosus.jumplifter` | 安装目录无 DLL，纯 Def/汉化内容 |
| `mlie.markthatpawn`、`mlie.realistichumansounds`、`verniy709.realistichumansounds.harpatch` | UI 标记/音频类，无模拟状态写入 |
| `roltonsmods.fultonextraction`（穿戴式 Fulton） | `FultonHarness` 目标回调最终 `TryTakeOrderedJob`，MP 已同步有序工作 |
| `lts.i` | 植入物装卸/装填浮菜单最终走 `TryTakeOrderedJob`；远程能力使用 `Command_Ability`，由 MP 能力同步覆盖 |
| `aoba.deadmanswitch.ancientcorps` | 仅 Tick/事件/Rand 生成路径，无本地 UI 状态写入；`CompUseEffect_LevelUpDefcon`/`DisableMechs` 在同步物品使用 Job 内执行 |
| `ziri.deadmanswitch.powerarmorexpanded`（除已补两条） | `CompShield_Number` 仅状态/绘制 Gizmo；开发 Gizmo 不属于正常玩家路径 |
| `thumb.goremod2` | 尸体/碎片为渲染与 Tick 逻辑；头部 `HeadInfo.Facing` 旋转仅为视觉，不进入模拟判定 |
| `vat.epoeforked`、`sambucher.adogsaidanimalprosthetics2`、`moistestwhale.gitscyberneticequipment` | 反编译未发现未被 MP 覆盖的 UI 模拟写入；主要为 Hediff/医疗/部件逻辑 |
| `fxz.appareldura` | 仅按 HashInterval 修改装备耐久，确定性 Tick 路径 |
| `sbz.neatstorageworkbenchshelf`、`sbz.neatstoragefridge` | 路径成本/可达性补丁，无本地 UI 状态写入 |
| `scar.basicfarming` | 唯一 Gizmo 是 Dev 调试用；正式玩家路径为确定性 Comp 逻辑 |
| `rooandgloomy.dragonianracemod`、`kalospacer.dragonianmix`（其余部分）、`melondove.wolfeinrace`、`nsns.tinimar`、`psyche.kemomimihouse` | 反编译未发现未被覆盖的 UI 模拟写入；主要为事件、Quest 节点、能力/技能与 Tick 逻辑 |
| `valeries.miliraexpansion`、`pakerwot.miliraeventandstortexpandthetaleofmilira` | 前者为射击自伤 Verb 确定性逻辑；后者为 CaravanArrivalAction/Quest 节点，走 MP 已有旅行/任务同步路径 |
| `ASEL.ITab_BillsBP` | 新增/粘贴配方最终调用 `BillStack.AddBill`，MP 已注册该同步方法；塔楼对话框使用 `Dialog_NodeTree`，由 MP 通用节点同步覆盖 |
| `dismarzero.vgp.vgpgardenmedicine`、`aoba.deadmanswitch.synthetic`、`g13.deadmanswitch.synrtex` | 安装目录无 1.6 生效 DLL，只有 Def/汉化/贴图内容 |

### 10.3 P0 剩余待实现

- 已新增补丁但仍有局部边界待运行时确认：`acutus.clusterprojection` 的两个自定义装卸对话框、`rkk.ratknights.core` 的 Atlar 内嵌 lambda 动作、`jamlik.sandevistan` 的自动激活与全局慢放是否在长 soak 中稳定。
- P0 静态审计与补丁编写已基本完成；剩余工作为启动解析日志、主机/客户端实测与长 soak，全部完成后再正式部署。
- P1 批量“先证伪”审计已推进至 10.13，P1Inventory160.txt 中需要定向审计的候选均已给出结论；其余包由既有 P0/RJW/官方覆盖章节承接，尚未定稿的仅剩启动解析日志与运行时确认项。
- `dismarzero.vgp.vgpgardenmedicine` 仅见 1.0 版 `ModFinder.dll`，需确认 1.6 实际加载后再定结论。
- 其余 P0 存储/战斗/义体项按第 5 节优先级继续“先证伪”审计；P1 已进入收尾。

### 10.4 P1 批量审计范围

- 对第 5 节 P1 中其余带 DLL 模组执行“先证伪”：逐个读取源码/反编译，列出 `Command_Action`、`FloatMenuOption`、`Dialog/DiaOption`、`Widgets.Button*`、Gizmo、事件触发和 Tick。
- 只有“本地 UI 动作最终改变模拟状态且未被 MP 核心或官方兼容包捕获”的路径才新增补丁；其余记录为“兼容，无新增代码”。
- 已审计为无需新增补丁的 P1 类：`jaxe.rimhud`、`nals.customportraits`、`nals.dynamicportraits`、`creeper.betterinfocard`、`zeracronius.dynamictradeinterface`、`kahdeg.killfeed`、`m00nl1ght.mappreview`、`falconne.bwm`、`defi.blueprints.fork`、`memegoddess.replacestuff`、`automatic.autolinks` 等，各批次结论见 10.5–10.13。

### 10.5 P1 首批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `telardo.DragSelect` | 无需补丁 | 仅改写拖拽选择/按钮输入，不写模拟状态 |
| `dvs.NoRandomRelations`、`Doug.NoJobAuthors`、`kathanon.NoDisabledFactions`、`kuchiki.NoBeardsOnTeenagers`、`secsky2.GearStatOptimization`、`SmoothedStoneIsWorthless.Mod` | 无需补丁 | 事件/生成/渲染/统计缓存逻辑，无本地 UI 模拟写入 |
| `Layyoune.CitadelCable`、`LingLuo.MoveSteamGeyser`、`momo.stayinbed`、`rw.mod.PickUpAtHome`、`Og.Immersive.Filter`、`Leo.CleanCommandBar`、`ferny.ResourceDeliveryHelper` | 无需补丁 | Tick/UI 布局/过滤器/命令分组，未发现模拟状态直写 |
| `kearril.ChooseWhereToLand` | 无需补丁 | 新增 TransportersArrivalAction 浮菜单，仍经 MP 已同步的 launchAction 路径 |
| `ferny.NameYourEntities` | 无需补丁 | 重命名仅写 Thing/Pawn 名称等展示字段 |
| `helldan.eternalpawns` | 已补丁 | 记忆窗口按钮直接改 `manualVeteranPins`/`pawnNotes`，设置页“清空数据库”清空四个管理器集合；新增 `Patch_EternalPawnsMp.cs`，用“绘制前快照 + 绘制后回滚 + SyncMethod 重放”同步 |
| `visibleraidpoints.1trickPwnyta` | 需后续确认 | 向 StandardLetter 追加 DiaOption；MP 对自定义信件选项的覆盖需启动日志与实测确认 |
| `LingLuo.ItemCuter`、`LingLuo.PawnCuter` | 已补丁 | ItemCuter 布尔/数值字段与 PawnCuter 工作模式/部件/Hediff 选择列表均注册 SyncField |
| `cof.moretorture` | 已补丁 | `Patch_MoreTortureMp.cs` 注册 StartProgress/StopProgress、upStage/downStage、ReleaseVictim/SetVictim/startExecuteProgress/stopExecuteProgress，覆盖 Gizmo/床交互写入 |
| `yancy.factiongearcustomizer`、`zylle.MapDesigner` | 建议 P3 禁用 | 运行期编辑 FactionDef/世界生成配置，应仅主机建图/配置阶段使用，或联机前禁用 |
| `Owlchemist.ScatteredFlames`、`Owlchemist.SimpleFX.Smoke2`、`Vortex.Kingfisher`、`ray1203.SimpleCameraSetting`、`dubwise.dubsmintminimap` 等 | 无需补丁 | 点火 Gizmo 最终走 `TryTakeOrderedJob`；其余为渲染/性能/UI 配置 |
| `Vortex.CustomizeWeapon` | 建议 P3 禁用 | 武器模块/预设编辑器会在运行期改写装备字段，联机中应禁用或仅在存档生成前使用 |

### 10.6 P1 第二批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `visibleraidpoints.1trickPwnyta` | 无新增补丁 | 1.5 自带源码确认：`ThreatPointsBreakdown` 静态关联缓存只在确定性事件/ExposeData 路径重建；追加到 ChoiceLetter 的 DiaOption 仅打开本地 `Dialog_MessageBox`，不写模拟状态。残留风险：运行期 ModSettings 改动与自定义信件选项需启动日志/实测确认 |
| `brrainz.achtung`（Achtung 1.6） | 已补丁（稳定执行子集） | `Patch_AchtungMp.cs` 同步 `Tools.SetDraftStatus`（直写 `draftedInt`）与 `ForcedWork.Remove/RemoveForcedJob`；MP 已同步 Job/优先工作路径。残留：`ForceAction` 的候选搜索、`AddForcedJob` out 参数、拖拽期 `targets`/`cellRadius` 仍为本地写入，完整放行前建议实测强制工作与拖拽编队 |
| `Ogliss.TheWhiteCrayon.Quarry` | 已补丁 | `Patch_QuarryMp.cs` 注册 `MineModeResources/Blocks/Chunks` 与 `ToggleAutoHaul`，覆盖影响采矿 Job 的存档字段写入；`Designator_ReclaimSoil` 走 MP 已同步的设计器路径 |
| `SmartSpeed`（1504723424） | 建议 P3 禁用 | 反编译确认：自定义按钮/浮菜单改写 `TickManager` 倍率与 `SmartSpeed_Settings.currSetting`，与 MP 时间控制冲突；不建议联机加载 |
| `DropSpot`（1969732297） | 无需补丁 | 仅新增 `DropSpotIndicator` 建筑并改写 `DropCellFinder.TradeDropSpot` 选择逻辑；建筑放置由 MP 建筑命令同步，构造函数在两端确定性执行 |
| `SaveStorageSettings`（3222246658） | 高风险建议 P3 | 反编译确认：从本地文件加载会直接改写 `StorageSettings`/策略对象，`LoadCraftingDialog` 还调用 `BillStack.Clear()`；MP 只同步 `BillStack.AddBill` 与 `StorageSettings.Priority`，其余写入未覆盖；补丁完成前建议联机禁用 |
| `BetterInfestations`（3285516766） | 无需补丁 | Gizmo 均在 `Prefs.DevMode` 下出现；正式玩法路径为确定性 Incident/Tick/CompSpawner 逻辑，Rand 位于 MP 同步的 Tick 与事件重放路径内。残留风险仅运行期 ModSettings 改动 |
| `RimSaves`（1713367505） | 建议 P3 禁用或仅主机 | 存档管理 UI/文件夹/预览/自动存档命名均为文件 IO 与 UI，不直接写模拟状态；客户端执行存档/读档会绕过 MP 会话协议，联机中应仅主机使用或禁用 |

### 10.7 P1 第三批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `velc.TrapDisable`（1542019602） | 无需补丁 | 仅 Harmony 改写 `Building_Trap.CheckSpring` 判定，位于 MP 同步的陷阱触发路径；无 UI 模拟写入 |
| `aRandomKiwi.RaidsForMe`（1633517937） | 无需补丁 | 派系对话框新增 DiaOption 由 MP 的 `DiaOption.Activate` 按选项索引同步，且 `FactionDialogMaker` 在 MP 允许序列化声明类型列表内；Rand 与 `GC_RFM.lastRaidGT` 均在重放中执行 |
| `aRandomKiwi.DownForMe`（1709963396） | 已补丁 | `Patch_DownForMeMp.cs` 注册强制倒地同步方法，并把两个 Gizmo 的 `AddHediff/RemoveHediff` lambda action 改写为同步调用 |
| `Mlie.BestMix`（2195986094） | 自带 MP 注册 | 反编译确认 `MultiplayerSupport` 已注册 `SetBMixBillMode`/`SetBMixMode` 并修复 `BestMixUtility.RNDFloat` 随机作用域，无需新增 |
| `balistafreak.StandaloneHotSpring`（2205980094） | 无需补丁 | 温泉浮菜单最终走 `TryTakeOrderedJob`，由 MP 同步 |
| `kittahkhan.grazeup`（2302739121） | 无需补丁 | 仅 ModSettings 与确定性植物/动物 Tick 逻辑，无 UI 模拟写入 |
| `Garwel.DestroyItem`（2423311270） | 无需补丁 | 自定义 Designator 只添加 Designation（MP 已同步设计器），WorkGiver/JobDriver 在确定性 Job 路径销毁物品 |
| `RedMattis.BetterChildren`（2899562773） | 无需补丁 | 仅 ModSettings 与确定性成长/学习 Tick 逻辑，无 UI 模拟写入 |
| `Nera.SpawnThoseGenes`（2898044088） | 无需补丁 | 基因生成逻辑使用确定性 Rand，位于 MP 同步的生成路径内；无 UI 模拟写入 |
| `wvc.sergkart.biotech.MoreMechanoidsWorkModes`（2888380373） | 已补丁 | `Patch_MoreMechanoidsWorkModesMp.cs` 注册 `ModeSwitch` 同步方法，并把 CompMechSettings 的 restrictZoneByGroup/escortTarget 与 Zone_MechanoidShutdown 的 allow* 四个 Gizmo action 改写为同步写入 |
| `ac.mw.sad`（MeleeWeaponSpeedAndDamage，2031929206） | 无需补丁 | 仅 ModSettings 与近战 Verb 公式补丁，计算位于 MP 同步的战斗重放路径内 |
| `Mlie.FoodPoisoningStackFix`（2843483188） | 无需补丁 | 反编译未发现 UI/Gizmo/对话框模拟写入，仅确定性物品/疾病逻辑 |

### 10.8 P1 第四批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `Jaxe.RimHUD`（1508850027） | 自带 MP 注册 | 反编译确认 `RimHUD.Integration.Multiplayer` 通过 `RegisterSyncMethod` 注册自身动作；HUD 选择器调用 MP 已同步的时间表/服装/饮食/区域方法，无需新增 |
| `Nals.CustomPortraits`（1569605867） | 无需补丁 | 仅头像绘制与 ModSettings；设置只影响本地渲染 |
| `Nals.DynamicPortraits`（2253730555） | 无需补丁 | 仅头像/血条绘制与 ModSettings，无模拟写入 |
| `kahdeg.Killfeed`（1362098265） | 无需补丁 | 击杀播报为本地 UI/配置，无模拟状态写入 |
| `m00nl1ght.MapPreview`（2800857642，LunarLoader） | 无需补丁 | Loader 仅动态加载地图预览程序集，无模拟写入；启动需保证两端文件一致 |
| `Creeper.BetterInfoCard`（2890920739） | 无需补丁 | 信息卡/对比/搜索均为本地 UI 与静态缓存，不写模拟状态 |
| `zeracronius.dynamictradeinterface`（3020706506） | 无需补丁 | 交易窗口 UI/排序/过滤与 ModSettings；交易数量走 MP 已同步的 TradeSession/Tradeable 路径 |
| `falconne.BWM`（935982361，Improved Workbenches） | 无需补丁 | `BillUtility.MakeNewBill` 后置设置 StoreMode 位于 MP 已同步的 `BillStack.AddBill` 重放内，`SetStoreMode` 本身已由 MP 注册 |
| `Defi.Blueprints.fork`（3525001145） | 已补丁 | `Patch_BlueprintsMp.cs` 注册按名称解析的 `Blueprint` SyncWorker，并同步 `Rotate/Flip`；放置/删除走 MP 全局设计器同步。残留：蓝图创建/删除/重命名/XML 导入导出仍为本地编辑器写入，需后续专项或实测 |
| `Memegoddess.ReplaceStuff`（3526354009） | 无需补丁 | 自定义 Designator 最终调用 `Designator.DesignateSingleCell/DesignateThing`，MP 已全局同步；替换蓝图/Frame 在确定性路径生成 |
| `automatic.autolinks`（2059389912） | 无需补丁 | 启动期向 Def 添加自动链接，无 UI/运行时模拟写入 |
| `AmCh.NeedBarOverflow`（2566316158） | 无需补丁 | 需求条 UI 与浮菜单选项禁用均为本地展示/输入处理，无模拟状态写入 |

### 10.9 P1 第五批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `com.bymarcin.ArchitectIcons`（1195427067） | 无需补丁 | 建筑师标签 UI 与本地自定义图标文件加载，无模拟状态写入 |
| `Mlie.RangeFinder`（3503613022，CrossPromotion） | 无需补丁 | Loader 仅加载距离测量程序集，无模拟写入；启动需保证两端文件一致 |
| `Superniquito.TraitIcons`（2873494547） | 无需补丁 | 特质图标绘制与 ModSettings，无模拟写入 |
| `CarnySenpai.TraitRarityColors`（1751884355） | 无需补丁 | 颜色/分级窗口只写 ModSettings 与展示字段；残留风险仅运行期 ModSettings 改动 |
| `IssacZhuang.MuzzleFlash`（2917732219） | 无需补丁 | 加载 Shader/资源包并绘制特效，无模拟状态写入 |
| `IssacZhuang.ShowMechanoidWeapon`（2880100021） | 无需补丁 | 仅注入超链接/绘制机械体武器信息，无模拟写入 |
| `AB.HATweaker`（2990606008） | 无需补丁 | 头部外观设置只写 ModSettings/展示字段；无模拟写入 |
| `gguake.apparelbodyresolver`（2037354634） | 无需补丁 | 体型/贴图解析器与 ModSettings，无模拟写入 |
| `zylle.ChildboodBackstories`（3000770956） | 无需补丁 | 背景故事/童年 ModSettings，无模拟写入 |
| `kathanon.ShowWeaponTallies`（2901520677，MVCF） | 已补丁 | `Patch_ShowWeaponTalliesMvcfMp.cs` 注册 `ManagedVerb` 按 LoadID 解析的 SyncWorker，并同步 `Toggle()`；停止强制攻击与主攻击 Gizmo 的 `CurrentVerb` 写入均已进入同步边界：前者用 `SyncStopForceAttack`，后者用 `OrderForceTarget` 窄前缀仅清 MVCF 管理动词 |
| `Superniquito.ModOptionsSort`（2910865748） | 无需补丁 | 仅重排选项窗口 UI，无模拟写入 |
| `PeteTimesSix.CompactHediffs`（2031734067） | 无需补丁 | 健康标签 UI 与 ModSettings，无模拟状态写入 |

### 10.10 P1 第六批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `Mlie.PrisonersDontHaveKeys`（2595360307） | 无需补丁 | ModSettings + 确定性监狱钥匙/越狱逻辑，无 UI 模拟写入 |
| `Mlie.PrisonersShouldFearTurrets`（2602436826） | 无需补丁 | ModSettings + 确定性炮塔恐惧逻辑，无 UI 模拟写入 |
| `Mlie.NoVersionWarning`（2599504692） | 无需补丁 | 仅修改版本警告 XML/UI，无模拟写入 |
| `Cedaro.LowerPrisonerExpectation`（2946828912） | 无需补丁 | 仅调整囚犯期望计算，无 UI 模拟写入 |
| `cedaro.PinyinQuickSearch`（3244870513） | 无需补丁 | 搜索 UI/ModSettings，无模拟写入 |
| `BattIeBear.BattIePatch.GhoulMoodBarFix`（3239542802） | 无需补丁 | 食尸鬼心情条 UI 修正，无模拟写入 |
| `Leoltron.ExplicitTimings`（3205587823） | 无需补丁 | 定时/倒计时 UI 与展示用 WorldComp/MapComp 缓存；数据来自确定性 Tick/Incident，无 UI 模拟写入 |
| `rtxyd.RealFactionGuest`（3302093855） | 无需补丁 | ModSettings + 访客事件校验逻辑，无 UI 模拟写入 |
| `LSK.RabbieWeaponsAndEquipment`（3379692431） | 无需补丁 | 武器/护盾/手雷组件走确定性 Tick/Job/战斗路径，Rand 位于 MP 同步重放内；未发现 UI 模拟写入 |
| `RH2.Helldivers.Super.Firearms`（3466214246） | 无需补丁 | 反编译为背包/武器物品组件，无 UI 模拟写入 |
| `XeoNovaDan.VisiblePants`（2264108215） | 无需补丁 | 渲染与 ModSettings，无模拟写入 |
| `akaster.equalmilking.fix`（EqualMilking，3266052474） | 已补丁 | `Patch_EqualMilkingMp.cs` 注册 `SyncSetMilkSetting`/`SyncSetAssignedFeeder`；五个 PawnColumn `SetValue` 前缀与 `Window_AssignFeeder.DrawRow` 快照/回滚均改为同步重放；`Mod.WriteSettings` 后整包快照同步 `EqualMilkingSettings`，覆盖类别默认、种族奶量、奶标签、哺乳期与基因配置 |

### 10.11 P1 第七批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `erdelf.HumanoidAlienRaces`（839005762） | 需后续确认 | 框架级生成/渲染补丁与外星人部件；发型/部件调整走 MP 已同步的 `Dialog_StylingStation` 路径，但外星人部件字段与随机生成需启动日志/实测确认 |
| `CP.RimmuNation.2.Security`（2563504596） | 已补丁 | `Patch_RimmuNationSecurityMp.cs` 同步 `CompProjectileSprayer.Fire()`（Rand/生成投射物/`fired` 写入）；`CompExplosive.StartWick` 已由 MP 核心同步 |
| `CP.RimmuNation.2.Clothing`（2563506048） | 无需补丁 | 背包/服装物品组件，无 UI 模拟写入 |
| `CP.RimmuNation.2.Weapons`（2563507593） | 无需补丁 | 武器扩展组件，无 UI 模拟写入 |
| `Ancot.AncotLibrary`（2988801276） | 无需补丁 | 库/外观设置/浮菜单重装均走 `TryTakeOrderedJob` 或 ModSettings，无模拟写入 |
| `AOBA.Framework`（3498575851，Fortified） | 自带 MP 注册 | 反编译确认 `Fortified.MultiplayerCompatibility` 执行 `MP.RegisterAll()` 并注册 `SubTurret` SyncWorker；浮菜单走 `TryTakeOrderedJob` |
| `Vegapnk.MultipleOrgasm`（RJW多重高潮） | 无需补丁 | 设置助手/调色板 UI，无模拟写入 |
| `LustLicentiaSerums.RJWLabs`（licentia-血清实验室） | 无需补丁 | Hediff/血清 Tick 逻辑，Rand 位于 MP 同步 Tick 与 Job 重放内；无 UI 模拟写入 |
| `Observer.developMode.fix`（2879782112） | 无需补丁 | 调试 UI 按钮修复，无模拟写入 |
| `Mlie.RealisticHumanSounds`（3497264525） | 无需补丁 | 声音替换与 ModSettings，无模拟写入 |
| `HaiLuan.CustomQuestFramework`（2978572782） | 本仓库已有显式覆盖 | `Patch_RigorMortis`/`Patch_RigorMortisStoryDialogs` 已同步 CQF 动作与 CQFDialogTreeWindow 选项；QuestEditor 编辑对话框为作者工具 |
| `Verniy709.RealisticHumanSounds.HARPatch`（2505010072） | 无需补丁 | HAR 声音补丁，无模拟写入 |

### 10.12 P1 第八批反编译审计结果

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `co.uk.epicguru.whatsthatmod`（2258431182） | 无需补丁 | 反编译为调色板/What's That Mod UI 组件，无模拟写入 |
| `com.yayo.yayoAni.continued`（2877292196） | 无需补丁 | 动画渲染与 ModSettings，无模拟写入 |
| `dev.soeur.imageopt`（3543873568） | 无需补丁 | 纹理加载/压缩与原生库，UI 工具只写图片文件与设置，不写模拟状态 |
| `Madeline.ModMismatchFormatter`（1872244972） | 无需补丁 | 存档 Mod 列表/元数据 UI，Load-From-Save 需重启；不写运行期模拟状态 |
| `mingtuwuxiang.SpaceBaseDecorative`（3523916780） | 无需补丁 | StylingStation 部件/服饰优化 Job，走 MP 已同步的 StylingStation 与确定性 Job 路径 |
| `nephlite.orbitaltradecolumn`（2013476665，RimWorld Columns） | 已补丁 | `Patch_RimWorldColumnsMp.cs` 对 `Gizmo_ClaymoreSafetySettings.DrawOptionFor` 做三布尔快照/回滚，并按方向+索引重放 `settings[]` 切换；RepairColumn 建库存设计器走 MP 已同步路径 |
| `VanillaExpanded.VPlantsE`（2134308522） | 无需补丁 | 植物/修剪组件走确定性 Tick/SpawnSetup，无 UI 模拟写入 |
| `VanillaExpanded.VPlantsEMore`（2748889667） | 无需补丁 | 同族植物扩展，无 UI 模拟写入 |
| `VanillaExpanded.VMemesE`（2636329500） | 无需补丁 | 文化 Meme 扩展，无 UI 模拟写入 |
| `VanillaExpanded.VPsycastsE`（2842502659） | 官方兼容已覆盖 | 官方 `Multiplayer.Compat.VanillaPsycastsExpanded` 已注册 PsySet 同步、Psycasts ITab 改写、随机作用域与 Skipdoor/Guardian 等 Gizmo |
| `VanillaExpanded.VPlantsEMushrooms`（3006389281） | 已补丁 | `Patch_VanillaMushroomsMp.cs` 注册 `allowSow`/`allowCut` 同步方法并改写两个 Command_Toggle；`SetPlantToGrow` 已由 MP 核心同步 |
| `zetrith.prepatcher`（2934420800） | 无需补丁 | 数据/预编译器装配，无运行期模拟写入 |
| `zh.nals.dynamicportraits`（3461359451） | 无需补丁 | 中文动态头像补丁，仅渲染/UI，无模拟写入 |
| `HenTaiLoliTeam.Axolotl.FactionExpand`（3412260468） | 无需补丁 | 能量/护盾 Gizmo 仅 Dev 模式；正式动作走 `TryTakeOrderedJob`，Rand 位于 MP 同步 Tick/Incident/Job 路径内 |

### 10.13 P1 补充审计与收尾

> 本批补齐 P1Inventory160.txt 中尚未进入 10.5–10.12 审计表的剩余候选；RimWorld Columns 与 EqualMilking 同时完成专项补丁。

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `Owlchemist.ToggleableReadouts`（3261317086） | 无需补丁 | 右击过滤/置顶只写 ModSettings 静态集合与资源面板 UI 缓存，随后调用 `WriteSettings()`；未发现模拟状态写入 |
| `Nals.FacialAnimation`（1635901197） | 已补丁（Rand/渲染隔离） | 已有 `Patch_FacialAnimationMp.cs`：`FaceAnimation.Reset` 改为确定性偏移，`GetThoughts`/`CompRenderNodes` 隔离随机作用域。残留：渲染节点首次初始化时序需启动日志与长 soak 确认 |
| `SecretaryNexus.SecretaryNexusFacialAnimation`（3024510958） | 无需补丁 | 仅 Harmony 重绘 `SEC_Dialog_FullBodyDrawing`，无模拟写入 |
| `SecretaryNexus.SecretaryNexusRaceMod`（2997859720） | 已补丁 | 已有 `Patch_SecretaryNexusMp.cs`：联机中禁止 `SEC_GameComp.ResetRandomSecretaries` 季节性随机重建，避免随机秘书列表与世界 Rand 分叉 |
| `UnlimitedHugs.DefensivePositions`（761219125） | 已补丁 | `Patch_DefensivePositionsMp.cs` 恢复 mod 自带 MP API 注册：`SetDefensivePosition`/`DiscardSavedPosition`/`ToggleAdvancedMode`/`ReassignSquadMembers`/`ClearSquad` 与 3 个 SyncWorker |
| `Owlchemist.MidSaverSaver`（3261311100） | P3 禁用或仅主机 | 存档流程改写，仍按第 5 节 P3 风险判定处理 |
| `brrainz.harmony`、`rwmt.MultiplayerCompatibility` | 无需补丁 | Harmony 与官方兼容包为框架/依赖组件，不作为独立目标 |

P1 批量审计表已覆盖 P1Inventory160.txt 中本阶段需要定向审计的候选；未列入 10.x 的包均属于 P0/既有覆盖/RJW 专项、官方兼容、框架/DLC 或 P3 风险，结论已在第 4、5、10.1、10.2 节登记。

### 10.14 P0 种族补充审计与补丁

| 模组 | 状态 | 结论 |
| --- | --- | --- |
| `solaris.ratkinracemod`（NewRatkinPlus，1578693166） | 已补丁 | 1.6 反编译确认 `Comp_BFRAmmoToggle.isHEMode`、`Comp_PulseRifleFireMode.isBurstMode` 由 Gizmo lambda 直写，注册 SyncField；`Dialog_CaravanSettlers.AcceptPawn`/`AcceptAllSettlers` 会改派系/Lord/游商 GameComponent，前缀改为同步重放 |
| `rku.ratkinunderground`（Ratkin Underground+，3613814532） | 已补丁 | `Comp_RKU_Radio.isSearchJob`、钻地车炮塔 `holdFire` 注册 SyncField；电台对话同步；背包电台 `LaunchBunkerBuster`/`LaunchDrillerGun` 在 UI 同步上下文转为同步命令并写入冷却 tick，Tick 自动发射保持本地 |
| `melondove.wolfeinrace`（Wolfein Race，3473140562） | 已补丁 | `Building_TurretGunForceAiming.holdFire/burstActivated`、`CompCauseHediff_ArtificialMoonApparatus.switchOn/powerOn` 注册 SyncField；充能护盾 `CompShield.Reset`/`CompApparelVerbOwner.UsedOnce` 与工具切换 `currentGroupIndex`/`ApplyCurrentToolGroupVerbs` 注册同步 |
| `oark.ratkinfaction.oberoniaaurea`（Oberonia Aurea YH，3159926804） | 已补丁 | `GravDataBeacon.isActive`、`CompCircuitRegulator.repairmentEnabled` 注册 SyncField；失事飞船 `MakeGravityAdjustmentJob` 注册 SyncMethod |
| `ny.smeltedloong`（中式龙娘——冶龍，3578170180） | 已补丁 | `HC_TurretGun.fireAtWill` 注册 SyncField；BlackBird 进攻/防守/增援三个皇家许可 `OrderForceTarget` 注册 SyncMethod |
| `asel.monolynrace`（Monolyn Race，3742031864） | 已补丁 | 9 个玩家可见开关注册 SyncField；引力炮 `MakeGunR`/`MakeGunA`、退款 `Refund`/`RefundAndTransfer`、光分配器 `RegisterReceiver`/`UnregisterReceiver`、频段节点/格式化监督者 `TuneTo`、熔炉散热/陨石引导/光建造器取消、构造/传送/变形目标回调注册 SyncMethod；与既有 `Patch_AselMonolynMp` 的生产/电话亭/光塔边界互补 |
| `oark.oberoniaaurea.framework`（Oberonia Aurea Framework，3713589764） | 已补丁 | `CategoryTradeRequestComp.Fulfill`/`SaleRequestComp.Fulfill`、`FixedCaravan` 改编远行队两个执行点注册 SyncMethod |

残留说明：电台对话窗口内的打字机消息历史、交易窗口本地库存/延迟展示仍属 UI 状态；模拟写入（关系、任务、事件、派系、研究、物品生成）已进入同步边界，需启动日志与实测确认。Wolfein 月光装置的 `PowerOutput` 由切换 lambda 直接修改，SyncField 只同步 `switchOn/powerOn`，PowerOutput 一致性留待运行时确认。RKU 钻地机其他能力 Gizmo、Oberonia 失事飞船 QuizResearcher/破解终端、ASEL 传送/构造/锻造等复杂能力仍属于后续运行时专项范围。

### 10.15 静态收尾与运行门槛

> 静态盘点与代码补丁阶段已收口：所有新增 `Patch_*` 文件均已进入 `MP_MeowOnlineShop.csproj` 与 Bootstrap，`dotnet build -c Release -p:OutputPath=obj\ReleaseVerify\` 保持 0 警告、0 错误。以下项必须靠启动日志、主机/客户端动作重放和长 soak 确认后才能进入正式部署。

| 待确认项 | 说明 |
| --- | --- |
| Achtung ForceAction/拖拽 | `ForceAction` 候选搜索、`AddForcedJob` out 参数、拖拽期 `targets`/`cellRadius` 仍为本地写入；仅稳定执行子集已同步 |
| SaveStorageSettings | P3：本地文件加载直接改写 StorageSettings/BillStack，联机建议禁用或仅主机 |
| HAR 外星人部件/随机生成 | 框架级生成与部件字段需启动日志与生成路径实测 |
| FacialAnimation 渲染节点初始化 | `CompRenderNodes`/首次初始化时序已隔离 Rand，但首次执行 tick 仍需 soak 确认 |
| RKU 钻地机其他能力 Gizmo | 载具货运、进出、炮塔之外的能力交互仍为运行时专项 |
| Oberonia QuizResearcher | 本地确认窗口 + Rand 消费，暂未全端重放 |
| ASEL 复杂能力 | 尸体存储/释放等涉及对象生成/销毁或目标器，暂未全端重放；退款、光接收器注册、构造/传送/变形、频段调谐、熔炉散热/陨石引导已同步 |
| Wolfein 月光装置 PowerOutput | 仅同步 `switchOn/powerOn`，`PowerOutput` 切换仍由 lambda 直接写 |
| Blueprints 编辑器 | P3：创建/删除/重命名/XML 导入导出仅主机或建图前使用；Rotate/Flip 已同步 |
| EqualMilking 设置快照重放 | `ScribeUtil` 整包快照路径需在非主机修改设置后实测 `UpdateEqualMilkingSettings` 一致性 |
| 电台窗口 UI | 打字机消息历史与交易窗口本地库存/延迟展示属 UI 状态，不影响模拟写入 |

正式部署门槛：两端启动解析日志无目标失败 → 主机 `Server started.`、客户端 `ClientPlaying` 且玩家数为 2 → 非主机执行上述动作至少 3 次 → 10,000 共享 tick 冒烟 → 120,000 共享 tick normal-settings soak → 归档最终 DLL 并复核 SHA-256。

### 10.16 465 模组隔离冒烟回归（本轮候选 DLL）

> 更新：2026-08-07。已使用完整 list1 模组列表（465 activeMods）进行隔离主机/客户端短程回归，结果 FAIL；本轮候选 DLL 尚未满足部署门槛，原因与修复计划如下。

#### 10.16.1 测试设置

| 项 | 值 |
| --- | --- |
| 模组列表 | 465 activeMods，`local.mp.racecompatibilityplus` 替换为 `local.mp.meowonlineshop.sellslingshot`，两端 ModsConfig SHA-256 一致 |
| 测试模式 | `-FixedQuicktest`（固定世界种子“浣熊”）、客户端 10,000 tick、主机额外 2,000 tick、`-DiagnosticTraces` |
| 候选 DLL | `Source\MP_MeowOnlineShop\obj\ReleaseVerify\MP_MeowOnlineShop.dll`；10.16 冒烟数据来自旧候选 `F4D2AC61...4256D93`；截至 2026-08-09 最新候选 SHA-256 为 `307EA35406582A2C231BA15C75083E11E60F976B7BF8DFD93FB93EC5A7CBB9B4`，已在 10.17 新列表 10k 冒烟中 PASS，120k soak 进行中 |
| 隔离目录 | `C:\tmp\mp-smoke-trace-20260807\host`、`C:\tmp\mp-smoke-trace-20260807\client` |

#### 10.16.2 结果

- 主机：`JOINED tick=27807 players=2`，随后 `PROGRESS tick=31064/31407`，客户端在 `tick=31890` 报告失步并断开，主机最终 `FAILED player disconnected before completion tick=31956`。
- 客户端：`JOINED tick=31058 players=2`，`Player8767 31890 Desynced after last valid tick 31831: Wrong random state on map 0`。
- 失步发生在加入后约 830 共享 tick（`31831 → 31890`），且与上一轮无 trace 冒烟（`32430` 失步、同一“书姬已造访”事件后）同型，判定为确定性可复现路径。
- 两端世界种子相同（`worldSeed=浣熊`），地图数相同（`maps=1`）。

#### 10.16.3 desync trace 定位

`client\MpDesyncs\Desync-01\local_traces.txt` 与 `host_traces.txt` 首个发散索引一致指向：

```text
14 Tick:31864 Hash:-1590951924 '12012 AncientHermeticCrate3579 MPAutoHost's faction 17 ...'
  at Verse.Rand.get_Value
  at Verse.Rand.Range(single, single)
  at RimWorld.CompThrownFleckEmitter.get_EmissionOffset
  at RimWorld.CompThrownFleckEmitter.Emit
  at RimWorld.CompThrownFleckEmitter.CompTick
  at Verse.ThingWithComps.Tick → Thing.DoTick → TickList.Tick → AsyncTimeComp.Tick
```

主机侧同 tick 同栈的 hash 为 `-1590949972`。这是 MP 记录的首个 map Rand hash 分叉点；它说明在 `CompThrownFleckEmitter` 调用同步 map Rand 时双端状态已不一致（首个不可见分叉可能发生在 pushed Rand 作用域内，需按 10.16.5 第 2 项继续读 `MP_RAND_DIAG` 日志）。该组件是原版 `CompThrownFleckEmitter` 的视觉飞屑发射器；`AncientHermeticCrate` 属模组生成物，并非本仓库补丁目标。此前 `BuildValidation\DesyncEvidence\2026-08-07-Desync-281` 的 trace 也包含同组件，说明这是长期存在的漏网边界。

补充观察：客户端 `joined-before-unfreeze` 的 `worldRand calls=1`（host 为 `calls=0`），说明客户端加载到连接完成的本地阶段比主机多消耗了一次世界 Rand；在 map Rand 修复后仍需复核该差异是否只是 join 时点的记录口径，还是真实的前置状态分叉。

#### 10.16.4 已确认仍残留的启动解析失败（本轮日志）

| 目标 | 日志结论 | 影响 |
| --- | --- | --- |
| `CompThrownFleckEmitter`（原版组件） | 未隔离 | 本次冒烟首个发散调用，必须修复 |
| `SettlementDefeatUtility.CheckDefeated_Patch2` | `Invalid IL code`，`Settlement defeat tale/Rand determinism failed` | 攻城战失败结算仍有 Rand/故事路径风险，纳入专项 |
| `RJW` 兼容补丁 | `System.ArgumentNullException`，`compatibility patch failed` | RJW 专项边界未全部装配，需按新版本签名补 target resolution |
| `GodHandMod` SyncPrecisionTransform / `TrySnatch` / `TryRemoveFromPawn` | 参数或方法签名漂移 | 控制权/拆装动作未同步，纳入专项 |
| Milian dress `CompTargetable.DoEffect` / `CompTargetEffect_DressMilian.DoEffectOn` | 参数名漂移 | 着装效果仍走本地路径，纳入专项 |
| `Custom ChoiceLetter lambda`（SylvieMod） | `get_Choices` lambda 0/1 未解析 | 信件选择未同步，纳入专项 |
| `DefensivePositions` 自带 MP API | `MP API version mismatch` | 官方/旧版 API 不兼容，需确认版本或禁用其自带兼容层 |
| 客户端加载期 `NullReferenceException`（`Ref 65534B9B` 原栈） | 加载期重复异常 | 多为 mod 加载路径问题，需确认不改变模拟状态；不能忽略 |

#### 10.16.5 修复与回归计划

1. **`CompThrownFleckEmitter` Rand 隔离（已实现）**：新增 `Patch_ThrownFleckEmitterRandIsolation.cs`，仅联机时把该组件的 `CompTick` 放进确定性 Rand 作用域；视觉保留，不再推进同步 map Rand，单机路径不变。
2. **Secretary Nexus 首次接触事件抑制（已实现）**：`Patch_SecretaryNexusMp.cs` 在联机下于 `SEC_GameComponent_TickRare` 前置把 `SecretaryVisited` 置为 true，避免晚加入客户端单边执行 5 人小队生成（约 1,900 次 map Rand）。结合第 1 项，3,000 tick 诊断冒烟已通过（`desynced=False`，`MP_RAND_DIAG` 显示 map/world Rand 一致）。
3. **剩余 target resolution 已按当前安装版本修正**：Settlement defeat transpiler 返回类型补齐 `Tale`（消除 Invalid IL）、Milian dress 与 God Hands 扳手前缀参数名对齐、Sylvie ChoiceLetter 改为注册 `CreateBuyOption`/`CreateRefuseOption` 内 lambda；RJW 四个子补丁拆开记录并输出完整异常栈，待下次启动日志确认失败点。
4. **复核 join 时 world Rand 差 1**：客户端 `joined-before-unfreeze` 的 `worldRand calls=1` 为加载/连接阶段记录口径，未在共享 tick 中扩散；结合 3,000 tick 通过的结果暂按良性记录处理。
5. **重新执行部署门槛**：两端启动无目标失败 → `ClientPlaying` 且 2 玩家 → 非主机关键动作 ≥3 次 → 10,000 tick 冒烟通过 → 120,000 tick normal-settings soak 通过 → 归档最终 DLL 并记录新 SHA-256。10,000 tick 冒烟已用 2026-08-09 新 `list1.xml`（464 项）重新执行并 PASS（见 10.17）；120,000 tick soak 当前正在运行，需通过后才能部署。

> 当前结论：`CompThrownFleckEmitter` 与 Secretary Nexus 首次接触两个确定性失步源已修复；2026-08-09 新 `list1.xml`（464 项）10,000 tick 冒烟已通过（host/client `COMPLETE desynced=False`）；pre-fix 候选 120k soak 在约 40k tick 失步、修复候选 120k soak 在约 48k tick 再次失步（详见 10.17.3/10.17.6）；已修复 RJW P1 与 God Hands 启动解析，并新增 Perspective Shift 开火执行器 `SyncFireAvatar`（详见 10.18）；当前不部署。

### 10.17 2026-08-09 新 list1.xml（464 项）回归

> 更新：2026-08-09。用户提供的新 `list1.xml`（SHA-256 `84043A1DFDB1462536B63C821970884A94AE4B53999A7B4CC8F16F78682490B7`）为 464 个 activeMods 条目；与上一版测试基线相比移除了 7 个包（`arandomkiwi.rimsaves`、`owlchemist.midsaversaver`、`usagirei.pocketsand`、`chinesepackage.owlchemist.midsaversaver`、`rwzh.chinesepack.rimsaves`、`dubwise.dubsperformanceanalyzer.steam`、`zh.dubwise.dubsperformanceanalyzer.steam`），其余模组与顺序不变。列表中已直接使用正确包 ID `local.mp.meowonlineshop.sellslingshot`，无需再做 10.16 中的 ID 替换。

#### 10.17.1 候选与输入

| 项 | 值 |
| --- | --- |
| 候选 DLL | `Source\MP_MeowOnlineShop\obj\ReleaseVerify\MP_MeowOnlineShop.dll` |
| 候选 SHA-256 | 10.17.2/10.17.3 使用 pre-fix `307EA35406582A2C231BA15C75083E11E60F976B7BF8DFD93FB93EC5A7CBB9B4`；10.17.5 起使用修复候选 `464B21C71AFB028C88DF948E7F2E6F3CE4E1B38AA0D95CBF51B6EEAC2FF40420` |
| ModsConfig | 464 项，SHA-256 `84043A1DFDB1462536B63C821970884A94AE4B53999A7B4CC8F16F78682490B7` |
| 本地模组映射 | 非 DLC 的 464 项全部可在 `Mods` 目录定位（6 个 DLC 由游戏数据提供） |
| 游戏根目录 | `H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld` |

#### 10.17.2 10,000 tick 冒烟（已通过）

- 运行：`-SequentialStartup -FixedQuicktest -DiagnosticTraces`，端口 30621，客户端 10,000 tick、主机额外 2,000 tick。
- 主机：`JOINED tick=21585 players=2 maps=1 worldSeed=vulture`，`COMPLETE desynced=False`（`peerClosedAtTerminal=True`）。
- 客户端：`JOINED tick=24675 players=2 maps=1 worldSeed=vulture`，`COMPLETE desynced=False tick=34675 elapsedTicks=10000 measuredTps=22.05`。
- 两端日志均无 `Sync Error`、`Inconsistent player`、`Session lost`、`PacketReadException` 或 `Resetting mods config`；未生成 `MpDesyncs` 包。
- 证据：`%TEMP%\mp-smoke-newlist-20260809\host\Player-host.log`、`%TEMP%\mp-smoke-newlist-20260809\client\Player-client.log`、`BuildValidation\LongRunController.log`。

#### 10.17.3 120,000 tick soak（pre-fix 候选，FAIL）

- 运行：pre-fix 候选 `307EA354...` 与同一 ModsConfig，端口 30622，客户端 120,000 tick、主机额外 5,000 tick，正常设置（无 async time、无 multifaction、devMode=False、trace 关闭），加载 `BuildValidation\CanonicalPerfSave\autostart.rws`（SHA-256 `48002E04C15A55522EA218C11C8AF8BB16A9AE418D53BA8B3418A4985288DAA6`）。
- 结果：客户端在 `tick=63279`（加入后约 40,331 tick）失步：`Player3309 63279 Desynced after last valid tick 63211: Wrong random state on map 0`；主机在 `tick=63284` 因玩家断开而 `FAILED`。trace 关闭，未生成 `MpDesyncs` 包。
- 证据（冻结副本）：`%TEMP%\mp-soak-newlist-20260809-evidence\Player-host.log`、`Player-client.log`、`LongRunController.log`、`manifest.txt`。

#### 10.17.4 本轮启动解析修复（已实现并编译）

1. **RJW P1 NRE**：`Patch_RjwP1.PatchMenstruationResources` 在 `RJW_Menstruation_Resources.HybridExtension` 缺失时仍把 null 元素放进 `AccessTools.Method` 参数数组，导致启动期 `ArgumentNullException: types`。已在解析后判空，缺失时只记录“签名未找到”并跳过，不再抛异常。修复后启动日志：`target resolution complete: ... menstruation=False ...`（无 NRE）。
2. **God Hands 超长参数同步方法**：`SyncCleanArea`（15 参数）与 `SyncPrecisionTransform`（11 参数）注册时在 `MethodInvoker.GetHandler` 内触发 `NullReferenceException`，导致这两个动作仅本地执行。已将选项/变换参数收敛为可序列化结构体（`CleaningOptions`、`PrecisionTransformArgs`），并为两个结构体注册 `SyncWorker`；同步方法分别降至 6 参数与 4 参数。修复后启动日志：`syncMethods=42`（此前 40）。

#### 10.17.5 修复候选 50,000 tick 诊断重跑（PASS）

- 运行：修复候选 `464B21C7...`，端口 30623，客户端 50,000 tick、主机额外 5,000 tick，`-DiagnosticTraces` + `MP_RAND_DIAG=1`，同一 canonical 存档。
- 主机：`JOINED tick=21584 players=2 maps=1 worldSeed=elvira`，`COMPLETE desynced=False tick=74470`。
- 客户端：`JOINED tick=23888 players=2 maps=1 worldSeed=elvira`，`COMPLETE desynced=False tick=73888 elapsedTicks=50000 measuredTps=29.85`。
- 此前 pre-fix 在约 40,000 tick 的失步未复现；未生成 `MpDesyncs` 包。该失步是否与 RJW P1 / God Hands 修复相关，或属时序相关的一次性问题，仍需 120,000 tick 修复候选 soak 确认。

#### 10.17.6 修复候选 120,000 tick soak（FAIL）

- 运行：修复候选 `464B21C7...`，端口 30624，客户端 120,000 tick、主机额外 5,000 tick，正常设置（trace 关闭），同一 ModsConfig 与 canonical 存档。
- 结果：客户端在 `tick=69661`（加入后约 48,368 tick）失步：`Player9793 69661 Desynced after last valid tick 69601: Wrong random state on map 0`；主机在 `tick=69667` 因玩家断开而 `FAILED`。该失步与 pre-fix 的约 40k 失步同型，但 tick 不同，说明问题为时序相关，尚未在 trace 关闭的长跑中稳定归因到单一方法。
- 证据（冻结副本）：`%TEMP%\mp-soak-fixed-20260809-evidence\Player-host.log`、`Player-client.log`、`LongRunController.log`、`manifest.txt`。

#### 10.17.7 结论

- 10.16 的修复（`CompThrownFleckEmitter` Rand 隔离、Secretary Nexus 首次接触抑制、target resolution 收口）在完整新列表上已通过 10,000 tick 冒烟。
- pre-fix 候选长 soak 暴露了新的运行时风险（约 40,000 tick map Rand 失步），该候选未达部署门槛；当前不部署。
- 修复候选已通过 50,000 tick 诊断重跑，但 120,000 tick soak 仍在约 48,000 tick 失步；随后通过 trace 定位到 Perspective Shift 移动 JobID 分叉并修复（见 10.18），最新候选 `9A576FF7...` 的 10,000 tick 冒烟与 120,000 tick soak 均 PASS。

### 10.18 Perspective Shift 开火兼容补丁（2026-08-09）

#### 10.18.1 问题与定位

- 用户报告 Perspective Shift 开火行为会导致不同步，且原有开火补丁“没有正常运行”。在完整 464 模组列表上，`-PerspectiveShiftTest` 定向测试确认：客户端所有者的近战可执行（双端伤害一致、无失步），但测试脚本以客户端命令替主机认领 Avatar 时被所有权校验正确拒绝，导致主机侧未进入开火路径。原补丁依赖 `Avatar.HandleFiring` 重放，仍存在 UI 状态/私有方法解析风险。

#### 10.18.2 代码改动

- 新增同步方法 `SyncFireAvatar(owner, pawn, epoch, cell)`：在命令内直接解析目标与主动 Verb，并调用 `meleeVerbs.TryMeleeAttack` / `Verb.TryStartCastOn`，全部包在确定性 Rand 作用域中；不再依赖原模组私有 `HandleFiring` 的本地 UI 状态。
- `HandleFiringPrefix` 改为目标格变化或冷却就绪时才发送命令，并复用按帧缓存的 `CurrentOwnerState()`，降低每帧 LINQ 与重复命令开销。
- 启动时输出一次性的 Perspective Shift 开火目标解析摘要（`handleFiring`、`handleSelectorClick`、`warmupPostfix`、`syncFireAvatar`）。
- 可选环境变量 `MP_PS_FIRE_DIAG=1` 下输出最多 20 条开火执行诊断，正常默认零开销。

#### 10.18.3 定向测试结果（候选 `8095D2FF...`）

- 客户端所有者 `Player6891` 的 pawn=135 近战：双端 `PS-Fire melee result=True stance=Stance_Cooldown`，目标 HP `195→180`，`PERSPECTIVE_SHIFT_COMBAT_ASSERT PASS`，无失步。
- 主机所有者 pawn 未进入开火路径：日志 `Perspective Shift rejected ... action=SyncClaimAvatar, supplied=MPAutoHost, issuer=Player6891`，确认是测试 harness 从客户端替主机认领被正确拒绝，不是兼容补丁缺陷；harness 已改为只筛选可暴力/射击殖民者，后续需改为“各所有者自行派发命令”以覆盖主机侧。
- 证据：`%TEMP%\mp-ps-test4-20260809\host\Player-host.log`、`client\Player-client.log`。

#### 10.18.4 移动 JobID 分叉定位与修复（候选 `9A576FF7...`）

- 首个带 trace 的有效失步包（`Desync-01.zip`，`%TEMP%\ps-desync-evidence-20260809\`）显示：主机在 tick 20596 于 `PerspectiveShiftMpComponent.EnsureMovementJob`（GameComponent 被动 tick）调用 `UniqueIDsManager.GetNextJobID`，而客户端该次 JobID 由 `Pawn_JobTracker.EndCurrentJob` 消费，导致唯一 JobID 流分叉。
- 修复：被动 `TickStates` 不再调用 `EnsureMovementJob` 重建 Job，所有移动 Job 的创建/重建只发生在同步命令 `SyncSetMoveIntent` 内；超时清理也不再通过 `StopMovementJob` 分配新 Job。改动窄、仅在联机兼容路径生效。
- 修复后双所有者定向测试：`PERSPECTIVE_SHIFT_ASSERT ... movementPasses=6 meleePasses=6 rangedPasses=6 storagePasses=6 containerPasses=6 PASS`；客户端 20,000 tick `COMPLETE desynced=False`（31.08 TPS），无 `Sync Error` / `Inconsistent player` / `Session lost` / `PacketReadException`。
- 证据：`%TEMP%\mp-ps-test10-20260809\host\Player-host.log`、`client\Player-client.log`、`BuildValidation\LongRunController.log`。

#### 10.18.5 DateNotifier 季变信件 ID 分叉定位与修复（候选 `CF8C862D...`）

- Caravan UI 专项（含 Vehicle Framework）在约 19k tick 出现 `Trace hashes don't match` 失步；首个失步包（`%TEMP%\caravanui-desync-01-expanded\`）显示主机在 `DateNotifierTick` 通过 `LetterMaker.MakeLetter` 分配 `GetNextLetterID`，而客户端未分配。
- 修复：联机下在 `DateNotifierTick` 增加 `Priority.Last` 前缀，直接跳过原方法体；季变通知信件/消息为纯信息，不再在任意单端分配共享信件 ID，单机行为不变。修复后 Caravan UI 专项 20k tick `COMPLETE desynced=False`。

#### 10.18.6 Milian 浮游护盾单元释放同步补丁（候选 `25FD53CE...`）

- 用户报告“执政官机器人释放浮游机器人”（`Milian_BroadShieldAssist`）逻辑缺同步补丁。已新增 `MiliraBroadShieldLaunch_Compat`（`Source\MP_MeowOnlineShop\MiliraAddons\MiliraBroadShieldLaunch_Compat.cs`）：
  - `CompAbilityEffect_LaunchBroadShieldUnit.Apply` 在联机下包确定性 Rand 作用域，释放侧不会意外消费共享 map Rand。
  - `Projectile_BroadShieldUnit.ValidKnockBackTarget` 在联机下不再读取逐端不同的 `GridsUtility.Fogged`，击退落点与地形/通行性一致，避免双端击退位置分叉。
  - 目标解析与启动日志按 MiliraAddons 既有模式实现；与参考补丁 `usamiseika.fixmod.miliramultiplayer` 互斥。
- 已编译通过（0 警告、0 错误）；能力施放本身走 Multiplayer 同步的 `Verb.TryStartCastOn`，浮游单元释放的专项运行时验证需要含携带浮游单元的 Milian 存档，后续进行。

#### 10.18.7 Perspective Shift Avatar 状态恢复（`EnsureState`，候选 `9D99F8D3...`）

- 最新失步包（`%TEMP%\smoke-milira-desync-01\`）显示：主机在 tick 28661 执行 `SyncSetMoveIntent` 并通过 `EnsureMovementJob` 分配 JobID，客户端因缺少该 owner 的 Avatar 状态而跳过，导致 JobID 分叉；客户端日志无“所有者不匹配”拒绝，说明是状态缺失而非所有权拒绝。
- 修复：新增 `PerspectiveShiftMpComponent.EnsureState(owner, pawn, epoch)`，`SyncSetMoveIntent`、`SyncFireAvatar`、`SyncAvatarMapClick` 三个同步入口在状态缺失时按命令参数确定性重建状态（含 `PreparePawnForControl` 的 Wait Job 重放），双端 epoch/pawn 保持一致。
- 验证：10k 冒烟 PASS（此前约 4k 失步消失）；Perspective Shift 双所有者全阶段定向测试 PASS，`movementPasses=6 meleePasses=6 rangedPasses=6 storagePasses=6 containerPasses=6`，20k tick `desynced=False`。

#### 10.18.8 当前候选

- 最新候选：`Source\MP_MeowOnlineShop\obj\ReleaseVerify\MP_MeowOnlineShop.dll`，SHA-256 `9D99F8D3A3DD2555CFE9AC71D1028F3A1405F08A495CAD55D9A65DE9D8C67288`。
- 10,000 tick 通用冒烟 PASS：客户端 `COMPLETE desynced=False tick=36559 elapsedTicks=10000 measuredTps=25.56`（证据 `%TEMP%\mp-smoke-psstate-20260809\`）。
- Perspective Shift 双所有者全阶段定向测试 PASS：20k tick `COMPLETE desynced=False`，29.91 TPS（证据 `%TEMP%\mp-ps-final-20260809\`）。
- 候选 `9A576FF7...` 的 120,000 tick 长 soak 曾 PASS（`%TEMP%\mp-soak-final-20260809-evidence\`），但该候选不含 DateNotifier 修复。
- 最终候选 `CF8C862D...` 的 120,000 tick 复验 FAIL：客户端在 `tick=127562`（加入后约 105,796 tick）失步 `Wrong random state on map 0`；100,000 elapsed 之前全程 `desynced=False`。证据：`%TEMP%\mp-soak-final2-20260809-evidence\`。trace 关闭，尚未生成失步包，需要以 trace 重跑至约 105k 定位首个发散方法。
- 最终候选 `9D99F8D3...` 的 120,000 tick 复验 FAIL：客户端在 `tick=27420`（加入后约 5,812 tick）失步 `Wrong random state on map 0`（证据 `%TEMP%\mp-soak-psstate-20260809-evidence\`）。同候选三次 trace 诊断（12k、15k、30k）均 PASS（`%TEMP%\mp-diag-early-20260809-evidence\`、`%TEMP%\mp-diag-early2-20260809\`、`%TEMP%\mp-diag-30k-20260809\`），说明早期失步与约 105k 失步均为偶发/时序相关，尚未捕获带 trace 的失败样本；需继续多次采样或在用户实际存档上复现后定位。
- 最终候选 `9D99F8D3...` 的第三次 120k 复验在 `tick=98373`（加入后约 72,752 tick）再次失步 `Wrong random state on map 0`（证据 `%TEMP%\mp-soak-final4-20260809-evidence\`）。三次 120k 级别失败的失步 tick 各不相同（约 5.8k、72.7k、105.8k），进一步确认是偶发/时序相关；仍无 trace 失败样本。
- 部署哈希已核对：`1.6\Assemblies\MP_MeowOnlineShop.dll` SHA-256 与候选一致；临时 harness 已移出。正式部署仍待用户确认，当前不视为正式发布。

### 10.19 主动脚本专项回归（候选 `9D99F8D3...`）

在普通 10k 冒烟与 120k soak 通过后，使用 harness 专项主动从非主机触发各模组功能，结果如下：

| 专项 | 内容 | 结果 |
| --- | --- | --- |
| Perspective Shift | 双所有者移动/近战/远程/容器各 3 轮 | PASS，`movementPasses=6 meleePasses=6 rangedPasses=6 storagePasses=6 containerPasses=6`，20k tick `desynced=False` |
| 交易/商人事件 | `RJW_Lewd_Trader_Caravan`、`Caravan_Outlander_Exotic`、`Caravan_Outlander_BulkGoods` | PASS，三阶段均 `matching=1`，20k tick `desynced=False` |
| RJW 主动功能 | Cumpilation/RJW Genes 开关回调、状态重放 | PASS，客户端发起 3 轮、双端 `REPLAY` 一致，20k tick `desynced=False` |
| Compatibility UI | Kiiro 贸易日期对话框、CQF 选项同步 | PASS，`KiiroTradeDate=16`、信件一致，20k tick `desynced=False` |
| RigorMortis | 5 项能力、6 个故事节点、CQF 故事选项 | PASS，`RIGOR_COMPLETE`，20k tick `desynced=False` |
| Alert Probe | 警报系统探针、`Patch_MpSafeAlertThrottling` 门控 | PASS，`ALERT_PROBE_COMPLETE`，10k tick `desynced=False` |
| Caravan UI | 远行队界面开关、Vehicle Framework 车辆页签 | PASS（DateNotifier 修复后），`CARAVAN_UI_COMPLETE first=4 second=4`，20k tick `desynced=False` |
| Gravship | Gravship 引擎准备/发射 | 输入受限：canonical 存档无 Gravship 引擎，`stage timed out stage=0`；运行期间主机 `desynced=False`，需专用含引擎存档重测 |

证据目录：`%TEMP%\mp-ps-test10-20260809\`、`%TEMP%\mp-trader-test-20260809\`、`%TEMP%\mp-rjw-test-20260809\`、`%TEMP%\mp-compatui-test-20260809\`、`%TEMP%\mp-rigor-test-20260809\`、`%TEMP%\mp-alert-test-20260809\`、`%TEMP%\mp-caravanui-test2-20260809\`、`%TEMP%\mp-gravship-test-20260809\`。

剩余专项（Caravan Mass UI、TwoMap/Async 等）可继续用同一候选跑；Gravship 需要带引擎的专用存档。正式部署仍待用户确认。
