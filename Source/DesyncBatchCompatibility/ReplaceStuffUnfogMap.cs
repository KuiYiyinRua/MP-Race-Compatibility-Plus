using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    // Desync-452: UnfogWorker(map 0) read the local camera's map through
    // FogUtil.IsUnderFog(Thing), then destroyed an install blueprint on one peer.
    // Keep preview checks local; fix only the native simulation unfog callback.
    internal static class ReplaceStuffUnfogMap
    {
        static MethodInfo nativeFog;
        internal static void Apply(Harmony harmony)
        {
            var utility = AccessTools.TypeByName("Replace_Stuff.OverMineable.FogUtil")
                ?? throw new TypeLoadException("Replace_Stuff.OverMineable.FogUtil");
            nativeFog = AccessTools.DeclaredMethod(utility, "IsUnderFog", new[] { typeof(Thing) })
                ?? throw new MissingMethodException(utility.FullName, "IsUnderFog(Thing)");
            var fix = AccessTools.TypeByName("Replace_Stuff.OverMineable.UnFogFix")
                ?? throw new TypeLoadException("Replace_Stuff.OverMineable.UnFogFix");
            var callback = AccessTools.DeclaredMethod(fix, "Postfix", new[] { typeof(FogGrid), typeof(IntVec3), typeof(Map) })
                ?? throw new MissingMethodException(fix.FullName, "Postfix(FogGrid,IntVec3,Map)");
            harmony.Patch(callback,
                transpiler: new HarmonyMethod(typeof(ReplaceStuffUnfogMap), nameof(OwnerMap)));
            Log.Message("[ReplaceStuffUnfogMap] resolved=1 native unfog simulation check; missing=0");
        }

        static IEnumerable<CodeInstruction> OwnerMap(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(nativeFog)) { yield return instruction; continue; }
                var load = new CodeInstruction(OpCodes.Ldarg_2);
                load.labels.AddRange(instruction.labels);
                load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ReplaceStuffUnfogMap), nameof(IsUnderFog)));
                count++;
            }
            if (count != 1) throw new InvalidOperationException("Native unfog check shape changed: " + count);
        }

        static bool IsUnderFog(Thing thing, Map map)
        {
            if (!MP.IsInMultiplayer) return (bool)nativeFog.Invoke(null, new object[] { thing });
            foreach (var cell in GenAdj.OccupiedRect(thing.Position, thing.Rotation, thing.def.Size))
                if (map.fogGrid.IsFogged(cell)) return true;
            return false;
        }
    }
}
