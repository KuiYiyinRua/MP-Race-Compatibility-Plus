using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350PermitsMp
    {
        private static bool applied;
        private static ISyncMethod execute;
        private static readonly Dictionary<Type, FieldInfo> factions = new Dictionary<Type, FieldInfo>();
        private static FieldInfo callerField, mapField, freeField;

        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled || !ModsConfig.IsActive("rooandgloomy.yuranracemod")) return;
            applied = true;
            try
            {
                callerField = RequiredField(typeof(RoyalTitlePermitWorker_Targeted), "caller", typeof(Pawn));
                mapField = RequiredField(typeof(RoyalTitlePermitWorker_Targeted), "map", typeof(Map));
                freeField = RequiredField(typeof(RoyalTitlePermitWorker_Targeted), "free", typeof(bool));
                var methods = new List<MethodInfo>();
                foreach (string name in new[] { "RoyalTitlePermitWorker_CallYRMikos", "RoyalTitlePermitWorker_CallYRNanny", "RoyalTitlePermitWorker_CallYRLaborer" })
                {
                    Type type = AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
                    if (type.BaseType != typeof(RoyalTitlePermitWorker_Targeted) || type.GetConstructor(Type.EmptyTypes) == null)
                        throw new InvalidOperationException(name + ": permit worker shape changed");
                    factions.Add(type, RequiredField(type, "calledFaction", typeof(Faction)));
                    MethodInfo method = AccessTools.DeclaredMethod(type, "OrderForceTarget", new[] { typeof(LocalTargetInfo) });
                    if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                        throw new MissingMethodException(name, "OrderForceTarget");
                    methods.Add(method);
                }
                execute = MP.RegisterSyncMethod(typeof(Patch_Light350PermitsMp), nameof(Execute));
                foreach (MethodInfo method in methods)
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patch_Light350PermitsMp), nameof(OrderPrefix)));
                Log.Message("[MP-MeowOnlineShop][Light350-B6] Yuran: 3 permit executors with explicit faction context installed.");
            }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B6] REQUIRED TARGET FAILED Yuran: " + e); }
        }

        private static FieldInfo RequiredField(Type type, string name, Type expected)
        {
            FieldInfo field = AccessTools.DeclaredField(type, name);
            if (field == null || field.IsStatic || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static bool OrderPrefix(RoyalTitlePermitWorker_Targeted __instance, LocalTargetInfo __0)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand) return true;
            Pawn caller = (Pawn)callerField.GetValue(__instance);
            Map map = (Map)mapField.GetValue(__instance);
            Faction faction = (Faction)factions[__instance.GetType()].GetValue(__instance);
            if (caller == null || map == null || faction == null || !__0.IsValid) return false;
            execute.DoSync(null, __instance.def, caller, map, faction, (bool)freeField.GetValue(__instance), __0.Cell);
            return false;
        }

        private static void Execute(RoyalTitlePermitDef def, Pawn caller, Map map, Faction faction, bool free, IntVec3 cell)
        {
            if (def == null || caller == null || map == null || faction == null || caller.MapHeld != map || !cell.InBounds(map)) return;
            Type type = def.workerClass;
            if (type == null || !factions.TryGetValue(type, out FieldInfo factionField)) return;
            // The Def's shared worker holds the local targeter. Do not overwrite another player's pending selection.
            var worker = (RoyalTitlePermitWorker_Targeted)Activator.CreateInstance(type);
            worker.def = def;
            callerField.SetValue(worker, caller);
            mapField.SetValue(worker, map);
            freeField.SetValue(worker, free);
            factionField.SetValue(worker, faction);
            worker.OrderForceTarget(new LocalTargetInfo(cell));
        }
    }
}
