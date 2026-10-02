"""Archive finished runs and produce the reviewable Desync325 candidate; no deployment."""
import difflib
import hashlib
import json
import shutil
import zipfile
from pathlib import Path

repo = Path(__file__).resolve().parents[1]
evidence = repo / "BuildValidation/DesyncEvidence/Desync325-328_20260930"
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest().upper()
candidate = evidence / "Candidate03/MP_MeowOnlineShop.dll"
assert sha(candidate) == "C46B8AA57970B8E36F3CA54027519205B5B6688CFC8F1189B32CBA6A684951FC"
runs = {
    "ConfigR4": Path("G:/RWDesync325ConfigR4"),
    "ConfigOptOutR1": Path("G:/RWDesync325ConfigOptOutR1"),
    "NativeR2": Path("G:/RWDesync325NativeR2"),
    "RepresentativeR1": Path("G:/RWDesync325ReplayR1"),
}
for name, source in runs.items():
    dest = evidence / "Runtime" / name
    dest.mkdir(parents=True, exist_ok=True)
    for f in source.iterdir():
        if f.is_file() and (f.suffix in {".log", ".json", ".sha256", ".ready", ".complete", ".failed", ".cs", ".csproj", ".ps1"}):
            shutil.copy2(f, dest / f.name)
    for peer in ("Host", "Client", "User"):
        folder = source / peer
        if not folder.exists():
            continue
        for name2 in ("result.txt", "MPMeowHostConfigState.xml", "drone-roundtrip.xml"):
            if (folder / name2).is_file():
                (dest / peer).mkdir(exist_ok=True)
                shutil.copy2(folder / name2, dest / peer / name2)
        for sub in ("Config", "MPMeowHostConfigBackup"):
            if (folder / sub).is_dir():
                shutil.copytree(folder / sub, dest / peer / sub, dirs_exist_ok=True)

snapshot = evidence / "HarnessSnapshot"
for name in ("HostConfigJoin", "Desync325", "Desync325Replay"):
    dest = snapshot / name
    dest.mkdir(parents=True, exist_ok=True)
    for f in (repo / "Tests" / name).iterdir():
        if f.is_file() and f.suffix in {".cs", ".csproj", ".ps1"}:
            shutil.copy2(f, dest / f.name)
    for f in (repo / "Tests" / name / "bin/Release/net48").glob("*Probe.dll"):
        shutil.copy2(f, dest / f.name)

diff = []
for name in ("Patch_MpConfigHotSync.cs", "AssemblyInfo.cs", "MP_MeowOnlineShop.csproj"):
    before = evidence / "Before" / name
    after = repo / "Source/MP_MeowOnlineShop" / ("Properties/AssemblyInfo.cs" if name == "AssemblyInfo.cs" else name)
    diff.extend(difflib.unified_diff(before.read_text(encoding="utf-8-sig").splitlines(True),
                                   after.read_text(encoding="utf-8-sig").splitlines(True),
                                   "before/" + name, "after/" + name))
new = repo / "Source/MP_MeowOnlineShop/Patch_Desync325Boundaries.cs"
diff.extend(difflib.unified_diff([], new.read_text(encoding="utf-8-sig").splitlines(True),
                               "/dev/null", "after/" + new.name))
(evidence / "source-changes.diff").write_text("".join(diff), encoding="utf-8")

rows = json.loads((evidence / "batch-analysis.json").read_text(encoding="utf-8-sig"))
log = ["Desync325–328 首处分歧日志；首次采样分歧不等于已证明全部上游根因。", ""]
for row in rows:
    log += [row["bundle"], "last valid=" + row["info"]["Last Valid Tick - Local"]]
    for peer, trace in row["traces"].items():
        first = trace.get("first")
        if first:
            log += [f"{peer}: sample={first['index']} tick={first['tick']} hash={first['hash']}",
                    "context=" + first["label"]] + first["stack"]
    log.append("")
(evidence / "desync-log.txt").write_text("\n".join(log), encoding="utf-8")

report = """# Desync325–328 调查与候选补丁（2026-09-30）

已完成源码修补和配置行为调整，冻结候选主模块 3.0.143。专项双端通过 10,000 共享 tick；完整整合存档在加载阶段失败。尚不能宣称四次不同步全部解决，也没有覆盖正式 3.0.142 DLL。

## 四次日志的最早分歧

共同条件：2 玩家、3 地图、async time=false、multifaction=false；RimWorld 1.6.4850、MP 0.11.5+4a3be27-dirty。主机 debug、客户端开发者模式开启，后续测试关闭开发者模式。

| 包 | 最后有效 tick | 首次采样分歧 tick | 具体证据 |
|---|---:|---:|---|
| 325 | 3148141 | 3148192 | 主机 Milira_Race91594 执行 Raven 补货任务，SplitOff(1) 分配 ThingID，继而结束任务分配 JobID；客户端跳过这两项。后续 Cumpilation/RJW 正常随机调用不是最早原因。|
| 326 | 3149971 | 3150005 | 同一 Explosion2637735、相同输入 Rand=170111391860738，主机处理 Filth_FlammableBile2637499 的伤害舍入，客户端已进入另一目标的随机伤害分支。|
| 327 | 3152251 | 3152285 | 同一 Explosion2637886、相同输入 Rand=196057289295874，主机爆炸已 DeSpawn，客户端多处理一次 ExplosionImpact/伤害。|
| 328 | 3154921 | 3154976 | 同一 Milian_Mechanoid_PawnII2636198、相同输入 Rand=223536490055682；双方此前都执行 Promotion 添加 Knight hediff。主机 AI 经 Hediff.AllAbilitiesForReading 创建 AbilityID，客户端直接进入后续 RJW hediff 分配。|

325/326 本地日志当前检测 tick 分别为 3148204/3150034，均距最后有效 tick 63。327/328 本地日志为旧/截断窗口，不能把旧检测时间当成这次首次分歧。原 ZIP、哈希、完整滚动栈及 JIT 摘录均已保存。

## 已修补的确定性边界及证据强度

1. **已确认代码缺陷：Raven 搬运无人机调度状态未进入快照。** 原生 ExposeData 保存无人机及 nextID，遗漏 dispatchCounter 和两个搜索游标；PostLoadInit 又新建 taskFinder。客户端重建后调度相位与主机不同。新增三个 Scribe 字段，在原生 PostLoadInit 替换 finder 后恢复。旧存档缺字段按原生初值 0；新主机联机快照包含当前值。
   **325 的因果限度：** 轨迹中的直接路径是地面补货管理器，已序列化自身飞行列表。搬运调度缺陷可能改变上游资源/任务，但这段滚动窗口没有证明它独自导致 Milira_Race91594 的任务跳过。
2. **已确认处理顺序风险：原生爆炸遍历 ThingGrid 插入顺序。** 相同目标集合如果装载后顺序不同，会把同一随机流作用于不同目标。将 DamageWorker.ExplosionAffectCell 的目标读取得到的副本按 thingIDNumber 排序，保留网格原顺序，不增加随机隔离范围。专项测试在主机故意反转网格后，三轮实际爆炸的目标、伤害和 Rand 与客户端完全相等。
   **326/327 的因果限度：** 相同随机输入而不同伤害/结束分支明确证明模拟控制流先不同。采样没有完整目标集合、受损物件状态和历史，因此不能排除更早的目标成员/状态差异；排序仅修复顺序边界。
3. **已确认能力 ID 界面污染入口：** MP 保护 Pawn_AbilityTracker 的界面 getter，但第三方可以直接访问 Hediff.AllAbilitiesForReading，其原生懒初始化仍会分配全局 AbilityID。新增 MP.InInterface guard，界面仅返回现有列表或临时空列表，正常模拟保留初始化。双端断言界面访问没有初始化缓存，模拟访问产生同一 Ability_129。
   **328 的因果限度：** 轨迹直接证明能力缓存初始化先后不同；尚未捕获客户端此前是哪次界面/其他调用初始化缓存。

未采用全局 Rand.PushState、强制相同 ID、跳过模拟 tick 或修改 MP 时钟等掩盖差异的方案。

## 配置调整

- 删除本补丁的配置差异强制禁用“仍然连接”及替换连接回调行为；保留原生 MP 协议/Def 不兼容限制。真正需要启动时应用的配置，不再由本补丁强制要求修复并重启。
- 混合批次逐项热加载，已验证成功的热配置不再因另一项需要启动而整批撤销。标准 ModSettings 重读并执行 WriteSettings；实例及静态字段进入验证/回滚；炮台等支持运行期切换的开关立即生效。
- 主机不存在而客户端存在的配置尝试运行期恢复默认；成功后删除长期配置文件，让下次正常启动也维持主机默认状态。
- 启动绑定项或无法验证的热加载项：保留当前运行期值，把主机文件写入长期目录，标记待下次启动，不冒充热同步成功、不自动继续连接。主模块安装期补丁开关、XML Extensions 的 Def-time 设置仍属于此类。
- 原生“修复并重启”在父进程 SaveConfigs 后即持久保存；重启子进程迁移一次后，正常读写指向 SaveData/Config，而不是仅使用 MultiplayerTempConfigs。HugsLib 对应 SaveData/HugsLib/ModSettings.xml；首份原文件备份在 MPMeowHostConfigBackup，待启动状态在 MPMeowHostConfigState.xml。
- `-mpmeowhotcfg=false` 关闭热加载时，仍保留手动连接和长期保存。

热加载覆盖面取决于第三方实际加载机制。标准字段重读不能证明所有第三方启动期缓存/Def 均已重建；HugsLib 专门路径本轮未进行实际完整模组专项回归，不宣称全部配置都能热同步。

## 验证结果

| 测试 | 冻结候选 | 结果 / 范围 |
|---|---|---|
| 编译 | Candidate03 | 0 警告、0 错误 |
| G:/RWDesync325ConfigR4 | 3.0.143 | 原生 JoinDataWindow，14 组断言 PASS；混合热/启动项、父进程保存、模拟 restart child 路径、默认重置、静态字段失败回滚、首次备份及新设置实例读盘。不是实际冷重启进程矩阵。|
| G:/RWDesync325ConfigOptOutR1 | 同一 SHA | 禁用热同步，1 组原生窗口断言 PASS，连接回调可用。|
| G:/RWDesync325NativeR2 | 同一 SHA | 实际两端 ClientPlaying，真实配置不一致加入并热应用炮台；冻结世界/地图 Rand 基线相同；5 条爆炸/能力/无人机断言及 3 条炮台断言双端相等；双方完成共享 tick 10002，desynced=False。单地图、14 正式包加 probe，非整合长测。|
| G:/RWDesync325ReplayR1 | 同一 SHA | 用户确认 Player6554's game.zip，320 正式包加 probe，三地图。FAILED_SETUP：加载尚未启动联机服务器即失败；未完成 ClientPlaying 基线，不能计入 smoke/soak。|

NativeR2 冻结基线：worldTicks=2, worldRand=398780327；mapID=0, mapTicks=2, mapRand=3053216096。三轮爆炸 targets=36903/4/5、36907/8/9、36911/12/13；每轮 health=148,-1,-1；Rand 分别为 26168584103、51938387879、77708191655。新实例无人机状态=13:9:17。完整主客日志和哈希归档 Runtime/。

ReplayR1 首个当前输入异常：host.log:3980–3982，RK_InfernoGrenadeLauncher2159447/2162688/2328696 的 /codedPawn 重复空 load ID；3985 起 RatkinAnomaly.RAComponent.FinalizeInit → ResearchManager.Notify_MonolithLevelChanged → MP FactionRepeater.Template NullReferenceException；3995–3996 GrandThrone1247161/2425534 的 /sourcePrecept 同类异常。原始输入没有删节点、吞异常或替换成新殖民地来制造通过结果。此前还有 ShowHair API 版本及其他启动错误，但这次控制器停止于上述快照加载失败。输入是 03:31 回放，早于下午四个 desync，不等于四次触发时完整状态。

失败尝试保留在 G:/RWDesync325ConfigR1、R3 与 G:/RWDesync325NativeR1：配置夹具次序/首份备份预期以及同格物品互相 wipe 的夹具错误。修正仅测试夹具后从新目录重跑；这些失败不计为 PASS，也不声称是产品原因。

尚未通过用户三地图完整重放、三次冷重连、120,000 共享 tick 长测。按 rimworld-mp-compat runtime-testing.md 的发布门槛“Copy the exact archived candidate tested in Gate D to the release assembly folder.”，此候选尚未覆盖正式 DLL，也未上传 GitHub/工坊。所有测试进程已退出。

## 构建与复核

- 主 DLL：Candidate03/MP_MeowOnlineShop.dll，版本 3.0.143.0，informational=3.0.143-desync325-partial-hot-config。
- SHA256：C46B8AA57970B8E36F3CA54027519205B5B6688CFC8F1189B32CBA6A684951FC。
- MVID：b311926f-f1f7-433a-acd9-5f1b15ad5578。
- 当前正式主 DLL 仍为 3.0.142：6BB1946CD4F231A42CAFFED21D29968AC3B7B65C481CD19D73BA6BBFB71D4F51。
- 当前安装源权威：Raven 1.0.6/zuoyao.ravenrace，SHA74AE9E1D…；原生 Assembly-CSharp SHA8FD7E750…；MP SHA28D2CDA4…；Milira SHA371891B8…。完整 SHA 和路径见 provenance.json。Raven 当前反编译签名与轨迹类型匹配；并未证明每位远端机器 DLL 都等于当前安装哈希。
- source-changes.diff 保存配置、版本、工程、新边界的变更；启动注册在 Patch_SellSlingshot 加一行 Patch_Desync325Boundaries.Apply(harmony)，该文件冻结全文在 SourceSnapshot。测试源和精确 harness DLL 在 HarnessSnapshot；生产包不含 harness。
- 候选增量包：Releases/MP-Race-Compatibility-Plus-3.0.143-Desync325-328-Candidate.zip，基于已安装完整 3.0.142，仅主 DLL/PDB、候选 About、说明和哈希。它不是独立完整模组。
"""
(evidence / "REPORT.md").write_text(report, encoding="utf-8")
docs = repo / "Docs/Desync325-328-Analysis-20260930.md"
docs.write_text(report, encoding="utf-8")

package = evidence / "Package"
(package / "1.6/Assemblies").mkdir(parents=True, exist_ok=True)
(package / "About").mkdir(exist_ok=True)
for f in (evidence / "Candidate03").iterdir():
    shutil.copy2(f, package / "1.6/Assemblies" / f.name)
about = (repo / "About/About.xml").read_text(encoding="utf-8-sig").replace("<modVersion>3.0.142</modVersion>", "<modVersion>3.0.143</modVersion>")
about = about.replace("<description><![CDATA[", "<description><![CDATA[\n3.0.143 候选：Desync325–328 确定性边界、部分热同步和长期主机配置。专项双端通过，完整整合存档加载失败，长测/三次冷重连未通过。\n3.0.143 candidate: deterministic boundaries and persistent partial hot sync; targeted smoke passed, representative load/rejoin/soak incomplete.\n")
(package / "About/About.xml").write_text(about, encoding="utf-8")
(package / "README.md").write_text(report, encoding="utf-8")
manifest = {str(p.relative_to(package)).replace("\\", "/"): sha(p) for p in package.rglob("*") if p.is_file()}
(package / "SHA256.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
release = repo / "Releases/MP-Race-Compatibility-Plus-3.0.143-Desync325-328-Candidate.zip"
with zipfile.ZipFile(release, "w", zipfile.ZIP_DEFLATED) as z:
    for p in package.rglob("*"):
        if p.is_file():
            z.write(p, str(p.relative_to(package)))
with zipfile.ZipFile(release) as z:
    assert not any("Probe" in f or "Tests/" in f for f in z.namelist())
    for name, expected in manifest.items():
        assert hashlib.sha256(z.read(name)).hexdigest().upper() == expected
summary = {"candidateSha256": sha(candidate), "packageSha256": sha(release), "deployed": False,
           "representative": "FAILED_SETUP", "targetedSharedTicks": 10000, "configAssertions": 14,
           "optOutAssertions": 1, "soak": "NOT_RUN", "coldRejoinMatrix": "NOT_RUN"}
(evidence / "validation-summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
