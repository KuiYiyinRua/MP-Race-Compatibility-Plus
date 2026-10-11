using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class PLAMilira_Technology
{
    static Type techDef, handler, faction, church, game;
    static MethodInfo unlocked, unlock, reinforcements, points, begin;
    static Type Resolve(string name) => AccessTools.TypeByName("PLAMilira." + name) ?? throw new TypeLoadException(name);
    static object Instance(Type type) => AccessTools.Property(type, "Instance").GetValue(null);
    static object Field(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
    static int Number(object obj, string name) => (int)AccessTools.Property(obj.GetType(), name).GetValue(obj);
    static PLAMilira_Technology()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("sleepycot.wingsofdemocracy")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }
    static void Install()
    {
        try
        {
            techDef = Resolve("PLAMiliraFactionTecDef"); handler = Resolve("PLAMilira_Tech_Handler");
            faction = Resolve("PLAMilira_Faction_Handler"); church = Resolve("PLAMilira_Church_Faction_Handler"); game = Resolve("PLAMilira_GameComponent");
            var normal = Resolve("Dialog_PLAMilira_TECUP_Window"); var churchWindow = Resolve("Dialog_PLAMilira_Church_TECUP_Window");
            unlocked = AccessTools.Method(handler, "IsTechUnlocked", new[] { typeof(string) });
            unlock = AccessTools.Method(normal, "UnlockTech", new[] { typeof(string), typeof(bool) });
            reinforcements = AccessTools.Method(faction, "AdjustReinforcements", new[] { typeof(int), typeof(bool) });
            points = AccessTools.Method(church, "AdjustTECPoints", new[] { typeof(int), typeof(bool) });
            begin = AccessTools.Method(game, "UnlockingTec", new[] { typeof(int), techDef });
            if (new[] { unlocked, unlock, reinforcements, points, begin }.Any(m => m == null)) throw new MissingMethodException("Technology native executor");
            foreach (var name in new[] { "techName", "uPNow", "reinforcements", "threatPoints", "tecPoint", "unlockTick", "labelKey" })
                if (AccessTools.Field(techDef, name) == null) throw new MissingFieldException(name);
            MP.RegisterSyncMethod(typeof(PLAMilira_Technology), nameof(Purchase));
            var h = new Harmony("Meow.PLAMilira.Technology");
            foreach (var type in new[] { normal, churchWindow })
            {
                var target = AccessTools.DeclaredMethod(type, "DrawBottomInfo", new[] { typeof(Rect), techDef }) ?? throw new MissingMethodException(type.FullName);
                h.Patch(target, transpiler: new HarmonyMethod(typeof(PLAMilira_Technology), nameof(Transpile)));
            }
            Log.Message("[PLAMilira_Technology] REQUIRED_TARGETS_OK two atomic purchase boundaries");
        }
        catch (Exception e) { Log.Error("[PLAMilira_Technology] REQUIRED_TARGET_FAILED " + e); }
    }
    static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
    {
        var result = new List<CodeInstruction>(); int matches = 0;
        foreach (var instruction in input)
        {
            result.Add(instruction);
            if (!(instruction.operand is MethodInfo method) || method.DeclaringType != typeof(Widgets) || method.Name != "ButtonText") continue;
            matches++;
            result.Add(new CodeInstruction(OpCodes.Ldarg_2));
            result.Add(new CodeInstruction(__originalMethod.DeclaringType.Name.Contains("Church") ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
            result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PLAMilira_Technology), nameof(PurchaseButton))));
        }
        if (matches != 1) throw new InvalidOperationException("Technology purchase button count=" + matches);
        return result;
    }
    // Only consume a successful purchase click. The native UI remains local;
    // its complete simulation outcome is replayed once after delivery.
    static bool PurchaseButton(bool clicked, Def def, bool isChurch)
    {
        if (!clicked || !MP.IsInMultiplayer || !MP.InInterface) return clicked;
        Purchase(def.defName, isChurch);
        return false;
    }
    public static void Purchase(string defName, bool isChurch)
    {
        var def = GenDefDatabase.GetDef(techDef, defName, false);
        var tech = Instance(handler);
        if (def == null || tech == null) return;
        string name = (string)Field(def, "techName");
        if ((bool)unlocked.Invoke(tech, new object[] { name })) return;
        if (isChurch)
        {
            var source = Instance(church); var component = Instance(game);
            int cost = (int)Field(def, "tecPoint");
            if (source == null || component == null || Number(source, "TECPoint") < cost ||
                (bool)AccessTools.Property(game, "IsUnlocking").GetValue(component)) return;
            points.Invoke(source, new object[] { -cost, true });
            begin.Invoke(component, new object[] { (int)Field(def, "unlockTick"), def });
        }
        else
        {
            var source = Instance(faction); int cost = (int)Field(def, "reinforcements");
            if (source == null || Number(source, "Reinforcements") < cost || Number(source, "ThreatPoints") < (int)Field(def, "threatPoints")) return;
            unlock.Invoke(null, new object[] { name, (bool)Field(def, "uPNow") });
            reinforcements.Invoke(source, new object[] { -cost, true });
            Messages.Message("PLAMilira_TechWindowUnlocked".Translate(((string)Field(def, "labelKey")).Translate().ToString()), MessageTypeDefOf.PositiveEvent);
        }
    }
}
