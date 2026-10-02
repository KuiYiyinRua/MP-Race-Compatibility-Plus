using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Smelted Loong (`ny.smeltedloong`) adds two ability-mode toggles whose
    /// saved fields change the outcome of later ability Apply calls. Both are
    /// only written by gizmo callbacks on the clicking peer, so registering
    /// the fields as SyncFields is the narrowest boundary.
    /// </summary>
    internal static class Patch_SmeltedLoongMp
    {
        private const string PackageId = "ny.smeltedloong";
        private const string GiveHediffTypeName = "SmeltedLoong.AC_GiveHediffDual";
        private const string GeneEditingTypeName = "SmeltedLoong.AC_GeneEditing";
        private const int SmartTraceSeedSalt = 0x534C4254; // "SLBT"
        private static readonly string[] BlackBirdPermitTypeNames =
        {
            "SmeltedLoong.BlackBirdPermitWorker_Offensive",
            "SmeltedLoong.BlackBirdPermitWorker_Defensive",
            "SmeltedLoong.BlackBirdPermitWorker_Reinforce"
        };

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || !MP.enabled || !ModsConfig.IsActive(PackageId))
                return;

            PatchSmartTrace(harmony);
            int registered = 0;
            registered += RegisterField(AccessTools.TypeByName(GiveHediffTypeName), "changeHediff");
            registered += RegisterField(AccessTools.TypeByName(GeneEditingTypeName), "changeMode");
            foreach (string typeName in BlackBirdPermitTypeNames)
            {
                registered += RegisterMethod(
                    AccessTools.TypeByName(typeName),
                    "OrderForceTarget",
                    new[] { typeof(LocalTargetInfo) });
            }

            if (registered == 0)
                Log.Warning("[MP-MeowOnlineShop] Smelted Loong target resolution failed; patch skipped.");
            else
                Log.Message("[MP-MeowOnlineShop] Smelted Loong MP patch active: " + registered + " sync fields.");
        }

        private static void PatchSmartTrace(Harmony harmony)
        {
            try
            {
                Type type = AccessTools.TypeByName("SmeltedLoong.Bullet_SmartTrace");
                MethodInfo init = type == null ? null : AccessTools.DeclaredMethod(type, "InitRandOffset", Type.EmptyTypes);
                FieldInfo initialized = type == null ? null : AccessTools.DeclaredField(type, "randInitialized");
                if (type == null || !typeof(Projectile).IsAssignableFrom(type) || init == null ||
                    init.IsStatic || init.ReturnType != typeof(void) || initialized?.FieldType != typeof(bool))
                    throw new MissingMethodException("SmeltedLoong.Bullet_SmartTrace.InitRandOffset exact target");
                harmony.Patch(init,
                    prefix: new HarmonyMethod(typeof(Patch_SmeltedLoongMp), nameof(SmartTracePrefix)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(Patch_SmeltedLoongMp), nameof(SmartTraceFinalizer)) { priority = Priority.Last });
                Log.Message("[MP-MeowOnlineShop][Desync335] READY Smelted Loong smart-trace offset: stable projectile seed, shared Rand preserved in draw/tick/sync contexts.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][Desync335] REQUIRED_TARGET_FAILED smart-trace offset: " + e);
            }
        }

        private static void SmartTracePrefix(Thing __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer) return;
            // Desync335/337/338/339: DrawAt and Tick both reach the same lazy
            // BPos initializer. Rendering may run first on only one peer, and
            // rejoining reconstructs an empty cache. This offset is cosmetic:
            // native Projectile.TickInterval/impact use the straight trajectory.
            // Do not key it to current tick, focus, map attachment, or sync-command
            // context: any of those can differ at the first local draw.
            Rand.PushState(Gen.HashCombineInt(__instance.thingIDNumber, SmartTraceSeedSalt));
            __state = true;
        }

        private static void SmartTraceFinalizer(bool __state)
        {
            if (__state) Rand.PopState();
        }

        private static int RegisterField(Type type, string fieldName)
        {
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            if (field == null)
                return 0;

            try
            {
                MP.RegisterSyncField(field);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Smelted Loong sync field failed on " + fieldName + ": " + e.Message);
                return 0;
            }
        }

        private static int RegisterMethod(Type type, string methodName, Type[] argTypes)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, methodName, argTypes);
            if (method == null)
                return 0;

            try
            {
                MP.RegisterSyncMethod(method, null);
                return 1;
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Smelted Loong sync method failed on " + methodName + ": " + e.Message);
                return 0;
            }
        }
    }
}
