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
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); checks++; }
    static MethodInfo M(Type type, string name) => AccessTools.DeclaredMethod(type, name);
    static List<CodeInstruction> IL(MethodBase method) => PatchProcessor.GetOriginalInstructions(method).ToList();
    static bool Calls(IEnumerable<CodeInstruction> code, string name) => code.Any(i => i.operand is MethodBase m && m.Name == name);
    static List<CodeInstruction> Rewrite(Type type, string name, IEnumerable<CodeInstruction> code) =>
        ((IEnumerable<CodeInstruction>)M(type, name).Invoke(null, new object[] { code })).ToList();
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception e) { for (; e != null; e = e.InnerException) Console.WriteLine("FAIL " + e); return 1; }
    }
    static void Run(string[] args)
    {
        string game = Path.GetFullPath(args[0]), evidence = Path.GetFullPath(args[1]), mods = Path.Combine(game, "Mods");
        var dirs = new[] { Path.Combine(game,"RimWorldWin64_Data/Managed"),
            Path.Combine(mods,"Multiplayer/1.6/Assemblies"), Path.Combine(mods,"Multiplayer/1.6/AssembliesCustom"),
            Path.Combine(mods,"MP-meow-online-shop/1.6/Assemblies"), Path.Combine(mods,"3595247479/1.6/Assemblies"),
            Path.Combine(mods,"3781005562/Assemblies"), Path.Combine(mods,"2023507013/1.6/Assemblies"),
            Path.Combine(mods,"2842502659/1.6/Assemblies"), Path.Combine(mods,"3624805128/1.6/Assemblies") };
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
            string file = new AssemblyName(e.Name).Name + ".dll";
            var p = dirs.Select(d => Path.Combine(d,file)).FirstOrDefault(File.Exists);
            return p == null ? null : Assembly.LoadFrom(p);
        };
        var rw = Assembly.LoadFrom(Path.Combine(dirs[0],"Assembly-CSharp.dll"));
        var flight = Assembly.LoadFrom(Path.Combine(dirs[4],"ChezhouLib.dll"));
        var raven = Assembly.LoadFrom(Path.Combine(dirs[5],"ZuoYao_RavenRace.dll"));
        var ballzPath = Directory.GetDirectories(mods).Select(d => Path.Combine(d,"1.6/Assemblies/AutoOrganAdder.dll")).Single(File.Exists);
        var ballz = Assembly.LoadFrom(ballzPath);
        var vpe = Assembly.LoadFrom(Path.Combine(dirs[7],"VanillaPsycastsExpanded.dll"));
        var candidate = Assembly.LoadFrom(Path.Combine(evidence,"Candidate/DesyncBatch/Meow.DesyncBatchCompatibility.dll"));
        var tale = Assembly.LoadFrom(Path.Combine(evidence,"Candidate/TaleNivarian/Meow.TaleNivarianCompatibility.dll"));
        foreach (var a in new[] { rw, flight, raven, ballz, vpe, candidate, tale }) Console.WriteLine("MVID " + a.GetName().Name + " " + a.ManifestModule.ModuleVersionId);
        Check(rw.ManifestModule.ModuleVersionId == new Guid("239ae808-e7f5-427a-a802-2d09e89ee1ed"), "exact logged game binary");
        Check(raven.ManifestModule.ModuleVersionId == new Guid("281a0ff6-4e15-43eb-9464-bc73ec0c79ab"), "exact logged Raven binary");
        Check(ballz.ManifestModule.ModuleVersionId == new Guid("e986f0da-b06d-4a6c-9467-85341291d6c7"), "exact logged Ballz binary");
        var boundary = candidate.GetType("Meow.DesyncBatchCompatibility.FlightSimulationBoundary",true);
        var visual = flight.GetType("ChezhouLib.Patch.Patch_FlyingVisualEffects+Patch_FlyingDrawPos",true);
        var fly = flight.GetType("ChezhouLib.ClThingComp.ThingComp_RaceFly",true);
        var original = IL(M(visual,"Postfix"));
        Check(Calls(original,"get_realtimeSinceStartup") && Calls(original,"Sin") && original.Any(i => i.operand is FieldInfo f && f.Name == "z" && i.opcode == OpCodes.Ldflda) && original.Any(i => i.opcode == OpCodes.Stind_R4), "flight realtime offset changes physical z");
        Check(Calls(IL(M(rw.GetType("Verse.Verb_LaunchProjectile",true),"TryCastShot")),"get_DrawPos"), "shot consumes drawing position");
        AccessTools.Field(boundary,"unfog").SetValue(null,M(visual,"UnfogAround"));
        var rewritten = Rewrite(boundary,"NoVisualUnfog",original);
        Check(!Calls(rewritten,"UnfogAround") && Calls(rewritten,"UnfogInSinglePlayer"), "render unfog redirected without removing hover");
        Check(Calls(rewritten,"get_realtimeSinceStartup"), "interface animation retained");
        Check(Calls(IL(M(boundary,"VisualOnly")),"get_InInterface") && Calls(IL(M(boundary,"VisualOnly")),"get_IsInMultiplayer"), "visual offset excluded at simulation boundary");
        Check(M(fly,"CompTick") != null && AccessTools.Field(fly,"isActionFiy").FieldType == typeof(bool), "flight tick and active state resolved");
        Check(Calls(IL(M(boundary,"TickUnfog")),"get_Position") && !Calls(IL(M(boundary,"TickUnfog")),"get_DrawPos"), "simulation unfog uses physical cell");
        var failures = candidate.GetType("Meow.DesyncBatchCompatibility.LoggedBoundaryFailures",true);
        var organ = ballz.GetType("Ballz.Patch_PreventGonadDestruction",true);
        var source = IL(M(organ,"Prefix"));
        Check(Calls(source,"get_Name") && Calls(source,"get_ToStringShort") && Calls(source,"RemoveHediff"), "unnamed-animal exception follows a real health mutation");
        var safe = Rewrite(failures,"SafeAnimalName",source);
        Check(Calls(safe,"AnimalLabel") && !Calls(safe,"get_ToStringShort"), "unsafe name chain removed");
        foreach (var call in new[] { "RemoveHediff", "RemoveTesticleGear", "AddHediff", "Message" })
            Check(Calls(safe,call), "Ballz side effect retained: " + call);
        foreach (var pair in new[] { Tuple.Create(boundary,"NoVisualUnfog"), Tuple.Create(failures,"SafeAnimalName") })
        {
            bool rejected = false;
            try { Rewrite(pair.Item1,pair.Item2,new CodeInstruction[0]); }
            catch (Exception) { rejected = true; }
            Check(rejected, pair.Item2 + " rejects missing target rather than claiming success");
        }
        var getter = M(raven.GetType("RavenRace.Features.UnityEffects.MapComponent_RavenUnityEffectUpdater",true),"Get");
        Check(getter.GetParameters().Single().ParameterType == rw.GetType("Verse.Map",true), "Raven visual map getter signature");
        Check(IL(M(failures,"LiveEffectMap")).Any(i => i.operand is FieldInfo f && f.Name == "components"), "Raven guard checks actual missing component list");
        var give = M(rw.GetType("Verse.DebugToolsPawns",true),"GivePsylink");
        var vanillaGive = IL(give);
        var vpeGive = Rewrite(vpe.GetType("VanillaPsycastsExpanded.DebugToolsPawns_GivePsylink",true),"Transpiler",vanillaGive);
        Check(vanillaGive.Count(i => i.operand is FieldInfo f && f.Name == "maxSeverity") == 1 && !vpeGive.Any(i => i.operand is FieldInfo f && f.Name == "maxSeverity"), "reproduced VPE removal that aborted compatibility initialization");
        var capApply = IL(M(tale.GetType("Meow.TaleNivarianCompatibility.NivarianPsionicCap",true),"Apply"));
        Check(!capApply.Any(i => Equals(i.operand,"GivePsylink")) && capApply.Any(i => Equals(i.operand,"UpdateAbilitiesAndPsylink")), "irrelevant debug reader removed, attunement reader retained");
        Check(!candidate.GetReferencedAssemblies().Any(a => a.Name == "Multiplayer"), "no fragile MP internal assembly reference");
        var imperium = Assembly.LoadFrom(Path.Combine(mods,"3588393755/1.6/Assemblies/MiliraImperium.dll"));
        Console.WriteLine("MVID MiliraImperium " + imperium.ManifestModule.ModuleVersionId);
        var dialog = imperium.GetType("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window",true);
        var service = imperium.GetType("MiliraImperium.MiliraImperiumTransactionService",true);
        var donation = candidate.GetType("Meow.DesyncBatchCompatibility.ImperiumDonation",true);
        Check(Calls(IL(M(dialog,"RunDonation")),"TryDonate"), "UI directly executes donation transaction");
        Check(Calls(IL(M(service,"TryDonate")),"TryConsumeResource") && Calls(IL(M(service,"TryDonate")),"GainFavor"), "donation combines payment and favor mutation");
        Check(Calls(IL(M(service,"TryConsumeResource")),"HasLaunchableResource") && Calls(IL(M(service,"TryConsumeResource")),"LaunchThingsOfType"), "original transaction revalidates balance before payment");
        var entry = M(dialog,"RunDonation").GetParameters().Single().ParameterType;
        Check(M(entry,"get_ResourceKind") != null && M(entry,"get_Amount").ReturnType == typeof(int), "boxed donation entry getters resolve");
        foreach (var name in new[] { "GetReferenceMap", "GetNegotiator", "GetMiliraFaction", "InvalidateDonationSnapshot" })
            Check(M(dialog,name) != null, "donation UI target " + name);
        Check(M(donation,"Donate").GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(new[] { "Map", "Pawn", "Faction", "Int32", "Int32" }), "sync payload has explicit context and no local window");
        Check(Calls(IL(M(donation,"QueueDonation")),"Donate") && Calls(IL(M(donation,"QueueDonation")),"get_InInterface"), "UI queues whole transaction through sync wrapper");
        Check(Calls(IL(M(donation,"Apply")),"RegisterSyncMethod"), "donation command registered");
        var reward = M(service,"GetDonationFavorReward");
        var kind = imperium.GetType("MiliraImperium.MiliraImperiumTransactionService+ResourceKind",true);
        Check((int)reward.Invoke(null,new object[] { Enum.ToObject(kind,0),5000 }) == 5 &&
            (int)reward.Invoke(null,new object[] { Enum.ToObject(kind,1),500 }) == 5, "silver and gold retain original favor rates");
        Console.WriteLine("OFFLINE_SURFACE_PASS checks=" + checks + "; no game launched");
    }
}
