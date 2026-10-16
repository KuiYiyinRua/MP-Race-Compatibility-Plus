using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using HarmonyLib;
using Meow.AutosaveRecovery;

class Program
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    static string root;
    static string Temp(string value) { var path=Path.Combine(root,"Autosave-5"+AtomicReplayFile.UniqueSuffix()); File.WriteAllText(path,value); return path; }
    static int Main(string[] args)
    {
        try
        {
            root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            var gameRoot=Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (sender,e) => {
                var name=new AssemblyName(e.Name).Name+".dll";
                foreach(var dir in new[]{Path.Combine(gameRoot,"RimWorldWin64_Data/Managed"),Path.Combine(gameRoot,"Mods/Multiplayer/1.6/AssembliesCustom"),Path.Combine(gameRoot,"Mods/Multiplayer/1.6/Assemblies")}) {
                    var path=Path.Combine(dir,name); if(File.Exists(path)) return Assembly.LoadFrom(path);
                } return null;
            };
            return Run(gameRoot);
        } catch(Exception e) { Console.Error.WriteLine(e.GetType().FullName + ": " + e.Message); return 1; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Run(string gameRoot)
    {
        try
        {
            string dst=Path.Combine(root,"Autosave-5.zip"), stale=Path.Combine(root,"Autosave-5.tmp.zip");
            File.WriteAllText(dst,"old"); File.WriteAllText(stale,"interrupted");
            bool oldFails=false; try { using(var f=new FileStream(stale,FileMode.CreateNew)){} } catch(IOException) { oldFails=true; }
            Check(oldFails,"unfixed fixed-name write reproduces reported exception");
            var first=Temp("new"); AtomicReplayFile.Commit(new FileInfo(first),dst,null);
            Check(File.ReadAllText(dst)=="new" && File.ReadAllText(stale)=="interrupted","stale temp does not block or get destroyed");
            Check(!File.Exists(first),"successful commit consumes only its own temp");
            var blocked=Temp("recoverable"); bool threw=false;
            using(var held=File.Open(dst,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                try { AtomicReplayFile.Commit(new FileInfo(blocked),dst,null); } catch(IOException e) { threw=e.Message.Contains(blocked); }
                Check(threw && File.ReadAllText(dst)=="new" && File.ReadAllText(blocked)=="recoverable","persistent reader lock preserves both old save and recovery file");
            }
            var retry=Temp("retry"); AtomicReplayFile.Commit(new FileInfo(retry),dst,null);
            Check(File.ReadAllText(dst)=="retry" && File.Exists(blocked),"next save succeeds after prior commit failure");
            // Inject the observed ReplaceFile error; run the actual Win32 rename fallback.
            var fallback=Temp("fallback"); int injected=0;
            AtomicReplayFile.CommitCore(new FileInfo(fallback),dst,null,(s,d,b)=> {
                injected++; throw new IOException("ERROR_UNABLE_TO_REMOVE_REPLACED",unchecked((int)0x80070497));
            });
            Check(injected==1,"observed ReplaceFile failure reaches fallback");
            Check(File.ReadAllText(dst)=="fallback" && !File.Exists(fallback),"MoveFileEx fallback commits complete replacement");
            var names=Enumerable.Range(0,1000).Select(_=>AtomicReplayFile.UniqueSuffix()).ToArray();
            Check(names.Distinct().Count()==1000,"independent save attempts have distinct names");
            var mp=Assembly.LoadFrom(Path.Combine(gameRoot,"Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll"));
            var target=mp.GetType("Multiplayer.Client.Autosaving").GetMethod("SaveGameToFile_Overwrite");
            var original=PatchProcessor.GetOriginalInstructions(target);
            var changed=AutosaveRecoveryMod.Rewrite(original).ToList();
            Check(changed.Count(i=>i.Calls(AccessTools.Method(typeof(AtomicReplayFile),nameof(AtomicReplayFile.Commit))))==1 &&
                changed.Count(i=>i.Calls(AccessTools.Method(typeof(AtomicReplayFile),nameof(AtomicReplayFile.UniqueSuffix))))==1,"exact installed MP autosave IL rewrites both boundaries");
            bool reject=false; try { AutosaveRecoveryMod.Rewrite(changed); } catch(InvalidOperationException) { reject=true; }
            Check(reject,"unexpected IL fails closed");
            Console.WriteLine("COMPLETE assertions="+checks); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
