using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meow.TurretCombatSleep
{
    // Hysteresis only on restart. Never hold PowerOn after a failed payment,
    // defer a shutdown, consume extra energy, or use a process-local timer.
    internal static class NivarianWirelessRestart
    {
        internal const int RestartReserveTicks = 60;
        private static Type turretType;
        private static Func<ThingComp, CompPowerTrader> power;
        private static Func<ThingComp, float> unreserved;

        internal static void Install(Harmony harmony)
        {
            turretType = AccessTools.TypeByName("NivarianRace.DraconicMilitary.Building_TurretWithValidator");
            var adapter = AccessTools.TypeByName("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianWirelessPowerAdapter");
            if (turretType == null || adapter == null) return;
            try
            {
                var tick = AccessTools.DeclaredMethod(adapter, "TickWireless", new[] { typeof(float) });
                var can = AccessTools.DeclaredMethod(adapter, "CanPowerNow", Type.EmptyTypes);
                var net = AccessTools.PropertyGetter(adapter, "Net");
                var energy = net == null ? null : AccessTools.PropertyGetter(net.ReturnType, "UnreservedEnergy");
                if (tick == null || tick.ReturnType != typeof(float) || can == null || can.ReturnType != typeof(bool) || energy == null)
                    throw new MissingMethodException("Wireless restart targets");
                power = TurretCombatSleep.FieldGetter<ThingComp, CompPowerTrader>(adapter, "powerTrader");
                var reader = new DynamicMethod("TurretSleep_UnreservedEnergy", typeof(float), new[] { typeof(ThingComp) }, typeof(NivarianWirelessRestart).Module, true);
                var il = reader.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, adapter);
                il.Emit(OpCodes.Call, net); il.Emit(OpCodes.Callvirt, energy); il.Emit(OpCodes.Ret);
                unreserved = (Func<ThingComp, float>)reader.CreateDelegate(typeof(Func<ThingComp, float>));
                // Validate both layouts before installing either hook. Other mods'
                // prefixes/finalizers and the original research/faction gates stay intact.
                Tick(PatchProcessor.GetOriginalInstructions(tick)).ToList();
                CanPower(PatchProcessor.GetOriginalInstructions(can)).ToList();
                harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(NivarianWirelessRestart), nameof(Tick)));
                harmony.Patch(can, transpiler: new HarmonyMethod(typeof(NivarianWirelessRestart), nameof(CanPower)));
                Log.Message("[Meow.TurretCombatSleep] NIVARIAN_WIRELESS_RESTART reserveTicks=60 turrets=Turret_Cryo,Turret_CryoMortar,Turret_NivarianWinter");
            }
            catch (Exception e)
            {
                // Remove only this integration's hooks if a later transpiler fails.
                foreach (var name in new[] { "TickWireless", "CanPowerNow" })
                {
                    var method = AccessTools.DeclaredMethod(adapter, name);
                    if (method == null) continue;
                    foreach (var hook in new[] { nameof(Tick), nameof(CanPower) })
                        harmony.Unpatch(method, AccessTools.Method(typeof(NivarianWirelessRestart), hook));
                }
                Log.Warning("[Meow.TurretCombatSleep] NIVARIAN_WIRELESS_FALLBACK original retained: " + e.Message);
            }
        }

        internal static bool Target(string defName)
            => defName == "Turret_Cryo" || defName == "Turret_CryoMortar" || defName == "Turret_NivarianWinter";

        private static bool Applies(CompPowerTrader trader)
            => TurretCombatSleep.Active && trader?.parent != null && trader.parent.Spawned &&
                trader.parent.Faction?.IsPlayer == true && turretType.IsInstanceOfType(trader.parent) && Target(trader.parent.def.defName);

        // Pure current-input policy: joining/loading never needs a saved timer or
        // a warmed cache. An online turret still uses the native one-tick gate.
        internal static bool RestartAllowed(bool nativeEnough, bool powered, float demand, float available)
            => nativeEnough && (powered || demand <= 0f || available >= demand * RestartReserveTicks);

        private static void SetPower(CompPowerTrader trader, bool value, ThingComp adapter, float available)
        {
            if (value && Applies(trader) && !RestartAllowed(true, trader.PowerOn, -trader.EnergyOutputPerTick, available)) return;
            trader.PowerOn = value;
        }

        private static bool CheckPower(bool result, ThingComp adapter)
        {
            if (!result || !TurretCombatSleep.Active) return result;
            var trader = power(adapter);
            if (!Applies(trader) || trader.PowerOn) return result;
            return RestartAllowed(result, false, -trader.EnergyOutputPerTick, unreserved(adapter));
        }

        private static IEnumerable<CodeInstruction> Tick(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var setter = AccessTools.PropertySetter(typeof(CompPowerTrader), nameof(CompPowerTrader.PowerOn));
            var indexes = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(setter)).ToArray();
            if (code.Any(c => c.blocks.Count != 0) || indexes.Length != 3 ||
                indexes.Count(i => i > 0 && code[i - 1].opcode == OpCodes.Ldc_I4_1) != 1 ||
                indexes.Count(i => i > 0 && code[i - 1].opcode == OpCodes.Ldc_I4_0) != 2)
                throw new InvalidOperationException("Wireless Tick layout changed; expected one start and two stops");
            foreach (var c in code)
            {
                if (!c.Calls(setter)) { yield return c; continue; }
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(c.labels); c.labels.Clear();
                yield return load;
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                c.opcode = OpCodes.Call;
                c.operand = AccessTools.Method(typeof(NivarianWirelessRestart), nameof(SetPower));
                yield return c;
            }
        }

        private static IEnumerable<CodeInstruction> CanPower(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            if (code.Count == 0 || code.Last().opcode != OpCodes.Ret || code.Any(c => c.blocks.Count != 0))
                throw new InvalidOperationException("CanPowerNow layout changed");
            foreach (var c in code)
            {
                if (c.opcode == OpCodes.Ret)
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(c.labels); c.labels.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NivarianWirelessRestart), nameof(CheckPower)));
                }
                yield return c;
            }
        }
    }
}
