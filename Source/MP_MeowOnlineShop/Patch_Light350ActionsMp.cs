using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop
{
    // Exact installed action bodies are indexed in Light350_20260910/Batch1.
    // Sync the original executor, including all its side effects, rather than
    // copying its field assignment into a second implementation.
    internal static class Patch_Light350ActionsMp
    {
        private static bool applied;

        internal static void Apply()
        {
            if (applied || !MP.enabled) return;
            applied = true;

            RegisterPackage("kokodayo.mlgreturn", new[]
            {
                Action("MLG.CompStealthWithGizmo", "<CompGetGizmosExtra>b__16_0", "Active"),
                Action("MLG.CompStealthWithGizmo", "<CompGetGizmosExtra>b__16_1", "Active"),
                Action("MLG.CompStealthWithGizmo", "<CompGetGizmosExtra>b__16_2", "Energy", true),
                Action("MLG.CompStealthWithGizmo", "<CompGetGizmosExtra>b__16_3", "Energy", true)
            });
            RegisterPackage("kokodayo.mgmechspidergirl", new[]
            {
                new Target("MSG.CompMechCarrier_Switchable", "TrySpawnPawns", "cooldownTicksRemaining", false, false, new[] { typeof(PawnKindDef) }),
                Action("MSG.Comp_ManualDetonation", "<CompGetGizmosExtra>b__8_0", "DetonateSignal"),
                Action("MSG.Comp_ManualDetonation", "<CompGetGizmosExtra>b__8_1", "DetonateSignal", true),
                Action("MSG.Comp_SelectShutDown", "<CompGetGizmosExtra>b__10_0", "ShutDownSignal"),
                Action("MSG.Comp_SelectShutDown", "<CompGetGizmosExtra>b__10_1", "ShutDownSignal", true)
            });
            RegisterPackage("bichang.kyulen", new[]
            {
                Action("Ninetail.CompFoxfireToggle", "<CompGetGizmosExtra>b__3_2", "foxfireEnabled")
            });
        }

        private static Target Action(string type, string method, string field, bool debug = false)
        {
            return new Target(type, method, field, true, debug, Type.EmptyTypes);
        }

        private static void RegisterPackage(string package, Target[] targets)
        {
            if (!ModsConfig.IsActive(package)) return;
            try
            {
                var resolved = new List<MethodInfo>();
                // Resolve and check every body before registering any target in this package.
                foreach (Target target in targets)
                {
                    Type type = AccessTools.TypeByName(target.Type);
                    if (type == null || !typeof(ThingComp).IsAssignableFrom(type))
                        throw new TypeLoadException(target.Type);
                    MethodInfo method = AccessTools.DeclaredMethod(type, target.Method, target.Parameters);
                    FieldInfo field = AccessTools.DeclaredField(type, target.Field);
                    if (method == null || method.IsStatic || method.ReturnType != typeof(void) || field == null)
                        throw new MissingMethodException(target.Type, target.Method);
                    if (target.Generated && !method.IsDefined(typeof(CompilerGeneratedAttribute), false))
                        throw new InvalidOperationException("Expected generated action: " + method);
                    if (!PatchProcessor.GetOriginalInstructions(method).Any(i => i.opcode == OpCodes.Stfld && Equals(i.operand, field)))
                        throw new InvalidOperationException("Expected state write not found: " + method + " -> " + target.Field);
                    resolved.Add(method);
                }
                for (int i = 0; i < resolved.Count; i++)
                {
                    ISyncMethod sync = MP.RegisterSyncMethod(resolved[i]);
                    if (targets[i].Debug) sync.SetDebugOnly();
                }
                Log.Message("[MP-MeowOnlineShop][Light350-B1] " + package + ": registered " + resolved.Count + " verified action targets.");
            }
            catch (Exception e)
            {
                Log.Error("[MP-MeowOnlineShop][Light350-B1] FAILED " + package + ": " + e);
            }
        }

        private sealed class Target
        {
            internal readonly string Type, Method, Field;
            internal readonly bool Generated, Debug;
            internal readonly Type[] Parameters;

            internal Target(string type, string method, string field, bool generated, bool debug, Type[] parameters)
            {
                Type = type; Method = method; Field = field;
                Generated = generated; Debug = debug; Parameters = parameters;
            }
        }
    }
}
