using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianModuleRefuel
    {
        static Type componentType;
        static FieldInfo workers, workerDef, changed;
        static readonly Dictionary<Type, PropertyInfo> thresholds = new Dictionary<Type, PropertyInfo>();
        static ISyncMethod setThreshold;
        internal static void Apply(Harmony harmony)
        {
            const string ns = "Nivarian_Race.Code.MechModuleSystem.";
            componentType = AccessTools.TypeByName(ns + "Comp_MechModule") ?? throw new TypeLoadException(ns + "Comp_MechModule");
            workers = AccessTools.Field(componentType, "installedWorkers") ?? throw new MissingFieldException(componentType.FullName, "installedWorkers");
            workerDef = AccessTools.Field(AccessTools.TypeByName(ns + "ModuleWorker"), "def") ?? throw new MissingFieldException("ModuleWorker.def");
            changed = AccessTools.Field(AccessTools.TypeByName("Nivarian_Race.Code.UI.Gizmo_Progress"), "onTargetChanged") ?? throw new MissingFieldException("Gizmo_Progress.onTargetChanged");
            setThreshold = MP.RegisterSyncMethod(typeof(NivarianModuleRefuel), nameof(SetThreshold));
            foreach (var name in new[] { "ModuleWorker_Teleport", "ModuleWorker_ReactiveArmor" })
            {
                var type = AccessTools.TypeByName(ns + name) ?? throw new TypeLoadException(ns + name);
                thresholds.Add(type, AccessTools.Property(type, "RefillThreshold") ?? throw new MissingMemberException(type.FullName, "RefillThreshold"));
                harmony.Patch(AccessTools.DeclaredMethod(type, "GetGizmos", new[] { typeof(Pawn) }) ?? throw new MissingMethodException(type.FullName, "GetGizmos"),
                    postfix: new HarmonyMethod(typeof(NivarianModuleRefuel), nameof(Gizmos)));
            }
            Log.Message("[TaleNivarianCompat] Teleport/reactive-armor refuel thresholds synchronized by pawn and installed module Def.");
        }
        static void Gizmos(object __instance, Pawn pawn, ref IEnumerable<Gizmo> __result)
        {
            if (MP.IsInMultiplayer && MP.InInterface && pawn != null)
                __result = Wrap(__result, pawn, (Def)workerDef.GetValue(__instance));
        }
        static IEnumerable<Gizmo> Wrap(IEnumerable<Gizmo> gizmos, Pawn pawn, Def def)
        {
            foreach (var gizmo in gizmos)
            {
                if (changed.DeclaringType.IsInstanceOfType(gizmo))
                    changed.SetValue(gizmo, new Action<float>(value => setThreshold.DoSync(null, pawn, def, value)));
                yield return gizmo;
            }
        }
        static void SetThreshold(Pawn pawn, Def def, float value)
        {
            if (pawn == null || pawn.Destroyed || def == null || float.IsNaN(value) || float.IsInfinity(value)) return;
            foreach (var comp in pawn.AllComps)
                if (componentType.IsInstanceOfType(comp))
                    foreach (var worker in (IEnumerable)workers.GetValue(comp))
                        if (worker != null && ReferenceEquals(workerDef.GetValue(worker), def) && thresholds.TryGetValue(worker.GetType(), out var threshold))
                        {
                            // Native setters clamp to [0,1]; native ExposeData already persists both fields.
                            threshold.SetValue(worker, value);
                            return;
                        }
        }
    }
}