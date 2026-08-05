using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    // 手感发电控制器
    [StaticConstructorOnStartup]
    public static class GodHandGeneratorController
    {
        private static ModelDef handCrankModel;
        private static ModelDef gearboxModel;
        private static bool initialized = false;

        private static CrankState toolState = new CrankState();
        private static float lastMouseAngle = 0f;
        private static Thing currentTarget;

        public static float CurrentOutputWatts => toolState.OutputWattsSafe;
        public static float CurrentRPM => toolState.RPM;
        public static Thing CurrentTarget => currentTarget;

        private static void EnsureInitialized()
        {
            if (initialized) return;
            handCrankModel = DefDatabase<ModelDef>.GetNamedSilentFail("GodHand_HandCrank");
            gearboxModel = DefDatabase<ModelDef>.GetNamedSilentFail("GodHand_GearboxGenerator");
            endRodModel = DefDatabase<ModelDef>.GetNamedSilentFail("GodHand_EndRodGenerator");
            initialized = true;
        }

        private static Thing lastTarget;

        public static void Update(Thing target, Vector3 mousePos)
        {
            currentTarget = target;
            if (target == null || !target.Spawned)
            {
                toolState.outputWatts = 0;
                toolState.smoothedVelocity = 0;
                lastTarget = null;
                return;
            }
            EnsureInitialized();

            Vector3 center = target.DrawPos;
            Vector2 direction = (mousePos - center).ToVector2();
            float angleToMouse = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;

            // 重置起始夹角
            if (target != lastTarget)
            {
                lastMouseAngle = angleToMouse;
                lastTarget = target;
            }

            float delta = Mathf.DeltaAngle(lastMouseAngle, angleToMouse);
            lastMouseAngle = angleToMouse;
            toolState.currentAngle = angleToMouse;

            toolState.UpdatePhysicsAndPower(Mathf.Abs(delta), target);
            RenderHandCrank(center, toolState.currentAngle, direction.magnitude);
        }

        private static void RenderHandCrank(Vector3 pos, float angle, float distToMouse)
        {
            if (handCrankModel == null) return;
            var parts = handCrankModel.GetParts();
            if (parts.NullOrEmpty()) return;

            float baseS = 1.0f / 16f;
            Quaternion rotation = Quaternion.Euler(0, angle + 90, 0) * Quaternion.Euler(90, 0, 0);
            Vector3 drawPos = pos + new Vector3(0, 2.0f, 0);
            Material outlineMat = SolidColorMaterials.SimpleSolidColorMaterial(Color.black);

            // 绘制本体
            foreach (var part in parts)
            {
                Vector3 partScale = Vector3.one * baseS;
                Vector3 partOffset = Vector3.zero;
                float armBaseLen = 14f * baseS;

                if (part.partName == "Arm_Body")
                {
                    float extension = Mathf.Max(0, distToMouse - armBaseLen);
                    partScale.x *= 1.0f + (extension / (1f * baseS));
                    // 补偿锚点坐标差
                    partOffset = rotation * new Vector3(-baseS, 0, 0);
                }
                else if (part.partName == "Arm_Head" || part.partName.Contains("Grip") || part.partName.Contains("Connector2"))
                {
                    float extension = Mathf.Max(0, distToMouse - armBaseLen);
                    partOffset = rotation * new Vector3(-extension, 0, 0);
                }

                Matrix4x4 matrix = Matrix4x4.TRS(drawPos + partOffset, rotation, partScale);
                Graphics.DrawMesh(part.mesh, matrix, part.material, 0);
            }

            // 绘制下沉描边
            foreach (var part in parts)
            {
                if (part.outlineMesh == null) continue;

                Vector3 partScale = Vector3.one * baseS;
                Vector3 partOffset = Vector3.zero;
                float armBaseLen = 14f * baseS;

                if (part.partName == "Arm_Body")
                {
                    float extension = Mathf.Max(0, distToMouse - armBaseLen);
                    partScale.x *= 1.0f + (extension / (1f * baseS));
                    // 补偿锚点坐标差
                    partOffset = rotation * new Vector3(-baseS, 0, 0);
                }
                else if (part.partName == "Arm_Head" || part.partName.Contains("Grip") || part.partName.Contains("Connector2"))
                {
                    float extension = Mathf.Max(0, distToMouse - armBaseLen);
                    partOffset = rotation * new Vector3(-extension, 0, 0);
                }

                // 描边深度偏移修复遮挡
                Vector3 outlineSink = new Vector3(0, -GodHandResources.OutlineDepthOffset, 0);
                Graphics.DrawMesh(part.outlineMesh, Matrix4x4.TRS(drawPos + partOffset + outlineSink, rotation, partScale), outlineMat, 0);
            }
        }

        private static readonly Material BlackOutlineMat = SolidColorMaterials.SimpleSolidColorMaterial(Color.black);
        private static readonly Material GhostOutlineMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0, 0, 0, 0.5f));

        // 渲染齿轮箱
        public static void RenderGearbox(Vector3 pos, Rot4 rot, float angle, bool isGhost = false)
        {
            EnsureInitialized();
            if (gearboxModel == null) return;
            var parts = gearboxModel.GetParts();
            if (parts.NullOrEmpty()) return;

            float baseS = 1.0f / 16f;
            Quaternion viewTilt = Quaternion.Euler(20, 0, 0);

            // 本体旋转模式
            bool absurd = GodHandModMain.Settings.enableAbsurdRotation;
            float bodyAngle = absurd ? angle : 0f;

            Quaternion baseRot = viewTilt * rot.AsQuat * Quaternion.Euler(0, 0, bodyAngle);
            // 实体渲染高度同步上调
            Vector3 drawPos = pos + new Vector3(0, isGhost ? 2.05f : 2.2f, 0.2f);
            Material outlineMat = isGhost ? GhostOutlineMat : BlackOutlineMat;

            foreach (var part in parts)
            {
                // 角度抵开处理
                Quaternion partRot = GetPartRotation(baseRot, part, angle, absurd);
                Matrix4x4 matrix = Matrix4x4.TRS(drawPos, partRot, Vector3.one * baseS);

                // 虚影模式下使用透明材质
                Material renderMat = isGhost ? FadedMaterialPool.FadedVersionOf(part.material, 0.5f) : part.material;
                Graphics.DrawMesh(part.mesh, matrix, renderMat, 0);
            }

            // 统一描边
            Vector3 sink = new Vector3(0, -GodHandResources.OutlineDepthOffset, 0);
            foreach (var part in parts)
            {
                if (part.outlineMesh == null) continue;
                Quaternion partRot = GetPartRotation(baseRot, part, angle, absurd);
                Graphics.DrawMesh(part.outlineMesh, Matrix4x4.TRS(drawPos + sink, partRot, Vector3.one * baseS), outlineMat, 0);
            }
        }

        private static Quaternion GetPartRotation(Quaternion baseRot, ModelPart part, float angle, bool isAbsurd = false)
        {
            if (!part.isDynamic) return baseRot;

            // 基础啮合逻辑
            float finalAngle = part.partName.Contains("Reverse") ? -angle : angle;

            if (isAbsurd)
            {
                // 锁定手柄位置
                {
                    finalAngle = -angle;
                }
                // 传动轴保持同步轴向旋转
            }

            if (part.partName.Contains("Horizontal"))
            {
                finalAngle = -finalAngle;
                return baseRot * Quaternion.Euler(finalAngle, 0, 0);
            }
            return baseRot * Quaternion.Euler(0, 0, finalAngle);
        }

        // 打印静态组件
        public static void PrintGearbox(SectionLayer layer, Vector3 pos, Rot4 rot)
        {
            EnsureInitialized();
            if (gearboxModel == null || GodHandModMain.Settings.enableAbsurdRotation) return;
            var parts = gearboxModel.GetParts();
            if (parts.NullOrEmpty()) return;

            float baseS = 1.0f / 16f;
            // 统一使用 20 度俯视倾斜
            Quaternion viewTilt = Quaternion.Euler(20, 0, 0);
            Quaternion baseRot = viewTilt * rot.AsQuat;
            Vector3 drawPos = pos + new Vector3(0, 0.2f, 0.2f);

            foreach (var part in parts)
            {
                if (part.isDynamic) continue;

                Printer_Mesh.PrintMesh(layer, Matrix4x4.TRS(drawPos, baseRot, Vector3.one * baseS), part.mesh, part.material);
                if (part.outlineMesh != null)
                {
                    Printer_Mesh.PrintMesh(layer, Matrix4x4.TRS(drawPos - new Vector3(0, GodHandResources.OutlineDepthOffset, 0), baseRot, Vector3.one * baseS), part.outlineMesh, BlackOutlineMat);
                }
            }
        }

        // 应用 3D 图标
        public static void GenerateAndApplyIcon()
        {
            EnsureInitialized();
            ThingDef def = ThingDef.Named("GodHand_GearboxGenerator");
            // 动态构建3D图标
        }

        private static ModelDef endRodModel;

        // 渲染永动机 (草羊机/永动机)
        public static void RenderEndRodMachine(Vector3 pos, Rot4 rot, float time, Building_EndRodGenerator b = null)
        {
            EnsureInitialized();
            if (endRodModel == null) return;
            var parts = endRodModel.GetParts();
            if (parts.NullOrEmpty()) return;

            float baseS = 1.0f / 16f;
            // 统一使用 20 度俯视倾斜
            Quaternion viewTilt = Quaternion.Euler(20, 0, 0);
            Quaternion baseRot = viewTilt * rot.AsQuat;

            // 状态同步
            float pistonProgress = (b != null) ? b.PistonExtendProgress : 0f;
            bool isObserverRemoved = (b != null) && b.IsObserverRemoved;
            bool isGhost = (b == null); // If b is null, it's a blueprint/ghost

            // 动态偏移量 (伸出距离)
            float extensionDist = pistonProgress * 14f * baseS;

            // 模型整体平移修正
            Vector3 alignOffset = new Vector3(0, 0, -2f);

            // 渲染高度加 2：实体 2.2f, 预览/蓝图 2.05f (防止贴地闪烁)
            Vector3 center = pos + new Vector3(0, (b != null) ? 2.2f : 2.05f, 0);

            // 1. 绘制本体部件
            foreach (var part in parts)
            {
                // 观察者状态检查
                if (isObserverRemoved && part.partName.StartsWith("Observer")) continue;

                Vector3 partAnimOffset = GetEndRodPartAnimationOffset(part.partName, extensionDist);
                Vector3 finalPos = center + rot.AsQuat * (alignOffset + partAnimOffset);

                Matrix4x4 mat = Matrix4x4.TRS(finalPos, baseRot, Vector3.one * baseS);

                // 虚影模式下使用透明材质
                Material renderMat = isGhost ? FadedMaterialPool.FadedVersionOf(part.material, 0.5f) : part.material;
                Material currentOutlineMat = isGhost ? GhostOutlineMat : BlackOutlineMat;

                // 绘制本体
                Graphics.DrawMesh(part.mesh, mat, renderMat, 0);

                // 绘制描边
                if (part.outlineMesh != null)
                {
                    Vector3 sink = new Vector3(0, -GodHandResources.OutlineDepthOffset, 0);
                    Graphics.DrawMesh(part.outlineMesh, Matrix4x4.TRS(finalPos + sink, baseRot, Vector3.one * baseS), currentOutlineMat, 0);
                }
            }

            // 2. 绘制额外效果 (仅限真实建筑)
            if (b != null)
            {
                // 使用内部暴露的属性渲染悬浮物和动画
                // 注意：这里需要 Building 内部逻辑配合，或者在这里重现逻辑
            }
        }

        private static Vector3 GetEndRodPartAnimationOffset(string partName, float extensionDist)
        {
            if (partName == "Piston_Head" || partName == "Piston_Rod" || partName == "End_Rod")
            {
                return new Vector3(0, 0, extensionDist);
            }
            if (partName == "Boat")
            {
                // 向内移动 0.2 格 (0.2f -> 0f), 且移动距离减小为 2/16
                return new Vector3(0, 0, 0.0f + extensionDist * (2f / 16f));
            }
            return Vector3.zero;
        }
    }

    public class CrankState
    {
        public float currentAngle = 0f;
        public float smoothedVelocity = 0f;
        public float outputWatts = 0f;
        public float lastUpdateTime = 0f;

        public float OutputWattsSafe
        {
            get
            {
                // 停止超时判断
                if (Time.realtimeSinceStartup - lastUpdateTime > 0.2f) return 0f;
                return outputWatts;
            }
        }

        public float RPM => (Mathf.Abs(smoothedVelocity) / 360f) * 60f;

        public void UpdatePhysicsAndPower(float deltaDegrees, Thing target)
        {
            float rawVel = deltaDegrees / Time.deltaTime;
            // 帧率无关阻尼计算
            // 修正旧Lerp算法
            // 设定半衰期
            smoothedVelocity = Mathf.Lerp(smoothedVelocity, rawVel, 1f - Mathf.Exp(-10f * Time.deltaTime));

            var s = GodHandModMain.Settings;
            float normalizedSpeed = Mathf.Abs(smoothedVelocity) / 360f;
            float currentRPM = normalizedSpeed * 60f;

            // 计算输出功率
            outputWatts = Mathf.Pow(normalizedSpeed, s.generatorExponent) * 2000f * s.godCrankPowerMultiplier;

            lastUpdateTime = Time.realtimeSinceStartup;

            // FTL超光速模式
            if (s.enableFTLCrank && currentRPM > s.ftlRPMThreshold)
            {
                outputWatts = float.PositiveInfinity; // 无限能量输出
            }

            if (Mathf.Abs(smoothedVelocity) < 10f) outputWatts = 0;
        }
    }
}
