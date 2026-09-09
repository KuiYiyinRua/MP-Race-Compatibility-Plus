using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    internal static class Recruitment
    {
        static ISyncMethod recruit;
        static Type target;
        internal static void Apply(Harmony harmony)
        {
            target=AccessTools.TypeByName("Nivarian.GameComp_NivarianRecruitment");
            if(target==null)throw new TypeLoadException("Nivarian recruitment component missing");
            recruit=MP.RegisterSyncMethod(typeof(Recruitment),nameof(Recruit)).SetContext(SyncContext.CurrentMap);
            harmony.Patch(AccessTools.Method(target,"EnsureCandidates"),prefix:new HarmonyMethod(typeof(Recruitment),nameof(EnsurePrefix)));
            harmony.Patch(AccessTools.Method(target,"TryRecruit",new[]{typeof(Pawn)}),prefix:new HarmonyMethod(typeof(Recruitment),nameof(RecruitPrefix)));
        }
        // The existing 97-tick simulation callback owns candidate generation.
        // Reading the tab must not generate pawns or consume simulation Rand.
        static bool EnsurePrefix()=>!MP.IsInMultiplayer||!MP.InInterface;
        static bool RecruitPrefix(GameComponent __instance,Pawn candidate,ref bool __result)
        {
            if(!MP.IsInMultiplayer||!MP.InInterface)return true;
            var candidates=(IList)AccessTools.Field(target,"_candidates").GetValue(__instance);
            int index=candidates.IndexOf(candidate);
            if(index<0||candidate==null){__result=false;return false;}
            recruit.DoSync(null,__instance,index,candidate.thingIDNumber);
            __result=true;
            return false;
        }
        static void Recruit(GameComponent component,int index,int pawnId)
        {
            var candidates=(IList)AccessTools.Field(target,"_candidates").GetValue(component);
            // A refresh or another player's recruitment can invalidate a pending selection.
            if(index<0||index>=candidates.Count)return;
            var pawn=candidates[index] as Pawn;
            if(pawn==null||pawn.thingIDNumber!=pawnId)return;
            bool success=(bool)AccessTools.Method(target,"TryRecruit").Invoke(component,new object[]{pawn});
            if(!success)Messages.Message("Nivarian.Uplink.Recruitment.NotEnoughSilver".Translate(
                new NamedArgument(AccessTools.Method(target,"RecruitPrice").Invoke(null,new object[]{pawn}),null)),RimWorld.MessageTypeDefOf.RejectInput,false);
        }
    }
}
