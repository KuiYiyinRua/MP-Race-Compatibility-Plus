# 联机交易窗口自动弹出开关

位置：模组设置 → `[MP] Meow Online Shop Compat` → **交易界面和穿梭机世界跳转仅向发起玩家显示（联机）**。

默认开启，各玩家独立设置，切换立即生效，独立于 TPS 优化预设。需要启用本补丁的「基础稳定性与存档修复」类别；类别开关仍需重启游戏。

- 据点交易：其他玩家创建交易时，本机不自动打开交易窗口，也不自动切换已打开窗口的交易页签。
- 途中交易：记录当前联机会话内下达远行队交易路线的玩家；抵达时只向发起者跳转镜头和自动打开窗口。
- 穿梭机交易：从世界远行队起飞时，其他玩家不跟随切到世界界面；从地图或远行队发射的穿梭机抵达据点交易时，仅发起者跳转并选中远行队、自动打开交易窗口。飞行归属按稳定的穿梭机 ID 保存在本机当前联机会话中，延迟抵达时不会使用当时碰巧正在执行的其他指令来猜发起者。
- 远行队遭遇：选择交易后，仅发起选择的玩家自动打开交易窗口。
- 主动查看：保留 Multiplayer 原有交易按钮、共享交易页签和会话入口。开关不会改变交易权限，其他派系的只读限制仍由 Multiplayer 控制。
- 关闭开关：本机恢复 Multiplayer 原有自动弹窗行为。单人游戏不受影响。

断线重连或重新开房后，之前途中指令的本机发起记录不再可用；这时不会猜测归属或向所有玩家弹窗，仍可主动打开已经创建的共享交易。当前会话内的存档重载使用稳定的远行队及据点 ID 保留本机记录，不把界面偏好写入同步游戏状态。

实现调整 `DialogTradeCtorPatch.Prefix`、`NodeTreeDialogSync.SyncDialogOptionByIndex` 的自动窗口调用，以及 `CaravanArrivalAction_Trade.Arrived`、`CaravanShuttleUtility.LaunchShuttle`、`TransportersArrivalAction_Trade.Arrived` 的镜头调用。`CompLaunchable.TryLaunch` 和 `CaravanShuttleUtility.LaunchShuttle` 只记录本机发起者归属，`TravellingTransporters.Arrived` 临时建立并在 finalizer 中恢复本机抵达上下文。`MpTradeSession.TryCreate`、商品生成、购买确认、库存转移、会话保存和手动 `OpenWindow` 不以发起者身份为条件。地图发射时原版 `TryHideWorld` 已由 Multiplayer 按指令发起者处理；手动选择目的地仍正常打开世界界面。

3.0.138 已通过原版及五个 DLC 的隔离双端十轮功能断言和 10,000 Multiplayer 共享 tick 烟测。未验证完整整合存档、冷重连及标准长测。证据保存在 `BuildValidation/TradeWindowAutoOpen_20260926`，最终核验为 `R7/FINAL-VERIFICATION.json`，部署记录为 `deployment.json`。

2026-10-01 穿梭机镜头热修已本地部署，程序集版本沿用 3.0.143.0，哈希以 `BuildValidation/ShuttleView_20260930/deployment.json` 为准。原版及五个 DLC 隔离双端通过 16 轮交易/手动查看断言，包含三轮远行队发射及三轮地图发射后的据点交易抵达，动作完成后继续通过 12,000 共享 tick 专项烟测。完整整合存档、冷重连及 120,000 共享 tick 长测未完成，详细界限见该目录的 `REPORT.md`。
