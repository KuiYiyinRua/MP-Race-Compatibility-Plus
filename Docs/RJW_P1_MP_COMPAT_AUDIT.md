# RJW P1 联机兼容审计与补丁说明

> 日期：2026-08-06；范围：`RJW_模组联机兼容初筛与实施计划.md` 中 P1 表列出的包。
> 方法：优先读取模组自带源码，其次对已安装程序集反编译（`ilspycmd`，去重后逐个审计）；与本地 Multiplayer 源码对照已有同步覆盖；只为未被自动捕获且会改变模拟状态的入口写补丁。静态结论不等于运行时通过，运行门槛见测试记录。

## 需要专项补丁的包

以下包存在本地 UI 回调、非确定性随机或无序集合选择，补丁集中在 `Source/MP_MeowOnlineShop/Patch_RjwP1.cs`。

| 包 ID | 目录 | 风险路径 | 同步边界 |
| --- | --- | --- | --- |
| `ESeeker.FO` | 家庭关系扩展 | RMB 浮菜单直接改写 BioMom/BioDad、AI 母亲关系、仆从状态信件；选择信件为本地生成 | 同步 `SetBioMom/SetBioDad`、AI 母亲关系、仆从状态信件重建、Bastard/BioLock 开关；信件由同步命令在两端重建后由 Multiplayer 内置 DiaOption 同步选择 |
| `readyforjeff.RJWPeculiarInstitution` | 小妾项圈 | `Dialog_AssignPawnOwner` 回调直接增删妾室关系；自主目标与建筑选择使用字典/联合集合 | 同步 `ForceAddLiegeRelationFor/TryRemoveLiegeRelationBetween`；对 `TryRandomElementByWeight` 与 `RandomElement` 先按 Thing/Pawn ID 稳定排序 |
| `ElToro.HumpshroomDryad` | rjw-elt-hdryad | `DryadHelper.find_dryad` 对 `Dictionary<Pawn,float>` 做 `RandomElement` | 先按 Pawn ID 排序再随机 |
| `raddusx.demons` | RaddusX 魔鬼 | 吸血之吻能力使用参数 `System.Random`（时间种子） | 替换为 `new Random(Rand.Int)`，随机仍在同步能力命令内执行 |
| `TeheeItsMe525.RJWGenderOrgansMod` | 睾丸和卵巢 | `ITab_Pawn_Hormones.FillTab` 每帧本地写激素 Hediff | 多人时跳过 UI 写入；世界 Tick 仍按同一确定性结果在两端应用 |
| `moth.rjw.cnc` | 边缘巴士 | `CompFreeUse.IsDesignated` 由列表复选框/Widget 本地写；Getter 有自动清位副作用 | 同步 setter；多人下 Getter 返回确定性结果且不产生本地副作用 |
| `Teacher.UAP` | UAP 动画 | `GenitalCache` 进程本地缓存 | 多人时直接调用 RJW `Genital_Helper.has_penis`，不读写缓存 |
| `ElToro.rjw.menstruation.resources` | 月经周期资源 | 杂交资源/子代选择在字典上做加权随机 | 稳定排序后再加权随机（`ResourceHelpers`、`Patch_HybridExtension_ChooseOne`、`HybridXenotypeHelper`） |
| `rjw.unleashed.framework` | 性解放框架 | `ApplyEffect.FullExtension` 使用参数 `System.Random`（美食/基因/特质触发） | 替换为确定性 `Random(Rand.Int)` |
| `telanda.rjw.animalgeneinheritance` | 兽交动物遗传 | `AddPregnancyHediffPrefix` 使用参数 `System.Random` | 替换为确定性 `Random(Rand.Int)` |
| `abscon.privacy.please` | 隐私意识 | `JobDriver_Sex.setup_ticks` 后置用 `UnityEngine.Random.value` 决定加入群交 | 多人时改用 Verse.Rand 确定性取值 |
| `rimworld.ekss.rjwex` | SM 道具+炮机 | 自带 `[SyncMethod]` 但未调用 `MP.RegisterAll()`，公共/私密 Gizmo 未被捕获 | 注册 `PublicPrivateComp.ChangeMode()`；炮机浮菜单的 `TryTakeOrderedJob` 已由 Multiplayer 内置同步 |

## 审计为“兼容，无新增代码”的包

以下包的风险入口要么由 Multiplayer/RJW 既有同步覆盖，要么为确定性 Tick/Def/视觉路径，无本地 UI 模拟变更。

| 包 ID | 结论依据 |
| --- | --- |
| `rjw.FB`、`rjw.FC`、`rjw.FH` | 纯 Harmony 后置在同步性行为后加想法/Hediff，无随机与 UI 入口 |
| `nalzurin.metalhorrorspreadsthroughsex` | `SexUtility.Aftersex` 后置调用 `MetalhorrorUtility.Infect`，随同步性行为运行 |
| `ROBTRG.RJWGirlCum` | 静态构造只改写 Def 数据 |
| `Vegapnk.MultipleOrgasm` | `JobDriver_Sex.SetupOrgasmTicks` 后置，确定性调整 |
| `ElToro.Stretching` | 仅 ModSettings 按钮与 Hediff 序列化，无随机/UI 模拟变更 |
| `omgwtfbbq.genderSupremacyXtreme` | 纯 ThoughtWorker，只读状态 |
| `sluggandnil.mmcr` | 工作限制、叛乱、压制 Tick 补丁均为确定性函数 |
| `c0ffee.rjw.IdeologyAddons` | 仅改标签、过滤与 HistoryEvent 记录，随同步性行为运行 |
| `ElToro.MAddon` | 性/妊娠/能力补丁随同步能力与 RJW 性行为运行，`Rand` 均在同步命令内 |
| `Ryuf.Rain.RJWAddons` | 全部为 `CompAbilityEffect`/`AbilityExtension_AbilityMod`，随同步能力运行 |
| `bep.rjw.brothelcolony.quest` | QuestNode 与 WhoringHelper 补丁随同步任务/性行为运行 |
| `lw.rjw.cumquer` | 手术配方、JobDriver、能力与 Tick 均确定性/同步 |
| `bep.rjwaddon.nurseryfly` | Tick 内 `RandomElement` 输入为确定性列表；IncidentWorker 随同步事件流 |
| `Euclidean.s16.core` | Hediff/Apparel 组件 Tick 确定性；随机特质列表来自 Def 列表 |
| `archersaiter.rjw.traits` | `NeedCheck.SetCurLevel` 只写临时对象，无模拟影响 |
| `archersaiter.rjw.unleashed.crests` | 手术配方与 Hediff Tick 确定性 |
| `alpenglow.canines.animations`、`ElToro.Anims`、`Teacher.UAP` 动画数据 | 动画/缓存/视觉路径，不改变模拟状态 |
| `bep.brothel.signs` | Gizmo 只写本地显示字段（`getPawn`/`recordDef`），未序列化、不进模拟 |
| `RJW.Sexpanded.Core`、`RJW.Sexpanded.Corruption` | 全部为 HediffComp/进食 OutcomeDoer，确定性 |
| `abscon.privacy.please` 其余路径 | 隐私检查与旁观 JobDriver 随同步性行为运行 |
| `rimworld.ekss.rjwex` 其余路径 | `JobDriver_UseFM`/`JobGiver_UseFM` 的 Verse.Rand 在同步作业内执行 |

## 无程序集或纯内容包

以下 P1 目录没有可审计的模拟代码（无 DLL 或仅 Def/纹理），按“保持文件一致”处理：`nugerumon.IWantIncest`、`muri.carniculus`、`Sullos.RimHolstaur`、`rjw.dirtytalk`、`RJW.Sexpanded.ReProduction`、`ROBTRG.MilkDrugs`、`Ed86.RJW.Whorebeds`。

## 补丁验收状态

- `dotnet build -c Release` 通过，无错误（仅既有 `Patch_PerspectiveShiftMp` 未使用字段警告）。
- 启动时每个补丁段都有目标解析日志；缺失目标只禁用对应段并告警，不注册错误重载。
- 运行时主机/客户端验证进行中；测试载荷排除 `calamabanana.rjw.brothelcolony`
  （`RimJobWorldBrothelColony.dll` 按 RJW 1.6.9375 编译，引用
  `InteractionPreferences`/`JobDriver_SexBaseInitiator` 等已装 RJW
  1.6.9576.22392 中不存在的类型，属载荷版本不匹配，按计划保持禁用）。
- 测试载荷同时排除本地与创意工坊目录均未安装前置类型的三个包：
  `Euclidean.FantasyRaces`（`XenotypePatchUtils.PatchMePlease`）、
  `Sullos.RimHolstaur`（同一缺失类型）、`RJW.Sexpanded.Corruption`
  （`FermentationComp.CompProperties_Fermentable`）。这些属 Def 依赖缺失，
  不是本兼容补丁可修复的运行时同步问题。
- 测试载荷再排除 `OskarPotocki.VanillaFactionsExpanded.Core`（包内 1.6 副本
  的 VEF Defs 未加载，快速世界派系/起始小人生成在
  `VEF.AestheticScaling.CachedPawnData` 空引用）及其唯一依赖者
  `raddusx.demons`（P1 补丁已写，待该前置修复后补测）。
- 测试载荷再排除 `Ed86.rjwinsects`（`RJW_原版分离_昆虫器官和卵`）：其 Def
  引用不存在的父节点 `RJW_ImplantEgg`/`RJW_RemoveInsectEgg`，加载期抛异常并
  触发游戏将 ModsConfig 重置为仅核心，属于载荷 Def 完整性错误。
