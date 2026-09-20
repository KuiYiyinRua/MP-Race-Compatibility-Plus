using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Multiplayer.Client.Factions;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace Meow.MultifactionStory
{
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        static Bootstrap()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("multifaction")) return;
            if (!MP.enabled) return;
            var h = new Harmony("meow.multifaction.story");
            try
            {
                IdeologySetup.Install(h);
                StoryHooks.Install(h);
                Log.Message("[Meow.MultifactionStory] Startup targets resolved; ideology setup and Kiiro scenario hooks installed.");
            }
            catch (Exception e) { Log.Error("[Meow.MultifactionStory] REQUIRED TARGET FAILURE: " + e); }
        }
    }

    public sealed class ValorRecord : IExposable
    {
        public Faction Faction;
        public Pawn Pawn;
        public Map StartMap;
        public int Age;
        public int ShepherdAt, WandererAt, MerchantAt;
        public bool EndingOffered;
        public void ExposeData()
        {
            Scribe_References.Look(ref Faction, "faction");
            Scribe_References.Look(ref Pawn, "valor");
            Scribe_References.Look(ref StartMap, "startMap");
            Scribe_Values.Look(ref Age, "age");
            Scribe_Values.Look(ref ShepherdAt, "shepherdAt", -1);
            Scribe_Values.Look(ref WandererAt, "wandererAt", -1);
            Scribe_Values.Look(ref MerchantAt, "merchantAt", -1);
            Scribe_Values.Look(ref EndingOffered, "endingOffered");
        }
    }

    public sealed class StoryState : GameComponent
    {
        public Dictionary<int, Ideo> Pending = new Dictionary<int, Ideo>();
        public List<ValorRecord> Valors = new List<ValorRecord>();
        private List<int> keys;
        private List<Ideo> values;
        public StoryState(Game game) { }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Pending, "meowPendingFactionIdeos", LookMode.Value, LookMode.Deep, ref keys, ref values);
            Scribe_Collections.Look(ref Valors, "meowFactionValors", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            { Pending = Pending ?? new Dictionary<int, Ideo>(); Valors = Valors ?? new List<ValorRecord>(); }
        }
    }

    internal static class StoryHooks
    {
        internal static Type Overall;
        internal static FieldInfo Valor;
        [ThreadStatic] private static Scenario generatingScenario;
        internal static void Install(Harmony h)
        {
            Overall = AccessTools.TypeByName("Kiiro_Event.KiiroEventGameComponent_OverallControl");
            if (Overall == null) return;
            Valor = AccessTools.Field(Overall, "valor_ScenarioStart") ?? throw new MissingFieldException(Overall.FullName, "valor_ScenarioStart");
            h.Patch(AccessTools.Method(typeof(FactionCreator), "GenerateNewMap"),
                new HarmonyMethod(typeof(StoryHooks), nameof(BeforeMap)),
                new HarmonyMethod(typeof(StoryHooks), nameof(AfterMap)),
                finalizer: new HarmonyMethod(typeof(StoryHooks), nameof(MapFinally)));
            h.Patch(AccessTools.Method(typeof(MapGenerator), nameof(MapGenerator.GenerateMap)), new HarmonyMethod(typeof(StoryHooks), nameof(Generator)));
            h.Patch(AccessTools.Method(Overall, "GameComponentTick"), new HarmonyMethod(typeof(StoryHooks), nameof(BeforeOverall)),
                finalizer: new HarmonyMethod(typeof(StoryHooks), nameof(AfterOverall)));
            var node = AccessTools.TypeByName("Kiiro_Event.QuestNode_GetPawn_ValorForScenario");
            foreach (var name in new[] { "TestRunInt", "RunInt" })
                h.Patch(AccessTools.Method(node, name), new HarmonyMethod(typeof(StoryHooks), nameof(BeforeNode)),
                    finalizer: new HarmonyMethod(typeof(StoryHooks), nameof(AfterNode)));
            h.Patch(AccessTools.Method(AccessTools.TypeByName("Kiiro_Event.QuestPart_LordJob_ExitMapBest"), "ExposeData"),
                postfix: new HarmonyMethod(typeof(StoryHooks), nameof(SaveValorName)));
        }
        private static void SaveValorName(object __instance)
        {
            var field = AccessTools.Field(__instance.GetType(), "valorName");
            string name = (string)field.GetValue(__instance);
            Scribe_Values.Look(ref name, "meowValorName");
            if (Scribe.mode == LoadSaveMode.LoadingVars) field.SetValue(__instance, name);
        }
        internal static object Component => Current.Game.components.FirstOrDefault(c => c.GetType() == Overall);
        private static void BeforeMap(Scenario scenario, out Scenario __state)
        { __state = generatingScenario; generatingScenario = scenario; }
        private static void MapFinally(Scenario __state) { generatingScenario = __state; }
        private static void Generator(ref MapGeneratorDef mapGenerator)
        {
            if (!MP.IsInMultiplayer || generatingScenario == null) return;
            var part = generatingScenario.AllParts.FirstOrDefault(p => p.GetType().FullName == "AncotLibrary.ScenPart_ForcedStartMap");
            if (part != null) mapGenerator = (MapGeneratorDef)AccessTools.Field(part.GetType(), "mapGenerator").GetValue(part);
        }
        private static void AfterMap(Scenario scenario, Map __result)
        {
            if (__result == null || scenario != DefDatabase<ScenarioDef>.GetNamedSilentFail("Kiiro_Scenarios_ValorStory")?.scenario) return;
            var faction = __result.ParentFaction;
            var pawn = __result.mapPawns.AllPawnsSpawned.Where(p => p.Faction == faction && p.kindDef.defName == "Kiiro_Valor").OrderBy(p => p.thingIDNumber).FirstOrDefault();
            if (pawn == null) throw new InvalidOperationException("Kiiro valor scenario generated without its starting valor.");
            Current.Game.GetComponent<StoryState>().Valors.Add(new ValorRecord
            {
                Pawn = pawn, Faction = faction, StartMap = __result,
                ShepherdAt = Rand.RangeInclusive(36000, 48000), WandererAt = Rand.RangeInclusive(180000, 360000),
                MerchantAt = Rand.RangeInclusive(900000, 1080000)
            });
            Log.Message("[Meow.MultifactionStory] Registered valor=" + pawn.thingIDNumber + " faction=" + faction.loadID + " map=" + __result.uniqueID);
        }
        // Keep the original trade-festival logic, suppress only duplicate ending scheduling.
        private static void BeforeOverall(object __instance, out Pawn __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer) return;
            __state = (Pawn)Valor.GetValue(__instance);
            if (__state != null)
            {
                var state = Current.Game.GetComponent<StoryState>();
                Pawn previous = __state;
                if (!state.Valors.Any(v => v.Pawn == previous))
                    state.Valors.Add(new ValorRecord { Pawn = __state, Faction = __state.Faction, StartMap = __state.MapHeld,
                        ShepherdAt = -1, WandererAt = -1, MerchantAt = -1 });
            }
            Valor.SetValue(__instance, null);
        }
        private static void AfterOverall(object __instance, Pawn __state)
        { if (MP.IsInMultiplayer) Valor.SetValue(__instance, __state); }
        private static void BeforeNode(out Pawn __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer) return;
            __state = (Pawn)Valor.GetValue(Component);
            var own = Current.Game.GetComponent<StoryState>().Valors.FirstOrDefault(v => v.Faction == Faction.OfPlayer);
            Valor.SetValue(Component, own?.Pawn ?? (__state?.Faction == Faction.OfPlayer ? __state : null));
        }
        private static void AfterNode(Pawn __state)
        { if (MP.IsInMultiplayer) Valor.SetValue(Component, __state); }
    }

    public sealed class ValorMapTick : MapComponent
    {
        public ValorMapTick(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            if (!MP.IsInMultiplayer || StoryHooks.Overall == null) return;
            foreach (var record in Current.Game.GetComponent<StoryState>().Valors.ToArray())
            {
                if (record.Faction == null || record.StartMap != map) continue;
                record.Age++;
                map.PushFaction(record.Faction);
                try
                {
                    Fire(ref record.ShepherdAt, record.Age, "Kiiro_LateReturningShepherd");
                    Fire(ref record.WandererAt, record.Age, "KiiroWandererJoin");
                    if (record.MerchantAt >= 0 && record.Age >= record.MerchantAt)
                    {
                        TryQuest("Kiiro_WanderingMerchant", map);
                        record.MerchantAt = -1;
                    }
                    if (!record.EndingOffered && record.Age % 30000 == 0 && record.Pawn != null && !record.Pawn.Dead && record.Pawn.Faction == record.Faction && record.Pawn.MapHeld != null)
                        record.EndingOffered = TryQuest("Kiiro_ValorEndGame", record.Pawn.MapHeld);
                }
                finally { map.PopFaction(); }
            }
        }
        private void Fire(ref int at, int age, string name)
        {
            if (at < 0 || age < at) return;
            at = -1;
            var def = DefDatabase<IncidentDef>.GetNamed(name);
            def.Worker.TryExecute(StorytellerUtility.DefaultParmsNow(def.category, map));
        }
        internal static bool TryQuest(string name, Map target)
        {
            var def = DefDatabase<QuestScriptDef>.GetNamed(name);
            var slate = new Slate();
            slate.Set("map", target);
            slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(target));
            if (!def.root.TestRun(slate)) return false;
            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(def, slate);
            QuestUtility.SendLetterQuestAvailable(quest);
            return true;
        }
    }
}
