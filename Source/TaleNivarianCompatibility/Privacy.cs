using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    internal static class Privacy
    {
        static Type component;
        static FieldInfo invitation, exclamation, timestampOffset;
        internal static void Apply(Harmony harmony)
        {
            // These three Unity draws select simulation jobs. No random scope: consume the map stream normally.
            NivarianSimulationRandom.Patch(harmony, "Privacy_Please.PrivacyUtility", "PrivacyCheckForPawn");
            NivarianSimulationRandom.Patch(harmony, "Privacy_Please.SexInteractionUtility", "GetReactionsToSexAct");
            NivarianSimulationRandom.Patch(harmony, "Privacy_Please.HarmonyPatch_JobDriver_Sex_setup_ticks", "Postfix");
            component = AccessTools.TypeByName("Privacy_Please.CompPawnThoughtData") ?? throw new TypeLoadException("Privacy thought component");
            invitation = AccessTools.Field(component, "lastInvitationTick") ?? throw new MissingFieldException("Privacy.lastInvitationTick");
            exclamation = AccessTools.Field(component, "lastExclaimationTick") ?? throw new MissingFieldException("Privacy.lastExclaimationTick");
            timestampOffset = AccessTools.Field(AccessTools.TypeByName("Multiplayer.Client.Patches.TimestampFixer"), "currentOffset") ?? throw new MissingFieldException("MP timestamp offset");
            // The installed component inherits this method. Save/load only, not an extra pawn-tick hook.
            if (AccessTools.DeclaredMethod(component, "PostExposeData") != null) throw new InvalidOperationException("Privacy serialization changed; inspect before applying");
            harmony.Patch(AccessTools.DeclaredMethod(typeof(ThingComp), nameof(ThingComp.PostExposeData)), postfix: new HarmonyMethod(typeof(Privacy), nameof(Expose)));
            Log.Message("[TaleNivarianCompat] Privacy: 3 simulation RNG sites and invitation/exclamation save state installed.");
        }
        static void Expose(ThingComp __instance)
        {
            if (!component.IsInstanceOfType(__instance)) return;
            int invite = (int)invitation.GetValue(__instance), exclaim = (int)exclamation.GetValue(__instance);
            if (MP.IsInMultiplayer && Scribe.mode == LoadSaveMode.Saving && timestampOffset.GetValue(null) is int offset)
            {
                // Original cooldown checks treat -1 as an actual timestamp, not a sentinel. Shift it too.
                invite = unchecked(invite + offset);
                exclaim = unchecked(exclaim + offset);
                invitation.SetValue(__instance, invite);
                exclamation.SetValue(__instance, exclaim);
            }
            Scribe_Values.Look(ref invite, "meowPrivacyLastInvitation", -1);
            Scribe_Values.Look(ref exclaim, "meowPrivacyLastExclamation", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                invitation.SetValue(__instance, invite);
                exclamation.SetValue(__instance, exclaim);
            }
        }
    }
}
