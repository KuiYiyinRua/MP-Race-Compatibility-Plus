using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.SpecialPawnIdeologyCompatibility
{
    [StaticConstructorOnStartup]
    public static class SpecialPawnIdeology
    {
        private const string Monitor = "MiliraImperium.MiliraImperiumSpecialPawnMonitor";
        private const string Conversion = "MiliraXian.Characters.GameComponent_SpecialPawnPrimaryCultureConversion";

        static SpecialPawnIdeology()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira")) return;
            if (!MP.enabled) return;
            var harmony = new Harmony("meow.special-pawn-ideology.multiplayer");
            try
            {
                if (ModsConfig.IsActive("Ariandel.MiliraImperium"))
                {
                    harmony.Patch(Required(Monitor, "RunSpecialPawnCheck"), transpiler:
                        new HarmonyMethod(typeof(SpecialPawnIdeology), nameof(MonitorTranspiler)));
                    Log.Message("[Meow.SpecialPawnIdeology] Imperium monitor uses each pawn's owning player faction ideology.");
                }
                if (ModsConfig.IsActive("HeChuanRiver.MiliraXian.NeiyuLaw"))
                {
                    foreach (string name in new[] { "RegisterPlayerSpecialPawns", "RegisterIfNeeded", "ProcessPendingConversions", "IsEligibleSpecialPawn" })
                        harmony.Patch(Required(Conversion, name), transpiler:
                            new HarmonyMethod(typeof(SpecialPawnIdeology), nameof(ConversionTranspiler)));
                    harmony.Patch(Required(Conversion, "IsEligibleSpecialPawn"), postfix:
                        new HarmonyMethod(typeof(SpecialPawnIdeology), nameof(EligiblePostfix)));
                    Log.Message("[Meow.SpecialPawnIdeology] Xian registration and pending conversions use owning player factions; original saved queues and deadlines retained.");
                }
            }
            catch (Exception e)
            {
                harmony.UnpatchAll(harmony.Id);
                Log.Error("[Meow.SpecialPawnIdeology] Required target failed; patches rolled back: " + e);
            }
        }

        private static MethodInfo Required(string type, string name)
        {
            return AccessTools.DeclaredMethod(AccessTools.TypeByName(type), name)
                ?? throw new MissingMethodException(type, name);
        }

        public static bool IsPlayerPawn(Pawn pawn)
        {
            return pawn?.Faction?.def?.isPlayer == true;
        }

        public static Faction OwnerFaction(Pawn pawn)
        {
            if (!MP.IsInMultiplayer) return Faction.OfPlayer;
            return IsPlayerPawn(pawn) ? pawn.Faction : null;
        }

        public static Ideo OwnerIdeology(Pawn pawn, Ideo original)
        {
            if (!MP.IsInMultiplayer) return original;
            return ModsConfig.IdeologyActive && IsPlayerPawn(pawn) ? pawn.Faction.ideos?.PrimaryIdeo : null;
        }

        public static List<Pawn> StablePawns(List<Pawn> original)
        {
            if (!MP.IsInMultiplayer) return original;
            return original.Where(p => p != null).Distinct().OrderBy(p => p.thingIDNumber).ToList();
        }

        public static List<Pawn> AllPlayerPawns()
        {
            if (!MP.IsInMultiplayer) return PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
            return StablePawns(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive).Where(IsPlayerPawn).ToList();
        }

        public static void EligiblePostfix(Pawn __0, ref bool __result)
        {
            // A factionless pawn must not become eligible because null == null.
            if (MP.IsInMultiplayer && !IsPlayerPawn(__0)) __result = false;
        }

        private static int LocalOfType(MethodBase method, Type type)
        {
            var locals = method.GetMethodBody().LocalVariables.Where(v => v.LocalType == type).ToArray();
            if (locals.Length != 1) throw new InvalidOperationException(method.Name + ": expected one " + type.Name + " local");
            return locals[0].LocalIndex;
        }

        private static bool Stores(CodeInstruction code, int index)
        {
            if (code.opcode == OpCodes.Stloc_0) return index == 0;
            if (code.opcode == OpCodes.Stloc_1) return index == 1;
            if (code.opcode == OpCodes.Stloc_2) return index == 2;
            if (code.opcode == OpCodes.Stloc_3) return index == 3;
            if (code.opcode != OpCodes.Stloc && code.opcode != OpCodes.Stloc_S) return false;
            return (code.operand is LocalBuilder b ? b.LocalIndex : Convert.ToInt32(code.operand)) == index;
        }

        public static IEnumerable<CodeInstruction> MonitorTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int pawn = LocalOfType(__originalMethod, typeof(Pawn));
            int ideo = LocalOfType(__originalMethod, typeof(Ideo));
            var result = new List<CodeInstruction>();
            int stores = 0, lists = 0;
            var getter = AccessTools.PropertyGetter(typeof(PawnsFinder), nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive));
            foreach (var code in instructions)
            {
                result.Add(code);
                if (code.Calls(getter))
                {
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpecialPawnIdeology), nameof(StablePawns))));
                    lists++;
                }
                if (Stores(code, pawn))
                {
                    result.Add(CodeInstruction.LoadLocal(pawn));
                    result.Add(CodeInstruction.LoadLocal(ideo));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpecialPawnIdeology), nameof(OwnerIdeology))));
                    result.Add(CodeInstruction.StoreLocal(ideo));
                    stores++;
                }
            }
            if (stores != 1 || lists != 1) throw new InvalidOperationException("Imperium monitor IL shape changed");
            return result;
        }

        public static IEnumerable<CodeInstruction> ConversionTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var result = new List<CodeInstruction>();
            var ofPlayer = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer));
            var playerList = AccessTools.PropertyGetter(typeof(PawnsFinder), nameof(PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction));
            int replacements = 0, lists = 0;
            bool arg = __originalMethod.Name == "RegisterIfNeeded" || __originalMethod.Name == "IsEligibleSpecialPawn";
            int pawn = arg ? -1 : LocalOfType(__originalMethod, typeof(Pawn));
            foreach (var code in instructions)
            {
                if (code.Calls(ofPlayer))
                {
                    var load = arg ? new CodeInstruction(__originalMethod.IsStatic ? OpCodes.Ldarg_0 : OpCodes.Ldarg_1)
                        : CodeInstruction.LoadLocal(pawn);
                    load.labels.AddRange(code.labels);
                    load.blocks.AddRange(code.blocks);
                    result.Add(load);
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpecialPawnIdeology), nameof(OwnerFaction))));
                    replacements++;
                }
                else if (code.Calls(playerList))
                {
                    var replacement = new CodeInstruction(code);
                    replacement.operand = AccessTools.Method(typeof(SpecialPawnIdeology), nameof(AllPlayerPawns));
                    result.Add(replacement);
                    lists++;
                }
                else result.Add(code);
            }
            int expected = __originalMethod.Name == "ProcessPendingConversions" ? 2 : 1;
            int expectedLists = __originalMethod.Name == "RegisterPlayerSpecialPawns" ? 1 : 0;
            if (replacements != expected || lists != expectedLists)
                throw new InvalidOperationException(__originalMethod.Name + ": faction lookup IL shape changed");
            return result;
        }
    }
}
