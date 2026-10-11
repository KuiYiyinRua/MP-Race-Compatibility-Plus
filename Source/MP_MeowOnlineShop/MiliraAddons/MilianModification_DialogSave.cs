using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace MP_MeowOnlineShop.MiliraAddonCompat;

[StaticConstructorOnStartup]
public static class MilianModification_DialogSave
{
    static FieldInfo available;

    static MilianModification_DialogSave()
    {
        if (!CompatibilityPatchCategories.IsEnabled("milira") || MiliraMpCompatGate.ReferenceModActive ||
            !MP.enabled || !ModsConfig.IsActive("Ancot.MilianModification")) return;
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            try
            {
                var utility = AccessTools.TypeByName("MilianModification.MermeisterDialogUtility") ?? throw new TypeLoadException("MermeisterDialogUtility");
                var component = AccessTools.TypeByName("MilianModification.MiliraGameComponent_MilianComponentRecipe") ?? throw new TypeLoadException("MilianComponentRecipe");
                available = AccessTools.DeclaredField(utility, "dialogAvailable");
                if (available?.FieldType != typeof(bool) || !available.IsStatic) throw new MissingFieldException("dialogAvailable");
                var constructor = AccessTools.Constructor(component, new[] { typeof(Game) }) ?? throw new MissingMethodException("MilianComponentRecipe(Game)");
                var expose = AccessTools.DeclaredMethod(component, "ExposeData") ?? throw new MissingMethodException("MilianComponentRecipe.ExposeData");
                var harmony = new Harmony("Meow.MilianModification.DialogSave");
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(MilianModification_DialogSave), nameof(Reset)));
                harmony.Patch(expose, postfix: new HarmonyMethod(typeof(MilianModification_DialogSave), nameof(Expose)));
                Log.Message("[MilianModification_DialogSave] REQUIRED_TARGETS_OK Mermeister availability persistence");
            }
            catch (Exception e) { Log.Error("[MilianModification_DialogSave] REQUIRED_TARGET_FAILED " + e); }
        });
    }

    static void Reset() => available.SetValue(null, true);

    static void Expose()
    {
        bool value = (bool)available.GetValue(null);
        Scribe_Values.Look(ref value, "meowMermeisterDialogAvailable", true);
        if (Scribe.mode == LoadSaveMode.LoadingVars) available.SetValue(null, value);
    }
}
