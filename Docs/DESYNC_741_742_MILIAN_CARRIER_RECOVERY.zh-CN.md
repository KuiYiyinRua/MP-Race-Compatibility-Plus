# Desync-741 / Desync-742：米莉安回收浮游单元

## 结论

这不是回收资源计算本身的随机算法问题，而是回收按钮的同步覆盖不完整。

米莉安持政官使用 `Milira.CompMechCarrier_Consul`，该类型继承
`AncotLibrary.CompMechCarrier_Custom.CompGetGizmosExtra`。回收按钮的匿名回调会：

1. 按 `spawnedPawns` 顺序读取每个浮游单位的健康度；
2. 创建回收资源并写入载体容器；
3. 销毁已回收的浮游单位；
4. 清空 `spawnedPawns` 并按虚方法 `RecoverTicks` 启动冷却。

本地 Multiplayer 只登记了原版 `CompMechCarrier` 的 `TrySpawnPawns` 及其 Gizmo 回调，原版类型注册不会覆盖 Ancot 自定义载体的声明类型。仓库原有的 Milira 兼容代码也只登记了自定义载体的 `TrySpawnPawns`，即释放浮游单位路径，没有登记回收匿名回调。

两份 desync 的首个随机状态分叉都发生在后续的：

`AncotLibrary.WorkGiver_HaulResourcesToMechCarrier_Custom.JobOnThing`

这与回收按钮只在点击端推进载体/浮游单位状态、随后各端进入不同工作路径相吻合。

## 证据来源

- `Desync-741.zip`：tick `1045221`，客户端进入 `UniqueIDsManager.GetNextJobID`，主机进入殖民地漫游随机路径。
- `Desync-742.zip`：tick `1113438`，客户端同样进入 `UniqueIDsManager.GetNextJobID`，主机进入 `WanderUtility.GetColonyWanderRoot`。
- `H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\3256974620\1.6\Assemblies\Milira.dll`：确认 `CompMechCarrier_Consul` 只重写 `RecoverTicks`/`RecoverFactor`，没有重写回收按钮动作。
- `H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\2988801276\1.6\Assemblies\AncotLibrary.dll`：确认回收动作是 `CompGetGizmosExtra` 的第 3 个 lambda，即 `<CompGetGizmosExtra>b__31_3`。
- `G:\Steam\steamapps\common\RimWorld\Multiplayer-master\Source\Client\Syncing\Game\SyncMethods.cs`：确认原版只注册 `CompMechCarrier`，并将第 3 个回调标为 `Empty`。

## 修改

- `MiliraConsulMechCarrier_Compat` 新增对 `AncotLibrary.CompMechCarrier_Custom.CompGetGizmosExtra` 第 3 个 lambda 的同步登记，同时保留释放动作 `TrySpawnPawns` 登记。
- 这样回收动作由 Multiplayer 在同一同步命令中于各端执行；`Milira.CompMechCarrier_Consul` 的 `RecoverTicks` 和 `RecoverFactor` 仍通过虚调用在各端生效。
- 修正 `MilianModification_Compat.Postfix_InstallComponentAbilityCache` 的 Harmony 参数名：目标 `InstallComponent` 的第一个参数实际名为 `milian`，原补丁写成 `pawn` 会触发 `parameter "pawn" not found`，使该兼容类后续注册提前中断。
- 版本更新为 `3.0.123`。

## 候选产物与边界

- 候选目录：`BuildOutput\Desync741_742_MilianCarrierRecovery_20260902\`
- DLL SHA-256：`681D409AD7CCC4779AC06B8C6C84585AE8A70B28FE6896A6DBD888BA26B4B969`
- PDB SHA-256：`FB76063B9C4CB92A6BEF39D1E2825AB7490D48744200BE8578B77761C636A410`
- 候选程序集版本：`3.0.123.0`
- 本轮未复制到 `1.6\Assemblies`；核对时 live DLL 仍为 `3.0.122.0`，SHA-256 为 `7ADB87FC395D5F857466199CA64CA462C9F39926B6BDA4D1F92EFFABB7ED0EF8`。
- 已完成离线编译和反编译静态核对；按请求未启动游戏、未进行联机测试、未部署。运行时结果仍需后续部署后验证。
