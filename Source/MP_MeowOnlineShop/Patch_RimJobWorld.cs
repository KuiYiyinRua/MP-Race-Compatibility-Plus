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
    /// RimJobWorld 6.1.x multiplayer compatibility:
    /// - syncs the one social float-menu callback which bypasses RJW's own synced helper;
    /// - replaces RJW's non-serializable SexInteractionResolved UI command argument;
    /// - syncs the custom bondage-gear job-order action;
    /// - stabilizes unordered random target selection;
    /// - seeds the System.Random used by baby trait inheritance from Verse.Rand.
    ///
    /// RJW remains optional. Every target is resolved by its verified 1.6 runtime signature.
    /// </summary>
    internal static class Patch_RimJobWorld
    {
        internal const string PackageId = "rim.job.world";
        private const string SocialTypeName = "rjw.RMB_Socialize";
        private const string SocialClosureName = "<>c__DisplayClass3_0";
        private const string DeepTalkCallbackName = "<GenerateSocialOptions>b__3";
        private const string RmbMenuTypeName = "rjw.RMB.RMB_Menu";
        private const string RmbMenuClosureName = "<>c__DisplayClass9_1";
        private const string RmbMenuOuterClosureName = "<>c__DisplayClass9_0";
        private const string RmbMenuCallbackName = "<GenerateNonSoloSexRoleOptions>b__0";
        private const string MasturbateTypeName = "rjw.RMB.RMB_Masturbate";
        private const string MasturbateClosureName = "<>c__DisplayClass5_1";
        private const string MasturbateOuterClosureName = "<>c__DisplayClass5_0";
        private const string MasturbateCallbackName = "<GenerateSoloSexPoseOptions>b__0";
        private const string SexInteractionResolvedTypeName =
            "rjw.Modules.Interactions.SexInteractionResolved";
        private const string SexAppraiserTypeName = "rjw.SexAppraiser";
        private const string CasualSexHelperTypeName = "rjw.CasualSex_Helper";
        private const string BondageExtensionsTypeName = "rjw.bondage_gear_extensions";
        private const string AttractionUtilityTypeName =
            "rjw.Modules.Attraction.AttractionUtility";
        private const string PregnancyTypeName = "rjw.Hediff_BasePregnancy";
        private const string DnaParentTypeName = "DnaGivingParent";
        private const string GenerateBabiesMethodName = "GenerateBabies";

        private static readonly MethodInfo DeterministicRandomFactory =
            AccessTools.Method(typeof(Patch_RimJobWorld), nameof(CreateDeterministicSystemRandom));

        private static bool _deepTalkResolved;
        private static bool _earlyTypeEnumerationApplied;
        private static bool _haveSexResolved;
        private static bool _bondageJobOrderResolved;
        private static bool _attractionResolved;
        private static bool _deterministicSelectionResolved;
        private static bool _pregnancyResolved;
        private static bool _randomConstructorReplaced;
        private static int _weightedSelectionReplacements;
        private static int _targetSelectionReplacements;
        private static int _casualPartnerListReplacements;

        private static MethodInfo _originalHaveSex;
        private static MethodInfo _originalBondageStartJob;
        private static FieldInfo _nonSoloDefField;
        private static FieldInfo _nonSoloOuterField;
        private static FieldInfo _nonSoloPawnField;
        private static FieldInfo _nonSoloJobField;
        private static FieldInfo _nonSoloTargetField;
        private static FieldInfo _soloDefField;
        private static FieldInfo _soloOuterField;
        private static FieldInfo _soloPawnField;
        private static FieldInfo _soloTargetField;
        private static JobDef _masturbateJobDef;
        private static List<Type> _safeAllTypes;

        internal static void ApplyEarly()
        {
            if (_earlyTypeEnumerationApplied || !ModsConfig.IsActive(PackageId))
                return;

            var allTypesGetter = AccessTools.PropertyGetter(
                typeof(GenTypes),
                nameof(GenTypes.AllTypes));
            if (allTypesGetter == null || allTypesGetter.ReturnType != typeof(List<Type>))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] early GenTypes.AllTypes getter signature not found; " +
                    "RJW attraction startup guard was not applied.");
                return;
            }

            var prefix = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RimJobWorld), nameof(SafeAllTypesPrefix)))
            {
                priority = Priority.First
            };
            new Harmony("mp.meowonlineshop.rjw.safetypes").Patch(allTypesGetter, prefix: prefix);
            _earlyTypeEnumerationApplied = true;
            Log.Message(
                "[MP-MeowOnlineShop][RJW] installed early safe type-enumeration guard " +
                "for RJW attraction initialization.");
        }

        private static bool SafeAllTypesPrefix(ref List<Type> __result)
        {
            if (_safeAllTypes == null)
            {
                var types = new List<Type>();
                int skippedAssemblies = 0;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        types.AddRange(assembly.GetTypes());
                    }
                    catch (ReflectionTypeLoadException exception)
                    {
                        // A Type returned by ReflectionTypeLoadException.Types can still
                        // throw later from IsAssignableFrom/GetMethods when one of its
                        // fields or signatures references a removed dependency. Exclude
                        // the whole partial assembly from GenTypes discovery.
                        skippedAssemblies++;
                        Log.Warning(
                            "[MP-MeowOnlineShop][RJW] excluded partially unloadable assembly " +
                            $"{assembly.GetName().Name} from safe type enumeration " +
                            $"({exception.LoaderExceptions?.Length ?? 0} loader errors).");
                    }
                    catch (Exception exception)
                    {
                        skippedAssemblies++;
                        Log.Warning(
                            "[MP-MeowOnlineShop][RJW] skipped unreadable assembly during " +
                            $"safe type enumeration: {assembly.FullName}: {exception.Message}");
                    }
                }

                _safeAllTypes = types;
                Log.Message(
                    "[MP-MeowOnlineShop][RJW] safe type enumeration cached " +
                    $"{_safeAllTypes.Count} types; excludedAssemblies={skippedAssemblies}.");
            }

            __result = _safeAllTypes;
            return false;
        }

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            var multiplayerType = AccessTools.TypeByName("rjw.RJW_Multiplayer");
            if (!ModsConfig.IsActive(PackageId) || multiplayerType == null)
            {
                Log.Message("[MP-MeowOnlineShop][RJW] rim.job.world is not active; compatibility patch skipped.");
                return;
            }

            string version = multiplayerType.Assembly.GetName().Version?.ToString() ?? "unknown";
            RegisterDeepTalkCallback(harmony);
            PatchHaveSexCallbacks(harmony);
            RegisterBondageJobOrder(harmony);
            PatchDeterministicSelections(harmony);
            PatchPregnancyRandom(harmony);
            VerifyAttractionInitialization();

            Log.Message(
                "[MP-MeowOnlineShop][RJW] target resolution complete: " +
                $"assembly={multiplayerType.Assembly.GetName().Name} {version}, " +
                $"deepTalk={_deepTalkResolved}, haveSex={_haveSexResolved}, " +
                $"bondageJobOrder={_bondageJobOrderResolved}, " +
                $"deterministicSelection={_deterministicSelectionResolved}, " +
                $"pregnancy={_pregnancyResolved}, " +
                $"deterministicRandom={_randomConstructorReplaced}, " +
                $"attraction={_attractionResolved}.");
        }

        private static void RegisterDeepTalkCallback(Harmony harmony)
        {
            var socialType = AccessTools.TypeByName(SocialTypeName);
            var closureType = socialType?.GetNestedType(SocialClosureName, BindingFlags.NonPublic);
            var callback = closureType?.GetMethod(
                DeepTalkCallbackName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            var pawnField = closureType?.GetField("pawn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var targetField = closureType?.GetField("target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (socialType == null || closureType == null || callback == null ||
                pawnField?.FieldType != typeof(Pawn) ||
                targetField?.FieldType != typeof(LocalTargetInfo))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] deep-talk callback signature not found; " +
                    "expected rjw.RMB_Socialize+<>c__DisplayClass3_0::<GenerateSocialOptions>b__3() " +
                    "with fields Pawn pawn and LocalTargetInfo target.");
                return;
            }

            MP.RegisterSyncDelegate(
                    socialType,
                    SocialClosureName,
                    DeepTalkCallbackName,
                    new[] { "pawn", "target" },
                    Type.EmptyTypes)
                .CancelIfAnyFieldNull()
                .SetContext(SyncContext.CurrentMap);

            var tracePrefix = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RimJobWorld), nameof(DeepTalkReplayPrefix)));
            harmony.Patch(callback, prefix: tracePrefix);
            _deepTalkResolved = true;
        }

        private static void PatchHaveSexCallbacks(Harmony harmony)
        {
            const BindingFlags instanceFields =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var menuType = AccessTools.TypeByName(RmbMenuTypeName);
            var resolvedType = AccessTools.TypeByName(SexInteractionResolvedTypeName);
            var nonSoloClosure = menuType?.GetNestedType(RmbMenuClosureName, BindingFlags.NonPublic);
            var nonSoloOuter = menuType?.GetNestedType(RmbMenuOuterClosureName, BindingFlags.NonPublic);
            var nonSoloCallback = nonSoloClosure?.GetMethod(
                RmbMenuCallbackName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            var masturbateType = AccessTools.TypeByName(MasturbateTypeName);
            var soloClosure = masturbateType?.GetNestedType(MasturbateClosureName, BindingFlags.NonPublic);
            var soloOuter = masturbateType?.GetNestedType(MasturbateOuterClosureName, BindingFlags.NonPublic);
            var soloCallback = soloClosure?.GetMethod(
                MasturbateCallbackName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);

            _nonSoloDefField = nonSoloClosure?.GetField("d", instanceFields);
            _nonSoloOuterField = nonSoloClosure?.GetField("CS$<>8__locals1", instanceFields);
            _nonSoloPawnField = nonSoloOuter?.GetField("pawn", instanceFields);
            _nonSoloJobField = nonSoloOuter?.GetField("job", instanceFields);
            _nonSoloTargetField = nonSoloOuter?.GetField("target", instanceFields);
            _soloDefField = soloClosure?.GetField("d", instanceFields);
            _soloOuterField = soloClosure?.GetField("CS$<>8__locals1", instanceFields);
            _soloPawnField = soloOuter?.GetField("pawn", instanceFields);
            _soloTargetField = soloOuter?.GetField("target", instanceFields);

            _originalHaveSex = menuType == null || resolvedType == null
                ? null
                : AccessTools.Method(
                    menuType,
                    "HaveSex",
                    new[] { typeof(Pawn), typeof(JobDef), typeof(LocalTargetInfo), typeof(InteractionDef), resolvedType });

            var xxxType = AccessTools.TypeByName("rjw.xxx");
            var masturbateField = xxxType?.GetField(
                "Masturbate",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            _masturbateJobDef = masturbateField?.GetValue(null) as JobDef;

            bool exactSignature =
                nonSoloCallback != null &&
                soloCallback != null &&
                _originalHaveSex != null &&
                _nonSoloDefField?.FieldType == typeof(InteractionDef) &&
                _nonSoloOuterField?.FieldType == nonSoloOuter &&
                _nonSoloPawnField?.FieldType == typeof(Pawn) &&
                _nonSoloJobField?.FieldType == typeof(JobDef) &&
                _nonSoloTargetField?.FieldType == typeof(LocalTargetInfo) &&
                _soloDefField?.FieldType == typeof(InteractionDef) &&
                _soloOuterField?.FieldType == soloOuter &&
                _soloPawnField?.FieldType == typeof(Pawn) &&
                _soloTargetField?.FieldType == typeof(LocalTargetInfo) &&
                _masturbateJobDef != null;

            if (!exactSignature)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] sex RMB callback signatures not found; " +
                    "expected the RimWorld 1.6 RJW 6.1.x non-solo and masturbation closures.");
                return;
            }

            MP.RegisterSyncMethod(typeof(Patch_RimJobWorld), nameof(ExecuteHaveSex))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            var nonSoloPrefix = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RimJobWorld), nameof(NonSoloHaveSexPrefix)));
            var soloPrefix = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RimJobWorld), nameof(SoloHaveSexPrefix)));
            harmony.Patch(nonSoloCallback, prefix: nonSoloPrefix);
            harmony.Patch(soloCallback, prefix: soloPrefix);
            _haveSexResolved = true;
        }

        private static void PatchDeterministicSelections(Harmony harmony)
        {
            var sexAppraiser = AccessTools.TypeByName(SexAppraiserTypeName);
            var rngType = sexAppraiser?.GetNestedType(
                "RNG",
                BindingFlags.Public | BindingFlags.NonPublic);
            var iteratorTypes = rngType?.GetNestedTypes(BindingFlags.NonPublic) ?? Type.EmptyTypes;
            var findResultsMoveNext = ResolveIteratorMoveNext(iteratorTypes, "<FindResults>d__");
            var findBiasedMoveNext = ResolveIteratorMoveNext(iteratorTypes, "<FindBiasedResults>d__");
            var casualSexHelper = AccessTools.TypeByName(CasualSexHelperTypeName);
            var casualFindPartner = casualSexHelper == null
                ? null
                : AccessTools.Method(
                    casualSexHelper,
                    "FindPartner",
                    new[] { typeof(Pawn), typeof(bool) });

            var violateCorpseType = AccessTools.TypeByName("rjw.JobGiver_ViolateCorpse");
            var aiRapePrisonerType = AccessTools.TypeByName("rjw.JobGiver_AIRapePrisoner");
            var rapeEnemyType = AccessTools.TypeByName("rjw.JobDriver_RapeEnemy");
            var violateCorpse = violateCorpseType == null
                ? null
                : AccessTools.Method(
                    violateCorpseType,
                    "find_corpse",
                    new[] { typeof(Pawn), typeof(Map) });
            var aiRapePrisoner = aiRapePrisonerType == null
                ? null
                : AccessTools.Method(
                    aiRapePrisonerType,
                    "find_victim",
                    new[] { typeof(Pawn), typeof(Map) });
            var rapeEnemy = rapeEnemyType == null
                ? null
                : AccessTools.Method(
                    rapeEnemyType,
                    "FindVictim",
                    new[] { typeof(Pawn), typeof(Map) });

            if (findResultsMoveNext == null || findBiasedMoveNext == null ||
                casualFindPartner == null || casualFindPartner.ReturnType != typeof(Pawn) ||
                violateCorpse == null || aiRapePrisoner == null || rapeEnemy == null)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] deterministic target-selection signatures not found; " +
                    "expected CasualSex_Helper.FindPartner, SexAppraiser RNG iterators, " +
                    "and three rape/necro target selectors.");
                return;
            }

            _weightedSelectionReplacements = 0;
            _targetSelectionReplacements = 0;
            _casualPartnerListReplacements = 0;

            var weightedTranspiler = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RimJobWorld), nameof(WeightedSelectionTranspiler)));
            harmony.Patch(findResultsMoveNext, transpiler: weightedTranspiler);
            harmony.Patch(findBiasedMoveNext, transpiler: weightedTranspiler);
            harmony.Patch(
                casualFindPartner,
                transpiler: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RimJobWorld),
                        nameof(CasualPartnerListTranspiler))));

            var targetTranspiler = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RimJobWorld), nameof(TargetSelectionTranspiler)));
            harmony.Patch(violateCorpse, transpiler: targetTranspiler);
            harmony.Patch(aiRapePrisoner, transpiler: targetTranspiler);
            harmony.Patch(rapeEnemy, transpiler: targetTranspiler);

            _deterministicSelectionResolved =
                _weightedSelectionReplacements == 3 &&
                _targetSelectionReplacements == 3 &&
                _casualPartnerListReplacements == 3;

            if (_deterministicSelectionResolved)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][RJW] patched deterministic target selection: " +
                    "3 casual-partner lists, 3 HashSet weighted draws, and 3 Dictionary " +
                    "target draws now use stable Thing IDs.");
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] deterministic target-selection patch expected " +
                    $"3 casual lists, 3 weighted draws, and 3 ordinary draws; found " +
                    $"{_casualPartnerListReplacements}, {_weightedSelectionReplacements}, " +
                    $"and {_targetSelectionReplacements}.");
            }
        }

        private static void RegisterBondageJobOrder(Harmony harmony)
        {
            var extensionsType = AccessTools.TypeByName(BondageExtensionsTypeName);
            var startJob = extensionsType == null
                ? null
                : AccessTools.Method(
                    extensionsType,
                    "start_job",
                    new[] { typeof(CompUsable), typeof(Pawn), typeof(LocalTargetInfo) });
            var makeOption = extensionsType == null
                ? null
                : AccessTools.Method(
                    extensionsType,
                    "make_option",
                    new[]
                    {
                        typeof(CompUsable),
                        typeof(string),
                        typeof(Pawn),
                        typeof(LocalTargetInfo),
                        typeof(WorkTypeDef)
                    });

            if (startJob == null || startJob.ReturnType != typeof(void) || !startJob.IsStatic ||
                makeOption == null || makeOption.ReturnType != typeof(FloatMenuOption) ||
                !makeOption.IsStatic)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] bondage job-order signatures not found; " +
                    "expected rjw.bondage_gear_extensions::start_job" +
                    "(CompUsable, Pawn, LocalTargetInfo) and make_option" +
                    "(CompUsable, String, Pawn, LocalTargetInfo, WorkTypeDef).");
                return;
            }

            _originalBondageStartJob = startJob;
            MP.RegisterSyncMethod(typeof(Patch_RimJobWorld), nameof(ExecuteBondageJob))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            harmony.Patch(
                makeOption,
                postfix: new HarmonyMethod(
                    AccessTools.Method(
                        typeof(Patch_RimJobWorld),
                        nameof(BondageMakeOptionPostfix))));
            _bondageJobOrderResolved = true;
        }

        private static void BondageMakeOptionPostfix(
            CompUsable usa,
            Pawn p,
            LocalTargetInfo tar,
            ref FloatMenuOption __result)
        {
            if (!MP.IsInMultiplayer || __result?.action == null ||
                usa?.parent == null || p == null)
                return;

            Thing item = usa.parent;
            __result.action = () => ExecuteBondageJob(item, p, tar);
        }

        private static void ExecuteBondageJob(
            Thing item,
            Pawn pawn,
            LocalTargetInfo target)
        {
            var usable = item?.TryGetComp<CompUsable>();
            if (usable == null || pawn == null)
                return;

            if (Prefs.DevMode && MP.IsExecutingSyncCommand)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][RJW][debug] replaying bondage job action " +
                    $"pawn={pawn.thingIDNumber}, item={item.thingIDNumber}.");
            }

            _originalBondageStartJob.Invoke(
                null,
                new object[] { usable, pawn, target });
        }

        private static MethodInfo ResolveIteratorMoveNext(IEnumerable<Type> iteratorTypes, string namePrefix)
        {
            var matches = iteratorTypes
                .Where(type => type.Name.StartsWith(namePrefix, StringComparison.Ordinal))
                .Select(type => AccessTools.Method(type, "MoveNext", Type.EmptyTypes))
                .Where(method => method != null)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static IEnumerable<CodeInstruction> WeightedSelectionTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var stableWeightedDefinition = AccessTools.Method(
                typeof(Patch_RimJobWorld),
                nameof(StableRandomElementByWeight)).GetGenericMethodDefinition();

            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo called &&
                    called.Name == "RandomElementByWeight" &&
                    called.IsGenericMethod)
                {
                    instruction.operand = stableWeightedDefinition.MakeGenericMethod(
                        called.GetGenericArguments());
                    _weightedSelectionReplacements++;
                }

                yield return instruction;
            }
        }

        private static IEnumerable<CodeInstruction> TargetSelectionTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var stableRandomDefinition = AccessTools.Method(
                typeof(Patch_RimJobWorld),
                nameof(StableRandomElement)).GetGenericMethodDefinition();

            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo called &&
                    called.Name == "RandomElement" &&
                    called.IsGenericMethod)
                {
                    instruction.operand = stableRandomDefinition.MakeGenericMethod(
                        called.GetGenericArguments());
                    _targetSelectionReplacements++;
                }

                yield return instruction;
            }
        }

        private static IEnumerable<CodeInstruction> CasualPartnerListTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            var stablePawnList = AccessTools.Method(
                typeof(Patch_RimJobWorld),
                nameof(ToStablePawnList));

            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call &&
                    instruction.operand is MethodInfo called &&
                    called.DeclaringType == typeof(Enumerable) &&
                    called.Name == nameof(Enumerable.ToList) &&
                    called.IsGenericMethod &&
                    called.GetGenericArguments().Length == 1 &&
                    called.GetGenericArguments()[0] == typeof(Pawn))
                {
                    instruction.operand = stablePawnList;
                    _casualPartnerListReplacements++;
                }

                yield return instruction;
            }
        }

        private static List<Pawn> ToStablePawnList(IEnumerable<Pawn> source)
        {
            return source
                .OrderBy(pawn => pawn?.thingIDNumber ?? int.MinValue)
                .ToList();
        }

        private static T StableRandomElement<T>(IEnumerable<T> source)
        {
            return source
                .OrderBy(item => StableThingId(item, "Key"))
                .ThenBy(item => StableThingId(item, "Value"))
                .RandomElement();
        }

        private static T StableRandomElementByWeight<T>(
            IEnumerable<T> source,
            Func<T, float> weightSelector)
        {
            return source
                .OrderBy(item => StableThingId(item, "Observer"))
                .ThenBy(item => StableThingId(item, "Target"))
                .RandomElementByWeight(weightSelector);
        }

        private static int StableThingId<T>(T item, string memberName)
        {
            if (item == null)
                return int.MinValue;

            object value = AccessTools.Property(item.GetType(), memberName)?.GetValue(item, null)
                ?? AccessTools.Field(item.GetType(), memberName)?.GetValue(item);
            return value is Thing thing ? thing.thingIDNumber : int.MinValue;
        }

        private static bool NonSoloHaveSexPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer)
                return true;

            object outer = _nonSoloOuterField.GetValue(__instance);
            ExecuteHaveSex(
                (Pawn)_nonSoloPawnField.GetValue(outer),
                (JobDef)_nonSoloJobField.GetValue(outer),
                (LocalTargetInfo)_nonSoloTargetField.GetValue(outer),
                (InteractionDef)_nonSoloDefField.GetValue(__instance));
            return false;
        }

        private static bool SoloHaveSexPrefix(object __instance)
        {
            if (!MP.IsInMultiplayer)
                return true;

            object outer = _soloOuterField.GetValue(__instance);
            ExecuteHaveSex(
                (Pawn)_soloPawnField.GetValue(outer),
                _masturbateJobDef,
                (LocalTargetInfo)_soloTargetField.GetValue(outer),
                (InteractionDef)_soloDefField.GetValue(__instance));
            return false;
        }

        private static void ExecuteHaveSex(
            Pawn pawn,
            JobDef jobDef,
            LocalTargetInfo target,
            InteractionDef interactionDef)
        {
            if (Prefs.DevMode && MP.IsExecutingSyncCommand)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][RJW][debug] replaying sex RMB action " +
                    $"pawn={pawn?.thingIDNumber}, job={jobDef?.defName}, " +
                    $"interaction={interactionDef?.defName}.");
            }

            _originalHaveSex.Invoke(
                null,
                new object[] { pawn, jobDef, target, interactionDef, null });
        }

        private static void PatchPregnancyRandom(Harmony harmony)
        {
            var pregnancyType = AccessTools.TypeByName(PregnancyTypeName);
            var dnaParentType = pregnancyType?.GetNestedType(
                DnaParentTypeName,
                BindingFlags.Public | BindingFlags.NonPublic);
            var generateBabies = dnaParentType == null
                ? null
                : AccessTools.Method(pregnancyType, GenerateBabiesMethodName, new[] { dnaParentType });

            if (pregnancyType == null || dnaParentType == null || generateBabies == null ||
                generateBabies.ReturnType != typeof(void))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] pregnancy target signature not found; " +
                    "expected rjw.Hediff_BasePregnancy::GenerateBabies(DnaGivingParent).");
                return;
            }

            var transpiler = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RimJobWorld), nameof(GenerateBabiesTranspiler)));
            harmony.Patch(generateBabies, transpiler: transpiler);
            _pregnancyResolved = true;
        }

        private static void VerifyAttractionInitialization()
        {
            var attractionType = AccessTools.TypeByName(AttractionUtilityTypeName);
            var countGetter = attractionType == null
                ? null
                : AccessTools.PropertyGetter(attractionType, "StandardApplicatorsCount");

            if (countGetter == null || countGetter.ReturnType != typeof(int) ||
                !countGetter.IsStatic)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] attraction verification signature not found; " +
                    "expected static int AttractionUtility.StandardApplicatorsCount.");
                return;
            }

            try
            {
                int applicatorCount = (int)countGetter.Invoke(null, null);
                _attractionResolved = applicatorCount > 0;
                if (_attractionResolved)
                {
                    Log.Message(
                        "[MP-MeowOnlineShop][RJW] attraction initialization verified: " +
                        $"standardApplicators={applicatorCount}.");
                }
                else
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop][RJW] attraction initialization returned no " +
                        "standard preference applicators.");
                }
            }
            catch (Exception exception)
            {
                Log.Error(
                    "[MP-MeowOnlineShop][RJW] attraction initialization verification failed: " +
                    exception);
            }
        }

        private static IEnumerable<CodeInstruction> GenerateBabiesTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            int replacements = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Newobj &&
                    instruction.operand is ConstructorInfo constructor &&
                    constructor.DeclaringType == typeof(Random) &&
                    constructor.GetParameters().Length == 0)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = DeterministicRandomFactory;
                    replacements++;
                }

                yield return instruction;
            }

            _randomConstructorReplaced = replacements == 1;
            if (replacements == 1)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][RJW] patched GenerateBabies: " +
                    "System.Random() now receives a deterministic Verse.Rand seed.");
            }
            else
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW] GenerateBabies random patch expected one " +
                    $"parameterless System.Random constructor, found {replacements}.");
            }
        }

        private static Random CreateDeterministicSystemRandom()
        {
            int seed = Rand.Int;
            if (Prefs.DevMode && MP.IsInMultiplayer)
                Log.Message($"[MP-MeowOnlineShop][RJW][debug] GenerateBabies deterministic trait seed={seed}.");
            return new Random(seed);
        }

        private static void DeepTalkReplayPrefix()
        {
            if (Prefs.DevMode && MP.IsExecutingSyncCommand)
                Log.Message("[MP-MeowOnlineShop][RJW][debug] replaying synced RMB deep-talk action.");
        }
    }
}
