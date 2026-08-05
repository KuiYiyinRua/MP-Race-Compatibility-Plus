using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace GodHandMod
{
    // 劫持电力产出逻辑
    [HarmonyPatch(typeof(PowerNet), nameof(PowerNet.CurrentEnergyGainRate))]
    public static class Patch_PowerNet_CurrentEnergyGainRate
    {
        // 劫持电网结算时的发电速率
        [HarmonyPostfix]
        public static void Postfix(PowerNet __instance, ref float __result)
        {
            try
            {
                // 如果当前正在手摇某个目标，且该目标属于此电网
                float watts = GodHandGeneratorController.CurrentOutputWatts;
                if (watts > 0)
                {
                    // 修正：PowerNet.CurrentEnergyGainRate() 返回的单位是能量/Tick (Watt-Days per Tick)
                    // 必须将瓦特 W 转换为 Wd/Tick 才能正确叠加
                    __result += watts * CompPower.WattsToWattDaysPerTick;
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
