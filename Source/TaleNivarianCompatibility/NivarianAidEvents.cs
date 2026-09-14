using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // Targets verified against Nivarian_Race.dll SHA256 1F0100B0E8FFCA1A... (20260823162750).
    internal static class NivarianAidEvents
    {
        const string Ns = "Nivarian_Race.Code.Incidents.";
        static Type letterType, componentType, extensionType;
        static FieldInfo targetMap, status, incident, cooldowns, requiresAcceptance;
        static MethodInfo accept, reject, setCooldown;
        static ISyncMethod decide, debugAction;

        static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        static MethodInfo Method(Type type, string name) => AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
        static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(NivarianAidEvents), name);

        internal static void Apply(Harmony harmony)
        {
            letterType = RequiredType(Ns + "ChoiceLetter_NivarianAid");
            componentType = RequiredType("Nivarian.MapComp_NivarianAidsIncident");
            extensionType = RequiredType(Ns + "DefExtension_NivarianAidIncident");
            targetMap = Field(letterType, "targetMap");
            status = Field(letterType, "stat");
            incident = Field(letterType, "incidentDef");
            cooldowns = Field(componentType, "nextTriggerTick");
            requiresAcceptance = Field(extensionType, "requiresPlayerAcceptance");
            accept = Method(letterType, "ExecuteAccept");
            reject = Method(letterType, "<get_RejectOption>b__12_0");
            setCooldown = Method(componentType, "SetToCD");
            decide = MP.RegisterSyncMethod(typeof(NivarianAidEvents), nameof(Decide));
            debugAction = MP.RegisterSyncMethod(typeof(NivarianAidEvents), nameof(DebugAction)).SetDebugOnly();
            harmony.Patch(accept, prefix: Hook(nameof(DecisionPrefix)));
            harmony.Patch(reject, prefix: Hook(nameof(DecisionPrefix)));
            harmony.Patch(Method(extensionType, "GetRandomCooldownTicks"), transpiler: Hook(nameof(CooldownRandom)));
            harmony.Patch(Method(componentType, "HasPendingLetter"), prefix: Hook(nameof(HasPending)));

            // LetterStack is client-local in multifaction; the shared archive is the MP letter worker's authority.
            // Keep unresolved aid letters in that archive even on peers that never display them.
            var cull = typeof(Letter).GetInterfaceMap(typeof(IArchivable));
            int cullIndex = Array.FindIndex(cull.InterfaceMethods, m => m.Name == "get_CanCullArchivedNow");
            if (cullIndex < 0) throw new MissingMethodException("IArchivable.CanCullArchivedNow");
            harmony.Patch(cull.TargetMethods[cullIndex], prefix: Hook(nameof(CanCull)));

            var debugWindow = RequiredType("Nivarian_Race.Code.DEBUG.Window_NivarianAidIncidentDebug");
            harmony.Patch(Method(debugWindow, "DrawActions"), transpiler: Hook(nameof(DebugButtons)));
            harmony.Patch(Method(debugWindow, "DrawIncidentTable"), prefix: Hook(nameof(BeginPreview)), finalizer: Hook(nameof(EndPreview)));
            // CanFireNow is called while drawing the debug table, but this worker writes its cooldown.
            harmony.Patch(Method(RequiredType(Ns + "IncidentWorker_NivarianKoelimeApologyAid"), "CanFireNowSub"),
                prefix: Hook(nameof(BeginConditionPreview)), finalizer: Hook(nameof(EndConditionPreview)));
            NivarianAidSettings.Install(harmony);
            Log.Message("[NivarianAidCompat] accept/reject, map context, shared pending letters, cooldown RNG, debug actions and session settings installed.");
        }

        static bool IsPending(Letter letter) => letter != null && letterType.IsInstanceOfType(letter)
            && Convert.ToInt32(status.GetValue(letter)) == 0;

        static bool DecisionPrefix(Letter __instance, MethodBase __originalMethod)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            var map = (Map)targetMap.GetValue(__instance);
            if (map != null && IsPending(__instance))
                decide.DoSync(null, map, __instance, __originalMethod == accept);
            return false;
        }

        static void Decide(Map map, Letter letter, bool accepted)
        {
            // An explicit Map argument selects its MP queue, clock and Rand stream even after a UI map switch.
            // Do not use ArchivedOnly/LetterStack here: other factions' letters are hidden on this peer.
            if (map == null || !Find.Maps.Contains(map) || !IsPending(letter)
                || !ReferenceEquals(targetMap.GetValue(letter), map)
                || map.ParentFaction != Faction.OfPlayer) return;
            (accepted ? accept : reject).Invoke(letter, null);
            // Failed acceptance also removes the letter; make that terminal state visible to every peer.
            if (IsPending(letter)) status.SetValue(letter, Enum.ToObject(status.FieldType, 2));
        }

        static bool HasPending(MapComponent __instance, IncidentDef def, ref bool __result)
        {
            if (!MP.IsInMultiplayer) return true;
            var extension = def?.modExtensions?.FirstOrDefault(extensionType.IsInstanceOfType);
            __result = extension != null && (bool)requiresAcceptance.GetValue(extension)
                && Find.Archive.ArchivablesListForReading.OfType<Letter>().Any(l => IsPending(l)
                    && ReferenceEquals(targetMap.GetValue(l), __instance.map) && ReferenceEquals(incident.GetValue(l), def));
            return false;
        }

        static bool CanCull(Letter __instance, ref bool __result)
        {
            if (!MP.IsInMultiplayer || !letterType.IsInstanceOfType(__instance)) return true;
            var map = (Map)targetMap.GetValue(__instance);
            // Accepted aid can generate another letter before removing itself from the local stack.
            // Its culling eligibility must also be equal during that nested ReceiveLetter call.
            __result = !IsPending(__instance) || map == null || !Find.Maps.Contains(map);
            return false;
        }

        static IEnumerable<CodeInstruction> CooldownRandom(IEnumerable<CodeInstruction> instructions)
        {
            var range = AccessTools.Method(typeof(UnityEngine.Random), "Range", new[] { typeof(float), typeof(float) });
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(range)) { instruction.operand = AccessTools.Method(typeof(NivarianAidEvents), nameof(Range)); count++; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Nivarian cooldown expected one Unity Range call, found " + count);
        }

        static float Range(float min, float max) => MP.IsInMultiplayer ? Rand.Range(min, max) : UnityEngine.Random.Range(min, max);

        static IEnumerable<CodeInstruction> DebugButtons(IEnumerable<CodeInstruction> instructions)
        {
            var execute = AccessTools.Method(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute));
            var message = AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) });
            int actions = 0;
            foreach (var instruction in instructions)
            {
                string replacement = instruction.Calls(execute) ? nameof(DebugForce)
                    : instruction.Calls(setCooldown) ? nameof(DebugCooldown)
                    : instruction.Calls(message) ? nameof(DebugMessage) : null;
                if (replacement != null)
                {
                    if (replacement != nameof(DebugMessage)) actions++;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianAidEvents), replacement);
                }
                yield return instruction;
            }
            if (actions != 2) throw new InvalidOperationException("Nivarian debug expected Force and Reset CD, found " + actions);
        }

        static bool DebugForce(IncidentWorker worker, IncidentParms parms)
        {
            if (!MP.IsInMultiplayer) return worker.TryExecute(parms);
            if (parms.target is Map map) debugAction.DoSync(null, map, worker.def, true);
            return false;
        }
        static void DebugCooldown(MapComponent component, IncidentDef def, int ticks)
        {
            if (!MP.IsInMultiplayer) { setCooldown.Invoke(component, new object[] { def, ticks }); return; }
            debugAction.DoSync(null, component.map, def, false);
        }
        static void DebugMessage(string text, MessageTypeDef def, bool historical)
            => Messages.Message(text, def, MP.IsInMultiplayer ? false : historical);

        static void DebugAction(Map map, IncidentDef def, bool force)
        {
            if (map == null || def == null || !Find.Maps.Contains(map) || map.ParentFaction != Faction.OfPlayer) return;
            if (def.modExtensions?.Any(extensionType.IsInstanceOfType) != true) return;
            if (force) def.Worker.TryExecute(StorytellerUtility.DefaultParmsNow(def.category, map));
            else
            {
                var component = map.components.FirstOrDefault(componentType.IsInstanceOfType);
                if (component != null) setCooldown.Invoke(component, new object[] { def, 0 });
            }
        }

        static void BeginPreview(out bool __state)
        {
            __state = MP.IsInMultiplayer && MP.InInterface;
            if (__state) Rand.PushState();
        }
        static void EndPreview(bool __state) { if (__state) Rand.PopState(); }

        internal sealed class ConditionPreview
        {
            internal Dictionary<IncidentDef, int> Values;
            internal Dictionary<IncidentDef, int> Original;
        }
        static void BeginConditionPreview(IncidentParms parms, out ConditionPreview __state)
        {
            __state = null;
            if (!MP.IsInMultiplayer || !MP.InInterface || !(parms.target is Map map)) return;
            var component = map.components.FirstOrDefault(componentType.IsInstanceOfType);
            var values = component == null ? null : (Dictionary<IncidentDef, int>)cooldowns.GetValue(component);
            if (values != null) __state = new ConditionPreview { Values = values, Original = new Dictionary<IncidentDef, int>(values) };
        }
        static void EndConditionPreview(ConditionPreview __state)
        {
            if (__state == null) return;
            __state.Values.Clear();
            foreach (var entry in __state.Original) __state.Values.Add(entry.Key, entry.Value);
        }
    }
}

