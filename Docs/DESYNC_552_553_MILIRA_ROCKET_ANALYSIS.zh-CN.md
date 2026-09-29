# Desync-552 / Desync-553 Milira 联装火箭炮塔分析

## 证据冻结

- `Desync-552.zip` 冻结目录：`TestValidation/DesyncEvidence/2026-08-21-552-milira-rocket-turret/`
- `Desync-552.zip` SHA-256：`383B8299F0EE6C246DFCADACB4813F6212EACE0EDBA8A649239AF8EFDC10CAE7`
- `Desync-553.zip` 冻结目录：`TestValidation/DesyncEvidence/2026-08-21-552-553-milira-rocket-turret/`
- `Desync-553.zip` SHA-256：`CE6BFC879EC8F0774AB78F7A13999CAE46A196913DD8F7539F2329FA66E44B5C`

两份 bundle 都是 Multiplayer `0.11.5+a481546`、RimWorld `1.6.4850 rev646`、2 玩家、2 地图、async time 开启、multifaction 开启。`552` 的最后有效 tick 是 `9797521`，检测 tick 是 `9797583`；`553` 的最后有效 tick 是 `9801421`，检测 tick 是 `9801483`，两次都相差 62 tick。

## 首次分叉

### Desync-552

在同一 tick `9797557`、同一 trace 槽位 `149`：

- local：`MiliraBullet_HeavyParticle3461067`，调用链进入 `Projectile_Explosive.Explode -> Thing.Destroy -> Thing.DeSpawn`。
- host：`MiliraTurret_HeavyRocketLauncher2427173`，调用链进入 `Verb_LaunchProjectile.TryCastShot` 的实际开火路径。

这不是单纯的爆炸视觉差异，而是主机正在生成下一枚火箭时，本地正在处理另一枚粒子弹的飞行结束/销毁。

### Desync-553

在同一 tick `9801476`、同一 trace 槽位 `624`：

- local 首先进入 `UniqueIDsManager.GetNextThingID -> ThingMaker.MakeThing -> GenSpawn.Spawn -> Verb_LaunchProjectile.TryCastShot -> Verb_Shoot.TryCastShot -> Verb.TryCastNextBurstShot -> Building_SpinTurretGun.Tick`，当前炮塔为 `MiliraTurret_HeavyRocketLauncher2427173`。
- host 同一位置仍在执行 `Building_HoldingPlatform.Tick` 的 `Rand.MTBEventOccurs`。

因此 `553` 直接证明差异已经发生在联装火箭炮塔的 burst 开火/弹体生成调度边界，而不是等火箭爆炸后才出现。

## 源码和定义关联

已核对本地反编译源码和统一 Def 缓存：

- 炮塔类是 `AncotLibrary.Building_SpinTurretGun`。
- `MiliraTurretGun_HeavyRocketLauncher` 使用 `Verb_Shoot`，默认弹体是 `MiliraProjectile_HeavyRocket`。
- 火箭炮是 8 连发、每发间隔 10 tick、forced miss radius 为 9。
- 炮塔 `Tick` 内先运行基类 Tick，再执行 `GunCompEq.verbTracker.VerbsTick()`；当前兼容补丁只隔离了 `TryStartShootSomething` 的目标/预热随机，没有覆盖实际 `TryCastShot` 和弹体飞行 Tick。
- `Milira.Projectile_ExplosiveWithSmokeTrail` 在飞行时继续调用基类 projectile Tick 和 smoke trail；`Milira.MiliraFleckMaker` 自有 helper 也会消费 Verse Rand。原补丁未覆盖这两个 Milira helper。

## 本次代码调整

新增 [`Patch_MiliraRocketDeterminism.cs`](../Source/MP_MeowOnlineShop/Patch_MiliraRocketDeterminism.cs)，并挂入统一启动流程：

1. 只对 `MiliraTurret_HeavyRocketLauncher`、`MiliraTurret_HeavyParticle` 的 `Building_SpinTurretGun.Tick` 建立确定性 Rand scope，覆盖真实 burst 开火路径。
2. 只对 `MiliraProjectile_HeavyRocket`、`MiliraBullet_HeavyParticle` 的 projectile Tick 建立确定性 Rand scope，覆盖飞行、碰撞和爆炸前的随机消耗。
3. 隔离 `MiliraFleckMaker.ThrowPlasmaAirPuffUp` 和 `ThrowLineEMP` 的视觉随机；不改变弹体、目标、伤害和对象同步语义。
4. 使用独立 scope 状态保存 map Rand，避免与既有 Ancot turret 嵌套 scope 互相覆盖。

这仍然不能证明调度器是最初原因；如果下一轮仍在同一位置出现“单端开火、另一端未开火”，需要继续检查 async 多地图 TickList 顺序和炮塔 burst 状态，而不是继续扩大随机 scope。

## 构建状态

候选 DLL：`BuildOutput/Desync552_553_MiliraRocketCandidate/MP_MeowOnlineShop.dll`

- 构建：0 warning、0 error
- SHA-256：`BD96F6C0FFE702CB4281B0AF943070EEC99913FBC4341A02ABC2435721C9E85B`
- 已使用 `ilspycmd` 静态确认新增类型、def 过滤、四个目标入口和启动注册。
- `552/553` 原 bundle 记录的实际加载版本是 `3.0.121`，且 `sha256=unavailable`；它们没有测试本次候选 DLL。
- 候选 DLL 尚未复制到 `1.6/Assemblies`，未打包，未推送正式 DLL，尚未宣称 runtime fixed。
