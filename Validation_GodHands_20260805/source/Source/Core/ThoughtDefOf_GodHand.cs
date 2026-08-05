using RimWorld;
using Verse;

namespace GodHandMod
{
    [DefOf]
    public static class ThoughtDefOf_GodHand
    {
        public static ThoughtDef CaressedByGodHand;
        
        static ThoughtDefOf_GodHand()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ThoughtDefOf_GodHand));
        }
    }
    
    [DefOf]
    public static class ThingDefOf_GodHand
    {
        public static ThingDef Mote_HeadPat;
        public static ThingDef Mote_GodJudgmentFalling;
        
        static ThingDefOf_GodHand()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(ThingDefOf_GodHand));
        }
    }
}
