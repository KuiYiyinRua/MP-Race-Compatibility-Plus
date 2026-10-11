using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;
using RimWorld;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianIndustryActions
    {
        const string Ns = "Nivarian_Race.Code.Comps.BuildingComps.";
        static Type dust, container;
        static FieldInfo selected, radius;
        static MethodInfo setRadius;
        static ISyncMethod selectChunk, radiusCommand;
        static PropertyInfo rodProps;
        static FieldInfo attackRadius;
        static FieldInfo rodPower;
        sealed class RadiusRequest { public float Value = float.NaN; }
        static readonly ConditionalWeakTable<object, RadiusRequest> requests = new ConditionalWeakTable<object, RadiusRequest>();
        internal static void Apply(Harmony harmony)
        {
            dust = NivarianUpdateActions.RequiredType(Ns + "CompDustCatcher");
            selected = AccessTools.Field(dust, "_selectedChunkDef") ?? throw new MissingFieldException(dust.FullName, "_selectedChunkDef");
            selectChunk = MP.RegisterSyncMethod(typeof(NivarianIndustryActions), nameof(SelectChunk));
            harmony.Patch(NivarianUpdateActions.RequiredMethod(dust, "OpenProductMenu"),
                postfix: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(ProductMenu)));
            // The product list also feeds RandomElement in simulation; localized labels must not decide RNG outcomes.
            harmony.Patch(NivarianUpdateActions.RequiredMethod(dust, "GetStoneChunkDefs"),
                postfix: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(OrderChunks)));

            container = NivarianUpdateActions.RequiredType(Ns + "CompUniversalContainer");
            radius = AccessTools.Field(container, "searchRadius") ?? throw new MissingFieldException(container.FullName, "searchRadius");
            setRadius = NivarianUpdateActions.RequiredMethod(container, "set_SearchRadius", typeof(float));
            radiusCommand = MP.RegisterSyncMethod(typeof(NivarianIndustryActions), nameof(SetRadius));
            harmony.Patch(setRadius, prefix: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(Radius)));

            var rod = NivarianUpdateActions.RequiredType(Ns + "Comp_NivarianLightningRod");
            rodPower = AccessTools.Field(rod, "_powerTrader") ?? throw new MissingFieldException(rod.FullName, "_powerTrader");
            harmony.Patch(NivarianUpdateActions.RequiredMethod(rod, "PostExposeData"), postfix: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(RodLoaded)));
            harmony.Patch(NivarianUpdateActions.RequiredMethod(rod, "CompTick"), prefix: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(RodTick)));
            harmony.Patch(NivarianUpdateActions.RequiredMethod(rod, "SetPowerOutput", typeof(float)), prefix: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(RodPowerOutput)));
            rodProps = AccessTools.Property(rod, "Props") ?? throw new MissingMemberException(rod.FullName, "Props");
            attackRadius = AccessTools.Field(rodProps.PropertyType, "attackRadius") ?? throw new MissingFieldException(rodProps.PropertyType.FullName, "attackRadius");
            harmony.Patch(NivarianUpdateActions.RequiredMethod(rod, "FindNearestHostile"), postfix: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(NearestTie)));
            harmony.Patch(NivarianUpdateActions.RequiredMethod(rod, "FindChainTarget", typeof(Thing), typeof(HashSet<Thing>)), transpiler: new HarmonyMethod(typeof(NivarianIndustryActions), nameof(ChainOrder)));
            // Each complete native callback includes secondary power/cooldown writes.
            var flags = new[] { "switchOn", "reverseMode", "artificialOn", "nextArtificialStrikeTick" };
            int callbacks = 0;
            foreach (var method in rod.GetMethods(AccessTools.allDeclared).Where(m => m.Name.StartsWith("<CompGetGizmosExtra>") && m.GetParameters().Length == 0 && m.ReturnType == typeof(void)))
            {
                var writes = PatchProcessor.GetOriginalInstructions(method).Where(i => i.opcode == System.Reflection.Emit.OpCodes.Stfld)
                    .Select(i => i.operand as FieldInfo).Where(f => f != null && f.DeclaringType == rod).Select(f => f.Name).ToArray();
                if (!writes.Any(flags.Contains)) continue;
                var sync = MP.RegisterSyncMethod(method);
                if (writes.All(f => f == "nextArtificialStrikeTick")) sync.SetDebugOnly();
                callbacks++;
            }
            if (callbacks != 4) throw new InvalidOperationException("Lightning rod gameplay/dev callbacks=" + callbacks);
            Log.Message("[TaleNivarianCompat] industrial product, ingredient radius with hauling notification, and four complete lightning-rod callbacks resolved.");
        }
        static bool BindRodPower(ThingComp rod)
        {
            var power = rod?.parent?.AllComps.OfType<CompPowerTrader>().FirstOrDefault();
            if (!ReferenceEquals(rodPower.GetValue(rod), power)) rodPower.SetValue(rod, power);
            return power != null;
        }
        static void RodLoaded(ThingComp __instance)
        {
            if (MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.PostLoadInit) BindRodPower(__instance);
        }
        static void RodTick(ThingComp __instance)
        {
            if (MP.IsInMultiplayer && rodPower.GetValue(__instance) == null) BindRodPower(__instance);
        }
        static bool RodPowerOutput(ThingComp __instance) => !MP.IsInMultiplayer || BindRodPower(__instance);
        static void OrderChunks(ref List<ThingDef> __result)
        {
            if (MP.IsInMultiplayer) __result.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
        }
        static void NearestTie(ThingComp __instance, ref Pawn __result)
        {
            if (!MP.IsInMultiplayer || __result == null) return;
            int distance = __result.Position.DistanceToSquared(__instance.parent.Position);
            float range = (float)attackRadius.GetValue(rodProps.GetValue(__instance));
            foreach (var thing in GenRadial.RadialDistinctThingsAround(__instance.parent.Position, __instance.parent.Map, range, false))
                if (thing is Pawn pawn && !pawn.DeadOrDowned && GenHostility.HostileTo(pawn, __instance.parent)
                    && pawn.Position.DistanceToSquared(__instance.parent.Position) == distance && pawn.thingIDNumber < __result.thingIDNumber)
                    __result = pawn;
        }
        static IEnumerable<CodeInstruction> ChainOrder(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(GenCollection)
                    && method.Name == "RandomElement" && method.IsGenericMethod && method.GetGenericArguments()[0] == typeof(Pawn)
                    && method.GetParameters().Length == 1)
                { instruction.operand = AccessTools.Method(typeof(NivarianIndustryActions), nameof(RandomChain)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Lightning chain RNG sites=" + count);
        }
        static Pawn RandomChain(IEnumerable<Pawn> pawns) => MP.IsInMultiplayer ? pawns.OrderBy(p => p.thingIDNumber).RandomElement() : pawns.RandomElement();
        static void ProductMenu(ThingComp __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return;
            var menu = Find.WindowStack.Windows.OfType<FloatMenu>().LastOrDefault();
            if (menu == null) throw new InvalidOperationException("Native dust catcher product menu missing");
            // Capture each native outcome under a complete one-field rollback, then replay the chosen Def.
            foreach (var option in (List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu), "options").GetValue(menu))
            {
                var action = option.action;
                option.action = () =>
                {
                    var before = selected.GetValue(__instance);
                    ThingDef value;
                    try { action(); value = selected.GetValue(__instance) as ThingDef; }
                    finally { selected.SetValue(__instance, before); }
                    selectChunk.DoSync(null, __instance, value);
                };
            }
        }
        static void SelectChunk(ThingComp comp, ThingDef def)
        {
            if (comp?.parent == null || comp.parent.Destroyed || !dust.IsInstanceOfType(comp)) return;
            if (def == null || RimWorld.ThingCategoryDefOf.StoneChunks.childThingDefs.Contains(def)) selected.SetValue(comp, def);
        }
        static bool Radius(ThingComp __instance, float __0)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            if (float.IsNaN(__0) || float.IsInfinity(__0)) return false;
            float value = __0 >= 100 ? 999 : Math.Max(3, __0);
            var request = requests.GetOrCreateValue(__instance);
            if ((float)radius.GetValue(__instance) == value) { request.Value = float.NaN; return false; }
            if (request.Value != value && radiusCommand.DoSync(null, __instance, value)) request.Value = value;
            return false;
        }
        static void SetRadius(ThingComp comp, float value)
        {
            if (comp?.parent == null || comp.parent.Destroyed || !container.IsInstanceOfType(comp)
                || float.IsNaN(value) || float.IsInfinity(value)) return;
            setRadius.Invoke(comp, new object[] { value });
        }
    }
}

