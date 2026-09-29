# Desync-738：Milira 日光燃料装载量不同步

## 证据

- 原始证据：`Desync-738.zip`。
- 会话环境：RimWorld 1.6.4850 rev646、Multiplayer `0.11.5+a481546`，async time 开启，单地图双玩家。
- 首次实际随机状态差异出现在 tick `961131`：local hash `508371777`，host hash `508371778`。随后轨迹落在 `CompScanner.TickDoesFind`，这是状态已经漂移后的下游表现，不是燃料滑条的直接写入点。
- host 的 JIT 记录包含 `AncotLibrary.Gizmo_ApparelReloadable_Custom.GizmoOnGUI`，随后出现 Ancot 自定义装备 reload job 链；这与滑条改变装载目标后影响 `targetCharges`、再影响补充燃料行为的链路一致。

## 根因与补丁覆盖结论

`AncotLibrary.CompIntegrationWeaponSystem` 继承 `CompApparelReloadable_Custom`。自定义 `Gizmo_ApparelReloadable_Custom.GizmoOnGUI` 在拖动滑条时直接写入 `CompApparelReloadable_Custom.targetCharges`。该字段会被 reload/charge 逻辑读取，但原 Multiplayer 原生 UI 补丁只覆盖普通 `Gizmo_Slider` 和 `Gizmo_SetFuelLevel`，不会自动覆盖 Ancot 的自定义 Gizmo。

原有本项目补丁只同步 IWS 激活、点防御开关并隔离炮塔随机数，没有同步 `targetCharges`，所以这部分 UI 补丁是不完整的。

## 修复

`Source/MP_MeowOnlineShop/Patch_AncotIntegrationWeaponMp.cs` 现在：

1. 通过反射解析 Ancot 的自定义 reloadable 组件、Gizmo 和 `targetCharges` 字段。
2. 用 Multiplayer 原生 `SyncField` 注册该字段并启用 `SetBufferChanges()`，避免拖动滑条时发送每一帧中间值。
3. 在 `Gizmo_ApparelReloadable_Custom.GizmoOnGUI` 前后建立 `MP.WatchBegin/Watch/WatchEnd` 边界，并以基类类型显式 watch，覆盖 `CompIntegrationWeaponSystem` 子类（包括同一 Ancot 装载机制的 Milira 装备），不依赖具体装备 defName。

## 部署

- 编译候选：`BuildOutput/Desync738_TargetCharges_20260901/MP_MeowOnlineShop.dll`
- 正式部署：`1.6/Assemblies/MP_MeowOnlineShop.dll`
- DLL SHA-256：`EFCD86460D3AA47E54610117F6AD3453D77D79AC959DABEE2FD144403E1F3E38`
- PDB SHA-256：`89895F5D0A85EF73DD427100185A8E51721BDDB137AD0287E776CCCA73142433`
- 程序集版本：`3.0.122.0`；informational version：`3.0.122-milira-target-charges-sync`

按请求，本次只完成编译、静态审查、归档和部署，未启动游戏，未进行运行时测试；因此运行时结果仍待后续双端验证。
