// Standalone net48 regression/surface check; never launches or modifies RimWorld.
// Args: candidate DLL, game root. Compile with the game's 0Harmony reference.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

internal static class TurretWirelessRestartTests
{
    static Type policy;
    static int checks;
    static string[] dirs;
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAIL " + name);
        checks++; Console.WriteLine("PASS " + name);
    }
    static MethodInfo Method(string name) { return policy.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
    static bool Allow(bool enough, bool powered, float demand, float available)
    {
        return (bool)Method("RestartAllowed").Invoke(null, new object[] { enough, powered, demand, available });
    }
    static List<CodeInstruction> Rewrite(string name, List<CodeInstruction> code)
    {
        return ((IEnumerable<CodeInstruction>)Method(name).Invoke(null, new object[] { code })).ToList();
    }
    // Replays the installed adapter's one-tick gate with an externally fluctuating
    // supply. This is a logic reproduction, not a TPS or full-game claim.
    static int Transitions(bool updated, IEnumerable<float> supply)
    {
        bool powered = false; int changes = 0;
        foreach (float available in supply)
        {
            bool next = powered ? available >= 1f : updated ? Allow(available >= 1f, false, 1f, available) : available >= 1f;
            if (powered != next) changes++;
            powered = next;
        }
        return changes;
    }
    static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[1]);
            dirs = new[] { Path.GetDirectoryName(Path.GetFullPath(args[0])), Path.Combine(root,"RimWorldWin64_Data/Managed"),
                Path.Combine(root,"Mods/2009463077/Current/Assemblies"), Path.Combine(root,"Mods/Multiplayer/1.6/Assemblies"),
                Path.Combine(root,"Mods/3624805128/1.6/Assemblies"), Path.Combine(root,"Mods/3624805128/1.6/Assemblies/LumiParticle"),
                Path.Combine(root,"Mods/3735573834/1.6/Assemblies") };
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) => {
                string name = new AssemblyName(e.Name).Name + ".dll";
                foreach (string dir in dirs) { string file = Path.Combine(dir,name); if (File.Exists(file)) return Assembly.LoadFrom(file); }
                return null;
            };
            Assembly candidate = Assembly.LoadFrom(args[0]);
            policy = candidate.GetType("Meow.TurretCombatSleep.NivarianWirelessRestart", true);
            Check(!Allow(false, true, 1f, 1000f), "native rejection cannot be turned into power");
            Check(!Allow(true, false, 1f, 59.99f), "restart blocked below reserve");
            Check(Allow(true, false, 1f, 60f), "restart at exact reserve");
            Check(Allow(true, true, 1f, 1f), "online turret retains one-tick threshold");
            Check(!Allow(false, true, 1f, 0.99f), "online shortage stops immediately");
            Check(Allow(true, false, 1f, float.MaxValue), "infinite supply preserved");
            Check(!Allow(true, false, 1f, float.NaN), "invalid supply cannot start");
            foreach (string def in new[] { "Turret_Cryo", "Turret_CryoMortar", "Turret_NivarianWinter" })
                Check((bool)Method("Target").Invoke(null,new object[]{def}), "target " + def);
            foreach (string def in new[] { "Turret_MiniTurret", "StandingLamp", "Nivarian_DisposableSentryTurret", "DroneHub", "turret_cryo", null })
                Check(!(bool)Method("Target").Invoke(null,new object[]{def}), "exclude " + (def ?? "null"));
            float[] jitter = Enumerable.Range(0, 1000).Select(i => i % 2 == 0 ? 1.2f : 0.2f).ToArray();
            int oldChanges = Transitions(false, jitter), newChanges = Transitions(true, jitter);
            Console.WriteLine("REPRO nativeTransitions=" + oldChanges + " candidateTransitions=" + newChanges);
            Check(oldChanges == 1000 && newChanges == 0, "low-supply oscillation reproduction");
            Check(Transitions(true,new[]{0f,59f,60f,2f,1f,0f,1f,60f}) == 3, "recover, real outage, accumulate, recover");
            Check(Transitions(true,Enumerable.Repeat(1000f,1000)) == 1, "stable supply does not cycle");
            Assembly core = Assembly.LoadFrom(Path.Combine(root,"Mods/3624805128/1.6/Assemblies/Nivarian_Race.dll"));
            Type adapter = core.GetType("Nivarian_Race.Code.Comps.BuildingComps.Comp_NivarianWirelessPowerAdapter",true);
            MethodInfo tick = adapter.GetMethod("TickWireless",BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo can = adapter.GetMethod("CanPowerNow");
            var originalTick = PatchProcessor.GetOriginalInstructions(tick).ToList();
            var rewrittenTick = Rewrite("Tick", originalTick.Select(c=>new CodeInstruction(c)).ToList());
            Check(rewrittenTick.Count(c => c.Calls(Method("SetPower"))) == 3, "exact installed start and two stop paths wrapped");
            Check(rewrittenTick.Count == originalTick.Count + 6, "payment/research/outage code retained");
            var originalCan = PatchProcessor.GetOriginalInstructions(can).ToList();
            var rewrittenCan = Rewrite("CanPower", originalCan.Select(c=>new CodeInstruction(c)).ToList());
            Check(rewrittenCan.Count(c=>c.Calls(Method("CheckPower"))) == originalCan.Count(c=>c.opcode == OpCodes.Ret), "all availability returns guarded");
            var malformed = originalTick.Select(c=>new CodeInstruction(c)).ToList();
            malformed.RemoveAt(malformed.FindIndex(c => (c.operand as MethodInfo) != null && ((MethodInfo)c.operand).Name == "set_PowerOn"));
            bool rejected = false;
            try { Rewrite("Tick",malformed); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "changed target layout rejected");
            // Compile Harmony's actual wrappers against the installed binary.
            // No game object, callback, payment or simulation method is executed.
            var harmony = new Harmony("meow.validation.turretwirelessrestart");
            try
            {
                harmony.Patch(tick,transpiler:new HarmonyMethod(Method("Tick")));
                harmony.Patch(can,transpiler:new HarmonyMethod(Method("CanPower")));
                Check(Harmony.GetPatchInfo(tick).Transpilers.Any(p=>p.PatchMethod == Method("Tick")), "TickWireless wrapper compiled");
                Check(Harmony.GetPatchInfo(can).Transpilers.Any(p=>p.PatchMethod == Method("CanPower")), "CanPowerNow wrapper compiled");
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Console.WriteLine("COMPLETE checks=" + checks + " runtime=UNVERIFIED");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
