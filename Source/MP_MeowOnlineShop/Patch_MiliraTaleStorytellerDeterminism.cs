using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    /// <summary>
    /// The Tale of Milira adds two quest storyteller comps to BaseStoryteller:
    /// `StorytellerComp_TaleOfMilira` and `StorytellerComp_TaleOfMilira_Milira`.
    /// Their `GenerateParms` runs every quest script's `CanRun` inside the
    /// synchronized storyteller tick. Quest `CanRun`/`TestRun` consumes world
    /// Rand through nodes such as `QuestNode_GetMap.TryFindMap`, and Desync-112
    /// shows the host executing that quest-selection path at world tick 973322
    /// while the client is still in vanilla comps (ShipChunkDrop/CategoryMTB)
    /// from the same world Rand state. The quests triggered by the user are the
    /// comp's own "神秘信号" (TheChurchAndMilira) and "米莉拉紧急求救信号"
    /// (TheMiliraSOSSignal) quests.
    ///
    /// Multiplayer requires identical world Rand consumption per tick. Isolate
    /// the entire `GenerateParms` body in a deterministic local scope seeded
    /// from the shared incident target seed and world tick, so quest
    /// availability probing and quest-map selection can never advance the
    /// synchronized stream on only one peer. Singleplayer is untouched.
    /// </summary>
    internal static class Patch_MiliraTaleStorytellerDeterminism
    {
        private const int MiliraTaleWorldSeedOffset = 0x4D54414C;
        private const int CompTypeSalt = 0x54414C45;

        private static readonly string[] CompTypeNames =
        {
            "TheTaleofMilira.StorytellerComp_TaleOfMilira",
            "TheTaleofMilira.StorytellerComp_TaleOfMilira_Milira"
        };

        [ThreadStatic]
        private static Map _mapForRandPop;

        private static bool _loggedActive;

        internal static void Apply(Harmony harmony)
        {
            if (!MP.enabled || harmony == null)
                return;

            int patched = 0;
            for (int i = 0; i < CompTypeNames.Length; i++)
            {
                try
                {
                    Type compType = AccessTools.TypeByName(CompTypeNames[i]);
                    MethodInfo generateParms = compType == null
                        ? null
                        : AccessTools.Method(
                            compType,
                            "GenerateParms",
                            new[] { typeof(IncidentCategoryDef), typeof(IIncidentTarget) });
                    MethodInfo prefix = AccessTools.Method(
                        typeof(Patch_MiliraTaleStorytellerDeterminism),
                        nameof(GenerateParmsPrefix));
                    MethodInfo finalizer = AccessTools.Method(
                        typeof(Patch_MiliraTaleStorytellerDeterminism),
                        nameof(GenerateParmsFinalizer));

                    if (generateParms == null || prefix == null || finalizer == null)
                        continue;

                    harmony.Patch(
                        generateParms,
                        prefix: new HarmonyMethod(prefix)
                        {
                            priority = Priority.First
                        },
                        finalizer: new HarmonyMethod(finalizer)
                        {
                            priority = Priority.Last
                        });
                    patched++;
                }
                catch (Exception e)
                {
                    Log.Warning(
                        "[MP-MeowOnlineShop] Milira Tale storyteller guard failed on " +
                        $"{CompTypeNames[i]}: {e.Message}");
                }
            }

            if (patched == 0)
            {
                Log.Message(
                    "[MP-MeowOnlineShop] Milira Tale storyteller determinism not applied " +
                    "(comp types not active).");
                return;
            }

            Log.Message(
                "[MP-MeowOnlineShop] Milira Tale storyteller quest Rand isolation active: " +
                $"comps patched={patched}/{CompTypeNames.Length}.");
        }

        private static void GenerateParmsPrefix(
            object __instance,
            IIncidentTarget target,
            ref int __state)
        {
            __state = 0;
            _mapForRandPop = null;
            if (!MP.IsInMultiplayer || __instance == null || target == null)
                return;

            Map map = target as Map;
            int seed = Gen.HashCombineInt(CompTypeSalt, target.ConstantRandSeed);
            seed = Gen.HashCombineInt(seed, Find.TickManager?.TicksGame ?? 0);

            if (DeterministicRandScope.Begin(
                    map,
                    seed,
                    MiliraTaleWorldSeedOffset,
                    ref __state,
                    out Map mapForPop))
            {
                _mapForRandPop = mapForPop;
                if (!_loggedActive)
                {
                    _loggedActive = true;
                    Log.Message(
                        "[MP-MeowOnlineShop] Milira Tale quest CanRun/selection Rand isolated " +
                        $"from the synchronized stream; targetSeed={target.ConstantRandSeed}.");
                }
            }
            else
            {
                __state = 0;
            }
        }

        private static Exception GenerateParmsFinalizer(Exception __exception, int __state)
        {
            if (__state != 0)
                DeterministicRandScope.End(__state, _mapForRandPop);
            _mapForRandPop = null;
            return __exception;
        }
    }
}
