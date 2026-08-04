# Milira Race 全武器多人兼容覆盖（3.0.115）

## 二进制来源

- 目标模组：Milira Race，`Ancot.MiliraRace`，Workshop `3256974620`
- `Milira.dll` SHA-256：`371891B8BBE05B68FFF7D6DCAED74A183D39ED6FD5D24C36A0BD9EAF4E4BD0E1`
- 依赖：Ancot Library，`Ancot.AncotLibrary`，Workshop `2988801276`
- `AncotLibrary.dll` SHA-256：`37C985CB2C884D79664A9FB53801477742E2C4566A30F49A81C17F5115997CA0`
- Perspective Shift DLL SHA-256：`85A9C0A9ACF1238DFFD0E032A0EF1A79345F574260D75CA53243D46F0C2A737E`
- Milira Race 与 Ancot Library 的安装包均未包含 C# 源码；类型和方法以已安装 1.6 DLL 的反编译结果为准。

## 覆盖边界

- 37 个可装备的 Milira/Milian 近战与远程武器 Def 与代码白名单完全一致（无缺项、无多项）。
- Perspective Shift 左键攻击在同步命令内按稳定 Thing ID 选择目标，并直接执行当前武器 Verb；不再让各端重新运行它面向本地 UI 的 `Avatar.HandleFiring`。
- 保留原有离子/等离子手枪及其他枪械的 Ancot 模式切换同步。
- 显式登记 `AncotLibrary.Verb_ShootSustained.OrderForceTarget(LocalTargetInfo)`。Multiplayer 原生只扫描 `Assembly-CSharp` 内的 `ITargetingSource` 实现，无法自动覆盖该模组重写。
- 充能、多弹丸、持续射击、Beam、RailGun、近战连击与格挡均在同步攻击或正常模拟 Tick/伤害链内执行；其状态由原模组的 `ExposeData` 保存。
- 无额外玩家写入入口的炮塔枪、战斗无人机内置枪和装备能力继续使用 Multiplayer 已有的炮塔/Ability 命令同步；其自定义 Verb 未重写新的玩家目标入口。

## 静态发布门槛

- Release 编译：0 warning，0 error。
- 程序集、文件和产品版本：`3.0.115.0` / `3.0.115.0` / `3.0.115-milira-all-weapons`。
- 已反编译候选 DLL，确认包含武器白名单、持续射击注册、Perspective Shift 专用执行器、`TryStartCastOn` 与 `TryMeleeAttack` 调用。
- 按用户要求未启动 RimWorld、未执行 host/client smoke 或 soak；本版运行时状态为 **unverified**。
