using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AM;
using AM.AMSettings;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop.MeleeAnimation
{
    // Save-owned copy: client preferences and live settings windows cannot change
    // simulation rules independently. No writes to the player's config files.
    public sealed class MeleeSessionState : GameComponent
    {
        public Settings Rules;
        private static readonly ConditionalWeakTable<Game, MeleeSessionState> States =
            new ConditionalWeakTable<Game, MeleeSessionState>();
        private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        internal static readonly AccessTools.FieldRef<Settings, Dictionary<string, AnimDef.SettingsData>> AnimSettings =
            AccessTools.FieldRefAccess<Settings, Dictionary<string, AnimDef.SettingsData>>("animSettings");

        public MeleeSessionState(Game game)
        {
            if (Core.Settings != null) Rules = Copy(Core.Settings);
        }

        private static Settings Copy(Settings original)
        {
            var settings = (Settings)Clone.Invoke(original, null);
            AnimSettings(settings) = AnimSettings(original).ToDictionary(k => k.Key,
                v => new AnimDef.SettingsData { Enabled = v.Value.Enabled, Probability = v.Value.Probability });
            return settings;
        }
        internal static void CaptureHostPreferences()
        {
            var state=Current.Game?.GetComponent<MeleeSessionState>();
            if(state!=null) state.Rules=Copy(Core.Settings);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref Rules, "meleeMultiplayerRules");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && Rules == null) Rules = Copy(Core.Settings);
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            // Scribe can replace the component list on an existing Game.
            var game = Current.Game;
            if (game != null) States.Remove(game);
        }

        public static Settings CurrentRules()
        {
            if (!Bootstrap.Active) return Core.Settings;
            var game = Current.Game;
            if (game == null) return Core.Settings;
            if (!States.TryGetValue(game, out var state)) state = CacheState(game);
            // Cache the component, not Rules: hosting and loading replace Rules.
            return state?.Rules ?? Core.Settings;
        }

        private static MeleeSessionState CacheState(Game game)
        {
            var state = game.GetComponent<MeleeSessionState>();
            // Do not cache a miss during game construction. Weak keys also allow
            // unloaded games to be collected and keep MP game swaps isolated.
            return state == null ? null : States.GetValue(game, _ => state);
        }
    }

    [HarmonyPatch]
    internal static class HostSettingsBoundary
    {
        private static MethodBase TargetMethod() => AccessTools.Method("Multiplayer.Client.HostUtil:HostServer")
            ?? throw new MissingMethodException("Multiplayer.Client.HostUtil.HostServer");
        private static void Prefix(bool fromReplay)
        {
            if(!fromReplay) MeleeSessionState.CaptureHostPreferences();
        }
    }

    [HarmonyPatch]
    internal static class ReadSessionRules
    {
        private static readonly FieldInfo Field = AccessTools.Field(typeof(Core), nameof(Core.Settings));

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in typeof(Core).Assembly.GetTypes())
            {
                // Settings editors keep using the user's local settings object.
                if (type.Namespace == "AM.AMSettings" || type == typeof(Core)) continue;
                foreach (var method in type.GetMethods(AccessTools.allDeclared))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                    if (PatchProcessor.GetOriginalInstructions(method).Any(i => i.opcode == OpCodes.Ldsfld && Equals(i.operand, Field)))
                        yield return method;
                }
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, Field))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(MeleeSessionState), nameof(MeleeSessionState.CurrentRules));
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch(typeof(AnimDef), nameof(AnimDef.UserEditedProbability), MethodType.Getter)]
    internal static class SavedAnimationProbability
    {
        private static bool Prefix(AnimDef __instance, ref float __result)
        {
            if (!Bootstrap.Active) return true;
            var settings = MeleeSessionState.CurrentRules();
            if (MeleeSessionState.AnimSettings(settings).TryGetValue(__instance.defName, out var data))
                __result = data.Enabled ? data.Probability : 0;
            else __result = 1;
            return false;
        }
    }
}
