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
    /// P1 RJW-family multiplayer compatibility:
    /// - FamilyOverhaul (ESeeker.FO): rewrites local float-menu actions that
    ///   mutate bio parents, AI-mother relations, servant status letters, and
    ///   debug toggles into synced commands; servant letters are recreated on
    ///   every peer so Multiplayer's built-in DiaOption sync can replay choices.
    /// - RJWPeculiarInstitution: syncs liege assignment/unassignment and makes
    ///   autonomous concubine target/building selection stable.
    /// - RJW_Hdryad: sorts dictionary-backed dryad target selection before
    ///   consuming Verse.Rand.
    /// - RaddusX Demons: replaces the ability's time-derived System.Random with
    ///   a Verse.Rand-seeded deterministic instance.
    /// - Ballz gender organs: skips the per-frame ITab hormone write in
    ///   multiplayer; the world tick already applies the same deterministic
    ///   result on both peers.
    /// - rjw_cnc: syncs the free-use designation checkbox/widget toggle and
    ///   removes the auto-reset side effect from the designation getter.
    /// - UAP: bypasses the process-local genital cache in multiplayer.
    ///
    /// Every target is resolved by type/method names at startup and a compact
    /// resolution summary is logged. Missing targets only disable the affected
    /// section; they never silently register a wrong overload.
    /// </summary>
    internal static class Patch_RjwP1
    {
        private const string FamilyPackageId = "ESeeker.FO";
        private const string FamilyNamespace = "FamilyOverhaul";
        private const string FamilyDataTypeName = "FamilyOverhaul.Comp_FamilyData";
        private const string FamilyRelationGetterTypeName = "FamilyOverhaul.RelationGetter";
        private const string FamilyLetterTypeName =
            "FamilyOverhaul.ChoiceLetter_ChooseServantDesignator";

        private const string PeculiarPackageId = "readyforjeff.RJWPeculiarInstitution";
        private const string PeculiarCompTypeName = "RJWPeculiarInstitution.PeculiarComp";
        private const string CollarCompTypeName =
            "RJWPeculiarInstitution.Apparel_Collar_Concubine_Comp";
        private const string LiegeSexJobGiverTypeName =
            "RJWPeculiarInstitution.JobGiver_LiegeSex";
        private const string LiegeRoomServiceDriverTypeName =
            "RJWPeculiarInstitution.JobDriver_LiegeGetRoomService";

        private const string HdryadHelperTypeName = "RJW_Hdryad.DryadHelper";

        private const string DemonsEffectTypeName =
            "RaddusX.Demons.Abilities.Draining_Kiss_Ability_Effect";

        private const string BallzPackageId = "TeheeItsMe525.RJWGenderOrgansMod";
        private const string BallzHormoneTabTypeName = "Ballz.ITab_Pawn_Hormones";

        private const string CncPackageId = "moth.rjw.cnc";
        private const string CncCompTypeName = "rjw_cnc.CompFreeUse";
        private const string CncDesignationUtilityTypeName = "rjw_cnc.DesignationUtility";

        private const string UapPackageId = "Teacher.UAP";
        private const string UapGenitalCacheTypeName = "UAP_Animations.GenitalCache";

        private const string MenstruationResourcesPackageId =
            "ElToro.rjw.menstruation.resources";
        private const string MenstruationResourcesHelperTypeName =
            "RJW_Menstruation_Resources.ResourceHelpers";
        private const string MenstruationHybridXenotypeTypeName =
            "RJW_Menstruation_Resources.HybridXenotypeHelper";
        private const string MenstruationHybridChooseOneTypeName =
            "RJW_Menstruation_Resources.Patch_HybridExtension_ChooseOne";

        private const string UnleashedFrameworkPackageId = "rjw.unleashed.framework";
        private const string UnleashedApplyEffectTypeName =
            "RJW_Unleashed_Framework.ApplyEffect";

        private const string AnimalGeneInheritancePackageId =
            "telanda.rjw.animalgeneinheritance";
        private const string AnimalGeneInheritancePatchTypeName =
            "RJW_BGS.Patch_RJW_PregnancyHelper_VanillaExpandedGenetics";

        private const string PrivacyPackageId = "abscon.privacy.please";
        private const string PrivacySexSetupPatchTypeName =
            "Privacy_Please.HarmonyPatch_JobDriver_Sex_setup_ticks";

        private const string RjwExtensionPackageId = "rimworld.ekss.rjwex";
        private const string RjwExtensionPublicPrivateTypeName =
            "rjwex.PublicPrivateComp";

        private static Type _familyDataType;
        private static Type _familyLetterType;
        private static Type _servantTypeEnum;
        private static Type _letterConditionEnum;
        private static MethodInfo _familyDataExtension;
        private static MethodInfo _setBioMom;
        private static MethodInfo _setBioDad;
        private static MethodInfo _addServant;
        private static MethodInfo _editServant;
        private static MethodInfo _removeServant;
        private static MethodInfo _addDirectRelation;
        private static MethodInfo _makeLetter;
        private static MethodInfo _receiveLetter;
        private static FieldInfo _bastardField;
        private static FieldInfo _bioMomLockField;
        private static FieldInfo _bioDadLockField;
        private static FieldInfo _letterOldMaster;
        private static FieldInfo _letterOldServantType;
        private static FieldInfo _letterMaster;
        private static FieldInfo _letterServant;
        private static FieldInfo _letterCondition;

        private static Type _collarCompType;
        private static MethodInfo _forceAddLiege;
        private static MethodInfo _tryRemoveLiege;
        private static MethodInfo _reactToUserChanges;
        private static MethodInfo _addLiegeRelationWith;
        private static MethodInfo _removeLiegeRelationTo;
        private static MethodInfo _liegeSexTryGiveJob;
        private static MethodInfo _liegeRoomServiceMakeNewToils;

        private static MethodInfo _dryadFindTarget;

        private static MethodInfo _drainingKissOutcome;

        private static MethodInfo _hormoneApply;

        private static MethodInfo _menstruationChooseOneResource;
        private static MethodInfo _menstruationHybridChooseOne;
        private static MethodInfo _menstruationTryPickXenotype;

        private static MethodInfo _unleashedApplyEffect;

        private static MethodInfo _animalGeneAddPregnancyHediff;

        private static MethodInfo _privacySexSetupPostfix;

        private static MethodInfo _rjwExtensionChangeMode;

        private static Type _cncCompType;
        private static MethodInfo _cncSetIsDesignated;
        private static MethodInfo _cncIsDesignatedFreeUse;
        private static MethodInfo _cncCanBeDesignatedFreeUse;
        private static FieldInfo _cncIsDesignatedField;

        private static Type _uapGenitalCacheType;
        private static MethodInfo _uapHasPenis;
        private static MethodInfo _rjwHasPenis;

        private static ISyncMethod _syncSetBio;
        private static ISyncMethod _syncAiMother;
        private static ISyncMethod _syncServantLetter;
        private static ISyncMethod _syncToggleBastard;
        private static ISyncMethod _syncToggleBioLock;
        private static ISyncMethod _syncLiegeAdd;
        private static ISyncMethod _syncLiegeRemove;
        private static ISyncMethod _syncSetFreeUse;

        private static bool _familyResolved;
        private static bool _peculiarResolved;
        private static bool _dryadResolved;
        private static bool _demonsResolved;
        private static bool _ballzResolved;
        private static bool _cncResolved;
        private static bool _uapResolved;
        private static bool _menstruationResolved;
        private static bool _unleashedResolved;
        private static bool _animalGeneInheritanceResolved;
        private static bool _privacyResolved;
        private static bool _rjwExtensionResolved;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            PatchFamilyOverhaul(harmony);
            PatchPeculiarInstitution(harmony);
            PatchHdryad(harmony);
            PatchRaddusxDemons(harmony);
            PatchBallzHormoneTab(harmony);
            PatchCncFreeUse(harmony);
            PatchUapGenitalCache(harmony);
            PatchMenstruationResources(harmony);
            PatchUnleashedFramework(harmony);
            PatchAnimalGeneInheritance(harmony);
            PatchPrivacyPlease(harmony);
            PatchRjwExtension(harmony);

            Log.Message(
                "[MP-MeowOnlineShop][RJW-P1] target resolution complete: " +
                $"family={_familyResolved}, peculiar={_peculiarResolved}, " +
                $"dryad={_dryadResolved}, demons={_demonsResolved}, " +
                $"ballz={_ballzResolved}, cnc={_cncResolved}, uap={_uapResolved}, " +
                $"menstruation={_menstruationResolved}, unleashed={_unleashedResolved}, " +
                $"animalGeneInheritance={_animalGeneInheritanceResolved}, " +
                $"privacy={_privacyResolved}, rjwExtension={_rjwExtensionResolved}.");
        }

        private static void PatchFamilyOverhaul(Harmony harmony)
        {
            if (!ModsConfig.IsActive(FamilyPackageId))
                return;

            _familyDataType = AccessTools.TypeByName(FamilyDataTypeName);
            Type relationGetter = AccessTools.TypeByName(FamilyRelationGetterTypeName);
            _familyLetterType = AccessTools.TypeByName(FamilyLetterTypeName);
            Type servantRelationType = _familyDataType == null
                ? null
                : _familyDataType.Assembly.GetType(
                    "FamilyOverhaul.ServantPawnRelation",
                    false);
            _servantTypeEnum = servantRelationType?.GetNestedType(
                "ServantType",
                BindingFlags.Public | BindingFlags.NonPublic);

            _familyDataExtension = relationGetter == null
                ? null
                : AccessTools.Method(
                    relationGetter,
                    "FamilyData",
                    new[] { typeof(Pawn) });
            _setBioMom = AccessTools.Method(
                _familyDataType,
                "SetBioMom",
                new[]
                {
                    typeof(Pawn),
                    typeof(string),
                    typeof(string),
                    typeof(bool),
                    typeof(TaggedString?)
                });
            _setBioDad = AccessTools.Method(
                _familyDataType,
                "SetBioDad",
                new[]
                {
                    typeof(Pawn),
                    typeof(string),
                    typeof(string),
                    typeof(bool),
                    typeof(TaggedString?)
                });
            _addServant = AccessTools.Method(
                _familyDataType,
                "AddServant",
                new[] { typeof(Pawn), _servantTypeEnum });
            _editServant = AccessTools.Method(
                _familyDataType,
                "EditServant",
                new[] { typeof(Pawn), _servantTypeEnum });
            _removeServant = AccessTools.Method(
                _familyDataType,
                "RemoveServant",
                new[] { typeof(Pawn) });
            _addDirectRelation = AccessTools.Method(
                typeof(Pawn_RelationsTracker),
                "AddDirectRelation",
                new[] { typeof(PawnRelationDef), typeof(Pawn) });
            _makeLetter = AccessTools.Method(
                typeof(LetterMaker),
                "MakeLetter",
                new[] { typeof(string), typeof(string), typeof(LetterDef) });
            _receiveLetter = AccessTools.Method(
                typeof(LetterStack),
                "ReceiveLetter",
                new[] { typeof(Letter) });
            _bastardField = AccessTools.Field(_familyDataType, "Bastard");
            _bioMomLockField = AccessTools.Field(_familyDataType, "BioMomLock");
            _bioDadLockField = AccessTools.Field(_familyDataType, "BioDadLock");
            _letterOldMaster = AccessTools.Field(_familyLetterType, "OldMaster");
            _letterOldServantType = AccessTools.Field(_familyLetterType, "OldServantType");
            _letterMaster = AccessTools.Field(_familyLetterType, "Master");
            _letterServant = AccessTools.Field(_familyLetterType, "Servant");
            _letterCondition = AccessTools.Field(_familyLetterType, "condition");
            _letterConditionEnum = _familyLetterType?.GetNestedType(
                "Condition",
                BindingFlags.Public | BindingFlags.NonPublic);

            bool exact =
                _familyDataType != null &&
                _familyDataExtension != null &&
                _setBioMom != null &&
                _setBioDad != null &&
                _addServant != null &&
                _editServant != null &&
                _removeServant != null &&
                _addDirectRelation != null &&
                _makeLetter != null &&
                _receiveLetter != null &&
                _bastardField != null &&
                _bioMomLockField != null &&
                _bioDadLockField != null &&
                _familyLetterType != null &&
                _letterOldMaster != null &&
                _letterOldServantType != null &&
                _letterMaster != null &&
                _letterServant != null &&
                _letterCondition != null &&
                _letterConditionEnum != null &&
                _servantTypeEnum != null;

            if (!exact)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] FamilyOverhaul executor signatures " +
                    "not found; float-menu and letter sync was not installed.");
                return;
            }

            _syncSetBio = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncSetBio))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);
            _syncAiMother = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncAiMother))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);
            _syncServantLetter = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncServantLetter))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);
            _syncToggleBastard = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncToggleBastard))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);
            _syncToggleBioLock = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncToggleBioLock))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            var rewrite = new HarmonyMethod(
                AccessTools.Method(
                    typeof(Patch_RjwP1),
                    nameof(RewriteFamilyFloatMenuOption)));
            foreach (ConstructorInfo ctor in typeof(FloatMenuOption).GetConstructors())
                harmony.Patch(ctor, postfix: rewrite);

            _familyResolved = true;
            Log.Message(
                "[MP-MeowOnlineShop][RJW-P1] FamilyOverhaul float-menu and " +
                "servant-letter sync installed.");
        }

        private static void PatchPeculiarInstitution(Harmony harmony)
        {
            if (!ModsConfig.IsActive(PeculiarPackageId))
                return;

            _collarCompType = AccessTools.TypeByName(CollarCompTypeName);
            Type peculiarComp = AccessTools.TypeByName(PeculiarCompTypeName);
            _forceAddLiege = _collarCompType == null
                ? null
                : AccessTools.Method(
                    _collarCompType,
                    "ForceAddLiegeRelationFor",
                    new[] { typeof(Pawn), typeof(Pawn), typeof(bool) });
            _tryRemoveLiege = _collarCompType == null
                ? null
                : AccessTools.Method(
                    _collarCompType,
                    "TryRemoveLiegeRelationBetween",
                    new[] { typeof(Pawn), typeof(Pawn) });
            _reactToUserChanges = peculiarComp == null
                ? null
                : AccessTools.Method(peculiarComp, "ReactToUserChanges");
            _addLiegeRelationWith = _collarCompType == null
                ? null
                : AccessTools.Method(
                    _collarCompType,
                    "AddLiegeRelationWith",
                    new[] { typeof(Pawn) });
            _removeLiegeRelationTo = _collarCompType == null
                ? null
                : AccessTools.Method(
                    _collarCompType,
                    "RemoveLiegeRelationTo",
                    new[] { typeof(Pawn) });
            _liegeSexTryGiveJob = AccessTools.Method(
                AccessTools.TypeByName(LiegeSexJobGiverTypeName),
                "TryGiveJob",
                new[] { typeof(Pawn) });
            _liegeRoomServiceMakeNewToils = AccessTools.Method(
                AccessTools.TypeByName(LiegeRoomServiceDriverTypeName),
                "MakeNewToils",
                Type.EmptyTypes);

            bool exact =
                _collarCompType != null &&
                _forceAddLiege != null &&
                _forceAddLiege.IsStatic &&
                _tryRemoveLiege != null &&
                _tryRemoveLiege.IsStatic &&
                _reactToUserChanges != null &&
                _addLiegeRelationWith != null &&
                _removeLiegeRelationTo != null &&
                _liegeSexTryGiveJob != null &&
                _liegeRoomServiceMakeNewToils != null;

            if (!exact)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] PeculiarInstitution signatures " +
                    "not found; liege sync was not installed.");
                return;
            }

            _syncLiegeAdd = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncLiegeAdd))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);
            _syncLiegeRemove = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncLiegeRemove))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            harmony.Patch(
                _reactToUserChanges,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(ReactToUserChangesPrefix))));
            harmony.Patch(
                _addLiegeRelationWith,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(AddLiegeRelationWithPrefix))));
            harmony.Patch(
                _removeLiegeRelationTo,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(RemoveLiegeRelationToPrefix))));
            harmony.Patch(
                _liegeSexTryGiveJob,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(LiegeSexTranspiler))));
            harmony.Patch(
                _liegeRoomServiceMakeNewToils,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(RandomElementTranspiler))));

            _peculiarResolved = true;
            Log.Message(
                "[MP-MeowOnlineShop][RJW-P1] PeculiarInstitution liege sync and " +
                "stable selection installed.");
        }

        private static void PatchHdryad(Harmony harmony)
        {
            if (!ModsConfig.IsActive("ElToro.HumpshroomDryad"))
                return;

            _dryadFindTarget = AccessTools.Method(
                AccessTools.TypeByName(HdryadHelperTypeName),
                "find_dryad",
                new[] { typeof(Pawn), typeof(Map) });
            if (_dryadFindTarget == null || !_dryadFindTarget.IsStatic)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] Hdryad find_dryad signature not " +
                    "found; stable target selection was not installed.");
                return;
            }

            harmony.Patch(
                _dryadFindTarget,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(RandomElementTranspiler))));
            _dryadResolved = true;
        }

        private static void PatchRaddusxDemons(Harmony harmony)
        {
            if (!ModsConfig.IsActive("raddusx.demons"))
                return;

            _drainingKissOutcome = AccessTools.Method(
                AccessTools.TypeByName(DemonsEffectTypeName),
                "DetermineDrainingKissOutcome",
                new[] { typeof(Pawn), typeof(Pawn) });
            if (_drainingKissOutcome == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] Demons Draining Kiss signature " +
                    "not found; deterministic Random was not installed.");
                return;
            }

            harmony.Patch(
                _drainingKissOutcome,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(DeterministicRandomTranspiler))));
            _demonsResolved = true;
        }

        private static void PatchBallzHormoneTab(Harmony harmony)
        {
            if (!ModsConfig.IsActive(BallzPackageId))
                return;

            _hormoneApply = AccessTools.Method(
                AccessTools.TypeByName(BallzHormoneTabTypeName),
                "ApplyHormoneEffects",
                new[] { typeof(Pawn), typeof(float), typeof(float) });
            if (_hormoneApply == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] Ballz hormone ITab signature " +
                    "not found; per-frame UI hormone write was not suppressed.");
                return;
            }

            harmony.Patch(
                _hormoneApply,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(HormoneTabPrefix))));
            _ballzResolved = true;
        }

        private static void PatchCncFreeUse(Harmony harmony)
        {
            if (!ModsConfig.IsActive(CncPackageId))
                return;

            _cncCompType = AccessTools.TypeByName(CncCompTypeName);
            Type designationUtility = AccessTools.TypeByName(CncDesignationUtilityTypeName);
            _cncSetIsDesignated = AccessTools.PropertySetter(_cncCompType, "IsDesignated");
            _cncIsDesignatedField = AccessTools.Field(_cncCompType, "isDesignated");
            _cncIsDesignatedFreeUse = designationUtility == null
                ? null
                : AccessTools.Method(
                    designationUtility,
                    "IsDesignatedFreeUse",
                    new[] { typeof(Pawn) });
            _cncCanBeDesignatedFreeUse = designationUtility == null
                ? null
                : AccessTools.Method(
                    designationUtility,
                    "CanBeDesignatedFreeUse",
                    new[] { typeof(Pawn) });

            bool exact =
                _cncCompType != null &&
                _cncSetIsDesignated != null &&
                _cncIsDesignatedField != null &&
                _cncIsDesignatedFreeUse != null &&
                _cncCanBeDesignatedFreeUse != null;
            if (!exact)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] rjw_cnc free-use signatures not " +
                    "found; designation sync was not installed.");
                return;
            }

            _syncSetFreeUse = MP.RegisterSyncMethod(
                    typeof(Patch_RjwP1),
                    nameof(SyncSetFreeUse))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            harmony.Patch(
                _cncSetIsDesignated,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(CncSetIsDesignatedPrefix))));
            harmony.Patch(
                _cncIsDesignatedFreeUse,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(CncIsDesignatedFreeUsePrefix))));
            _cncResolved = true;
            Log.Message(
                "[MP-MeowOnlineShop][RJW-P1] rjw_cnc free-use designation sync " +
                "installed.");
        }

        private static void PatchUapGenitalCache(Harmony harmony)
        {
            if (!ModsConfig.IsActive(UapPackageId))
                return;

            _uapGenitalCacheType = AccessTools.TypeByName(UapGenitalCacheTypeName);
            _uapHasPenis = _uapGenitalCacheType == null
                ? null
                : AccessTools.Method(
                    _uapGenitalCacheType,
                    "HasPenis",
                    new[] { typeof(Pawn) });
            Type rjwGenitalHelper = AccessTools.TypeByName("rjw.Genital_Helper");
            _rjwHasPenis = rjwGenitalHelper == null
                ? null
                : AccessTools.Method(
                    rjwGenitalHelper,
                    "has_male_bits");
            if (_uapHasPenis == null || _rjwHasPenis == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] UAP genital cache signature not " +
                    "found; process-local cache guard was not installed.");
                return;
            }

            harmony.Patch(
                _uapHasPenis,
                prefix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(UapGenitalCachePrefix)))
                {
                    priority = Priority.First
                });
            _uapResolved = true;
        }

        private static void PatchMenstruationResources(Harmony harmony)
        {
            if (!ModsConfig.IsActive(MenstruationResourcesPackageId))
                return;

            Type resourceHelpers = AccessTools.TypeByName(
                MenstruationResourcesHelperTypeName);
            Type hybridXenotypeHelper = AccessTools.TypeByName(
                MenstruationHybridXenotypeTypeName);
            Type hybridChooseOne = AccessTools.TypeByName(
                MenstruationHybridChooseOneTypeName);
            // HybridExtension belongs to the base Menstruation assembly, not
            // the Resources add-on namespace in the installed 1.6 build.
            Type hybridExtension = AccessTools.TypeByName(
                "RJW_Menstruation.HybridExtension");
            _menstruationChooseOneResource = resourceHelpers == null || hybridExtension == null
                ? null
                : AccessTools.Method(
                    resourceHelpers,
                    "ChooseOneResource",
                    new[] { hybridExtension });
            _menstruationTryPickXenotype = hybridXenotypeHelper == null
                ? null
                : AccessTools.Method(
                    hybridXenotypeHelper,
                    "TryPickXenotypeForParents",
                    new[]
                    {
                        typeof(Pawn),
                        typeof(Pawn),
                        typeof(XenotypeDef).MakeByRefType()
                    });
            _menstruationHybridChooseOne = hybridChooseOne == null
                ? null
                : AccessTools.Method(hybridChooseOne, "Prefix");

            if (_menstruationChooseOneResource == null ||
                _menstruationTryPickXenotype == null ||
                _menstruationHybridChooseOne == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] Menstruation Resources hybrid " +
                    "selection signatures not found; stable weighted selection " +
                    "was not installed.");
                return;
            }

            var weightedTranspiler = new HarmonyMethod(
                AccessTools.Method(
                    typeof(Patch_RjwP1),
                    nameof(WeightedElementTranspiler)));
            harmony.Patch(
                _menstruationChooseOneResource,
                transpiler: weightedTranspiler);
            harmony.Patch(
                _menstruationTryPickXenotype,
                transpiler: weightedTranspiler);
            harmony.Patch(
                _menstruationHybridChooseOne,
                transpiler: weightedTranspiler);
            _menstruationResolved = true;
            Log.Message(
                "[MP-MeowOnlineShop][RJW-P1] Menstruation Resources hybrid " +
                "weighted selection stabilized.");
        }

        private static void PatchUnleashedFramework(Harmony harmony)
        {
            if (!ModsConfig.IsActive(UnleashedFrameworkPackageId))
                return;

            _unleashedApplyEffect = AccessTools.Method(
                AccessTools.TypeByName(UnleashedApplyEffectTypeName),
                "FullExtension",
                new[] { typeof(Pawn), typeof(ThingDef) });
            if (_unleashedApplyEffect == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] Unleashed Framework ApplyEffect " +
                    "signature not found; deterministic Random was not installed.");
                return;
            }

            harmony.Patch(
                _unleashedApplyEffect,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(DeterministicRandomTranspiler))));
            _unleashedResolved = true;
        }

        private static void PatchAnimalGeneInheritance(Harmony harmony)
        {
            if (!ModsConfig.IsActive(AnimalGeneInheritancePackageId))
                return;

            _animalGeneAddPregnancyHediff = AccessTools.Method(
                AccessTools.TypeByName(AnimalGeneInheritancePatchTypeName),
                "AddPregnancyHediffPrefix");
            if (_animalGeneAddPregnancyHediff == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] Animal Gene Inheritance pregnancy " +
                    "patch signature not found; deterministic Random was not " +
                    "installed.");
                return;
            }

            harmony.Patch(
                _animalGeneAddPregnancyHediff,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(DeterministicRandomTranspiler))));
            _animalGeneInheritanceResolved = true;
        }

        private static void PatchPrivacyPlease(Harmony harmony)
        {
            if (!ModsConfig.IsActive(PrivacyPackageId))
                return;

            _privacySexSetupPostfix = AccessTools.Method(
                AccessTools.TypeByName(PrivacySexSetupPatchTypeName),
                "Postfix");
            if (_privacySexSetupPostfix == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] Privacy Please sex setup postfix " +
                    "signature not found; Unity Random isolation was not installed.");
                return;
            }

            harmony.Patch(
                _privacySexSetupPostfix,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RjwP1),
                        nameof(UnityRandomTranspiler))));
            _privacyResolved = true;
        }

        private static void PatchRjwExtension(Harmony harmony)
        {
            if (!ModsConfig.IsActive(RjwExtensionPackageId))
                return;

            // The mod ships [SyncMethod] attributes but never calls MP.RegisterAll
            // for its own assembly, so its public/private gizmo executor is not
            // automatically intercepted by Multiplayer. Register the stable
            // ThingComp executor; Multiplayer serializes ThingComp through the
            // parent Thing and patches the direct call into a synced command.
            _rjwExtensionChangeMode = AccessTools.Method(
                AccessTools.TypeByName(RjwExtensionPublicPrivateTypeName),
                "ChangeMode",
                Type.EmptyTypes);
            if (_rjwExtensionChangeMode == null ||
                _rjwExtensionChangeMode.ReturnType != typeof(void))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] RJW Extension ChangeMode " +
                    "signature not found; gizmo sync was not installed.");
                return;
            }

            MP.RegisterSyncMethod(_rjwExtensionChangeMode, null);
            _rjwExtensionResolved = true;
            Log.Message(
                "[MP-MeowOnlineShop][RJW-P1] RJW Extension public/private " +
                "gizmo registered as a synced command.");
        }

        private static bool ShouldSyncUi()
        {
            return MP.IsInMultiplayer &&
                   !MP.IsExecutingSyncCommand &&
                   Current.ProgramState == ProgramState.Playing &&
                   Find.TickManager != null;
        }

        private static object GetFamilyData(Pawn pawn)
        {
            if (pawn == null || _familyDataExtension == null)
                return null;
            try
            {
                return _familyDataExtension.Invoke(null, new object[] { pawn });
            }
            catch
            {
                return null;
            }
        }

        private static object FindComp(Pawn pawn, string typeName)
        {
            if (pawn?.AllComps == null)
                return null;
            foreach (ThingComp comp in pawn.AllComps)
                if (comp != null &&
                    string.Equals(
                        comp.GetType().FullName,
                        typeName,
                        StringComparison.Ordinal))
                    return comp;
            return null;
        }

        private static void RewriteFamilyFloatMenuOption(FloatMenuOption __instance)
        {
            if (__instance?.action == null || !MP.IsInMultiplayer)
                return;

            Action original = __instance.action;
            MethodInfo method = original.Method;
            object closure = original.Target;
            if (method?.DeclaringType == null || closure == null)
                return;
            if (method.DeclaringType.Namespace?.StartsWith(
                    FamilyNamespace,
                    StringComparison.Ordinal) != true)
                return;

            __instance.action = BuildFamilyAction(original, closure);
        }

        private static Action BuildFamilyAction(Action original, object closure)
        {
            byte[] il = original.Method?.GetMethodBody()?.GetILAsByteArray();
            if (il == null)
                return original;

            int bioKind = 0;
            bool aiMother = false;
            bool servantLetter = false;
            bool bastardToggle = false;
            int bioLockToggle = -1;

            for (int index = 0; index + 4 < il.Length; index++)
            {
                if (il[index] == OpCodes.Call.Value)
                {
                    MethodInfo called = ResolveCall(original.Method, il, index);
                    if (called == null)
                        continue;
                    if (SameMethod(called, _setBioMom))
                        bioKind |= 1;
                    else if (SameMethod(called, _setBioDad))
                        bioKind |= 2;
                    else if (SameMethod(called, _addDirectRelation))
                        aiMother = true;
                    else if (SameMethod(called, _makeLetter))
                        servantLetter = true;
                }
                else if (il[index] == OpCodes.Stfld.Value)
                {
                    FieldInfo field = ResolveField(original.Method, il, index);
                    if (field == null)
                        continue;
                    if (SameField(field, _bastardField))
                        bastardToggle = true;
                    else if (SameField(field, _bioMomLockField))
                        bioLockToggle = 0;
                    else if (SameField(field, _bioDadLockField))
                        bioLockToggle = 1;
                }
            }

            List<Pawn> pawns = CollectPawnFields(closure);
            PawnRelationDef capturedRelation = CollectRelationDef(closure);

            if (servantLetter && pawns.Count >= 2)
            {
                Pawn first = pawns[0];
                Pawn second = pawns[1];
                return () => _syncServantLetter.DoSync(null, first, second);
            }
            if (bioKind != 0 && pawns.Count >= 2)
            {
                Pawn modified = pawns[0];
                Pawn target = pawns[1];
                return () => _syncSetBio.DoSync(null, modified, target, bioKind);
            }
            if (aiMother && pawns.Count >= 2 && capturedRelation?.defName == "FO_AIMother")
            {
                Pawn pawn = pawns[0];
                Pawn target = pawns[1];
                return () => _syncAiMother.DoSync(null, pawn, target);
            }
            if (bastardToggle && pawns.Count >= 1)
            {
                Pawn pawn = pawns[0];
                return () => _syncToggleBastard.DoSync(null, pawn);
            }
            if (bioLockToggle >= 0 && pawns.Count >= 1)
            {
                Pawn pawn = pawns[0];
                int lockKind = bioLockToggle;
                return () => _syncToggleBioLock.DoSync(null, pawn, lockKind);
            }

            return original;
        }

        private static MethodInfo ResolveCall(MethodBase method, byte[] il, int index)
        {
            try
            {
                int token = BitConverter.ToInt32(il, index + 1);
                return method.Module.ResolveMethod(
                    token,
                    GetTypeArguments(method),
                    GetMethodArguments(method)) as MethodInfo;
            }
            catch
            {
                return null;
            }
        }

        private static FieldInfo ResolveField(MethodBase method, byte[] il, int index)
        {
            try
            {
                int token = BitConverter.ToInt32(il, index + 1);
                return method.Module.ResolveField(
                    token,
                    GetTypeArguments(method),
                    GetMethodArguments(method));
            }
            catch
            {
                return null;
            }
        }

        private static Type[] GetTypeArguments(MethodBase method)
        {
            return method.DeclaringType != null && method.DeclaringType.IsGenericType
                ? method.DeclaringType.GetGenericArguments()
                : null;
        }

        private static Type[] GetMethodArguments(MethodBase method)
        {
            return method.IsGenericMethod ? method.GetGenericArguments() : null;
        }

        private static List<Pawn> CollectPawnFields(object closure)
        {
            var result = new List<Pawn>();
            foreach (FieldInfo field in closure.GetType().GetFields(
                         BindingFlags.Instance |
                         BindingFlags.Public |
                         BindingFlags.NonPublic))
            {
                if (typeof(Pawn).IsAssignableFrom(field.FieldType))
                {
                    Pawn pawn = field.GetValue(closure) as Pawn;
                    if (pawn != null)
                        result.Add(pawn);
                }
            }
            return result;
        }

        private static PawnRelationDef CollectRelationDef(object closure)
        {
            foreach (FieldInfo field in closure.GetType().GetFields(
                         BindingFlags.Instance |
                         BindingFlags.Public |
                         BindingFlags.NonPublic))
            {
                if (typeof(PawnRelationDef).IsAssignableFrom(field.FieldType))
                {
                    var def = field.GetValue(closure) as PawnRelationDef;
                    if (def != null)
                        return def;
                }
            }
            return null;
        }

        private static bool SameMethod(MethodInfo left, MethodInfo right)
        {
            return left != null &&
                   right != null &&
                   left.Module == right.Module &&
                   left.MetadataToken == right.MetadataToken;
        }

        private static bool SameField(FieldInfo left, FieldInfo right)
        {
            return left != null &&
                   right != null &&
                   left.Module == right.Module &&
                   left.MetadataToken == right.MetadataToken;
        }

        private static void SyncSetBio(Pawn pawn, Pawn target, int kind)
        {
            object family = GetFamilyData(pawn);
            if (family == null || target == null)
                return;

            try
            {
                if ((kind & 1) != 0)
                    _setBioMom.Invoke(
                        family,
                        new object[] { target, null, null, false, null });
                if ((kind & 2) != 0)
                    _setBioDad.Invoke(
                        family,
                        new object[] { target, null, null, false, null });
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] FamilyOverhaul SetBio replay " +
                    $"failed: {exception.Message}");
            }
        }

        private static void SyncAiMother(Pawn pawn, Pawn target)
        {
            if (pawn?.relations == null || target == null)
                return;

            PawnRelationDef def = DefDatabase<PawnRelationDef>
                .GetNamedSilentFail("FO_AIMother");
            if (def == null)
                return;

            if (!pawn.relations.DirectRelationExists(def, target))
                pawn.relations.AddDirectRelation(def, target);
            if (ModsConfig.IdeologyActive &&
                pawn.ideo != null &&
                pawn.Ideo == null &&
                target.Ideo != null)
            {
                pawn.ideo.SetIdeo(target.Ideo);
            }
        }

        private static void SyncServantLetter(Pawn first, Pawn second)
        {
            if (first == null || second == null || first == second)
                return;

            object firstFamily = GetFamilyData(first);
            object secondFamily = GetFamilyData(second);
            Pawn master;
            Pawn servant;
            bool hasServant;
            if (HasServant(firstFamily, second))
            {
                master = first;
                servant = second;
                hasServant = true;
            }
            else if (HasServant(secondFamily, first))
            {
                master = second;
                servant = first;
                hasServant = true;
            }
            else
            {
                master = first;
                servant = second;
                hasServant = false;
            }

            try
            {
                string label = "Servant status";
                string text = hasServant
                    ? $"Update servant designation for {servant.NameFullColored} " +
                      $"with {master.NameFullColored}"
                    : $"Make {servant.NameFullColored} a personal " +
                      $"{(servant.IsSlave ? "slave" : "servant")} with " +
                      $"{servant.Possessive()} new " +
                      $"{GetServantDefLabel(master)} being {master.NameFullColored}";
                LetterDef letterDef = DefDatabase<LetterDef>
                    .GetNamedSilentFail("FO_DesignateServantType");
                if (letterDef == null)
                    return;

                Letter letter = (Letter)_makeLetter.Invoke(
                    null,
                    new object[] { label, text, letterDef });
                if (letter == null ||
                    !string.Equals(
                        letter.GetType().FullName,
                        FamilyLetterTypeName,
                        StringComparison.Ordinal))
                    return;

                _letterMaster.SetValue(letter, master);
                _letterServant.SetValue(letter, servant);
                if (hasServant)
                {
                    _letterOldMaster.SetValue(letter, master);
                    _letterOldServantType.SetValue(
                        letter,
                        GetCurrentServantType(master, servant));
                }
                else
                {
                    _letterOldMaster.SetValue(letter, null);
                    _letterOldServantType.SetValue(letter, null);
                }

                int condition = servant.IsSlave ? 1 : 0;
                _letterCondition.SetValue(
                    letter,
                    Enum.ToObject(_letterConditionEnum, condition));
                _receiveLetter.Invoke(Find.LetterStack, new object[] { letter });
            }
            catch (Exception exception)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-P1] FamilyOverhaul letter replay " +
                    $"failed: {exception.Message}");
            }
        }

        private static bool HasServant(object family, Pawn servant)
        {
            if (family == null || servant == null)
                return false;
            try
            {
                return ContainsServant(family, servant);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsServant(object family, Pawn servant)
        {
            object servants = AccessTools.Field(_familyDataType, "servants")
                ?.GetValue(family);
            if (servants is System.Collections.IEnumerable enumerable)
            {
                foreach (object relation in enumerable)
                {
                    object pawn = AccessTools.Field(
                            relation.GetType(),
                            "servant")
                        ?.GetValue(relation);
                    if (pawn == servant)
                        return true;
                }
            }
            return false;
        }

        private static object GetCurrentServantType(Pawn master, Pawn servant)
        {
            object family = GetFamilyData(master);
            object servants = family == null
                ? null
                : AccessTools.Field(_familyDataType, "servants")?.GetValue(family);
            if (servants is System.Collections.IEnumerable enumerable)
            {
                foreach (object relation in enumerable)
                {
                    object pawn = AccessTools.Field(
                            relation.GetType(),
                            "servant")
                        ?.GetValue(relation);
                    if (pawn == servant)
                    {
                        return AccessTools.Field(relation.GetType(), "type")
                            ?.GetValue(relation);
                    }
                }
            }
            return null;
        }

        private static string GetServantDefLabel(Pawn master)
        {
            try
            {
                PawnRelationDef def = DefDatabase<PawnRelationDef>
                    .GetNamedSilentFail("FO_MasterServant");
                if (def == null)
                    return "master";
                MethodInfo label = AccessTools.Method(
                    typeof(PawnRelationDef),
                    "GetGenderSpecificLabel",
                    new[] { typeof(Pawn) });
                return label == null
                    ? def.label
                    : (string)label.Invoke(def, new object[] { master });
            }
            catch
            {
                return "master";
            }
        }

        private static void SyncToggleBastard(Pawn pawn)
        {
            object family = GetFamilyData(pawn);
            if (family == null || _bastardField == null)
                return;
            bool value = !(bool)_bastardField.GetValue(family);
            _bastardField.SetValue(family, value);
        }

        private static void SyncToggleBioLock(Pawn pawn, int momOrDad)
        {
            object family = GetFamilyData(pawn);
            if (family == null)
                return;
            FieldInfo field = momOrDad == 0 ? _bioMomLockField : _bioDadLockField;
            if (field == null)
                return;
            bool value = !(bool)field.GetValue(family);
            field.SetValue(family, value);
        }

        private static bool ReactToUserChangesPrefix(
            object __instance,
            Pawn liege,
            object assignment)
        {
            if (!ShouldSyncUi())
                return true;

            var comp = __instance as ThingComp;
            var concubine = comp?.parent as Pawn;
            if (concubine == null || liege == null || _syncLiegeAdd == null ||
                _syncLiegeRemove == null)
                return false;

            if (Convert.ToInt32(assignment) == 0)
                _syncLiegeAdd.DoSync(null, concubine, liege);
            else
                _syncLiegeRemove.DoSync(null, concubine, liege);
            return false;
        }

        private static bool AddLiegeRelationWithPrefix(
            object __instance,
            Pawn validLiege)
        {
            if (!ShouldSyncUi())
                return true;

            var comp = __instance as ThingComp;
            var apparel = comp?.parent as Apparel;
            var concubine = apparel?.Wearer;
            if (concubine == null || validLiege == null)
                return false;
            _syncLiegeAdd.DoSync(null, concubine, validLiege);
            return false;
        }

        private static bool RemoveLiegeRelationToPrefix(
            object __instance,
            Pawn liege)
        {
            if (!ShouldSyncUi())
                return true;

            var comp = __instance as ThingComp;
            var apparel = comp?.parent as Apparel;
            var concubine = apparel?.Wearer;
            if (concubine == null || liege == null)
                return false;
            _syncLiegeRemove.DoSync(null, concubine, liege);
            return false;
        }

        private static void SyncLiegeAdd(Pawn concubine, Pawn liege)
        {
            if (concubine == null || liege == null || _forceAddLiege == null)
                return;
            _forceAddLiege.Invoke(null, new object[] { concubine, liege, false });
        }

        private static void SyncLiegeRemove(Pawn concubine, Pawn liege)
        {
            if (concubine == null || liege == null || _tryRemoveLiege == null)
                return;
            _tryRemoveLiege.Invoke(null, new object[] { concubine, liege });
        }

        private static IEnumerable<CodeInstruction> LiegeSexTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var stable = AccessTools.Method(
                typeof(Patch_RjwP1),
                nameof(StableTryRandomElementByWeight));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(GenCollection) &&
                    method.Name == "TryRandomElementByWeight" &&
                    method.IsGenericMethod)
                {
                    instruction.operand = stable.MakeGenericMethod(
                        method.GetGenericArguments());
                }
                yield return instruction;
            }
        }

        private static IEnumerable<CodeInstruction> RandomElementTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var stable = AccessTools.Method(
                typeof(Patch_RjwP1),
                nameof(StableRandomElement));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(GenCollection) &&
                    method.Name == "RandomElement" &&
                    method.IsGenericMethod)
                {
                    Type elementType = method.GetGenericArguments()[0];
                    if (elementType.IsGenericType ||
                        typeof(Thing).IsAssignableFrom(elementType))
                    {
                        instruction.operand = stable.MakeGenericMethod(elementType);
                    }
                }
                yield return instruction;
            }
        }

        private static IEnumerable<CodeInstruction> WeightedElementTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var stableWeighted = AccessTools.Method(
                typeof(Patch_RjwP1),
                nameof(StableRandomElementByWeight));
            var stableTryWeighted = AccessTools.Method(
                typeof(Patch_RjwP1),
                nameof(StableTryRandomElementByWeight));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(GenCollection) &&
                    method.IsGenericMethod)
                {
                    Type elementType = method.GetGenericArguments()[0];
                    if (method.Name == "RandomElementByWeight" &&
                        (elementType.IsGenericType ||
                         elementType.GetProperty("xenotypeDefName") != null))
                    {
                        instruction.operand =
                            stableWeighted.MakeGenericMethod(elementType);
                    }
                    else if (method.Name == "TryRandomElementByWeight" &&
                             elementType.IsGenericType)
                    {
                        instruction.operand =
                            stableTryWeighted.MakeGenericMethod(elementType);
                    }
                }
                yield return instruction;
            }
        }

        private static T StableRandomElement<T>(IEnumerable<T> source)
        {
            return source
                .OrderBy(StableElementId)
                .RandomElement();
        }

        private static bool StableTryRandomElementByWeight<T>(
            IEnumerable<T> source,
            Func<T, float> weightSelector,
            out T result)
        {
            return source
                .OrderBy(StableElementId)
                .TryRandomElementByWeight(weightSelector, out result);
        }

        private static T StableRandomElementByWeight<T>(
            IEnumerable<T> source,
            Func<T, float> weightSelector)
        {
            return source
                .OrderBy(StableElementId)
                .RandomElementByWeight(weightSelector);
        }

        private static int StableElementId<T>(T item)
        {
            if (item == null)
                return int.MinValue;
            if (item is Thing thing)
                return thing.thingIDNumber;

            object value = item.GetType()
                .GetProperty("Key")
                ?.GetValue(item, null);
            if (value == null)
            {
                value = item.GetType()
                    .GetProperty("Target")
                    ?.GetValue(item, null);
            }
            if (value is Pawn pawn)
                return pawn.thingIDNumber;
            if (value is Thing targetThing)
                return targetThing.thingIDNumber;

            PropertyInfo xenotypeName = item.GetType()
                .GetProperty("xenotypeDefName");
            if (xenotypeName != null)
            {
                string name = xenotypeName.GetValue(item, null) as string;
                return StableStringHash(name);
            }
            return 0;
        }

        private static int StableStringHash(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }

        private static IEnumerable<CodeInstruction> DeterministicRandomTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo factory = AccessTools.Method(
                typeof(Patch_RjwP1),
                nameof(CreateDeterministicRandom));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj &&
                    instruction.operand is ConstructorInfo ctor &&
                    ctor.DeclaringType == typeof(Random) &&
                    ctor.GetParameters().Length == 0)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = factory;
                }
                yield return instruction;
            }
        }

        private static Random CreateDeterministicRandom()
        {
            return MP.IsInMultiplayer ? new Random(Rand.Int) : new Random();
        }

        private static IEnumerable<CodeInstruction> UnityRandomTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo deterministicValue = AccessTools.Method(
                typeof(Patch_RjwP1),
                nameof(GetDeterministicRandomValue));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(UnityEngine.Random) &&
                    method.Name == "get_value")
                {
                    instruction.operand = deterministicValue;
                }
                yield return instruction;
            }
        }

        private static float GetDeterministicRandomValue()
        {
            return MP.IsInMultiplayer ? Rand.Value : UnityEngine.Random.value;
        }

        private static bool HormoneTabPrefix()
        {
            return !MP.IsInMultiplayer;
        }

        private static bool CncSetIsDesignatedPrefix(
            object __instance,
            bool value)
        {
            if (!ShouldSyncUi())
                return true;

            var comp = __instance as ThingComp;
            var pawn = comp?.parent as Pawn;
            if (pawn == null || _syncSetFreeUse == null)
                return false;
            _syncSetFreeUse.DoSync(null, pawn, value);
            return false;
        }

        private static void SyncSetFreeUse(Pawn pawn, bool value)
        {
            object comp = FindComp(pawn, CncCompTypeName);
            if (comp == null || _cncSetIsDesignated == null)
                return;
            _cncSetIsDesignated.Invoke(comp, new object[] { value });
        }

        private static bool CncIsDesignatedFreeUsePrefix(
            Pawn pawn,
            ref bool __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            object comp = FindComp(pawn, CncCompTypeName);
            if (comp == null || _cncIsDesignatedField == null)
            {
                __result = false;
                return false;
            }

            bool designated = (bool)_cncIsDesignatedField.GetValue(comp);
            if (designated && _cncCanBeDesignatedFreeUse != null)
            {
                bool can = (bool)_cncCanBeDesignatedFreeUse.Invoke(
                    null,
                    new object[] { pawn });
                if (!can)
                {
                    // The original getter mutates the comp to clear an invalid
                    // designation. In multiplayer that write is local-only, so
                    // return the deterministic value without the side effect.
                    __result = false;
                    return false;
                }
            }

            __result = designated;
            return false;
        }

        private static bool UapGenitalCachePrefix(Pawn p, ref bool __result)
        {
            if (!MP.IsInMultiplayer || p == null)
                return true;

            try
            {
                ParameterInfo[] parameters = _rjwHasPenis.GetParameters();
                object[] args = parameters.Length == 1
                    ? new object[] { p }
                    : new object[] { p, null };
                __result = (bool)_rjwHasPenis.Invoke(null, args);
            }
            catch
            {
                __result = p.gender == Gender.Male;
            }
            return false;
        }
    }
}
