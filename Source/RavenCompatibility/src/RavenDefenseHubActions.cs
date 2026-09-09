using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenDefenseHubActions
    {
        private static Type hubType, managerType, designatorType;
        private static FieldInfo managerField, ownerField, repairField, plansField;
        private static MethodInfo unregister;
        private static ISyncMethod removeCommand, repairCommand;
        internal static void Apply(Harmony harmony)
        {
            const string ns = "RavenRace.Features.DefenseHub.";
            hubType = AccessTools.TypeByName(ns + "Building_RavenDefenseHub");
            managerType = AccessTools.TypeByName(ns + "DefenseHubPlanManager");
            designatorType = AccessTools.TypeByName(ns + "Designator_DefenseHubTurret");
            managerField = AccessTools.DeclaredField(hubType, "planManager");
            ownerField = AccessTools.DeclaredField(managerType, "hub");
            repairField = AccessTools.DeclaredField(managerType, "autoRepairEnabled");
            plansField = AccessTools.DeclaredField(managerType, "turretPlans");
            unregister = AccessTools.DeclaredMethod(managerType, "UnregisterPlan");
            foreach (string name in new[] { "SetAutonomousTier", "EjectAll", "ScanAndLinkClaymores", "TryRegisterTurret" })
            {
                var method = AccessTools.DeclaredMethod(hubType, name) ?? throw new MissingMethodException(hubType.FullName, name);
                MP.RegisterSyncMethod(method);
            }
            if (managerField == null || ownerField == null || repairField == null || plansField == null || unregister == null || designatorType == null)
                throw new MissingMemberException("Raven defense hub action target");
            removeCommand = MP.RegisterSyncMethod(typeof(RavenDefenseHubActions), nameof(RemovePlan));
            repairCommand = MP.RegisterSyncMethod(typeof(RavenDefenseHubActions), nameof(SetRepair));
            harmony.Patch(unregister, prefix: new HarmonyMethod(typeof(RavenDefenseHubActions), nameof(BeforeRemove)));
            var dialog = AccessTools.TypeByName("RavenRace.Features.DefenseHub.UI.Dialog_DefenseHubConsole");
            harmony.Patch(AccessTools.DeclaredMethod(dialog, "DoWindowContents"),
                prefix: new HarmonyMethod(typeof(RavenDefenseHubActions), nameof(BeforeDraw)),
                finalizer: new HarmonyMethod(typeof(RavenDefenseHubActions), nameof(AfterDraw)));
            MP.RegisterSyncWorker<Designator_Build>(SyncDesignator, designatorType, false, false);
        }
        private struct RepairSnapshot { public Thing hub; public bool value; }
        private static void BeforeDraw(Thing ___hub, out RepairSnapshot __state)
        {
            __state = default;
            if (MP.InInterface && ___hub?.Spawned == true)
                __state = new RepairSnapshot { hub = ___hub, value = (bool)repairField.GetValue(managerField.GetValue(___hub)) };
        }
        private static void AfterDraw(RepairSnapshot __state)
        {
            if (__state.hub == null) return;
            var manager = managerField.GetValue(__state.hub);
            bool current = (bool)repairField.GetValue(manager);
            if (current == __state.value) return;
            repairField.SetValue(manager, __state.value);
            repairCommand.DoSync(null, __state.hub, current);
        }
        private static void SetRepair(Thing hub, bool value)
        {
            if (hub?.Spawned == true && hubType.IsInstanceOfType(hub)) repairField.SetValue(managerField.GetValue(hub), value);
        }
        private static object Value(object plan, string name) => AccessTools.Field(plan.GetType(), name).GetValue(plan);
        private static bool BeforeRemove(object __instance, object __0, bool __1, ref bool __result)
        {
            if (!MP.InInterface) return true;
            __result = false;
            if (__0 == null) return false;
            var plans = (IList)plansField.GetValue(__instance);
            int index = plans.IndexOf(__0);
            if (index < 0) return false;
            removeCommand.DoSync(null, (Thing)ownerField.GetValue(__instance), index, (ThingDef)Value(__0, "turretDef"),
                (IntVec3)Value(__0, "cell"), (Thing)Value(__0, "boundTurret"), (Thing)Value(__0, "boundFrame"), __1);
            return false;
        }
        private static void RemovePlan(Thing hub, int index, ThingDef def, IntVec3 cell, Thing turret, Thing frame, bool deconstruct)
        {
            if (hub?.Spawned != true || !hubType.IsInstanceOfType(hub)) return;
            var manager = managerField.GetValue(hub);
            var plans = (IList)plansField.GetValue(manager);
            if (index < 0 || index >= plans.Count) return;
            var plan = plans[index];
            if (plan == null || (ThingDef)Value(plan, "turretDef") != def || (IntVec3)Value(plan, "cell") != cell ||
                (Thing)Value(plan, "boundTurret") != turret || (Thing)Value(plan, "boundFrame") != frame) return;
            unregister.Invoke(manager, new object[] { plan, deconstruct });
        }
        private static void SyncDesignator(SyncWorker sync, ref Designator_Build value)
        {
            Thing hub = sync.isWriting ? (Thing)AccessTools.Field(designatorType, "hub").GetValue(value) : null;
            ThingDef def = sync.isWriting ? value.PlacingDef as ThingDef : null;
            ThingDef stuff = sync.isWriting ? value.StuffDef : null;
            Rot4 rot = sync.isWriting ? (Rot4)AccessTools.Field(typeof(Designator_Place), "placingRot").GetValue(value) : Rot4.North;
            sync.Bind(ref hub); sync.Bind(ref def); sync.Bind(ref stuff); sync.Bind(ref rot);
            if (sync.isWriting) return;
            if (hub == null || !hubType.IsInstanceOfType(hub) || def == null) throw new InvalidOperationException("Raven defense designator lost hub/def");
            var ext = AccessTools.Property(hubType, "Ext").GetValue(hub);
            var utility = AccessTools.TypeByName("RavenRace.Features.DefenseHub.DefenseHubUIUtility");
            var option = AccessTools.Method(utility, "FindTurretOption").Invoke(null, new[] { (object)def, ext });
            if (option == null) throw new InvalidOperationException("Raven defense turret option missing");
            value = (Designator_Build)Activator.CreateInstance(designatorType, hub, ext, option);
            AccessTools.Field(typeof(Designator_Build), "stuffDef").SetValue(value, stuff);
            AccessTools.Field(typeof(Designator_Place), "placingRot").SetValue(value, rot);
        }
    }
}
