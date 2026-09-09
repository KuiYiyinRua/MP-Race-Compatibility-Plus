using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.RatkinCompatibility
{
    [StaticConstructorOnStartup]
    public static class Anomaly
    {
        static readonly Harmony Harmony = new Harmony("meow.ratkin.anomaly");
        static FieldInfo stage, cooldown, crossTick;
        static PropertyInfo component, wearer;
        static int resolved;
        static Type T(string name) => AccessTools.TypeByName("RatkinAnomaly." + name) ?? throw new MissingMemberException(name);
        static MethodInfo M(string type, string name) => AccessTools.Method(T(type), name) ?? throw new MissingMethodException(type, name);
        static HarmonyMethod H(string name) => new HarmonyMethod(typeof(Anomaly), name);
        static void Patch(string type, string method, string prefix = null, string postfix = null, string finalizer = null)
        {
            Harmony.Patch(M(type, method), prefix == null ? null : H(prefix), postfix == null ? null : H(postfix), null, finalizer == null ? null : H(finalizer));
            resolved++;
        }
        static Anomaly()
        {
            if (!MP.enabled || !ModsConfig.IsActive("fxz.ratkinanomaly.update")) return;
            try
            {
                stage = AccessTools.Field(T("RAComponent"), "amethystStage");
                component = AccessTools.Property(T("CompFairyTale"), "Component");
                wearer = AccessTools.Property(T("CompMemoryWatch"), "Wearer");
                cooldown = AccessTools.Field(T("CompMemoryWatch"), "readyToUseTicks");
                crossTick = AccessTools.Field(T("CompBlackCrossStone"), "voidTick");
                if (stage == null || component == null || wearer == null || cooldown == null || crossTick == null) throw new MissingFieldException("Anomaly state");
                // Exact compiler-generated method verified against installed 1.6 DLL.
                var watch = M("CompMemoryWatch", "<CompGetWornGizmosExtra>b__5_0");
                Harmony.Patch(watch, prefix: H(nameof(WatchReady)));
                MP.RegisterSyncMethod(watch); resolved++;
                Patch("CompFairyTale", "OrderAgain", nameof(BookReady));
                MP.RegisterSyncMethod(M("CompFairyTale", "OrderAgain"));
                // Cancellation is local: resetting stage after another player confirmed rewinds the story.
                Patch("CompFairyTale+<>c__DisplayClass15_0", "<OnInteracted>b__0", nameof(CancelBook));
                Patch("CompBlackCrossStone", "StartCollapse", nameof(CollapseReady));
                MP.RegisterSyncMethod(M("CompBlackCrossStone", "StartCollapse"));
                Patch("CompBlackCrossStone", "PostExposeData", postfix: nameof(SaveCross));
                // These helpers produce only graphics; camera visibility must not advance simulation Rand.
                foreach (var type in new[] { "RAUtility", "MagicTreasure", "MetalBean", "MetalBeanSmall", "CompUseEffect_PickHerb", "CompUseEffect_Statue", "JobDriver_CrossSuppression", "WatchLabyrinthMapComponent" })
                    Patch(type, "MakeGlow", nameof(PushVisual), finalizer: nameof(PopVisual));
                foreach (var type in new[] { "CrossStone", "CrossStoneWatch", "HediffComp_CommanderMutant", "DarkKnightCommander", "Projectile_Spear" })
                    Patch(type, "MakeFleck", nameof(PushVisual), finalizer: nameof(PopVisual));
                Patch("CompAbilityEffect_CircularCut", "ThrowAirPuffUp", nameof(PushVisual), finalizer: nameof(PopVisual));
                Patch("SubEffecter_Scissor", "MakeMote", nameof(PushVisual), finalizer: nameof(PopVisual));
                // This offset is serialized on DarkSoul, so use the shared stream rather than Unity Random.
                Patch("RAUtility", "RandomPointInCircle", nameof(RandomPoint));
                Patch("CompBerryWine", "ChangeFlavor", nameof(ChangeFlavor));
                foreach (string method in new[] { "SentOut", "Destroy" })
                {
                    Harmony.Patch(M("GlassPlatycodon", method), transpiler: H(nameof(VisualRanges))); resolved++;
                }
                Log.Message("[RatkinMP] Anomaly targets resolved=" + resolved + " version=1.0.0");
            }
            catch (Exception e) { Log.Error("[RatkinMP] REQUIRED TARGET FAILED " + e); }
        }
        static bool WatchReady(object __instance)
        {
            if (!MP.IsInMultiplayer || MP.InInterface) return true;
            var pawn = (Pawn)wearer.GetValue(__instance);
            return pawn != null && !pawn.Dead && (int)cooldown.GetValue(__instance) <= 0;
        }
        static bool BookReady(object __instance, Pawn pawn)
        {
            if (!MP.IsInMultiplayer || MP.InInterface) return true;
            var comp = (ThingComp)__instance;
            return pawn != null && pawn.Spawned && comp.parent.Spawned && pawn.Map == comp.parent.Map && (int)stage.GetValue(component.GetValue(__instance)) == 0;
        }
        static bool CancelBook() => !MP.IsInMultiplayer;
        static bool ChangeFlavor(object __instance)
        {
            if (!MP.IsInMultiplayer) return true;
            var field = AccessTools.Field(__instance.GetType(), "flavor");
            var values = Enum.GetValues(field.FieldType);
            field.SetValue(__instance, values.GetValue(Rand.Range(0, values.Length)));
            return false;
        }
        static bool CollapseReady(object __instance) => !MP.IsInMultiplayer || MP.InInterface || (int)crossTick.GetValue(__instance) < 0;
        static void SaveCross(object __instance)
        {
            var field = AccessTools.Field(__instance.GetType(), "interacted");
            bool value = (bool)field.GetValue(__instance);
            Scribe_Values.Look(ref value, "meow_interacted", false);
            field.SetValue(__instance, value);
        }
        static void PushVisual(out bool __state) { __state = MP.IsInMultiplayer; if (__state) Rand.PushState(); }
        static Exception PopVisual(Exception __exception, bool __state) { if (__state) Rand.PopState(); return __exception; }
        static bool RandomPoint(float radius, ref Vector3 __result)
        {
            if (!MP.IsInMultiplayer) return true;
            float angle = Rand.Range(0f, Mathf.PI * 2f), distance = Rand.Range(0f, radius);
            __result = new Vector3(distance * Mathf.Cos(angle), 0f, distance * Mathf.Sin(angle)); return false;
        }
        // The only explicit integer Range calls in these two methods set fleck scale/velocity.
        static IEnumerable<CodeInstruction> VisualRanges(IEnumerable<CodeInstruction> instructions)
        {
            var range = AccessTools.Method(typeof(Rand), nameof(Rand.Range), new[] { typeof(int), typeof(int) });
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(range)) instruction.operand = AccessTools.Method(typeof(Anomaly), nameof(VisualRange));
                yield return instruction;
            }
        }
        static int VisualRange(int min, int max)
        {
            if (!MP.IsInMultiplayer) return Rand.Range(min, max);
            Rand.PushState(); try { return Rand.Range(min, max); } finally { Rand.PopState(); }
        }
    }
}
