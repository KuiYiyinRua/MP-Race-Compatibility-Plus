using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

// The Naglfar middle turret's world-support targeting callback spawns both
// skyfallers and a fire-mission projectile, then writes its cooldown.
[StaticConstructorOnStartup]
public static class PLAMilira_NaglfarWorldBombardment
{
    static Type compType, closureType;
    static MethodInfo targetCallback;
    static FieldInfo capturedMap, capturedComp, cooldown;
    static bool InUi => MP.IsInMultiplayer && MP.InInterface && !MP.IsExecutingSyncCommand;

    static PLAMilira_NaglfarWorldBombardment()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }

    static void Install()
    {
        try
        {
            compType = AccessTools.TypeByName("PLAMilira.CompPLAMilira_TurretGun_Building")
                ?? throw new TypeLoadException("PLAMilira.CompPLAMilira_TurretGun_Building");
            var callbacks = compType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(AccessTools.GetDeclaredMethods)
                .Where(m => m.Name == "<StartTargetingWithThing>b__0" && m.ReturnType == typeof(void) &&
                    m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(LocalTargetInfo) }) &&
                    PatchProcessor.GetOriginalInstructions(m).Any(i => i.operand is MethodBase called && called.Name == "SpawnSkyfaller"))
                .ToArray();
            if (callbacks.Length != 1) throw new MissingMethodException("Naglfar world support target callback count=" + callbacks.Length);
            targetCallback = callbacks[0];
            closureType = targetCallback.DeclaringType;
            capturedMap = AccessTools.DeclaredField(closureType, "map") ?? throw new MissingFieldException(closureType.FullName, "map");
            capturedComp = AccessTools.DeclaredField(closureType, "<>4__this") ?? throw new MissingFieldException(closureType.FullName, "<>4__this");
            if (closureType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length != 2 ||
                capturedMap.FieldType != typeof(Map) || capturedComp.FieldType != compType)
                throw new InvalidOperationException("Naglfar world support captures changed");
            cooldown = AccessTools.DeclaredField(compType, "ticksToReset") ?? throw new MissingFieldException(compType.FullName, "ticksToReset");
            MP.RegisterSyncMethod(typeof(PLAMilira_NaglfarWorldBombardment), nameof(FireSupport));
            new Harmony("Meow.PLAMilira.NaglfarWorldBombardment").Patch(targetCallback,
                prefix: new HarmonyMethod(typeof(PLAMilira_NaglfarWorldBombardment), nameof(BeforeTarget)));
            Log.Message("[PLAMilira_NaglfarWorldBombardment] REQUIRED_TARGETS_OK world support target, cooldown and fire mission");
        }
        catch (Exception e) { Log.Error("[PLAMilira_NaglfarWorldBombardment] REQUIRED_TARGET_FAILED " + e); }
    }

    static bool BeforeTarget(object __instance, LocalTargetInfo __0)
    {
        if (!InUi) return true;
        var comp = (ThingComp)capturedComp.GetValue(__instance);
        var source = comp?.parent;
        var map = (Map)capturedMap.GetValue(__instance);
        if (source == null || source.Map == null || map == null) return false;
        FireSupport(source.Map.uniqueID, source.thingIDNumber, source.AllComps.IndexOf(comp), map.uniqueID, __0.Cell);
        return false;
    }

    public static void FireSupport(int sourceMapId, int sourceId, int compIndex, int targetMapId, IntVec3 cell)
    {
        var sourceMap = Find.Maps.FirstOrDefault(m => m.uniqueID == sourceMapId);
        var targetMap = Find.Maps.FirstOrDefault(m => m.uniqueID == targetMapId);
        var source = sourceMap?.listerThings.AllThings.FirstOrDefault(t => t.thingIDNumber == sourceId) as ThingWithComps;
        if (source == null || source.Faction == null || !source.Faction.IsPlayer || targetMap == null ||
            !cell.InBounds(targetMap) || compIndex < 0 || compIndex >= source.AllComps.Count) return;
        var comp = source.AllComps[compIndex];
        if (!compType.IsInstanceOfType(comp) || (int)cooldown.GetValue(comp) > 0) return;
        var closure = Activator.CreateInstance(closureType);
        capturedMap.SetValue(closure, targetMap);
        capturedComp.SetValue(closure, comp);
        var previousMap = Find.CurrentMap;
        try { targetCallback.Invoke(closure, new object[] { new LocalTargetInfo(cell) }); }
        finally { Current.Game.CurrentMap = previousMap; }
    }
}
