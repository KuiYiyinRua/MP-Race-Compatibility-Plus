using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace GodHandMod
{
    // 拦截任务防止抢占工作台
    [HarmonyPatch(typeof(WorkGiver_DoBill), "JobOnThing")]
    public static class Patch_WorkGiver_DoBill
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn pawn, Thing thing, ref Job __result)
        {
            if (thing is Building_WorkTable table)
            {
                var map = table.Map;
                if (map != null)
                {
                    var comp = map.GetComponent<MapComponent_GodAssistant>();
                    if (comp != null && comp.IsTableBusy(table))
                    {
                        // 占用时不分配任务
                        __result = null;
                        return false; // 跳过原方法
                    }
                }
            }
            return true;
        }
    }
}
