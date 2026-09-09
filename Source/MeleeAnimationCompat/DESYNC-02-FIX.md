# Desync-02 补丁加载修复

这份记录的首个轨迹差异在 tick 78333：客户端多执行了待机动画 `HandleStartingFlavourAnim` 的随机调用。客户端的已加载模组清单只有主补丁 `MP_MeowOnlineShop (3.0.124)`，没有 `MP_MeowOnlineShop.MeleeAnimation`；启动日志也没有近战模块注册信息。现有 `VisualRandom` 已隔离这一调用链，本次补齐模块交付，不重复修改模拟逻辑。

## 安装

两名玩家退出游戏，解压修复包。在 PowerShell 中运行（把路径换成实际的 **MP-Race-Compatibility-Plus** 模组根目录）：

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Install-MeleeAnimationCompat.ps1 -TargetModRoot 'D:\Game\RimWorld\Mods\MP-Race-Compatibility-Plus'
```

安装器补齐 `MeleeAnimation/Assemblies/MP_MeowOnlineShop.MeleeAnimation.dll`，合并条件加载项和加载顺序，保留其他模块配置及主 DLL，并在目标目录的 `MeleeCompatBackup` 留存被改文件的备份。不要指向游戏根目录或 Workshop 的近战动画原模组。仅更新主 DLL 不包含此修复。

双方重新启动游戏后，`Player.log` 应包含：

```text
[MeleeAnimationMP] 1.0.0 synchronization and deterministic simulation patches registered.
```

交付 DLL SHA-256：`2FC449C67ECC944AC37099806068474B29C2CA19EECA8DF643DA53F44201BD32`。

本记录未包含存档，无法据此重放原有全部种族和其他模组组合。快速双端测试的范围和结果见随包验证记录。
