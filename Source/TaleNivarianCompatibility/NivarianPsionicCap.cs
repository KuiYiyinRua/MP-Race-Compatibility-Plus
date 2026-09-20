using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Retain the actual resolved host cap, not a guessed default derived from the setting label.
    public sealed class NivarianPsionicCap : GameComponent
    {
        const string DefName = "Nivarian_Lumin_PsionicAttunement";
        static HediffDef targetDef;
        static readonly FieldInfo CapField = AccessTools.Field(typeof(HediffDef), nameof(HediffDef.maxSeverity));
        static readonly Dictionary<MethodBase, int> Expected = new Dictionary<MethodBase, int>();
        float cap;
        public NivarianPsionicCap(Game game)
        {
            Resolve();
            if (targetDef != null) cap = targetDef.maxSeverity;
        }
        static void Resolve() { if (targetDef == null) targetDef = DefDatabase<HediffDef>.GetNamedSilentFail(DefName); }
        public override void ExposeData()
        {
            if (targetDef != null) Scribe_Values.Look(ref cap, "meowResolvedPsionicCap", cap, true);
        }
        sealed class Site
        {
            internal readonly string Type, Method;
            internal readonly int Count;
            internal readonly Type[] Parameters;
            internal Site(string type, string method, int count, params Type[] parameters) { Type = type; Method = method; Count = count; Parameters = parameters; }
        }
        internal static void Apply(Harmony harmony)
        {
            Resolve();
            if (targetDef == null) return; // No Royalty content in this loadout.
            foreach (var site in new[] {
                new Site("Verse.HediffDef+<ConfigErrors>d__105", "MoveNext", 2),
                new Site("Verse.PollutionUtility", "Notify_TunnelHiveSpawnedInsect", 1, typeof(Verse.Pawn)),
                new Site("Verse.Hediff", "set_Severity", 1, typeof(System.Single)),
                new Site("Verse.Hediff", "DebugString", 1),
                new Site("Verse.HediffComp_GrowthMode", "CompDebugString", 1),
                new Site("Verse.Hediff_VatLearning", "PostTickInterval", 1, typeof(System.Int32)),
                new Site("Verse.Hediff_Level", "ChangeLevel", 1, typeof(System.Int32)),
                new Site("Verse.Hediff_Psylink", "ChangeLevel", 1, typeof(System.Int32), typeof(System.Boolean)),
                new Site("Verse.DebugToolsPawns", "GivePsylink", 1),
                new Site("Verse.Dialog_DebugSetSeverity", ".ctor", 2, typeof(Verse.Hediff)),
                new Site("RimWorld.IncidentWorker_Disease", "TryExecuteWorker", 1, typeof(RimWorld.IncidentParms)),
                new Site("RimWorld.Recipe_ChangeImplantLevel", "Operable", 1, typeof(Verse.Hediff), typeof(Verse.RecipeDef)),
                new Site("RimWorld.PawnUtility", "GetMaxPsylinkLevel", 1, typeof(Verse.Pawn)),
                new Site("RimWorld.Building_GrowthVat", "get_BiostarvationSeverityPercent", 1),
                new Site("RimWorld.Building_GrowthVat", "Tick", 1),
                new Site("RimWorld.Building_GrowthVat", "EmbryoBirth", 1),
                new Site("RimWorld.CompRoyalImplant+<SpecialDisplayStats>d__2", "MoveNext", 1),
                new Site("RimWorld.CompUsableImplant", "FloatMenuOptionLabel", 1, typeof(Verse.Pawn)),
                new Site("RimWorld.CompUseEffect_InstallImplant", "CanBeUsedBy", 1, typeof(Verse.Pawn)),
                new Site("RimWorld.HealthCardUtility", "DrawHediffRow", 1, typeof(UnityEngine.Rect), typeof(Verse.Pawn), typeof(System.Collections.Generic.IEnumerable<Verse.Hediff>), typeof(System.Single).MakeByRefType(), typeof(System.Single)),
                new Site("RimWorld.HediffComp_DestroyOrgan", "CompPostTickInterval", 1, typeof(System.Single).MakeByRefType(), typeof(System.Int32)),
                new Site("Nivarian_Race.Code.IngestionOutcomeDoers.IngestionOutcomeDoer_OffsetHediff", "DoIngestionOutcomeSpecial", 1, typeof(Verse.Pawn), typeof(Verse.Thing), typeof(System.Int32)),
                new Site("Nivarian_Race.Code.Comps.ThingComps.Comp_HarmOnSpot", "CompTick", 2),
                new Site("Nivarian.Hediff_PsionicAttunement", "UpdateAbilitiesAndPsylink", 1),
                new Site("Nivarian.CompAbilityEffectAbilityExpGain", "Apply", 1, typeof(Verse.LocalTargetInfo), typeof(Verse.LocalTargetInfo)),
            })
            {
                var type = AccessTools.TypeByName(site.Type) ?? throw new TypeLoadException(site.Type);
                MethodBase method = site.Method == ".ctor"
                    ? (MethodBase)AccessTools.DeclaredConstructor(type, site.Parameters)
                    : AccessTools.DeclaredMethod(type, site.Method, site.Parameters);
                if (method == null) throw new MissingMethodException(site.Type, site.Method);
                Expected.Add(method, site.Count);
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(NivarianPsionicCap), nameof(ReadSharedCap)));
            }
            Log.Message("[TaleNivarianCompat] Psionic cap readers use the saved host value only for Nivarian attunement; other Defs unchanged.");
        }
        static float Read(HediffDef def)
        {
            // This method also covers generic game readers. Avoid even consulting MP for other Defs.
            if (ReferenceEquals(def, targetDef) && targetDef != null && MP.IsInMultiplayer && Current.Game != null)
            {
                var state = Current.Game.GetComponent<NivarianPsionicCap>();
                if (state != null) return state.cap;
            }
            return def.maxSeverity;
        }
        static IEnumerable<CodeInstruction> ReadSharedCap(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, CapField))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianPsionicCap), nameof(Read));
                    count++;
                }
                yield return instruction;
            }
            if (count != Expected[__originalMethod]) throw new InvalidOperationException(__originalMethod + " cap reads changed: " + count);
        }
    }
}
