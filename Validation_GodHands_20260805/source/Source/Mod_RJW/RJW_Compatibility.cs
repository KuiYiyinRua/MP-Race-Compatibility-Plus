using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using RimWorld;
using rjw;

namespace GodHandMod.RJW
{
    [StaticConstructorOnStartup]
    public static class RJW_Compatibility
    {
        private static bool? isStretcherActive;
        // 检测扩展模组
        public static bool IsStretcherActive => isStretcherActive ??= LoadedModManager.RunningMods.Any(m => m.PackageId.Equals("ElToro.Stretching", StringComparison.OrdinalIgnoreCase));

        // 反射缓存
        private static MethodInfo applyInjuryMethod;
        private static MethodInfo calculateStretchMethod;
        private static HediffDef stretchTearDef;

        static RJW_Compatibility()
        {
            CompatibilityBridge.OnPawnCapturedTick += ApplyOrganExpansion;
            CompatibilityBridge.ShowCustomIcon += ThrowBrokenHeart;
            CompatibilityBridge.GetOrificeLoosenessStage += GetLoosenessStage;

            if (IsStretcherActive)
            {
                InitializeStretcherReflection();
            }
            ModLog.Message("GodHand RJW Compatibility Module (Hardcore) Loaded");
        }

        private static void InitializeStretcherReflection()
        {
            try
            {
                Type stretcherType = GenTypes.GetTypeInAnyAssembly("LLStretcher.Stretcher");
                if (stretcherType != null)
                {
                    // 获取损伤应用方法
                    applyInjuryMethod = stretcherType.GetMethod("ApplyInjury", BindingFlags.Public | BindingFlags.Static);
                    // 获取计算扩张的方法 (使用 Hediff 的重载)
                    calculateStretchMethod = stretcherType.GetMethod("CalculateStretchOrifice", BindingFlags.Public | BindingFlags.Static);
                }
                stretchTearDef = HediffDef.Named("RES_StretchTear");
            }
            catch (Exception e) { ModLog.Error($"Failed to initialize Stretcher reflection: {e.Message}"); }
        }

        public static void ThrowBrokenHeart(Pawn p)
        {
            FleckMaker.ThrowMetaIcon(p.Position, p.Map, FleckDefOf.Heart);
            FleckMaker.ThrowMetaIcon(p.Position, p.Map, xxx.mote_noheart);
        }

        public static int GetLoosenessStage(Pawn p)
        {
            Hediff part = GetPrioritizedPart(p);
            if (part == null) return 0;

            float sev = part.Severity;
            if (sev < 0.6f) return 0; // 紧致 (初期)
            if (sev < 1.0f) return 1; // 适应 (中期)
            if (sev < 1.4f) return 2; // 松弛 (开发中)
            return 3; // 完全开发 (深渊等级)
        }

        private static Hediff GetPrioritizedPart(Pawn pawn)
        {
            var genitals = pawn.GetGenitalsList();
            if (genitals != null && genitals.Count > 0)
            {
                // 优先找阴道
                var vagina = genitals.FirstOrDefault(h => h.def.defName.ToLower().Contains("vagina"));
                if (vagina != null) return vagina;
                return genitals.First();
            }
            // 没有生殖器则找肛门
            return pawn.GetAnusList()?.FirstOrDefault();
        }

        public static void ApplyOrganExpansion(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned) return;

            // 保持裸露图标
            if (pawn.IsHashIntervalTick(250)) SexUtility.DrawNude(pawn);

            // 每 500 tick 进行一次“扩张/交配过程”模拟
            if (pawn.IsHashIntervalTick(500))
            {
                // 检查子模组
                if (!IsStretcherActive) return;

                Hediff part = GetPrioritizedPart(pawn);
                if (part == null) return;

                // 尝试应用子 Mod 的扩张逻辑
                ExecuteStretcherLogic(pawn, part);
            }
        }

        private static void ExecuteStretcherLogic(Pawn pawn, Hediff orifice)
        {
            if (calculateStretchMethod == null) return;

            try
            {
                // 规格设定
                var penDims = (length: 100f, girth: 20f);

                // 获取器官当前尺寸
                // (模拟 Stretcher 的内部调用)
                float currentLength = PartSizeCalculator.TryGetLength(orifice, out var l) ? l : orifice.Severity * 20f;
                float currentGirth = PartSizeCalculator.TryGetGirth(orifice, out var g) ? g : orifice.Severity * 15f;
                var oriDims = (length: currentLength, girth: currentGirth);

                // 调用 CalculateStretchOrifice
                // 方法签名
                object[] args = new object[] { pawn, pawn, orifice, penDims, oriDims, 0f, 0f, 0f };
                float newSeverity = (float)calculateStretchMethod.Invoke(null, args);
                float maxDamage = (float)args[7];

                // 更新尺寸
                if (orifice.TryGetComp<HediffComp_SexPart>(out var comp))
                {
                    // 限制深度
                    comp.baseSize = Math.Min(newSeverity, 2.0f) * pawn.BodySize;
                    comp.UpdateSeverity();
                }

                // 应用损伤
                // 如果损伤大于阈值且反射成功
                if (maxDamage > 1.0f && applyInjuryMethod != null && stretchTearDef != null)
                {
                    applyInjuryMethod.Invoke(null, new object[] { pawn, orifice.Part, stretchTearDef, maxDamage });
                }

                // 添加状态
                if (!pawn.health.hediffSet.HasHediff(GodHandDefOf.GodHand_OrificeStretchingStatus))
                {
                    pawn.health.AddHediff(GodHandDefOf.GodHand_OrificeStretchingStatus);
                }
            }
            catch (Exception e) { ModLog.Error($"Error executing Stretcher logic: {e.Message}"); }
        }
    }
}
