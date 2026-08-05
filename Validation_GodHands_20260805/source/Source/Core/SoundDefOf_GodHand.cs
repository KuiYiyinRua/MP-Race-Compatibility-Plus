using RimWorld;
using Verse;

namespace GodHandMod
{
    [DefOf]
    public static class SoundDefOf_GodHand
    {
        public static SoundDef MEME;
        
        static SoundDefOf_GodHand()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(SoundDefOf_GodHand));
        }
    }
}
