# MP 配置热同步方案（3.0.117）

## 目标

`Patch_MpConfigHotSync` 在 `JoinDataWindow.PostOpen` 后处理“仅配置不匹配”的加入场景。3.0.117 不再声称“所有配置都可安全热重载”，而是把每一项配置先分类，只有能证明“内存与磁盘都变为主机值”的项才热应用；其余项走原生 `FixAndRestart` 流程，并阻止自动连接。

## 分类规则

### 可热应用（hot）

- 普通 `ModSettings`：
  - `modId` 等于某个运行中 Mod 的 `PackageIdPlayerFacing`；
  - `fileName` 等于该 Mod 运行实例类型名，且实例存在 `modSettings`；
  - `WriteSettings` 回调存在；
  - 先把主机内容写入隔离 staging 文件，用 `Scribe.loader` 直接加载进现有 `ModSettings` 对象；
  - 调用 `WriteSettings`；
  - 字段快照前后至少有一个具体字段发生变化；
  - 最后把主机规范字节原子写回正式路径。
- HugsLib `ModSettings.xml`：
  - 快照每个 handle 的 `StringValue` / `HasUnsavedChanges`；
  - 写入远端 handle 值，缺失项调用 `ResetToDefault`；
  - 调用 `SettingsManager.SaveChanges()`；
  - 任何 handle 失败即回滚并重存旧值。
- 字节不同但 XML 语义相同的配置：直接规范化字节，不重载运行时。

### 不可热应用（restart / rejected）

- XML Extensions（`imranfish.xmlextensions`）等启动期绑定配置；
- 无匹配运行实例、无 `modSettings`、无 `WriteSettings`；
- Scribe 加载或回调抛异常、字段验证无变化；
- HugsLib 保存/验证失败；
- 客户端独有、主机不存在的配置项；
- 恶意或异常输入：路径穿越、非法文件名、超长文件名/内容、未知 Mod ID。

## 安全与回滚

- 路径解析只接受 `GenFilePaths.SaveDataFolderPath` 下的文件，普通配置必须由
  `LoadedModManager.GetSettingsFilename(mod.FolderName, typeName)` 生成。
- 每个变更项先快照原始文件字节与运行时字段/handle；任何一项不是 `hot` 或
  `unchanged` 时，本批全部写盘与运行时变更整体回滚。
- 自动调用 `connectAnyway` 仅发生在：全部远端配置都是 `hot`/`unchanged`，
  且没有客户端独有配置项时。
- 命令行 `-mpmeowhotcfg=false` 可完全禁用本补丁，回到 Multiplayer 原生
  临时配置 + 重启流程。

## 日志

每次加入窗口处理输出逐项审计：

`MP config hot sync item: <modId>/<fileName>: outcome=HotApplied|Unchanged|RestartRequired|Failed|Rejected, reason=<...>`

汇总行包含 `hot/unchanged/restartRequired/failed/rejected` 计数与变更列表。

## 验证矩阵

1. 普通 `ModSettings` 差异：host/client 仅设置值不同，客户端应热应用并自动继续。
2. 字节不同但语义相同的 XML：客户端应规范化字节并自动继续。
3. XML Extensions 差异：只审计为 restart，客户端不自动连接。
4. 恶意 `fileName`（`../`、绝对路径、超长、未知 Mod）：拒绝，不写文件。
5. HugsLib 差异：handle 保存链验证成功才热应用，失败回滚并保留窗口。
6. 客户端独有配置：不删除、不重置，阻止自动连接。
