using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.Client;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Mp = Multiplayer.Client.Multiplayer;

namespace Meow.FactionPurge
{
    public sealed class PurgePlan
    {
        public Faction Target;
        public readonly List<MapParent> Bases = new List<MapParent>();
        public readonly List<Pawn> Pawns = new List<Pawn>();
        public readonly List<Quest> Quests = new List<Quest>();
        public readonly List<string> Blockers = new List<string>();
        public bool Allowed => Target != null && Blockers.Count == 0;
        public string Fingerprint => string.Join(";", new[] {
            Target?.loadID.ToString() ?? "missing",
            string.Join(",", Bases.Select(b => b.ID + ":" + (b.Map?.uniqueID ?? -1))),
            string.Join(",", Pawns.Select(p => p.thingIDNumber)),
            string.Join(",", Quests.Select(q => q.id)) });

        public static PurgePlan Build(int id, int protectedFactionId)
        {
            var plan = new PurgePlan { Target = Find.FactionManager.GetById(id) };
            var f = plan.Target;
            if (f == null) { plan.Blockers.Add("派系已不存在。"); return plan; }
            if (!f.IsPlayer || f == Mp.WorldComp.spectatorFaction || id == protectedFactionId
                || f == Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => x.IsPlayer))
                plan.Blockers.Add("主派系、观战派系和 NPC 派系受保护。");
            if (Mp.GameComp?.multifaction != true) plan.Blockers.Add("仅支持多派系联机存档。");
            foreach (var obj in Find.WorldObjects.AllWorldObjects.Where(x => x.Faction == f).OrderBy(x => x.ID))
            {
                if (obj.GetType() == typeof(Settlement)) plan.Bases.Add((MapParent)obj);
                else plan.Blockers.Add("请先处理远行队、运输器或特殊据点：" + obj.Label + " / " + obj.GetType().FullName);
            }
            var maps = new HashSet<Map>(plan.Bases.Where(b => b.Map != null).Select(b => b.Map));
            if (!Find.Maps.Any(m => !maps.Contains(m) && m.ParentFaction?.IsPlayer == true))
                plan.Blockers.Add("必须保留至少一张其他玩家派系的基地地图。");
            foreach (var pocket in Find.World.pocketMaps)
                if (maps.Contains(pocket.sourceMap)) plan.Blockers.Add("目标基地连接口袋地图，请先结束该特殊地图。");
            plan.Pawns.AddRange(PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead
                .Where(p => p.Faction == f).Distinct().OrderBy(p => p.thingIDNumber));
            foreach (var pawn in plan.Pawns)
            {
                if (pawn.MapHeld != null && !maps.Contains(pawn.MapHeld))
                    plan.Blockers.Add("目标人物仍在其他地图，请先撤回：" + pawn.LabelShort);
                if (pawn.ParentHolder != null && pawn.MapHeld == null && !Find.WorldPawns.Contains(pawn))
                    plan.Blockers.Add("目标人物在运输或特殊容器中：" + pawn.LabelShort);
            }
            foreach (var map in maps.OrderBy(m => m.uniqueID))
            {
                var contents = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(map, contents);
                foreach (var pawn in contents.OfType<Pawn>())
                    if ((pawn.Faction?.IsPlayer == true && pawn.Faction != f)
                        || (pawn.HostFaction?.IsPlayer == true && pawn.HostFaction != f))
                        plan.Blockers.Add("目标地图有其他玩家的人物或俘虏，请先撤离：" + pawn.LabelShort);
                foreach (var thing in contents)
                    if (thing.Faction?.IsPlayer == true && thing.Faction != f)
                        plan.Blockers.Add("目标地图有其他玩家的财产：" + thing.LabelShort);
            }
            foreach (var map in Find.Maps.Where(m => !maps.Contains(m)).OrderBy(m => m.uniqueID))
            {
                var contents = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(map, contents);
                foreach (var thing in contents)
                    if (thing.Faction == f || thing is Pawn pawn && pawn.HostFaction == f)
                        plan.Blockers.Add("目标派系的人物或财产仍在保留地图 " + map.uniqueID + "：" + thing.LabelShort);
            }
            // Pending trade/ritual/caravan sessions can contain callbacks into maps being removed.
            // Refuse maintenance until these are closed; do not silently commit/cancel other players' work.
            foreach (var manager in Managers())
                foreach (var session in (IEnumerable)AccessTools.Property(manager.GetType(), "AllSessions").GetValue(manager, null))
                    if (session.GetType() != typeof(Multiplayer.Client.Persistent.PauseLockSession))
                        plan.Blockers.Add("存在未结束的 MP 会话，请先关闭后重试：" + session.GetType().Name);
            foreach (var quest in Find.QuestManager.QuestsListForReading.OrderBy(q => q.id))
            {
                if (!Touches(quest, plan) && OwnerId(quest) != id) continue;
                var parts = (IEnumerable)AccessTools.Field(typeof(Quest), "parts").GetValue(quest);
                if (parts.Cast<object>().Any(p => p != null && p.GetType().Assembly != typeof(Quest).Assembly))
                    plan.Blockers.Add("关联任务包含第三方 QuestPart，需先结束并清理：" + quest.name);
                else plan.Quests.Add(quest);
            }
            // Unknown mod-owned references must be handled by that mod, not nulled by a generic mutator.
            foreach (var component in Current.Game.components.Cast<object>().Concat(Find.World.components.Cast<object>())
                .Concat(Find.Maps.Where(m => !maps.Contains(m)).SelectMany(m => m.components).Cast<object>()))
                if (component.GetType().Assembly != typeof(Game).Assembly
                    && component.GetType().Assembly != typeof(PurgePlan).Assembly
                    && component.GetType().FullName != "Meow.FactionDiplomacy.FactionDiplomacyState" && Touches(component, plan))
                    plan.Blockers.Add("第三方组件仍引用目标，需要专门适配：" + component.GetType().FullName);
            return plan;
        }

        internal static IEnumerable<object> Managers()
        {
            var result = new List<object> { Mp.WorldComp.sessionManager };
            foreach (var comp in Mp.game.mapComps) result.Add(comp.sessionManager);
            return result;
        }

        private static int OwnerId(Quest quest)
        {
            var type = AccessTools.TypeByName("Meow.FactionStoryIsolation.QuestAcceptanceGuard");
            if (type == null) return -1;
            // ConditionalWeakTable.TryGetValue avoids creating ownership records while previewing.
            var table = AccessTools.Field(type, "Records")?.GetValue(null);
            if (table == null) return -1;
            var args = new object[] { quest, null };
            if (!(bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args)) return -1;
            return (int)AccessTools.Field(args[1].GetType(), "OwnerId").GetValue(args[1]);
        }

        // Bounded field traversal, never properties or getters that may mutate simulation state.
        // Objects which are references to world entities are leaves, not gateways into the entire world.
        public static bool Touches(object root, PurgePlan plan)
        {
            var seen = new HashSet<object>(ReferenceEquality.Instance);
            try { return Visit(root, plan, seen, 0, true); }
            catch (Exception ex) { throw new InvalidOperationException("引用检查失败：" + root?.GetType().FullName + " / " + ex.Message, ex); }
        }
        private static bool Visit(object value, PurgePlan plan, HashSet<object> seen, int depth, bool root = false)
        {
            if (value == null) return false;
            if (!root)
            {
                if (value is Faction f) return f == plan.Target;
                if (value is Map m) return plan.Bases.Any(b => b.Map == m);
                if (value is WorldObject w) return w.Faction == plan.Target;
                if (value is Thing t) return t.Faction == plan.Target || (t.MapHeld != null && plan.Bases.Any(b => b.Map == t.MapHeld));
                if (value is Quest q) return plan.Quests.Contains(q);
                if (value is Game || value is World || value is GameComponent || value is WorldComponent || value is MapComponent) return false;
            }
            var type = value.GetType();
            if (value is string || value is Def || value is MemberInfo || value is Assembly || value is Module
                || value is System.Reflection.Pointer || value is IntPtr || value is UIntPtr
                || value is Delegate || value is UnityEngine.Object
                || type.IsPrimitive || type.IsEnum || value is decimal) return false;
            if (type.IsValueType && ReferenceFree(type) || type.IsArray && ReferenceFree(type.GetElementType())) return false;
            if (!seen.Add(value)) return false;
            if (depth > 24 || seen.Count > 50000) throw new InvalidOperationException("引用检查超出安全上限：" + type.FullName);
            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable) if (Visit(item, plan, seen, depth + 1)) return true;
                return false;
            }
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    // Components hold the entire Game/World/Map as their owner. Do not traverse these roots.
                    if (field.FieldType == typeof(Game) || field.FieldType == typeof(World)) continue;
                    if (Visit(field.GetValue(value), plan, seen, depth + 1)) return true;
                }
            return false;
        }

        private static readonly Dictionary<Type, bool> ReferenceFreeTypes = new Dictionary<Type, bool>();
        private static bool ReferenceFree(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr) || type == typeof(string)) return true;
            if (!type.IsValueType) return false;
            if (ReferenceFreeTypes.TryGetValue(type, out var cached)) return cached;
            // Large particle buffers contain numeric structs, not managed entity references.
            // Inspect their layout once instead of boxing tens of thousands of identical elements.
            bool result = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(f => ReferenceFree(f.FieldType));
            ReferenceFreeTypes[type] = result;
            return result;
        }

        private sealed class ReferenceEquality : IEqualityComparer<object>
        {
            internal static readonly ReferenceEquality Instance = new ReferenceEquality();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }
}
