using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace GodHandMod
{
    // 位移补丁
    [HarmonyPatch(typeof(PawnRenderNode), nameof(PawnRenderNode.GetTransform))]
    public static class HarmonyPatch_ForceCrawlingTransform
    {
        [HarmonyPostfix]
        public static void Postfix(PawnRenderNode __instance, PawnDrawParms parms, ref Vector3 offset, ref Vector3 pivot, ref Quaternion rotation, ref Vector3 scale)
        {
            // 只针对被机器捕获的 Pawn 生效
            if (parms.pawn == null || !CompPawnCaptureOnTouch.pawnToGenerator.TryGetValue(parms.pawn, out var generator))
                return;

            if (generator == null || !generator.Spawned) return;

            // 过滤基节点
            if (__instance.Props.tagDef != PawnRenderNodeTagDefOf.Body &&
                __instance.Props.tagDef != PawnRenderNodeTagDefOf.Head)
                return;

            // 获取当前方向的配置数据
            int rotInt = generator.Rotation.AsInt;
            if (!PoseDebugData.RotData.ContainsKey(rotInt)) return;
            PoseData data = PoseDebugData.RotData[rotInt];

            // 获取船的位置
            Vector3 boatWorldPos = generator.GetPistonTipPosition();

            // 计算位移
            Vector3 currentDrawPos = parms.pawn.DrawPos;
            Vector3 delta = boatWorldPos - currentDrawPos;

            // 修正空间
            // 逆转旋转
            Vector3 localDelta = Quaternion.Inverse(parms.pawn.Rotation.AsQuat) * delta;

            // 直接应用配置里的偏移 (叠加转换后的动态位移)
            Vector3 totalOffset = localDelta + data.PosOffset + new Vector3(0, data.AltitudeOffset, 0);

            // 叠加头部偏移
            if (__instance.Props.tagDef == PawnRenderNodeTagDefOf.Head)
            {
                totalOffset += data.HeadPosOffset;
            }

            // 应用位移
            offset += totalOffset;
        }
    }

    // 强制爬行角度
    // 复用爬行逻辑
    [HarmonyPatch(typeof(PawnRenderer), "BodyAngle")]
    public static class HarmonyPatch_PawnBodyAngle
    {
        [HarmonyPrefix]
        public static bool Prefix(PawnRenderer __instance, ref float __result, Pawn ___pawn)
        {
            if (___pawn != null && CompPawnCaptureOnTouch.pawnToGenerator.TryGetValue(___pawn, out var generator))
            {
                // 获取当前方向的配置数据
                if (generator != null && PoseDebugData.RotData.TryGetValue(generator.Rotation.AsInt, out var data))
                {
                    // 使用调试器设定的角度 (叠加原角度)
                    __result = ___pawn.Rotation.AsAngle + data.AngleOffset;
                    return false; // 拦截并覆盖
                }
            }
            return true;
        }
    }

    // 强制爬行状态
    // 自动处理细节
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Crawling), MethodType.Getter)]
    public static class HarmonyPatch_Pawn_Crawling
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (__instance != null && CompPawnCaptureOnTouch.pawnToGenerator.ContainsKey(__instance))
            {
                __result = PoseDebugData.ForceCrawling; // 强制设为 true (可调试)
            }
        }
    }
}
