# Perspective Shift 主角认定回归

`Probe` 是独立的本地测试模组，只在 `-perspectiveownershipprobe` 启动参数存在时运行。不要将它放进正式 `1.6/Assemblies` 或分发给玩家。

控制器会建立独立游戏布局和保存目录，复制指定候选 DLL，使用指定端口启动真正的图形版房主/客户端，并只清理自己记录的进程。每次使用不存在的新 `RunRoot`。

```powershell
dotnet build Tests\PerspectiveOwnership\Probe.csproj -c Release

# 先使用已归档的兼容层 DLL 做短测。
& Tests\PerspectiveOwnership\Run.ps1 `
  -RunRoot 'H:\test-output\perspective-smoke-unique' `
  -Core 'BuildValidation\PerspectiveOwnership_20261005\ReleaseCandidate4\MP_MeowOnlineShop.dll' `
  -TwoMaps -Cycles 0 -SoakTicks 10000 -Traces

# 短测通过后关闭高流量轨迹，三次冷重连，每次重新认定，最后长测。
& Tests\PerspectiveOwnership\Run.ps1 `
  -RunRoot 'H:\test-output\perspective-soak-unique' `
  -Core 'BuildValidation\PerspectiveOwnership_20261005\ReleaseCandidate4\MP_MeowOnlineShop.dll' `
  -TwoMaps -Cycles 3 -SoakTicks 120000
```

异步时间阶段增加 `-AsyncTime`，并另用一组新进程。并行的独立运行必须指定不同 `-Port`。

`-Save` 与 `-Replay` 输入必须是本夹具产生的 `.rws` 或 `claims-cycleN.zip`，里面保存了测试用角色引用；它们不是任意用户存档的通用验证器。前者覆盖单机读档后主持，后者覆盖联机保存重新主持。

每轮验证原生主角按钮与释放路径、征召、独占控制、递增序号和过期输入拒绝。双地图另外验证 Lord 只恢复一次、客户端跨图查看与停止移动的同步回执。保存加入点前冻结并比较双方 world/地图 tick、随机状态及认定表。旧客户端完整退出，再以同名新连接加入；新连接必须先释放自己的旧认定，并保留房主认定。

`result.json` 的 `PASS` 还要求输入 DLL/配置哈希没有改变，所有主客动作回执匹配。`SoakTicks` 使用共享 world 模拟 tick，不是渲染帧数或 MP 网络计时器。出现失步、同步方法异常、地图不匹配、断线或断言失败时不能把该运行计作通过。

候选或测试 DLL 发生改变后必须使用新运行目录与新进程。运行目录保留本次实际用到的测试源码、测试 DLL、进程命令行、配置、日志和哈希，可独立核对证据。
