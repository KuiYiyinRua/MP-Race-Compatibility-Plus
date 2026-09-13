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
    internal static class Patch_Light350PoliciesMp
    {
        private static bool applied;
        private static FieldInfo dataPawn, cache, cacheTick, alert;
        private static MethodInfo getData;
        private static readonly List<FieldInfo> policyFields = new List<FieldInfo>();
        private static readonly Dictionary<MethodBase,int> callbackKinds = new Dictionary<MethodBase,int>();
        internal static void Apply(Harmony harmony)
        {
            if (applied || !MP.enabled) return;
            applied = true;
            Package("ariandel.ariandellibrary", () =>
            {
                var retreat = Executor("AriandelLibrary.HediffComp_ClickToRetreat", "PerformWarp");
                var returning = Executor("AriandelLibrary.HediffComp_ReturnFromPocketMap", "TryReturnToPlayerHomeAndRemoveSelf");
                MP.RegisterSyncMethod(retreat);
                MP.RegisterSyncMethod(returning);
            });
            Package("calamabanana.rjw.brothelcolony", () =>
            {
                var data = Type("BrothelColony.WhoringData");
                dataPawn = Field(data, "pawn", typeof(Pawn));
                cache = Field(data, "checkedClientCache", typeof(List<Pawn>));
                cacheTick = Field(data, "checkedClientCacheTick", typeof(int));
                getData = Method(Type("BrothelColony.Extensions"), "WhoringData", new[] { typeof(Pawn) });
                if (!getData.IsStatic || getData.ReturnType != data) throw new InvalidOperationException("WhoringData accessor changed");
                alert = AccessTools.DeclaredField(Type("BrothelColony.Alert_WhoringBase"), "recheckAlert");
                if (alert == null || !alert.IsStatic || alert.FieldType != typeof(bool)) throw new MissingFieldException("recheckAlert");
                foreach (string name in new[] { "WhoringPayment", "WhoringPregnancy", "WhoringCondom" })
                {
                    var field = AccessTools.DeclaredField(data, name);
                    if (field == null || field.IsStatic || !field.FieldType.IsEnum || Enum.GetUnderlyingType(field.FieldType) != typeof(int))
                        throw new MissingFieldException(data.FullName, name);
                    policyFields.Add(field);
                }
                var ui = Type("BrothelColony.WhoringTabUIUtility");
                var callbacks = ui.Assembly.GetTypes().Where(t => t == ui || t.FullName.StartsWith(ui.FullName + "+", StringComparison.Ordinal))
                    .SelectMany(t => t.GetMethods(AccessTools.allDeclared)).Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0 && m.GetMethodBody() != null &&
                        (m.IsDefined(typeof(CompilerGeneratedAttribute), false) || m.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), false))).ToArray();
                for (int kind = 0; kind < policyFields.Count; kind++)
                {
                    var field = policyFields[kind];
                    var matches = callbacks.Where(m => PatchProcessor.GetOriginalInstructions(m).Any(i => i.opcode == OpCodes.Stfld && Equals(i.operand, field))).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException("Expected one policy callback for " + field.Name);
                    callbackKinds.Add(matches[0], kind);
                }
                var debug = Method(Type("BrothelColony.FloatMenuOptionProvider_WhoringDebug+DebugWorkGiver_Whore"), "runTests", new[] { typeof(Pawn), typeof(Pawn) });
                var expose = Method(data, "ExposeData", System.Type.EmptyTypes);
                MP.RegisterSyncMethod(typeof(Patch_Light350PoliciesMp), nameof(SetPolicy));
                foreach (var callback in callbackKinds.Keys)
                    harmony.Patch(callback, transpiler: new HarmonyMethod(typeof(Patch_Light350PoliciesMp), nameof(PolicyWrite)));
                MP.RegisterSyncMethod(debug).SetDebugOnly();
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(Patch_Light350PoliciesMp), nameof(CacheExpose)));
            });
        }
        private static void Package(string id, Action action)
        {
            if (!ModsConfig.IsActive(id)) return;
            try { action(); Log.Message("[MP-MeowOnlineShop][Light350-B5] " + id + ": policy/action targets installed."); }
            catch (Exception e) { Log.Error("[MP-MeowOnlineShop][Light350-B5] REQUIRED TARGET FAILED " + id + ": " + e); }
        }
        private static Type Type(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        private static MethodInfo Method(Type type, string name, Type[] args) => AccessTools.DeclaredMethod(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = AccessTools.DeclaredField(type, name);
            if (field == null || field.IsStatic || field.FieldType != expected) throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static MethodInfo Executor(string typeName, string name)
        {
            var type = Type(typeName);
            if (!typeof(HediffComp).IsAssignableFrom(type)) throw new InvalidOperationException("Hediff executor base changed");
            var method = Method(type, name, System.Type.EmptyTypes);
            if (method.IsStatic || method.ReturnType != typeof(void)) throw new InvalidOperationException("Hediff executor signature changed");
            return method;
        }
        private static IEnumerable<CodeInstruction> PolicyWrite(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            int kind = callbackKinds[__originalMethod], count = 0;
            var body = instructions.ToList();
            foreach (var instruction in body)
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, policyFields[kind]))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.DeclaredMethod(typeof(Patch_Light350PoliciesMp), new[] { nameof(Payment), nameof(Pregnancy), nameof(Condom) }[kind]);
                    count++;
                }
            if (count != 1) throw new InvalidOperationException("Policy write coverage changed");
            return body;
        }
        private static void Payment(object data, int value) => SetPolicy((Pawn)dataPawn.GetValue(data), 0, value);
        private static void Pregnancy(object data, int value) => SetPolicy((Pawn)dataPawn.GetValue(data), 1, value);
        private static void Condom(object data, int value) => SetPolicy((Pawn)dataPawn.GetValue(data), 2, value);
        private static void SetPolicy(Pawn pawn, int kind, int value)
        {
            if (pawn == null || kind < 0 || kind >= policyFields.Count) return;
            var field = policyFields[kind];
            if (!Enum.IsDefined(field.FieldType, value)) return;
            object data = getData.Invoke(null, new object[] { pawn });
            field.SetValue(data, Enum.ToObject(field.FieldType, value));
            alert.SetValue(null, true);
        }
        private static void CacheExpose(object __instance)
        {
            var clients = (List<Pawn>)cache.GetValue(__instance);
            int tick = (int)cacheTick.GetValue(__instance);
            Scribe_Collections.Look(ref clients, "meowMpCheckedClients", LookMode.Reference);
            Scribe_Values.Look(ref tick, "meowMpCheckedClientsTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (clients == null) clients = new List<Pawn>();
                clients.RemoveAll(p => p == null);
            }
            cache.SetValue(__instance, clients);
            cacheTick.SetValue(__instance, tick);
        }
    }
}
