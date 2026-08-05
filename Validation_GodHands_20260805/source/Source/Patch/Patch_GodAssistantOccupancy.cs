using HarmonyLib;
using RimWorld;
using Verse;

namespace GodHandMod
{
    // 防止其他 Pawn 在神之手工作时干扰工作台
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.CurrentlyUsableForBills))]
    public static class Patch_GodAssistantOccupancy
    {
        [HarmonyPostfix]
        public static void Postfix(Building_WorkTable __instance, ref bool __result)
        {
            if (!__result) return;

            // 神之手占用则表现为不可用
            var map = __instance.Map;
            if (map != null)
            {
                var comp = map.GetComponent<MapComponent_GodAssistant>();
                if (comp != null && comp.IsTableBusy(__instance))
                {
                    __result = false;
                }
            }
        }
    }
}
