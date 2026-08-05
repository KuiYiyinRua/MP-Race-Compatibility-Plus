using RimWorld;
using Verse;

namespace GodHandMod
{
    [DefOf]
    public static class GodHandDefOf
    {
        public static ThingDef GodHand_TurretHead;
        public static ThingDef GodHand_TurretBase;
        public static ThingDef GodHand_PawnFlyer;
        public static ThingDef GodHand_GearboxGenerator;

        public static JobDef GodHand_ReloadTurretHead;
        public static JobDef GodHand_RefuelTurretHead;
        public static JobDef GodHand_MergeTurretHead;
        public static JobDef GodHand_InstallTurretModule;
        public static JobDef GodHand_HandCrankGearbox;

        public static HediffDef GodHand_TurretHeadBonus;
        public static HediffDef GodHand_StasisLock;
        public static HediffDef GodHand_ProtectionIndividual;
        public static HediffDef GodHand_IsActuallyABoat;
        public static HediffDef GodHand_OrificeStretchingStatus;
        public static JobDef GodHand_BeingFucked;

        public static ThoughtDef GodHand_BoatFilling;
        public static ThoughtDef GodHand_BoatEmptying;

        public static SoundDef GodHand_CogsLoop;
        public static SoundDef GodHand_PistonOut;
        public static SoundDef GodHand_PistonIn;

        static GodHandDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(GodHandDefOf));
        }
    }
}
