using System;
using System.Collections;
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
    /// Synchronizes Rigor Mortis UI actions which mutate saved game state directly,
    /// repairs its post-load NPC cache, and canonicalizes the one random selection
    /// which consumes a pawn-keyed dictionary in quest generation.
    /// </summary>
    internal static class Patch_RigorMortisStateActions
    {
        private const string LogTag = "[MP-MeowOnlineShop] RigorMortisStateActions";

        private static readonly Type BuildingCallBoardType = AccessTools.TypeByName("RigorMortis.Building_CallBoard");
        private static readonly Type RmComponentType = AccessTools.TypeByName("RigorMortis.RMComponent");
        private static readonly Type CompTaoistType = AccessTools.TypeByName("RigorMortis.CompTaoist");
        private static readonly Type TaoistArtifactGizmoType = AccessTools.TypeByName("RigorMortis.TaoistArtifactGizmo");
        private static readonly Type ApparelArtifactType = AccessTools.TypeByName("RigorMortis.ApparelArtifact");
        private static readonly Type WeaponArtifactType = AccessTools.TypeByName("RigorMortis.WeaponArtifact");
        private static readonly Type ApprenticeQuestNodeType = AccessTools.TypeByName("RigorMortis.QuestNode_Root_Apprentice");
        private static readonly Type RmSettingsType = AccessTools.TypeByName("RigorMortis.RMSettings");
        private static readonly Type RedStringType = AccessTools.TypeByName("RigorMortis.HediffRedString");
        private static readonly Type RmDebugToolsType = AccessTools.TypeByName("RigorMortis.DebugTools");
        private static readonly Type YinCompType = AccessTools.TypeByName("RigorMortis.CompYinAndMalevolent");
        private static readonly Type TaoistCastingType = AccessTools.TypeByName("RigorMortis.HediffAbility_TaoistCasting");
        private static readonly Type ApprenticeTalePatchType = AccessTools.TypeByName("RigorMortis.Apprentice_Tale_Patch");
        private static readonly Type ApprenticeMarriagePatchType = AccessTools.TypeByName("RigorMortis.Apprentice_Merry_Patch");

        private static readonly FieldInfo BoardActiveField = AccessTools.Field(BuildingCallBoardType, "active");
        private static readonly FieldInfo BoardMainField = AccessTools.Field(BuildingCallBoardType, "main");
        private static readonly FieldInfo GizmoCarrierField = AccessTools.Field(TaoistArtifactGizmoType, "carrier");
        private static readonly FieldInfo SelectedArtifactField = AccessTools.Field(CompTaoistType, "selectedArtifact");
        private static readonly FieldInfo ArtifactRollField = AccessTools.Field(CompTaoistType, "roll");
        private static readonly FieldInfo NpcDictionaryField = AccessTools.Field(RmComponentType, "NPCDict");
        private static readonly FieldInfo NameDictionaryField = AccessTools.Field(RmComponentType, "nameDict");
        private static readonly FieldInfo LanternDictionaryField = AccessTools.Field(RmComponentType, "lanternDict");
        private static readonly FieldInfo ApprenticeField = AccessTools.Field(RmComponentType, "apprentice");
        private static readonly FieldInfo MasterField = AccessTools.Field(RmComponentType, "master");
        private static readonly Type RmUtilityType = AccessTools.TypeByName("RigorMortis.RMUtility");
        private static readonly PropertyInfo RmComponentProperty = AccessTools.Property(RmUtilityType, "TheComponent");
        private static readonly FieldInfo CachedNpcsField = AccessTools.Field(RmUtilityType, "cachedNPCs");
        private static readonly FieldInfo TaoistCastingCompassField = AccessTools.Field(TaoistCastingType, "compass");
        private static readonly PropertyInfo YinContainedThingProperty = AccessTools.Property(YinCompType, "ContainedThing");
        private static readonly PropertyInfo YinLevelProperty = AccessTools.Property(YinCompType, "Level");

        private static readonly MethodInfo BoardGetGizmosMethod = AccessTools.Method(BuildingCallBoardType, "GetGizmos");
        private static readonly MethodInfo ComponentExposeDataMethod = AccessTools.Method(RmComponentType, "ExposeData");
        private static readonly MethodInfo ComponentWorldUpdateMethod = AccessTools.Method(RmComponentType, "WorldComponentUpdate");
        private static readonly MethodInfo ComponentRecordKillMethod = AccessTools.Method(RmComponentType, "RecordOneKill");
        private static readonly MethodInfo ArtifactGizmoOnGuiMethod = AccessTools.Method(TaoistArtifactGizmoType, "GizmoOnGUI");
        private static readonly MethodInfo ApparelArtifactGizmosMethod = AccessTools.Method(ApparelArtifactType, "GetArtifactsGizmos");
        private static readonly MethodInfo WeaponArtifactGizmosMethod = AccessTools.Method(WeaponArtifactType, "GetArtifactsGizmos");
        private static readonly MethodInfo ApprenticeTestRunMethod = AccessTools.Method(ApprenticeQuestNodeType, "TestRunInt");
        private static readonly MethodInfo ApprenticeRunMethod = AccessTools.Method(ApprenticeQuestNodeType, "RunInt");
        private static readonly MethodInfo SettingsDrawMethod = AccessTools.Method(RmSettingsType, "DoWindowContents");
        private static readonly MethodInfo RedStringPostDrawMethod = AccessTools.Method(RedStringType, "PostDraw");
        private static readonly MethodInfo DebugSpawnAreaMethod = AccessTools.Method(RmDebugToolsType, "SpawnAreaChance");
        private static readonly MethodInfo YinCompGetGizmosMethod = AccessTools.Method(YinCompType, "CompGetGizmosExtra");
        private static readonly MethodInfo TaoistCastingExposeDataMethod = AccessTools.Method(TaoistCastingType, "ExposeData");
        private static readonly MethodInfo ApprenticeTalePostfixMethod = AccessTools.Method(ApprenticeTalePatchType, "Postfix");
        private static readonly MethodInfo ApprenticeMarriagePostfixMethod = AccessTools.Method(ApprenticeMarriagePatchType, "Postfix");
        private static readonly MethodInfo IsZombieMethod = AccessTools.Method(
            RmUtilityType, "IsZombie", new[] { typeof(Pawn), typeof(int).MakeByRefType() });
        private static readonly MethodInfo AnyZombieThreatMethod = AccessTools.Method(
            RmUtilityType, "AnyZombieThreat", new[] { typeof(Map), typeof(int) });
        private static readonly MethodInfo AnyZombieThreatWithLevelMethod = AccessTools.Method(
            RmUtilityType, "AnyZombieThreat", new[] { typeof(Map), typeof(int).MakeByRefType() });

        private static readonly string[] IntegerSettingNames =
        {
            "QuestSentInterval", "UnrottenBoneNum", "DisableInvulnerabilityFactor",
            "ZombieDamageFactor", "ZombieHurtFactor", "ZombieMalevolentReduceFactor",
            "ArtifactsCostFactor"
        };
        private static readonly string[] BooleanSettingNames =
        {
            "DisableInvulnerability", "DisallowZombieAppear", "DisallowDrawIncantation",
            "DisallowDrawIncantationPatch", "SkipEnding", "DisallowFoodWhenDanger",
            "DisableWorshipMoon", "DisableBegForFood", "DisablePaintedSkin"
        };
        private static readonly FieldInfo[] IntegerSettingFields = IntegerSettingNames
            .Select(name => AccessTools.Field(RmSettingsType, name)).ToArray();
        private static readonly FieldInfo[] BooleanSettingFields = BooleanSettingNames
            .Select(name => AccessTools.Field(RmSettingsType, name)).ToArray();

        private static readonly Type CrimsonEnergyCompType = AccessTools.TypeByName("Axolotl.FactionExpand.CompAxolotlOminousEnergy");
        private static readonly MethodInfo CrimsonCompGetGizmosMethod = AccessTools.Method(CrimsonEnergyCompType, "CompGetGizmosExtra");

        private static ISyncMethod SyncBoardToggleMethod;
        private static ISyncMethod SyncArtifactSelectionMethod;
        private static ISyncMethod SyncEquipArtifactMethod;
        private static ISyncMethod SyncGougeHeartMethod;
        private static ISyncMethod SyncSettingsMethod;
        private static ISyncMethod SyncBoardDebugMethod;
        private static ISyncMethod SyncDebugSpawnAreaMethod;
        private static ISyncMethod SyncYinCommandMethod;
        private static ISyncMethod SyncYinDebugCommandMethod;

        private static readonly HashSet<string> YinDebugLabels = new HashSet<string>
        {
            "DEV: Force berserk", "DEV: +100 Malevolent", "DEV: -100 Malevolent",
            "DEV: Stun for 10 sec", "DEV: Show mutant progress", "DEV: Force mutant",
            "DEV: Mutant Progress + 10%", "DEV: Start begging for food",
            "DEV: Force painted skin"
        };

        private sealed class ThreatCache
        {
            internal Map Map;
            internal int Tick = int.MinValue;
            internal bool Hostile;
            internal int Level;
        }

        private static readonly Dictionary<string, ThreatCache> ThreatCaches =
            new Dictionary<string, ThreatCache>();

        public struct ArtifactUiState
        {
            internal bool Active;
            internal ThingComp Carrier;
            internal Thing Selected;
            internal int Roll;
        }

        public struct SettingsUiState
        {
            internal bool Active;
            internal int[] Integers;
            internal bool[] Booleans;
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || BuildingCallBoardType == null)
                return;

            var required = new Dictionary<string, MemberInfo>
            {
                { "Building_CallBoard.GetGizmos", BoardGetGizmosMethod },
                { "Building_CallBoard.active", BoardActiveField },
                { "Building_CallBoard.main", BoardMainField },
                { "RMComponent.ExposeData", ComponentExposeDataMethod },
                { "RMComponent.NPCDict", NpcDictionaryField },
                { "RMComponent.nameDict", NameDictionaryField },
                { "RMComponent.lanternDict", LanternDictionaryField },
                { "RMComponent.WorldComponentUpdate", ComponentWorldUpdateMethod },
                { "RMComponent.RecordOneKill", ComponentRecordKillMethod },
                { "RMUtility.cachedNPCs", CachedNpcsField },
                { "RMUtility.TheComponent", RmComponentProperty },
                { "TaoistArtifactGizmo.GizmoOnGUI", ArtifactGizmoOnGuiMethod },
                { "TaoistArtifactGizmo.carrier", GizmoCarrierField },
                { "CompTaoist.selectedArtifact", SelectedArtifactField },
                { "CompTaoist.roll", ArtifactRollField },
                { "ApparelArtifact.GetArtifactsGizmos", ApparelArtifactGizmosMethod },
                { "WeaponArtifact.GetArtifactsGizmos", WeaponArtifactGizmosMethod },
                { "QuestNode_Root_Apprentice.TestRunInt", ApprenticeTestRunMethod },
                { "QuestNode_Root_Apprentice.RunInt", ApprenticeRunMethod },
                { "RMSettings.DoWindowContents", SettingsDrawMethod }
                ,{ "HediffRedString.PostDraw", RedStringPostDrawMethod }
                ,{ "DebugTools.SpawnAreaChance", DebugSpawnAreaMethod }
                ,{ "CompYinAndMalevolent.CompGetGizmosExtra", YinCompGetGizmosMethod }
                ,{ "HediffAbility_TaoistCasting.ExposeData", TaoistCastingExposeDataMethod }
                ,{ "HediffAbility_TaoistCasting.compass", TaoistCastingCompassField }
                ,{ "Apprentice_Tale_Patch.Postfix", ApprenticeTalePostfixMethod }
                ,{ "Apprentice_Merry_Patch.Postfix", ApprenticeMarriagePostfixMethod }
                ,{ "RMComponent.apprentice", ApprenticeField }
                ,{ "RMComponent.master", MasterField }
                ,{ "RMUtility.IsZombie", IsZombieMethod }
                ,{ "RMUtility.AnyZombieThreat(Map,int)", AnyZombieThreatMethod }
                ,{ "RMUtility.AnyZombieThreat(Map,out int)", AnyZombieThreatWithLevelMethod }
                ,{ "CompYinAndMalevolent.ContainedThing", YinContainedThingProperty }
                ,{ "CompYinAndMalevolent.Level", YinLevelProperty }
            };
            for (int i = 0; i < IntegerSettingFields.Length; i++)
                required.Add("RMSettings." + IntegerSettingNames[i], IntegerSettingFields[i]);
            for (int i = 0; i < BooleanSettingFields.Length; i++)
                required.Add("RMSettings." + BooleanSettingNames[i], BooleanSettingFields[i]);
            var missing = required.Where(pair => pair.Value == null).Select(pair => pair.Key).ToList();
            if (missing.Count != 0)
            {
                Log.Error($"{LogTag}: disabled because required 1.6 symbols are missing: {string.Join(", ", missing)}.");
                return;
            }

            try
            {
                SyncBoardToggleMethod = RegisterSync(nameof(SyncBoardToggle));
                SyncArtifactSelectionMethod = RegisterSync(nameof(SyncArtifactSelection));
                SyncEquipArtifactMethod = RegisterSync(nameof(SyncEquipArtifact));
                SyncSettingsMethod = RegisterSync(nameof(SyncSettings));
                SyncBoardDebugMethod = RegisterSync(nameof(SyncBoardDebug));
                SyncBoardDebugMethod.SetDebugOnly();
                SyncDebugSpawnAreaMethod = RegisterSync(nameof(SyncDebugSpawnArea));
                SyncDebugSpawnAreaMethod.SetDebugOnly();
                SyncYinCommandMethod = RegisterSync(nameof(SyncYinCommand));
                SyncYinDebugCommandMethod = RegisterSync(nameof(SyncYinDebugCommand));
                SyncYinDebugCommandMethod.SetDebugOnly();

                harmony.Patch(BoardGetGizmosMethod,
                    postfix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(BoardGetGizmosPostfix))
                    { priority = Priority.Last });
                harmony.Patch(ComponentExposeDataMethod,
                    postfix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(ComponentExposeDataPostfix))
                    { priority = Priority.Last });
                harmony.Patch(ComponentWorldUpdateMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(ComponentWorldUpdatePrefix))
                    { priority = Priority.First });
                harmony.Patch(ComponentRecordKillMethod,
                    transpiler: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(SortDamagePawnKeysTranspiler)));
                harmony.Patch(ArtifactGizmoOnGuiMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(ArtifactGizmoPrefix))
                    { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(ArtifactGizmoPostfix))
                    { priority = Priority.Last });
                var equipPostfix = new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(ArtifactCommandsPostfix))
                { priority = Priority.Last };
                harmony.Patch(ApparelArtifactGizmosMethod, postfix: equipPostfix);
                harmony.Patch(WeaponArtifactGizmosMethod, postfix: equipPostfix);

                var dictionaryTranspiler = new HarmonyMethod(
                    typeof(Patch_RigorMortisStateActions), nameof(SortNamedPawnKeysTranspiler));
                harmony.Patch(ApprenticeTestRunMethod, transpiler: dictionaryTranspiler);
                harmony.Patch(ApprenticeRunMethod, transpiler: dictionaryTranspiler);
                harmony.Patch(SettingsDrawMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(SettingsDrawPrefix))
                    { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(SettingsDrawPostfix))
                    { priority = Priority.Last });
                harmony.Patch(RedStringPostDrawMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(RenderRandPrefix))
                    { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(RenderRandFinalizer))
                    { priority = Priority.Last });
                harmony.Patch(DebugSpawnAreaMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(DebugSpawnAreaPrefix))
                    { priority = Priority.First });
                harmony.Patch(YinCompGetGizmosMethod,
                    postfix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(YinCompGizmosPostfix))
                    { priority = Priority.Last });
                harmony.Patch(TaoistCastingExposeDataMethod,
                    postfix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(TaoistCastingExposeDataPostfix))
                    { priority = Priority.Last });
                harmony.Patch(ApprenticeTalePostfixMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(ApprenticeTalePostfixPrefix))
                    { priority = Priority.First });
                harmony.Patch(ApprenticeMarriagePostfixMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(ApprenticeMarriagePostfixPrefix))
                    { priority = Priority.First });
                harmony.Patch(AnyZombieThreatMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(AnyZombieThreatPrefix))
                    { priority = Priority.First });
                harmony.Patch(AnyZombieThreatWithLevelMethod,
                    prefix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(AnyZombieThreatWithLevelPrefix))
                    { priority = Priority.First });
                int debugMethods = RegisterDebugMethods();

                ApplyOptionalCrimson(harmony);
                Log.Message($"{LogTag}: ready; requiredSymbols={required.Count}, uiSync=board/artifact/equip, " +
                            $"settingsSync={IntegerSettingFields.Length + BooleanSettingFields.Length}, " +
                            $"postLoadNpcCache=true, dictionaryOrder=world/apprentice/damage, renderRandIsolation=1, " +
                            $"yinGizmoSync=true, taoistCompassScribed=true, apprenticeEventGuard=2, mapThreatCache=true, " +
                            $"debugMethods={debugMethods + 3}, crimson={CrimsonCompGetGizmosMethod != null}.");
            }
            catch (Exception e)
            {
                Log.Error($"{LogTag}: setup failed; suite is not considered ready: {e}");
            }
        }

        private static ISyncMethod RegisterSync(string methodName)
        {
            MethodInfo method = AccessTools.Method(typeof(Patch_RigorMortisStateActions), methodName);
            if (method == null)
                throw new MissingMethodException(typeof(Patch_RigorMortisStateActions).FullName, methodName);
            return MP.RegisterSyncMethod(method, null);
        }

        private static void ApplyOptionalCrimson(Harmony harmony)
        {
            if (CrimsonCompGetGizmosMethod == null)
                return;

            SyncGougeHeartMethod = RegisterSync(nameof(SyncGougeHeart));
            harmony.Patch(CrimsonCompGetGizmosMethod,
                postfix: new HarmonyMethod(typeof(Patch_RigorMortisStateActions), nameof(CrimsonGizmosPostfix))
                { priority = Priority.Last });
        }

        public static void BoardGetGizmosPostfix(object __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!ShouldCaptureInterfaceAction() || __instance == null || __result == null)
                return;
            __result = WrapBoardGizmos(__instance as Thing, __result);
        }

        private static IEnumerable<Gizmo> WrapBoardGizmos(Thing board, IEnumerable<Gizmo> source)
        {
            string activeLabel = "RiM.BoardActive".Translate().ToString();
            string mainLabel = "RiM.BoardMainActive".Translate().ToString();
            foreach (Gizmo gizmo in source)
            {
                if (board != null && gizmo is Command_Toggle toggle)
                {
                    bool isMain;
                    string label = toggle.defaultLabel;
                    if (label == activeLabel)
                        isMain = false;
                    else if (label == mainLabel)
                        isMain = true;
                    else
                    {
                        yield return gizmo;
                        continue;
                    }

                    Action original = toggle.toggleAction;
                    toggle.toggleAction = delegate
                    {
                        if (!ShouldCaptureInterfaceAction() || SyncBoardToggleMethod == null)
                        {
                            original?.Invoke();
                            return;
                        }
                        FieldInfo field = isMain ? BoardMainField : BoardActiveField;
                        bool next = !(bool)field.GetValue(board);
                        SyncBoardToggleMethod.DoSync(null, board, isMain, next);
                    };
                }
                else if (board != null && gizmo is Command_Action action &&
                         (action.defaultLabel == "DEV: Force trigger apprentice" ||
                          action.defaultLabel == "DEV: Generate a daily quest"))
                {
                    bool dailyQuest = action.defaultLabel == "DEV: Generate a daily quest";
                    Action original = action.action;
                    action.action = delegate
                    {
                        if (!ShouldCaptureInterfaceAction() || SyncBoardDebugMethod == null)
                        {
                            original?.Invoke();
                            return;
                        }
                        SyncBoardDebugMethod.DoSync(null, board, dailyQuest);
                    };
                }
                yield return gizmo;
            }
        }

        public static void SyncBoardToggle(Thing board, bool isMain, bool value)
        {
            if (board == null || !BuildingCallBoardType.IsInstanceOfType(board))
                return;
            (isMain ? BoardMainField : BoardActiveField).SetValue(board, value);
        }

        public static void SyncBoardDebug(Thing board, bool dailyQuest)
        {
            if (board == null || !BuildingCallBoardType.IsInstanceOfType(board))
                return;
            var gizmos = BoardGetGizmosMethod.Invoke(board, null) as IEnumerable<Gizmo>;
            string label = dailyQuest ? "DEV: Generate a daily quest" : "DEV: Force trigger apprentice";
            gizmos?.OfType<Command_Action>().FirstOrDefault(command => command.defaultLabel == label)?.action?.Invoke();
        }

        public static void ArtifactGizmoPrefix(object __instance, out ArtifactUiState __state)
        {
            __state = default(ArtifactUiState);
            if (!ShouldCaptureInterfaceAction() || __instance == null)
                return;

            var carrier = GizmoCarrierField.GetValue(__instance) as ThingComp;
            if (carrier?.parent == null)
                return;
            __state.Active = true;
            __state.Carrier = carrier;
            __state.Selected = SelectedArtifactField.GetValue(carrier) as Thing;
            __state.Roll = (int)ArtifactRollField.GetValue(carrier);
        }

        public static void ArtifactGizmoPostfix(ArtifactUiState __state)
        {
            if (!__state.Active || __state.Carrier == null || !ShouldCaptureInterfaceAction())
                return;

            Thing selected = SelectedArtifactField.GetValue(__state.Carrier) as Thing;
            int roll = (int)ArtifactRollField.GetValue(__state.Carrier);
            if (selected == __state.Selected && roll == __state.Roll)
                return;

            // The original UI has already played its local sound. Roll the simulation
            // fields back until the synchronized command applies them on every peer.
            SelectedArtifactField.SetValue(__state.Carrier, __state.Selected);
            ArtifactRollField.SetValue(__state.Carrier, __state.Roll);
            if (SyncArtifactSelectionMethod != null && __state.Carrier.parent is Pawn pawn)
                SyncArtifactSelectionMethod.DoSync(null, pawn, selected, roll);
        }

        public static void SyncArtifactSelection(Pawn pawn, Thing selectedArtifact, int roll)
        {
            ThingComp comp = FindTaoistComp(pawn);
            if (comp == null)
                return;

            if (selectedArtifact != null)
            {
                var inventoryOwner = selectedArtifact.ParentHolder?.ParentHolder as Pawn;
                if (inventoryOwner != pawn)
                    return;
            }

            SelectedArtifactField.SetValue(comp, selectedArtifact);
            ArtifactRollField.SetValue(comp, Math.Max(0, roll));
        }

        public static void ArtifactCommandsPostfix(object __instance, ref List<Gizmo> __result)
        {
            if (!ShouldCaptureInterfaceAction() || !(__instance is Thing artifact) || __result == null)
                return;

            foreach (Command_Action command in __result.OfType<Command_Action>())
            {
                Action original = command.action;
                command.action = delegate
                {
                    if (!ShouldCaptureInterfaceAction() || SyncEquipArtifactMethod == null)
                    {
                        original?.Invoke();
                        return;
                    }
                    Pawn pawn = artifact.ParentHolder?.ParentHolder as Pawn;
                    if (pawn != null)
                        SyncEquipArtifactMethod.DoSync(null, pawn, artifact);
                };
            }
        }

        public static void SyncEquipArtifact(Pawn pawn, Thing artifact)
        {
            if (pawn == null || artifact == null || artifact.ParentHolder?.ParentHolder as Pawn != pawn)
                return;

            MethodInfo method = ApparelArtifactType.IsInstanceOfType(artifact)
                ? ApparelArtifactGizmosMethod
                : WeaponArtifactType.IsInstanceOfType(artifact) ? WeaponArtifactGizmosMethod : null;
            if (method == null)
                return;

            var gizmos = method.Invoke(artifact, null) as IEnumerable<Gizmo>;
            gizmos?.OfType<Command_Action>().FirstOrDefault()?.action?.Invoke();
        }

        public static void CrimsonGizmosPostfix(object __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!ShouldCaptureInterfaceAction() || __instance == null || __result == null)
                return;
            __result = WrapCrimsonGizmos(__instance, __result);
        }

        private static IEnumerable<Gizmo> WrapCrimsonGizmos(object comp, IEnumerable<Gizmo> source)
        {
            string gougeLabel = "RiM.GougeHeart".Translate().ToString();
            foreach (Gizmo gizmo in source)
            {
                if (gizmo is Command_Action command && command.defaultLabel == gougeLabel)
                {
                    Action original = command.action;
                    command.action = delegate
                    {
                        if (!ShouldCaptureInterfaceAction() || SyncGougeHeartMethod == null)
                        {
                            original?.Invoke();
                            return;
                        }
                        Pawn pawn = (comp as ThingComp)?.parent as Pawn;
                        if (pawn != null)
                            SyncGougeHeartMethod.DoSync(null, pawn);
                    };
                }
                yield return gizmo;
            }
        }

        public static void SyncGougeHeart(Pawn pawn)
        {
            if (pawn == null || CrimsonEnergyCompType == null || CrimsonCompGetGizmosMethod == null)
                return;
            ThingComp comp = pawn.AllComps?.FirstOrDefault(c => c != null && CrimsonEnergyCompType.IsInstanceOfType(c));
            if (comp == null)
                return;

            var gizmos = CrimsonCompGetGizmosMethod.Invoke(comp, null) as IEnumerable<Gizmo>;
            string label = "RiM.GougeHeart".Translate().ToString();
            gizmos?.OfType<Command_Action>().FirstOrDefault(c => c.defaultLabel == label)?.action?.Invoke();
        }

        public static void ComponentExposeDataPostfix(object __instance)
        {
            if (__instance == null || Scribe.mode != LoadSaveMode.PostLoadInit)
                return;

            var npcDictionary = NpcDictionaryField.GetValue(__instance) as IDictionary;
            var cachedNpcs = CachedNpcsField.GetValue(null) as IList;
            if (npcDictionary == null || cachedNpcs == null)
                return;

            cachedNpcs.Clear();
            foreach (Pawn pawn in npcDictionary.Values.Cast<object>()
                         .OfType<Pawn>()
                         .OrderBy(p => p.thingIDNumber))
                cachedNpcs.Add(pawn);
        }

        public static void ComponentWorldUpdatePrefix(object __instance)
        {
            if (__instance == null)
                return;
            NormalizePawnDictionary(NameDictionaryField.GetValue(__instance) as IDictionary, true);
            NormalizePawnDictionary(LanternDictionaryField.GetValue(__instance) as IDictionary, false);
        }

        private static void NormalizePawnDictionary(IDictionary dictionary, bool removeNullValues)
        {
            if (dictionary == null || dictionary.Count == 0)
                return;

            var snapshot = new List<DictionaryEntry>(dictionary.Count);
            IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
            while (enumerator.MoveNext())
                snapshot.Add(new DictionaryEntry(enumerator.Key, enumerator.Value));

            var entries = snapshot
                .Where(entry => entry.Key is Pawn && (!removeNullValues || entry.Value != null))
                .OrderBy(entry => ((Pawn)entry.Key).thingIDNumber)
                .ToList();
            bool unchanged = entries.Count == dictionary.Count;
            if (unchanged)
            {
                int index = 0;
                IDictionaryEnumerator currentEnumerator = dictionary.GetEnumerator();
                while (currentEnumerator.MoveNext())
                {
                    var current = new DictionaryEntry(
                        currentEnumerator.Key,
                        currentEnumerator.Value);
                    if (!ReferenceEquals(current.Key, entries[index].Key))
                    {
                        unchanged = false;
                        break;
                    }
                    index++;
                }
            }
            if (unchanged)
                return;

            dictionary.Clear();
            foreach (DictionaryEntry entry in entries)
                dictionary.Add(entry.Key, entry.Value);
        }

        public static void RenderRandPrefix(ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer)
                return;
            Rand.PushState();
            __state = true;
        }

        public static Exception RenderRandFinalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        public static bool DebugSpawnAreaPrefix(CellRect r, Map map, ThingDef def)
        {
            if (!ShouldCaptureInterfaceAction() || SyncDebugSpawnAreaMethod == null)
                return true;
            SyncDebugSpawnAreaMethod.DoSync(null, map, r.minX, r.minZ, r.maxX, r.maxZ, def);
            return false;
        }

        public static void SyncDebugSpawnArea(Map map, int minX, int minZ, int maxX, int maxZ, ThingDef def)
        {
            if (map == null || def == null)
                return;
            CellRect rect = CellRect.FromLimits(minX, minZ, maxX, maxZ);
            DebugSpawnAreaMethod.Invoke(null, new object[] { rect, map, def });
        }

        public static void YinCompGizmosPostfix(object __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!ShouldCaptureInterfaceAction() || !(__instance is ThingComp comp) ||
                !(comp.parent is Pawn pawn) || __result == null)
                return;
            __result = WrapYinCompGizmos(pawn, __result);
        }

        private static IEnumerable<Gizmo> WrapYinCompGizmos(Pawn pawn, IEnumerable<Gizmo> source)
        {
            string jumpingLabel = "RiM.ZombieJumping".Translate().ToString();
            foreach (Gizmo gizmo in source)
            {
                string label = (gizmo as Command)?.defaultLabel;
                bool isJumping = label == jumpingLabel && gizmo is Command_Toggle;
                bool isDebug = YinDebugLabels.Contains(label) &&
                               (gizmo is Command_Action || gizmo is Command_Toggle);
                if (isJumping)
                    WrapYinGizmo(gizmo, pawn, label, SyncYinCommandMethod);
                else if (isDebug)
                    WrapYinGizmo(gizmo, pawn, label, SyncYinDebugCommandMethod);
                yield return gizmo;
            }
        }

        private static void WrapYinGizmo(Gizmo gizmo, Pawn pawn, string label, ISyncMethod syncMethod)
        {
            if (gizmo is Command_Action action)
            {
                Action original = action.action;
                action.action = delegate
                {
                    if (!ShouldCaptureInterfaceAction() || syncMethod == null)
                    {
                        original?.Invoke();
                        return;
                    }
                    syncMethod.DoSync(null, pawn, label);
                };
            }
            else if (gizmo is Command_Toggle toggle)
            {
                Action original = toggle.toggleAction;
                toggle.toggleAction = delegate
                {
                    if (!ShouldCaptureInterfaceAction() || syncMethod == null)
                    {
                        original?.Invoke();
                        return;
                    }
                    syncMethod.DoSync(null, pawn, label);
                };
            }
        }

        public static void SyncYinCommand(Pawn pawn, string label)
        {
            ExecuteYinCommand(pawn, label, false);
        }

        public static void SyncYinDebugCommand(Pawn pawn, string label)
        {
            ExecuteYinCommand(pawn, label, true);
        }

        private static void ExecuteYinCommand(Pawn pawn, string label, bool debug)
        {
            if (pawn == null || string.IsNullOrEmpty(label) || YinCompGetGizmosMethod == null)
                return;
            if (debug && !YinDebugLabels.Contains(label))
                return;
            if (!debug && label != "RiM.ZombieJumping".Translate().ToString())
                return;

            ThingComp comp = pawn.AllComps?.FirstOrDefault(c => c != null && YinCompType.IsInstanceOfType(c));
            var gizmos = comp == null ? null : YinCompGetGizmosMethod.Invoke(comp, null) as IEnumerable<Gizmo>;
            Gizmo matching = gizmos?.FirstOrDefault(g => (g as Command)?.defaultLabel == label);
            if (matching is Command_Action action)
                action.action?.Invoke();
            else if (matching is Command_Toggle toggle)
                toggle.toggleAction?.Invoke();
        }

        public static void TaoistCastingExposeDataPostfix(object __instance)
        {
            if (__instance == null)
                return;

            Thing compass = TaoistCastingCompassField.GetValue(__instance) as Thing;
            Scribe_References.Look(ref compass, "mpMeow_taoistCastingCompass");
            TaoistCastingCompassField.SetValue(__instance, compass);
        }

        public static bool ApprenticeTalePostfixPrefix(TaleDef def, object[] args)
        {
            if (def == TaleDefOf.BecameLover)
                ApplyApprenticeRelationshipMemories(args?.OfType<Pawn>(), false);
            return false;
        }

        public static bool ApprenticeMarriagePostfixPrefix(LordJob_Joinable_MarriageCeremony __instance)
        {
            if (__instance != null)
                ApplyApprenticeRelationshipMemories(
                    new[] { __instance.firstPawn, __instance.secondPawn }, true);
            return false;
        }

        private static void ApplyApprenticeRelationshipMemories(IEnumerable<Pawn> participants, bool wedding)
        {
            object component = RmComponentProperty.GetValue(null, null);
            if (component == null)
                return;
            Pawn apprentice = ApprenticeField.GetValue(component) as Pawn;
            Pawn master = MasterField.GetValue(component) as Pawn;
            if (apprentice == null || master == null || apprentice.Dead || master.Dead)
                return;

            var participantSet = new HashSet<Pawn>((participants ?? Enumerable.Empty<Pawn>()).Where(p => p != null));
            bool apprenticeParticipated = participantSet.Contains(apprentice);
            bool masterParticipated = participantSet.Contains(master);
            if (!apprenticeParticipated && !masterParticipated)
                return;

            if (apprenticeParticipated && masterParticipated)
            {
                GiveMemory(master, wedding ? "RM_WeddingWithApprantice" : "RM_RomanceWithApprantice");
                GiveMemory(apprentice, wedding ? "RM_WeddingWithMaster" : "RM_RomanceWithMaster");
            }
            else if (apprenticeParticipated)
            {
                GiveMemory(master, wedding ? "RM_AppranticeWedding" : "RM_AppranticeRomance");
            }
            else
            {
                GiveMemory(apprentice, wedding ? "RM_MasterWedding" : "RM_MasterRomance");
            }
        }

        private static void GiveMemory(Pawn pawn, string thoughtDefName)
        {
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtDefName);
            if (thought != null)
                pawn?.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
        }

        public static bool AnyZombieThreatPrefix(Map map, int interval, ref bool __result)
        {
            int safeInterval = Math.Max(1, interval);
            ThreatCache cache = GetThreatCache(map, "bool:" + safeInterval, safeInterval);
            __result = cache != null && cache.Hostile;
            return false;
        }

        public static bool AnyZombieThreatWithLevelPrefix(Map map, ref int level, ref bool __result)
        {
            ThreatCache cache = GetThreatCache(map, "level", 60);
            level = cache?.Level ?? 0;
            __result = cache != null && cache.Hostile;
            return false;
        }

        private static ThreatCache GetThreatCache(Map map, string suffix, int interval)
        {
            if (map == null)
                return null;
            string key = map.uniqueID + ":" + suffix;
            if (!ThreatCaches.TryGetValue(key, out ThreatCache cache))
            {
                cache = new ThreatCache { Map = map };
                ThreatCaches.Add(key, cache);
            }
            else if (!ReferenceEquals(cache.Map, map))
            {
                cache.Map = map;
                cache.Tick = int.MinValue;
            }

            int tick = Find.TickManager?.TicksGame ?? 0;
            if (cache.Tick == int.MinValue || (cache.Tick != tick && tick % interval == 0))
            {
                EvaluateZombieThreat(map, out cache.Hostile, out cache.Level);
                cache.Tick = tick;
            }
            return cache;
        }

        private static void EvaluateZombieThreat(Map map, out bool hostile, out int level)
        {
            hostile = false;
            level = 0;
            foreach (Pawn pawn in map.mapPawns.AllHumanlikeSpawned)
            {
                if (!TryGetZombieLevel(pawn, out int pawnLevel))
                    continue;

                ThingComp yin = pawn.AllComps?.FirstOrDefault(c => c != null && YinCompType.IsInstanceOfType(c));
                Pawn contained = yin == null ? null : YinContainedThingProperty.GetValue(yin, null) as Pawn;
                bool containedZombie = TryGetZombieLevel(contained, out int containedLevel);
                if (!pawn.HostileTo(Faction.OfPlayer) && !containedZombie)
                    continue;

                hostile = true;
                level = Math.Max(level, Math.Max(pawnLevel, containedLevel));
            }

            foreach (PawnFlyer flyer in map.listerThings.AllThings.OfType<PawnFlyer>())
            {
                Pawn pawn = flyer.FlyingPawn;
                if (pawn != null && pawn.HostileTo(Faction.OfPlayer) &&
                    TryGetZombieLevel(pawn, out int pawnLevel))
                {
                    hostile = true;
                    level = Math.Max(level, pawnLevel);
                }
            }
        }

        private static bool TryGetZombieLevel(Pawn pawn, out int level)
        {
            level = 0;
            if (pawn == null)
                return false;
            object[] args = { pawn, 0 };
            bool zombie = (bool)IsZombieMethod.Invoke(null, args);
            if (!zombie)
                return false;

            ThingComp yin = pawn.AllComps?.FirstOrDefault(c => c != null && YinCompType.IsInstanceOfType(c));
            if (yin != null)
                level = (int)YinLevelProperty.GetValue(yin, null);
            else if (args[1] is int reportedLevel)
                level = reportedLevel;
            return true;
        }

        private static int RegisterDebugMethods()
        {
            string[] methodNames =
            {
                "SetAsPurpleZombie", "SetAsWhiteZombie", "SetAsGreenZombie",
                "SetAsBeastlyZombie", "SetAsFlyingZombie", "GiveTaoistName",
                "ChangeTaoistName", "ForceStopMusic", "BestRelationship",
                "RespawnNPCs", "ForceStopWorshpMoon", "FixIncantationExecute",
                "AddKillCount"
            };
            int registered = 0;
            foreach (string methodName in methodNames)
            {
                MethodInfo method = AccessTools.Method(RmDebugToolsType, methodName);
                if (method == null)
                {
                    Log.Error($"{LogTag}: debug action symbol missing: RigorMortis.DebugTools.{methodName}.");
                    continue;
                }
                MP.RegisterSyncMethod(method, null).SetDebugOnly();
                registered++;
            }
            return registered;
        }

        public static void SettingsDrawPrefix(out SettingsUiState __state)
        {
            __state = default(SettingsUiState);
            if (!ShouldCaptureInterfaceAction())
                return;
            __state.Active = true;
            __state.Integers = ReadIntegerSettings();
            __state.Booleans = ReadBooleanSettings();
        }

        public static void SettingsDrawPostfix(SettingsUiState __state)
        {
            if (!__state.Active || !ShouldCaptureInterfaceAction())
                return;

            int[] integers = ReadIntegerSettings();
            bool[] booleans = ReadBooleanSettings();
            if (__state.Integers.SequenceEqual(integers) && __state.Booleans.SequenceEqual(booleans))
                return;

            WriteSettings(__state.Integers, __state.Booleans);
            SyncSettingsMethod?.DoSync(null, integers, booleans);
        }

        public static void SyncSettings(int[] integers, bool[] booleans)
        {
            if (integers == null || booleans == null ||
                integers.Length != IntegerSettingFields.Length ||
                booleans.Length != BooleanSettingFields.Length)
                return;

            int[] safeIntegers = (int[])integers.Clone();
            safeIntegers[0] = Math.Min(20, Math.Max(1, safeIntegers[0]));
            safeIntegers[1] = Math.Min(40, Math.Max(10, safeIntegers[1]));
            safeIntegers[2] = Math.Min(100, Math.Max(1, safeIntegers[2]));
            safeIntegers[3] = Math.Min(200, Math.Max(0, safeIntegers[3]));
            safeIntegers[4] = Math.Min(200, Math.Max(0, safeIntegers[4]));
            safeIntegers[5] = Math.Min(200, Math.Max(0, safeIntegers[5]));
            safeIntegers[6] = Math.Min(100, Math.Max(0, safeIntegers[6]));
            WriteSettings(safeIntegers, booleans);
        }

        private static int[] ReadIntegerSettings()
        {
            return IntegerSettingFields.Select(field => (int)field.GetValue(null)).ToArray();
        }

        private static bool[] ReadBooleanSettings()
        {
            return BooleanSettingFields.Select(field => (bool)field.GetValue(null)).ToArray();
        }

        private static void WriteSettings(int[] integers, bool[] booleans)
        {
            for (int i = 0; i < IntegerSettingFields.Length; i++)
                IntegerSettingFields[i].SetValue(null, integers[i]);
            for (int i = 0; i < BooleanSettingFields.Length; i++)
                BooleanSettingFields[i].SetValue(null, booleans[i]);
        }

        public static IEnumerable<CodeInstruction> SortNamedPawnKeysTranspiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var list = instructions.ToList();
            MethodInfo replacement = AccessTools.Method(
                typeof(Patch_RigorMortisStateActions), nameof(SortedNamedPawnKeys));
            int replaced = 0;
            foreach (CodeInstruction instruction in list)
            {
                if (instruction.operand is MethodInfo method && method.Name == "get_Keys" &&
                    method.DeclaringType != null && method.DeclaringType.IsGenericType &&
                    method.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    Type[] args = method.DeclaringType.GetGenericArguments();
                    if (args.Length == 2 && args[0] == typeof(Pawn) && args[1] == typeof(string))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = replacement;
                        replaced++;
                    }
                }
            }

            if (replaced != 1)
                Log.Error($"{LogTag}: expected one pawn dictionary key read in " +
                          $"{__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name}, found {replaced}.");
            return list;
        }

        public static IEnumerable<CodeInstruction> SortDamagePawnKeysTranspiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var list = instructions.ToList();
            MethodInfo replacement = AccessTools.Method(
                typeof(Patch_RigorMortisStateActions), nameof(SortedDamagePawnKeys));
            int replaced = 0;
            foreach (CodeInstruction instruction in list)
            {
                if (instruction.operand is MethodInfo method && method.Name == "get_Keys" &&
                    method.DeclaringType == typeof(Dictionary<Pawn, float>))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replaced++;
                }
            }
            if (replaced != 1)
                Log.Error($"{LogTag}: expected one damage dictionary key read in " +
                          $"{__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name}, found {replaced}.");
            return list;
        }

        public static IEnumerable<Pawn> SortedNamedPawnKeys(Dictionary<Pawn, string> dictionary)
        {
            if (dictionary == null)
                return Enumerable.Empty<Pawn>();
            return dictionary.Keys.Where(p => p != null).OrderBy(p => p.thingIDNumber);
        }

        public static IEnumerable<Pawn> SortedDamagePawnKeys(Dictionary<Pawn, float> dictionary)
        {
            if (dictionary == null)
                return Enumerable.Empty<Pawn>();
            return dictionary.Keys.Where(p => p != null).OrderBy(p => p.thingIDNumber);
        }

        private static ThingComp FindTaoistComp(Pawn pawn)
        {
            return pawn?.AllComps?.FirstOrDefault(c => c != null && CompTaoistType.IsInstanceOfType(c));
        }

        private static bool ShouldCaptureInterfaceAction()
        {
            return MP.enabled && MP.IsInMultiplayer && !MP.IsExecutingSyncCommand;
        }
    }
}
