using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TurretCombatSleep
{
    internal static class WeaponComponentSleep
    {
        private sealed class Layout
        {
            public Func<ThingComp, int> Warmup, Cooldown;
            public Func<ThingComp, bool> Warming;
            public Func<ThingComp, LocalTargetInfo> Current, Forced;
            public Func<ThingComp, Thing> Gun;
            public bool Sprayer;
        }
        private static readonly List<Layout> Layouts = new List<Layout>();
        private static readonly Dictionary<MethodBase, int> Indices = new Dictionary<MethodBase, int>();

        internal static void Install(Harmony harmony)
        {
            foreach (string name in new[] {
                "AncotLibrary.CompTurretGun_Building",
                "PLAMilira.CompPLAMilira_TurretGun_Building",
                "RavenRace.Features.CustomTurrets.Arknights.Flame.CompFlameTurretSprayer" })
            {
                var type = AccessTools.TypeByName(name);
                if (type == null) continue;
                try
                {
                    bool spray = name.EndsWith(".CompFlameTurretSprayer", StringComparison.Ordinal);
                    var layout = new Layout { Sprayer = spray };
                    layout.Warmup = Read<int>(type, spray ? "sprayTicksLeft" : "burstWarmupTicksLeft");
                    layout.Cooldown = Read<int>(type, spray ? "cooldownTicksLeft" : "burstCooldownTicksLeft");
                    if (!spray)
                    {
                        layout.Warming = Read<bool>(type, "IsWarmingup");
                        layout.Current = Read<LocalTargetInfo>(type, "currentTarget");
                        layout.Forced = Read<LocalTargetInfo>(type, "forcedTarget");
                        layout.Gun = Read<Thing>(type, "gun");
                    }
                    var tick = AccessTools.DeclaredMethod(type, "CompTick", Type.EmptyTypes);
                    if (tick == null) throw new MissingMethodException(name, "CompTick");
                    Indices.Add(tick, Layouts.Count);
                    Layouts.Add(layout);
                    harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(WeaponComponentSleep), nameof(Rewrite)));
                    Log.Message("[Meow.TurretCombatSleep] COMPONENT_SLEEP " + name);
                }
                catch (Exception e) { Log.Error("[Meow.TurretCombatSleep] REQUIRED_TARGET_FAILED weapon component " + name + " " + e); }
            }
        }

        private static Func<ThingComp, T> Read<T>(Type type, string field) => TurretCombatSleep.FieldGetter<ThingComp, T>(type, field);

        private static bool CanSleep(ThingComp component, int index)
        {
            if (!MP.IsInMultiplayer || !(component.parent is Building building) || !building.Spawned ||
                building.Faction == null || !building.Faction.IsPlayer) return false;
            if (building is Building_Turret turret && (turret.ForcedTarget.IsValid || turret.CurrentTarget.IsValid)) return false;
            var layout = Layouts[index];
            if (layout.Warmup(component) > 0 || layout.Cooldown(component) > 0) return false;
            if (!layout.Sprayer && (layout.Warming(component) ||
                layout.Current(component).IsValid || layout.Forced(component).IsValid ||
                !TurretCombatSleep.VerbsCanSleep(ComponentIndex.Get<CompEquippable>(layout.Gun(component) as ThingWithComps)))) return false;
            return TurretCombatSleep.MapIsQuiet(building);
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            if (code.Any(c => c.blocks.Count != 0)) throw new InvalidOperationException("Unexpected exception regions in weapon component Tick");
            bool recoil = __originalMethod.DeclaringType.FullName == "PLAMilira.CompPLAMilira_TurretGun_Building";
            var boundary = recoil ? AccessTools.DeclaredMethod(__originalMethod.DeclaringType, "TickFakeRecoil", Type.EmptyTypes)
                : AccessTools.DeclaredMethod(typeof(ThingComp), "CompTick", Type.EmptyTypes);
            if (boundary == null) throw new MissingMethodException("Weapon component tick boundary");
            var calls = code.Select((c, i) => new { c, i }).Where(x => x.c.Calls(boundary)).Select(x => x.i).ToArray();
            if (calls.Length != 1 || calls[0] + 1 >= code.Count) throw new InvalidOperationException("Unrecognized weapon tick layout");
            int insertion = calls[0] + 1;
            var awake = generator.DefineLabel();
            code[insertion].labels.Add(awake);
            code.InsertRange(insertion, new[] {
                new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldc_I4, Indices[__originalMethod]),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WeaponComponentSleep), nameof(CanSleep))),
                new CodeInstruction(OpCodes.Brfalse, awake), new CodeInstruction(OpCodes.Ret)
            });
            return code;
        }
    }
}
