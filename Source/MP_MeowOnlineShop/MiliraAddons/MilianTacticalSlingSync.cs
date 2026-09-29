using System;
using System.Collections;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat
{
    // Tactical Sling uses AncotLibrary's unsynchronized Command_Action to move
    // weapons between the pawn equipment tracker and the hediff container.
    [StaticConstructorOnStartup]
    public static class MilianTacticalSlingSync
    {
        private static Type tacticalSlingType;
        private static System.Reflection.MethodInfo switchAction;
        private static System.Reflection.FieldInfo compsField;

        static MilianTacticalSlingSync()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("milira") ||
                !MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification") ||
                MiliraMpCompatGate.ReferenceModActive) return;
            LongEventHandler.ExecuteWhenFinished(Install);
        }

        private static void Install()
        {
            tacticalSlingType = AccessTools.TypeByName("MilianModification.HediffComp_TacticalSling");
            var baseType = AccessTools.TypeByName("AncotLibrary.HediffComp_AlternateWeapon");
            switchAction = baseType == null ? null :
                AccessTools.DeclaredMethod(baseType, "<CompGetGizmos>b__18_0", Type.EmptyTypes);
            compsField = AccessTools.Field(typeof(HediffWithComps), "comps");
            if (tacticalSlingType == null || switchAction == null || compsField == null ||
                !baseType.IsAssignableFrom(tacticalSlingType))
            {
                Log.Error("[MilianTacticalSlingSync] REQUIRED_TARGET_FAILED: tactical sling action or hediff components missing");
                return;
            }

            MP.RegisterSyncMethod(typeof(MilianTacticalSlingSync), nameof(SwitchWeapon))
                .SetContext(SyncContext.CurrentMap);
            new Harmony("meow.milian.tacticalsling").Patch(switchAction,
                prefix: new HarmonyMethod(typeof(MilianTacticalSlingSync), nameof(BeforeSwitch)));
            Log.Message("[MilianTacticalSlingSync] READY: Ancot alternate weapon action for Milian tactical sling");
        }

        private static bool BeforeSwitch(HediffComp __instance)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface ||
                __instance.GetType() != tacticalSlingType) return true;

            var hediff = __instance.parent;
            var pawn = hediff?.pawn;
            var index = pawn?.health?.hediffSet?.hediffs.IndexOf(hediff) ?? -1;
            if (index < 0)
            {
                Log.Error("[MilianTacticalSlingSync] Cannot locate tactical sling hediff on pawn");
                return false;
            }
            SwitchWeapon(pawn, index);
            return false;
        }

        public static void SwitchWeapon(Pawn pawn, int hediffIndex)
        {
            var hediffs = pawn?.health?.hediffSet?.hediffs;
            if (hediffs == null || hediffIndex < 0 || hediffIndex >= hediffs.Count ||
                !(hediffs[hediffIndex] is HediffWithComps hediff)) return;

            var comps = compsField.GetValue(hediff) as IList;
            if (comps == null) return;
            foreach (var comp in comps)
            {
                if (comp != null && comp.GetType() == tacticalSlingType)
                {
                    switchAction.Invoke(comp, null);
                    return;
                }
            }
        }
    }
}
