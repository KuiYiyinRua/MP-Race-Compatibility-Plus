using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class FunnelBit_SaveCompat
{
    sealed class ReferenceState { public Thing Parent; public int Index = -1; }
    sealed class PendingState
    {
        public Thing Parent;
        public int Index = -1;
        public Pawn Launcher;
        public LocalTargetInfo Target = LocalTargetInfo.Invalid;
        public float Rotation;
    }
    sealed class RestoreState { public object State, Worker; public int Delay; }
    static readonly ConditionalWeakTable<object, ReferenceState> owners = new ConditionalWeakTable<object, ReferenceState>();
    static readonly ConditionalWeakTable<object, PendingState> pending = new ConditionalWeakTable<object, PendingState>();
    static MethodInfo lookSlots;
    static Type turretType;
    static object Get(object o, string n) => AccessTools.Field(o.GetType(), n).GetValue(o);
    static void Set(object o, string n, object v) => AccessTools.Field(o.GetType(), n).SetValue(o, v);
    static HarmonyMethod Hook(string n) => new HarmonyMethod(typeof(FunnelBit_SaveCompat), n);
    static FunnelBit_SaveCompat()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("rabiosus.funnelmilian")) return;
        LongEventHandler.ExecuteWhenFinished(Install);
    }
    static void Install()
    {
        try
        {
            var slotType = AccessTools.TypeByName("FunnelBit.FunnelBitSlot") ?? throw new TypeLoadException("FunnelBitSlot");
            turretType = AccessTools.TypeByName("FunnelBit.CompTurretGun_FunnelBit") ?? throw new TypeLoadException("CompTurretGun_FunnelBit");
            var data = AccessTools.TypeByName("FunnelBit.FunnelBitManager+TurretSlotsData") ?? throw new TypeLoadException("TurretSlotsData");
            lookSlots = typeof(Scribe_Collections).GetMethods().Single(m => m.Name == "Look" && m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 4 &&
                m.GetParameters()[0].ParameterType.GetElementType()?.IsGenericType == true &&
                m.GetParameters()[0].ParameterType.GetElementType().GetGenericTypeDefinition() == typeof(List<>)).MakeGenericMethod(slotType);
            foreach (var n in new[] { "pendingTurret", "pendingLauncher", "pendingWorker", "pendingTarget", "pendingRotation", "AbilityWorker", "State", "launchDelayTicks" })
                if (AccessTools.Field(slotType, n) == null) throw new MissingFieldException(n);
            var h = new Harmony("Meow.FunnelBit.SaveCompat");
            h.Patch(AccessTools.DeclaredMethod(data, "ExposeData"), prefix: Hook(nameof(OwnerExpose)));
            h.Patch(AccessTools.DeclaredMethod(slotType, "ExposeData"), prefix: Hook(nameof(BeforeSlot)), postfix: Hook(nameof(AfterSlot)));
            Log.Message("[FunnelBit_SaveCompat] REQUIRED_TARGETS_OK slot owners and pending launch persistence");
        }
        catch (Exception e) { Log.Error("[FunnelBit_SaveCompat] REQUIRED_TARGET_FAILED " + e); }
    }
    static ThingComp Resolve(Thing thing, int index)
    {
        var parent = thing as ThingWithComps;
        if (parent == null || parent.Destroyed || index < 0 || index >= parent.AllComps.Count) return null;
        var comp = parent.AllComps[index];
        return turretType.IsInstanceOfType(comp) ? comp : null;
    }
    // Native temporaries are method locals and disappear between Scribe phases.
    // Keep the original XML names so existing saves retain their slot associations.
    static bool OwnerExpose(object __instance)
    {
        var state = owners.GetOrCreateValue(__instance);
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            var comp = Get(__instance, "turretComp") as ThingComp;
            state.Parent = comp?.parent;
            state.Index = comp?.parent.AllComps.IndexOf(comp) ?? -1;
        }
        Scribe_References.Look(ref state.Parent, "parentThing");
        Scribe_Values.Look(ref state.Index, "compIndex", -1);
        object[] args = { Get(__instance, "slots"), "slots", LookMode.Deep, Array.Empty<object>() };
        lookSlots.Invoke(null, args);
        Set(__instance, "slots", args[0]);
        if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs || Scribe.mode == LoadSaveMode.PostLoadInit)
            Set(__instance, "turretComp", Resolve(state.Parent, state.Index));
        return false;
    }
    static void BeforeSlot(object __instance, out RestoreState __state)
    {
        __state = null;
        var state = pending.GetOrCreateValue(__instance);
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            var comp = Get(__instance, "pendingTurret") as ThingComp;
            state.Parent = comp?.parent;
            state.Index = comp?.parent.AllComps.IndexOf(comp) ?? -1;
            state.Launcher = (Pawn)Get(__instance, "pendingLauncher");
            state.Target = (LocalTargetInfo)Get(__instance, "pendingTarget");
            state.Rotation = (float)Get(__instance, "pendingRotation");
        }
        Scribe_References.Look(ref state.Parent, "meowPendingTurretParent");
        Scribe_Values.Look(ref state.Index, "meowPendingTurretIndex", -1);
        Scribe_References.Look(ref state.Launcher, "meowPendingLauncher");
        Scribe_TargetInfo.Look(ref state.Target, "meowPendingTarget");
        Scribe_Values.Look(ref state.Rotation, "meowPendingRotation");
        if (Scribe.mode != LoadSaveMode.PostLoadInit || Get(__instance, "State").ToString() != "PendingLaunch" ||
            Resolve(state.Parent, state.Index) == null || state.Launcher == null || state.Launcher.Destroyed || !state.Target.IsValid) return;
        __state = new RestoreState { State = Get(__instance, "State"), Worker = Get(__instance, "AbilityWorker"), Delay = (int)Get(__instance, "launchDelayTicks") };
    }
    static void AfterSlot(object __instance, RestoreState __state)
    {
        if (__state == null) return;
        var state = pending.GetOrCreateValue(__instance);
        Set(__instance, "State", __state.State);
        Set(__instance, "launchDelayTicks", __state.Delay);
        Set(__instance, "AbilityWorker", __state.Worker);
        Set(__instance, "pendingWorker", __state.Worker);
        Set(__instance, "pendingTurret", Resolve(state.Parent, state.Index));
        Set(__instance, "pendingLauncher", state.Launcher);
        Set(__instance, "pendingTarget", state.Target);
        Set(__instance, "pendingRotation", state.Rotation);
    }
}
