using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class NivarianWindowQuestButtons
    {
        const string ResetLabel = "Restart Nivarian Join Quest";
        const string TriggerLabel = "Trigger Nivarian Quest Now";
        static Type componentType;
        static MethodInfo reset, parameters;
        static ISyncMethod execute;

        internal static void Apply(Harmony harmony)
        {
            var window = AccessTools.TypeByName("TestIconWindow") ?? throw new TypeLoadException("TestIconWindow");
            componentType = AccessTools.TypeByName("Nivarian.MapComp_NivarianAidsIncident")
                ?? throw new TypeLoadException("Nivarian aid map component");
            reset = AccessTools.DeclaredMethod(componentType, "SetToCD", new[] { typeof(IncidentDef), typeof(int) })
                ?? throw new MissingMethodException(componentType.FullName, "SetToCD");
            parameters = AccessTools.DeclaredMethod(componentType, "GetIncidentParmsPublic", new[] { typeof(IncidentDef) })
                ?? throw new MissingMethodException(componentType.FullName, "GetIncidentParmsPublic");
            var button = AccessTools.DeclaredMethod(window, "DrawButton", new[] { typeof(float).MakeByRefType(), typeof(float), typeof(float), typeof(string) })
                ?? throw new MissingMethodException(window.FullName, "DrawButton");
            execute = MP.RegisterSyncMethod(typeof(NivarianWindowQuestButtons), nameof(Execute),
                new[] { new SyncType(typeof(Map)) { contextMap = true }, new SyncType(typeof(bool)) }).SetDebugOnly();
            harmony.Patch(button, postfix: new HarmonyMethod(typeof(NivarianWindowQuestButtons), nameof(Button)));
            Log.Message("[TaleNivarianCompat] custom debug-window quest buttons synchronized=2 before parameter generation.");
        }

        static void Button(string label, ref bool __result)
        {
            if (!__result || !MP.IsInMultiplayer || !MP.InInterface) return;
            if (label != ResetLabel && label != TriggerLabel) return;
            // Prevent the entire original UI branch, including DefaultParmsNow.
            if (execute.DoSync(null, Find.CurrentMap, label == TriggerLabel)) __result = false;
        }

        static void Execute(Map map, bool trigger)
        {
            if (map == null || !Find.Maps.Contains(map)) return;
            var component = map.components.FirstOrDefault(componentType.IsInstanceOfType);
            var def = DefDatabase<IncidentDef>.GetNamed("Nivarian_ColdShelterJoinQuest", false);
            if (!trigger)
            {
                if (component != null) reset.Invoke(component, new object[] { def, 0 });
                Messages.Message("Nivarian join quest CD reset.", MessageTypeDefOf.TaskCompletion, true);
                return;
            }
            var parms = component != null && def != null
                ? (IncidentParms)parameters.Invoke(component, new object[] { def }) : null;
            bool succeeded = parms != null && def != null && def.Worker.TryExecute(parms);
            Messages.Message(succeeded ? "Nivarian join quest triggered." : "Failed to trigger Nivarian join quest.",
                succeeded ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, true);
        }
    }
}
