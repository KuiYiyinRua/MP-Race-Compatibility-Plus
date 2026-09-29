# list1.xml 全量联机兼容审计报告

> 审计日期：2026-08-09
> 输入：`H:/桌面/list1.xml`
> RimWorld：`1.6.4850 rev645`
> 清单 SHA-256：`B4E0167FA6B01AC6DD6159AC2EC00AE23B1C75C3CF38B2B1EF93A22A4214AF4B`
> 清单规模：458 个唯一 `activeMods`
> 审计对象：当前工作区源码、`1.6/Assemblies` 已部署文件、本地 Multiplayer 与 Multiplayer Compatibility 源码/反编译件、现有双端测试证据。

## 1. 最终结论

**没有完成清单中全部模组的全方位联机兼容，当前候选不应作为“已验证稳定版”发布。**

结论依据不是“缺少形式上的补丁数量”，而是已经存在下列硬证据：

1. 当前部署 DLL `9D99F8D3...` 在同一完整载荷的两次 120k 复验中分别于约 5,812 和 72,752 elapsed ticks 出现 `Wrong random state on map 0`；发布门槛已明确失败。
2. 当前源码重新编译出的 DLL 为 `9C81A43D...`，与部署/测试 DLL 不同，且没有对应双端 smoke/soak 证据。
3. `Patch_GravshipAbandonQueue.cs` 既没有加入旧式 `.csproj`，也没有从启动器调用；该修复目前是死代码。
4. 已启用模组的启动日志中仍有 Ratkin Weapons、Hospitality、I Will Be Back/AcceptJoiner、RJW P1、Axolotl 等关键目标解析或补丁安装失败。
5. 458 个包中，只有 79 个能通过“官方兼容包精确 packageId”或“本项目源码精确 packageId 字符串”建立直接包级覆盖证据；另有 178 个带 DLL 的包没有这类直接证据。类型级补丁能覆盖其中一部分，但尚未形成逐包来源、动作入口、目标解析和运行时证据闭环。
6. 发布目录混入测试 Harness，About 版本与程序集版本不一致，并存在候选、源码、PDB 不同批次的问题。

因此当前状态应标记为：

> **可编译；部分功能有定向 PASS；完整载荷长期确定性失败；覆盖审计未闭环；禁止宣称全部兼容或稳定发布。**

## 2. 覆盖盘点

| 分类 | 数量 | 判定 |
| --- | ---: | --- |
| Ludeon 核心/DLC | 6 | MP 核心负责，不计第三方专项补丁 |
| 官方 Multiplayer Compatibility 精确命中 | 12 | 有官方包级处理器，但仍需版本/目标解析和运行时验证 |
| 本项目源码精确出现 packageId | 68 | 只证明代码提及该包，不等于动作面完整或运行时通过 |
| 官方或本项目精确命中（去重） | 79 | 当前最强的包级静态证据集合 |
| 带 DLL、无官方/本项目精确 packageId 证据 | 178 | 其中部分存在类型级补丁；其余需逐包动作面审计 |
| 无 DLL、无精确覆盖证据 | 195 | 通常不需 SyncMethod，但仍需 Def/生成/存档/加载顺序一致性验证 |
| 总计 | 458 | 全部本地 packageId 均可定位 |

### 2.1 官方兼容包精确命中的 12 项

`unlimitedhugs.hugslib`、`adaptive.storage.framework`、`dubwise.dubsmintmenus`、`dubwise.dubsmintminimap`、`mlie.prisoncommons`、`merthsoft.designatorshapes`、`dubwise.dubspaintshop`、`haplo.miscellaneous.training`、`syrchalis.processor.framework`、`azuraal.choiceofpsycasts`、`albion.sparklingworlds.full`、`owlchemist.toggleableshields`。

注意：`nagisa.orion.hospitality` 不是官方处理器声明的 `Orion.Hospitality`。本项目虽增加了 fork packageId 别名，但实际运行日志显示目标签名仍未解析，故不能视为已覆盖。

### 2.2 已有类型级补丁但证据仍不完整的代表项

下列包没有全部通过 packageId 精确门控，主要依赖程序集/类型名反射：

| 包 | 现有类型级补丁 | 仍缺少的证据 |
| --- | --- | --- |
| `ancot.milirarace` / Milira 扩展簇 | `Patch_Milira*`、`MiliraAddons/*` | 每个扩展程序集哈希、所有能力/窗口/事件入口、双地图/重连；浮游盾和舰船仍未完整实测 |
| `ancot.kiirorace` | Kiiro 事件/对话相关补丁 | 种族本体全部 Gizmo、能力、世界事件动作清单 |
| `pupa.insectgirls` | 永久伤、生成阵营补丁 | 驯服、生成、遗传/组件初始化与重连验证 |
| `otyoty.sizedapparel` | RJW 序列化 Rand 隔离 | 穿脱、重绘缓存、存档/重连完整回归 |
| `c0ffee.rimworld.animations` | RJW 语音/动画 Rand 隔离 | 动画回调与模拟副作用边界证明 |
| `fuu.bloodanimations` | `Patch_BloodAnimationsMp` | 目标解析摘要和长跑证据 |
| `xmb.ancientthreat.mo` | `Patch_AncientAmorphousThreatMp` | 事件真实触发和双端不变量 |
| `trigger.eliteraid` | `Patch_EliteRaidDeterminism` | 多种 Raid 真实入口和 faction/Rand 断言 |
| `hailuan.gravshipexpanded` | `Patch_Gravship*` | 当前专项测试无引擎而超时；另有未编译死补丁 |
| `laayoune.shootingwall` | `Patch_TrueShootingWallMp` | 非主机真实操作三轮和目标解析 |
| `secretarynexus.secretarynexusracemod` | 首次接触/季节刷新抑制 | 禁用行为的玩法影响、重连与长期验证 |
| `thumb.goremod2` | Visual Brutality Rand/生成防护 | 斩首、碎片、尸体全路径定向测试 |

## 3. 阻断发布的问题

### P0-1：当前部署候选已经被长期测试证明确认会失步

部署文件：`1.6/Assemblies/MP_MeowOnlineShop.dll`
SHA-256：`9D99F8D3A3DD2555CFE9AC71D1028F3A1405F08A495CAD55D9A65DE9D8C67288`

| 证据 | 结果 |
| --- | --- |
| `mp-soak-psstate-20260809-evidence/manifest.txt` | FAIL；tick 27420，lastValid 27361，约 5,812 elapsed，`Wrong random state on map 0` |
| `mp-soak-final4-20260809-evidence/manifest.txt` | FAIL；tick 98373，lastValid 98311，约 72,752 elapsed，同型 map Rand 失步 |
| 前一候选 `CF8C862D...` | FAIL；约 105,796 elapsed 后 map Rand 失步 |
| 更早候选 `9A576FF7...` | 曾完成一次 120k PASS，但不包含后续 DateNotifier/Avatar 状态等改动，不能替代最终候选验证 |

这已经直接否定“全方位兼容完成”。短 smoke、20k 专项和一次旧候选 120k PASS 均不能覆盖最终候选的重复 FAIL。

**要求：** 保留首个带 trace 的有效失败包，定位最早分叉方法和 Rand 流；修复后必须用新的归档哈希从 Gate A 开始重新跑，不能复用旧候选 PASS。

### P0-2：当前源码、部署 DLL 和运行时证据不是同一候选

| 项 | 值 |
| --- | --- |
| 当前源码隔离 Release 构建 | 成功，0 warning / 0 error |
| 当前源码构建 SHA-256 | `9C81A43DD4CDD15A98EAB9260CD6229F53121D58B641FC315CDA2B629B40A419` |
| 已部署 DLL SHA-256 | `9D99F8D3A3DD2555CFE9AC71D1028F3A1405F08A495CAD55D9A65DE9D8C67288` |
| 结论 | 当前源码产物未运行时验证；部署产物已验证失败 |

当前工作树还有大量未提交源码修改。即便这些修改可能修复旧候选问题，也必须把 `9C81...` 或后续唯一归档候选重新执行完整 Gate A-E；不能将旧 DLL 的测试结果移植到新源码。

### P0-3：Gravship 关键修复是死代码

`Source/MP_MeowOnlineShop/Patch_GravshipAbandonQueue.cs`：

- 不在 `MP_MeowOnlineShop.csproj` 的 `<Compile>` 项中；240 个非 obj C# 文件中唯一漏编译的源文件。
- 全源码只有文件内部引用 `Patch_GravshipAbandonQueue`，启动器没有调用 `Apply`。
- 文件声称修复 Odyssey 起飞后旧地图命令队列/TickList 分叉，但当前 DLL 完全不包含这项保护。
- 现有 Gravship 专项因为测试存档没有引擎而 `stage timed out stage=0`，没有执行真实起飞/落地动作。

清单包含 Odyssey 与 `hailuan.gravshipexpanded`，因此这是实际覆盖缺口，不是无关死文件。

### P0-4：已启用模组存在明确目标解析/补丁安装失败

在当前部署候选的完整载荷启动日志中确认：

| 模组/功能 | 启动证据 | 风险 |
| --- | --- | --- |
| Ratkin Weapons+ | `BayonetJob_Patch1` transpiler 报 `Label #7 is not marked` | 刺刀路径补丁未安装；该异常虽被 optional wrapper 隔离，但功能未兼容 |
| Hospitality fork | `random-interaction replacement signature not found` | 候选列表排序修复未应用，互动选择可能受集合顺序/Rand 影响 |
| I Will Be Back / AcceptJoiner | `world-command target resolution failed; patch was not installed` | 加入者/世界命令 UI 回调未同步 |
| RJW P1 / UAP | `UAP genital cache signature not found` | 进程本地缓存保护未安装 |
| RJW Menstruation Resources | `hybrid selection signatures not found` | 稳定加权选择未安装 |
| Axolotl | `verb save fix skipped: signatures not resolved` | Verb 存档/重连风险仍在 |
| Quest/Ideology ChoiceLetter | `sync methods registered=0` | 信件选择入口没有建立同步证据 |
| Projectile launcher | 日志明确显示 determinism patch disabled | Ancot/Milira 投射物 Impact 保持原行为，原失步面未覆盖 |
| ITab Storage priority | callback 未找到 | 储存优先级本地 UI 修改可能未同步 |

这些不是“可能存在”的静态猜测，而是运行时启动摘要明确报告的未安装项。所有 required target 必须 fail closed 或将对应功能列为不支持，不能在报告中算作兼容完成。

### P0-5：发布目录受测试文件和版本漂移污染

`1.6/Assemblies` 当前包含：

- `MP_MeowOnlineShop.RjwP1Harness.dll`：测试 Harness 不应出现在发布程序集目录，即使它有命令行门控。
- `0Harmony.dll` 和 PDB：项目已依赖独立 Harmony 模组，不应在兼容包中静默携带另一份 Harmony；至少必须证明版本和加载顺序不会产生重复程序集问题。
- `MP_MeowOnlineShop.pdb` 的时间早于当前 DLL，未证明与 DLL 匹配。

版本也不一致：

| 位置 | 版本 |
| --- | --- |
| `About/About.xml` | `3.0.119` |
| `AssemblyInfo.cs` Assembly/File Version | `3.0.117.0` |
| InformationalVersion | `3.0.117-tps-fix-nullguard` |

发布前必须只保留正式程序集，统一三套版本，并对最终归档、部署副本和双端启动日志中的 SHA-256 做相等校验。

## 4. 高风险设计问题

### P1-1：`World.Tick` 整体 Rand 作用域过宽

启动器对整个 `Verse.World.Tick` 安装前缀/终结器，在每 tick 对 `World.rand` 做临时种子 Push/Pop。它能压住某些表面失步，但会把该 tick 内所有世界系统、所有第三方模组都纳入同一个人为随机边界：

- 可能隐藏真正的本地回调或未排序集合问题；
- 改变长期 world Rand 的正常推进语义；
- 新增/移除任意世界组件会改变同一作用域内的随机消费顺序；
- 与“补最窄真实执行器”的 MP 原则相反。

长期测试仍然出现随机状态失步，说明广域包裹没有建立可证明的确定性。应通过 trace 找到具体事件/执行器，再逐步缩窄或移除全 World.Tick 兜底。

### P1-2：启动器仍是一个大 try，前半段大量补丁没有故障隔离

`ApplyPatch()` 外层只有一个总 `try/catch`。虽然部分第三方补丁使用 `ApplyOptionalPatch`，但在其前后仍有大量直接 `Patch_X.Apply(...)`。任一直接补丁因签名漂移抛异常，会使其后的所有兼容项不再注册。

仓库注释已经记录过“一项无效 Ratkin transpiler 阻断后续补丁”的历史。应让每个独立模组/功能簇都有隔离、required/optional 分类和最终汇总；required 失败应明确阻止发布，而不是只写一条日志后继续游戏。

### P1-3：全量包级动作面审计尚未完成

178 个带 DLL 包没有官方或本项目精确 packageId 证据。包名未出现不代表一定不兼容，类型名反射也不代表已经完整兼容。要把某包从“待证明”提升到“已覆盖”，至少需要：

1. 安装程序集、版本、Workshop ID、SHA-256 与来源对应；
2. Command/Gizmo/FloatMenu/Dialog/Ability/Targeting/快捷键等入口清单；
3. 与 MP 核心及官方兼容包的重复同步排查；
4. 每个状态变更执行器、参数序列化、map/faction/owner 上下文和随机流说明；
5. 启动目标解析成功；
6. 非主机真实动作至少三轮、功能不变量双端一致；
7. 相关多地图/异步时间/重连矩阵与最终 120k soak。

当前仓库只有部分重点模组达到其中若干项，不能据此外推到剩余全部 DLL 模组。

### P1-4：多地图、异步时间、重连门槛没有对最终候选闭环

清单含 Odyssey Gravship、世界事件、远行队、多个种族/组件初始化和大型 RJW 生态。现有证据没有证明最终候选完成：

- 一地图 async off 控制；
- 两地图 async off；
- 两地图 async on；
- 三次冷重连；
- 每张地图上的真实动作和 Rand/world 状态基线相等。

Gravship 专项没有进入 stage 1，不能替代两地图/世界生命周期测试。

### P1-5：测试 ModsConfig 与当前输入不是同一文件哈希

当前 `list1.xml` 为 458 个唯一包；既有“464 项”证据配置实际上是同一 458 个唯一包顺序，但把 6 个 Ludeon 核心/DLC 各重复写了一次。唯一集合和顺序一致，因此可作为支持性证据；但文件 SHA-256 与当前输入不同，不满足正式 release gate 的精确输入证明。

### P1-6：本地存在重复 packageId 安装

`fxz.ratkin.facialanimation` 同时存在两个安装副本：

- `Mods/2852881266`：Hyacinth's Ratkin Facial Animation
- `Mods/3625762924`：old Hyacinth's Ratkin Facial Animation

主机/客户端若选中不同副本，可能出现程序集、XML、纹理或 Def hash 不一致。正式测试和发布载荷应只保留并哈希一个明确版本。

### P1-7：性能/Tick 改写模组组合仍是系统性风险

清单同时包含 `arkymn.slowerpawntickrate`、`arkymn.performanceesmolas`、`chokey.tpsoptimizer`、`yuki.fpsandtps`、`jaeger972.turretperformancetweaks`、`secsky2.gearstatoptimization` 等。

启动日志证明本项目会移除其中部分 Harmony patch，但没有证明所有版本、所有入口都被禁用，也没有覆盖 Yuki、炮塔优化、装备缓存等全部行为。建议在完整兼容闭环前从 MP 正式载荷移除这些 Tick/缓存改写项，逐个加入验证。

## 5. 已有正面证据及其边界

以下功能有真实双端定向 PASS，可保留为回归资产：

- Perspective Shift：双所有者移动、近战、远程、储存/容器阶段曾完成 20k PASS；但最终候选长 soak 仍失败，不能视为全局稳定。
- 交易/商人：三种 caravan trader 事件 20k PASS。
- RJW 主动开关回调：三轮双端 replay 20k PASS。
- Kiiro/CQF 兼容 UI、RigorMortis 多项能力/故事节点、Alert probe、Caravan UI 均有短专项 PASS。
- 旧候选 `9A576FF7...` 有一次 120k PASS。

这些证据证明“部分动作能工作”，不证明：所有 458 包、最终源码、最终部署 DLL、多地图/async/rejoin、所有随机事件均兼容。

## 6. 修复与重新放行顺序

1. **冻结输入和候选**：清理重复 packageId，使用当前 458 项 `list1.xml` 原文件；归档唯一 DLL/About/PDB/hash。
2. **修复编译/启动完整性**：将 Gravship 死代码纳入编译并显式注册；逐项消除 required target failures；拆分启动器故障域。
3. **清理发布目录**：移除 Harness 与多余 Harmony/PDB；统一 3.0.119 或新版本元数据。
4. **定位长期随机失步**：开启有界 trace，保存第一个有效 desync bundle，比较最早 trace、map/world Rand state 和命令上下文；禁止再用更宽的 tick 级 Rand 包裹掩盖。
5. **补重点未证明模组动作面**：优先 Ratkin/Milira/Axolotl/RJW 附属、世界事件、战斗控制、存储/生产和性能模组。
6. **依次运行 Gate A-E**：启动/加入 → 10k 定向 smoke → 最小诊断重放 → 120k normal soak → 发布哈希一致。
7. **追加矩阵**：一图/两图、async off/on、三次冷重连、Gravship 起飞落地、每图真实动作。
8. **最终报告**：只对通过目标解析、双端功能断言和最终哈希测试的包使用“已兼容”；其余明确标注 unsupported/experimental/runtime-unverified。

## 7. 带 DLL、缺少精确包级覆盖证据的 178 项

此表的含义是“尚未建立官方/本项目精确 packageId 证据”，不是自动判定每项都需要新补丁。框架、UI、视觉模组可能无需同步代码，但仍须完成动作面分类；已有类型级反射补丁的项目也仍需目标解析和运行时闭环。

| packageId | 名称 |
| --- | --- |
| zetrith.prepatcher | Prepatcher |
| brrainz.harmony | Harmony |
| vortex.kingfisher | Kingfisher |
| rwmt.multiplayer | Multiplayer [Continuous] |
| rwmt.multiplayercompatibility | Multiplayer Compatibility |
| issaczhuang.muzzleflash | Muzzle Flash |
| dev.soeur.imageopt | Image Opt |
| ancot.ancotlibrary | Ancot Library |
| m00nl1ght.mappreview | Map Preview |
| yancy.factiongearcustomizer | yc's Faction Editor |
| og.immersive.filter | [Og] Immersive Filter |
| visibleraidpoints.1trickpwnyta | Visible Raid Points |
| jaxe.rimhud | RimHUD |
| nals.customportraits | [NL] Custom Portraits |
| creeper.betterinfocard | BetterInfoCard |
| nals.dynamicportraits | [NL] Dynamic Portraits |
| zeracronius.dynamictradeinterface | Dynamic Trade Interface |
| ray1203.simplecamerasetting | SimpleCameraSetting |
| madeline.modmismatchformatter | Better ModMismatch Window |
| amch.needbaroverflow | Need Bar Overflow |
| leoltron.explicittimings | Explicit Timings |
| rtxyd.realfactionguest | Real Faction Guest (Forked) |
| vesper.notmyfault | Not My Fault |
| kathanon.nodisabledfactions | No Disabled Factions In Quests |
| erdelf.humanoidalienraces | Humanoid Alien Races |
| cedaro.pinyinquicksearch | 拼音搜索 Pinyin Quick Search |
| kearril.choosewheretoland | Choose Where To Land |
| mlie.foodpoisoningstackfix | Food Poisoning Stack Fix (Continued) |
| kittahkhan.grazeup | [Kit] Graze up |
| mlie.noversionwarning | No Version Warning |
| doug.nojobauthors | No Job Authors |
| telardo.dragselect | DragSelect |
| lingluo.movesteamgeyser | [Ling]Move Steam Geyser |
| redmattis.betterchildren | Better Children Skill Learning |
| nals.facialanimation | [NL] Facial Animation - WIP |
| dvs.norandomrelations | No Random Relations |
| velc.trapdisable | Trap Disable |
| ac.mw.sad | Melee Weapons: Speed and Damage |
| memegoddess.replacestuff | Replace Stuff - Continued |
| cedaro.lowerprisonerexpectation | Lower Prisoner Expectation |
| mlie.prisonersdonthavekeys | Prisoners Dont Have Keys |
| mlie.prisonersshouldfearturrets | Prisoners Should Fear Turrets |
| mlie.bestmix | Best Mix (Continued) |
| smoothedstoneisworthless.mod | Smoothed stone is worthless |
| rw.mod.pickupathome | Pick Up at Home (Fork) |
| brrainz.rangefinder | Range Finder |
| falconne.bwm | Better Workbench Management |
| automatic.autolinks | Auto links |
| kahdeg.killfeed | Killfeed |
| unlimitedhugs.defensivepositions | Defensive Positions |
| garwel.destroyitem | Destroy Item |
| gguake.apparelbodyresolver | Apparel BodyType Resolver |
| battiebear.battiepatch.ghoulmoodbarfix | BattIePatch: Ghoul Mood Bar Shows Their Hunger |
| roo.antyracemod | Anty the war ant race |
| ab.hatweaker | AB's Head Apparel Tweaker |
| zylle.childboodbackstories | Childhood Backstories |
| zylle.mapdesigner | Map Designer |
| marvinkosh.sometimesraidsgowrong | Sometimes Raids Go Wrong |
| vortex.customizeweapon | Customize Weapon |
| hailuan.customquestframework | Custom Quest Framework |
| ghxx.techadvancing | Tech Advancing |
| superniquito.modoptionssort | Mod Options Sort |
| ferny.nameyourentities | Name Your Entities |
| cn.youaresobeautiful | You Are So Beautiful |
| layyoune.citadelcable | 自修复电缆 - Self-repairing cables |
| cat.cqc | Close Quarters Control |
| leo.cleancommandbar | Clean Command Bar |
| bbb.ratkinweapon.morefailure | Ratkin Weapons+ |
| fxz.ratkinfaction | Ratkin Faction+ |
| oark.ratkinfaction.geneexpand | [OA]Ratkin Gene Expand |
| hentailoliteam.axolotl | MoeLotl Race |
| secretarynexus.secretarynexusracemod | Secretary Nexus a clone race |
| rooandgloomy.dragonianracemod | Gloomy Dragonian race |
| ancot.kiirorace | Kiiro Race |
| nsns.tinimar | Tinimar the Harfling |
| valeries.miliraexpansion | 瓦莱丽的米莉拉扩展包 |
| pakerwot.miliraeventandstortexpandthetaleofmilira | Milira Event Story Expand: The Tale of Milira |
| fuu.bloodanimations | Blood Animations |
| secretarynexus.secretarynexusfacialanimation | Secretary Nexus Facial Animation |
| hentailoliteam.axolotl.factionexpand | MoeLotl Faction Expand |
| fxz.moelotlzombie.update | MoeLotl: Rigor Mortis |
| kalospacer.ahndemi.panieltheautomata | Paniel the Automata[1.6] |
| issaczhuang.showmechanoidweapon | Show mechanoid weapons |
| murmur.tcu.kotobike | Temperature Control Unit |
| mingtuwuxiang.spacebasedecorative | [MUS]太空基地家具Space Base Furniture |
| hailuan.gravshipexpanded | 百里墨的逆重飞船拓展 |
| sbz.neatstorageworkbenchshelf | [sbz] Workbench Shelf |
| sbz.neatstoragefridge | [sbz] Fridge |
| mo.mhscanner | Metalhorror Scanner |
| meltup.advancedpowerplus | Advanced Power Plus |
| moistestwhale.gitscyberneticequipment | GiTS Cybernetic Equipment |
| lsk.rabbieweaponsandequipment | [LSK]月兔扩展——军用武器和装备 |
| rabiosus.halorailgun | ARC-920 Railgun |
| lingluo.giantcrop | Giant crop |
| rh2.helldivers.super.firearms | [RH2] Helldivers: Super Firearms |
| cp.rimmunation.2.clothing | [RH2] Rimmu-Nation² - Clothing |
| cp.rimmunation.2.security | [RH2] Rimmu-Nation² - Security |
| cp.rimmunation.2.weapons | [RH2] Rimmu-Nation² - Weapons |
| sunsetmoderteam.rottenfood | Rottenfood |
| ferny.resourcedeliveryhelper | Resource Delivery Helper |
| balistafreak.standalonehotspring | Standalone Hot Spring |
| vis.staticquality | Static Quality |
| araeotu.rimendel | 边缘孟德尔（Rimendel） |
| spray.transfer | Spray Transfer |
| trigger.eliteraid | Elite Raid 精英化袭击 |
| krafs.levelup | Level Up! |
| mlie.markthatpawn | Mark That Pawn |
| fxz.appareldura | Infinite apparel durability |
| vat.epoeforked | Expanded Prosthetics and Organ Engineering - Forked |
| lts.i | Integrated Implants |
| sambucher.adogsaidanimalprosthetics2 | A Dog Said... Animal Prosthetics 2 |
| ancot.kiirostoryeventsexpanded | Kiiro Story: Events Expanded |
| pupa.insectgirls | 虫娘 Insect Girls |
| psyche.kemomimihouse | kemomimihouse |
| laayoune.shootingwall | true Shooting-Wall 真实射击墙 |
| aoba.framework | Fortified Features Framework |
| breadmo.cinders | Cinders of the Embergarden |
| xmb.ancientthreat.mo | Ancient amorphous threat |
| aoba.deadmanswitch.ancientcorps | The Dead Man's Switch - AncientCorps |
| scar.basicfarming | Scar's Basic Farming |
| dismarzero.vgp.vgpgardenmedicine | VGP Garden Medicine |
| mlie.realistichumansounds | Realistic Human Sounds (Continued) |
| verniy709.realistichumansounds.harpatch | Realistic Humansounds HAR Patch |
| kuchiki.nobeardsonteenagers | No Beards On Teenagers |
| mlie.showmeyourhands | Show Me Your Hands |
| carnysenpai.traitraritycolors | Trait Rarity Colors |
| com.yayo.yayoani.continued | Yayo's Animation (Continued) |
| mlie.rangefinder | Range Finder (Continued) |
| co.uk.epicguru.whatsthatmod | What's That Mod |
| owlchemist.toggleablereadouts | Toggleable Readouts |
| superniquito.traiticons | Trait and Backstory Icons |
| owlchemist.simplefx.smoke2 | Simple FX: Smoke |
| owlchemist.scatteredflames | Scattered Flames |
| petetimessix.compacthediffs | Compact Hediffs |
| kathanon.showweapontallies | Show Weapon Tallies |
| com.bymarcin.architecticons | Architect Icons |
| xeonovadan.visiblepants | [XND] Visible Pants |
| c0ffee.rimworld.animations | Rimworld Animations 2.0 |
| rjw.sexperience | RJW Sexperience |
| otyoty.sizedapparel | Sized Apparel for RJW (SAR version) |
| eltoro.rjw.menstruation.fluids | RJW Menstruation - Fluids |
| nugerumon.romancetweaksmoreoptions | Romance Tweaks More Options |
| rjw.fb | RimJobWorld - FB |
| ryuf.rain.rjwaddons | Rains RJW Addons |
| lw.rjw.cumquer | [LW1.6]rjw-cumquer |
| euclidean.s16.core | S16's Extension |
| bep.brothel.signs | [B.E.P Mod]Brothel Signs |
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
| donald.vcr | Vanilla Combat Reloaded |
| momo.stayinbed | [MOMO]Stay in bed |
| mlie.allturretscansetforcedtarget | All Turrets Can Set Forced Target (Continued) |
| jaeger972.turretperformancetweaks | Turret Performance Tweaks |
| yuki.fpsandtps | FPS And TPS |
| ab.beheading | Cutting out Head (1.6) |
| secsky2.gearstatoptimization | Gear Stat Optimization |
| thumb.goremod2 | Visual Brutality |
| chokey.tpsoptimizer | TPS Optimizer |
| zh.nals.dynamicportraits | [NL] Dynamic Portraits_zh |
| observer.developmode.fix | DevelopMode fix |
| local.mp.meowonlineshop.sellslingshot | MP-Race-Compatibility-Plus |

## 8. 审计限制

- 本轮没有启动新的 RimWorld 双端进程；运行时结论来自已冻结在磁盘上的现有 host/client/manifest 证据，并按当前技能门槛重新分类。
- 不能仅凭包名或是否含 DLL 判断兼容；第 7 节是待证明清单，不是“必然不兼容”清单。
- 当前列表的 195 个无 DLL 未逐项反编译（其本身无程序集）；它们仍必须保证双方文件、Def、补丁顺序和配置相同，并纳入生成/存档/重连 smoke。
- 完整来源权威与每个目标程序集 SHA-256 尚未为 458 项全部归档；这是“全量兼容完成”声明仍缺失的一部分证据。
