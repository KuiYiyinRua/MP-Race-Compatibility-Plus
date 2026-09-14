using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    public static class Tale
    {
        static readonly Dictionary<MethodBase, FieldInfo[]> CaravanPaths = new Dictionary<MethodBase, FieldInfo[]>();
        static FieldInfo mapIndex;
        static Func<Faction, bool, Faction> pushFaction;
        static Func<Faction> popFaction;
        static MethodInfo join;
        static ISyncMethod syncJoin;

        public sealed class Scope
        {
            internal Game Game;
            internal Map PreviousMap;
            internal sbyte PreviousIndex;
            internal bool Pushed;
        }

        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("TheTaleofMilira.TaleOfMilira_TheBattlefield") ?? throw new TypeLoadException("Tale battlefield");
            var context = AccessTools.TypeByName("Multiplayer.Client.FactionContext") ?? throw new TypeLoadException("MP faction context");
            pushFaction = (Func<Faction, bool, Faction>)Delegate.CreateDelegate(typeof(Func<Faction, bool, Faction>), AccessTools.Method(context, "Push", new[] { typeof(Faction), typeof(bool) }));
            popFaction = (Func<Faction>)Delegate.CreateDelegate(typeof(Func<Faction>), AccessTools.Method(context, "Pop", Type.EmptyTypes));
            mapIndex = AccessTools.Field(typeof(Game), "currentMapIndex") ?? throw new MissingFieldException("Game.currentMapIndex");
            var registrations = new List<MethodInfo>();
            foreach (var targetType in type.Assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                // Only event/outcome closures in this exact assembly; no pawn, map or world tick hooks.
                var path = CapturedCaravan(targetType, 0);
                if (path == null) continue;
                foreach (var method in targetType.GetMethods(AccessTools.allDeclared).OrderBy(m => m.MetadataToken))
                {
                    if (method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0 || !method.Name.StartsWith("<Outcome_", StringComparison.Ordinal)) continue;
                    var calls = PatchProcessor.GetOriginalInstructions(method).Select(i => i.operand).OfType<MethodInfo>().ToArray();
                    if (!calls.Any(m => m.DeclaringType == typeof(Faction) && m.Name == "get_OfPlayer"
                        || m.DeclaringType == typeof(PawnGroupMakerUtility) && m.Name == "GeneratePawns"
                        || m.DeclaringType == typeof(PawnGenerator) && m.Name == "GeneratePawn")) continue;
                    CaravanPaths.Add(method, path);
                    registrations.Add(method);
                }
            }
            if (registrations.Count < 40) throw new InvalidOperationException("Incomplete Tale callback inventory: " + registrations.Count);
            foreach (var method in registrations)
            {
                // Replace the old four-callback scope, which restored the chosen map rather than the prior map.
                var info = Harmony.GetPatchInfo(method);
                if (info != null)
                    foreach (var patch in info.Prefixes.Concat(info.Finalizers).Where(p => p.PatchMethod.DeclaringType?.FullName == "MP_MeowOnlineShop.Patch_MiliraCaravanRaidFactionContext").ToArray())
                        harmony.Unpatch(method, patch.PatchMethod);
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(Tale), nameof(BeforeOutcome)), finalizer: new HarmonyMethod(typeof(Tale), nameof(AfterOutcome)));
            }
            join = AccessTools.DeclaredMethod(AccessTools.TypeByName("TheTaleofMilira.MiliraMahouShoujoDialogue"), "JoinIfPossible", new[] { typeof(Pawn) }) ?? throw new MissingMethodException("Tale dialogue join");
            syncJoin = MP.RegisterSyncMethod(typeof(Tale), nameof(Join), new[] { new SyncType(typeof(Map)) { contextMap = true }, new SyncType(typeof(Pawn)) });
            harmony.Patch(join, prefix: new HarmonyMethod(typeof(Tale), nameof(BeforeJoin)));
            var supply = AccessTools.DeclaredMethod(type.Assembly.GetType("TheTaleofMilira.TaleOfMilira_TheMiliraSupply", true), "Notify_CaravanArrived", new[] { typeof(Caravan) });
            if (supply == null) throw new MissingMethodException("Tale supply arrival");
            // Register the whole supply conversation as for the other ten arrival types.
            // The legacy wrapper only synchronized leave letters, and would send from both peers during a synced option.
            foreach (var ctor in typeof(Dialog_NodeTree).GetConstructors(AccessTools.all))
            {
                var patches = Harmony.GetPatchInfo(ctor);
                if (patches == null) continue;
                foreach (var patch in patches.Postfixes.Where(p => p.PatchMethod.DeclaringType?.FullName == "MP_MeowOnlineShop.Patch_MiliraSupplyMp").ToArray())
                    harmony.Unpatch(ctor, patch.PatchMethod);
            }
            MP.RegisterSyncDialogNodeTree(supply);
            Log.Message("[TaleNivarianCompat] Tale caravan faction/map scopes=" + registrations.Count + "; dialogue join synchronized.");
        }

        static FieldInfo[] CapturedCaravan(Type type, int depth)
        {
            if (depth > 3 || !type.Name.Contains("DisplayClass")) return null;
            var fields = type.GetFields(AccessTools.allDeclared).Where(f => !f.IsStatic).OrderBy(f => f.MetadataToken).ToArray();
            foreach (var field in fields) if (field.FieldType == typeof(Caravan)) return new[] { field };
            foreach (var field in fields)
            {
                var nested = CapturedCaravan(field.FieldType, depth + 1);
                if (nested != null) return new[] { field }.Concat(nested).ToArray();
            }
            return null;
        }

        static void BeforeOutcome(object __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer) return;
            object value = __instance;
            foreach (var field in CaravanPaths[__originalMethod]) value = value == null ? null : field.GetValue(value);
            if (!(value is Caravan caravan) || caravan.Faction?.def?.isPlayer != true) return;
            __state = new Scope { Game = Current.Game, PreviousMap = Find.CurrentMap, PreviousIndex = (sbyte)mapIndex.GetValue(Current.Game) };
            pushFaction(caravan.Faction, false);
            __state.Pushed = true;
            Map selected = null;
            foreach (var map in Find.Maps)
                if (map.ParentFaction == caravan.Faction && (selected == null || map.uniqueID < selected.uniqueID)) selected = map;
            if (selected == null)
                foreach (var map in Find.Maps)
                    if (selected == null || map.uniqueID < selected.uniqueID) selected = map;
            mapIndex.SetValue(Current.Game, selected == null ? (sbyte)-1 : (sbyte)Find.Maps.IndexOf(selected));
        }

        static void AfterOutcome(Scope __state)
        {
            if (__state == null) return;
            try
            {
                if (ReferenceEquals(Current.Game, __state.Game))
                {
                    int index = __state.PreviousMap == null ? __state.PreviousIndex : Find.Maps.IndexOf(__state.PreviousMap);
                    mapIndex.SetValue(__state.Game, (sbyte)index);
                }
            }
            finally { if (__state.Pushed) popFaction(); }
        }

        static bool BeforeJoin(Pawn target)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            if (target?.Map != null) syncJoin.DoSync(null, target.Map, target);
            return false;
        }

        public static void Join(Map map, Pawn target)
        {
            if (map == null || target == null || target.Dead || target.Map != map || map.ParentFaction != Faction.OfPlayer) return;
            join.Invoke(null, new object[] { target });
        }
    }
}
