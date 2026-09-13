using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class Patch_Light350SnapshotsMp
    {
        private static bool applied;
        private const string ExplosionPrefix = "meowMpExplosion_";
        private const string FactionKey = "meowMpUndergroundFaction";
        private const string TunnelFactionKey = "meowMpTunnelFaction";
        private static readonly string[] ExplosionFields = { "startTick", "cellsToAffect", "damagedThings", "ignoredThings", "addedCellsAffectedOnlyByDamage" };
        private static readonly string[] ExplosionSerializedKeys = ExplosionFields.Concat(new[] { "instigator", "intendedTarget" }).ToArray();
        private static readonly Type[] ExplosionFieldTypes = { typeof(int), typeof(List<IntVec3>), typeof(List<Thing>), typeof(List<Thing>), typeof(HashSet<IntVec3>) };
        private static Type whiteMoonComponent;
        private static readonly Dictionary<string, FieldInfo> moonFields = new Dictionary<string, FieldInfo>();

        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled) return;
            applied = true;
            Package("rooandgloomy.dragonianracemod", () => Explosion(harmony, "DragonianMelee.DragonianMeleeExplosion"));
            Package("nephlite.orbitaltradecolumn", () => Explosion(harmony, "RimWorldColumns.Explosion_Directed"));
            Package("rku.ratkinunderground", () =>
            {
                var type = RequiredType("RatkinUnderground.RKU_TunnelHiveSpawner_Und");
                Field(type, "faction", typeof(Faction));
                Field(type.BaseType, "faction", typeof(Faction));
                var method = RequiredExpose(type);
                var original = PatchProcessor.GetOriginalInstructions(method);
                if (original.Count(i => IsDeepFaction(i.operand as MethodInfo)) != 1)
                    throw new InvalidOperationException("Expected exactly one deep faction writer");
                var parentMethod = RequiredExpose(type.BaseType);
                CheckTunnelKey(PatchProcessor.GetOriginalInstructions(parentMethod));
                harmony.Patch(parentMethod, prefix: new HarmonyMethod(typeof(Patch_Light350SnapshotsMp), nameof(MigrateTunnelFaction)),
                    transpiler: new HarmonyMethod(typeof(Patch_Light350SnapshotsMp), nameof(TunnelFactionKeys)));
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patch_Light350SnapshotsMp), nameof(MigrateFaction)),
                    transpiler: new HarmonyMethod(typeof(Patch_Light350SnapshotsMp), nameof(FactionWriter)));
            });
            Package("rkk.ratknights.core", () =>
            {
                whiteMoonComponent = RequiredType("RatkinKnights.MapComponent_WhiteMoonSkill");
                if (!typeof(MapComponent).IsAssignableFrom(whiteMoonComponent) || AccessTools.DeclaredMethod(whiteMoonComponent, "ExposeData") != null)
                    throw new InvalidOperationException("WhiteMoon component serialization shape changed");
                foreach (var name in new[] { "currentTick", "totalSpears", "spearsPerTick", "spearsLaunched", "deathTick" })
                    moonFields[name] = Field(whiteMoonComponent, name, typeof(int));
                moonFields["isDead"] = Field(whiteMoonComponent, "isDead", typeof(bool));
                moonFields["circleCenters"] = Field(whiteMoonComponent, "circleCenters", typeof(List<IntVec3>));
                moonFields["caster"] = Field(whiteMoonComponent, "caster", typeof(Pawn));
                var moon = AccessTools.DeclaredField(whiteMoonComponent, "whiteMoon");
                if (moon == null || !typeof(Pawn).IsAssignableFrom(moon.FieldType)) throw new MissingFieldException("whiteMoon");
                moonFields["whiteMoon"] = moon;
                moonFields["cachedTracker"] = AccessTools.DeclaredField(whiteMoonComponent, "cachedTracker") ?? throw new MissingFieldException("cachedTracker");
                harmony.Patch(RequiredExpose(typeof(MapComponent)), postfix: new HarmonyMethod(typeof(Patch_Light350SnapshotsMp), nameof(WhiteMoonExpose)));
            });
        }

        private static void Package(string id, Action action)
        {
            if (!ModsConfig.IsActive(id)) return;
            try { action(); Log.Message("[MP-MeowOnlineShop][Light350-B3] " + id + ": snapshot targets installed."); }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B3] REQUIRED TARGET FAILED " + id + ": " + e); }
        }
        private static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        private static MethodInfo RequiredExpose(Type type) => AccessTools.DeclaredMethod(type, "ExposeData", Type.EmptyTypes) ?? throw new MissingMethodException(type.FullName, "ExposeData");
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = AccessTools.DeclaredField(type, name);
            if (field == null || field.IsStatic || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static void Explosion(Harmony harmony, string name)
        {
            var type = RequiredType(name);
            if (type.BaseType != typeof(Verse.Explosion)) throw new InvalidOperationException("Explosion base changed");
            for (int i = 0; i < ExplosionFields.Length; i++)
            {
                Field(type, ExplosionFields[i], ExplosionFieldTypes[i]);
                Field(typeof(Verse.Explosion), ExplosionFields[i], ExplosionFieldTypes[i]);
            }
            var method = RequiredExpose(type);
            CheckExplosionKeys(PatchProcessor.GetOriginalInstructions(method));
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(Patch_Light350SnapshotsMp), nameof(MigrateExplosion)),
                transpiler: new HarmonyMethod(typeof(Patch_Light350SnapshotsMp), nameof(ExplosionKeys)));
        }
        private static void CheckExplosionKeys(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            foreach (var key in ExplosionSerializedKeys)
                if (body.Count(i => i.opcode == OpCodes.Ldstr && (string)i.operand == key) != 1)
                    throw new InvalidOperationException("Explosion serializer key changed: " + key);
        }
        private static IEnumerable<CodeInstruction> ExplosionKeys(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            CheckExplosionKeys(body);
            foreach (var instruction in body)
                if (instruction.opcode == OpCodes.Ldstr && ExplosionSerializedKeys.Contains((string)instruction.operand))
                    instruction.operand = ExplosionPrefix + instruction.operand;
            return body;
        }
        private static XmlElement LastElement(XmlNode parent, string key) => parent?.ChildNodes.OfType<XmlElement>().LastOrDefault(n => n.Name == key);
        private static void CopyAs(XmlNode parent, XmlElement source, string key)
        {
            var node = parent.OwnerDocument.CreateElement(key);
            foreach (XmlAttribute attribute in source.Attributes) node.Attributes.Append((XmlAttribute)attribute.CloneNode(true));
            foreach (XmlNode child in source.ChildNodes) node.AppendChild(child.CloneNode(true));
            parent.AppendChild(node);
        }
        private static void MigrateExplosion()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            var parent = Scribe.loader.curXmlParent;
            if (parent == null) return;
            foreach (var key in ExplosionSerializedKeys)
            {
                if (parent[ExplosionPrefix + key] != null) continue;
                // Prefer the last old node when duplicate writes exist. With
                // only one scalar node the old format cannot distinguish an
                // omitted parent default from an omitted child default; retain
                // the available value. Never delete the parent's node.
                var old = LastElement(parent, key);
                if (old != null) CopyAs(parent, old, ExplosionPrefix + key);
            }
        }
        private static bool IsDeepFaction(MethodInfo method) => method != null && method.DeclaringType == typeof(Scribe_Deep) &&
            method.Name == "Look" && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(Faction) }) &&
            method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(Faction).MakeByRefType(), typeof(string), typeof(object[]) });
        private static IEnumerable<CodeInstruction> FactionWriter(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            int count = 0;
            foreach (var instruction in body)
                if (IsDeepFaction(instruction.operand as MethodInfo))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Light350SnapshotsMp), nameof(FactionReference));
                    count++;
                }
            if (count != 1) throw new InvalidOperationException("Deep faction writer coverage changed");
            return body;
        }
        private static void FactionReference(ref Faction value, string unusedLabel, object[] unusedCtorArgs) => Scribe_References.Look(ref value, FactionKey);
        private static void CheckTunnelKey(IEnumerable<CodeInstruction> instructions)
        {
            if (instructions.Count(i => i.opcode == OpCodes.Ldstr && (string)i.operand == "faction") != 1)
                throw new InvalidOperationException("Tunnel parent faction serializer changed");
        }
        private static IEnumerable<CodeInstruction> TunnelFactionKeys(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            CheckTunnelKey(body);
            foreach (var instruction in body)
                if (instruction.opcode == OpCodes.Ldstr && (string)instruction.operand == "faction")
                    instruction.operand = TunnelFactionKey;
            return body;
        }
        private static void MigrateTunnelFaction() => MigrateTunnelXml(false);
        private static void MigrateTunnelXml(bool legacyUnderground)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            var parent = Scribe.loader.curXmlParent;
            if (parent == null || parent[TunnelFactionKey] != null) return;
            var nodes = parent.ChildNodes.OfType<XmlElement>().Where(n => n.Name == "faction").ToList();
            if (legacyUnderground && nodes.Count > 0) nodes.RemoveAt(nodes.Count - 1);
            if (nodes.Count == 0) return;
            CopyAs(parent, nodes[nodes.Count - 1], TunnelFactionKey);
            // Thing omits its null faction; the mod's reference writer does not.
            // Restore that omitted root value before Thing.ExposeData reads it.
            if (nodes.Count == 1)
            {
                var empty = parent.OwnerDocument.CreateElement("faction");
                empty.InnerText = "null";
                parent.InsertBefore(empty, nodes[0]);
            }
        }
        private static void MigrateFaction()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            var parent = Scribe.loader.curXmlParent;
            MigrateTunnelXml(parent != null && parent[FactionKey] == null);
            if (parent == null || parent[FactionKey] != null) return;
            var old = LastElement(parent, "faction");
            if (old == null) return;
            string reference = null;
            if (old.GetAttribute("IsNull") == "True" || old.GetAttribute("IsNull") == "true") reference = "null";
            // Faction.ExposeData omits loadID when it is zero.
            else if ((old["loadID"] != null || old["def"] != null) && int.TryParse(old["loadID"]?.InnerText ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                reference = "Faction_" + id.ToString(CultureInfo.InvariantCulture);
            else if (!old.ChildNodes.OfType<XmlElement>().Any() && (old.InnerText == "null" || old.InnerText.StartsWith("Faction_", StringComparison.Ordinal)))
                reference = old.InnerText;
            if (reference == null) { Log.Error("[MP-MeowOnlineShop][Light350-B3] Cannot recover legacy underground faction reference."); return; }
            var node = parent.OwnerDocument.CreateElement(FactionKey);
            node.InnerText = reference;
            parent.AppendChild(node);
        }
        private static void WhiteMoonExpose(MapComponent __instance)
        {
            if (!whiteMoonComponent.IsInstanceOfType(__instance)) return;
            var centers = (List<IntVec3>)moonFields["circleCenters"].GetValue(__instance);
            Scribe_Collections.Look(ref centers, "meowMpMoonCircleCenters", LookMode.Value);
            moonFields["circleCenters"].SetValue(__instance, centers);
            foreach (var key in new[] { "currentTick", "totalSpears", "spearsPerTick", "spearsLaunched", "deathTick" })
            {
                int value = (int)moonFields[key].GetValue(__instance);
                Scribe_Values.Look(ref value, "meowMpMoon_" + key, key == "deathTick" ? -1 : 0);
                moonFields[key].SetValue(__instance, value);
            }
            bool dead = (bool)moonFields["isDead"].GetValue(__instance);
            Scribe_Values.Look(ref dead, "meowMpMoon_isDead", false);
            moonFields["isDead"].SetValue(__instance, dead);
            foreach (var key in new[] { "caster", "whiteMoon" })
            {
                var pawn = (Pawn)moonFields[key].GetValue(__instance);
                Scribe_References.Look(ref pawn, "meowMpMoon_" + key);
                moonFields[key].SetValue(__instance, pawn);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit) moonFields["cachedTracker"].SetValue(__instance, null);
        }
    }
}
