using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// Determinism and UI-command compatibility for source-verified RJW add-ons.
    /// Targets are discovered from exact IL operands so compiler-generated methods
    /// are covered without relying on unstable lambda ordinal names.
    /// </summary>
    internal static class Patch_RjwAddons
    {
        private const string CumpilationPackageId = "vegapnk.cumpilation";
        private const string RjwGenesPackageId = "Vegapnk.rjw.genes";
        private const string CumpilationAnchorType = "Cumpilation.Leaking.Comp_SealCum";
        private const string RjwGenesAnchorType = "RJW_Genes.Patch_GeneticSexSwap";
        private const string SexperiencePawnGenerationPatchType =
            "RJWSexperience.Patches.Rimworld_Patch_GeneratePawn";
        private const string SexperienceNymphSkillsPatchType =
            "RJWSexperience.RJW_Patch_Nymph_set_skills";
        private const string RjwExtensionFappingChanceType =
            "rjwex.ThinkNode_ChancePerHour_UsingFM";

        private static readonly ConstructorInfo ParameterlessRandomConstructor =
            AccessTools.Constructor(typeof(Random), Type.EmptyTypes);
        private static readonly MethodInfo DeterministicRandomFactory =
            AccessTools.Method(typeof(Patch_RjwAddons), nameof(CreateSystemRandom));
        private static readonly MethodInfo SetCanDeflateFromUiMethod =
            AccessTools.Method(typeof(Patch_RjwAddons), nameof(SetCanDeflateFromUi));

        private static readonly Dictionary<MethodBase, int> CumpilationToggleMethods =
            new Dictionary<MethodBase, int>();

        private static Type _cumpilationCompType;
        private static FieldInfo _cumSealedField;
        private static FieldInfo _canDeflateField;
        private static ISyncMethod _syncSetCumpilationToggle;
        private static int _randomReplacements;
        private static int _uiResetReplacements;
        private static int _testReplayCount;
        private static int _sexperienceScopeTraceCount;
        private static int _sexperienceNymphScopeTraceCount;
        private static int _rjwExtensionDebugScopeTraceCount;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            int cumpilationMethods = 0;
            int cumpilationReplacements = 0;
            int genesMethods = 0;
            int genesReplacements = 0;
            bool toggleResolved = false;

            Assembly cumpilationAssembly = FindLoadedAssembly("Cumpilation");
            Assembly genesAssembly = FindLoadedAssembly("Rjw-Genes");

            PatchSexperiencePawnGenerationRand(harmony);
            PatchSexperienceNymphSkillsRand(harmony);
            PatchRjwExtensionFappingChance(harmony);

            if (cumpilationAssembly != null)
            {
                var anchor = cumpilationAssembly.GetType(CumpilationAnchorType, false);
                if (anchor == null)
                {
                    Log.Warning("[MP-MeowOnlineShop][RJW-Addons] Cumpilation 1.2.6 anchor type was not found; add-on patch skipped.");
                }
                else
                {
                    PatchParameterlessRandomConstructors(
                        harmony,
                        anchor.Assembly,
                        out cumpilationMethods,
                        out cumpilationReplacements);
                    toggleResolved = PatchCumpilationToggles(harmony, anchor);
                }
            }

            if (genesAssembly != null)
            {
                var anchor = genesAssembly.GetType(RjwGenesAnchorType, false);
                if (anchor == null)
                {
                    Log.Warning("[MP-MeowOnlineShop][RJW-Addons] RJW Genes 2.6.1 anchor type was not found; add-on patch skipped.");
                }
                else
                {
                    PatchParameterlessRandomConstructors(
                        harmony,
                        anchor.Assembly,
                        out genesMethods,
                        out genesReplacements);
                }
            }

            int expectedCumpilationRandom = Patch_Light350LoadStateMp.HasOverflowRandomPatch ? 5 : 6;
            if (cumpilationAssembly != null &&
                (cumpilationMethods != expectedCumpilationRandom || cumpilationReplacements != expectedCumpilationRandom))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Addons] Cumpilation deterministic-Random signature drift: " +
                    $"expected methods/replacements={expectedCumpilationRandom}/{expectedCumpilationRandom}, actual={cumpilationMethods}/{cumpilationReplacements}.");
            }

            if (genesAssembly != null &&
                (genesMethods != 12 || genesReplacements != 13))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Addons] RJW Genes deterministic-Random signature drift: " +
                    $"expected methods/replacements=12/13, actual={genesMethods}/{genesReplacements}.");
            }

            if (cumpilationAssembly != null || genesAssembly != null)
            {
                Log.Message(
                    "[MP-MeowOnlineShop][RJW-Addons] target resolution complete: " +
                    $"CumpilationRandom={cumpilationMethods}/{cumpilationReplacements}, " +
                    $"CumpilationToggles={toggleResolved}, " +
                    $"RjwGenesRandom={genesMethods}/{genesReplacements}.");
            }
        }

        private static void PatchRjwExtensionFappingChance(Harmony harmony)
        {
            Type chanceType = AccessTools.TypeByName(RjwExtensionFappingChanceType);
            if (chanceType == null)
                return;

            MethodInfo target = AccessTools.Method(
                chanceType,
                "get_fappin_mtb_hours",
                new[] { typeof(Pawn) });
            if (target == null || target.ReturnType != typeof(float))
            {
                if (chanceType != null)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop][RJW-Addons] RJW Extension fuck-machine MTB signature drift; " +
                        "local debug-state isolation was not installed.");
                }
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwAddons),
                    nameof(RjwExtensionFappingChancePrefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwAddons),
                    nameof(RjwExtensionFappingChanceFinalizer))));

            Log.Message(
                "[MP-MeowOnlineShop][RJW-Addons] RJW Extension fuck-machine MTB no longer depends " +
                "on the process-local alwaysDoLovin debug flag in multiplayer.");
        }

        private static void RjwExtensionFappingChancePrefix(ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || !DebugSettings.alwaysDoLovin)
                return;

            // DebugSettings is process-local and a late-joining client does not
            // inherit the host's live debug toggles.  The target uses this flag
            // to change its MTB from the pawn-derived value to 0.1 hours.  That
            // can make only one peer enter JobGiver_UseFM, whose orientation
            // filter consumes Verse.Rand and immediately shifts the map state.
            DebugSettings.alwaysDoLovin = false;
            __state = true;

            int traceOrdinal = Interlocked.Increment(ref _rjwExtensionDebugScopeTraceCount);
            if (traceOrdinal <= 8)
            {
                TraceTest(
                    $"RJWEX_FM_DEBUG_ISOLATION tick={Find.TickManager?.TicksGame ?? -1} " +
                    $"ordinal={traceOrdinal}");
            }
        }

        private static Exception RjwExtensionFappingChanceFinalizer(
            Exception __exception,
            bool __state)
        {
            if (__state)
                DebugSettings.alwaysDoLovin = true;
            return __exception;
        }

        private static void PatchSexperiencePawnGenerationRand(Harmony harmony)
        {
            Type patchType = AccessTools.TypeByName(SexperiencePawnGenerationPatchType);
            if (patchType == null)
                return;

            MethodInfo target = AccessTools.Method(patchType, "Postfix");
            if (target == null || target.ReturnType != typeof(void))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Addons] Sexperience PawnGenerator Postfix signature drift; " +
                    "Rand isolation was not installed.");
                return;
            }

            ParameterInfo[] parameters = target.GetParameters();
            if (parameters.Length != 1 ||
                parameters[0].ParameterType != typeof(Pawn).MakeByRefType())
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Addons] Sexperience PawnGenerator Postfix parameters changed; " +
                    "Rand isolation was not installed.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwAddons),
                    nameof(SexperiencePostfixRandPrefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwAddons),
                    nameof(SexperiencePostfixRandFinalizer))));

            Log.Message(
                "[MP-MeowOnlineShop][RJW-Addons] Sexperience PawnGenerator Postfix Rand isolation active.");
        }

        private static void SexperiencePostfixRandPrefix(Pawn __0, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || __0 == null)
                return;

            int seed = Gen.HashCombineInt(0x53585052, __0.thingIDNumber);
            seed = Gen.HashCombineInt(seed, __0.def?.shortHash ?? 0);
            seed = Gen.HashCombineInt(seed, (int)__0.gender);
            Rand.PushState(seed);
            __state = true;

            int traceOrdinal = Interlocked.Increment(ref _sexperienceScopeTraceCount);
            if (traceOrdinal <= 8)
            {
                TraceTest(
                    $"SEXPERIENCE_RAND pawn={__0.thingIDNumber} def={__0.def?.defName ?? "<null>"} " +
                    $"seed={seed} ordinal={traceOrdinal}");
            }
        }

        private static Exception SexperiencePostfixRandFinalizer(
            Exception __exception,
            bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        private static void PatchSexperienceNymphSkillsRand(Harmony harmony)
        {
            Type patchType = AccessTools.TypeByName(SexperienceNymphSkillsPatchType);
            if (patchType == null)
                return;

            MethodInfo target = AccessTools.Method(patchType, "Postfix");
            ParameterInfo[] parameters = target?.GetParameters();
            if (target == null || target.ReturnType != typeof(void) ||
                parameters.Length != 1 || parameters[0].ParameterType != typeof(Pawn))
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Addons] Sexperience Nymph skill Postfix signature drift; " +
                    "Rand isolation was not installed.");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwAddons),
                    nameof(SexperienceNymphSkillsRandPrefix)))
                {
                    priority = Priority.First
                },
                finalizer: new HarmonyMethod(AccessTools.Method(
                    typeof(Patch_RjwAddons),
                    nameof(SexperiencePostfixRandFinalizer))));

            Log.Message(
                "[MP-MeowOnlineShop][RJW-Addons] Sexperience Nymph skill Postfix Rand isolation active.");
        }

        private static void SexperienceNymphSkillsRandPrefix(Pawn __0, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || __0 == null)
                return;

            int seed = Gen.HashCombineInt(0x4E594D50, __0.thingIDNumber);
            seed = Gen.HashCombineInt(seed, __0.def?.shortHash ?? 0);
            Rand.PushState(seed);
            __state = true;

            int traceOrdinal = Interlocked.Increment(ref _sexperienceNymphScopeTraceCount);
            if (traceOrdinal <= 8)
            {
                TraceTest(
                    $"SEXPERIENCE_NYMPH_RAND pawn={__0.thingIDNumber} " +
                    $"def={__0.def?.defName ?? "<null>"} seed={seed} ordinal={traceOrdinal}");
            }
        }

        private static void PatchParameterlessRandomConstructors(
            Harmony harmony,
            Assembly assembly,
            out int patchedMethods,
            out int replacements)
        {
            patchedMethods = 0;
            int before = _randomReplacements;
            var transpiler = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RjwAddons), nameof(RandomConstructorTranspiler)));

            foreach (var method in GetDeclaredMethodsAndConstructors(assembly))
            {
                if (Patch_Light350LoadStateMp.OwnsRandomConstructor(method))
                    continue;
                if (!ContainsMemberOperand(method, ParameterlessRandomConstructor, OpCodes.Newobj))
                    continue;

                try
                {
                    harmony.Patch(method, transpiler: transpiler);
                    patchedMethods++;
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop][RJW-Addons] failed to patch parameterless Random in " +
                        $"{method.DeclaringType?.FullName}.{method.Name}: {exception.Message}");
                }
            }

            replacements = _randomReplacements - before;
        }

        private static IEnumerable<CodeInstruction> RandomConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            foreach (var instruction in instructions)
            {
                var constructor = instruction.operand as ConstructorInfo;
                if (instruction.opcode == OpCodes.Newobj &&
                    constructor != null &&
                    constructor.DeclaringType == typeof(Random) &&
                    constructor.GetParameters().Length == 0)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = DeterministicRandomFactory;
                    _randomReplacements++;
                }

                yield return instruction;
            }
        }

        private static Random CreateSystemRandom()
        {
            return MP.IsInMultiplayer ? new Random(Rand.Int) : new Random();
        }

        private static bool PatchCumpilationToggles(Harmony harmony, Type compType)
        {
            _cumpilationCompType = compType;
            _cumSealedField = AccessTools.Field(compType, "cumSealed");
            _canDeflateField = AccessTools.Field(compType, "canDeflate");
            if (_cumSealedField?.FieldType != typeof(bool) ||
                _canDeflateField?.FieldType != typeof(bool))
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Addons] Cumpilation toggle fields were not found.");
                return false;
            }

            _syncSetCumpilationToggle = MP.RegisterSyncMethod(
                    typeof(Patch_RjwAddons),
                    nameof(SetCumpilationToggle))
                .CancelIfAnyArgNull()
                .SetContext(SyncContext.CurrentMap);

            var prefix = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RjwAddons), nameof(CumpilationTogglePrefix)));
            var uiResetTranspiler = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_RjwAddons), nameof(CanDeflateUiResetTranspiler)));

            MethodBase canDeflateIterator = null;
            foreach (var method in GetDeclaredMethodsAndConstructors(compType.Assembly))
            {
                if (method.DeclaringType == compType &&
                    method is MethodInfo methodInfo &&
                    methodInfo.ReturnType == typeof(void) &&
                    method.GetParameters().Length == 0)
                {
                    if (ContainsMemberOperand(method, _cumSealedField, OpCodes.Stfld))
                        CumpilationToggleMethods[method] = 0;
                    if (ContainsMemberOperand(method, _canDeflateField, OpCodes.Stfld))
                        CumpilationToggleMethods[method] = 1;
                }

                if (method.Name == "MoveNext" &&
                    method.DeclaringType?.DeclaringType == compType &&
                    ContainsMemberOperand(method, _canDeflateField, OpCodes.Stfld))
                {
                    canDeflateIterator = method;
                }
            }

            foreach (var method in CumpilationToggleMethods.Keys.ToList())
                harmony.Patch(method, prefix: prefix);

            if (canDeflateIterator != null)
                harmony.Patch(canDeflateIterator, transpiler: uiResetTranspiler);

            bool resolved =
                CumpilationToggleMethods.Values.Count(value => value == 0) == 1 &&
                CumpilationToggleMethods.Values.Count(value => value == 1) == 1 &&
                canDeflateIterator != null &&
                _uiResetReplacements == 1;

            if (!resolved)
            {
                Log.Warning(
                    "[MP-MeowOnlineShop][RJW-Addons] Cumpilation toggle signature drift: " +
                    $"toggleMethods={CumpilationToggleMethods.Count}, " +
                    $"iterator={canDeflateIterator != null}, uiResetReplacements={_uiResetReplacements}.");
            }

            return resolved;
        }

        private static bool CumpilationTogglePrefix(object __instance, MethodBase __originalMethod)
        {
            if (!MP.IsInMultiplayer)
                return true;

            if (!CumpilationToggleMethods.TryGetValue(__originalMethod, out int toggleKind))
            {
                var resolved = CumpilationToggleMethods.FirstOrDefault(pair =>
                    SameMember(pair.Key, __originalMethod));
                if (resolved.Key == null)
                    return false;
                toggleKind = resolved.Value;
            }

            var comp = __instance as ThingComp;
            var pawn = comp?.parent as Pawn;
            var field = toggleKind == 0 ? _cumSealedField : _canDeflateField;
            if (pawn == null || field == null || _syncSetCumpilationToggle == null)
            {
                Log.Warning("[MP-MeowOnlineShop][RJW-Addons] blocked a local-only Cumpilation toggle because its pawn context was unavailable.");
                return false;
            }

            bool desiredValue = !(bool)field.GetValue(__instance);
            TraceTest(
                $"SEND pawn={pawn.thingIDNumber} kind={toggleKind} desired={desiredValue} " +
                $"method={__originalMethod.Name}");
            _syncSetCumpilationToggle.DoSync(null, pawn, toggleKind, desiredValue);
            return false;
        }

        private static void SetCumpilationToggle(Pawn pawn, int toggleKind, bool value)
        {
            if (pawn == null || _cumpilationCompType == null)
                return;

            var comp = pawn.AllComps?.FirstOrDefault(candidate =>
                candidate != null && _cumpilationCompType.IsInstanceOfType(candidate));
            var field = toggleKind == 0 ? _cumSealedField :
                toggleKind == 1 ? _canDeflateField : null;
            if (comp != null && field != null)
            {
                field.SetValue(comp, value);
                _testReplayCount++;
                TraceTest(
                    $"REPLAY pawn={pawn.thingIDNumber} kind={toggleKind} value={value} " +
                    $"applied={field.GetValue(comp)}");
            }
            else
            {
                TraceTest(
                    $"REPLAY_MISSING pawn={pawn.thingIDNumber} kind={toggleKind} " +
                    $"comp={comp != null} field={field != null}");
            }
        }

        private static IEnumerable<CodeInstruction> CanDeflateUiResetTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                var field = instruction.operand as FieldInfo;
                if (instruction.opcode == OpCodes.Stfld && field == _canDeflateField)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = SetCanDeflateFromUiMethod;
                    _uiResetReplacements++;
                }

                yield return instruction;
            }
        }

        private static void SetCanDeflateFromUi(object comp, bool value)
        {
            if (!MP.IsInMultiplayer && comp != null)
                _canDeflateField?.SetValue(comp, value);
        }

        private static IEnumerable<MethodBase> GetDeclaredMethodsAndConstructors(Assembly assembly)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Instance |
                                           BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var constructor in type.GetConstructors(flags))
                    if (constructor.GetMethodBody() != null)
                        yield return constructor;
                foreach (var method in type.GetMethods(flags))
                    if (!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null)
                        yield return method;
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
        }

        private static Assembly FindLoadedAssembly(string simpleName)
        {
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    simpleName,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void TraceTest(string message)
        {
            string enabledText;
            bool enabled =
                GenCommandLine.TryGetCommandLineArg("mpautotestrjwaddons", out enabledText) &&
                bool.TryParse(enabledText, out var parsed) &&
                parsed;
            if (enabled)
                Log.Message("[MP-MeowOnlineShop][RJW-Addons][test] " + message);
        }

        private static int GetTestReplayCount()
        {
            return _testReplayCount;
        }

        private static bool ContainsMemberOperand(MethodBase method, MemberInfo expected, OpCode opcode)
        {
            var body = method.GetMethodBody();
            var bytes = body?.GetILAsByteArray();
            if (bytes == null || expected == null)
                return false;

            byte opcodeByte = (byte)opcode.Value;
            for (int index = 0; index + 4 < bytes.Length; index++)
            {
                if (bytes[index] != opcodeByte)
                    continue;

                int token = BitConverter.ToInt32(bytes, index + 1);
                try
                {
                    MemberInfo resolved;
                    if (opcode == OpCodes.Stfld)
                        resolved = method.Module.ResolveField(token, GetTypeArguments(method), GetMethodArguments(method));
                    else
                        resolved = method.Module.ResolveMethod(token, GetTypeArguments(method), GetMethodArguments(method));

                    if (SameMember(resolved, expected))
                        return true;
                }
                catch
                {
                    // The byte can be part of another operand. Continue scanning.
                }
            }

            return false;
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

        private static bool SameMember(MemberInfo left, MemberInfo right)
        {
            if (left == null || right == null)
                return false;
            if (left.Module == right.Module && left.MetadataToken == right.MetadataToken)
                return true;

            var leftConstructor = left as ConstructorInfo;
            var rightConstructor = right as ConstructorInfo;
            return leftConstructor != null && rightConstructor != null &&
                   leftConstructor.DeclaringType == rightConstructor.DeclaringType &&
                   leftConstructor.GetParameters().Length == rightConstructor.GetParameters().Length;
        }
    }
}
