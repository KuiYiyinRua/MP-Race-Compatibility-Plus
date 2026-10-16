using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Meow.AutosaveRecovery
{
    public sealed class AutosaveRecoveryMod : Mod
    {
        public AutosaveRecoveryMod(ModContentPack content) : base(content)
        {
            if (!ModsConfig.IsActive("rwmt.multiplayer")) return;
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        private static void Install()
        {
            try
            {
                var target = AccessTools.Method(AccessTools.TypeByName("Multiplayer.Client.Autosaving"),
                    "SaveGameToFile_Overwrite", new[] { typeof(string), typeof(bool) });
                if (target == null) throw new MissingMethodException("Autosaving.SaveGameToFile_Overwrite");
                new Harmony("meow.autosaverecovery").Patch(target,
                    transpiler: new HarmonyMethod(typeof(AutosaveRecoveryMod), nameof(Rewrite)));
                Log.Message("[Meow.AutosaveRecovery] READY version=1.0.0 mvid=" +
                    typeof(AutosaveRecoveryMod).Assembly.ManifestModule.ModuleVersionId +
                    "; unique temporary replay + atomic commit recovery.");
            }
            catch (Exception e) { Log.Error("[Meow.AutosaveRecovery] FAILED to install: " + e); }
        }

        public static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            var replace = typeof(FileInfo).GetMethod(nameof(FileInfo.Replace), new[] { typeof(string), typeof(string) });
            int suffixes = 0, commits = 0;
            foreach (var instruction in code)
            {
                if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, ".tmp.zip"))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(AtomicReplayFile), nameof(AtomicReplayFile.UniqueSuffix));
                    suffixes++;
                }
                else if (instruction.Calls(replace))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(AtomicReplayFile), nameof(AtomicReplayFile.Commit));
                    commits++;
                }
            }
            if (suffixes != 1 || commits != 1)
                throw new InvalidOperationException("Unsupported autosave IL: suffixes=" + suffixes + ", commits=" + commits);
            return code;
        }
    }
}
