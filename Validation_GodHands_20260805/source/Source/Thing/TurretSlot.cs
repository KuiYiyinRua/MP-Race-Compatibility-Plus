using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 炮塔槽位
    [StaticConstructorOnStartup]
    public partial class TurretSlot : IExposable, IAttackTargetSearcher
    {
        // 反射缓存用于访问受保护字段
        private static readonly FieldInfo burstShotsLeftField = typeof(Verb).GetField("burstShotsLeft", BindingFlags.NonPublic | BindingFlags.Instance);

        // 护盾能量字段缓存
        private static readonly Dictionary<Type, FieldInfo> shieldEnergyFieldCache = new Dictionary<Type, FieldInfo>();

        // 护盾HP字段缓存
        private static FieldInfo shieldHPFieldCache;
        private static bool shieldHPFieldCacheInit;

        // 核心数据
        public ThingDef sourceTurretDef;      // 源炮塔定义
        public Thing gun;                      // 枪械实例
        public int stackIndex;                 // 堆叠索引底部为零

        // 运行时射击状态记录
        public float curRotation;              // 当前旋转角度
        public int burstCooldownTicksLeft;     // 冷却剩余
        public int burstWarmupTicksLeft;       // 预热剩余
        public LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;
        public LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;
        public bool holdFire = false;
        public bool sweepMode = false;           // 扫射模式开关
        public bool leadTargeting = true;        // 预瞄模式（对爆炸弹头）
        public bool smartRetarget = true;        // 智能转火开关

        // 扫射逻辑参数设置
        private float sweepAngle = 0f;           // 当前扫射角度偏移
        private float sweepDirection = 1f;       // 扫射方向
        private float sweepMinAngle = -30f;      // 扫射最小角度
        private float sweepMaxAngle = 30f;       // 扫射最大角度
        private float sweepBaseAngle = 0f;       // 扫射基准角度
        private const float SweepSpeed = 3f;     // 扫射速度（度/tick）
        private int sweepUpdateTick = 0;         // 上次更新扫射范围的tick

        // 燃料系统
        public float fuel;
        public float fuelCapacity;
        public float consumeFuelPerShot = 1f;
        public ThingDef fuelThingDef;
        public bool fuelSystemEnabled = false;

        // 炮弹装填系统资料
        public ThingDef loadedShellDef;           // 当前装填的炮弹类型
        public int loadedShellCount;               // 装填的炮弹数量
        public bool shellSystemEnabled = false;   // 是否启用炮弹系统
        public int shellCapacity = 1;              // 炮弹容量

        // 手动激活系统资料
        public bool activationRequired = false;   // 是否需要手动激活
        public bool isActivated = false;          // 是否已激活

        // 残阳特殊功能兼容资料
        public bool isAirDefenseTurret = false;   // 是否为防空炮塔
        public bool airDefenseMode = false;       // 防空模式开关
        public int airDefenseMinPower = 1;        // 最小拦截伤害阈值
        public int airDefenseMinRange = 0;        // 最小拦截爆炸范围
        [Unsaved] private Type sourceTurretClass; // 源炮塔的类类型（用于反射调用）

        // 模块扩展系统架构
        public List<Thing> storedModules = new List<Thing>();  // 存储的扩展模块
        public List<ThingDef> allowedModuleDefs = new List<ThingDef>(); // 允许安装的模块类型
        public bool hasModuleSystem = false;      // 是否有模块系统

        // 残阳模块槽位数据（用于正确计算模块尺寸和位置）
        public Dictionary<int, ModuleSlotData> moduleSlotData = new Dictionary<int, ModuleSlotData>();

        // 残阳炮管系统兼容资料说明
        public List<TopGunData> topGuns = new List<TopGunData>();  // 炮管数据列表
        public bool hasTopGunSystem = false;      // 是否有多炮管系统
        public GraphicData extraGraphicData;      // 额外图形
        public List<Vector3> shootOffsets = new List<Vector3>();  // 射击偏移列表
        public int currentShootOffsetIndex = 0;   // 当前使用的射击偏移索引

        // 浮游炮塔转换系统资料
        public List<FloatingTurret> floatingTurrets = new List<FloatingTurret>();  // 浮游炮塔列表

        // 护盾拦截系统资料说明
        public bool hasShieldModule = false;           // 是否有护盾模块
        public float shieldRadius = 4.5f;              // 护盾半径
        public Color shieldColor = new Color(0.4f, 0.4f, 0.6f); // 护盾颜色
        public int shieldMaxHitPoints = 2000;          // 护盾最大HP
        public int shieldCurrentHitPoints = 0;         // 当前护盾HP
        public int shieldRechargeRate = 3;             // 每tick恢复HP

        // 空转随机动画控制参数
        private int ticksUntilIdleTurn;
        private int idleTurnTicksLeft;
        private bool idleTurnClockwise;

        // 性能缓存
        // 潜在目标缓存（避免每次索敌全量扫描）
        [Unsaved] private List<Thing> cachedPotentialTargets = new List<Thing>();
        [Unsaved] private int lastTargetCacheTick = -999;
        private const int TARGET_CACHE_INTERVAL = 30; // 目标缓存刷新间隔

        // 护盾阻挡缓存（避免重复检测同一目标）
        [Unsaved] private Dictionary<int, bool> shieldBlockCache = new Dictionary<int, bool>(); // 护盾阻挡缓存
        [Unsaved] private int lastShieldCacheTick = -999;
        private const int SHIELD_CACHE_INTERVAL = 15; // 护盾缓存刷新间隔

        // 其他炮塔目标缓存（复用HashSet避免每次分配）
        [Unsaved] private HashSet<Thing> otherTurretsTargetsCache = new HashSet<Thing>();

        // 模块类型缓存（避免每tick字符串比较）
        [Unsaved] private Dictionary<int, ModuleEffectType> moduleEffectTypeCache = new Dictionary<int, ModuleEffectType>();
        [Unsaved] private bool moduleTypeCacheBuilt = false;

        // ========== 引用 ==========
        [Unsaved] private GodHandTurretHead parentHead;
        [Unsaved] private Pawn cachedWearer;

        // 攻击目标搜寻实现
        public Thing Thing => cachedWearer;
        public Verb CurrentEffectiveVerb => AttackVerb;
        public LocalTargetInfo LastAttackedTarget => currentTarget;
        public int LastAttackTargetTick => 0;

        // ========== 属性 ==========
        public Pawn Wearer => cachedWearer;
        public GodHandTurretHead ParentHead => parentHead;  // 用于浮游炮分弹瞄准

        public Verb AttackVerb
        {
            get
            {
                if (gun == null) return null;
                var comp = gun.TryGetComp<CompEquippable>();
                return comp?.PrimaryVerb;
            }
        }

        public bool HasFuel => !fuelSystemEnabled || fuel >= consumeFuelPerShot;
        public float FuelPercent => fuelCapacity > 0 ? fuel / fuelCapacity : 0f;

        // 燃料存量检查状态
        public bool IsFull => !fuelSystemEnabled || (fuelCapacity - fuel) < 1f;

        // 检查自动补给需求状态
        public bool ShouldAutoRefuelNow(float threshold = 0.5f)
        {
            if (!fuelSystemEnabled) return false;
            if (IsFull) return false;
            if (fuelThingDef == null) return false;
            return FuelPercent <= threshold;
        }

        // 检查迫击炮弹装填状态
        public bool HasShellLoaded => !shellSystemEnabled || (loadedShellDef != null && loadedShellCount > 0);

        // 识别手动装填迫击炮类型
        public bool IsMortarType => shellSystemEnabled;

        // 识别手动激活类型说明
        public bool IsManualFireType => activationRequired;

        // 识别手动操纵类型说明
        public bool RequiresManualFire => IsMortarType || IsManualFireType;

        // 判断曲射弹道逻辑条件
        public bool ProjectileFliesOverhead => AttackVerb?.ProjectileFliesOverhead() == true;

        // 获取最大射程数值资料
        public float Range => AttackVerb?.verbProps?.range ?? 0f;

        // 获取有效最小射程数值资料
        public float MinRange => AttackVerb?.verbProps?.EffectiveMinRange(cachedWearer, cachedWearer) ?? 0f;

        // 获取当前弹药定义资料
        public ThingDef CurrentProjectile
        {
            get
            {
                if (shellSystemEnabled && loadedShellDef != null)
                {
                    return loadedShellDef.projectileWhenLoaded;
                }
                return AttackVerb?.verbProps?.defaultProjectile;
            }
        }

        // 检查开火前置条件满足状态
        public bool CanFire
        {
            get
            {
                if (!HasFuel) return false;
                if (!HasShellLoaded) return false;
                if (activationRequired && !isActivated) return false;
                return true;
            }
        }

        // 获取贴图缩放数值资料
        public float TurretDrawSize
        {
            get
            {
                if (sourceTurretDef?.building != null)
                {
                    return sourceTurretDef.building.turretTopDrawSize;
                }
                return 1f;
            }
        }

        // 堆叠间距计算参数抽离
        private const float StackSpacingFactor = 0.035f;

        // 计算当前层级高度偏移资料
        public float HeightOffset
        {
            get
            {
                if (stackIndex == 0 || parentHead == null) return 0f;

                float totalHeight = 0f;
                for (int i = 0; i < stackIndex && i < parentHead.TurretSlots.Count; i++)
                {
                    // 每个炮塔的高度贡献 = 其贴图尺寸 * 间距系数
                    // 堆叠高度偏移量
                    totalHeight += parentHead.TurretSlots[i].TurretDrawSize * StackSpacingFactor;
                }
                return totalHeight;
            }
        }

        // 计算实际绘制空间偏移资料
        public Vector3 DrawOffset => new Vector3(0f, 0.1f + stackIndex * 0.05f, HeightOffset);

        // ========== 初始化 ==========
        public TurretSlot() { }

        public TurretSlot(ThingDef turretDef, int index, GodHandTurretHead parent, float initialFuel = -1f)
        {
            sourceTurretDef = turretDef;
            stackIndex = index;
            parentHead = parent;
            cachedWearer = parent?.Wearer;

            InitializeGun(initialFuel);
        }

        public void SetParent(GodHandTurretHead parent)
        {
            parentHead = parent;
            cachedWearer = parent?.Wearer;
            UpdateGunVerbs();
        }

        private void InitializeGun(float initialFuel = -1f)
        {
            if (sourceTurretDef?.building?.turretGunDef != null)
            {
                gun = ThingMaker.MakeThing(sourceTurretDef.building.turretGunDef);
                UpdateGunVerbs();

                // 自动从 ThingDef 解读炮塔特性
                AutoDetectTurretFeatures(initialFuel);
            }
        }

        // 自动探测组件
        private void AutoDetectTurretFeatures(float initialFuel = -1f)
        {
            if (sourceTurretDef == null) return;

            // 1 燃料系统检查
            InitializeFuelSystem(initialFuel);

            // 2 炮弹系统检查
            InitializeShellSystem();

            // 3 激活系统检查
            InitializeActivationSystem();

            // 4 从定义读取冷却
            if (sourceTurretDef.building != null)
            {
                float initialCooldown = sourceTurretDef.building.turretInitialCooldownTime;
                if (initialCooldown > 0)
                {
                    burstCooldownTicksLeft = (int)(initialCooldown * 60f);
                }
            }
        }

        // 初始化迫击炮弹装填系统资料
        private void InitializeShellSystem()
        {
            if (gun == null) return;

            // 检查炮弹更换组件
            var changeableComp = gun.TryGetComp<CompChangeableProjectile>();
            if (changeableComp == null) return;

            // 验证标准迫击炮弹
            // 仅限手动装填
            var storeSettings = changeableComp.GetStoreSettings();
            if (storeSettings?.filter == null) return;

            // 检查是否允许高爆弹（原版迫击炮弹的标志）
            var heShell = ThingDefOf.Shell_HighExplosive;
            if (heShell == null || !storeSettings.filter.Allows(heShell))
            {
                GodHandModMain.DebugLog($"[炮塔头] {sourceTurretDef.LabelCap} 有CompChangeableProjectile但不使用标准迫击炮弹，跳过炮弹系统");
                return;
            }

            shellSystemEnabled = true;
            shellCapacity = 1; // 迫击炮默认一次装一发
            loadedShellDef = null; // 初始不装填
            loadedShellCount = 0;
            GodHandModMain.DebugLog($"[炮塔头] {sourceTurretDef.LabelCap} 启用迫击炮弹系统");
        }

        // 检索默认可用炮弹定义资料
        private ThingDef GetDefaultShell()
        {
            // 优先查找高爆弹
            var heShell = DefDatabase<ThingDef>.GetNamedSilentFail("Shell_HighExplosive");
            if (heShell != null) return heShell;

            // 查找任何载体炮弹
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.projectileWhenLoaded != null && def.IsShell)
                {
                    return def;
                }
            }

            return null;
        }

        // 激活逻辑说明
        private void InitializeActivationSystem()
        {
            if (sourceTurretDef == null) return;

            // 检查手动激活组件
            var interactableProps = sourceTurretDef.GetCompProperties<CompProperties_Interactable>();
            if (interactableProps != null)
            {
                activationRequired = true;
                isActivated = false; // 需要手动激活
            }
        }

        private void UpdateGunVerbs()
        {
            if (gun == null || cachedWearer == null) return;
            var comp = gun.TryGetComp<CompEquippable>();
            if (comp == null) return;

            foreach (var verb in comp.AllVerbs)
            {
                verb.caster = cachedWearer;
                // 不要修改共享数据
                // 修改它会影响所有使用相同武器的炮塔
                // warmup 由 TurretSlot 自己管理
                verb.castCompleteCallback = BurstComplete;
            }
        }

        // 燃料补给系统初始化分部说明
        private void InitializeFuelSystem(float initialFuel = -1f)
        {
            if (sourceTurretDef == null) return;

            // 获取单发燃料消耗
            var verbProps = gun?.TryGetComp<CompEquippable>()?.PrimaryVerb?.verbProps;
            if (verbProps != null && verbProps.consumeFuelPerShot > 0)
            {
                consumeFuelPerShot = verbProps.consumeFuelPerShot;
            }

            var refuelableProps = sourceTurretDef.GetCompProperties<CompProperties_Refuelable>();
            if (refuelableProps != null)
            {
                fuelSystemEnabled = true;
                fuelCapacity = refuelableProps.fuelCapacity;

                // 应用燃料初始值
                if (initialFuel >= 0f)
                {
                    fuel = Mathf.Clamp(initialFuel, 0f, fuelCapacity);
                }
                else
                {
                    fuel = fuelCapacity;
                }

                if (refuelableProps.fuelFilter?.AllowedThingDefs?.Any() == true)
                {
                    fuelThingDef = refuelableProps.fuelFilter.AllowedThingDefs.First();
                }
            }
            else if (consumeFuelPerShot > 0)
            {
                // 即使无组件也检查消耗
                fuelSystemEnabled = true;
                fuelCapacity = 1000f; // 默认大容量

                // 应用燃料初始值
                if (initialFuel >= 0f)
                {
                    fuel = Mathf.Clamp(initialFuel, 0f, fuelCapacity);
                }
                else
                {
                    fuel = fuelCapacity;
                }
            }
            else
            {
                fuelSystemEnabled = false;
            }
        }

        // 获取预热延迟时间数值资料
        private float GetWarmupTime()
        {
            // 获取预热延迟时间
            if (sourceTurretDef?.building != null)
            {
                return sourceTurretDef.building.turretBurstWarmupTime.Average;
            }
            return 0f;
        }

        private bool WarmingUp => burstWarmupTicksLeft > 0;

        // 刷新逻辑
        public void Tick()
        {
            // 动态更新穿戴者
            if (parentHead != null && cachedWearer != parentHead.Wearer)
            {
                cachedWearer = parentHead.Wearer;
                UpdateGunVerbs(); // 更新verb的caster
            }

            if (cachedWearer == null || !cachedWearer.Spawned) return;
            if (cachedWearer.Downed || cachedWearer.Dead) return;
            if (sourceTurretDef == null || gun == null) return;

            int currentTick = Find.TickManager.TicksGame;

            // 分时分散执行非关键逻辑分部说明
            int tickOffset = stackIndex % 5;
            int tickMod5 = currentTick % 5;

            // 模块效果每五刻执行
            if (hasModuleSystem && tickMod5 == tickOffset)
            {
                ExecuteModuleEffectsOptimized(cachedWearer);
            }

            // 模块动画（每3tick执行一次 - 动画需要更流畅）
            if (hasModuleSystem && currentTick % 3 == stackIndex % 3)
            {
                TickModuleAnimations();
            }

            // 浮游炮塔（每tick执行 - 需要及时响应）
            if (floatingTurrets != null && floatingTurrets.Count > 0)
            {
                TickFloatingTurrets(cachedWearer);
            }

            // 炮管后坐力动画（每tick执行 - 动画需要流畅）
            if (hasTopGunSystem)
            {
                TickTopGuns();
            }

            // Tick枪械的VerbTracker（必须每tick）
            var comp = gun.TryGetComp<CompEquippable>();
            if (comp != null)
            {
                comp.verbTracker.VerbsTick();

                // 如果正在射击
                if (AttackVerb?.state == VerbState.Bursting)
                {
                    // 扫射逻辑定期检查
                    if (currentTick % 30 == 0)
                    {
                        TrySmartRetarget();
                    }
                    return;
                }

                // 确保发射者正确
                foreach (var verb in comp.AllVerbs)
                {
                    if (verb.caster != cachedWearer) verb.caster = cachedWearer;
                }
            }

            // 冷却处理（必须每tick）
            if (burstCooldownTicksLeft > 0)
            {
                burstCooldownTicksLeft--;
            }

            // 炮塔旋转（必须每tick - 视觉效果）
            TurretTopTick();

            // 预热逻辑
            if (WarmingUp)
            {
                // 检查目标有效性
                // 目标无效取消预热
                if (!currentTarget.IsValid || !IsValidTarget(currentTarget))
                {
                    // 取消预热
                    burstWarmupTicksLeft = 0;
                    currentTarget = LocalTargetInfo.Invalid;
                    return;
                }

                burstWarmupTicksLeft--;
                if (burstWarmupTicksLeft == 0)
                {
                    // 预热完成开始射击
                    BeginBurst();
                }
                return; // warmup期间不执行索敌逻辑
            }

            // 索敌与攻击定期检查
            if (burstCooldownTicksLeft <= 0 && (currentTick + stackIndex * 3) % 15 == 0)
            {
                TurretLogic();
            }
        }

        // 执行射击
        private void BeginBurst()
        {
            if (!currentTarget.IsValid) return;
            if (!CanFire) return;

            Verb verb = AttackVerb;
            if (verb == null) return;

            // 设置迫击炮弹药
            if (IsMortarType && loadedShellDef != null)
            {
                // 设置弹药组件
                var changeableComp = gun?.TryGetComp<CompChangeableProjectile>();
                if (changeableComp != null)
                {
                    // 反射设置装填弹药
                    SetCompLoadedShell(changeableComp, loadedShellDef);
                }
                // 迫击炮弹药设置
                // 通过组件设置弹药
                // 反射设置弹药字段
            }

            // 注册verb和slot的关联（用于射击位置偏移）
            TurretHeadShootingTracker.RegisterVerbSlot(verb, this);

            // 注册射击状态
            // 补丁防止姿态卡死
            if (cachedWearer != null)
            {
                TurretHeadShootingTracker.RegisterShooting(cachedWearer);
            }

            // 使用非中断自投射
            bool success = verb.TryStartCastOn(currentTarget, currentTarget, false, true, false, true);

            if (success)
            {
                // 射击成功消耗燃料
                float totalFuelCost = consumeFuelPerShot * verb.verbProps.burstShotCount;
                ConsumeFuel(totalFuelCost);

                // 触发后坐力动画（残阳炮管兼容）
                TriggerRecoil();
            }
            else
            {
                // 射击失败取消注册
                if (cachedWearer != null)
                {
                    TurretHeadShootingTracker.UnregisterShooting(cachedWearer);
                }
            }
        }

        // 装填迫击炮弹实体资料配置
        private void SetCompLoadedShell(CompChangeableProjectile comp, ThingDef shellDef)
        {
            if (comp == null || shellDef == null) return;

            try
            {
                // 使用官方的 LoadShell 方法
                comp.LoadShell(shellDef, 1);
            }
            catch (Exception ex)
            {
                Log.Error($"[TurretSlot] SetCompLoadedShell error: {ex}");
            }
        }

        // 智能转火逻辑检测分部说明
        private void TrySmartRetarget()
        {
            // 检查智能转火开关
            if (!smartRetarget) return;

            // 强制目标不转火
            if (forcedTarget.IsValid) return;

            // 检查当前目标是否需要转火
            if (!ShouldRetarget()) return;

            // 寻找新目标
            LocalTargetInfo newTarget = TryFindNewTarget();
            if (newTarget.IsValid && newTarget.Thing != currentTarget.Thing)
            {
                // 切换目标
                currentTarget = newTarget;

                // 反射更新攻击目标
                Verb verb = AttackVerb;
                if (verb != null)
                {
                    SetVerbCurrentTarget(verb, newTarget);
                }
            }
        }

        // 反射缓存
        private static readonly FieldInfo verbCurrentTargetField = typeof(Verb).GetField("currentTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        private void SetVerbCurrentTarget(Verb verb, LocalTargetInfo target)
        {
            if (verbCurrentTargetField != null)
            {
                try
                {
                    verbCurrentTargetField.SetValue(verb, target);
                }
                catch { }
            }
        }

        // 判断目标击杀价值决定转火资料
        private bool ShouldRetarget()
        {
            // 确保cachedWearer有效
            if (cachedWearer == null || !cachedWearer.Spawned || cachedWearer.Map == null)
                return false;

            if (!currentTarget.IsValid || currentTarget.Thing == null)
                return true; // 目标无效需转火

            Thing target = currentTarget.Thing;

            // 目标已死亡或被摧毁
            if (target.Destroyed)
                return true;

            if (target is Pawn pawn)
            {
                // 目标已死亡或倒地
                if (pawn.Dead || pawn.Downed)
                    return true;

                // 计算预估伤害来判断是否转火
                Verb verb = AttackVerb;
                if (verb?.verbProps?.defaultProjectile?.projectile != null)
                {
                    var proj = verb.verbProps.defaultProjectile.projectile;

                    // 获取基础伤害
                    float baseDamage = proj.GetDamageAmount(gun);
                    int burstShotsLeft = GetBurstShotsLeft(verb);

                    // 评估剩余总伤害
                    // 伤害超过阈值则转火
                    float estimatedDamage = baseDamage * burstShotsLeft * 0.5f * 0.7f;

                    // 获取目标当前生命值
                    float currentHP = GetPawnCurrentHP(pawn);

                    // 伤害溢出则转火
                    if (estimatedDamage > currentHP * 1.5f)
                        return true;
                }
                else
                {
                    // 缺失数据则百分比判断
                    float targetHealth = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                    if (targetHealth < 0.15f)
                        return true;
                }
            }

            return false;
        }

        // 获取目标实时生命数值资料库
        private float GetPawnCurrentHP(Pawn pawn)
        {
            if (pawn?.health?.summaryHealth == null) return 100f;

            // 使用血量百分比估算
            float healthPercent = pawn.health.summaryHealth.SummaryHealthPercent;
            float maxHealth = pawn.HealthScale * 100f; // 基础100HP * 健康比例

            return healthPercent * maxHealth;
        }

        // 反射获取连发剩余量
        private int GetBurstShotsLeft(Verb verb)
        {
            if (verb == null || burstShotsLeftField == null)
                return 0;

            try
            {
                return (int)burstShotsLeftField.GetValue(verb);
            }
            catch
            {
                return 0;
            }
        }

        private void TurretTopTick()
        {
            if (currentTarget.IsValid)
            {
                // 获取基础瞄准角度
                Vector3 targetPos = GetAimPosition();
                Vector3 myPos = cachedWearer.DrawPos;
                float baseTargetAngle = (targetPos - myPos).AngleFlat();

                // 扫射模式瞄准范围
                if (sweepMode && AttackVerb?.state == VerbState.Bursting)
                {
                    // 更新扫射
                    if (Find.TickManager.TicksGame - sweepUpdateTick > 60)
                    {
                        UpdateSweepRange();
                        sweepUpdateTick = Find.TickManager.TicksGame;
                    }

                    // 更新扫射角度
                    sweepAngle += SweepSpeed * sweepDirection;

                    // 到达边界时反向
                    if (sweepAngle >= sweepMaxAngle)
                    {
                        sweepDirection = -1f;
                        sweepAngle = sweepMaxAngle;
                    }
                    else if (sweepAngle <= sweepMinAngle)
                    {
                        sweepDirection = 1f;
                        sweepAngle = sweepMinAngle;
                    }

                    float targetAngle = sweepBaseAngle + sweepAngle;
                    curRotation = Mathf.MoveTowardsAngle(curRotation, targetAngle, 10f);
                }
                else
                {
                    // 普通模式直接瞄准
                    sweepAngle = 0f;
                    curRotation = Mathf.MoveTowardsAngle(curRotation, baseTargetAngle, 6f);
                }
            }
            else
            {
                // 空转动画
                sweepAngle = 0f;
                IdleTurnTick();
            }
        }

        // 计算并更新扫射角度范围资料
        private void UpdateSweepRange()
        {
            if (cachedWearer == null || !cachedWearer.Spawned) return;

            Verb verb = AttackVerb;
            if (verb == null) return;

            float range = verb.verbProps.range;
            Vector3 myPos = cachedWearer.DrawPos;

            List<float> enemyAngles = new List<float>();

            // 获取范围内所有敌人的角度
            foreach (var thing in cachedWearer.Map.attackTargetsCache.GetPotentialTargetsFor(cachedWearer))
            {
                Thing t = thing.Thing;
                if (!IsValidTargetThing(t)) continue;

                float dist = (t.Position - cachedWearer.Position).LengthHorizontal;
                if (dist > range) continue;

                float angle = (t.DrawPos - myPos).AngleFlat();
                enemyAngles.Add(angle);
            }

            if (enemyAngles.Count == 0)
            {
                // 无敌人用默认范围
                sweepBaseAngle = curRotation;
                sweepMinAngle = -30f;
                sweepMaxAngle = 30f;
                return;
            }

            if (enemyAngles.Count == 1)
            {
                // 单目标小范围扫射
                sweepBaseAngle = enemyAngles[0];
                sweepMinAngle = -15f;
                sweepMaxAngle = 15f;
                return;
            }

            // 多目标计算包络范围
            enemyAngles.Sort();

            // 计算平均角度作为基准
            float sumX = 0f, sumY = 0f;
            foreach (float a in enemyAngles)
            {
                sumX += Mathf.Cos(a * Mathf.Deg2Rad);
                sumY += Mathf.Sin(a * Mathf.Deg2Rad);
            }
            sweepBaseAngle = Mathf.Atan2(sumY, sumX) * Mathf.Rad2Deg;

            // 计算相对于基准角度的最大偏移
            float maxOffset = 0f;
            foreach (float a in enemyAngles)
            {
                float offset = Mathf.Abs(Mathf.DeltaAngle(sweepBaseAngle, a));
                if (offset > maxOffset) maxOffset = offset;
            }

            // 扫射范围带余量
            maxOffset = Mathf.Clamp(maxOffset + 10f, 15f, 90f);
            sweepMinAngle = -maxOffset;
            sweepMaxAngle = maxOffset;
        }

        // 获取瞄准点位置资料包含预瞄资料
        private Vector3 GetAimPosition()
        {
            if (!currentTarget.IsValid) return cachedWearer.DrawPos;

            Vector3 targetPos = currentTarget.HasThing
                ? currentTarget.Thing.DrawPos
                : currentTarget.Cell.ToVector3Shifted();

            // 对移动目标的预瞄
            if (leadTargeting && currentTarget.Thing is Pawn targetPawn && targetPawn.pather != null)
            {
                Verb verb = AttackVerb;
                if (verb?.verbProps?.defaultProjectile?.projectile != null)
                {
                    var proj = verb.verbProps.defaultProjectile.projectile;

                    // 检查是否是爆炸弹头
                    bool isExplosive = proj.explosionRadius > 0;

                    if (isExplosive && targetPawn.pather.MovingNow)
                    {
                        // 计算弹丸飞行时间
                        float distance = (targetPos - cachedWearer.DrawPos).magnitude;
                        float projectileSpeed = proj.speed;
                        if (projectileSpeed > 0)
                        {
                            float flightTime = distance / projectileSpeed;

                            // 预测目标位置
                            Vector3 velocity = targetPawn.pather.nextCell.ToVector3Shifted() - targetPawn.DrawPos;
                            velocity = velocity.normalized * targetPawn.GetStatValue(StatDefOf.MoveSpeed);

                            targetPos += velocity * flightTime * 0.5f; // 半倍补偿预瞄
                        }
                    }
                }
            }

            return targetPos;
        }

        private void IdleTurnTick()
        {
            float baseAngle = cachedWearer.Rotation.AsAngle;

            if (idleTurnTicksLeft > 0)
            {
                idleTurnTicksLeft--;
                float turnSpeed = 0.26f;
                curRotation += idleTurnClockwise ? turnSpeed : -turnSpeed;
            }
            else if (ticksUntilIdleTurn > 0)
            {
                ticksUntilIdleTurn--;
                // 缓慢回正
                curRotation = Mathf.MoveTowardsAngle(curRotation, baseAngle, 0.5f);
            }
            else
            {
                // 随机启动新的空转
                if (Rand.Chance(0.01f))
                {
                    idleTurnTicksLeft = Rand.RangeInclusive(150, 350);
                    idleTurnClockwise = Rand.Bool;
                }
                else
                {
                    curRotation = Mathf.MoveTowardsAngle(curRotation, baseAngle, 0.3f);
                }
            }
        }

        private void TurretLogic()
        {
            if (holdFire) return;
            if (burstCooldownTicksLeft > 0) return;

            Verb verb = AttackVerb;
            if (verb == null)
            {
                return;
            }

            // 迫击炮仅强制目标
            // 集群火箭仅强制目标
            // 普通类型正常搜寻
            // 迫击炮仅强制攻击
            if (IsMortarType)
            {
                // 没有装填炮弹时不做任何事
                if (!HasShellLoaded)
                {
                    if (forcedTarget.IsValid)
                    {
                        Log.Warning($"[TurretSlot] 迫击炮有目标但没有炮弹装填");
                    }
                    return;
                }

                // 只有强制目标时才射击
                if (forcedTarget.IsValid)
                {
                    GodHandModMain.DebugLog($"[TurretSlot] 迫击炮检查目标: {forcedTarget}, IsValidTarget: {IsValidTarget(forcedTarget)}");

                    if (IsValidTarget(forcedTarget))
                    {
                        currentTarget = forcedTarget;
                        GodHandModMain.DebugLog($"[TurretSlot] 迫击炮开始预热瞄准: {currentTarget}");
                        StartWarmup();
                        // 迫击炮射击后清除强制目标（一发一目标）
                        forcedTarget = LocalTargetInfo.Invalid;
                    }
                    else
                    {
                        Log.Warning($"[TurretSlot] 迫击炮目标无效，清除: {forcedTarget}");
                        forcedTarget = LocalTargetInfo.Invalid;
                    }
                }
                return;
            }

            // 火箭仅强制攻击
            if (IsManualFireType)
            {
                // 只有强制目标时才射击
                if (forcedTarget.IsValid && IsValidTarget(forcedTarget))
                {
                    currentTarget = forcedTarget;
                    StartWarmup();
                    // 射击后清除强制目标
                    forcedTarget = LocalTargetInfo.Invalid;
                }
                return;
            }

            // 普通索敌逻辑
            // 强制目标优先
            if (forcedTarget.IsValid)
            {
                if (IsValidTarget(forcedTarget))
                {
                    currentTarget = forcedTarget;
                    StartWarmup();
                    return;
                }
                else
                {
                    // 静默清除目标
                }
                forcedTarget = LocalTargetInfo.Invalid;
            }

            // 自动索敌
            currentTarget = TryFindNewTarget();

            if (currentTarget.IsValid)
            {
                StartWarmup();
            }
        }

        // 发起射击预热瞄准序列分部说明
        private void StartWarmup()
        {
            if (!CanFire)
            {
                if (!HasFuel) Log.Warning($"[TurretSlot] StartWarmup失败: 没有燃料");
                else if (!HasShellLoaded) Log.Warning($"[TurretSlot] StartWarmup失败: 没有装填炮弹");
                else if (activationRequired && !isActivated) Log.Warning($"[TurretSlot] StartWarmup失败: 需要手动激活");
                return;
            }

            float warmupTime = GetWarmupTime();
            if (warmupTime > 0)
            {
                // 开始预热
                burstWarmupTicksLeft = (int)(warmupTime * 60f);
            }
            else
            {
                // 直接射击
                BeginBurst();
            }
        }

        private LocalTargetInfo TryFindNewTarget()
        {
            // 确保cachedWearer有效
            if (cachedWearer == null || !cachedWearer.Spawned || cachedWearer.Map == null)
                return LocalTargetInfo.Invalid;

            // 防空优先拦截
            if (isAirDefenseTurret && airDefenseMode)
            {
                LocalTargetInfo airTarget = TryFindAirDefenseTarget();
                if (airTarget.IsValid)
                    // 优先拦截飞弹
                    return airTarget;
                // 无飞弹不攻地
                return LocalTargetInfo.Invalid;
            }

            Verb verb = AttackVerb;
            if (verb == null) return LocalTargetInfo.Invalid;

            int currentTick = Find.TickManager.TicksGame;
            float range = verb.verbProps.range;

            // 刷新目标缓存
            // 刷新护盾缓存
            if (currentTick - lastTargetCacheTick > TARGET_CACHE_INTERVAL)
            {
                RefreshPotentialTargetsCache(range);
                lastTargetCacheTick = currentTick;
            }

            // 刷新护盾缓存
            if (currentTick - lastShieldCacheTick > SHIELD_CACHE_INTERVAL)
            {
                shieldBlockCache.Clear();
                lastShieldCacheTick = currentTick;
            }

            // 获取其他炮塔已瞄准的目标（用于分散火力）
            HashSet<Thing> alreadyTargeted = GetOtherTurretsTargets();

            // 获取投射物伤害（用于评估护盾贯穿）
            int projectileDamage = verb.verbProps.defaultProjectile?.projectile?.GetDamageAmount(gun) ?? 0;

            // 优先选择近距离目标
            // 备选被护盾保护目标
            float bestScore = float.MinValue;

            // 获取备选目标
            Thing closestShieldedTarget = null;
            float closestShieldedDist = float.MaxValue;

            // 手动搜索目标
            Thing bestTarget = null;

            // 使用缓存的目标列表
            int targetCount = cachedPotentialTargets.Count;
            for (int i = 0; i < targetCount; i++)
            {
                Thing t = cachedPotentialTargets[i];
                if (t == null || t.Destroyed) continue;
                if (!IsValidTargetThingBasic(t)) continue;

                float dist = (t.Position - cachedWearer.Position).LengthHorizontal;
                if (dist > range) continue;

                // 使用缓存的护盾检测结果
                bool blockedByShield = IsTargetBlockedByShieldCached(t, projectileDamage);

                // 记录最近的被护盾保护的目标
                if (blockedByShield)
                {
                    if (dist < closestShieldedDist)
                    {
                        closestShieldedDist = dist;
                        closestShieldedTarget = t;
                    }
                    continue; // 跳过被护盾保护的目标
                }

                // 评分距离近未被瞄准
                float score = range - dist; // 距离分数（近的更高）

                // 威胁度加分
                if (t is Pawn pawn)
                {
                    // 正在攻击我方的加分
                    if (pawn.CurJob?.targetA.Thing?.Faction == cachedWearer.Faction)
                        score += 50f;
                    // 血量低的减分（让其他炮塔处理）
                    float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                    score += health * 20f;
                }

                // 未被其他炮塔瞄准的大幅加分（分弹）
                if (!alreadyTargeted.Contains(t))
                    score += 100f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = t;
                }
            }

            // 返回未被阻挡目标
            if (bestTarget != null)
            {
                return new LocalTargetInfo(bestTarget);
            }
            // 返回未被阻挡目标
            // 选择最近目标硬贯穿
            else if (closestShieldedTarget != null)
            {
                GodHandModMain.DebugLog($"[炮塔] 所有目标被护盾保护，尝试硬贯穿最近目标: {closestShieldedTarget.LabelCap}");
                return new LocalTargetInfo(closestShieldedTarget);
            }

            return LocalTargetInfo.Invalid;
        }

        // 刷新区域潜在敌对目标缓存数据库
        private void RefreshPotentialTargetsCache(float range)
        {
            cachedPotentialTargets.Clear();
            if (cachedWearer?.Map == null) return;

            foreach (var thing in cachedWearer.Map.attackTargetsCache.GetPotentialTargetsFor(cachedWearer))
            {
                Thing t = thing.Thing;
                if (t == null || t.Destroyed) continue;

                // 距离预筛选
                float dist = (t.Position - cachedWearer.Position).LengthHorizontal;
                if (dist > range + 5f) continue;

                cachedPotentialTargets.Add(t);
            }
        }

        // 缓存检测目标是否被护盾拦截逻辑
        private bool IsTargetBlockedByShieldCached(Thing target, int projectileDamage)
        {
            int targetId = target.thingIDNumber;

            // 检查缓存
            if (shieldBlockCache.TryGetValue(targetId, out bool cached))
            {
                return cached;
            }

            // 计算并缓存结果
            bool blocked = IsTargetBlockedByShield(cachedWearer.DrawPos, target.DrawPos, cachedWearer.Map, parentHead, projectileDamage);

            // 检查个人护盾
            if (!blocked && target is Pawn targetPawn && HasActivePersonalShield(targetPawn))
            {
                blocked = true;
            }

            shieldBlockCache[targetId] = blocked;
            return blocked;
        }

        // 获取队友已搜寻目标实现火力分配
        private HashSet<Thing> GetOtherTurretsTargets()
        {
            // 复用缓存集合
            otherTurretsTargetsCache.Clear();
            if (parentHead == null) return otherTurretsTargetsCache;

            var slots = parentHead.TurretSlots;
            int count = slots.Count;
            for (int i = 0; i < count; i++)
            {
                var slot = slots[i];
                if (slot == this) continue; // 跳过自己

                // 包括正在瞄准、预热中或射击中的目标
                Thing targetThing = slot.currentTarget.IsValid ? slot.currentTarget.Thing : null;
                if (targetThing != null)
                {
                    otherTurretsTargetsCache.Add(targetThing);
                }
                else
                {
                    targetThing = slot.forcedTarget.IsValid ? slot.forcedTarget.Thing : null;
                    if (targetThing != null)
                    {
                        otherTurretsTargetsCache.Add(targetThing);
                    }
                }
            }

            return otherTurretsTargetsCache;
        }

        // 执行目标基础合法性过滤分部说明
        private bool IsValidTargetThingBasic(Thing t)
        {
            if (t == null) return false;
            if (cachedWearer == null) return false;
            if (t == cachedWearer) return false;

            if (t is Pawn pawn)
            {
                if (pawn.Dead || pawn.Downed) return false;
                if (cachedWearer.Faction != null && pawn.Faction != null && !pawn.Faction.HostileTo(cachedWearer.Faction))
                    return false;
            }

            Verb verb = AttackVerb;
            if (verb == null) return false;

            float dist = (t.Position - cachedWearer.Position).LengthHorizontal;
            if (dist > verb.verbProps.range) return false;
            if (dist < verb.verbProps.EffectiveMinRange(t, cachedWearer)) return false;

            return true;
        }

        // 综合验证目标包含护盾检测分部说明
        private bool IsValidTargetThing(Thing t)
        {
            if (!IsValidTargetThingBasic(t)) return false;

            // 检查目标受护盾保护
            if (IsTargetBlockedByShield(cachedWearer.DrawPos, t.DrawPos, cachedWearer.Map, parentHead))
            {
                return false;
            }

            // 检查个人护盾
            if (t is Pawn targetPawn && HasActivePersonalShield(targetPawn))
            {
                return false;
            }

            return true;
        }

        // 静态检测护盾拦截物理逻辑架构说明
        // 检查目标是否被护盾阻挡
        public static bool IsTargetBlockedByShield(Vector3 shooterPos, Vector3 targetPos, Map map, GodHandTurretHead turretHead, int projectileDamage = 0)
        {
            if (map == null) return false;

            // 检查原版护盾拦截
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.ProjectileInterceptor))
            {
                var comp = thing.TryGetComp<CompProjectileInterceptor>();
                if (comp == null || !comp.Active) continue;

                Vector3 shieldCenter = thing.DrawPos;
                float radius = comp.Props.radius;
                float radiusSq = radius * radius;

                // 射击者在护盾内（可以自由攻击）
                if ((shooterPos - shieldCenter).sqrMagnitude <= radiusSq) continue;

                bool blocked = false;

                // 目标在护盾内
                if ((targetPos - shieldCenter).sqrMagnitude <= radiusSq)
                    blocked = true;

                // 弹道穿过护盾
                if (!blocked && DoesLineIntersectCircleStatic(shooterPos, targetPos, shieldCenter, radius))
                    blocked = true;

                if (blocked)
                {
                    // 评估是否可以贯穿护盾
                    int shieldHP = GetShieldCurrentHP(comp);
                    if (projectileDamage > 0 && shieldHP > 0 && shieldHP <= projectileDamage * 3)
                    {
                        // 护盾余量低可尝试贯穿
                        continue;
                    }
                    return true;
                }
            }

            // 检查炮塔头护盾
            if (turretHead != null)
            {
                foreach (var slot in turretHead.TurretSlots)
                {
                    if (!slot.hasShieldModule || slot.shieldCurrentHitPoints <= 0) continue;

                    Pawn wearer = slot.Wearer;
                    if (wearer == null) continue;

                    Vector3 shieldCenter = wearer.DrawPos;
                    float radius = slot.shieldRadius;
                    float radiusSq = radius * radius;

                    // 射击者在护盾内
                    if ((shooterPos - shieldCenter).sqrMagnitude <= radiusSq) continue;

                    bool blocked = false;

                    // 目标在护盾内
                    if ((targetPos - shieldCenter).sqrMagnitude <= radiusSq)
                        blocked = true;

                    // 弹道穿过护盾
                    if (!blocked && DoesLineIntersectCircleStatic(shooterPos, targetPos, shieldCenter, radius))
                        blocked = true;

                    if (blocked)
                    {
                        // 评估是否可以贯穿
                        if (projectileDamage > 0 && slot.shieldCurrentHitPoints <= projectileDamage * 3)
                        {
                            continue;
                        }
                        return true;
                    }
                }
            }

            return false;
        }

        // 反射获取护盾实时生命数值资料库
        private static int GetShieldCurrentHP(CompProjectileInterceptor comp)
        {
            if (comp == null) return 0;

            try
            {
                // 使用缓存的字段避免每次反射查找
                if (!shieldHPFieldCacheInit)
                {
                    shieldHPFieldCache = typeof(CompProjectileInterceptor).GetField("currentHitPoints",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    shieldHPFieldCacheInit = true;
                }

                if (shieldHPFieldCache != null)
                {
                    return (int)shieldHPFieldCache.GetValue(comp);
                }
            }
            catch { }

            return int.MaxValue; // 默认满血
        }

        // 检测目标是否开启单体防护装置说明
        public static bool HasActivePersonalShield(Pawn targetPawn)
        {
            if (targetPawn?.apparel?.WornApparel == null) return false;

            foreach (var apparel in targetPawn.apparel.WornApparel)
            {
                var apparelType = apparel.GetType();
                if (!apparelType.Name.Contains("Shield") && apparelType.BaseType?.Name.Contains("Shield") != true)
                    continue;

                // 使用缓存的反射字段
                if (!shieldEnergyFieldCache.TryGetValue(apparelType, out FieldInfo energyField))
                {
                    energyField = apparelType.GetField("energy", BindingFlags.Instance | BindingFlags.NonPublic);
                    shieldEnergyFieldCache[apparelType] = energyField;
                }

                if (energyField != null)
                {
                    float energy = (float)energyField.GetValue(apparel);
                    if (energy > 0) return true;
                }
            }

            return false;
        }

        // 执行线段与圆交点几何计算分部说明
        private static bool DoesLineIntersectCircleStatic(Vector3 lineStart, Vector3 lineEnd, Vector3 circleCenter, float radius)
        {
            Vector2 start = new Vector2(lineStart.x, lineStart.z);
            Vector2 end = new Vector2(lineEnd.x, lineEnd.z);
            Vector2 center = new Vector2(circleCenter.x, circleCenter.z);

            Vector2 d = end - start;
            Vector2 f = start - center;

            float a = Vector2.Dot(d, d);
            float b = 2f * Vector2.Dot(f, d);
            float c = Vector2.Dot(f, f) - radius * radius;

            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0) return false;

            discriminant = Mathf.Sqrt(discriminant);
            float t1 = (-b - discriminant) / (2f * a);
            float t2 = (-b + discriminant) / (2f * a);

            return (t1 >= 0f && t1 <= 1f) || (t2 >= 0f && t2 <= 1f);
        }

        // 射击预热阶段实时目标验证逻辑说明
        private bool IsValidTarget(LocalTargetInfo target)
        {
            if (!target.IsValid) return false;
            if (target.ThingDestroyed) return false;
            if (cachedWearer?.Map == null) return false;

            // 检查目标位置是否在射程内
            float dist = (target.Cell - cachedWearer.Position).LengthHorizontal;
            float range = Range;
            float minRange = MinRange;

            if (dist > range || dist < minRange) return false;

            // 检查目标位置是否在地图内
            if (!target.Cell.InBounds(cachedWearer.Map)) return false;

            // 迫击炮类型可以打地面位置
            if (IsMortarType || IsManualFireType)
            {
                // 物体目标验证
                if (target.HasThing)
                {
                    return IsValidTargetThing(target.Thing);
                }

                return true; // 地面位置有效
            }

            // 普通炮塔
            Thing t = target.Thing;
            if (t == null) return false;

            // 玩家手动设置放宽检查
            // 仅检查存续与距离
            if (forcedTarget.IsValid && forcedTarget.Thing == t)
            {
                // 强制目标只检查基本存在性和射程
                if (t.Destroyed) return false;
                return true;
            }

            // 自动索敌的目标使用完整验证
            return IsValidTargetThing(t);
        }

        private void BurstComplete()
        {
            // 清理射击位置追踪
            var verb = AttackVerb;
            if (verb != null)
            {
                TurretHeadShootingTracker.UnregisterVerbSlot(verb);
            }

            // 消耗炮弹（迫击炮等）
            if (shellSystemEnabled)
            {
                ConsumeShell();
            }

            // 集群火箭射击后取消
            if (activationRequired)
            {
                Deactivate();
            }

            if (sourceTurretDef?.building != null)
            {
                burstCooldownTicksLeft = (int)(sourceTurretDef.building.turretBurstCooldownTime * 60f);
            }
        }

        // ========== 燃料方法 ==========
        public void ConsumeFuel(float amount)
        {
            if (!fuelSystemEnabled) return;
            fuel = Mathf.Max(0f, fuel - amount);
        }

        public void Refuel(float amount)
        {
            if (!fuelSystemEnabled) return;
            fuel = Mathf.Min(fuelCapacity, fuel + amount);
        }

        // 炮弹装填

        // 执行炮弹实体装填任务分配资料库
        public void LoadShell(ThingDef shellDef, int count)
        {
            if (!shellSystemEnabled) return;
            loadedShellDef = shellDef;
            loadedShellCount = Mathf.Clamp(count, 0, shellCapacity);
        }

        // 扣除发射消耗的炮弹实体数量资料
        public void ConsumeShell()
        {
            if (!shellSystemEnabled) return;
            if (loadedShellCount > 0)
            {
                loadedShellCount--;
            }
            if (loadedShellCount <= 0)
            {
                loadedShellDef = null;
            }
        }

        // 获取所需炮弹数
        public int GetShellsNeeded()
        {
            if (!shellSystemEnabled) return 0;
            return shellCapacity - loadedShellCount;
        }

        // ========== 激活方法 ==========

        // 激活炮塔
        public void Activate()
        {
            if (!activationRequired) return;
            isActivated = true;
        }

        // 取消激活
        public void Deactivate()
        {
            isActivated = false;
        }

        // 保存与加载数据序列化接口
        public void ExposeData()
        {
            Scribe_Defs.Look(ref sourceTurretDef, "sourceTurretDef");
            Scribe_Deep.Look(ref gun, "gun");
            Scribe_Values.Look(ref stackIndex, "stackIndex");
            Scribe_Values.Look(ref curRotation, "curRotation");
            Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft");
            Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft");
            Scribe_TargetInfo.Look(ref currentTarget, "currentTarget");
            Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget");
            Scribe_Values.Look(ref holdFire, "holdFire");
            Scribe_Values.Look(ref sweepMode, "sweepMode");
            Scribe_Values.Look(ref leadTargeting, "leadTargeting", true);
            Scribe_Values.Look(ref smartRetarget, "smartRetarget", true);
            Scribe_Values.Look(ref fuel, "fuel");
            Scribe_Values.Look(ref fuelCapacity, "fuelCapacity");
            Scribe_Values.Look(ref consumeFuelPerShot, "consumeFuelPerShot", 1f);
            Scribe_Defs.Look(ref fuelThingDef, "fuelThingDef");
            Scribe_Values.Look(ref fuelSystemEnabled, "fuelSystemEnabled");

            // 炮弹系统
            Scribe_Defs.Look(ref loadedShellDef, "loadedShellDef");
            Scribe_Values.Look(ref loadedShellCount, "loadedShellCount");
            Scribe_Values.Look(ref shellSystemEnabled, "shellSystemEnabled");
            Scribe_Values.Look(ref shellCapacity, "shellCapacity", 1);

            // 激活系统
            Scribe_Values.Look(ref activationRequired, "activationRequired");
            Scribe_Values.Look(ref isActivated, "isActivated");

            // 模块系统
            Scribe_Collections.Look(ref storedModules, "storedModules", LookMode.Deep);
            Scribe_Collections.Look(ref allowedModuleDefs, "allowedModuleDefs", LookMode.Def);
            Scribe_Values.Look(ref hasModuleSystem, "hasModuleSystem");

            // 炮管系统（残阳 TopGun_cy）
            Scribe_Collections.Look(ref topGuns, "topGuns", LookMode.Deep);
            Scribe_Values.Look(ref hasTopGunSystem, "hasTopGunSystem");

            // 浮游炮塔
            Scribe_Collections.Look(ref floatingTurrets, "floatingTurrets", LookMode.Deep);

            // 护盾系统
            Scribe_Values.Look(ref hasShieldModule, "hasShieldModule");
            Scribe_Values.Look(ref shieldRadius, "shieldRadius", 4.5f);
            Scribe_Values.Look(ref shieldColor, "shieldColor");
            Scribe_Values.Look(ref shieldMaxHitPoints, "shieldMaxHitPoints", 2000);
            Scribe_Values.Look(ref shieldCurrentHitPoints, "shieldCurrentHitPoints");
            Scribe_Values.Look(ref shieldRechargeRate, "shieldRechargeRate", 3);

            // 防空系统（残阳兼容）
            Scribe_Values.Look(ref isAirDefenseTurret, "isAirDefenseTurret");
            Scribe_Values.Look(ref airDefenseMode, "airDefenseMode");
            Scribe_Values.Look(ref airDefenseMinPower, "airDefenseMinPower", 1);
            Scribe_Values.Look(ref airDefenseMinRange, "airDefenseMinRange", 0);

            // 多炮口偏移
            Scribe_Collections.Look(ref shootOffsets, "shootOffsets", LookMode.Value);
            Scribe_Values.Look(ref currentShootOffsetIndex, "currentShootOffsetIndex");

            // 模块槽位数据（用于尺寸倍率）
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // 保存时转换为列表
                List<ModuleSlotData> slotDataList = moduleSlotData?.Values.ToList() ?? new List<ModuleSlotData>();
                Scribe_Collections.Look(ref slotDataList, "moduleSlotDataList", LookMode.Deep);
            }
            else
            {
                List<ModuleSlotData> slotDataList = null;
                Scribe_Collections.Look(ref slotDataList, "moduleSlotDataList", LookMode.Deep);
                if (slotDataList != null)
                {
                    moduleSlotData = new Dictionary<int, ModuleSlotData>();
                    foreach (var data in slotDataList)
                    {
                        if (data != null)
                        {
                            moduleSlotData[data.slot] = data;
                        }
                    }
                }
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (sourceTurretDef != null && gun == null)
                {
                    InitializeGun();
                }
                if (storedModules == null)
                {
                    storedModules = new List<Thing>();
                }
                if (allowedModuleDefs == null)
                {
                    allowedModuleDefs = new List<ThingDef>();
                }
                if (moduleSlotData == null)
                {
                    moduleSlotData = new Dictionary<int, ModuleSlotData>();
                }
                if (topGuns == null)
                {
                    topGuns = new List<TopGunData>();
                }
                if (floatingTurrets == null)
                {
                    floatingTurrets = new List<FloatingTurret>();
                }

                // 恢复浮游炮的父槽位引用（用于分弹瞄准）
                foreach (var floater in floatingTurrets)
                {
                    floater.parentSlot = this;
                }

                // 重建炮管图形
                if (hasTopGunSystem && topGuns.Count > 0 && sourceTurretDef != null)
                {
                    RebuildTopGunGraphics();
                }
            }
        }

        // 加载存档后重建炮管图形资料库
        private void RebuildTopGunGraphics()
        {
            if (sourceTurretDef == null || topGuns == null || topGuns.Count == 0) return;

            try
            {
                var modExtensions = sourceTurretDef.modExtensions;
                if (modExtensions == null) return;

                foreach (var ext in modExtensions)
                {
                    if (ext == null) continue;

                    var extType = ext.GetType();
                    if (!extType.Name.Contains("MoreFeaturesTurret") && !extType.Name.Contains("TopGun"))
                        continue;

                    var topGunsDataField = extType.GetField("topGunsData",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (topGunsDataField != null)
                    {
                        var topGunsDataList = topGunsDataField.GetValue(ext) as System.Collections.IList;
                        if (topGunsDataList != null)
                        {
                            // 重建每个炮管的 GraphicData
                            for (int i = 0; i < topGuns.Count && i < topGunsDataList.Count; i++)
                            {
                                var topGunObj = topGunsDataList[i];
                                if (topGunObj == null) continue;

                                var topGunType = topGunObj.GetType();

                                // 提取 graphicData_Gun
                                var graphicField = topGunType.GetField("graphicData_Gun",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (graphicField != null)
                                {
                                    topGuns[i].graphicDataGun = graphicField.GetValue(topGunObj) as GraphicData;
                                }

                                // 提取 graphicData_Gun_ed
                                var graphicEdField = topGunType.GetField("graphicData_Gun_ed",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (graphicEdField != null)
                                {
                                    topGuns[i].graphicDataGunEd = graphicEdField.GetValue(topGunObj) as GraphicData;
                                }
                            }

                            GodHandModMain.DebugLog($"[炮塔头] 重建了 {topGuns.Count} 个炮管的图形数据");
                        }
                    }

                    break;
                }

                // 重建 extraGraphicData
                RebuildExtraGraphicData();
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 重建炮管图形失败: {ex.Message}");
            }
        }

        // 重建额外组件装饰图形资料库
        private void RebuildExtraGraphicData()
        {
            if (sourceTurretDef?.building?.turretGunDef == null) return;

            try
            {
                var gunDef = sourceTurretDef.building.turretGunDef;
                if (gunDef.comps == null) return;

                foreach (var compProps in gunDef.comps)
                {
                    if (compProps == null) continue;
                    var propsType = compProps.GetType();

                    if (propsType.Name.Contains("DrawExtra"))
                    {
                        var graphicDataField = propsType.GetField("graphicData",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (graphicDataField != null)
                        {
                            extraGraphicData = graphicDataField.GetValue(compProps) as GraphicData;
                            GodHandModMain.DebugLog($"[炮塔头] 重建了额外图形数据");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 重建额外图形失败: {ex.Message}");
            }
        }

        // 模块系统管理方法集合架构

        // 初始化残阳模块扩展系统资料库
        public void InitializeModuleSystem(Building sourceTurret)
        {
            if (sourceTurret == null)
            {
                GodHandModMain.DebugLog($"[炮塔头] InitializeModuleSystem: sourceTurret 为空！");
                return;
            }

            GodHandModMain.DebugLog($"[炮塔头] 开始初始化模块系统，源炮塔: {sourceTurret.def.defName}");

            // 确保 storedModules 列表已初始化
            if (storedModules == null)
            {
                storedModules = new List<Thing>();
            }

            // 使用反射检查是否有模块容器组件
            foreach (var comp in sourceTurret.AllComps)
            {
                if (comp == null) continue;

                var compType = comp.GetType();

                // 检查模块容器类型
                if (compType.Name.Contains("ExpansionModule") || compType.Name.Contains("ModuleContainer"))
                {
                    hasModuleSystem = true;
                    GodHandModMain.DebugLog($"[炮塔头] 发现模块容器组件: {compType.Name}");

                    // 获取 innerContainer
                    var containerField = compType.GetField("innerContainer",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (containerField != null)
                    {
                        var container = containerField.GetValue(comp);
                        if (container is ThingOwner thingOwner)
                        {
                            GodHandModMain.DebugLog($"[炮塔头] innerContainer 有 {thingOwner.Count} 个模块");

                            // 复制列表防止遍历冲突
                            var modulesToTransfer = thingOwner.ToList();

                            foreach (Thing module in modulesToTransfer)
                            {
                                if (module != null && !module.Destroyed)
                                {
                                    // 记录模块详细信息
                                    GodHandModMain.DebugLog($"[炮塔头] 准备转移模块: {module.LabelCap}, def={module.def.defName}, type={module.GetType().Name}");

                                    // 从原容器移除（使用 TryDrop 确保正确分离）
                                    bool removed = thingOwner.Remove(module);
                                    GodHandModMain.DebugLog($"[炮塔头] 从原容器移除模块: {removed}");

                                    // 检查模块状态
                                    if (module.Destroyed)
                                    {
                                        GodHandModMain.DebugLog($"[炮塔头] 警告：模块在移除后被销毁！尝试重新创建...");
                                        // 模块销毁则重创
                                        Thing newModule = ThingMaker.MakeThing(module.def, module.Stuff);
                                        if (newModule != null)
                                        {
                                            storedModules.Add(newModule);
                                            GodHandModMain.DebugLog($"[炮塔头] 重新创建模块成功");
                                        }
                                    }
                                    else
                                    {
                                        // 直接添加到我们的存储（保留原模块实例）
                                        storedModules.Add(module);
                                        GodHandModMain.DebugLog($"[炮塔头] 成功转移模块: {module.LabelCap}");
                                    }
                                }
                            }

                            GodHandModMain.DebugLog($"[炮塔头] 模块转移完成，storedModules 现有 {storedModules.Count} 个模块");
                        }
                    }
                    else
                    {
                        GodHandModMain.DebugLog($"[炮塔头] 未找到 innerContainer 字段");
                    }

                    // 获取允许安装的模块类型和槽位配置
                    PropertyInfo propsProp = compType.GetProperty("Props");
                    FieldInfo propsField = compType.GetField("props", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (propsProp != null || propsField != null)
                    {
                        object props = propsProp != null ? propsProp.GetValue(comp) : propsField?.GetValue(comp);
                        if (props != null)
                        {
                            // 获取允许的模块列表
                            var modulesField = props.GetType().GetField("expansionModules",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (modulesField != null)
                            {
                                var modulesList = modulesField.GetValue(props) as IEnumerable<ThingDef>;
                                if (modulesList != null)
                                {
                                    allowedModuleDefs = modulesList.ToList();
                                }
                            }

                            // 获取槽位配置
                            ExtractModuleSlotData(props);
                        }
                    }

                    break;
                }
            }

            // 初始化炮管系统（残阳 TopGun_cy 兼容）
            InitializeTopGunSystem(sourceTurret);

            // 提取多炮口射击偏移
            ExtractShootOffsets(sourceTurret);

            // 检测是否是残阳防空炮塔
            DetectAirDefenseTurret(sourceTurret);

            // 提取护盾参数
            ExtractShieldParameters(sourceTurret);

            // 将炮塔模块转换为浮游炮塔
            InitializeFloatingTurrets();

            // 检查是否有护盾模块
            CheckShieldModule();
        }

        // 提取原版护盾拦截组件参数资料
        private void ExtractShieldParameters(Building sourceTurret)
        {
            if (sourceTurret == null) return;

            try
            {
                foreach (var comp in sourceTurret.AllComps)
                {
                    if (comp == null) continue;
                    var compType = comp.GetType();

                    // 检查是否是护盾拦截器组件
                    if (compType.Name.Contains("ProjectileInterceptor"))
                    {
                        GodHandModMain.DebugLog($"[炮塔头] 发现护盾组件: {compType.Name}");

                        // 标记存在护盾模块
                        hasShieldModule = true;

                        // 获取 Props
                        var propsProperty = compType.GetProperty("Props");
                        if (propsProperty != null)
                        {
                            var props = propsProperty.GetValue(comp);
                            if (props != null)
                            {
                                // 遍历继承链查找字段
                                Type currentType = props.GetType();
                                while (currentType != null)
                                {
                                    // 获取 radius
                                    var radiusField = currentType.GetField("radius", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                                    if (radiusField != null && shieldRadius <= 0)
                                    {
                                        shieldRadius = (float)radiusField.GetValue(props);
                                        GodHandModMain.DebugLog($"[炮塔头] 找到 radius={shieldRadius} in {currentType.Name}");
                                    }

                                    // 获取 color
                                    var colorField = currentType.GetField("color", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                                    if (colorField != null)
                                    {
                                        shieldColor = (Color)colorField.GetValue(props);
                                    }

                                    // 获取 hitPoints
                                    var hitPointsField = currentType.GetField("hitPoints", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                                    if (hitPointsField != null && shieldMaxHitPoints <= 0)
                                    {
                                        shieldMaxHitPoints = (int)hitPointsField.GetValue(props);
                                        GodHandModMain.DebugLog($"[炮塔头] 找到 hitPoints={shieldMaxHitPoints} in {currentType.Name}");
                                    }

                                    // 获取护盾回复间隔刻
                                    var rechargeField = currentType.GetField("rechargeHitPointsIntervalTicks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                                    if (rechargeField != null && shieldRechargeRate <= 0)
                                    {
                                        shieldRechargeRate = (int)rechargeField.GetValue(props);
                                    }

                                    currentType = currentType.BaseType;
                                }

                                // 设置默认半径
                                if (shieldRadius <= 0)
                                {
                                    shieldRadius = 4.5f; // 默认半径
                                    GodHandModMain.DebugLog($"[炮塔头] 使用默认护盾半径: {shieldRadius}");
                                }

                                // 初始化护盾HP
                                if (shieldCurrentHitPoints <= 0)
                                {
                                    shieldCurrentHitPoints = shieldMaxHitPoints;
                                }

                                GodHandModMain.DebugLog($"[炮塔头] 护盾参数: radius={shieldRadius}, color={shieldColor}, maxHP={shieldMaxHitPoints}, currentHP={shieldCurrentHitPoints}");
                            }
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 提取护盾参数失败: {ex.Message}");
            }
        }

        // 提取残阳模块槽位布局数据详细资料
        private void ExtractModuleSlotData(object props)
        {
            if (props == null) return;

            // 确保 moduleSlotData 已初始化
            if (moduleSlotData == null)
            {
                moduleSlotData = new Dictionary<int, ModuleSlotData>();
            }

            try
            {
                // 获取 ExpansionModuleSlots 字段
                var slotsField = props.GetType().GetField("ExpansionModuleSlots",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (slotsField == null)
                {
                    GodHandModMain.DebugLog($"[炮塔头] 未找到 ExpansionModuleSlots 字段");
                    return;
                }

                var slotsList = slotsField.GetValue(props) as System.Collections.IList;
                if (slotsList == null || slotsList.Count == 0)
                {
                    GodHandModMain.DebugLog($"[炮塔头] ExpansionModuleSlots 列表为空");
                    return;
                }

                GodHandModMain.DebugLog($"[炮塔头] 发现 {slotsList.Count} 个模块槽位配置");

                foreach (var slotObj in slotsList)
                {
                    if (slotObj == null) continue;

                    var slotType = slotObj.GetType();
                    ModuleSlotData slotData = new ModuleSlotData();

                    // 获取 slot ID
                    var slotIdField = slotType.GetField("slot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (slotIdField != null)
                    {
                        slotData.slot = (int)slotIdField.GetValue(slotObj);
                    }

                    // 获取 width
                    var widthField = slotType.GetField("width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (widthField != null)
                    {
                        slotData.width = (int)widthField.GetValue(slotObj);
                    }

                    // 获取 moduleOffsets 列表
                    var offsetsField = slotType.GetField("moduleOffsets", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (offsetsField != null)
                    {
                        var offsetsList = offsetsField.GetValue(slotObj) as List<Vector3>;
                        if (offsetsList != null)
                        {
                            slotData.moduleOffsets = new List<Vector3>(offsetsList);
                        }
                    }

                    // 获取槽位尺寸列表
                    var sizesField = slotType.GetField("moduleSizes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (sizesField != null)
                    {
                        var sizesList = sizesField.GetValue(slotObj) as List<float>;
                        if (sizesList != null)
                        {
                            slotData.moduleSizes = new List<float>(sizesList);
                        }
                    }

                    // 存储槽位数据
                    moduleSlotData[slotData.slot] = slotData;
                    GodHandModMain.DebugLog($"[炮塔头] 槽位 {slotData.slot}: width={slotData.width}, sizes={string.Join(",", slotData.moduleSizes)}, offsets={slotData.moduleOffsets.Count}个");
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 提取槽位数据失败: {ex.Message}");
            }
        }

        // 转化炮塔模块为独立浮游炮塔实体
        private void InitializeFloatingTurrets()
        {
            GodHandModMain.DebugLog($"[浮游炮塔] 开始初始化浮游炮塔，storedModules={storedModules?.Count ?? 0}");

            if (storedModules == null || storedModules.Count == 0)
            {
                GodHandModMain.DebugLog($"[浮游炮塔] storedModules 为空，跳过");
                return;
            }

            // 确保 floatingTurrets 列表已初始化
            if (floatingTurrets == null)
            {
                floatingTurrets = new List<FloatingTurret>();
            }

            // 列出所有模块
            foreach (var m in storedModules)
            {
                if (m != null)
                {
                    GodHandModMain.DebugLog($"[浮游炮塔] 检查模块: {m.LabelCap}, type={m.GetType().Name}, defName={m.def.defName}");
                }
            }

            var turretModules = storedModules.Where(m =>
                m != null &&
                !m.Destroyed &&
                IsShootableModule(m)
            ).ToList();

            GodHandModMain.DebugLog($"[浮游炮塔] 找到 {turretModules.Count} 个炮塔模块");

            foreach (var module in turretModules)
            {
                GodHandModMain.DebugLog($"[浮游炮塔] 正在转换模块: {module.LabelCap}");

                FloatingTurret floater = new FloatingTurret();
                floater.InitFromModule(module);
                floater.parentSlot = this;  // 设置父槽位引用（用于分弹瞄准）
                floatingTurrets.Add(floater);

                // 从普通模块列表移除（避免重复渲染）
                storedModules.Remove(module);

                GodHandModMain.DebugLog($"[浮游炮塔] 创建浮游炮塔成功: {module.LabelCap}, 现有浮游炮塔: {floatingTurrets.Count}");
            }

            // 重置模块类型缓存（性能优化）
            moduleTypeCacheBuilt = false;

            GodHandModMain.DebugLog($"[浮游炮塔] 初始化完成，floatingTurrets={floatingTurrets.Count}, 剩余storedModules={storedModules.Count}");
        }

        // 判定模块是否具备射击功能类型资料
        private bool IsShootableModule(Thing module)
        {
            if (module == null) return false;

            string typeName = module.GetType().Name;
            string defName = module.def.defName;

            // 名称包含 Turret 的模块
            if (typeName.Contains("Turret") || defName.Contains("Turret"))
                return true;

            // 检查残阳射击模块
            try
            {
                var defType = module.def.GetType();
                var projectileField = defType.GetField("moduleProjectile",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (projectileField != null)
                {
                    var projectileDef = projectileField.GetValue(module.def) as ThingDef;
                    if (projectileDef != null)
                    {
                        GodHandModMain.DebugLog($"[浮游炮塔] 模块 {defName} 有 moduleProjectile: {projectileDef.defName}");
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // 初始化残阳多炮管复合图形系统资料
        private void InitializeTopGunSystem(Building sourceTurret)
        {
            if (sourceTurretDef == null) return;

            // 使用反射获取 ModExtension
            try
            {
                // 查找残阳功能扩展
                var modExtensions = sourceTurretDef.modExtensions;
                if (modExtensions == null) return;

                foreach (var ext in modExtensions)
                {
                    if (ext == null) continue;

                    var extType = ext.GetType();
                    // 检查是否是残阳的炮塔扩展
                    if (!extType.Name.Contains("MoreFeaturesTurret") && !extType.Name.Contains("TopGun"))
                        continue;

                    GodHandModMain.DebugLog($"[炮塔头] 发现残阳炮塔扩展: {extType.Name}");

                    // 获取 topGunsData 列表
                    var topGunsDataField = extType.GetField("topGunsData",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (topGunsDataField != null)
                    {
                        var topGunsDataList = topGunsDataField.GetValue(ext) as System.Collections.IList;
                        if (topGunsDataList != null && topGunsDataList.Count > 0)
                        {
                            hasTopGunSystem = true;

                            foreach (var topGunObj in topGunsDataList)
                            {
                                if (topGunObj == null) continue;

                                var topGunType = topGunObj.GetType();
                                TopGunData gunData = new TopGunData();

                                // 提取 offset_gun
                                var offsetField = topGunType.GetField("offset_gun",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (offsetField != null)
                                {
                                    gunData.offsetGun = (Vector3)offsetField.GetValue(topGunObj);
                                }

                                // 提取 graphicData_Gun
                                var graphicField = topGunType.GetField("graphicData_Gun",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (graphicField != null)
                                {
                                    gunData.graphicDataGun = graphicField.GetValue(topGunObj) as GraphicData;
                                }

                                // 提取通电图形数据
                                var graphicEdField = topGunType.GetField("graphicData_Gun_ed",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (graphicEdField != null)
                                {
                                    gunData.graphicDataGunEd = graphicEdField.GetValue(topGunObj) as GraphicData;
                                }

                                // 提取 angle
                                var angleField = topGunType.GetField("angle",
                                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (angleField != null)
                                {
                                    gunData.angle = (int)angleField.GetValue(topGunObj);
                                }

                                topGuns.Add(gunData);
                                GodHandModMain.DebugLog($"[炮塔头] 提取炮管数据: offset={gunData.offsetGun}, hasGraphic={gunData.graphicDataGun != null}");
                            }
                        }
                    }

                    break;
                }

                // 提取 CompDrawExtra_cy 的额外图形
                if (sourceTurret != null)
                {
                    foreach (var comp in sourceTurret.AllComps)
                    {
                        if (comp == null) continue;
                        var compType = comp.GetType();

                        if (compType.Name.Contains("DrawExtra"))
                        {
                            var propsProperty = compType.GetProperty("Props");
                            if (propsProperty != null)
                            {
                                var props = propsProperty.GetValue(comp);
                                if (props != null)
                                {
                                    var graphicDataField = props.GetType().GetField("graphicData",
                                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                    if (graphicDataField != null)
                                    {
                                        extraGraphicData = graphicDataField.GetValue(props) as GraphicData;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 提取炮管数据失败: {ex.Message}");
            }
        }

        // 提取残阳交替射击偏移坐标数据分部
        private void ExtractShootOffsets(Building sourceTurret)
        {
            if (sourceTurret == null) return;

            try
            {
                // 从定义获取偏移扩展
                var gunDef = sourceTurretDef?.building?.turretGunDef;
                if (gunDef?.modExtensions == null) return;

                foreach (var ext in gunDef.modExtensions)
                {
                    if (ext == null) continue;
                    var extType = ext.GetType();

                    // 查找残阳偏移类型
                    if (extType.Name.Contains("Offset") || extType.Name.Contains("offset"))
                    {
                        // 获取 ShootOffset 字段
                        var shootOffsetField = extType.GetField("ShootOffset",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                        if (shootOffsetField != null)
                        {
                            var offsets = shootOffsetField.GetValue(ext) as IList;
                            if (offsets != null && offsets.Count > 0)
                            {
                                shootOffsets.Clear();
                                foreach (var offset in offsets)
                                {
                                    if (offset is Vector3 v3)
                                    {
                                        shootOffsets.Add(v3);
                                    }
                                }

                                if (shootOffsets.Count > 0)
                                {
                                    GodHandModMain.DebugLog($"[炮塔头] 提取多炮口偏移: {shootOffsets.Count} 个位置");
                                    foreach (var offset in shootOffsets)
                                    {
                                        GodHandModMain.DebugLog($"  - {offset}");
                                    }
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 提取多炮口偏移失败: {ex.Message}");
            }
        }

        // 获取并递增射击偏移
        public Vector3 GetNextShootOffset()
        {
            if (shootOffsets == null || shootOffsets.Count == 0)
                return Vector3.zero;

            // 获取当前偏移
            Vector3 offset = shootOffsets[currentShootOffsetIndex];

            // 递增索引（循环）
            currentShootOffsetIndex = (currentShootOffsetIndex + 1) % shootOffsets.Count;

            return offset;
        }

        // 是否有多炮口系统
        public bool HasMultipleShootOffsets => shootOffsets != null && shootOffsets.Count > 1;

        // 残阳防空功能对接
        // 检查是否为防空炮塔
        private void DetectAirDefenseTurret(Building sourceTurret)
        {
            if (sourceTurret == null) return;

            try
            {
                sourceTurretClass = sourceTurret.GetType();

                // 检查类名是否包含 AirDefense
                if (sourceTurretClass.Name.Contains("AirDefense"))
                {
                    isAirDefenseTurret = true;
                    GodHandModMain.DebugLog($"[炮塔头] 检测到残阳防空炮塔: {sourceTurretClass.Name}");

                    // 尝试获取当前的防空设置
                    var airDefenseField = sourceTurretClass.GetField("airDefense",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (airDefenseField != null)
                    {
                        airDefenseMode = (bool)airDefenseField.GetValue(sourceTurret);
                    }

                    var minPowerField = sourceTurretClass.GetField("currentMinInterceptPower",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (minPowerField != null)
                    {
                        airDefenseMinPower = (int)minPowerField.GetValue(sourceTurret);
                    }

                    var minRangeField = sourceTurretClass.GetField("currentMinInterceptRange",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (minRangeField != null)
                    {
                        airDefenseMinRange = (int)minRangeField.GetValue(sourceTurret);
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 检测防空炮塔失败: {ex.Message}");
            }
        }

        // 防空模式查找目标
        public LocalTargetInfo TryFindAirDefenseTarget()
        {
            if (!isAirDefenseTurret || !airDefenseMode)
                return LocalTargetInfo.Invalid;

            if (cachedWearer?.Map == null)
                return LocalTargetInfo.Invalid;

            try
            {
                Map map = cachedWearer.Map;
                float range = Range;

                // 扫描弹丸（复用残阳的逻辑）
                var projectiles = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
                foreach (Thing thing in projectiles)
                {
                    if (thing is Projectile projectile)
                    {
                        // 检查是否是敌对弹丸
                        if (!GenHostility.HostileTo(projectile.Launcher, cachedWearer))
                            continue;

                        // 检查距离
                        float dist = (thing.Position - cachedWearer.Position).LengthHorizontal;
                        if (dist > range)
                            continue;

                        // 检查伤害阈值
                        if (projectile.DamageAmount < airDefenseMinPower)
                            continue;

                        // 检查爆炸范围阈值
                        if (thing.def.projectile?.explosionRadius < airDefenseMinRange)
                            continue;

                        GodHandModMain.DebugLog($"[炮塔头] 防空目标: {thing.LabelCap}");
                        return new LocalTargetInfo(thing);
                    }
                }

                // 扫描天降物（如敌对运输舱）
                var skyfallers = map.listerThings.ThingsInGroup(ThingRequestGroup.Transporter);
                foreach (Thing thing in skyfallers)
                {
                    if (thing is Skyfaller skyfaller && skyfaller.ticksToImpact <= 80)
                    {
                        // 检查是否包含敌对单位
                        bool hasHostile = false;
                        foreach (Thing inner in skyfaller.innerContainer)
                        {
                            if (inner is ActiveTransporter transporter)
                            {
                                foreach (Thing content in transporter.Contents.innerContainer)
                                {
                                    if (content.Faction != null && GenHostility.HostileTo(content, cachedWearer))
                                    {
                                        hasHostile = true;
                                        break;
                                    }
                                }
                            }
                            if (hasHostile) break;
                        }

                        if (hasHostile)
                        {
                            float dist = (thing.Position - cachedWearer.Position).LengthHorizontal;
                            if (dist <= range)
                            {
                                return new LocalTargetInfo(thing);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 防空目标搜索失败: {ex.Message}");
            }

            return LocalTargetInfo.Invalid;
        }

        // 切换防空模式
        public void ToggleAirDefenseMode()
        {
            if (!isAirDefenseTurret) return;
            airDefenseMode = !airDefenseMode;
            currentTarget = LocalTargetInfo.Invalid; // 切换模式时清除当前目标
            GodHandModMain.DebugLog($"[炮塔头] 防空模式: {(airDefenseMode ? "开启" : "关闭")}");
        }

        // 渲染残阳多炮管参考
        [System.Obsolete("使用 PawnRenderNode_TurretTopGun 代替")]
        public void DrawTopGuns(Vector3 drawPos, float rotation, float turretTopDrawSize)
        {
            if (!hasTopGunSystem || topGuns == null || topGuns.Count == 0) return;

            foreach (var topGun in topGuns)
            {
                Material mat = topGun.GetMaterial();
                if (mat == null) continue;

                // 参考残阳渲染位置
                // 炮管跟随炮塔旋转
                // 固定渲染高度
                Vector3 gunPos = drawPos;

                // 旋转XZ平面
                float radians = rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);
                Vector3 rotatedOffset = new Vector3(
                    topGun.offsetGun.x * cos - topGun.offsetGun.z * sin,
                    topGun.offsetGun.y,  // Y 保持不变
                    topGun.offsetGun.x * sin + topGun.offsetGun.z * cos
                );
                gunPos += rotatedOffset;

                // 应用后坐力偏移
                if (topGun.retarderV3.z != 0 || topGun.retarderV3.x != 0)
                {
                    float retarderRad = (rotation + topGun.angle) * Mathf.Deg2Rad;
                    float retCos = Mathf.Cos(retarderRad);
                    float retSin = Mathf.Sin(retarderRad);
                    Vector3 rotatedRetarder = new Vector3(
                        topGun.retarderV3.x * retCos - topGun.retarderV3.z * retSin,
                        topGun.retarderV3.y,
                        topGun.retarderV3.x * retSin + topGun.retarderV3.z * retCos
                    );
                    gunPos += rotatedRetarder;
                }

                // 炮管旋转角度
                float finalAngle = rotation - 90f;
                Quaternion q = Quaternion.Euler(0, finalAngle, 0);

                // 旋转平面
                // 应用后坐力
                // 固定 Y 偏移
                // 绘制通电图形
                Matrix4x4 matrix = Matrix4x4.TRS(
                    gunPos + new Vector3(0, -0.2f, 0),
                    q,
                    new Vector3(turretTopDrawSize, 1f, turretTopDrawSize)
                );
                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);

                // 绘制通电发光图
                Material matEd = topGun.GetMaterialEd();
                if (matEd != null)
                {
                    Matrix4x4 matrixEd = Matrix4x4.TRS(
                        gunPos + new Vector3(0, -0.1f, 0),
                        q,
                        new Vector3(turretTopDrawSize, 1f, turretTopDrawSize)
                    );
                    Graphics.DrawMesh(MeshPool.plane10, matrixEd, matEd, 0);
                }
            }
        }

        // 绘制装饰图形
        public void DrawExtraGraphic(Vector3 drawPos, float rotation, float turretTopDrawSize)
        {
            if (extraGraphicData == null) return;

            try
            {
                Material mat = extraGraphicData.Graphic?.MatSingle;
                if (mat == null) return;

                float finalAngle = rotation - 90f;
                Quaternion q = Quaternion.Euler(0, finalAngle, 0);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    drawPos + new Vector3(0, 1.05f, 0),
                    q,
                    new Vector3(turretTopDrawSize, 1f, turretTopDrawSize)
                );
                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
            }
            catch { }
        }

        // 更新后坐力动画
        public void TickTopGuns()
        {
            if (!hasTopGunSystem || topGuns == null) return;

            foreach (var topGun in topGuns)
            {
                topGun.TickRecoil();
            }
        }

        // 触发后坐力
        public void TriggerRecoil()
        {
            if (!hasTopGunSystem || topGuns == null) return;

            foreach (var topGun in topGuns)
            {
                topGun.ApplyRecoil();
            }
        }

        // 浮游炮塔方法

        // 更新浮游炮塔
        public void TickFloatingTurrets(Pawn pawn)
        {
            if (floatingTurrets == null || floatingTurrets.Count == 0) return;

            foreach (var floater in floatingTurrets)
            {
                floater.Tick(pawn);
            }
        }

        // 绘制浮游炮塔
        public void DrawFloatingTurrets(Pawn pawn)
        {
            if (floatingTurrets == null || floatingTurrets.Count == 0) return;

            foreach (var floater in floatingTurrets)
            {
                floater.Draw(pawn);
            }
        }

        // 是否有浮游炮塔
        public bool HasFloatingTurrets => floatingTurrets != null && floatingTurrets.Count > 0;

        // 添加模块到炮塔
        public bool AddModule(Thing module)
        {
            if (module == null) return false;
            if (!hasModuleSystem) return false;
            if (!allowedModuleDefs.Contains(module.def)) return false;

            // 获取模块的槽位ID
            int moduleCase = GetModuleCase(module);

            // 检查槽位是否可用
            if (!SlotIsAvailable(moduleCase, module.def))
            {
                GodHandModMain.DebugLog($"[炮塔头] 槽位 {moduleCase} 已满，无法安装模块: {module.LabelCap}");
                return false;
            }

            storedModules.Add(module);
            GodHandModMain.DebugLog($"[炮塔头] 安装模块: {module.LabelCap} 到槽位 {moduleCase}");

            // 重置模块类型缓存（性能优化）
            moduleTypeCacheBuilt = false;

            // 重新检查护盾模块和其他效果
            CheckShieldModule();

            // 通知炮塔头重新计算叠加护盾
            parentHead?.RecalculateCombinedShield();

            return true;
        }

        // 检查槽位可用性
        public bool SlotIsAvailable(int slot, ThingDef moduleDef)
        {
            if (moduleDef == null) return false;
            if (!hasModuleSystem) return false;

            // 检查模块是否在允许列表中
            if (!allowedModuleDefs.Contains(moduleDef))
            {
                return false;
            }

            // 构建每个槽位的剩余容量字典
            Dictionary<int, int> slotCapacity = new Dictionary<int, int>();

            // 获取槽位初始容量
            if (moduleSlotData != null)
            {
                foreach (var kvp in moduleSlotData)
                {
                    slotCapacity[kvp.Key] = kvp.Value.width;
                }
            }

            // 检查目标槽位是否存在
            if (!slotCapacity.ContainsKey(slot))
            {
                // 默认允许
                // 减少槽位容量
                slotCapacity[slot] = 1;
            }

            // 扣除已装模块容量
            if (storedModules != null)
            {
                foreach (Thing installedModule in storedModules)
                {
                    if (installedModule == null) continue;
                    int installedSlot = GetModuleCase(installedModule);
                    if (slotCapacity.ContainsKey(installedSlot))
                    {
                        slotCapacity[installedSlot]--;
                    }
                }
            }

            // 浮游炮塔也占用槽位
            if (floatingTurrets != null)
            {
                foreach (var floater in floatingTurrets)
                {
                    if (floater?.sourceModule == null) continue;
                    int floaterSlot = GetModuleCase(floater.sourceModule);
                    if (slotCapacity.ContainsKey(floaterSlot))
                    {
                        slotCapacity[floaterSlot]--;
                    }
                }
            }

            // 检查槽位是否还有容量
            if (slotCapacity[slot] <= 0)
            {
                return false;
            }

            // 检查模块重复安装
            bool canRepeated = GetModuleCanRepeated(moduleDef);
            if (!canRepeated)
            {
                // 检查是否已经安装了相同 ModuleTag 的模块
                string moduleTag = GetModuleTag(moduleDef);
                if (!string.IsNullOrEmpty(moduleTag))
                {
                    foreach (Thing installedModule in storedModules ?? Enumerable.Empty<Thing>())
                    {
                        if (installedModule == null) continue;
                        string installedTag = GetModuleTag(installedModule.def);
                        if (installedTag == moduleTag)
                        {
                            return false; // 重复标签禁止安装
                        }
                    }
                }
            }

            return true;
        }

        // 获取重复安装属性
        private bool GetModuleCanRepeated(ThingDef moduleDef)
        {
            if (moduleDef == null) return true;
            try
            {
                var field = moduleDef.GetType().GetField("canRepeated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return (bool)field.GetValue(moduleDef);
            }
            catch { }
            return true;
        }

        // 获取模块标签
        private string GetModuleTag(ThingDef moduleDef)
        {
            if (moduleDef == null) return null;
            try
            {
                var prop = moduleDef.GetType().GetProperty("ModuleTag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null) return prop.GetValue(moduleDef) as string;
                var field = moduleDef.GetType().GetField("ModuleTag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(moduleDef) as string;
            }
            catch { }
            return null;
        }

        // 获取槽位剩余容量
        public int GetSlotRemainingCapacity(int slot)
        {
            int capacity = GetSlotTotalCapacity(slot);
            if (storedModules != null)
            {
                foreach (Thing module in storedModules)
                {
                    if (module != null && GetModuleCase(module) == slot) capacity--;
                }
            }
            if (floatingTurrets != null)
            {
                foreach (var floater in floatingTurrets)
                {
                    if (floater?.sourceModule != null && GetModuleCase(floater.sourceModule) == slot) capacity--;
                }
            }
            return Math.Max(0, capacity);
        }

        // 获取槽位总容量
        public int GetSlotTotalCapacity(int slot)
        {
            if (moduleSlotData != null && moduleSlotData.TryGetValue(slot, out ModuleSlotData slotData))
            {
                return slotData.width;
            }
            return 1;
        }
        public Thing RemoveModule(int index)
        {
            if (index < 0 || index >= storedModules.Count) return null;

            Thing module = storedModules[index];
            storedModules.RemoveAt(index);
            GodHandModMain.DebugLog($"[炮塔头] 移除模块: {module.LabelCap}");

            // 重新检查护盾模块和重新计算
            CheckShieldModule();
            parentHead?.RecalculateCombinedShield();

            return module;
        }

        // 移除模块实例
        public bool RemoveModule(Thing module)
        {
            if (module == null || storedModules == null) return false;

            if (storedModules.Remove(module))
            {
                GodHandModMain.DebugLog($"[炮塔头] 移除模块: {module.LabelCap}");

                // 重新检查护盾模块和重新计算
                CheckShieldModule();
                parentHead?.RecalculateCombinedShield();

                return true;
            }
            return false;
        }

        // 获取模块数量
        public int ModuleCount => storedModules?.Count ?? 0;

        // 是否有模块系统
        public bool HasModuleSystem => hasModuleSystem;

        // 模块旋转角度缓存（用于能量护盾等旋转模块）
        private Dictionary<int, float> moduleRotations = new Dictionary<int, float>();

        // 绘制模块
        public void DrawModules(Vector3 drawPos, float rotation)
        {
            if (!hasModuleSystem || storedModules == null || storedModules.Count == 0) return;

            // 记录每个槽位已绘制的模块数量（用于获取正确的尺寸索引）
            Dictionary<int, int> slotIndexCounters = new Dictionary<int, int>();

            for (int i = 0; i < storedModules.Count; i++)
            {
                Thing module = storedModules[i];
                if (module == null) continue;

                // 反射获取图形
                GraphicData graphicData = GetModuleGraphicData(module);
                if (graphicData == null) continue;

                Material mat = graphicData.Graphic?.MatSingle;
                if (mat == null) continue;

                // 获取模块的槽位ID（moduleCase）
                int moduleCase = GetModuleCase(module);

                // 获取当前槽位的索引（同一槽位的第几个模块）
                if (!slotIndexCounters.ContainsKey(moduleCase))
                {
                    slotIndexCounters[moduleCase] = 0;
                }
                int slotIndex = slotIndexCounters[moduleCase];
                slotIndexCounters[moduleCase]++;

                // 获取尺寸倍率（从槽位配置中获取）
                float sizeMultiplier = 1f;
                Vector3 slotOffset = Vector3.zero;
                if (moduleSlotData != null && moduleSlotData.TryGetValue(moduleCase, out ModuleSlotData slotData))
                {
                    sizeMultiplier = slotData.GetSizeMultiplier(slotIndex);
                    slotOffset = slotData.GetOffset(slotIndex);
                }
                else
                {
                    // 无数据时使用默认尺寸
                    // 护盾模块倍率参考
                    // 其他模块使用较小的默认值以避免尺寸过大
                    string defName = module.def.defName;
                    if (defName.Contains("Shield") || defName.Contains("EnergyShield") || moduleCase == 3)
                    {
                        sizeMultiplier = 0.35f; // 护盾模块默认倍率
                    }
                    else
                    {
                        sizeMultiplier = 0.5f; // 其他模块默认倍率
                    }
                }

                // 获取模块旋转角度（能量护盾等会自动旋转）
                float moduleAngle = rotation;
                if (moduleRotations.TryGetValue(module.thingIDNumber, out float savedAngle))
                {
                    moduleAngle = savedAngle;
                }

                // 模块位置偏移
                // 应用槽位偏移
                Vector3 modulePos = drawPos;
                // 应用槽位位置偏移（根据炮塔旋转）
                Vector3 rotatedSlotOffset = Quaternion.Euler(0, rotation, 0) * new Vector3(
                    slotOffset.x + graphicData.drawOffset.x,
                    0f,
                    slotOffset.z + graphicData.drawOffset.z
                );
                modulePos += rotatedSlotOffset;
                modulePos.y += 1.15f + slotOffset.y + (i * 0.01f);

                // 模块尺寸 = 基础尺寸 * 槽位倍率
                Vector2 drawSize = graphicData.drawSize;
                if (drawSize.x <= 0) drawSize = Vector2.one;
                float finalSizeX = drawSize.x * sizeMultiplier;
                float finalSizeY = drawSize.y * sizeMultiplier;

                // 旋转
                Quaternion rot = Quaternion.Euler(0f, moduleAngle - 90f, 0f);

                Matrix4x4 matrix = Matrix4x4.TRS(modulePos, rot, new Vector3(finalSizeX, 1f, finalSizeY));
                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);

                // 绘制通电时的发光图形
                GraphicData graphicDataEd = GetModuleGraphicDataEd(module);
                if (graphicDataEd != null)
                {
                    Material matEd = graphicDataEd.Graphic?.MatSingle;
                    if (matEd != null)
                    {
                        Matrix4x4 matrixEd = Matrix4x4.TRS(
                            modulePos + new Vector3(0, 0.01f, 0),
                            rot,
                            new Vector3(finalSizeX, 1f, finalSizeY)
                        );
                        Graphics.DrawMesh(MeshPool.plane10, matrixEd, matEd, 0);
                    }
                }
            }
        }

        // 获取模块槽位号
        private int GetModuleCase(Thing module)
        {
            if (module == null) return 0;

            try
            {
                // 尝试从模块类型获取 moduleCase 属性
                var prop = module.GetType().GetProperty("moduleCase",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                {
                    return (int)prop.GetValue(module);
                }

                // 尝试从 def 获取 moduleCase
                var defType = module.def.GetType();
                var field = defType.GetField("moduleCase",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return (int)field.GetValue(module.def);
                }
            }
            catch { }

            return 0; // 默认槽位0
        }

        // 获取模块图形数据
        private GraphicData GetModuleGraphicData(Thing module)
        {
            if (module == null) return null;

            try
            {
                // 尝试获取 moduleGraphic 属性（残阳模块）
                var prop = module.GetType().GetProperty("moduleGraphic",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                {
                    return prop.GetValue(module) as GraphicData;
                }

                // 从定义获取图形数据
                var defType = module.def.GetType();
                var field = defType.GetField("moduleGraphicData",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(module.def) as GraphicData;
                }
            }
            catch { }

            // 回退到默认 graphicData
            return module.def.graphicData;
        }

        // 获取通电图形
        private GraphicData GetModuleGraphicDataEd(Thing module)
        {
            if (module == null) return null;

            try
            {
                // 尝试获取 moduleGraphic_ed 属性
                var prop = module.GetType().GetProperty("moduleGraphic_ed",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                {
                    return prop.GetValue(module) as GraphicData;
                }

                // 尝试从 def 获取
                var defType = module.def.GetType();
                var field = defType.GetField("moduleGraphicData_ed",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(module.def) as GraphicData;
                }
            }
            catch { }

            return null;
        }

        // 执行模块旋转动画计算逻辑资料
        public void TickModuleAnimations()
        {
            if (!hasModuleSystem || storedModules == null) return;

            foreach (var module in storedModules)
            {
                if (module == null) continue;

                string typeName = module.GetType().Name;

                // 能量护盾模块自动旋转
                if (typeName.Contains("EnergyShield") || module.def.defName.Contains("Shield"))
                {
                    if (!moduleRotations.ContainsKey(module.thingIDNumber))
                    {
                        moduleRotations[module.thingIDNumber] = 0f;
                    }
                    moduleRotations[module.thingIDNumber] += 1f;
                    if (moduleRotations[module.thingIDNumber] >= 360f)
                    {
                        moduleRotations[module.thingIDNumber] = 0f;
                    }
                }
            }
        }

        // 模块效果适配
        // 将残阳科技等模块的炮塔功能适配到 Pawn 身上

        // 模块效果冷却计时
        private Dictionary<string, int> moduleCooldowns = new Dictionary<string, int>();

        // 构建模块类型分类缓存加快查询
        private void BuildModuleTypeCache()
        {
            moduleEffectTypeCache.Clear();
            if (storedModules == null) return;

            foreach (var module in storedModules)
            {
                if (module == null) continue;

                string defName = module.def.defName;
                string typeName = module.GetType().Name;
                ModuleEffectType effectType = ModuleEffectType.None;

                // 按优先级判断模块类型
                if (typeName.Contains("SeriesConnection") || defName.Contains("SeriesConnection") ||
                    defName.Contains("Power") || defName.Contains("Generator"))
                {
                    effectType = ModuleEffectType.Energy;
                }
                else if (typeName.Contains("AutomaticRepair") || defName.Contains("Repair") || defName.Contains("AutoRepair"))
                {
                    effectType = ModuleEffectType.Repair;
                }
                else if (typeName.Contains("EnergyShield") || defName.Contains("Shield"))
                {
                    effectType = ModuleEffectType.Shield;
                }
                else if (typeName.Contains("EMPDefend") || defName.Contains("EMP"))
                {
                    effectType = ModuleEffectType.EMP;
                }
                else if (typeName.Contains("Firefighting") || defName.Contains("Fire") || defName.Contains("Extinguish"))
                {
                    effectType = ModuleEffectType.Firefighting;
                }
                else if (typeName.Contains("LaserADS") || defName.Contains("ADS") || defName.Contains("PointDefense"))
                {
                    effectType = ModuleEffectType.LaserADS;
                }
                else if (typeName.Contains("Turret") && !defName.Contains("Module"))
                {
                    effectType = ModuleEffectType.Turret;
                }

                moduleEffectTypeCache[module.thingIDNumber] = effectType;
            }
            moduleTypeCacheBuilt = true;
        }

        // 分发执行各模块特定效果逻辑库
        public void ExecuteModuleEffectsOptimized(Pawn pawn)
        {
            if (!hasModuleSystem || storedModules == null || storedModules.Count == 0) return;
            if (pawn == null || pawn.Dead) return;

            // 确保类型缓存已构建
            if (!moduleTypeCacheBuilt)
            {
                BuildModuleTypeCache();
            }

            foreach (Thing module in storedModules)
            {
                if (module == null) continue;

                int moduleId = module.thingIDNumber;
                string moduleKey = $"{module.def.defName}_{moduleId}";

                // 检查冷却（使用TryGetValue避免双重查找）
                if (moduleCooldowns.TryGetValue(moduleKey, out int cooldown) && cooldown > 0)
                {
                    moduleCooldowns[moduleKey] = cooldown - 5; // 补偿执行间隔
                    continue;
                }

                // 使用缓存的类型快速分发
                if (!moduleEffectTypeCache.TryGetValue(moduleId, out ModuleEffectType effectType))
                {
                    effectType = ModuleEffectType.None;
                }

                switch (effectType)
                {
                    case ModuleEffectType.Energy:
                        ApplyEnergyEffect(pawn, 0.01f);
                        moduleCooldowns[moduleKey] = 60;
                        break;
                    case ModuleEffectType.Repair:
                        ApplyHealingEffect(pawn, 0.5f);
                        moduleCooldowns[moduleKey] = 250;
                        break;
                    case ModuleEffectType.EMP:
                        ApplyEMPDefenseEffect(pawn);
                        moduleCooldowns[moduleKey] = 60;
                        break;
                    case ModuleEffectType.Firefighting:
                        ApplyFirefightingEffect(pawn);
                        moduleCooldowns[moduleKey] = 120;
                        break;
                    case ModuleEffectType.LaserADS:
                        TryCallModuleExecute(module, pawn);
                        break;
                    case ModuleEffectType.Shield:
                    case ModuleEffectType.Turret:
                    case ModuleEffectType.None:
                        // 这些类型不需要在这里处理
                        break;
                }
            }
        }

        // 执行模块效果适配原版生物逻辑
        public void ExecuteModuleEffects(Pawn pawn)
        {
            if (!hasModuleSystem || storedModules == null || storedModules.Count == 0) return;
            if (pawn == null || pawn.Dead) return;

            foreach (Thing module in storedModules)
            {
                if (module == null) continue;

                string defName = module.def.defName;
                string typeName = module.GetType().Name;
                string moduleKey = $"{defName}_{module.thingIDNumber}";

                // 检查冷却
                if (moduleCooldowns.ContainsKey(moduleKey) && moduleCooldowns[moduleKey] > 0)
                {
                    moduleCooldowns[moduleKey]--;
                    continue;
                }

                // 根据模块类型应用不同效果
                ApplyModuleEffect(pawn, module, defName, typeName, moduleKey);
            }
        }

        // 应用模块特有效果到生物个体资料
        private void ApplyModuleEffect(Pawn pawn, Thing module, string defName, string typeName, string moduleKey)
        {
            // === 串联/发电模块 → 恢复饱食和休息 ===
            // 转化为生物能量
            if (typeName.Contains("SeriesConnection") || defName.Contains("SeriesConnection") ||
                defName.Contains("Power") || defName.Contains("Generator"))
            {
                ApplyEnergyEffect(pawn, 0.01f); // 恢复1%饱食和休息
                moduleCooldowns[moduleKey] = 60; // 1秒冷却
                return;
            }

            // === 自动修复模块 → 治疗 Pawn ===
            // 纳米技术修复组织
            if (typeName.Contains("AutomaticRepair") || defName.Contains("Repair") || defName.Contains("AutoRepair"))
            {
                ApplyHealingEffect(pawn, 0.5f); // 治疗半点
                moduleCooldowns[moduleKey] = 250; // 约4秒冷却
                return;
            }

            // 能量护盾模块处理
            // 护盾由系统统一处理
            // 查看独立护盾逻辑
            if (typeName.Contains("EnergyShield") || defName.Contains("Shield"))
            {
                return; // 护盾跳过
            }

            // === EMP防护模块 → 清除电击状态 ===
            // 电磁屏蔽保护
            if (typeName.Contains("EMPDefend") || defName.Contains("EMP"))
            {
                ApplyEMPDefenseEffect(pawn);
                moduleCooldowns[moduleKey] = 60; // 1秒检查
                return;
            }

            // === 消防模块 → 熄灭火焰 ===
            // 自动喷射灭火剂
            if (typeName.Contains("Firefighting") || defName.Contains("Fire") || defName.Contains("Extinguish"))
            {
                ApplyFirefightingEffect(pawn);
                moduleCooldowns[moduleKey] = 120; // 2秒冷却
                return;
            }

            // 激光防空模块处理
            if (typeName.Contains("LaserADS") || defName.Contains("ADS") || defName.Contains("PointDefense"))
            {
                // 调用残阳模块方法
                // 复用残阳逻辑
                // 不设冷却由模块控制
                TryCallModuleExecute(module, pawn);
                // 模块周期逻辑控制
                return;
            }

            // 自动攻击模块处理
            // 额外攻击由主系统处理
            // 被主系统接管
            if (typeName.Contains("Turret") && !defName.Contains("Module"))
            {
                // 主系统接管模块功能
                return;
            }
        }

        // 恢复生物个体能量与休息数值资料
        private void ApplyEnergyEffect(Pawn pawn, float amount)
        {
            // 恢复饱食
            if (pawn.needs?.food != null)
            {
                pawn.needs.food.CurLevelPercentage = Mathf.Min(1f, pawn.needs.food.CurLevelPercentage + amount);
            }

            // 恢复休息
            if (pawn.needs?.rest != null)
            {
                pawn.needs.rest.CurLevelPercentage = Mathf.Min(1f, pawn.needs.rest.CurLevelPercentage + amount);
            }
        }

        // 执行纳米修复技术治疗生物损伤库
        private void ApplyHealingEffect(Pawn pawn, float amount)
        {
            if (pawn.health?.hediffSet == null) return;

            // 优先治疗最严重的伤口
            var injuries = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(h => h.Severity > 0)
                .OrderByDescending(h => h.Severity)
                .ToList();

            if (injuries.Count > 0)
            {
                var injury = injuries.First();
                injury.Heal(amount);
            }
        }

        // 护盾由主系统管理
        // 详见护盾方法
        // 护盾显示材质
        [Unsaved] private static Material forceFieldMat;
        [Unsaved] private static readonly MaterialPropertyBlock shieldMatPropertyBlock = new MaterialPropertyBlock();

        // 绘制护盾球体视觉反馈效果分部
        public void DrawShieldEffect(Vector3 drawPos)
        {
            // 只有安装了护盾模块且护盾有效时才绘制
            if (!hasShieldModule || shieldCurrentHitPoints <= 0) return;

            // 使用原版的 ForceField 贴图
            if (forceFieldMat == null)
            {
                forceFieldMat = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);
            }

            // 使用从源炮塔提取的护盾参数
            float radius = shieldRadius;

            // 护盾透明度（根据 HP 百分比）
            float hpPercent = (float)shieldCurrentHitPoints / shieldMaxHitPoints;
            float alpha = 0.2f + 0.5f * hpPercent;
            Color drawColor = new Color(shieldColor.r, shieldColor.g, shieldColor.b, alpha);

            shieldMatPropertyBlock.SetColor(ShaderPropertyIDs.Color, drawColor);

            Vector3 shieldPos = drawPos;
            shieldPos.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);

            // 原版缩放系数
            Matrix4x4 matrix = Matrix4x4.TRS(shieldPos, Quaternion.identity,
                new Vector3(radius * 2f * 1.1601562f, 1f, radius * 2f * 1.1601562f));

            Graphics.DrawMesh(MeshPool.plane10, matrix, forceFieldMat, 0, null, 0, shieldMatPropertyBlock);
        }

        // 检测模块变更并同步护盾参数库
        public void CheckShieldModule()
        {
            hasShieldModule = false;
            if (storedModules == null || storedModules.Count == 0)
            {
                GodHandModMain.DebugLog($"[炮塔头] CheckShieldModule: 无模块");
                return;
            }

            GodHandModMain.DebugLog($"[炮塔头] CheckShieldModule: 检查 {storedModules.Count} 个模块");

            foreach (var module in storedModules)
            {
                if (module == null) continue;
                string defName = module.def.defName;
                string typeName = module.GetType().Name;

                GodHandModMain.DebugLog($"[炮塔头] 检查模块: def={defName}, type={typeName}");

                if (defName.Contains("Shield") || defName.Contains("EnergyShield") ||
                    typeName.Contains("Shield") || typeName.Contains("EnergyShield"))
                {
                    hasShieldModule = true;

                    // 尝试从模块的 def 中提取护盾参数
                    ExtractShieldParamsFromModule(module);

                    // 初始化护盾HP
                    if (shieldCurrentHitPoints <= 0)
                    {
                        shieldCurrentHitPoints = shieldMaxHitPoints;
                    }
                    GodHandModMain.DebugLog($"[炮塔头] 发现护盾模块: {module.LabelCap}, maxHP={shieldMaxHitPoints}, radius={shieldRadius}");
                    break;
                }
            }

            GodHandModMain.DebugLog($"[炮塔头] CheckShieldModule 完成: hasShieldModule={hasShieldModule}");
        }

        // 从特定模块定义获取护盾性能资料
        private void ExtractShieldParamsFromModule(Thing module)
        {
            if (module == null) return;

            try
            {
                // 获取模块定义属性
                var defProperty = module.GetType().GetProperty("_def", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (defProperty != null)
                {
                    var moduleDef = defProperty.GetValue(module);
                    if (moduleDef != null)
                    {
                        var defType = moduleDef.GetType();

                        // 尝试获取护盾相关参数
                        var radiusField = defType.GetField("EffectiveRange", BindingFlags.Instance | BindingFlags.Public);
                        if (radiusField != null)
                        {
                            shieldRadius = (float)radiusField.GetValue(moduleDef);
                            GodHandModMain.DebugLog($"[炮塔头] 从模块提取护盾半径: {shieldRadius}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[炮塔头] 提取模块护盾参数失败: {ex.Message}");
            }
        }

        // 执行护盾能量自然恢复逻辑流程
        public void TickShield()
        {
            if (!hasShieldModule) return;

            // 恢复护盾HP
            if (shieldCurrentHitPoints < shieldMaxHitPoints && shieldRechargeRate > 0)
            {
                if (Find.TickManager.TicksGame % shieldRechargeRate == 0)
                {
                    shieldCurrentHitPoints++;
                }
            }
        }

        // 处理护盾承载损伤扣除实时资料
        public void DamageShield(int damage)
        {
            if (!hasShieldModule) return;
            shieldCurrentHitPoints = Mathf.Max(0, shieldCurrentHitPoints - damage);
        }

        // 清除生物个体异常电击状态库说明
        private void ApplyEMPDefenseEffect(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null) return;

            // 移除电击相关的 Hediff
            var stunHediffs = pawn.health.hediffSet.hediffs
                .Where(h => h.def.defName.Contains("EMP") || h.def.defName.Contains("Stun"))
                .ToList();

            foreach (var hediff in stunHediffs)
            {
                pawn.health.RemoveHediff(hediff);
                GodHandModMain.DebugLog($"[模块效果] EMP防护移除了 {pawn.LabelCap} 的 {hediff.def.label}");
            }
        }

        // 自动扑灭生物及周边火焰任务说明
        private void ApplyFirefightingEffect(Pawn pawn)
        {
            // 熄灭 Pawn 身上的火
            var fire = pawn.GetAttachment(ThingDefOf.Fire);
            if (fire != null)
            {
                fire.Destroy();
                GodHandModMain.DebugLog($"[模块效果] 消防模块熄灭了 {pawn.LabelCap} 身上的火焰");
            }

            // 也熄灭周围1格内的火焰
            if (pawn.Spawned && pawn.Map != null)
            {
                var nearbyFires = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, 2f, false)
                    .Where(t => t.def == ThingDefOf.Fire)
                    .ToList();

                foreach (var nearFire in nearbyFires)
                {
                    if (!nearFire.Destroyed)
                    {
                        nearFire.Destroy();
                    }
                }
            }
        }

        // 转发激光拦截模块核心执行任务库
        private void TryCallModuleExecute(Thing module, Pawn pawn)
        {
            if (module == null || pawn == null || !pawn.Spawned || pawn.Map == null) return;

            try
            {
                var defType = module.def.GetType();

                // 从 Def 读取参数
                float effectiveRange = GetFieldValue<float>(defType, module.def, "EffectiveRange", 16f);
                float maxSpeed = GetFieldValue<float>(defType, module.def, "maxInterceptSpeed", 300f);
                float interceptDamage = GetFieldValue<float>(defType, module.def, "InterceptOfDamage", 100f);

                // 模块位置（用于特效）
                Vector3 modulePos = pawn.DrawPos + pawn.Drawer.renderer.BaseHeadOffsetAt(pawn.Rotation);

                // 扫描弹丸（残阳的核心逻辑）
                var projectiles = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
                foreach (var thing in projectiles)
                {
                    if (!(thing is Projectile proj)) continue;
                    if (!GenHostility.HostileTo(proj.Launcher, pawn)) continue;

                    float dist = (thing.Position - pawn.Position).LengthHorizontal;
                    if (dist > effectiveRange) continue;

                    float speed = thing.def.projectile?.speed ?? 0f;
                    if (speed <= 0f || speed > maxSpeed) continue;

                    // 调用残阳连线特效
                    TryCallSunsetConnectingLine(modulePos, thing.DrawPos, pawn.Map);
                    FleckMaker.Static(thing.DrawPos, pawn.Map, FleckDefOf.ShotHit_Dirt, 1.5f);

                    // 残阳的 stackCount 修复
                    if (proj.stackCount == 1 && proj.HitPoints < 1)
                    {
                        proj.stackCount = proj.DamageAmount;
                        proj.HitPoints = 100;
                    }

                    // 伤害处理
                    if ((float)proj.DamageAmount > interceptDamage && (float)proj.stackCount > interceptDamage)
                    {
                        proj.stackCount -= (int)interceptDamage;
                        break;
                    }

                    // 销毁
                    if (!proj.Destroyed) proj.Destroy();
                    break;
                }
            }
            catch { }
        }

        private T GetFieldValue<T>(Type type, object obj, string name, T defaultVal)
        {
            try
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return (T)Convert.ChangeType(field.GetValue(obj), typeof(T));
            }
            catch { }
            return defaultVal;
        }

        // 残阳连线方法缓存
        [Unsaved] private static MethodInfo sunsetConnectingLineMethod;
        [Unsaved] private static bool sunsetMethodSearched = false;
        [Unsaved] private static FleckDef sunsetLaserFleckDef;

        // 绘制激光连线特效交互逻辑分部
        private void TryCallSunsetConnectingLine(Vector3 start, Vector3 end, Map map)
        {
            if (map == null) return;

            // 查找残阳的方法
            if (!sunsetMethodSearched)
            {
                sunsetMethodSearched = true;
                try
                {
                    var sunsetType = GenTypes.GetTypeInAnyAssembly("SUNSET3.FleckMaker_cy");
                    if (sunsetType != null)
                    {
                        sunsetConnectingLineMethod = sunsetType.GetMethod("ConnectingLine",
                            BindingFlags.Static | BindingFlags.Public);

                        // 获取残阳的激光 FleckDef
                        sunsetLaserFleckDef = DefDatabase<FleckDef>.GetNamedSilentFail("Laser_ADS_cy");
                    }
                }
                catch { }
            }

            // 调用残阳联动方法
            if (sunsetConnectingLineMethod != null && sunsetLaserFleckDef != null)
            {
                try
                {
                    // 残阳连线法缓存
                    // 残阳联动调用
                    // 连线方法参数参考
                    Color laserColor = new Color(0.92f, 0.4f, 0.9f); // 残阳的 edgeColor
                    sunsetConnectingLineMethod.Invoke(null, new object[] { start, end, sunsetLaserFleckDef, map, (Color?)laserColor, 0.11f });
                }
                catch { }
            }
        }

        // 获取模块功能描述
        public string GetModuleEffectsDescription()
        {
            if (!hasModuleSystem || storedModules == null || storedModules.Count == 0)
                return null;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("安装的模块:");

            foreach (Thing module in storedModules)
            {
                if (module == null) continue;

                string effectDesc = GetModuleEffectDescription(module);
                sb.AppendLine($"  - {module.LabelCap}{(string.IsNullOrEmpty(effectDesc) ? "" : $" ({effectDesc})")}");
            }

            return sb.ToString().TrimEnd();
        }

        // 获取特定模块描述
        private string GetModuleEffectDescription(Thing module)
        {
            string defName = module.def.defName;
            string typeName = module.GetType().Name;

            if (typeName.Contains("SeriesConnection") || defName.Contains("Power") || defName.Contains("Generator"))
                return "恢复饱食和休息";

            if (typeName.Contains("AutomaticRepair") || defName.Contains("Repair"))
                return "自动治疗伤口";

            if (typeName.Contains("EnergyShield") || defName.Contains("Shield"))
                return "能量护盾减伤";

            if (typeName.Contains("EMPDefend") || defName.Contains("EMP"))
                return "EMP电击防护";

            if (typeName.Contains("Firefighting") || defName.Contains("Fire"))
                return "自动灭火";

            if (typeName.Contains("LaserADS") || defName.Contains("ADS"))
                return "激光拦截弹药";

            if (typeName.Contains("Turret"))
                return "额外炮塔";

            return "未知效果";
        }
    }
}