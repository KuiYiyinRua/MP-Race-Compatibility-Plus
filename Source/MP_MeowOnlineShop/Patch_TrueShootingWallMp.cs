using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// True Shooting Wall (Laayoune.shootingWall) stores its active zone
    /// pattern in CompShootingWall.rotationIndex. The rotate gizmo mutates it
    /// from a UI lambda, so only the clicking peer rotates unless that callback
    /// is synchronized (Desync-281).
    /// </summary>
    internal static class Patch_TrueShootingWallMp
    {
        private const string CompTypeName = "TrueShootingWall.CompShootingWall";
        private const string GizmoMethodName = "CompGetGizmosExtra";
        private const string LambdaNamePrefix = "<" + GizmoMethodName + ">b__";

        private static bool _registered;

        internal static void Apply()
        {
            if (_registered || !MP.enabled)
                return;

            Type compType = AccessTools.TypeByName(CompTypeName);
            if (compType == null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] True Shooting Wall MP: Laayoune.shootingWall " +
                    "is not active; patch skipped.");
                return;
            }

            MethodInfo rotateLambda = FindRotateLambda(compType);
            if (rotateLambda == null)
                return;

            try
            {
                MP.RegisterSyncMethod(rotateLambda, null)
                    .SetContext(SyncContext.MapSelected);
                _registered = true;
                Log.Message(
                    "[MP-MeowOnlineShop] True Shooting Wall MP active: rotate gizmo " +
                    "callback synchronized (" + rotateLambda.Name + ").");
            }
            catch (Exception e)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] True Shooting Wall MP registration failed: " +
                    e.Message);
            }
        }

        private static MethodInfo FindRotateLambda(Type compType)
        {
            MethodInfo[] candidates = compType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name.StartsWith(LambdaNamePrefix, StringComparison.Ordinal)
                            && m.ReturnType == typeof(void)
                            && m.GetParameters().Length == 0)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();

            if (candidates.Length != 1)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop] True Shooting Wall MP: expected one " +
                    LambdaNamePrefix + "* void() rotate callback, found " +
                    candidates.Length + "; target left unregistered.");
                return null;
            }

            return candidates[0];
        }
    }
}
