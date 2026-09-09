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
    /// Facial Animation resets cosmetic animation timers from simulation ticks
    /// and consumes Verse.Rand. Initialization can happen on different ticks on
    /// different peers, so use a local deterministic offset in multiplayer.
    /// </summary>
    internal static class Patch_FacialAnimationMp
    {
        private const string AssemblyName = "FacialAnimation";
        private const string AnimationTypeName = "FacialAnimation.FaceAnimation";
        private const string HelperTypeName = "FacialAnimation.FAHelper";
        private const string DrawFaceGraphicsCompTypeName = "FacialAnimation.DrawFaceGraphicsComp";
        private const int FaceGraphicsSeed = 0x46414345;
        private static FieldInfo _animationDefField;
        private static FieldInfo _startTickField;
        private static FieldInfo _intervalMinField;
        private static FieldInfo _intervalMaxField;
        private static readonly MethodInfo SnapshotDictAddMethod =
            typeof(Dictionary<string, object>).GetMethod(
                "Add",
                new[] { typeof(string), typeof(object) });
        private static readonly MethodInfo SnapshotDictIndexerSetMethod =
            typeof(Dictionary<string, object>).GetProperty("Item")?
                .GetSetMethod();

        internal static void Apply(Harmony harmony)
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.GetName().Name, AssemblyName, StringComparison.Ordinal));
                var animationType = assembly?.GetType(AnimationTypeName, false);
                var helperType = assembly?.GetType(HelperTypeName, false);
                var drawFaceGraphicsCompType = assembly?.GetType(DrawFaceGraphicsCompTypeName, false);
                var harmonyPatchesType = assembly?.GetType(
                    "FacialAnimation.HarmonyPatches",
                    false);
                var resetMethod = AccessTools.Method(animationType, "Reset", new[] { typeof(int) });
                var getThoughtsMethod = AccessTools.Method(helperType, "GetThoughts", new[] { typeof(Pawn) });
                var compRenderNodesMethod = AccessTools.Method(drawFaceGraphicsCompType, "CompRenderNodes", Type.EmptyTypes);
                var snapshotPrefixMethod = AccessTools.Method(
                    harmonyPatchesType,
                    "PrefixCreateSnapshotOfPawn_HookForMods",
                    new[]
                    {
                        typeof(Pawn),
                        typeof(Dictionary<string, object>).MakeByRefType()
                    });
                _animationDefField = AccessTools.Field(animationType, "animationDef");
                _startTickField = AccessTools.Field(animationType, "startTick");
                var animationDefType = _animationDefField?.FieldType;
                _intervalMinField = AccessTools.Field(animationDefType, "roopIntervalMin");
                _intervalMaxField = AccessTools.Field(animationDefType, "roopIntervalMax");
                var prefix = AccessTools.Method(typeof(Patch_FacialAnimationMp), nameof(ResetPrefix));
                var getThoughtsPrefix = AccessTools.Method(
                    typeof(Patch_FacialAnimationMp),
                    nameof(GetThoughtsPrefix));
                var renderNodesPrefix = AccessTools.Method(
                    typeof(Patch_FacialAnimationMp),
                    nameof(RenderNodesPrefix));
                var renderNodesFinalizer = AccessTools.Method(
                    typeof(Patch_FacialAnimationMp),
                    nameof(RenderNodesFinalizer));
                if (resetMethod == null || prefix == null ||
                    getThoughtsMethod == null || getThoughtsPrefix == null ||
                    compRenderNodesMethod == null || renderNodesPrefix == null ||
                    renderNodesFinalizer == null ||
                    _animationDefField == null || _startTickField == null ||
                    _intervalMinField == null || _intervalMaxField == null ||
                    snapshotPrefixMethod == null ||
                    SnapshotDictAddMethod == null ||
                    SnapshotDictIndexerSetMethod == null)
                {
                    if (assembly != null)
                        Log.Warning("[MP-MeowOnlineShop] Facial Animation MP patch target was not resolved.");
                    return;
                }

                harmony.Patch(
                    resetMethod,
                    prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                harmony.Patch(
                    getThoughtsMethod,
                    prefix: new HarmonyMethod(getThoughtsPrefix) { priority = Priority.First });
                harmony.Patch(
                    compRenderNodesMethod,
                    prefix: new HarmonyMethod(renderNodesPrefix) { priority = Priority.First },
                    finalizer: new HarmonyMethod(renderNodesFinalizer) { priority = Priority.Last });
                harmony.Patch(
                    snapshotPrefixMethod,
                    transpiler: new HarmonyMethod(
                        typeof(Patch_FacialAnimationMp),
                        nameof(SnapshotPrefixTranspiler)));
                Log.Message(
                    "[MP-MeowOnlineShop] Facial Animation MP patch active: " +
                    "cosmetic reset timers no longer consume synchronized Rand and " +
                    "visual thought queries cannot force simulation thought refreshes; " +
                    "lazy face/render-node initialization uses an isolated deterministic Rand scope; " +
                    "statue snapshots tolerate duplicate FacialAnimation comp keys.");
            }
            catch (Exception exception)
            {
                Log.Warning("[MP-MeowOnlineShop] Facial Animation MP patch failed: " + exception);
            }
        }

        private static IEnumerable<CodeInstruction> SnapshotPrefixTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call ||
                     instruction.opcode == OpCodes.Callvirt) &&
                    instruction.operand is MethodInfo method &&
                    method.Equals(SnapshotDictAddMethod))
                {
                    instruction.operand = SnapshotDictIndexerSetMethod;
                }

                yield return instruction;
            }
        }

        private static bool ResetPrefix(object __instance, int tickGame)
        {
            if (!MP.IsInMultiplayer)
                return true;

            var animationDef = _animationDefField.GetValue(__instance);
            int minimum = (int)_intervalMinField.GetValue(animationDef);
            int maximum = (int)_intervalMaxField.GetValue(animationDef);
            int width = maximum - minimum;
            int offset = 0;
            if (width > 0)
            {
                int hash = StableStringHash((animationDef as Def)?.defName);
                hash = Gen.HashCombineInt(hash, tickGame);
                offset = (int)((uint)hash % (uint)width);
            }

            _startTickField.SetValue(__instance, tickGame + minimum + offset);
            return false;
        }

        private static bool GetThoughtsPrefix(ref List<Thought> __result)
        {
            if (!MP.IsInMultiplayer)
                return true;

            __result = new List<Thought>();
            return false;
        }

        private static void RenderNodesPrefix(ThingComp __instance, ref bool __state)
        {
            __state = false;
            if (!MP.IsInMultiplayer || __instance?.parent == null)
                return;

            int seed = Gen.HashCombineInt(FaceGraphicsSeed, __instance.parent.thingIDNumber);
            seed = Gen.HashCombineInt(seed, StableStringHash(__instance.parent.def?.defName));
            Rand.PushState(seed);
            __state = true;
        }

        private static Exception RenderNodesFinalizer(Exception __exception, bool __state)
        {
            if (__state)
                Rand.PopState();
            return __exception;
        }

        private static int StableStringHash(string value)
        {
            int hash = 17;
            if (value == null)
                return hash;
            foreach (char character in value)
                hash = Gen.HashCombineInt(hash, character);
            return hash;
        }
    }
}
