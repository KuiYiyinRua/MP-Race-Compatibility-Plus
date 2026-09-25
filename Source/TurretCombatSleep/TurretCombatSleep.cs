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
using Verse.AI;

namespace Meow.TurretCombatSleep
{
    public sealed class TurretCombatSleepMod : Mod
    {
        private readonly TurretCombatSleepSettings settings;

        public TurretCombatSleepMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<TurretCombatSleepSettings>();
            if (settings.enabled && ModsConfig.IsActive("rwmt.Multiplayer"))
                LongEventHandler.ExecuteWhenFinished(TurretCombatSleep.Install);
            else
                Log.Message("[Meow.TurretCombatSleep] Disabled by mod setting (default OFF)");
        }

        public override string SettingsCategory() => "炮台战斗休眠（实验）";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.CheckboxLabeled("启用炮台战斗休眠实验（默认关闭；重启后生效）", ref settings.enabled);
            listing.Label("所有联机玩家必须设置相同并重启。未完成游戏运行验证；关闭时不安装本模块的模拟补丁。");
            listing.End();
        }
    }

    public sealed class TurretCombatSleepSettings : ModSettings
    {
        public bool enabled;

        public override void ExposeData() => Scribe_Values.Look(ref enabled, "turretCombatSleepEnabled", false);
    }

    // Derived threat membership may be shared; combat state always remains live.
    // No wall clock, current-view map or local settings drive simulation decisions.
    public static class TurretCombatSleep
    {
        public const string HarmonyId = "meow.turretcombatsleep";
        private static readonly AccessTools.FieldRef<Building_TurretGun, int> Cooldown =
            AccessTools.FieldRefAccess<Building_TurretGun, int>("burstCooldownTicksLeft");
        private static readonly AccessTools.FieldRef<Building_TurretGun, int> Warmup =
            AccessTools.FieldRefAccess<Building_TurretGun, int>("burstWarmupTicksLeft");
        private static readonly AccessTools.FieldRef<Building_TurretGun, bool> Activated =
            AccessTools.FieldRefAccess<Building_TurretGun, bool>("burstActivated");
        private static readonly AccessTools.FieldRef<Building_TurretGun, Effecter> Progress =
            AccessTools.FieldRefAccess<Building_TurretGun, Effecter>("progressBarEffecter");
        private static readonly AccessTools.FieldRef<Building_TurretGun, CompMannable> Manning =
            AccessTools.FieldRefAccess<Building_TurretGun, CompMannable>("mannableComp");
        private static readonly AccessTools.FieldRef<Verb, List<Tuple<Effecter, TargetInfo, TargetInfo>>> MaintainedEffects =
            AccessTools.FieldRefAccess<Verb, List<Tuple<Effecter, TargetInfo, TargetInfo>>>("maintainedEffecters");

        // This cache holds only immutable type metadata and compiled accessors, never game state.
        private static readonly Dictionary<Type, Layout> Layouts = new Dictionary<Type, Layout>();
        private sealed class Layout
        {
            public Func<Building_Turret, int> Cooldown, Warmup;
            public Func<Building_Turret, bool> Activated;
            public Func<Building_Turret, Effecter> Progress;
            public Func<Building_Turret, Thing> Gun;
            public bool WatchesProjectiles;
        }

        private static Func<Building_Turret, T> Getter<T>(Type type, string name, bool required = false)
            => FieldGetter<Building_Turret, T>(type, name, required);

        internal static Func<TSource, T> FieldGetter<TSource, T>(Type type, string name, bool required = true)
        {
            var field = AccessTools.Field(type, name);
            if (field == null)
            {
                if (required) throw new MissingFieldException(type.FullName, name);
                return _ => default(T);
            }
            if (!typeof(T).IsAssignableFrom(field.FieldType)) throw new InvalidOperationException("Unexpected field type: " + field);
            var method = new DynamicMethod("TurretSleep_" + name, typeof(T), new[] { typeof(TSource) }, typeof(TurretCombatSleep).Module, true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<TSource, T>)method.CreateDelegate(typeof(Func<TSource, T>));
        }

        public static void Install()
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                var types = DefDatabase<ThingDef>.AllDefsListForReading.Select(d => d.thingClass)
                    .Where(t => t != null && typeof(Building_Turret).IsAssignableFrom(t))
                    .Concat(new[] { typeof(Building_TurretGun) }).Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
                var ticks = new HashSet<MethodInfo>();
                int supported = 0;
                foreach (var type in types)
                {
                    try
                    {
                        Layouts[type] = new Layout
                        {
                            Cooldown = Getter<int>(type, "burstCooldownTicksLeft", true),
                            Warmup = Getter<int>(type, "burstWarmupTicksLeft", true),
                            Gun = Getter<Thing>(type, "gun", true),
                            Activated = Getter<bool>(type, "burstActivated"),
                            Progress = Getter<Effecter>(type, "progressBarEffecter"),
                            WatchesProjectiles = NeedsProjectileWatch(type)
                        };
                        for (var current = type; current != null && typeof(Building_Turret).IsAssignableFrom(current); current = current.BaseType)
                        {
                            var tick = AccessTools.DeclaredMethod(current, "Tick", Type.EmptyTypes);
                            if (tick != null) ticks.Add(tick);
                        }
                        supported++;
                        Log.Message("[Meow.TurretCombatSleep] PROFILE " + type.FullName);
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[Meow.TurretCombatSleep] Unsupported layout, retained original: " + type.FullName + " (" + e.Message + ")");
                    }
                }
                foreach (var tick in ticks.OrderBy(t => t.DeclaringType.FullName, StringComparer.Ordinal))
                {
                    try { harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(TurretCombatSleep), nameof(RewriteTick))); }
                    catch (Exception e) { Log.Error("[Meow.TurretCombatSleep] REQUIRED_TARGET_FAILED " + tick + " " + e); }
                }
                // Shared fallback for building-mounted weapon components and unfamiliar
                // turret classes: suppress only an empty native targeting query. Their
                // ticks, cooldowns, rotation/recoil and component state are left intact.
                int queryMethods = 0;
                foreach (var name in new[] { "BestAttackTarget", "BestShootTargetFromCurrentPosition" })
                {
                    foreach (var query in typeof(AttackTargetFinder).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.Name == name && m.ReturnType == typeof(IAttackTarget) &&
                            m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == typeof(IAttackTargetSearcher)))
                    {
                        harmony.Patch(query, prefix: new HarmonyMethod(typeof(TurretCombatSleep), nameof(BuildingSearchPrefix)));
                        queryMethods++;
                    }
                }
                var sprayer = AccessTools.TypeByName("RavenRace.Features.CustomTurrets.Arknights.Flame.CompFlameTurretSprayer");
                var scan = sprayer == null ? null : AccessTools.DeclaredMethod(sprayer, "TryFindEnemyPawnInSprayArea", new[] { typeof(Pawn).MakeByRefType() });
                if (scan != null && scan.ReturnType == typeof(bool))
                    harmony.Patch(scan, prefix: new HarmonyMethod(typeof(TurretCombatSleep), nameof(FlameSearchPrefix)));
                MapThreatManagement.Install(harmony);
                WeaponComponentSleep.Install(harmony);
                RavenAccessorCache.Install(harmony);
                RavenPowerLookup.Install(harmony);
                FlameVisualSleep.Install(harmony);
                DeepSleepEntry.Install(harmony);
                Log.Message("[Meow.TurretCombatSleep] READY version=1.3.0 profiles=" + supported + " tickMethods=" + ticks.Count + " nativeQueries=" + queryMethods + " flameScan=" + (scan != null) + " mvid=" +
                    typeof(TurretCombatSleep).Assembly.ManifestModule.ModuleVersionId);
            }
            catch (Exception e)
            {
                Log.Error("[Meow.TurretCombatSleep] REQUIRED_TARGET_FAILED " + e);
            }
        }

        public static bool ShouldSleep(Building_TurretGun turret)
        {
            if (!MP.IsInMultiplayer || turret == null || !turret.Spawned ||
                turret.Faction == null ||
                !turret.Faction.IsPlayer ||
                turret.ForcedTarget.IsValid || turret.CurrentTarget.IsValid ||
                Warmup(turret) > 0 || Activated(turret) || Progress(turret) != null)
                return false;

            var manning = Manning(turret);
            if (manning != null && manning.MannedNow) return false;
            // Native code does not advance an unmanned mortar's cooldown either.
            if (Cooldown(turret) > 0 && manning == null) return false;

            // In this installed version VerbsTick calls nonvirtual Verb.VerbTick.
            // Idle verbs still maintain effecters: finish those before sleeping.
            var equipment = ComponentIndex.Get<CompEquippable>(turret.gun as ThingWithComps);
            if (!VerbsCanSleep(equipment)) return false;

            return MapIsQuiet(turret);
        }

        internal static bool VerbsCanSleep(CompEquippable equipment)
        {
            if (equipment == null) return false;
            var verbs = equipment.AllVerbs;
            if (verbs.Count == 0) return false;
            for (int i = 0; i < verbs.Count; i++)
            {
                var verb = verbs[i];
                if (verb.state != VerbState.Idle || MaintainedEffects(verb).Count != 0) return false;
            }
            return true;
        }

        public static bool ShouldSleepSpinBody(Building_Turret turret)
        {
            return ShouldPauseCombat(turret);
        }

        public static bool MapIsQuiet(Thing thing)
        {
            if (!MP.IsInMultiplayer || thing == null || !thing.Spawned || thing.Faction == null || !thing.Faction.IsPlayer)
                return false;
            if (!SharedThreatIndex.Quiet(thing)) return false;
            // Native/Raven guns cannot target projectiles. Unknown/custom target
            // finders remain conservative so interception keeps working.
            bool watch = !(thing is Building_Turret turret) ||
                !Layouts.TryGetValue(turret.GetType(), out var layout) || layout.WatchesProjectiles;
            return !watch || thing.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).Count == 0;
        }

        private static bool NeedsProjectileWatch(Type type)
        {
            var finder = AccessTools.Method(type, "TryFindNewTarget", Type.EmptyTypes);
            string owner = finder?.DeclaringType?.FullName;
            return owner != "RimWorld.Building_TurretGun" && owner != "AncotLibrary.Building_SpinTurretGun" &&
                owner != "RavenRace.Features.CustomTurrets.PatternTurrets.Building_PatternTurret" &&
                owner != "RavenRace.Features.CustomTurrets.Arknights.Flame.Building_FlamePatternTurret";
        }

        public static bool ShouldPauseCombat(Building_Turret turret)
        {
            if (!MP.IsInMultiplayer || turret == null || !turret.Spawned || turret.Faction == null || !turret.Faction.IsPlayer ||
                turret.ForcedTarget.IsValid || turret.CurrentTarget.IsValid ||
                !Layouts.TryGetValue(turret.GetType(), out var layout) ||
                layout.Warmup(turret) > 0 || layout.Activated(turret) || layout.Progress(turret) != null)
                return false;
            var manning = ComponentIndex.Get<CompMannable>(turret);
            if (manning != null && manning.MannedNow) return false;
            if (layout.Cooldown(turret) > 0 && manning == null) return false;
            var equipment = ComponentIndex.Get<CompEquippable>(layout.Gun(turret) as ThingWithComps);
            if (!VerbsCanSleep(equipment)) return false;
            return MapIsQuiet(turret);
        }

        private static bool FlameSearchPrefix(ThingComp __instance, ref Pawn __0, ref bool __result)
        {
            if (!MapIsQuiet(__instance.parent)) return true;
            __0 = null;
            __result = false;
            return false;
        }

        private static bool BuildingSearchPrefix(IAttackTargetSearcher __0, TargetScanFlags __1, ref IAttackTarget __result)
        {
            // Do not change callers intentionally searching for non-threatening targets.
            if (!MP.IsInMultiplayer || (__1 & TargetScanFlags.NeedThreat) == 0 || __0 == null || !(__0.Thing is Building building) ||
                !MapIsQuiet(building)) return true;
            __result = null;
            return false;
        }

        private static int CallArguments(MethodInfo method)
        {
            if (method.IsStatic || method.ReturnType != typeof(void)) return -1;
            if (method.Name == "TryStartShootSomething" && typeof(Building_Turret).IsAssignableFrom(method.DeclaringType) &&
                method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(bool) })) return 2;
            if (method.GetParameters().Length != 0) return -1;
            if (method.Name == "TurretTopTick" && (method.DeclaringType == typeof(TurretTop) ||
                method.DeclaringType.FullName == "AncotLibrary.SpinTurretTop")) return 1;
            if (method.Name == "SnapToIdleAngle" && method.DeclaringType.FullName == "RavenRace.Features.CustomTurrets.PatternTurrets.Building_PatternTurret") return 1;
            if (method.Name == "LockTopToPlacedRotation" && method.DeclaringType.FullName == "RavenRace.Features.CustomTurrets.Arknights.Flame.Building_FlamePatternTurret") return 1;
            return -1;
        }

        public static IEnumerable<CodeInstruction> RewriteTick(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            if (code.Any(c => c.blocks.Count != 0))
            {
                Log.Warning("[Meow.TurretCombatSleep] Retained exception-region Tick: " + __originalMethod.DeclaringType.FullName);
                return code;
            }
            bool native = __originalMethod.DeclaringType == typeof(Building_TurretGun);
            bool ancot = __originalMethod.DeclaringType.FullName == "AncotLibrary.Building_SpinTurretGun";
            if (native || ancot)
            {
                var baseTick = AccessTools.DeclaredMethod(typeof(Building_Turret), "Tick");
                // Ancot's shipped debug build retains a leading nop; ignore only nops,
                // never arbitrary initialization or a branch before the base call.
                int first = code.FindIndex(c => c.opcode != OpCodes.Nop);
                if (first < 0 || first + 2 >= code.Count || code[first].opcode != OpCodes.Ldarg_0 || !code[first + 1].Calls(baseTick))
                    throw new InvalidOperationException("Unrecognized turret Tick prologue: " + __originalMethod.DeclaringType.FullName);
                int insertion = first + 2;
                Label awake = generator.DefineLabel();
                code[insertion].labels.Add(awake);
                // Returning from this base body still lets each subclass finish its own Tick.
                code.InsertRange(insertion, new[] {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TurretCombatSleep), native ? nameof(ShouldSleep) : nameof(ShouldSleepSpinBody))),
                    new CodeInstruction(OpCodes.Brfalse, awake), new CodeInstruction(OpCodes.Ret)
                });
            }
            var result = new List<CodeInstruction>();
            int guarded = 0;
            foreach (var instruction in code)
            {
                var method = instruction.operand as MethodInfo;
                int args = (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && method != null ? CallArguments(method) : -1;
                if (args < 0) { result.Add(instruction); continue; }
                var run = generator.DefineLabel();
                var end = generator.DefineLabel();
                var check = new CodeInstruction(OpCodes.Ldarg_0);
                check.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                result.Add(check);
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TurretCombatSleep), nameof(ShouldPauseCombat))));
                result.Add(new CodeInstruction(OpCodes.Brfalse, run));
                for (int i = 0; i < args; i++) result.Add(new CodeInstruction(OpCodes.Pop));
                result.Add(new CodeInstruction(OpCodes.Br, end));
                instruction.labels.Add(run);
                result.Add(instruction);
                var after = new CodeInstruction(OpCodes.Nop);
                after.labels.Add(end);
                result.Add(after);
                guarded++;
            }
            if (guarded > 0) Log.Message("[Meow.TurretCombatSleep] GUARDED " + __originalMethod.DeclaringType.FullName + " calls=" + guarded);
            return result;
        }
    }
}
