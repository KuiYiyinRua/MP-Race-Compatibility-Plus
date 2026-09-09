# 渡鸦族 Multiplayer 兼容模块

目标：工坊 3781005562，`ZuoYao.RavenRace`，RimWorld 1.6。

该模块独立于合集主 DLL 编译，通过 LoadFolders.xml 在启用渡鸦族时加载。src 来自对应候选版本的实际兼容源码，不包含运行测试驱动、未接入的仪式/联络任务/代孕草稿。

编译：`dotnet build RavenCompatibility.csproj -c Release`。可通过 GameRoot、MultiplayerRoot、HarmonyRoot 指定本地依赖。输出到 BuildValidation，编译不会覆盖已部署文件。

验收按用户要求采用基础批量联机测试：常见操作能执行、两端结果一致、未观察到不同步。不宣称穷尽全部组合、第三方大型整包、长期运行或所有成人功能。成人互动相关兼容入口限制为成年角色，未审计或未接入的相关功能不纳入支持声明。

源码与测试证据：BuildValidation/RavenRace_20260907/STATUS.md。最终安装文件及哈希以该目录 release.json 为准；重新编译得到的 DLL 未自动继承已归档二进制的验证结果。
