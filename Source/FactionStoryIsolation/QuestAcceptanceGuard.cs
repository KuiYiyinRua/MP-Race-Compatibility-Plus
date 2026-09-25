using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;
using MpClient = Multiplayer.Client.Multiplayer;

namespace Meow.FactionStoryIsolation
{
    internal sealed class QuestOwnership
    {
        internal int Version;
        internal int OwnerId = -1;
        // Diagnostic state only; never used for simulation decisions.
        internal readonly HashSet<int> ReportedActors = new HashSet<int>();
    }

    internal static class QuestAcceptanceGuard
    {
        [System.ThreadStatic] private static bool rejectAcceptContext;
        private static readonly ConditionalWeakTable<Quest, QuestOwnership> Records = new ConditionalWeakTable<Quest, QuestOwnership>();
        internal static QuestOwnership Record(Quest quest) => Records.GetValue(quest, _ => new QuestOwnership());
        private static bool MultiplayerRoom => MP.IsInMultiplayer && MpClient.GameComp?.multifaction == true;
        private static bool Enabled => Bootstrap.Ready && MultiplayerRoom
            && Current.Game?.GetComponent<IsolationSession>()?.RoutingEnabled == true;

        // Patch the MP hiding prefix, not ShouldListNow itself: returning true
        // from MP's prefix lets vanilla retain hidden-quest and tab filtering.
        internal static bool KeepOtherFactionQuestsVisible(ref bool __result)
        {
            if (!Enabled) return true;
            __result = true;
            return false;
        }

        internal static void Created(Quest __result)
        {
            // Capture even when protection is off, so later rehosting can enable it.
            // Never derive simulation ownership from the viewing player's faction.
            if (__result == null || !MultiplayerRoom || !(MpClient.Ticking || MpClient.ExecutingCmds)) return;
            var owner = Faction.OfPlayer;
            if (owner?.def.isPlayer != true || owner == MpClient.WorldComp?.spectatorFaction) return;
            var record = Record(__result);
            record.OwnerId = owner.loadID;
            record.Version = 1;
        }

        internal static void Expose(Quest __instance)
        {
            // Runs even with the switch off: saving must not lose known ownership.
            var record = Record(__instance);
            Scribe_Values.Look(ref record.Version, "meowAcceptanceOwnerVersion", 0);
            Scribe_Values.Look(ref record.OwnerId, "meowAcceptanceOwnerFactionId", -1);
        }

        internal static bool Accept(Quest __instance, Pawn by, ref bool __state)
        {
            __state = rejectAcceptContext;
            rejectAcceptContext = !Check(__instance, by);
            return !rejectAcceptContext;
        }
        internal static void AcceptFinalizer(bool __state) => rejectAcceptContext = __state;
        internal static bool AutoAccept(Quest __instance) => Check(__instance, null);
        // This patches MP's prefix method itself. __0 is its quest argument,
        // whereas __instance would refer to the (static) prefix's declaring type.
        internal static bool AcceptMapContext(Quest __0) => !rejectAcceptContext && Check(__0, null);

        private static bool Check(Quest quest, Pawn by)
        {
            if (!Enabled) return true;
            // The MP command executor establishes Faction.OfPlayer before calling
            // Accept. This prefix runs before MP's quest-map context prefix.
            bool simulation = MpClient.Ticking || MpClient.ExecutingCmds;
            var actor = simulation ? Faction.OfPlayer : MpClient.RealPlayerFaction;
            var record = Record(quest);
            string reason = null;
            if (record.Version != 1 || record.OwnerId < 0)
                reason = "该任务缺少可验证的派系归属，已阻止接取。旧任务需要归属迁移后才能在保护开启时接取。";
            else if (actor?.def.isPlayer != true || actor == MpClient.WorldComp?.spectatorFaction || actor.loadID != record.OwnerId)
                reason = "该任务属于另一个玩家派系，不能由当前派系接取。";
            else if (by != null && by.Faction?.loadID != record.OwnerId)
                reason = "接取人物不属于该任务的玩家派系，已阻止接取。";
            if (reason == null) return true;

            if (!simulation && MP.InInterface)
                Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
            if (record.ReportedActors.Add(actor?.loadID ?? -1))
                Log.Warning("[Meow.FactionStoryIsolation] Quest accept denied: quest=" + quest.id
                    + " owner=" + record.OwnerId + " actor=" + (actor?.loadID ?? -1) + " schema=" + record.Version + ". " + reason);
            return false;
        }
    }
}
