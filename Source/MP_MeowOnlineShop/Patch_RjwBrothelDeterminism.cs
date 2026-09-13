using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Brothel Colony selects a client from map- and caravan-derived lists with
    /// RandomElement.  The candidate membership is deterministic, but its
    /// enumeration order is not guaranteed after a cross-PC save load.  Sorting
    /// the Pawn candidates by durable Thing ID before the existing Rand draw
    /// preserves one draw while making the selected client identical.
    /// </summary>
    internal static class Patch_RjwBrothelDeterminism
    {
        private const string PackageId = "calamabanana.rjw.brothelcolony";
        private const string AnchorTypeName = "BrothelColony.JobGiver_WhoreInvitingVisitors";

        private static readonly MethodInfo StablePawnRandomElementMethod =
            AccessTools.Method(typeof(Patch_RjwBrothelDeterminism), nameof(StablePawnRandomElement));
        private static int _replacementCount;
        private static Func<Room, Building_Bed, float> CalculateRoomBedFactors;
        private static bool priceQueryPatched;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));
            if (!ModsConfig.IsActive(PackageId))
                return;

            ApplyPriceQuery(harmony);

            Type anchor = AccessTools.TypeByName(AnchorTypeName);
            Assembly assembly = anchor?.Assembly;
            if (assembly == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Brothel] runtime anchor was not found; stable client selection was not applied.");
                return;
            }

            _replacementCount = 0;
            int patchedMethods = 0;
            var transpiler = new HarmonyMethod(AccessTools.Method(
                typeof(Patch_RjwBrothelDeterminism),
                nameof(StablePawnRandomElementTranspiler)));
            foreach (Type type in GetLoadableTypes(assembly))
            {
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null ||
                        !CallsPawnRandomElement(method))
                        continue;
                    harmony.Patch(method, transpiler: transpiler);
                    patchedMethods++;
                }
            }

            if (patchedMethods == 0)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Brothel] no Pawn RandomElement callers matched the installed assembly.");
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop][RJW-Brothel] stabilized Pawn RandomElement callers=" +
                patchedMethods + ".");
        }

        private static void ApplyPriceQuery(Harmony harmony)
        {
            if (priceQueryPatched) return;
            try
            {
                Type utility = AccessTools.TypeByName("BrothelColony.WhoreBed_Utility");
                MethodInfo price = AccessTools.DeclaredMethod(utility, "CalculatePriceFactor",
                    new[] { typeof(Building_Bed), typeof(float) });
                MethodInfo room = AccessTools.DeclaredMethod(utility, "CalculateBedFactorsForRoom",
                    new[] { typeof(Room), typeof(Building_Bed) });
                if (price == null || !price.IsStatic || price.ReturnType != typeof(float)
                    || room == null || !room.IsStatic || room.ReturnType != typeof(float))
                    throw new MissingMethodException("BrothelColony price/room factor API");

                CalculateRoomBedFactors = (Func<Room, Building_Bed, float>)Delegate.CreateDelegate(
                    typeof(Func<Room, Building_Bed, float>), room);
                harmony.Patch(price, prefix: new HarmonyMethod(
                    typeof(Patch_RjwBrothelDeterminism), nameof(PriceFactorPrefix)));
                priceQueryPatched = true;
                Log.Message("[MP-MeowOnlineShop][RJW-Brothel] MP bed prices use live factors without randomized cache updates.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][RJW-Brothel] REQUIRED_TARGET_FAILURE price query: " + e);
            }
        }

        // Desync-18 (drafting) and -19 (STD cleanliness query) both trigger a
        // lazy Room stats refresh on only one peer. Brothel's room postfix
        // calls CalculatePriceFactor, whose cache backoff draws Rand.Int.
        // Merely isolating that draw would still leave UI/rejoin-dependent
        // stale prices. Bypass the entire price cache during MP instead.
        // Source + installed DLL: 80EB64E342A4653CF96FE7E5A4F503CB9DE9857D13D9DA4B1C2C8FC1879B8B48.
        private static bool PriceFactorPrefix(Building_Bed __0, float __1, ref float __result)
        {
            if (!MP.IsInMultiplayer) return true;
            Building_Bed bed = __0;
            float roomMultiplier = __1;
            if (roomMultiplier < 0f)
            {
                Room room = bed.Map != null && bed.Map.regionAndRoomUpdater.Enabled
                    ? bed.GetRoom() : null;
                // Keep the original room eligibility, bed count, room role and
                // impressiveness rules. Its sibling-bed calls pass an explicit
                // multiplier, so they cannot recurse through this branch.
                roomMultiplier = CalculateRoomBedFactors(room, bed);
            }
            __result = roomMultiplier * bed.GetStatValue(RimWorld.StatDefOf.Comfort);
            // No writes to bedScore/roomScore/lastScoreUpdateTick/backoff.
            // Owner permissions, reservations and payment logic remain intact.
            return false;
        }

        private static IEnumerable<CodeInstruction> StablePawnRandomElementTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo called = instruction.operand as MethodInfo;
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    IsPawnRandomElement(called))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = StablePawnRandomElementMethod;
                    _replacementCount++;
                }
                yield return instruction;
            }
        }

        private static Pawn StablePawnRandomElement(IEnumerable<Pawn> pawns)
        {
            if (pawns == null)
                return null;
            // ToList deliberately preserves the original source's empty-list
            // behavior through RandomElement after the deterministic ordering.
            return pawns.OrderBy(pawn => pawn?.thingIDNumber ?? int.MinValue).RandomElement();
        }

        private static bool CallsPawnRandomElement(MethodInfo method)
        {
            byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
            if (bytes == null)
                return false;
            for (int i = 0; i + 4 < bytes.Length; i++)
            {
                if (bytes[i] != OpCodes.Call.Value && bytes[i] != OpCodes.Callvirt.Value)
                    continue;
                try
                {
                    MethodInfo called = method.Module.ResolveMethod(
                        BitConverter.ToInt32(bytes, i + 1), GetTypeArguments(method),
                        method.IsGenericMethod ? method.GetGenericArguments() : null) as MethodInfo;
                    if (IsPawnRandomElement(called))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static bool IsPawnRandomElement(MethodInfo method)
        {
            return method != null && method.Name == "RandomElement" &&
                   method.ReturnType == typeof(Pawn) && method.GetParameters().Length == 1;
        }

        private static Type[] GetTypeArguments(MethodBase method)
        {
            return method.DeclaringType != null && method.DeclaringType.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : null;
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { return exception.Types.Where(type => type != null); }
        }
    }
}
