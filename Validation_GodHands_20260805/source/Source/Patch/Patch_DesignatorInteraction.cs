using HarmonyLib;
using Verse;
using UnityEngine;
using RimWorld;

namespace GodHandMod
{
    // 拦截输入实现分层取消
    [HarmonyPatch(typeof(DesignatorManager), "ProcessInputEvents")]
    public static class Patch_DesignatorManager_ProcessInputEvents
    {
        [HarmonyPrefix]
        public static bool Prefix(DesignatorManager __instance)
        {
            // 仅处理右键点击
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                Designator sel = __instance.SelectedDesignator;

                // 处于交互状态的神之手工具
                if (sel is GodHandDesignatorBase godHandDes && godHandDes.IsInteracting)
                {
                    // 执行内部取消动作 (如释放物体)
                    if (godHandDes.CancelAction())
                    {
                        // 处理成功阻止原版执行
                        Event.current.Use();
                        return false;
                    }
                }
            }

            // 其他走原版逻辑
            return true;
        }
    }
}
