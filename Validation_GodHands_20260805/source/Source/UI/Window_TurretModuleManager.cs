using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld;

namespace GodHandMod
{
    // 炮塔模块管理窗口
    public class Window_TurretModuleManager : Window
    {
        private GodHandTurretHead turretHead;
        private Pawn wearer;
        private Vector2 scrollPosition = Vector2.zero;
        private int selectedTurretIndex = 0;

        // 窗口尺寸
        private const float WindowWidth = 600f;
        private const float WindowHeight = 500f;
        private new const float Margin = 10f;
        private const float RowHeight = 30f;
        private const float SectionSpacing = 15f;

        public override Vector2 InitialSize => new Vector2(WindowWidth, WindowHeight);

        public Window_TurretModuleManager(GodHandTurretHead head)
        {
            turretHead = head;
            wearer = head?.Wearer;

            forcePause = false;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (turretHead == null || wearer == null || !wearer.Spawned)
            {
                Close();
                return;
            }

            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(0, 0, inRect.width, 35f);
            Widgets.Label(titleRect, "炮塔头模块管理");

            Text.Font = GameFont.Small;
            float curY = 40f;

            // 炮塔选择区域
            curY = DrawTurretSelector(inRect, curY);

            curY += SectionSpacing;

            // 当前选中的炮塔信息
            if (selectedTurretIndex >= 0 && selectedTurretIndex < turretHead.TurretSlots.Count)
            {
                var slot = turretHead.TurretSlots[selectedTurretIndex];

                // 已安装模块区域
                curY = DrawInstalledModules(inRect, curY, slot);

                curY += SectionSpacing;

                // 可安装模块区域
                curY = DrawAvailableModules(inRect, curY, slot);
            }
        }

        // 绘制炮塔选择器
        private float DrawTurretSelector(Rect inRect, float curY)
        {
            Rect selectorRect = new Rect(0, curY, inRect.width, RowHeight);

            // 绘制炮塔选择按钮
            float buttonWidth = (inRect.width - Margin * (turretHead.TurretSlots.Count - 1)) / turretHead.TurretSlots.Count;
            buttonWidth = Mathf.Min(buttonWidth, 150f);

            for (int i = 0; i < turretHead.TurretSlots.Count; i++)
            {
                var slot = turretHead.TurretSlots[i];
                string turretName = slot.sourceTurretDef?.LabelCap ?? $"炮塔{i + 1}";
                int moduleCount = (slot.storedModules?.Count ?? 0) + (slot.floatingTurrets?.Count ?? 0);

                Rect buttonRect = new Rect(i * (buttonWidth + Margin), curY, buttonWidth, RowHeight);

                // 高亮选中的炮塔
                if (i == selectedTurretIndex)
                {
                    Widgets.DrawHighlight(buttonRect);
                }

                string label = $"{turretName}";
                if (slot.hasModuleSystem)
                {
                    label += $" [{moduleCount}]";
                }
                else
                {
                    label += " (无模块系统)";
                }

                if (Widgets.ButtonText(buttonRect, label))
                {
                    selectedTurretIndex = i;
                }
            }

            return curY + RowHeight;
        }

        // 绘制已安装模块
        // 渲染所需逻辑
        private float DrawInstalledModules(Rect inRect, float curY, TurretSlot slot)
        {
            // 标题
            Rect headerRect = new Rect(0, curY, inRect.width, 25f);
            Widgets.Label(headerRect, "已安装模块:");
            curY += 25f;

            if (!slot.hasModuleSystem)
            {
                Rect noSystemRect = new Rect(Margin, curY, inRect.width - Margin * 2, RowHeight);
                GUI.color = Color.gray;
                Widgets.Label(noSystemRect, "此炮塔不支持模块系统");
                GUI.color = Color.white;
                return curY + RowHeight;
            }

            // 槽位容量信息
            if (slot.moduleSlotData != null && slot.moduleSlotData.Count > 0)
            {
                foreach (var kvp in slot.moduleSlotData)
                {
                    int remaining = slot.GetSlotRemainingCapacity(kvp.Key);
                    int total = kvp.Value.width;
                    Rect slotInfoRect = new Rect(Margin, curY, inRect.width - Margin * 2, 20f);
                    Widgets.Label(slotInfoRect, $"  槽位 {kvp.Key}: {remaining}/{total} 空闲");
                    curY += 20f;
                }
            }

            curY += 5f;

            // 已安装的模块列表
            int installedCount = 0;

            // 普通模块
            if (slot.storedModules != null)
            {
                foreach (var module in slot.storedModules.ToList())
                {
                    if (module == null) continue;
                    installedCount++;

                    Rect moduleRect = new Rect(Margin, curY, inRect.width - Margin * 2, RowHeight);
                    Rect labelRect = new Rect(moduleRect.x, moduleRect.y, moduleRect.width - 80f, moduleRect.height);
                    Rect buttonRect = new Rect(moduleRect.xMax - 75f, moduleRect.y + 3f, 70f, RowHeight - 6f);

                    int moduleCase = GetModuleCase(module);
                    Widgets.Label(labelRect, $"  · {module.LabelCap} (槽位{moduleCase})");

                    var localModule = module;
                    if (Widgets.ButtonText(buttonRect, "拆卸"))
                    {
                        if (slot.RemoveModule(localModule))
                        {
                            if (wearer?.Map != null)
                            {
                                GenSpawn.Spawn(localModule, wearer.Position, wearer.Map);
                                Messages.Message($"已拆卸 {localModule.LabelCap}", MessageTypeDefOf.NeutralEvent);
                            }
                        }
                    }

                    curY += RowHeight;
                }
            }

            // 浮游炮塔
            if (slot.floatingTurrets != null)
            {
                foreach (var floater in slot.floatingTurrets.ToList())
                {
                    if (floater?.sourceModule == null) continue;
                    installedCount++;

                    Rect moduleRect = new Rect(Margin, curY, inRect.width - Margin * 2, RowHeight);
                    Rect labelRect = new Rect(moduleRect.x, moduleRect.y, moduleRect.width - 80f, moduleRect.height);

                    int moduleCase = GetModuleCase(floater.sourceModule);
                    Widgets.Label(labelRect, $"  · {floater.sourceModule.LabelCap} [浮游] (槽位{moduleCase})");

                    // 浮游炮塔不能单独拆卸
                    curY += RowHeight;
                }
            }

            if (installedCount == 0)
            {
                Rect emptyRect = new Rect(Margin, curY, inRect.width - Margin * 2, RowHeight);
                GUI.color = Color.gray;
                Widgets.Label(emptyRect, "  无已安装模块");
                GUI.color = Color.white;
                curY += RowHeight;
            }

            return curY;
        }

        // 绘制可安装模块
        private float DrawAvailableModules(Rect inRect, float curY, TurretSlot slot)
        {
            // 标题
            Rect headerRect = new Rect(0, curY, inRect.width, 25f);
            Widgets.Label(headerRect, "可安装的模块 (地图上):");
            curY += 25f;

            if (!slot.hasModuleSystem || slot.allowedModuleDefs == null)
            {
                return curY;
            }

            // 滚动区域
            float availableHeight = InitialSize.y - curY - 60f; // 留出底部按钮空间
            Rect outRect = new Rect(0, curY, inRect.width, availableHeight);

            // 计算内容高度
            float contentHeight = 0;
            var availableModules = FindAvailableModules(slot);
            contentHeight = Mathf.Max(availableModules.Count * RowHeight, RowHeight);

            Rect viewRect = new Rect(0, 0, outRect.width - 20f, contentHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);

            float scrollY = 0;

            if (availableModules.Count == 0)
            {
                Rect emptyRect = new Rect(Margin, scrollY, viewRect.width - Margin * 2, RowHeight);
                GUI.color = Color.gray;
                Widgets.Label(emptyRect, "  附近没有可安装的模块");
                GUI.color = Color.white;
            }
            else
            {
                foreach (var moduleInfo in availableModules)
                {
                    Rect moduleRect = new Rect(Margin, scrollY, viewRect.width - Margin * 2, RowHeight);
                    Rect labelRect = new Rect(moduleRect.x, moduleRect.y, moduleRect.width - 90f, moduleRect.height);
                    Rect buttonRect = new Rect(moduleRect.xMax - 85f, moduleRect.y + 3f, 80f, RowHeight - 6f);

                    // 显示模块信息
                    string statusText = moduleInfo.canInstall
                        ? $"(槽位{moduleInfo.moduleCase}: {moduleInfo.remaining}/{moduleInfo.total})"
                        : $"(槽位{moduleInfo.moduleCase}已满)";

                    float distance = (moduleInfo.module.Position - wearer.Position).LengthHorizontal;
                    string distText = $"[{distance:F0}格]";

                    if (moduleInfo.canInstall)
                    {
                        Widgets.Label(labelRect, $"  · {moduleInfo.module.LabelCap} {statusText} {distText}");
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        Widgets.Label(labelRect, $"  · {moduleInfo.module.LabelCap} {statusText}");
                        GUI.color = Color.white;
                    }

                    // 安装按钮
                    if (moduleInfo.canInstall)
                    {
                        var localModule = moduleInfo.module;
                        var localSlot = slot;
                        if (Widgets.ButtonText(buttonRect, "安装"))
                        {
                            // 创建安装工作
                            TryOrderInstallModule(localSlot, localModule);
                        }
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        Widgets.ButtonText(buttonRect, "已满");
                        GUI.color = Color.white;
                    }

                    scrollY += RowHeight;
                }
            }

            Widgets.EndScrollView();

            return curY + availableHeight;
        }

        // 查找可用模块
        private List<ModuleAvailabilityInfo> FindAvailableModules(TurretSlot slot)
        {
            var result = new List<ModuleAvailabilityInfo>();

            if (wearer?.Map == null || slot.allowedModuleDefs == null) return result;

            // 搜索范围
            const float searchRadius = 50f;

            foreach (var moduleDef in slot.allowedModuleDefs)
            {
                // 获取模块槽位ID
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

                bool canInstall = slot.SlotIsAvailable(moduleCase, moduleDef);
                int remaining = slot.GetSlotRemainingCapacity(moduleCase);
                int total = slot.GetSlotTotalCapacity(moduleCase);

                // 在地图上查找该类型的所有模块
                IntVec3 wearerPos = wearer.Position;
                var modules = wearer.Map.listerThings.ThingsOfDef(moduleDef)
                    .Where(t => !t.IsForbidden(wearer) &&
                                (t.Position - wearerPos).LengthHorizontal <= searchRadius &&
                                wearer.CanReach(t, PathEndMode.Touch, Danger.Deadly))
                    .OrderBy(t => (t.Position - wearerPos).LengthHorizontal)
                    .Take(5); // 每种模块最多显示5个

                foreach (var module in modules)
                {
                    result.Add(new ModuleAvailabilityInfo
                    {
                        module = module,
                        moduleDef = moduleDef,
                        moduleCase = moduleCase,
                        canInstall = canInstall,
                        remaining = remaining,
                        total = total
                    });
                }
            }

            // 按距离排序
            IntVec3 sortPos = wearer.Position;
            result = result.OrderBy(m => (m.module.Position - sortPos).LengthHorizontal).ToList();

            return result;
        }

        // 尝试命令安装模块
        private void TryOrderInstallModule(TurretSlot slot, Thing module)
        {
            if (wearer == null || module == null) return;

            // 检查是否可以到达
            if (!wearer.CanReach(module, PathEndMode.Touch, Danger.Deadly))
            {
                Messages.Message("无法到达该模块", MessageTypeDefOf.RejectInput);
                return;
            }

            // 直接拾取并安装（简化流程）
            // 创建一个自定义工作来拾取模块并安装
            Job job = JobMaker.MakeJob(GodHandDefOf.GodHand_InstallTurretModule, module);
            job.count = 1;

            if (wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                Messages.Message($"已命令 {wearer.LabelShort} 去安装 {module.LabelCap}", MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                Messages.Message($"{wearer.LabelShort} 无法执行安装任务", MessageTypeDefOf.RejectInput);
            }
        }

        // 获取模块槽位ID
        private int GetModuleCase(Thing module)
        {
            if (module == null) return 0;

            try
            {
                var prop = module.GetType().GetProperty("moduleCase",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (prop != null)
                {
                    return (int)prop.GetValue(module);
                }

                var field = module.def.GetType().GetField("moduleCase",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    return (int)field.GetValue(module.def);
                }
            }
            catch { }

            return 0;
        }

        // 模块可用性信息
        private class ModuleAvailabilityInfo
        {
            public Thing module;
            public ThingDef moduleDef;
            public int moduleCase;
            public bool canInstall;
            public int remaining;
            public int total;
        }
    }
}
