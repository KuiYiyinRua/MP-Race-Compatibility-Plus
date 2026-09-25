using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

class Program
{
    static int checks;
    static void Check(bool value, string text) { if (!value) throw new Exception("FAIL " + text); Console.WriteLine("PASS " + text); checks++; }
    static List<CodeInstruction> IL(MethodBase method) => PatchProcessor.GetOriginalInstructions(method).ToList();
    static bool Calls(IEnumerable<CodeInstruction> code, string type, string name) => code.Any(i => i.operand is MethodBase m && m.DeclaringType.FullName == type && m.Name == name);
    static MethodInfo Method(Type type, string name) => AccessTools.DeclaredMethod(type, name);
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception e)
        {
            for (var current = e; current != null; current = current.InnerException)
                Console.WriteLine("ERROR " + current.GetType().FullName + ": " + current.Message);
            Environment.ExitCode = 1;
        }
    }
    static void Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("game root, romance DLL, candidate DLL");
        string game = Path.GetFullPath(args[0]), candidatePath = Path.GetFullPath(args[2]), mods = Path.Combine(game, "Mods");
        string[] dirs = { Path.Combine(game, "RimWorldWin64_Data/Managed"), Path.Combine(mods, "Multiplayer/1.6/Assemblies"), Path.Combine(mods, "Multiplayer/1.6/AssembliesCustom"), Path.Combine(mods, "MP-meow-online-shop/1.6/Assemblies"), Path.GetDirectoryName(candidatePath) };
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => { string name = new AssemblyName(e.Name).Name + ".dll"; string file = dirs.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists); return file == null ? null : Assembly.LoadFrom(file); };
        var rw = Assembly.LoadFrom(Path.Combine(dirs[0], "Assembly-CSharp.dll"));
        var al = Assembly.LoadFrom(Path.Combine(mods, "3665997350/1.6/Assemblies/AriandelLibrary.dll"));
        var romance = Assembly.LoadFrom(Path.GetFullPath(args[1]));
        var mugirl = Assembly.LoadFrom(Path.Combine(mods, "3578882219/1.6/Assemblies/MugirlRace.dll"));
        var mp = Assembly.LoadFrom(Path.Combine(dirs[2], "Multiplayer.dll"));
        var candidate = Assembly.LoadFrom(candidatePath);
        foreach (var asm in new[] { rw, al, romance, mugirl, mp, candidate }) Console.WriteLine("MVID " + asm.GetName().Name + " " + asm.ManifestModule.ModuleVersionId);
        Check(rw.ManifestModule.ModuleVersionId == new Guid("239ae808-e7f5-427a-a802-2d09e89ee1ed"), "game binary matches traces");
        Check(al.ManifestModule.ModuleVersionId == new Guid("c893f806-cf21-4934-94f0-a6cfab146a63"), "Ariandel binary matches traces");
        Check(romance.ManifestModule.ModuleVersionId == new Guid("61ce596f-88bc-415b-ad45-fdd00aeb0b03"), "Romance binary matches traces");
        Type Patch(string name) => candidate.GetType("Meow.DesyncBatchCompatibility." + name, true);
        var text = Method(al.GetType("AriandelLibrary.DamageWorker_AddInjury_NoDamageFactor", true), "ThrowDamageMote");
        var snow = Method(romance.GetType("RomanceOnTheRim.JobDriver_SnowballFight", true), "ThrowObjectAt");
        Check(text.IsStatic && text.ReturnType == typeof(void) && string.Join(",", text.GetParameters().Select(p => p.ParameterType.Name)) == "Vector3,Map,String,Color", "damage text exact signature");
        Check(snow.IsStatic && snow.ReturnType == typeof(void) && string.Join(",", snow.GetParameters().Select(p => p.ParameterType.Name)) == "Pawn,IntVec3,FleckDef", "snowball exact signature");
        foreach (var emitter in new[] { text, snow })
        {
            var code = IL(emitter);
            Check(Calls(code, "Verse.GenView", "ShouldSpawnMotesAt"), "visibility gate " + emitter.Name);
            Check(Calls(code, "Verse.Rand", "Range"), "visual random calls " + emitter.Name);
            Check(!code.Any(i => i.operand is MethodBase m && new[] { "TakeDamage", "AddHediff", "GainJoy", "SetTarget", "EndJobWith" }.Contains(m.Name)), "no gameplay mutation inside emitter " + emitter.Name);
        }
        var visual = Patch("LightningVisuals");
        Check(Calls(IL(Method(visual, "Before")), "Verse.Rand", "PushState") && Calls(IL(Method(visual, "After")), "Verse.Rand", "PopState"), "visual scope balances RNG via finalizer");
        Check(IL(Method(visual, "Apply")).Any(i => Equals(i.operand, "ThrowDamageMote")) && IL(Method(visual, "ApplySnowball")).Any(i => Equals(i.operand, "ThrowObjectAt")), "both exact visual targets registered");
        var mount = mugirl.GetType("Mugirl.Comp_MugirlMount", true);
        var dismount = Method(mount, "TryDismount");
        Check(dismount.ReturnType == typeof(bool) && string.Join(",", dismount.GetParameters().Select(p => p.ParameterType.ToString())) == "System.Nullable`1[Verse.IntVec3],System.Boolean", "dismount exact optional argument signature");
        var dismountIL = IL(dismount);
        Check(Calls(dismountIL, "Mugirl.MountedCombatController", "NotifyDismounting") && Calls(dismountIL, "RimWorld.PawnUtility", "ForceWait"), "dismount executor includes combat and job side effects");
        var callers = mugirl.GetType("Mugirl.MountedPawnUtility", true).GetNestedTypes(AccessTools.all)
            .Concat(mount.GetNestedTypes(AccessTools.all)).Concat(new[] { mount }).SelectMany(t => t.GetMethods(AccessTools.allDeclared))
            .Where(m => m.GetMethodBody() != null && Calls(IL(m), mount.FullName, "TryDismount")).ToArray();
        Check(callers.Any(m => m.Name.Contains("GetMountedPawnGizmos")) && callers.Any(m => m.Name.Contains("CompGetGizmosExtra")), "both dismount gizmos reach the same registered executor");
        var reg = IL(Method(Patch("MugirlDismount"), "Apply"));
        Check(Calls(reg, "Multiplayer.API.MP", "RegisterSyncMethod") && reg.Any(i => Equals(i.operand, "TryDismount")), "candidate registers native dismount, no partial rollback");
        var templates = mp.GetType("Multiplayer.Client.SyncTemplates", true);
        Check(Calls(IL(Method(templates, "General")), "Multiplayer.Client.Multiplayer", "get_ShouldSync"), "installed MP uses ShouldSync gate for native method interception");
        var world = mp.GetType("Multiplayer.Client.AsyncTime.AsyncWorldTimeComp", true);
        var worldIL = IL(Method(world, "Tick"));
        int dispatch = worldIL.FindIndex(i => i.operand is MethodBase m && m.Name == "DoSingleTick");
        int increment = worldIL.FindIndex(i => i.opcode == OpCodes.Stfld && i.operand is FieldInfo f && f.Name == "worldTicks");
        Check(dispatch >= 0 && increment > dispatch, "installed worldTicks increments after component dispatch");
        var clock = Patch("WorldComponentClock");
        var utility = Method(rw.GetType("Verse.GameComponentUtility", true), "GameComponentTick");
        var rewrite = Method(clock, "Rewrite");
        var original = IL(utility);
        var rewritten = ((IEnumerable<CodeInstruction>)rewrite.Invoke(null, new object[] { original.Select(i => new CodeInstruction(i)).ToList() })).ToList();
        Check(!Calls(rewritten, "Verse.GameComponent", "GameComponentTick") && Calls(rewritten, clock.FullName, "TickComponent"), "actual dispatcher rewritten to scoped world clock");
        Check(rewritten.Count == original.Count, "dispatcher retains exception handlers and iteration order");
        foreach (int count in new[] { 0, 2 })
        {
            var changed = Enumerable.Range(0, count).Select(_ => new CodeInstruction(OpCodes.Callvirt, Method(rw.GetType("Verse.GameComponent", true), "GameComponentTick"))).ToArray();
            bool rejected = false;
            try { rewrite.Invoke(null, new object[] { changed }); } catch (TargetInvocationException e) { rejected = e.InnerException is InvalidOperationException; }
            Check(rejected, "changed dispatcher fails closed: call count " + count);
        }
        var wrapper = Method(clock, "TickComponent");
        Check(wrapper.GetMethodBody().ExceptionHandlingClauses.Any(c => c.Flags == ExceptionHandlingClauseOptions.Finally), "world clock always restores on component exception");
        Check(IL(wrapper).Count(i => i.operand is MethodBase m && m.Name == "DebugSetTicksGame") == 2 && !Calls(IL(wrapper), "Verse.Rand", "PushState"), "clock scope does not reseed or isolate simulation RNG");
        Check(candidate.GetName().Version == new Version(1, 0, 2, 0), "candidate version 1.0.2");
        Console.WriteLine("OFFLINE_SURFACE_PASS checks=" + checks + "; no game process or gameplay method executed");
    }
}
