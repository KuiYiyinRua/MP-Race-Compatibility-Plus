# 性能模组联机兼容与本项目优化（2026-09-11）

用户要求停止测试后的交付记录。代码已实现并编译，候选 DLL 仅在隔离目录运行；没有替换正式发布目录的 DLL，也没有发布工坊/GitHub。

## 已实现

新增独立程序集 `Meow.PerformanceCompatibility.dll`，源码位于 `Source/PerformanceCompatibility`，不需要重新编译现有核心程序集。

### Performance Esmolas（3676343434）

保留世界 Pawn 休眠判定优化、冥想固定间隔执行和植物显示优化。联机配置固定世界休眠/冥想/意识形态优化为开启，扩展属性缓存为关闭；植物设置沿用原始启动配置。固定配置在启用 Multiplayer 的整个进程中生效，包括开服前单机阶段，防止两端启动时安装不同补丁或改变不同 StatDef。设置界面说明当前固定配置；保存设置时暂时恢复原始偏好并在 finalizer 中恢复联机配置，避免覆盖用户原有设置。设置保存机制的专项实测未完成。

意识形态调度用存档中的 `Ideo.id` 替代 `object.GetHashCode()`；所有玩家阵营的意识形态保持正常更新。冥想的自建近似属性缓存在联机中绕过读写，避免界面查询污染、重连缓存差异和过期时钟问题；原模组的冥想降频仍保留。

新模块成功注册全部相关保护后，仅从旧核心的批量清理名单移除 Esmolas，保留其他性能模组的旧保护。第一轮启动发现当前核心 DLL 会卸载 Esmolas 的 8 个补丁，该轮不算兼容通过；修正后的第二轮保留了这些优化。

### Kingfisher 0.7.4

保留其他原有算法改写。加油候选每次从当前地图状态生成，保持输入顺序、返回独立结果；牺牲该项 250 tick 近似缓存，避免同 tick 的自动加油开关、加满燃料和不同查询时机留下过期候选。

联机中绕过尸变体的自定义搜索延迟及其静态警戒缓存入口，保留原版搜索；这些静态字典未存档，不把整体重写当成简单同步动作来处理。尸变体入口绕过已通过断言，实际尸变体战斗场景未专项验证。

建筑查询接入本项目的精确列表索引。其他改写虽保留且参与本次双端运行，但不能据此声称每个战斗/死亡思想分支都已专项覆盖。

### 本项目独立优化

对 `ListerBuildings.AllBuildingsColonistOfDef`、`ColonistsHaveBuilding(ThingDef)`、`ColonistsHaveBuildingWithPowerOn` 建立按建筑 Def 的索引。使用实际列表对象和 `List<T>._version` 判断失效，识别相同数量的替换、清空、重新排序及不同列表；保留原有结果缓冲区约定，供电状态实时查询。无需安装 Kingfisher/Esmolas 即可注册该功能，但不安装目标模组的独立双端负对照尚未执行。

缓存遵循游戏正常的注册建筑生命周期；绕过注册流程直接改写已注册建筑 `def` 字段的第三方代码不在本次覆盖范围。

### MissileGirl

上游 `About.xml` 明确列出 `rwmt.Multiplayer` 不兼容。其方法包括 XML 启动缓存、警报降频、自适应属性缓存、温度缓存、减少美观采样、扩大休眠范围。尸体清理还依据镜头可见性和 Stopwatch 时间预算，默认关闭；不应将这个可选风险说成默认必然触发的错误。

完整兼容涉及局部设置、缓存生命周期、查询上下文和可选玩法变更，超出简单补丁范围。本次没有宣称兼容 MissileGirl，也没有安装它进入正式模组列表。保留本项目已有的警报降频/部分移植作为已有功能，并新增上述列表索引优化。源码中的部分优化代码已经注释，属性缓存还依赖 Ticking 上下文；仅存在源码不能证明运行时实际获得相应收益。

## 已有验证证据

- Release 编译：0 警告、0 错误。
- 隔离宿主与客户端加载同一候选，游戏 1.6.4871 rev591、Multiplayer 0.11.5+a481546，12 个启用项，包含 PurePatcher、Kingfisher、Esmolas 和现有项目核心。
- 宿主出现 `Server started.`；两端进入 ClientPlaying、玩家数为 2。
- 暂停基线一致：map=0、mapTicks=2、map Rand=1307984758。
- 非宿主派发 4 轮动作，双方执行记录一致；加油开关/燃料即时更新、结果独立性、列表同数量替换/排序/跨列表隔离、固定设置与意识形态调度均通过断言。真实冥想任务累计入口计数一致：第 4 轮为 8462。
- 停止前最后一组双方检查点：至少 20,000 共享 tick，约 99,885 地图 tick；均为 `desynced=False`，未发现失步标记。不是 120,000 共享 tick 长测完成。
- 建筑查询微基准：4000 个建筑、10000 次末尾命中查询，普通线性扫描约 51–56 ms，索引约 0.7–0.8 ms。仅说明该局部路径，不等于整局 TPS 提升比例。
- 两端均出现 PurePatcher 环境下 `UnityEngine.InputLegacyModule` 反射依赖预加载异常，但之后仍完成启动、加入与动作验证。未完成无优化模组基线对照，不能确定该异常归因。
- 两端设置不同的专项夹具已构建并启动宿主，但用户停止时尚未形成通过结论；冷重连、异步多地图、完整长测均未完成。

证据：`BuildValidation/PerformanceMods_20260911/R2`。原始隔离目录：`C:/WINDOWS/TEMP/MeowPerformance0911r2`；专项未完成目录：`C:/WINDOWS/TEMP/MeowPerformance0911Matrix`。测试进程按用户要求关闭，测试状态为 `STOPPED_BY_USER`。

## 源码与二进制来源

- MissileGirl：https://github.com/ViralReaction/MissileGirl ，commit `3170983d90e726f47f105dd2f426af8c3ce60e29`；仓库 Cosmodrome.dll SHA-256 `F1366C5259D701BB2F6C6D8E66DF74894F444EE5DA6ABE9A211FAB6BF2B50ED5`。核对了随仓库 DLL 的属性缓存方法。
- Kingfisher：https://github.com/realloon/Kingfisher ，commit `33158852e1497114b233ec5f00245d72a234bfb4`；安装包 3711543433、版本 0.7.4，实际 DLL 已反编译核对目标签名/逻辑。SHA-256 `148D5DDA1C005ADA024E97CBB380B7AB3A8752458EF22C959995FDFB2EFF8776`。
- Esmolas：安装包 3676343434，附带 Ready/Test 源码，以运行目录实际 PerformanceEsmolas.dll 的反编译为准；assembly 1.0.0.0。SHA-256 `BA1363DD6F1B22C11F0487F1440B8DAA5A5F79BECC7891D3D89BF2B675602662`。
- 已测候选 SHA-256：`6C4FAA05A8893BDDB7EEBA9F972FB30ADD40EC4C86D77F352AD665525C1F2F2B`；MVID `ed8d5369-179c-49dd-a6af-d074f99827df`。

## 交付与回退

候选文件保存在 `BuildValidation/PerformanceMods_20260911/R2/Meow.PerformanceCompatibility.dll`。当前未复制到正式 `1.6/Assemblies`，也未更新 G 盘安装副本或工坊文件。正式启用后需要所有玩家使用同一版本，并重启游戏；回退方式是移除此独立模块并重启。完整验证通过前，仅将其视为已通过上述有限实测的候选版本。

Kingfisher 加油筛选条件的适配采用其 MIT 授权代码，授权文本见 `PerformanceMods-ThirdPartyNotices.txt`。建筑索引为独立实现，没有复制其 MPL 建筑索引实现或重新分发这些优化模组的 DLL。
