using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace GodHandMod
{
    [StaticConstructorOnStartup]
    public static class JFAOutlineRenderer
    {

        private static Material jfaLogicMat;
        private static Material JFALogicMat => jfaLogicMat ??= (AssetLoader.JFAShader != null ? new Material(AssetLoader.JFAShader) : null);
        private static RenderTexture cachedMask;

        // 独立相机隔离渲染
        private static Camera captureCam;
        private static Camera CaptureCam
        {
            get
            {
                if (captureCam == null)
                {
                    GameObject go = new GameObject("JFA_CaptureCam_Fixed");
                    captureCam = go.AddComponent<Camera>();
                    captureCam.enabled = false;
                    captureCam.orthographic = true;
                    captureCam.clearFlags = CameraClearFlags.SolidColor;
                    captureCam.backgroundColor = Color.clear;
                    captureCam.nearClipPlane = 0.1f;
                    captureCam.farClipPlane = 100f;
                    captureCam.cullingMask = 0; // 手动绘制不裁剪
                    UnityEngine.Object.DontDestroyOnLoad(go);
                }
                return captureCam;
            }
        }

        public static void RenderOutline(Vector3 center, List<(Mesh mesh, Matrix4x4 matrix)> parts, float thickness, Color color)
        {
            if (parts == null || parts.Count == 0 || AssetLoader.JFAShader == null) return;

            // 锁定格位
            Vector3 captureCenter = center;
            // 确保覆盖
            float captureRadius = 2.5f;

            // 配置纹理
            int res = 1024;
            if (cachedMask != null && (cachedMask.width != res || !cachedMask.IsCreated())) { cachedMask.Release(); cachedMask = null; }
            if (cachedMask == null)
            {
                cachedMask = new RenderTexture(res, res, 16, RenderTextureFormat.ARGB32)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 4,
                };
                cachedMask.Create();
            }

            // 配置相机
            var cam = CaptureCam;
            cam.targetTexture = cachedMask;
            cam.orthographicSize = captureRadius;
            cam.transform.position = captureCenter + new Vector3(0, 10, 0);
            cam.transform.rotation = Quaternion.Euler(90, 0, 0); // 朝正下方

            // 渲染逻辑
            var prevRT = RenderTexture.active;
            RenderTexture.active = cachedMask;
            GL.Clear(true, true, Color.clear);

            // 设置相机的投影和视图矩阵到 GL 上下文
            cam.projectionMatrix = Matrix4x4.Ortho(-captureRadius, captureRadius, -captureRadius, captureRadius, 0.01f, 50f);
            cam.worldToCameraMatrix = cam.transform.worldToLocalMatrix;

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            GL.Viewport(new Rect(0, 0, res, res));

            var mat = JFALogicMat;
            foreach (var p in parts)
            {
                // 渲染遮罩通道
                Graphics.DrawMeshNow(p.mesh, p.matrix);
            }

            GL.PopMatrix();
            RenderTexture.active = prevRT;

            // 输出结果
            RenderTexture.active = prevRT;
        }
    }
}
