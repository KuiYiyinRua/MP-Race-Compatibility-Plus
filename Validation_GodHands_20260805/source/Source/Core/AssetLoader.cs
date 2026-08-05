using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace GodHandMod
{
    [StaticConstructorOnStartup]
    public static class AssetLoader
    {
        public static Shader ShieldDissolveShader;
        public static Shader ObjectOutlineShader; // 暴露轮廓着色器
        public static Shader StencilMaskShader; // 遮罩着色器
        public static Shader JFAShader; // JFA 核心着色器
        public static Shader DebugCaptureShader; // 简易剪影着色器
        public static AssetBundle MainBundle;

        static AssetLoader()
        {
            // 在 LongEventHandler 中加载
            LongEventHandler.ExecuteWhenFinished(LoadAssets);
        }

        private static void LoadAssets()
        {
            try
            {
                // 加载主资源包
                MainBundle = FindBundle("godhand_visuals");
                if (MainBundle != null)
                {
                    GodHandModMain.DebugLog("[神之手] 已找到视觉包 资产清单");
                    var allNames = MainBundle.GetAllAssetNames();
                    foreach (var s in allNames)
                    {
                        GodHandModMain.DebugLog($"[神之手] - {s}");
                    }

                    // 部分名称匹配加载着色器
                    ShieldDissolveShader = LoadShader(MainBundle, allNames, "ShieldDissolve");
                    ObjectOutlineShader = LoadShader(MainBundle, allNames, "ObjectOutline");
                    StencilMaskShader = LoadShader(MainBundle, allNames, "StencilMask");
                    JFAShader = LoadShader(MainBundle, allNames, "JFAShaders");
                    DebugCaptureShader = LoadShader(MainBundle, allNames, "JFA_DebugCapture");

                    Log.Message($"[神之手] 视觉资源加载: 轮廓着色器={(ObjectOutlineShader != null ? "成功" : "失败")}, 遮罩着色器={(StencilMaskShader != null ? "成功" : "失败")}, JFA着色器={(JFAShader != null ? "成功" : "失败")}");
                }
                else
                {
                    Log.Error("[神之手] 错误：找不到视觉包 godhand_visuals");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[神之手] 资产加载溢出 {ex}");
            }
        }

        private static Shader LoadShader(AssetBundle bundle, string[] allNames, string partialName)
        {
            foreach (var name in allNames)
            {
                if (name.Contains(partialName.ToLower()))
                {
                    return bundle.LoadAsset<Shader>(name);
                }
            }
            Log.Warning($"[神之手] 警告 无法在包中找到 {partialName}");
            return null;
        }

        private static AssetBundle FindBundle(string bundleName)
        {
            // 从运行模组列表提取已加载包
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
            {
                // 匹配神之手 PackageId
                if (mod.PackageId.ToLower().Contains("palpha.godhands"))
                {
                    if (mod.assetBundles == null || mod.assetBundles.loadedAssetBundles == null) continue;

                    foreach (AssetBundle bundle in mod.assetBundles.loadedAssetBundles)
                    {
                        if (bundle.name.Contains(bundleName)) return bundle;
                    }
                }
            }
            return null;
        }
    }
}
