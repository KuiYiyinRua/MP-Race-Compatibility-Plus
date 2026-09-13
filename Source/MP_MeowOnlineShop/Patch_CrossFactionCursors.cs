using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace MP_MeowOnlineShop
{
    // Only the cursor renderer's faction comparison is changed. No player or game state is written.
    internal static class Patch_CrossFactionCursors
    {
        internal static void Apply(Harmony harmony)
        {
            var target = AccessTools.DeclaredMethod(AccessTools.TypeByName("Multiplayer.Client.DrawPlayerCursors"), "Postfix", Type.EmptyTypes);
            if (target == null)
            {
                Log.Warning("[MP-MeowOnlineShop] Cross-faction cursors: renderer unavailable; original behavior retained.");
                return;
            }
            try
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(Patch_CrossFactionCursors), nameof(Transpiler)));
            }
            catch (Exception e)
            {
                Log.Warning("[MP-MeowOnlineShop] Cross-faction cursors disabled: " + e);
            }
        }

        private static bool CanShowFaction(int remoteFaction, int localFaction)
        {
            return remoteFaction == localFaction || MpMeowOnlineShopMod.Settings?.showCrossFactionCursors == true;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            var playerFaction = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.PlayerInfo"), "factionId");
            var realFaction = AccessTools.PropertyGetter(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "RealPlayerFaction");
            var loadId = AccessTools.Field(typeof(RimWorld.Faction), "loadID");
            int match = -1;
            int count = 0;
            for (int i = 0; i + 3 < code.Count; i++)
            {
                if (code[i].LoadsField(playerFaction) && code[i + 1].Calls(realFaction) && code[i + 2].LoadsField(loadId)
                    && (code[i + 3].opcode == OpCodes.Bne_Un || code[i + 3].opcode == OpCodes.Bne_Un_S))
                {
                    match = i + 3;
                    count++;
                }
            }
            if (count != 1)
            {
                Log.Warning("[MP-MeowOnlineShop] Cross-faction cursors: expected one faction comparison, found " + count + "; original renderer retained.");
                return code;
            }
            var branch = code[match];
            var call = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Patch_CrossFactionCursors), nameof(CanShowFaction)));
            call.labels.AddRange(branch.labels);
            call.blocks.AddRange(branch.blocks);
            code[match] = call;
            code.Insert(match + 1, new CodeInstruction(OpCodes.Brfalse, branch.operand));
            Log.Message("[MP-MeowOnlineShop] Cross-faction cursor renderer initialized (one faction comparison; local setting).");
            return code;
        }
    }
}
