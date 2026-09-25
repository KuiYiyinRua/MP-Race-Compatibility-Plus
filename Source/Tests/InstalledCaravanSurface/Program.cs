using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Pass game root and candidate DLL path");
        var gameRoot = Path.GetFullPath(args[0]);
        var candidatePath = Path.GetFullPath(args[1]);
        var search = new[] {
            Path.Combine(gameRoot, "RimWorldWin64_Data", "Managed"),
            Path.Combine(gameRoot, "Mods", "Multiplayer", "1.6", "Assemblies"),
            Path.Combine(gameRoot, "Mods", "Multiplayer", "1.6", "AssembliesCustom"),
            Path.GetDirectoryName(candidatePath)
        };
        AppDomain.CurrentDomain.AssemblyResolve += (_, ev) => {
            var name = new AssemblyName(ev.Name).Name + ".dll";
            var path = search.Select(dir => Path.Combine(dir, name)).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        var game = Assembly.LoadFrom(Path.Combine(search[0], "Assembly-CSharp.dll"));
        var mp = Assembly.LoadFrom(Path.Combine(search[2], "Multiplayer.dll"));
        var candidate = Assembly.LoadFrom(candidatePath);
        Console.WriteLine("GAME_MVID=" + game.ManifestModule.ModuleVersionId);
        Console.WriteLine("MP_MVID=" + mp.ManifestModule.ModuleVersionId);
        Console.WriteLine("CANDIDATE_MVID=" + candidate.ManifestModule.ModuleVersionId);
        var dialog = game.GetType("RimWorld.Dialog_FormCaravan", true);
        var refresh = AccessTools.DeclaredMethod(dialog, "Notify_TransferablesChanged", Type.EmptyTypes);
        var code = PatchProcessor.GetOriginalInstructions(refresh).ToList();
        var getter = AccessTools.PropertyGetter(game.GetType("Verse.ModsConfig", true), "BiotechActive");
        if (code.Count(c => c.Calls(getter)) != 1) throw new Exception("Unexpected escort block count");
        var force = AccessTools.Method(game.GetType("RimWorld.Transferable", true), "ForceToDestination");
        if (code.Count(c => c.Calls(force)) != 2) throw new Exception("Unexpected escort add/remove call count");
        var patch = candidate.GetType("MP_MeowOnlineShop.Patch_CaravanEscortSelectionMp", true);
        var transpiler = AccessTools.Method(patch, "EscortRefreshTranspiler");
        var rewritten = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { code })).ToList();
        if (rewritten.Count != code.Count + 1 || rewritten.Any(c => c.Calls(getter)))
            throw new Exception("Candidate did not replace exactly one gate");
        if (rewritten.Count(c => c.Calls(force)) != 2) throw new Exception("Candidate changed vanilla escort mutations");
        foreach (var name in new[] { "TryReformCaravan", "TryFormAndSendCaravan", "DebugTryFormCaravanInstantly" })
            if (AccessTools.DeclaredMethod(dialog, name, Type.EmptyTypes) == null) throw new MissingMethodException(name);
        var session = mp.GetType("Multiplayer.Client.CaravanFormingSession", true);
        foreach (var name in new[] { "PrepareDummyDialog", "AddItems", "Reset", "Notify_CountChanged" })
            if (AccessTools.DeclaredMethod(session, name) == null) throw new MissingMethodException(name);
        if (AccessTools.Field(session, "faction") == null || !dialog.IsAssignableFrom(mp.GetType("Multiplayer.Client.CaravanFormingProxy", true)))
            throw new Exception("Unexpected session/proxy contract");
        var context = mp.GetType("Multiplayer.Client.FactionContext", true);
        if (AccessTools.Method(context, "Push", new[] { game.GetType("RimWorld.Faction", true), typeof(bool) }) == null ||
            AccessTools.Method(context, "Pop", Type.EmptyTypes) == null) throw new Exception("Missing faction push/pop");
        Console.WriteLine("INSTALLED_SURFACE_PASS exact game IL, candidate transpiler, 7 patch targets, session/proxy/faction signatures; no game methods executed");
        return 0;
    }
}
