using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class BuildBundle
{
    private static string OutputPath = "Assets/StreamingAssets";

    public static void BuildFromCommandLine()
    {
        if (!Directory.Exists(OutputPath)) Directory.CreateDirectory(OutputPath);

        // 仅打包核心 Shader
        string[] targetAssets = new string[]
        {
            "Assets/Shaders/ObjectOutline.shader",
            "Assets/Shaders/ShieldDissolve.shader",
            "Assets/Shaders/StencilMask.shader",
            "Assets/Shaders/JFAShaders.shader",
            "Assets/Shaders/JFA_DebugCapture.shader"
        };

        List<string> validAssets = new List<string>();
        foreach (var path in targetAssets)
        {
            if (File.Exists(path)) validAssets.Add(path);
            else Debug.LogWarning($"[BuildBundle] Missing target asset: {path}");
        }

        if (validAssets.Count == 0)
        {
            Debug.LogError("[BuildBundle] No valid assets found to build!");
            EditorApplication.Exit(1);
            return;
        }

        AssetBundleBuild[] builds = new AssetBundleBuild[1];
        builds[0].assetBundleName = "godhand_visuals";
        builds[0].assetNames = validAssets.ToArray();

        BuildPipeline.BuildAssetBundles(OutputPath, builds, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        Debug.Log("[BuildBundle] Build Complete.");
    }
}
