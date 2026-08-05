using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // 炮塔头饰支持多个炮塔堆叠
    [StaticConstructorOnStartup]
    public class GodHandTurretHead : Apparel
    {
        // ========== 多炮塔系统 ==========
        private List<TurretSlot> turretSlots = new List<TurretSlot>();
        public static int MaxTurrets => GodHandModMain.Settings.turretHeadMaxTurrets; // 最大炮塔数量（从设置读取）

        // 统一护盾系统
        private int combinedShieldMaxHP = 0;         // 总护盾最大HP（所有炮塔之和）
        private int combinedShieldCurrentHP = 0;     // 当前护盾HP
        private float combinedShieldRadius = 0f;     // 护盾范围（取最大）
        private Color combinedShieldColor = new Color(0.4f, 0.4f, 0.6f); // 护盾颜色（取第一个）
        private int combinedShieldRechargeRate = 0;  // 总恢复速率（叠加）
        private bool hasCombinedShield = false;      // 是否有护盾

        // 公开属性
        public List<TurretSlot> TurretSlots => turretSlots;
        public int TurretCount => turretSlots.Count;
        public bool HasTurrets => turretSlots.Count > 0;
        public bool HasShield => hasCombinedShield && combinedShieldCurrentHP > 0;
        public float ShieldRadius => combinedShieldRadius;
        public int ShieldCurrentHP => combinedShieldCurrentHP;
        public int ShieldMaxHP => combinedShieldMaxHP;

        // 覆盖Label显示炮塔内容
        public override string Label
        {
            get
            {
                if (turretSlots.Count == 0)
                    return base.Label;

                if (turretSlots.Count == 1)
                {
                    return "GodHand.TurretHead.LabelSingle".Translate(turretSlots[0].sourceTurretDef?.LabelCap ?? "???");
                }
                else
                {
                    // 多个显示数量和首个名称
                    return "GodHand.TurretHead.LabelMultipleTurrets".Translate(turretSlots.Count, turretSlots[0].sourceTurretDef?.LabelCap ?? "???");
                }
            }
        }

        public override string DescriptionDetailed
        {
            get
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine(base.DescriptionDetailed);

                if (turretSlots.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("GodHand.TurretHead.TurretList".Translate());

                    float totalHP = 0f;
                    for (int i = 0; i < turretSlots.Count; i++)
                    {
                        var slot = turretSlots[i];
                        string turretName = slot.sourceTurretDef?.LabelCap ?? "???";

                        // 累计炮塔HP
                        if (slot.sourceTurretDef != null)
                        {
                            totalHP += slot.sourceTurretDef.GetStatValueAbstract(StatDefOf.MaxHitPoints);
                        }

                        if (slot.fuelSystemEnabled)
                        {
                            int shots = Mathf.FloorToInt(slot.fuel / slot.consumeFuelPerShot);
                            int maxShots = Mathf.FloorToInt(slot.fuelCapacity / slot.consumeFuelPerShot);
                            sb.AppendLine($"  {i + 1}. {turretName} [{shots}/{maxShots}]");
                        }
                        else
                        {
                            sb.AppendLine($"  {i + 1}. {turretName}");
                        }
                    }

                    // 显示头部HP加成
                    float hpBonus = totalHP / 2f;
                    sb.AppendLine();
                    sb.AppendLine("GodHand.TurretHead.HeadHPBonus".Translate(Mathf.FloorToInt(hpBonus)));
                }

                return sb.ToString().TrimEndNewlines();
            }
        }

        // 兼容旧代码 - 返回第一个炮塔的数据
        public Thing Gun => turretSlots.Count > 0 ? turretSlots[0].gun : null;
        public ThingDef SourceTurretDef => turretSlots.Count > 0 ? turretSlots[0].sourceTurretDef : null;
        public Verb AttackVerb => turretSlots.Count > 0 ? turretSlots[0].AttackVerb : null;

        // 全局控制
        private bool holdFire = false;

        // 缓存
        private Material cachedTopMat;
        private Graphic overrideGraphic;

        // 添加新炮塔默认满弹药
        public bool AddTurret(ThingDef turretDef)
        {
            return AddTurret(turretDef, -1f, null);
        }

        // 添加新炮塔指定弹药
        public bool AddTurret(ThingDef turretDef, float initialFuel)
        {
            return AddTurret(turretDef, initialFuel, null);
        }

        // 添加新炮塔提取模块资料
        public bool AddTurret(ThingDef turretDef, float initialFuel, Building sourceTurret)
        {
            if (turretSlots.Count >= MaxTurrets)
            {
                Messages.Message("GodHand.TurretHead.MaxTurretsReached".Translate(MaxTurrets), MessageTypeDefOf.RejectInput);
                return false;
            }

            if (turretDef?.building?.turretGunDef == null)
            {
                return false;
            }

            int newIndex = turretSlots.Count;
            var newSlot = new TurretSlot(turretDef, newIndex, this, initialFuel);

            // 初始化模块系统（如果源炮塔有模块）
            if (sourceTurret != null)
            {
                newSlot.InitializeModuleSystem(sourceTurret);
            }

            turretSlots.Add(newSlot);

            UpdateGraphic();
            InvalidateHPBonusCache();
            RecalculateCombinedShield(); // 重新计算叠加护盾
            DebugLog($"添加炮塔: {turretDef.LabelCap}, 当前数量: {turretSlots.Count}, 初始燃料: {(initialFuel < 0 ? "满" : initialFuel.ToString("F1"))}, 模块: {newSlot.ModuleCount}");

            return true;
        }

        // 移除指定索引炮塔
        public TurretSlot RemoveTurret(int index)
        {
            if (index < 0 || index >= turretSlots.Count)
                return null;

            var slot = turretSlots[index];
            turretSlots.RemoveAt(index);

            // 重新计算堆叠索引
            for (int i = 0; i < turretSlots.Count; i++)
            {
                turretSlots[i].stackIndex = i;
            }

            UpdateGraphic();
            InvalidateHPBonusCache();
            RecalculateCombinedShield(); // 重新计算叠加护盾
            DebugLog($"移除炮塔索引 {index}, 剩余数量: {turretSlots.Count}");

            return slot;
        }

        // 移除最顶层炮塔
        public TurretSlot RemoveTopTurret()
        {
            if (turretSlots.Count == 0) return null;
            return RemoveTurret(turretSlots.Count - 1);
        }

        // 移除指定槽位
        public bool RemoveTurretSlot(TurretSlot slot)
        {
            if (slot == null || !turretSlots.Contains(slot))
                return false;

            int index = turretSlots.IndexOf(slot);
            turretSlots.RemoveAt(index);

            // 重新计算堆叠索引
            for (int i = 0; i < turretSlots.Count; i++)
            {
                turretSlots[i].stackIndex = i;
            }

            UpdateGraphic();
            InvalidateHPBonusCache();
            DebugLog($"移除炮塔: {slot.sourceTurretDef?.LabelCap}, 剩余数量: {turretSlots.Count}");

            return true;
        }

        // 转移现成槽位数据
        public bool AddTurretSlot(TurretSlot slot)
        {
            if (slot == null)
                return false;

            if (turretSlots.Count >= MaxTurrets)
            {
                Messages.Message("GodHand.TurretHead.MaxTurretsReached".Translate(MaxTurrets), MessageTypeDefOf.RejectInput);
                return false;
            }

            // 更新槽位索引和父级引用
            slot.stackIndex = turretSlots.Count;
            slot.SetParent(this);

            turretSlots.Add(slot);

            UpdateGraphic();
            InvalidateHPBonusCache();
            RecalculateCombinedShield();

            int moduleCount = slot.ModuleCount + (slot.HasFloatingTurrets ? slot.floatingTurrets.Count : 0);
            DebugLog($"转移炮塔槽位: {slot.sourceTurretDef?.LabelCap}, 当前数量: {turretSlots.Count}, 模块: {moduleCount}");

            return true;
        }

        // 兼容旧代码的属性设置
        public void SetSourceTurretDef(ThingDef value)
        {
            if (turretSlots.Count == 0)
            {
                AddTurret(value);
            }
        }

        private string GetTurretTopGraphicPath(int index = 0)
        {
            if (index >= turretSlots.Count) return null;
            var slot = turretSlots[index];
            if (slot.sourceTurretDef?.building == null) return null;

            ThingDef turretGunDef = slot.sourceTurretDef.building.turretGunDef;
            if (turretGunDef?.graphicData == null) return null;

            return turretGunDef.graphicData.texPath;
        }

        // 清除血量加成缓存
        private void InvalidateHPBonusCache()
        {
            if (Wearer != null)
            {
                TurretHeadHPBonus.InvalidateCache(Wearer);
            }
        }

        // 穿戴初始化并锁定
        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            AddTurretPowerHediff(pawn);

            // 锁定炮塔头防止手动脱下
            if (pawn?.apparel != null)
            {
                pawn.apparel.Lock(this);
            }
        }

        // 脱下清理数据
        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            RemoveTurretPowerHediff(pawn);
            TurretHeadHPBonus.InvalidateCache(pawn);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Wearer != null)
            {
                TurretHeadHPBonus.InvalidateCache(Wearer);
            }
            base.Destroy(mode);
        }

        // 赋能头部强化效果
        private void AddTurretPowerHediff(Pawn pawn)
        {
            if (pawn == null || turretSlots.Count == 0) return;
            if (GodHandDefOf.GodHand_TurretHeadBonus == null) return;

            // 检查是否已有该Hediff
            if (pawn.health?.hediffSet?.GetFirstHediffOfDef(GodHandDefOf.GodHand_TurretHeadBonus) != null)
                return;

            // 找到头部
            BodyPartRecord head = pawn.health?.hediffSet?.GetNotMissingParts()?.FirstOrDefault(p => p.def.defName == "Head");
            if (head == null) return;

            // 添加Hediff
            Hediff hediff = HediffMaker.MakeHediff(GodHandDefOf.GodHand_TurretHeadBonus, pawn, head);
            pawn.health.AddHediff(hediff);

            DebugLog($"添加炮塔之力Hediff到 {pawn.LabelShort}");
        }

        // 移除头部强化效果
        private void RemoveTurretPowerHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return;
            if (GodHandDefOf.GodHand_TurretHeadBonus == null) return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(GodHandDefOf.GodHand_TurretHeadBonus);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
                DebugLog($"移除炮塔之力Hediff从 {pawn.LabelShort}");
            }
        }

        private void DebugLog(string msg)
        {
            if (Prefs.DevMode)
            {
                GodHandModMain.DebugLog($"[GodHandTurretHead {this.ThingID}] {msg}");
            }
        }

        // 交互菜单及合并逻辑
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            // 先返回基类选项
            foreach (var opt in base.GetFloatMenuOptions(selPawn))
            {
                yield return opt;
            }

            // 检查是否在地上（未被穿戴）
            if (!this.Spawned) yield break;

            // 检查Pawn是否可以操作
            if (!selPawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("GodHand.TurretHead.CannotReach".Translate(), null);
                yield break;
            }

            if (selPawn.apparel == null)
            {
                yield return new FloatMenuOption("GodHand.TurretHead.CannotWearApparel".Translate(), null);
                yield break;
            }

            // 检查Pawn是否已经戴着炮塔头
            GodHandTurretHead existingHead = selPawn.apparel.WornApparel.OfType<GodHandTurretHead>().FirstOrDefault();

            if (existingHead == null)
            {
                // 没有炮塔头 - 提供装备选项
                yield return new FloatMenuOption(
                    "GodHand.TurretHead.Equip".Translate(this.Label),
                    () =>
                    {
                        Job job = JobMaker.MakeJob(JobDefOf.Wear, this);
                        selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    });
            }
            else
            {
                // 已有炮塔头 - 提供合并选项
                if (existingHead.TurretCount >= MaxTurrets)
                {
                    yield return new FloatMenuOption(
                        "GodHand.TurretHead.MaxTurretsReached".Translate(MaxTurrets), null);
                }
                else
                {
                    int canAdd = MaxTurrets - existingHead.TurretCount;
                    int willAdd = Mathf.Min(canAdd, this.TurretCount);

                    yield return new FloatMenuOption(
                        "GodHand.TurretHead.MergeTurrets".Translate(willAdd, existingHead.TurretCount + willAdd),
                        () =>
                        {
                            // 创建合并工作
                            Job job = JobMaker.MakeJob(GodHandDefOf.GodHand_MergeTurretHead, this);
                            selPawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                        });
                }
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // 为所有炮塔设置parent
            foreach (var slot in turretSlots)
            {
                slot.SetParent(this);
            }

            UpdateGraphic();
        }

        private void UpdateGraphic()
        {
            if (turretSlots.Count == 0) return;

            // 使用第一个炮塔的图形
            var firstSlot = turretSlots[0];
            GetTurretMaterial();

            if (cachedTopMat != null)
            {
                string path = GetTurretTopGraphicPath(0);

                if (!string.IsNullOrEmpty(path))
                {
                    float size = firstSlot.sourceTurretDef?.building?.turretTopDrawSize ?? 1f;

                    overrideGraphic = GraphicDatabase.Get<Graphic_Single>(
                        path,
                        ShaderDatabase.Cutout,
                        new Vector2(size, size),
                        Color.white
                    );
                }
            }

            // 刷新渲染树重新创建节点
            RefreshRenderTree();
        }

        // 重建渲染节点树
        private void RefreshRenderTree()
        {
            if (Wearer?.Drawer?.renderer?.renderTree == null) return;

            try
            {
                Wearer.Drawer.renderer.renderTree.SetDirty();
                DebugLog($"渲染树已标记为 dirty，将重新构建动态节点");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[炮塔头] 刷新渲染树失败: {ex.Message}");
            }
        }

        // 覆盖 Graphic 属性以返回炮塔贴图
        public override Graphic Graphic
        {
            get
            {
                if (overrideGraphic != null)
                {
                    return overrideGraphic;
                }
                return base.Graphic;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();

            // 保存炮塔列表
            Scribe_Collections.Look(ref turretSlots, "turretSlots", LookMode.Deep);
            Scribe_Values.Look(ref holdFire, "holdFire");

            // 序列化护盾资料
            Scribe_Values.Look(ref combinedShieldMaxHP, "combinedShieldMaxHP");
            Scribe_Values.Look(ref combinedShieldCurrentHP, "combinedShieldCurrentHP");
            Scribe_Values.Look(ref combinedShieldRadius, "combinedShieldRadius");
            Scribe_Values.Look(ref combinedShieldColor, "combinedShieldColor");
            Scribe_Values.Look(ref combinedShieldRechargeRate, "combinedShieldRechargeRate");
            Scribe_Values.Look(ref hasCombinedShield, "hasCombinedShield");

            if (turretSlots == null)
            {
                turretSlots = new List<TurretSlot>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 恢复parent引用
                foreach (var slot in turretSlots)
                {
                    slot.SetParent(this);
                }
                // 延迟到主线程更新图形（资源加载必须在主线程）
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    UpdateGraphic();
                    // 重新计算护盾（确保参数正确）
                    RecalculateCombinedShield();
                });
            }
        }

        // 生成控制按钮
        public override IEnumerable<Gizmo> GetWornGizmos()
        {
            // 调用基类的 Gizmos
            foreach (Gizmo gizmo in base.GetWornGizmos())
            {
                yield return gizmo;
            }

            if (Wearer == null || turretSlots.Count == 0) yield break;

            // ========== 护盾状态条 ==========
            if (hasCombinedShield && combinedShieldMaxHP > 0)
            {
                yield return new Gizmo_TurretHeadShieldStatus { turretHead = this };
            }

            // 停火切换按钮
            Command_Toggle holdFireCommand = new Command_Toggle
            {
                defaultLabel = "全部停火",
                defaultDesc = holdFire ? "所有炮塔不会自动射击" : "所有炮塔会自动射击敌人",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/HoldFire"),
                isActive = () => holdFire,
                toggleAction = delegate
                {
                    holdFire = !holdFire;
                    // 同步到所有炮塔
                    foreach (var slot in turretSlots)
                    {
                        slot.holdFire = holdFire;
                        if (holdFire)
                        {
                            slot.currentTarget = LocalTargetInfo.Invalid;
                        }
                    }
                }
            };
            yield return holdFireCommand;

            // 扫射模式切换
            bool sweepMode = turretSlots.Count > 0 && turretSlots[0].sweepMode;
            Command_Toggle sweepCommand = new Command_Toggle
            {
                defaultLabel = "扫射模式",
                defaultDesc = sweepMode ? "炮塔会在射击时左右扫射\n点击关闭" : "炮塔会精确瞄准目标\n点击开启扫射",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                isActive = () => turretSlots.Count > 0 && turretSlots[0].sweepMode,
                toggleAction = delegate
                {
                    bool newValue = !(turretSlots.Count > 0 && turretSlots[0].sweepMode);
                    foreach (var slot in turretSlots)
                    {
                        slot.sweepMode = newValue;
                    }
                }
            };
            yield return sweepCommand;

            // 预瞄模式切换
            bool leadMode = turretSlots.Count > 0 && turretSlots[0].leadTargeting;
            Command_Toggle leadCommand = new Command_Toggle
            {
                defaultLabel = "预瞄模式",
                defaultDesc = leadMode ? "对移动目标使用预瞄（适合爆炸弹头）\n点击关闭" : "直接瞄准目标当前位置\n点击开启预瞄",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/FireAtWill"),
                isActive = () => turretSlots.Count > 0 && turretSlots[0].leadTargeting,
                toggleAction = delegate
                {
                    bool newValue = !(turretSlots.Count > 0 && turretSlots[0].leadTargeting);
                    foreach (var slot in turretSlots)
                    {
                        slot.leadTargeting = newValue;
                    }
                }
            };
            yield return leadCommand;

            // 智能转火切换
            bool retargetMode = turretSlots.Count > 0 && turretSlots[0].smartRetarget;
            Command_Toggle retargetCommand = new Command_Toggle
            {
                defaultLabel = "智能转火",
                defaultDesc = retargetMode ? "目标血量过低时自动切换目标\n点击关闭" : "不会自动切换目标\n点击开启智能转火",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                isActive = () => turretSlots.Count > 0 && turretSlots[0].smartRetarget,
                toggleAction = delegate
                {
                    bool newValue = !(turretSlots.Count > 0 && turretSlots[0].smartRetarget);
                    foreach (var slot in turretSlots)
                    {
                        slot.smartRetarget = newValue;
                    }
                }
            };
            yield return retargetCommand;

            // 防空模式模块按钮
            foreach (var gizmo in GetAirDefenseGizmos())
            {
                yield return gizmo;
            }

            // 强制攻击按钮
            foreach (var gizmo in GetForceTargetGizmos())
            {
                yield return gizmo;
            }

            // 清除全部强制攻击
            bool anyForcedTarget = turretSlots.Any(s => s.forcedTarget.IsValid);
            if (anyForcedTarget)
            {
                Command_Action stopForceAttackCommand = new Command_Action
                {
                    defaultLabel = "取消全部强制目标",
                    defaultDesc = "停止所有炮塔的强制攻击",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"),
                    action = delegate
                    {
                        foreach (var slot in turretSlots)
                        {
                            slot.forcedTarget = LocalTargetInfo.Invalid;
                            slot.currentTarget = LocalTargetInfo.Invalid;
                        }
                    }
                };
                yield return stopForceAttackCommand;
            }

            // 弹药显示条
            for (int i = 0; i < turretSlots.Count; i++)
            {
                var slot = turretSlots[i];

                if (slot.fuelSystemEnabled)
                {
                    // 燃料条指示器
                    yield return new Gizmo_TurretSlotFuelStatus
                    {
                        turretSlot = slot,
                        slotIndex = i
                    };

                    // 添加装填按钮（只有当燃料未满时显示）
                    if (slot.fuel < slot.fuelCapacity)
                    {
                        int slotIdx = i;
                        string fuelName = slot.fuelThingDef?.LabelCap ?? "弹药";
                        int needed = Mathf.CeilToInt(slot.fuelCapacity - slot.fuel);

                        Command_Action refuelCommand = new Command_Action
                        {
                            defaultLabel = $"补充{fuelName}",
                            defaultDesc = $"命令殖民者去拿取 {fuelName} 来补充炮塔弹药。\n需要: {needed} {fuelName}",
                            icon = slot.fuelThingDef?.uiIcon ?? ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport"),
                            action = delegate
                            {
                                TryOrderRefuel(slotIdx);
                            }
                        };
                        yield return refuelCommand;
                    }
                }
            }

            // 按钮统一处理
            // 避免重复生成按钮

            // 统计炮塔数量进度显示
            if (turretSlots.Count > 1)
            {
                Command_Action countDisplay = new Command_Action
                {
                    defaultLabel = $"炮塔数量: {turretSlots.Count}/{MaxTurrets}",
                    defaultDesc = "当前安装的炮塔:\n" + string.Join("\n", turretSlots.Select((s, i) => $"  {i + 1}. {s.sourceTurretDef?.LabelCap}")),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport"),
                    action = delegate { } // 仅显示
                };
                yield return countDisplay;
            }

            // 扩展模块按钮
            foreach (var gizmo in GetModuleSystemGizmos())
            {
                yield return gizmo;
            }

            // 调试功能按钮
            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Reset All Cooldown",
                    action = () =>
                    {
                        foreach (var slot in turretSlots)
                        {
                            slot.burstCooldownTicksLeft = 0;
                        }
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Refuel All to Max",
                    action = () =>
                    {
                        foreach (var slot in turretSlots)
                        {
                            if (slot.fuelSystemEnabled)
                            {
                                slot.fuel = slot.fuelCapacity;
                            }
                        }
                        Messages.Message("所有炮塔燃料已补满", MessageTypeDefOf.PositiveEvent);
                    }
                };
            }
        }

        // 生成防空按钮
        private IEnumerable<Gizmo> GetAirDefenseGizmos()
        {
            for (int i = 0; i < turretSlots.Count; i++)
            {
                var slot = turretSlots[i];
                if (!slot.isAirDefenseTurret) continue;

                int slotIndex = i;
                string turretName = slot.sourceTurretDef?.LabelCap ?? $"炮塔{slotIndex + 1}";
                var localSlot = slot;

                // 防空模式切换按钮
                Command_Toggle airDefenseToggle = new Command_Toggle
                {
                    defaultLabel = $"[{slotIndex + 1}] 防空模式",
                    defaultDesc = localSlot.airDefenseMode
                        ? $"{turretName} 正在防空模式\n自动拦截飞来的弹丸和导弹\n点击切换为地面攻击模式"
                        : $"{turretName} 正在地面攻击模式\n点击切换为防空模式",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/FireAtWill"),
                    isActive = () => localSlot.airDefenseMode,
                    toggleAction = delegate
                    {
                        localSlot.ToggleAirDefenseMode();
                    }
                };
                yield return airDefenseToggle;
            }
        }

        // 生成强制目标按钮
        private IEnumerable<Gizmo> GetForceTargetGizmos()
        {
            for (int i = 0; i < turretSlots.Count; i++)
            {
                var slot = turretSlots[i];
                int slotIndex = i;
                string turretName = slot.sourceTurretDef?.LabelCap ?? $"炮塔{slotIndex + 1}";

                // 迫击炮类型使用专门的 Gizmo
                if (slot.IsMortarType)
                {
                    foreach (var gizmo in GetMortarGizmos(slot, slotIndex, turretName))
                    {
                        yield return gizmo;
                    }
                    continue;
                }

                // 集群火箭类型使用专门的 Gizmo
                if (slot.IsManualFireType)
                {
                    foreach (var gizmo in GetManualFireGizmos(slot, slotIndex, turretName))
                    {
                        yield return gizmo;
                    }
                    continue;
                }

                // 强制目标按钮
                if (slot.AttackVerb != null)
                {
                    float range = slot.Range;
                    float minRange = slot.MinRange;
                    string rangeText = minRange > 0 ? $"{minRange:F0}-{range:F0}" : $"{range:F0}";

                    var localSlot = slot;
                    var localIndex = slotIndex;

                    Command_TurretTarget forceTargetCommand = new Command_TurretTarget
                    {
                        defaultLabel = $"[{slotIndex + 1}] {turretName}\n强制目标",
                        defaultDesc = $"为炮塔 {slotIndex + 1} ({turretName}) 选择强制目标\n射程: {rangeText}\n\n选择一个目标后，该炮塔将持续攻击该目标",
                        icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                        turretSlot = localSlot,
                        casterPawn = Wearer,
                        onTargetSelected = delegate (LocalTargetInfo target)
                        {
                            if (target.IsValid)
                            {
                                localSlot.forcedTarget = target;
                                localSlot.currentTarget = target;
                                DebugLog($"炮塔 {localIndex + 1} 强制目标已设置: {target}");
                            }
                        }
                    };

                    yield return forceTargetCommand;
                }
            }
        }

        // 生成集群火箭按钮
        private IEnumerable<Gizmo> GetManualFireGizmos(TurretSlot slot, int slotIndex, string turretName)
        {
            float range = slot.Range;
            float minRange = slot.MinRange;
            string rangeText = minRange > 0 ? $"{minRange:F0}-{range:F0}" : $"{range:F0}";

            var localSlot = slot;
            var localIndex = slotIndex;

            // 显示激活按钮
            if (slot.activationRequired && !slot.isActivated)
            {
                Command_Action activateCommand = new Command_Action
                {
                    defaultLabel = $"[{slotIndex + 1}] {turretName}\n激活",
                    defaultDesc = $"激活 {turretName} 准备发射\n射程: {rangeText}\n\n激活后选择目标进行齐射",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower"),
                    action = delegate
                    {
                        localSlot.Activate();
                        Messages.Message($"{turretName} 已激活！", MessageTypeDefOf.PositiveEvent);
                    }
                };
                yield return activateCommand;
                yield break; // 未激活时不显示发射按钮
            }

            // 发射按钮（已激活或不需要激活）
            Command_TurretTarget fireCommand = new Command_TurretTarget
            {
                defaultLabel = $"[{slotIndex + 1}] {turretName}\n发射",
                defaultDesc = $"选择目标位置进行轰炸\n射程: {rangeText}\n\n点击后选择目标位置，炮塔将旋转对准并发射",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport"),
                turretSlot = localSlot,
                casterPawn = Wearer,
                onTargetSelected = delegate (LocalTargetInfo target)
                {
                    if (target.IsValid)
                    {
                        localSlot.forcedTarget = target;
                        DebugLog($"集群火箭 {localIndex + 1} 发射目标: {target}");
                    }
                }
            };

            yield return fireCommand;
        }

        // 生成迫击炮按钮
        private IEnumerable<Gizmo> GetMortarGizmos(TurretSlot slot, int slotIndex, string turretName)
        {
            float range = slot.Range;
            float minRange = slot.MinRange;
            string rangeText = minRange > 0 ? $"{minRange:F0}-{range:F0}" : $"{range:F0}";

            // 状态显示
            string statusText = slot.HasShellLoaded
                ? $"已装填: {slot.loadedShellDef?.LabelCap}"
                : "未装填";

            // 装填按钮
            Command_Action loadCommand = new Command_Action
            {
                defaultLabel = $"[{slotIndex + 1}] {turretName}\n装填炮弹",
                defaultDesc = $"为迫击炮 {slotIndex + 1} ({turretName}) 装填炮弹\n射程: {rangeText}\n\n当前状态: {statusText}",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter"),
                action = delegate
                {
                    TryOrderLoadShell(slotIndex);
                }
            };

            // 满载禁用按钮
            if (slot.loadedShellCount >= slot.shellCapacity)
            {
                loadCommand.Disable("炮弹已满");
            }

            yield return loadCommand;

            // 发射按钮（只有装填了炮弹才显示）
            if (slot.HasShellLoaded)
            {
                var localSlot = slot;
                var localIndex = slotIndex;

                Command_TurretTarget fireCommand = new Command_TurretTarget
                {
                    defaultLabel = $"[{slotIndex + 1}] {turretName}\n发射",
                    defaultDesc = $"选择目标发射 {slot.loadedShellDef?.LabelCap}\n射程: {rangeText}\n\n发射后需要重新装填",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                    turretSlot = localSlot,
                    casterPawn = Wearer,
                    onTargetSelected = delegate (LocalTargetInfo target)
                    {
                        if (target.IsValid)
                        {
                            GodHandModMain.DebugLog($"[GodHandTurretHead] 迫击炮设置目标: {target}, Cell: {target.Cell}, HasThing: {target.HasThing}");
                            localSlot.forcedTarget = target;
                            GodHandModMain.DebugLog($"[GodHandTurretHead] forcedTarget已设置: {localSlot.forcedTarget}");
                        }
                    }
                };

                yield return fireCommand;
            }
        }

        // 命令角色装填炮弹
        private void TryOrderLoadShell(int slotIndex = 0)
        {
            if (slotIndex < 0 || slotIndex >= turretSlots.Count) return;

            var slot = turretSlots[slotIndex];
            if (!slot.shellSystemEnabled)
            {
                Messages.Message("此炮塔不需要装填炮弹", MessageTypeDefOf.RejectInput);
                return;
            }

            if (slot.loadedShellCount >= slot.shellCapacity)
            {
                Messages.Message("炮弹已满", MessageTypeDefOf.RejectInput);
                return;
            }

            // 显示炮弹选择菜单
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (var shellDef in DefDatabase<ThingDef>.AllDefs.Where(d => d.IsShell && d.projectileWhenLoaded != null))
            {
                // 检查是否有可用的炮弹
                Thing shellThing = GenClosest.ClosestThingReachable(
                    Wearer.Position,
                    Wearer.Map,
                    ThingRequest.ForDef(shellDef),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(Wearer),
                    9999f,
                    (Thing t) => !t.IsForbidden(Wearer) && Wearer.CanReserve(t)
                );

                if (shellThing != null)
                {
                    var localShellDef = shellDef;
                    var localShellThing = shellThing;
                    options.Add(new FloatMenuOption(
                        $"装填 {shellDef.LabelCap} (x{shellThing.stackCount})",
                        () =>
                        {
                            // 消耗炮弹物品
                            int neededCount = slot.shellCapacity - slot.loadedShellCount;
                            int actualCount = Mathf.Min(neededCount, localShellThing.stackCount);

                            if (actualCount > 0)
                            {
                                // 消耗炮弹
                                localShellThing.SplitOff(actualCount).Destroy();

                                // 装填到炮塔
                                slot.LoadShell(localShellDef, actualCount);
                                Messages.Message($"已装填 {localShellDef.LabelCap} x{actualCount}", MessageTypeDefOf.PositiveEvent);
                            }
                        }
                    ));
                }
            }

            if (options.Count == 0)
            {
                Messages.Message("没有可用的炮弹", MessageTypeDefOf.RejectInput);
                return;
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        // 命令角色补给弹药
        private void TryOrderRefuel(int slotIndex = 0)
        {
            if (slotIndex < 0 || slotIndex >= turretSlots.Count) return;

            var slot = turretSlots[slotIndex];
            if (!slot.fuelSystemEnabled || slot.fuelThingDef == null)
            {
                Messages.Message("GodHand.TurretHead.NoFuelType".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (slot.fuel >= slot.fuelCapacity)
            {
                Messages.Message("GodHand.TurretHead.FuelFull".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            // 查找可用的燃料物品
            Thing fuelThing = GenClosest.ClosestThingReachable(
                Wearer.Position,
                Wearer.Map,
                ThingRequest.ForDef(slot.fuelThingDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(Wearer),
                9999f,
                (Thing t) => !t.IsForbidden(Wearer) && Wearer.CanReserve(t)
            );

            if (fuelThing != null)
            {
                // 创建补充燃料的Job
                Job job = JobMaker.MakeJob(GodHandDefOf.GodHand_RefuelTurretHead, fuelThing);
                Wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            }
            else
            {
                Messages.Message("GodHand.TurretHead.NoFuelAvailable".Translate(slot.fuelThingDef.LabelCap), MessageTypeDefOf.RejectInput);
            }
        }

        // 生成模块管理按钮
        private IEnumerable<Gizmo> GetModuleSystemGizmos()
        {
            // 检查是否有任何炮塔支持模块系统
            var slotsWithModules = turretSlots.Where(s => s.hasModuleSystem).ToList();
            if (slotsWithModules.Count == 0) yield break;

            // 统计总模块数量
            int totalModules = 0;
            int totalFloaters = 0;
            foreach (var slot in slotsWithModules)
            {
                totalModules += slot.storedModules?.Count ?? 0;
                totalFloaters += slot.floatingTurrets?.Count ?? 0;
            }
            int totalCount = totalModules + totalFloaters;

            // 生成简洁的描述
            StringBuilder desc = new StringBuilder();
            desc.AppendLine("点击打开模块管理窗口");
            desc.AppendLine();
            desc.AppendLine($"支持模块的炮塔: {slotsWithModules.Count}");
            desc.AppendLine($"已安装模块: {totalCount}");

            if (totalCount > 0)
            {
                desc.AppendLine();
                foreach (var slot in slotsWithModules)
                {
                    string turretName = slot.sourceTurretDef?.LabelCap ?? "炮塔";
                    int count = (slot.storedModules?.Count ?? 0) + (slot.floatingTurrets?.Count ?? 0);
                    if (count > 0)
                    {
                        desc.AppendLine($"  · {turretName}: {count}个模块");
                    }
                }
            }

            // 单个管理按钮
            Command_Action moduleManagerCommand = new Command_Action
            {
                defaultLabel = $"模块管理 ({totalCount})",
                defaultDesc = desc.ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LoadTransporter", false) ?? BaseContent.BadTex,
                action = delegate
                {
                    // 打开模块管理窗口
                    Find.WindowStack.Add(new Window_TurretModuleManager(this));
                }
            };
            yield return moduleManagerCommand;
        }

        // 显示模块管理菜单
        private void ShowModuleManagementMenu(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= turretSlots.Count) return;

            var slot = turretSlots[slotIndex];
            if (!slot.hasModuleSystem) return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            // 显示已安装的模块（可拆卸）
            if (slot.storedModules != null && slot.storedModules.Count > 0)
            {
                options.Add(new FloatMenuOption("--- 已安装模块 (点击拆卸) ---", null));

                foreach (var module in slot.storedModules.ToList())
                {
                    var localModule = module;
                    options.Add(new FloatMenuOption(
                        $"拆卸: {module.LabelCap}",
                        () =>
                        {
                            if (slot.RemoveModule(localModule))
                            {
                                // 将模块掉落到地上
                                if (Wearer?.Map != null)
                                {
                                    GenSpawn.Spawn(localModule, Wearer.Position, Wearer.Map);
                                    Messages.Message($"已拆卸 {localModule.LabelCap}", MessageTypeDefOf.NeutralEvent);
                                }
                            }
                        }
                    ));
                }
            }
            else
            {
                options.Add(new FloatMenuOption("无已安装模块", null));
            }

            // 显示可安装的模块（从地图上查找）
            if (Wearer?.Map != null && slot.allowedModuleDefs != null)
            {
                options.Add(new FloatMenuOption("", null)); // 分隔线
                options.Add(new FloatMenuOption("--- 可安装的模块 ---", null));

                bool foundAny = false;
                foreach (var moduleDef in slot.allowedModuleDefs)
                {
                    // 获取模块的槽位ID
                    int moduleCase = 0;
                    try
                    {
                        var field = moduleDef.GetType().GetField("moduleCase",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (field != null)
                        {
                            moduleCase = (int)field.GetValue(moduleDef);
                        }
                    }
                    catch { }

                    // 检查槽位是否可用
                    bool slotAvailable = slot.SlotIsAvailable(moduleCase, moduleDef);
                    int remaining = slot.GetSlotRemainingCapacity(moduleCase);
                    int total = slot.GetSlotTotalCapacity(moduleCase);

                    // 在地图上查找该类型的模块
                    Thing availableModule = GenClosest.ClosestThingReachable(
                        Wearer.Position,
                        Wearer.Map,
                        ThingRequest.ForDef(moduleDef),
                        PathEndMode.ClosestTouch,
                        TraverseParms.For(Wearer),
                        30f,
                        (Thing t) => !t.IsForbidden(Wearer)
                    );

                    if (availableModule != null)
                    {
                        foundAny = true;
                        var localModule = availableModule;

                        if (slotAvailable)
                        {
                            // 槽位可用显示安装选项
                            options.Add(new FloatMenuOption(
                                $"安装: {availableModule.LabelCap} (槽位{moduleCase}: {remaining}/{total})",
                                () =>
                                {
                                    if (slot.AddModule(localModule))
                                    {
                                        localModule.DeSpawn();
                                        Messages.Message($"已安装 {localModule.LabelCap}", MessageTypeDefOf.PositiveEvent);
                                    }
                                    else
                                    {
                                        Messages.Message($"无法安装 {localModule.LabelCap}：槽位已满", MessageTypeDefOf.RejectInput);
                                    }
                                }
                            ));
                        }
                        else
                        {
                            // 槽位已满显示禁用选项
                            options.Add(new FloatMenuOption(
                                $"安装: {availableModule.LabelCap} (槽位{moduleCase}已满: {remaining}/{total})",
                                null // null action 表示禁用
                            ));
                        }
                    }
                }

                if (!foundAny)
                {
                    options.Add(new FloatMenuOption("附近无可用模块", null));
                }
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        // 兼容旧代码方法

        // 补充燃料兼容旧代码
        public void Refuel(float amount)
        {
            if (turretSlots.Count > 0)
            {
                turretSlots[0].Refuel(amount);
            }
        }

        // 获取补充燃料数量
        public int GetFuelCountToFullyRefuel()
        {
            if (turretSlots.Count == 0) return 0;
            var slot = turretSlots[0];
            if (!slot.fuelSystemEnabled) return 0;
            return Mathf.CeilToInt(slot.fuelCapacity - slot.fuel);
        }

        protected override void Tick()
        {
            base.Tick();
            if (Wearer == null || !Wearer.Spawned) return;
            if (Wearer.Downed || Wearer.Dead) return;
            if (turretSlots.Count == 0) return;

            // Tick 每个炮塔槽位（独立运作）
            foreach (var slot in turretSlots)
            {
                slot.Tick();
            }

            // 护盾恢复
            TickCombinedShield();
        }

        // 护盾恢复HP
        private void TickCombinedShield()
        {
            if (!hasCombinedShield || combinedShieldRechargeRate <= 0) return;

            if (combinedShieldCurrentHP < combinedShieldMaxHP)
            {
                if (Find.TickManager.TicksGame % combinedShieldRechargeRate == 0)
                {
                    combinedShieldCurrentHP++;
                }
            }
        }

        // 重新计算叠加护盾资料
        public void RecalculateCombinedShield()
        {
            combinedShieldMaxHP = 0;
            combinedShieldRadius = 0f;
            combinedShieldRechargeRate = 0;
            hasCombinedShield = false;
            bool colorSet = false;

            foreach (var slot in turretSlots)
            {
                if (slot.hasShieldModule)
                {
                    hasCombinedShield = true;

                    // HP叠加
                    combinedShieldMaxHP += slot.shieldMaxHitPoints;

                    // 范围取最大（确保有有效值）
                    float slotRadius = slot.shieldRadius > 0 ? slot.shieldRadius : 4.5f;
                    if (slotRadius > combinedShieldRadius)
                    {
                        combinedShieldRadius = slotRadius;
                    }

                    // 恢复速率（取有效值）
                    int slotRecharge = slot.shieldRechargeRate > 0 ? slot.shieldRechargeRate : 3;
                    if (combinedShieldRechargeRate == 0 || slotRecharge < combinedShieldRechargeRate)
                    {
                        combinedShieldRechargeRate = slotRecharge;
                    }

                    // 颜色取第一个
                    if (!colorSet)
                    {
                        combinedShieldColor = slot.shieldColor;
                        colorSet = true;
                    }

                    GodHandModMain.DebugLog($"[炮塔头] 槽位护盾: slot={slot.sourceTurretDef?.defName}, hasShield={slot.hasShieldModule}, radius={slot.shieldRadius}, maxHP={slot.shieldMaxHitPoints}");
                }
            }

            // 护盾零半径使用默认值
            if (hasCombinedShield && combinedShieldRadius <= 0)
            {
                combinedShieldRadius = 4.5f;
                GodHandModMain.DebugLog($"[炮塔头] 护盾半径为0，使用默认值: {combinedShieldRadius}");
            }

            // 初始化当前HP
            if (hasCombinedShield && combinedShieldCurrentHP <= 0)
            {
                combinedShieldCurrentHP = combinedShieldMaxHP;
            }

            GodHandModMain.DebugLog($"[炮塔头] 叠加护盾结果: hasShield={hasCombinedShield}, maxHP={combinedShieldMaxHP}, currentHP={combinedShieldCurrentHP}, radius={combinedShieldRadius}, recharge={combinedShieldRechargeRate}");
        }

        // 护盾受到伤害逻辑
        public void DamageCombinedShield(int damage)
        {
            if (!hasCombinedShield) return;
            combinedShieldCurrentHP = Mathf.Max(0, combinedShieldCurrentHP - damage);
        }

        // 绘制统一护盾效果
        private static Material forceFieldMat;
        private static readonly MaterialPropertyBlock shieldMatPropertyBlock = new MaterialPropertyBlock();

        public void DrawCombinedShield()
        {
            if (!hasCombinedShield || combinedShieldCurrentHP <= 0 || Wearer == null) return;

            // 确保有有效半径
            if (combinedShieldRadius <= 0)
            {
                // 尝试重新计算护盾参数
                RecalculateCombinedShield();
                if (combinedShieldRadius <= 0) return; // 仍然无效则跳过
            }

            if (forceFieldMat == null)
            {
                forceFieldMat = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);
            }

            float radius = combinedShieldRadius;
            float hpPercent = (float)combinedShieldCurrentHP / combinedShieldMaxHP;
            float alpha = 0.2f + 0.5f * hpPercent;
            Color drawColor = new Color(combinedShieldColor.r, combinedShieldColor.g, combinedShieldColor.b, alpha);

            shieldMatPropertyBlock.SetColor(ShaderPropertyIDs.Color, drawColor);

            Vector3 shieldPos = Wearer.DrawPos;
            shieldPos.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);

            Matrix4x4 matrix = Matrix4x4.TRS(shieldPos, Quaternion.identity,
                new Vector3(radius * 2f * 1.1601562f, 1f, radius * 2f * 1.1601562f));

            Graphics.DrawMesh(MeshPool.plane10, matrix, forceFieldMat, 0, null, 0, shieldMatPropertyBlock);
        }

        // 渲染职责划分炮塔主体及附加效果

        public override void DrawWornExtras()
        {
            if (Wearer == null || !Wearer.Spawned || turretSlots.Count == 0) return;

            // 计算基础绘制位置
            Vector3 headOffset = Wearer.Drawer.renderer.BaseHeadOffsetAt(Wearer.Rotation);
            Vector3 baseDrawPos = Wearer.DrawPos + headOffset;

            for (int i = 0; i < turretSlots.Count; i++)
            {
                var slot = turretSlots[i];
                if (slot.sourceTurretDef?.building == null) continue;

                // 计算当前槽位的绘制位置
                Vector3 drawPos = baseDrawPos + slot.DrawOffset;
                drawPos.y = Wearer.DrawPos.y + Altitudes.AltInc + (i * 0.01f);

                float size = slot.sourceTurretDef.building.turretTopDrawSize;
                if (size <= 0) size = 1f;

                // 渲染树系统处理主体
                // 参阅渲染节点设置文件

                // ===== 附加效果渲染 =====

                // 残阳额外装饰渲染
                if (slot.hasTopGunSystem && slot.topGuns.Count > 0)
                {
                    slot.DrawExtraGraphic(drawPos, slot.curRotation, size);
                }

                // 模块图形渲染
                slot.DrawModules(drawPos, slot.curRotation);

                // 浮游炮塔渲染
                slot.DrawFloatingTurrets(Wearer);
            }

            // 统一护盾渲染
            DrawCombinedShield();
        }

        // 获取炮塔顶材质资料
        private Material GetTurretTopMaterial(TurretSlot slot)
        {
            var building = slot.sourceTurretDef?.building;
            if (building == null) return null;

            // 方案1预生成材质
            // 初始化生成最可靠
            if (building.turretTopMat != null)
            {
                return building.turretTopMat;
            }

            // 方案2手动创建材质
            if (building.turretGunDef?.graphicData?.texPath != null)
            {
                string texPath = building.turretGunDef.graphicData.texPath;
                var mat = MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout);
                if (mat != null) return mat;
            }

            // 方案3获取图形材质
            if (building.turretGunDef?.graphic?.MatSingle != null)
            {
                return building.turretGunDef.graphic.MatSingle;
            }

            // 方案4从缓存对象获取
            if (slot.gun?.Graphic?.MatSingle != null)
            {
                return slot.gun.Graphic.MatSingle;
            }

            // 方案5尝试顶层后缀
            if (slot.sourceTurretDef.graphicData?.texPath != null)
            {
                string basePath = slot.sourceTurretDef.graphicData.texPath;

                // 尝试 _Top 后缀
                string topPath = basePath + "_Top";
                if (ContentFinder<Texture2D>.Get(topPath, false) != null)
                {
                    return MaterialPool.MatFrom(topPath, ShaderDatabase.Cutout);
                }

                // 直接使用基础路径
                if (ContentFinder<Texture2D>.Get(basePath, false) != null)
                {
                    return MaterialPool.MatFrom(basePath, ShaderDatabase.Cutout);
                }
            }

            GodHandModMain.DebugLog($"[炮塔头] 无法获取 {slot.sourceTurretDef?.LabelCap} 的炮塔顶材质");
            return null;
        }

        private Material GetTurretMaterial()
        {
            string path = GetTurretTopGraphicPath(0);

            if (cachedTopMat == null && !string.IsNullOrEmpty(path))
            {
                cachedTopMat = MaterialPool.MatFrom(path, ShaderDatabase.Cutout);
            }
            return cachedTopMat;
        }

        public override string GetInspectString()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(base.GetInspectString());

            if (turretSlots.Count > 0)
            {
                sb.AppendLine();
                sb.Append("GodHand.TurretHead.InspectTurretCount".Translate(turretSlots.Count));

                // 显示每个炮塔的简要状态
                for (int i = 0; i < turretSlots.Count; i++)
                {
                    var slot = turretSlots[i];
                    sb.AppendLine();

                    string status = "";
                    if (slot.fuelSystemEnabled)
                    {
                        int shots = Mathf.FloorToInt(slot.fuel / slot.consumeFuelPerShot);
                        status = "GodHand.TurretHead.ShotsLeft".Translate(shots);
                    }
                    if (slot.currentTarget.IsValid)
                    {
                        status += " " + "GodHand.TurretHead.Aiming".Translate();
                    }

                    sb.Append($"  {i + 1}. {slot.sourceTurretDef?.LabelCap} {status}");

                    // 显示模块数量
                    if (slot.HasModuleSystem && slot.ModuleCount > 0)
                    {
                        sb.Append($" [模块: {slot.ModuleCount}]");
                    }
                }
            }

            return sb.ToString().TrimEndNewlines();
        }
    }

    // 自定义目标选择命令支持射程显示
    public class Command_TurretTarget : Command
    {
        public TurretSlot turretSlot;
        public Pawn casterPawn;
        public Action<LocalTargetInfo> onTargetSelected;

        // 静态变量用于在目标选择期间绘制射程
        public static TurretSlot activeTargetingSlot = null;
        public static Pawn activeTargetingPawn = null;

        public override void GizmoUpdateOnMouseover()
        {
            // 绘制射程圆圈
            DrawRangeRingFor(turretSlot, casterPawn);
        }

        public static void DrawRangeRingFor(TurretSlot slot, Pawn pawn)
        {
            if (slot?.AttackVerb != null && pawn != null && pawn.Spawned)
            {
                float range = slot.Range;
                float minRange = slot.MinRange;

                // 限制最大绘制半径
                const float MaxDrawableRadius = 89.9f;

                // 绘制最大射程
                if (range > 0 && range <= MaxDrawableRadius)
                {
                    GenDraw.DrawRadiusRing(pawn.Position, range);
                }

                // 绘制最小射程（如果有）
                if (minRange > 0 && minRange <= MaxDrawableRadius)
                {
                    GenDraw.DrawRadiusRing(pawn.Position, minRange, Color.red);
                }
            }
        }

        public override void ProcessInput(Event ev)
        {
            base.ProcessInput(ev);

            if (turretSlot?.AttackVerb == null || casterPawn == null)
                return;

            // 设置当前活动的目标选择（用于外部绘制射程）
            activeTargetingSlot = turretSlot;
            activeTargetingPawn = casterPawn;

            // 创建目标参数
            // 允许瞄准地面位置（对于曲射弹道和强制攻击地点很有用）
            TargetingParameters targetParams = new TargetingParameters
            {
                canTargetPawns = true,
                canTargetBuildings = true,
                canTargetLocations = true,  // 允许瞄准地面
                validator = (TargetInfo ti) =>
                {
                    if (!ti.IsValid) return false;
                    float dist = (ti.Cell - casterPawn.Position).LengthHorizontal;
                    return dist <= turretSlot.Range && dist >= turretSlot.MinRange;
                }
            };

            // 使用简单的 BeginTargeting 重载
            Find.Targeter.BeginTargeting(
                targetParams,
                delegate (LocalTargetInfo target)
                {
                    onTargetSelected?.Invoke(target);
                    activeTargetingSlot = null;
                    activeTargetingPawn = null;
                },
                delegate (LocalTargetInfo target)
                {
                    // 高亮回调 - 绘制射程和目标
                    DrawRangeRingFor(turretSlot, casterPawn);
                    if (target.IsValid)
                    {
                        GenDraw.DrawTargetHighlight(target);
                    }
                },
                delegate (LocalTargetInfo target)
                {
                    // 验证器
                    float dist = (target.Cell - casterPawn.Position).LengthHorizontal;
                    return dist <= turretSlot.Range && dist >= turretSlot.MinRange;
                },
                casterPawn,
                delegate ()
                {
                    // 取消回调
                    activeTargetingSlot = null;
                    activeTargetingPawn = null;
                },
                TexCommand.Attack
            );
        }

        // 绘制目标选择射程
        public static void DrawTargetingRangeIfActive()
        {
            if (activeTargetingSlot != null && activeTargetingPawn != null && Find.Targeter.IsTargeting)
            {
                DrawRangeRingFor(activeTargetingSlot, activeTargetingPawn);
            }
        }
    }
}
