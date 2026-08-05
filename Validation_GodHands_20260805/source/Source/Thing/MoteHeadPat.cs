using UnityEngine;
using Verse;
using System.Collections.Generic;

namespace GodHandMod
{
    // 摸头序列帧动画
    [StaticConstructorOnStartup]
    public class MoteHeadPat : MoteAttached
    {
        private static List<Texture2D> animationFrames = null;
        private static bool framesLoaded = false;

        private int currentFrame = 0;
        private float frameDuration = 0.1f; // 每帧持续时间（秒）
        private float nextFrameTime = 0f;

        // 加载动画帧
        private static void LoadAnimationFrames()
        {
            if (framesLoaded)
                return;

            framesLoaded = true;
            animationFrames = new List<Texture2D>();

            // 加载序列帧
            int frameIndex = 0;
            while (frameIndex < 100) // 最多支持100帧
            {
                string texturePath = $"FX/HeadPat_{frameIndex}";
                Texture2D frame = ContentFinder<Texture2D>.Get(texturePath, false);

                if (frame == null)
                {
                    break;
                }

                animationFrames.Add(frame);
                frameIndex++;
            }

            if (animationFrames.Count == 0)
            {
                Log.Warning("[爱抚型神之手] 没有找到摸头动画序列帧！\n" +
                    "请将序列帧放在 Textures/FX/ 目录：\n" +
                    "HeadPat_0.png, HeadPat_1.png, HeadPat_2.png...");
            }
            else
            {
                GodHandModMain.DebugLog($"[爱抚型神之手] 成功加载 {animationFrames.Count} 帧摸头动画");
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            LoadAnimationFrames();
            nextFrameTime = Time.time;
        }

        protected override void TimeInterval(float deltaTime)
        {
            base.TimeInterval(deltaTime);

            // 更新动画帧
            if (animationFrames != null && animationFrames.Count > 0)
            {
                if (Time.time >= nextFrameTime)
                {
                    currentFrame++;
                    if (currentFrame >= animationFrames.Count)
                    {
                        currentFrame = 0; // 循环播放
                    }
                    nextFrameTime = Time.time + frameDuration;
                }
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (animationFrames == null || animationFrames.Count == 0)
            {
                // 无帧时不绘制
                return;
            }

            // 使用当前帧的texture绘制
            Texture2D currentTexture = animationFrames[currentFrame];

            // 创建临时材质
            Material mat = MaterialPool.MatFrom(currentTexture, ShaderDatabase.Mote, instanceColor);

            // 头部上方绘制
            Vector3 drawPos = DrawPos;

            // 附着时用头位
            if (link1.Linked && link1.Target.HasThing)
            {
                Pawn pawn = link1.Target.Thing as Pawn;
                if (pawn != null && pawn.Drawer?.renderer != null)
                {
                    // 使用接口获取头部偏移
                    Vector3 headOffset = pawn.Drawer.renderer.BaseHeadOffsetAt(pawn.Rotation);

                    // 加上头部偏移
                    drawPos = pawn.DrawPos + headOffset;
                    drawPos.y = Altitudes.AltitudeFor(AltitudeLayer.MetaOverlays);  // 使用更高的渲染层
                    drawPos.z += 0.3f;  // 头部高度偏移
                }
            }

            Vector3 s = ExactScale;
            Matrix4x4 matrix = Matrix4x4.TRS(drawPos, Quaternion.Euler(0f, exactRotation, 0f), new Vector3(s.x, 1f, s.z));

            // 绘制在更高层级
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
        }
    }
}
