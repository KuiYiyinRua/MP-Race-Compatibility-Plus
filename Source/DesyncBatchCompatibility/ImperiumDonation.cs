using System;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.DesyncBatchCompatibility
{
    internal static class ImperiumDonation
    {
        static MethodInfo getMap, getPawn, getFaction, getKind, getAmount, invalidate, donate, notifyFailure;
        static Type resourceKind;

        internal static void Apply(Harmony harmony)
        {
            var dialog = Bootstrap.Type("MiliraImperium.Dialog_MiliraImperium_RoyalAid_Window");
            var entry = Bootstrap.Type(dialog.FullName + "+DonationEntry");
            var service = Bootstrap.Type("MiliraImperium.MiliraImperiumTransactionService");
            resourceKind = Bootstrap.Type(service.FullName + "+ResourceKind");
            getMap = Bootstrap.Method(dialog, "GetReferenceMap");
            getPawn = Bootstrap.Method(dialog, "GetNegotiator");
            getFaction = Bootstrap.Method(dialog, "GetMiliraFaction");
            getKind = Bootstrap.Method(entry, "get_ResourceKind");
            getAmount = Bootstrap.Method(entry, "get_Amount");
            invalidate = Bootstrap.Method(dialog, "InvalidateDonationSnapshot");
            donate = Bootstrap.Method(service, "TryDonate", typeof(Map), typeof(Pawn), typeof(Faction), resourceKind, typeof(int));
            notifyFailure = Bootstrap.Method(service, "NotifyFailure", donate.ReturnType);
            var run = Bootstrap.Method(dialog, "RunDonation", entry);
            MP.RegisterSyncMethod(typeof(ImperiumDonation), nameof(Donate));
            harmony.Patch(run, prefix: new HarmonyMethod(typeof(ImperiumDonation), nameof(QueueDonation)));
        }

        static bool QueueDonation(object __instance, object __0)
        {
            if (!MP.IsInMultiplayer || !MP.InInterface) return true;
            // Resolve local selection/window state only here; never serialize a Window.
            Donate((Map)getMap.Invoke(__instance, null), (Pawn)getPawn.Invoke(__instance, null),
                (Faction)getFaction.Invoke(null, null), Convert.ToInt32(getKind.Invoke(__0, null)),
                (int)getAmount.Invoke(__0, null));
            invalidate.Invoke(__instance, null);
            return false;
        }

        public static void Donate(Map map, Pawn pawn, Faction faction, int kind, int amount)
        {
            if (map == null || !Find.Maps.Contains(map) || pawn == null || pawn.Destroyed || faction == null ||
                amount <= 0 || !Enum.IsDefined(resourceKind, kind)) return;
            // The original transaction rechecks alliance, royalty and launchable balance at execution time.
            // Keep payment and favor together, including any resulting title/quest side effects.
            var result = donate.Invoke(null, new object[] { map, pawn, faction, Enum.ToObject(resourceKind, kind), amount });
            notifyFailure.Invoke(null, new[] { result });
        }
    }
}
