using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using RimWorld;

namespace GodHandMod
{
    // 浮游炮塔围绕瞄准攻击
    [StaticConstructorOnStartup]
    public class FloatingTurret : IExposable
    {
        #region 字段

        public Thing sourceModule;             // 源模块
        public GraphicData graphicData;        // 图形数据
        public GraphicData graphicDataEd;      // 通电图形

        // 父槽位引用（用于分弹瞄准）
        [Unsaved] public TurretSlot parentSlot;

        // 位置和运动
        public Vector3 orbitOffset;            // 相对于 Pawn 的轨道偏移
        public float orbitAngle;               // 当前轨道角度
        public float orbitRadius = 1.5f;       // 轨道半径
        public float orbitSpeed = 0.5f;        // 同步位置每刻更新
        public float bobOffset;                // 上下浮动偏移
        public float bobPhase;                 // 浮动相位

        // 攻击
        public float curRotation;              // 当前朝向角度
        public LocalTargetInfo currentTarget = LocalTargetInfo.Invalid;
        public int burstCooldownTicks;         // 冷却
        public int burstWarmupTicks;           // 预热
        public int burstShotsLeft;             // 连发剩余次数
        public int ticksBetweenShots = 6;      // 连发间隔（tick）
        public int nextShotTick;               // 下一发的时间

        // 武器属性（从模块提取）
        public float range = 20f;
        public float warmupTime = 0.5f;
        public float cooldownTime = 1f;
        public int burstCount = 1;
        public ThingDef projectileDef;
        public SoundDef soundCast;  // 开火音效

        // 缓存
        [Unsaved] private Material cachedMat;
        [Unsaved] private static Material laserLineMat;

        // 推进火焰计时
        private int thrusterFlamePhase = 0;

        // 推进火焰材质缓存
        [Unsaved] private static Material thrusterFlameCoreMat;
        [Unsaved] private static Material thrusterFlameOuterMat;
        [Unsaved] private static Material thrusterGlowMat;

        #endregion

        #region 静态材质属性

        // 获取激光瞄准线材质（红色）
        private static Material LaserLineMat
        {
            get
            {
                if (laserLineMat == null)
                {
                    laserLineMat = MaterialPool.MatFrom(GenDraw.LineTexPath,
                        ShaderDatabase.Transparent, new Color(1f, 0.3f, 0.3f, 0.8f));
                }
                return laserLineMat;
            }
        }

        // 推进火焰材质
        private static Material ThrusterFlameCoreMat
        {
            get
            {
                if (thrusterFlameCoreMat == null)
                {
                    thrusterFlameCoreMat = MaterialPool.MatFrom("Things/Mote/LightningGlow",
                        ShaderDatabase.MoteGlow, new Color(0.8f, 0.9f, 1f, 0.9f));
                }
                return thrusterFlameCoreMat;
            }
        }

        // 获取推进火焰外层材质（橙黄色）
        private static Material ThrusterFlameOuterMat
        {
            get
            {
                if (thrusterFlameOuterMat == null)
                {
                    thrusterFlameOuterMat = MaterialPool.MatFrom("Things/Mote/LightningGlow",
                        ShaderDatabase.MoteGlow, new Color(1f, 0.5f, 0.1f, 0.6f));
                }
                return thrusterFlameOuterMat;
            }
        }

        // 获取地面光晕材质
        private static Material ThrusterGlowMat
        {
            get
            {
                if (thrusterGlowMat == null)
                {
                    thrusterGlowMat = MaterialPool.MatFrom("Things/Mote/LightningGlow",
                        ShaderDatabase.MoteGlow, new Color(1f, 0.7f, 0.3f, 0.4f));
                }
                return thrusterGlowMat;
            }
        }

        #endregion

        #region 构造函数

        public FloatingTurret()
        {
            // 随机初始化轨道参数
            orbitAngle = Rand.Range(0f, 360f);
            orbitRadius = Rand.Range(1.2f, 2.0f);
            orbitSpeed = Rand.Range(0.3f, 0.8f) * (Rand.Bool ? 1f : -1f);
            bobPhase = Rand.Range(0f, Mathf.PI * 2f);
        }

        #endregion

        #region 序列化

        public void ExposeData()
        {
            // 保存源模块数据
            Scribe_Deep.Look(ref sourceModule, "sourceModule");

            // 保存轨道参数
            Scribe_Values.Look(ref orbitAngle, "orbitAngle");
            Scribe_Values.Look(ref orbitRadius, "orbitRadius");
            Scribe_Values.Look(ref orbitSpeed, "orbitSpeed");
            Scribe_Values.Look(ref bobPhase, "bobPhase");

            // 保存攻击状态
            Scribe_Values.Look(ref curRotation, "curRotation");
            Scribe_Values.Look(ref burstCooldownTicks, "burstCooldownTicks");
            Scribe_Values.Look(ref burstWarmupTicks, "burstWarmupTicks");
            Scribe_TargetInfo.Look(ref currentTarget, "currentTarget");

            // 保存武器属性
            Scribe_Values.Look(ref range, "range", 20f);
            Scribe_Values.Look(ref warmupTime, "warmupTime", 0.5f);
            Scribe_Values.Look(ref cooldownTime, "cooldownTime", 1f);
            Scribe_Values.Look(ref burstCount, "burstCount", 1);
            Scribe_Defs.Look(ref projectileDef, "projectileDef");

            // 加载后重建图形数据
            if (Scribe.mode == LoadSaveMode.PostLoadInit && sourceModule != null)
            {
                RebuildGraphicData();
            }
        }

        #endregion

        #region 初始化

        // 从源模块重建图形数据
        private void RebuildGraphicData()
        {
            if (sourceModule == null) return;

            try
            {
                var moduleType = sourceModule.GetType();
                var graphicProp = moduleType.GetProperty("moduleGraphic",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (graphicProp != null)
                {
                    graphicData = graphicProp.GetValue(sourceModule) as GraphicData;
                }
                else
                {
                    var defType = sourceModule.def.GetType();
                    var field = defType.GetField("moduleGraphicData",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        graphicData = field.GetValue(sourceModule.def) as GraphicData;
                    }
                }

                var graphicEdProp = moduleType.GetProperty("moduleGraphic_ed",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (graphicEdProp != null)
                {
                    graphicDataEd = graphicEdProp.GetValue(sourceModule) as GraphicData;
                }
                else
                {
                    var defType = sourceModule.def.GetType();
                    var fieldEd = defType.GetField("moduleGraphicData_ed",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fieldEd != null)
                    {
                        graphicDataEd = fieldEd.GetValue(sourceModule.def) as GraphicData;
                    }
                }
            }
            catch { }
        }

        // 从模块初始化浮游炮塔
        public void InitFromModule(Thing module)
        {
            sourceModule = module;

            try
            {
                var moduleType = module.GetType();

                // moduleGraphic
                var graphicProp = moduleType.GetProperty("moduleGraphic",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (graphicProp != null)
                {
                    graphicData = graphicProp.GetValue(module) as GraphicData;
                }

                // moduleGraphic_ed
                var graphicEdProp = moduleType.GetProperty("moduleGraphic_ed",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (graphicEdProp != null)
                {
                    graphicDataEd = graphicEdProp.GetValue(module) as GraphicData;
                }

                // 获取武器属性
                var defType = module.def.GetType();

                // moduleProjectile
                var projectileField = defType.GetField("moduleProjectile",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (projectileField != null)
                {
                    projectileDef = projectileField.GetValue(module.def) as ThingDef;
                }

                // EffectiveRange
                var rangeField = defType.GetField("EffectiveRange",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (rangeField != null)
                {
                    range = (float)rangeField.GetValue(module.def);
                }
                else
                {
                    var rangeField2 = defType.GetField("range",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (rangeField2 != null)
                    {
                        range = (float)rangeField2.GetValue(module.def);
                    }
                }

                // WarmingTime
                var warmingField = defType.GetField("WarmingTime",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (warmingField != null)
                {
                    int warmingTicks = (int)warmingField.GetValue(module.def);
                    warmupTime = warmingTicks / 60f;
                }

                // cooldownTime
                var cooldownField = defType.GetField("cooldownTime",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cooldownField != null)
                {
                    int cooldownTicks = (int)cooldownField.GetValue(module.def);
                    cooldownTime = cooldownTicks / 60f;
                }

                // BurstCount
                var burstField = defType.GetField("BurstCount",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (burstField != null)
                {
                    burstCount = (int)burstField.GetValue(module.def);
                }

                // ticksBetweenBurstShots
                var ticksBetweenField = defType.GetField("ticksBetweenBurstShots",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (ticksBetweenField != null)
                {
                    ticksBetweenShots = (int)ticksBetweenField.GetValue(module.def);
                    if (ticksBetweenShots <= 0) ticksBetweenShots = 6;
                }

                // 尝试从属性获取弹丸
                if (projectileDef == null)
                {
                    var verbField = defType.GetField("verb", BindingFlags.Instance | BindingFlags.Public);
                    if (verbField != null)
                    {
                        var verbProps = verbField.GetValue(module.def) as VerbProperties;
                        if (verbProps != null)
                        {
                            if (range <= 0) range = verbProps.range;
                            if (warmupTime <= 0) warmupTime = verbProps.warmupTime;
                            if (cooldownTime <= 0) cooldownTime = verbProps.defaultCooldownTime;
                            if (burstCount <= 0) burstCount = verbProps.burstShotCount;
                            projectileDef = verbProps.defaultProjectile;
                            soundCast = verbProps.soundCast;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                GodHandModMain.DebugLog($"[浮游炮塔] 初始化失败: {ex.Message}");
            }

            // 回退到默认图形
            if (graphicData == null)
            {
                graphicData = module.def.graphicData;
            }

            // 设置默认值
            if (range <= 0) range = 20f;
            if (warmupTime <= 0) warmupTime = 0.5f;
            if (cooldownTime <= 0) cooldownTime = 1f;
            if (burstCount <= 0) burstCount = 1;
        }

        #endregion

        #region 公共方法

        // 获取材质
        public Material GetMaterial()
        {
            if (cachedMat == null && graphicData != null)
            {
                try { cachedMat = graphicData.Graphic?.MatSingle; } catch { }
            }
            return cachedMat;
        }

        // 计算当前世界位置
        public Vector3 GetDrawPos(Pawn pawn)
        {
            if (pawn == null) return Vector3.zero;

            Vector3 pos = pawn.DrawPos;

            // 轨道位置
            float radAngle = orbitAngle * Mathf.Deg2Rad;
            pos.x += Mathf.Cos(radAngle) * orbitRadius;
            pos.z += Mathf.Sin(radAngle) * orbitRadius;

            // 浮动
            pos.y += 0.5f + Mathf.Sin(bobPhase) * 0.15f;

            return pos;
        }

        // Tick 更新
        public void Tick(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return;

            // 更新轨道位置
            orbitAngle += orbitSpeed;
            if (orbitAngle >= 360f) orbitAngle -= 360f;
            if (orbitAngle < 0f) orbitAngle += 360f;

            // 更新浮动
            bobPhase += 0.05f;
            if (bobPhase >= Mathf.PI * 2f) bobPhase -= Mathf.PI * 2f;

            int checkOffset = (int)(orbitAngle / 36f);

            // 每 15 tick 检查一次目标
            if ((Find.TickManager.TicksGame + checkOffset) % 15 == 0)
            {
                bool needNewTarget = !currentTarget.IsValid || !IsValidTarget(pawn, currentTarget);

                if (!needNewTarget && ShouldSwitchTarget(pawn))
                {
                    needNewTarget = true;
                }

                if (needNewTarget)
                {
                    currentTarget = FindTarget(pawn);
                }
            }

            Vector3 myPos = GetDrawPos(pawn);

            if (!currentTarget.IsValid) return;

            // 射击中
            if (burstShotsLeft > 0)
            {
                if (Find.TickManager.TicksGame >= nextShotTick)
                {
                    FireOneShot(pawn);
                    burstShotsLeft--;
                    nextShotTick = Find.TickManager.TicksGame + ticksBetweenShots;
                    if (burstShotsLeft <= 0)
                    {
                        burstCooldownTicks = (int)(cooldownTime * 60f);
                        if (burstCooldownTicks < 30) burstCooldownTicks = 30;
                        EvaluateTargetAfterBurst(pawn);
                    }
                }
                return;
            }

            // 冷却
            if (burstCooldownTicks > 0)
            {
                burstCooldownTicks--;
                return;
            }

            // 瞄准朝向
            Vector3 targetPosCurrent = currentTarget.CenterVector3;
            float desiredAngleCurrent = (targetPosCurrent - myPos).AngleFlat();
            curRotation = Mathf.MoveTowardsAngle(curRotation, desiredAngleCurrent, 8f);

            // 射击触发
            if (Mathf.Abs(Mathf.DeltaAngle(curRotation, desiredAngleCurrent)) < 15f)
            {
                if (burstWarmupTicks > 0)
                {
                    burstWarmupTicks--;
                    return;
                }
                StartBurst(pawn);
            }
        }

        // 渲染浮游炮
        public void Draw(Pawn pawn)
        {
            if (pawn?.Map == null) return;

            Material mat = GetMaterial();
            if (mat == null) return;

            Vector3 pos = GetDrawPos(pawn);
            float size = graphicData?.drawSize.x ?? 0.8f;

            DrawThrusterFlame(pawn, pos);

            Quaternion rot = Quaternion.Euler(0f, curRotation - 90f, 0f);
            Matrix4x4 matrix = Matrix4x4.TRS(pos, rot, new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);

            DrawLaserSight(pawn, pos);
        }

        #endregion


        #region 私有方法 - 目标选择

        // 寻找目标 - 支持分弹瞄准
        private LocalTargetInfo FindTarget(Pawn pawn)
        {
            HashSet<Thing> targetsAlreadyTaken = GetOtherFloatingTurretsTargets(pawn);

            // 首先找未被瞄准且不被护盾保护的目标
            var target = AttackTargetFinder.BestAttackTarget(
                pawn,
                TargetScanFlags.NeedThreat | TargetScanFlags.NeedLOSToAll,
                x => x is Pawn p && !p.Downed
                    && !targetsAlreadyTaken.Contains(p)
                    && !IsTargetProtectedByShield(pawn, new LocalTargetInfo(p)),
                0f, range, default, float.MaxValue, false, true
            );

            // 允许瞄准已被瞄准但不被护盾保护的目标
            if (target == null)
            {
                target = AttackTargetFinder.BestAttackTarget(
                    pawn,
                    TargetScanFlags.NeedThreat | TargetScanFlags.NeedLOSToAll,
                    x => x is Pawn p && !p.Downed
                        && !IsTargetProtectedByShield(pawn, new LocalTargetInfo(p)),
                    0f, range, default, float.MaxValue, false, true
                );
            }

            // 最后考虑被护盾保护的目标
            if (target == null)
            {
                target = AttackTargetFinder.BestAttackTarget(
                    pawn,
                    TargetScanFlags.NeedThreat | TargetScanFlags.NeedLOSToAll,
                    x => x is Pawn p && !p.Downed,
                    0f, range, default, float.MaxValue, false, true
                );
            }

            return target != null ? new LocalTargetInfo(target.Thing) : LocalTargetInfo.Invalid;
        }

        // 获取其他浮游炮已经瞄准的目标
        private HashSet<Thing> GetOtherFloatingTurretsTargets(Pawn pawn)
        {
            HashSet<Thing> targets = new HashSet<Thing>();

            if (parentSlot?.ParentHead == null) return targets;

            foreach (var slot in parentSlot.ParentHead.TurretSlots)
            {
                if (slot.floatingTurrets == null) continue;

                foreach (var floater in slot.floatingTurrets)
                {
                    if (floater == this) continue;

                    if (floater.currentTarget.IsValid && floater.currentTarget.Thing != null)
                    {
                        targets.Add(floater.currentTarget.Thing);
                    }
                }
            }

            return targets;
        }

        // 检查是否应该切换目标
        private bool ShouldSwitchTarget(Pawn pawn)
        {
            if (!currentTarget.IsValid || parentSlot?.ParentHead == null) return false;

            int sameTargetCount = 0;
            int totalFloaters = 0;

            foreach (var slot in parentSlot.ParentHead.TurretSlots)
            {
                if (slot.floatingTurrets == null) continue;

                foreach (var floater in slot.floatingTurrets)
                {
                    totalFloaters++;

                    if (floater.currentTarget.IsValid &&
                        floater.currentTarget.Thing == currentTarget.Thing)
                    {
                        sameTargetCount++;
                    }
                }
            }

            if (totalFloaters <= 1) return false;

            if (sameTargetCount > 1)
            {
                HashSet<Thing> takenTargets = GetOtherFloatingTurretsTargets(pawn);

                var otherTarget = AttackTargetFinder.BestAttackTarget(
                    pawn,
                    TargetScanFlags.NeedThreat | TargetScanFlags.NeedLOSToAll,
                    x => x is Pawn p && !p.Downed
                        && !takenTargets.Contains(p)
                        && p != currentTarget.Thing
                        && !IsTargetProtectedByShield(pawn, new LocalTargetInfo(p)),
                    0f, range, default, float.MaxValue, false, true
                );

                if (otherTarget != null)
                {
                    return true;
                }
            }

            return false;
        }

        // 完成一轮射击后评估是否需要切换目标
        private void EvaluateTargetAfterBurst(Pawn pawn)
        {
            if (pawn?.Map == null) return;

            if (!currentTarget.IsValid || !IsValidTarget(pawn, currentTarget))
            {
                currentTarget = FindTarget(pawn);
                return;
            }

            Vector3 myPos = GetDrawPos(pawn);
            float currentDist = (currentTarget.CenterVector3 - myPos).magnitude;

            HashSet<Thing> takenTargets = GetOtherFloatingTurretsTargets(pawn);

            LocalTargetInfo betterTarget = LocalTargetInfo.Invalid;
            float betterScore = GetTargetPriorityScore(pawn, currentTarget, currentDist, takenTargets.Contains(currentTarget.Thing));

            foreach (var thing in pawn.Map.attackTargetsCache.GetPotentialTargetsFor(pawn))
            {
                if (thing.Thing == currentTarget.Thing) continue;
                if (!(thing.Thing is Pawn targetPawn)) continue;
                if (targetPawn.Dead || targetPawn.Downed) continue;

                float dist = (targetPawn.DrawPos - myPos).magnitude;
                if (dist > range) continue;

                LocalTargetInfo potentialTarget = new LocalTargetInfo(targetPawn);
                if (IsTargetProtectedByShield(pawn, potentialTarget)) continue;

                bool isTaken = takenTargets.Contains(targetPawn);
                float score = GetTargetPriorityScore(pawn, potentialTarget, dist, isTaken);

                if (score > betterScore)
                {
                    betterScore = score;
                    betterTarget = potentialTarget;
                }
            }

            if (betterTarget.IsValid)
            {
                currentTarget = betterTarget;
            }
        }

        // 计算目标优先级分数
        private float GetTargetPriorityScore(Pawn pawn, LocalTargetInfo target, float distance, bool isAlreadyTaken)
        {
            float score = 100f;

            score -= Mathf.Min(distance * 2f, 50f);

            if (target.Thing is Pawn targetPawn)
            {
                if (targetPawn.CurJob?.targetA.Thing == pawn)
                {
                    score += 30f;
                }
                else if (targetPawn.CurJob?.def == JobDefOf.AttackMelee ||
                         targetPawn.CurJob?.def == JobDefOf.AttackStatic)
                {
                    var jobTarget = targetPawn.CurJob?.targetA.Thing;
                    if (jobTarget is Pawn victim && !victim.HostileTo(pawn))
                    {
                        score += 20f;
                    }
                }

                if (distance < 5f && targetPawn.equipment?.Primary == null)
                {
                    score += 15f;
                }
                else if (distance < 3f)
                {
                    score += 25f;
                }
            }

            if (isAlreadyTaken)
            {
                score -= 40f;
            }

            return score;
        }

        // 检查目标是否有效
        private bool IsValidTarget(Pawn pawn, LocalTargetInfo target)
        {
            if (!target.IsValid) return false;
            if (target.Thing == null || target.Thing.Destroyed) return false;
            if (target.Thing is Pawn p && (p.Dead || p.Downed)) return false;

            float dist = (target.CenterVector3 - pawn.DrawPos).magnitude;
            if (dist > range) return false;

            if (IsTargetProtectedByShield(pawn, target))
            {
                return false;
            }

            return true;
        }

        // 检查目标是否被护盾保护
        private bool IsTargetProtectedByShield(Pawn pawn, LocalTargetInfo target)
        {
            if (pawn?.Map == null || !target.IsValid) return false;

            Vector3 myPos = GetDrawPos(pawn);
            Vector3 targetPos = target.CenterVector3;

            if (TurretSlot.IsTargetBlockedByShield(myPos, targetPos, pawn.Map, parentSlot?.ParentHead))
            {
                return true;
            }

            if (target.Thing is Pawn targetPawn && TurretSlot.HasActivePersonalShield(targetPawn))
            {
                return true;
            }

            return false;
        }

        #endregion

        #region 私有方法 - 射击

        // 开始连发
        private void StartBurst(Pawn pawn)
        {
            if (!currentTarget.IsValid || pawn?.Map == null) return;

            burstShotsLeft = burstCount;
            nextShotTick = Find.TickManager.TicksGame;

            FireOneShot(pawn);
            burstShotsLeft--;
            nextShotTick = Find.TickManager.TicksGame + ticksBetweenShots;

            if (burstShotsLeft <= 0)
            {
                burstCooldownTicks = (int)(cooldownTime * 60f);
                if (burstCooldownTicks < 30) burstCooldownTicks = 30;
            }

            burstWarmupTicks = (int)(warmupTime * 60f);
        }

        // 发射单发
        private void FireOneShot(Pawn pawn)
        {
            if (!currentTarget.IsValid || pawn?.Map == null) return;

            TurretHeadShootingTracker.RegisterFloatingTurretShooting(pawn);

            ThingDef usedProjectile = projectileDef;
            if (usedProjectile == null)
            {
                usedProjectile = DefDatabase<ThingDef>.GetNamedSilentFail("Bullet_MiniTurret")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("Bullet_Minigun")
                    ?? DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d => d.projectile != null);

                if (usedProjectile == null)
                {
                    TurretHeadShootingTracker.UnregisterFloatingTurretShooting(pawn);
                    return;
                }
            }

            Vector3 launchPos = GetDrawPos(pawn);
            IntVec3 spawnCell = pawn.Position;

            try
            {
                Projectile projectile = (Projectile)ThingMaker.MakeThing(usedProjectile);
                GenSpawn.Spawn(projectile, spawnCell, pawn.Map);
                projectile.Launch(pawn, launchPos, currentTarget, currentTarget, ProjectileHitFlags.IntendedTarget);

                FleckMaker.Static(launchPos, pawn.Map, FleckDefOf.ShotFlash, 1f);

                if (soundCast != null)
                {
                    soundCast.PlayOneShotOnCamera(pawn.Map);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[浮游炮] 发射失败: {ex.Message}");
            }
            finally
            {
                TurretHeadShootingTracker.UnregisterFloatingTurretShooting(pawn);
            }
        }

        #endregion

        #region 私有方法 - 绘制

        // 绘制红色激光瞄准线
        private void DrawLaserSight(Pawn pawn, Vector3 pos)
        {
            if (!currentTarget.IsValid || pawn?.Map == null) return;

            Vector3 targetPos = currentTarget.CenterVector3;

            float altitude = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);
            Vector3 startPos = pos;
            startPos.y = altitude;
            Vector3 endPos = targetPos;
            endPos.y = altitude;

            GenDraw.DrawLineBetween(startPos, endPos, LaserLineMat, 0.15f);
        }

        // 绘制推进火焰效果
        private void DrawThrusterFlame(Pawn pawn, Vector3 pos)
        {
            if (pawn?.Map == null) return;

            bool isPaused = Find.TickManager.Paused;
            if (!isPaused)
            {
                thrusterFlamePhase++;
            }

            float pulseScale = 0.85f + Mathf.Sin(thrusterFlamePhase * 0.2f) * 0.15f;
            float flickerScale = 0.95f + (isPaused ? 0f : Rand.Range(-0.05f, 0.05f));
            float finalScale = pulseScale * flickerScale;

            float turretAltitude = pos.y;
            float flameBaseY = turretAltitude - 0.2f;
            float groundY = turretAltitude - 0.8f;

            Vector3 groundPos = new Vector3(pos.x, groundY, pos.z);

            // 绘制多层火焰
            int flameSegments = 4;
            for (int i = 0; i < flameSegments; i++)
            {
                float t = (float)i / flameSegments;
                float segmentY = Mathf.Lerp(groundY, flameBaseY, t);
                Vector3 segmentPos = new Vector3(pos.x, segmentY, pos.z);

                float segmentSize = Mathf.Lerp(0.35f, 0.15f, t) * finalScale;

                segmentPos.x += Mathf.Sin(thrusterFlamePhase * 0.3f + i) * 0.03f;
                segmentPos.z += Mathf.Cos(thrusterFlamePhase * 0.25f + i * 0.5f) * 0.03f;

                Material mat = (t < 0.4f) ? ThrusterFlameOuterMat : ThrusterFlameCoreMat;

                Quaternion rot = Quaternion.identity;
                Matrix4x4 matrix = Matrix4x4.TRS(segmentPos, rot, new Vector3(segmentSize, 1f, segmentSize));
                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
            }

            // 绘制地面光晕
            float glowSize = 0.5f * finalScale;
            Vector3 glowPos = groundPos;
            glowPos.y = groundY - 0.1f;

            Matrix4x4 glowMatrix = Matrix4x4.TRS(glowPos, Quaternion.identity, new Vector3(glowSize, 1f, glowSize));
            Graphics.DrawMesh(MeshPool.plane10, glowMatrix, ThrusterGlowMat, 0);

            if (isPaused) return;

            // 地面热浪效果
            if (thrusterFlamePhase % 30 == 0 && Rand.Chance(0.4f))
            {
                FleckMaker.ThrowHeatGlow(groundPos.ToIntVec3(), pawn.Map, 0.4f);
            }

            // 地面灰尘效果
            if (thrusterFlamePhase % 25 == 0 && Rand.Chance(0.3f))
            {
                FleckMaker.ThrowDustPuff(groundPos, pawn.Map, 0.4f);
            }
        }

        #endregion
    }
}
