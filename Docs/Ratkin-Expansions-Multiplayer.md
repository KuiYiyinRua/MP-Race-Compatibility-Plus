# Ratkin Anomaly+ / Ratkin Underground+ Multiplayer

适用 RimWorld 1.6；目标创意工坊 ID 为 `3293914637`（`fxz.ratkinanomaly.update`）和 `3613814532`（`rku.ratkinunderground`）。

## 实现

- `Source/RatkinAnomalyCompatibility/Anomaly.cs`：同步记忆怀表使用、童话书再次交互和黑十字石崩塌确认；避免本地取消窗口回退全局剧情；保存黑十字石交互状态；将浆果酒口味和暗魂位置随机数接入共享随机流，隔离纯画面随机调用。
- `Source/RatkinAnomalyCompatibility/UndergroundBasics.cs`：同步研究开关、钻机移动/出发/返回及炮塔控制、背包电台目标回调；货运窗口提交确定的物品数量，乘客窗口提交载具和乘客引用；钻机遭遇和洞穴生成的 System.Random 使用共享种子。
- `Source/RatkinUndergroundCompatibility/Radio.cs`：电台按钮发出同步命令；对话和冷却状态由共享游戏组件维护，窗口打字动画保持本地；使用 Multiplayer 的持久交易会话；货物交付通过保存的来信重新选点；保存扫描站点、已进入状态、货物和稳定电台 ID；运输投射物以 Deep 保存其未落地货舱。
- `RadioActions.cs`：按安装版本的原始按钮逻辑执行扫描、求救与支援。扫描标记保存于游戏状态，不再改写全局 Def。
- `RatkinUnderground/Defs/Letters.xml` 与 `LoadFolders.xml`：仅启用地下扩展时加载电台程序集和交付来信定义。异象补丁通过反射检测目标，无硬引用。

单机继续使用原版交互和随机行为；新增存档字段不改变原版单机流程。世界地图菜单和命令中的 LongEvent 继续由 Multiplayer 通用同步处理。

## 验证环境与边界

RimWorld 1.6.4871，Multiplayer 0.11.5+4a3be27-dirty，Harmony、Prepatcher、HAR、NewRatkinPlus、Ratkin Faction+、上述两个扩展及本补丁。使用独立存档目录和正常主机/客户端连接，关闭联机调试模式。

按本次要求，以一轮连续执行多个主要功能组的快速测试为主，不进行极端输入、边缘组合或长时间压力测试。测试自动调用原版 Gizmo/对话回调、电台按钮和真实任务执行器；不是仅注册补丁或直接改写最终状态。

最终功能测试 `combined-r4-20260910-010106` 同时加载两个扩展与官方 Multiplayer Compatibility，双端结果均为 `COMPLETE ticks=12300 mapTicks=59465 desynced=False`：

- 异象：非主机发起三轮怀表治疗、童话书确认/旧取消回调、宝箱交互任务；每轮实际获得 500 白银。两端三组各 24 次浆果酒口味序列完全一致。
- 电台：研究开关、扫描世界站点、交易信号等待、共享交易会话、实际购买钢铁、交付发射、求救及支援按钮。
- 钻机与装备：货运窗口卸载后保留 10 白银；主机从窗口弹出乘客；真实任务重新登车；非主机使用背包的炸弹和炮塔目标回调；主机执行携货/载人移动；非主机钻入世界地图后货物仍为 10 白银。
- 主机和非主机均承担主动操作。

重新加入补测 `join-r5-20260910-011203` 使用完全相同的两个候选 DLL，并保留存活电台及尚未交付的购买货物。双端完成 1,650 个共享 tick 后，新客户端重新加入：`radio=RKU_Radio1453 pending=1 stock=17`，电台引用、扫描站点、库存及待交付货物均恢复，随后由加入端发出交付操作并通过检查，结果 `REJOIN_COMPLETE desynced=False`。

R4 的初次重新加入检查中，测试电台已经从主机地图快照消失，保存引用为 `null`，因此未将该次“存活引用”断言计为通过。R5 只补测被阻断的存活电台/货物场景，没有重复战斗与钻机组，也没有修改生产 DLL。

此前不带官方兼容包的异象快速测试 `anomaly-r1-20260910-003219` 也完成三轮交互，双端未失步；它使用较早的仅异象候选，不替代最终候选的组合测试。

验证范围为上述主要交互及源码/安装 IL 审查。未跑完整异象剧情、所有地下任务结局或任意整合包的长时间测试。

最终候选程序集（版本均为 1.0.0）：

- `1.6/Assemblies/Meow.RatkinCompatibility.dll` SHA-256：`ED1E64B6B8EEDB07AAC88CE5851C15679824356FD473C1E343BE1C4AF96C1216`。
- `RatkinUnderground/Assemblies/Meow.RatkinUndergroundCompatibility.dll` SHA-256：`40209A279DDA03795FE0ABECD8FAB4B7A423C558FADB22EFA10BAAEA19023E25`。
- 核心 `MP_MeowOnlineShop.dll` 保持既有 3.0.125，SHA-256：`D3B6FB1EF7A6C544C38F0ECC3819D205C647526D993F70905961B531D3F18663`。

本地证据保留于 `BuildValidation/RatkinAnomaly_20260910/`：运行目录的主机/客户端日志、启动清单、候选归档和控制脚本。GitHub 不提交这些大型本地验证资料。

安装的原始程序集：

- RatkinAnomaly.dll SHA-256：`F874DD89FDE6718F6A7018B1C51E065C3A88535A517EBA2B3BC2C89C1F2841F9`；MVID `a94db646-9d45-4940-b555-1ccea7faf9ea`。
- RatkinUnderground.dll SHA-256：`140880C0DD5F234D1EEC59C5BB5DB1AD40DD6F2734B4237E3E5A53BB99D45C91`；MVID `41af564e-068d-4b70-904e-7a664c44cb34`。

运行时发布仅包含补丁 DLL、必要 XML 和既有模组资源；不包含验证程序、反编译文件、日志、构建缓存或第三方 DLL。
