using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace MP_MeowOnlineShop
{
    internal static class RavenCentralResearch
    {
        private static Type systemType;
        private static object Read(object obj, string field) => AccessTools.Field(obj.GetType(), field).GetValue(obj);
        internal static void Apply(Harmony harmony)
        {
            systemType = AccessTools.TypeByName("RavenRace.Features.CentralHub.GameComponent_RavenCentralHubSystem")
                ?? throw new TypeLoadException("Raven central research");
            RavenResearchClock.Apply(harmony, systemType);
            foreach (string name in new[] { "TryEnqueue", "TryRemoveAt", "TryMove", "TryUpgrade" })
                harmony.Patch(AccessTools.DeclaredMethod(systemType, name) ?? throw new MissingMethodException(systemType.FullName, name),
                    prefix: new HarmonyMethod(typeof(RavenCentralResearch), nameof(BeforeBoolAction)));
            harmony.Patch(AccessTools.DeclaredMethod(systemType, "ToggleEntryPaused"),
                prefix: new HarmonyMethod(typeof(RavenCentralResearch), nameof(BeforePause)));
            harmony.Patch(AccessTools.DeclaredMethod(systemType, "ExposeData"),
                postfix: new HarmonyMethod(typeof(RavenCentralResearch), nameof(ExposeClock)));
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(systemType, "ToggleResearchPaused"), null);
            MP.RegisterSyncMethod(typeof(RavenCentralResearch), nameof(Execute));
        }

        private static bool BeforeBoolAction(GameComponent __instance, MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (!MP.InInterface) return true;
            __result = false;
            switch (__originalMethod.Name)
            {
                case "TryEnqueue":
                    Execute(__instance, (Def)__args[0], 0, 0, 0);
                    __args[1] = null;
                    break;
                case "TryRemoveAt": Queue(__instance, (int)__args[0], 1, 0); break;
                case "TryMove":
                    Queue(__instance, (int)__args[0], 2, (int)__args[1]);
                    __args[2] = null;
                    break;
                case "TryUpgrade": Execute(__instance, null, 0, 4, 0); __args[0] = null; break;
                default: throw new InvalidOperationException("Unknown Raven research action");
            }
            return false;
        }

        private static bool BeforePause(GameComponent __instance, int __0)
        {
            if (!MP.InInterface) return true;
            Queue(__instance, __0, 3, 0);
            return false;
        }

        // Capture project identity and level so simultaneous edits cannot redirect an index to another project.
        private static void Queue(GameComponent instance, int index, int operation, int direction)
        {
            var queue = (IList)Read(instance, "researchQueue");
            if (index < 0 || index >= queue.Count || queue[index] == null) return;
            var entry = queue[index];
            Execute(instance, (Def)Read(entry, "project"), (int)Read(entry, "targetLevel"), operation, direction);
        }

        private static void Execute(GameComponent instance, Def project, int level, int operation, int direction)
        {
            if (instance == null || instance.GetType() != systemType) return;
            string method;
            object[] args;
            if (operation == 0) { method = "TryEnqueue"; args = new object[] { project, null }; }
            else if (operation == 4) { method = "TryUpgrade"; args = new object[] { null }; }
            else
            {
                var queue = (IList)Read(instance, "researchQueue");
                int index = -1;
                for (int i = 0; i < queue.Count; i++)
                    if (queue[i] != null && ReferenceEquals(Read(queue[i], "project"), project) &&
                        (int)Read(queue[i], "targetLevel") == level) { index = i; break; }
                if (index < 0) return;
                switch (operation)
                {
                    case 1: method = "TryRemoveAt"; args = new object[] { index }; break;
                    case 2:
                        if (direction != -1 && direction != 1) return;
                        method = "TryMove"; args = new object[] { index, direction, null }; break;
                    case 3: method = "ToggleEntryPaused"; args = new object[] { index }; break;
                    default: return;
                }
            }
            var result = AccessTools.DeclaredMethod(systemType, method).Invoke(instance, args);
            // Out reasons are presentation feedback; the simulation mutation is wholly in the original executor.
            if (result is bool ok && !ok && args.Length > 0 && args[args.Length - 1] is string reason && !string.IsNullOrEmpty(reason))
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        }

        private static void ExposeClock(GameComponent __instance)
        {
            int tick = (int)Read(__instance, "lastResearchTick");
            Scribe_Values.Look(ref tick, "meowRavenLastResearchTick", -1);
            AccessTools.Field(systemType, "lastResearchTick").SetValue(__instance, tick);
        }
    }
}
