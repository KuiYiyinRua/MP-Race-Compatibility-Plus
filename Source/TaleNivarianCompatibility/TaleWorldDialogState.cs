using System;
using System.Collections.Generic;
using Verse;

namespace Meow.TaleNivarianCompatibility
{
    // MP saves map dialogs, but Tale's world dialogs normally have no map context.
    // Keep the raid event identity beside the saved dialog ID; the native closure
    // captures its caravan but not its destroyed world site.
    public sealed class TaleWorldDialogState : GameComponent
    {
        List<int> dialogIds = new List<int>();
        List<int> siteIds = new List<int>();

        public TaleWorldDialogState(Game game) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref dialogIds, "taleRaidDialogIds", LookMode.Value);
            Scribe_Collections.Look(ref siteIds, "taleRaidSiteIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                dialogIds = dialogIds ?? new List<int>();
                siteIds = siteIds ?? new List<int>();
                if (dialogIds.Count != siteIds.Count)
                    throw new InvalidOperationException("Tale raid dialog identity save is incomplete");
            }
        }

        internal void Remember(int dialogId, int siteId)
        {
            var index = dialogIds.IndexOf(dialogId);
            if (index >= 0)
            {
                if (siteIds[index] != siteId)
                    throw new InvalidOperationException("Tale raid dialog ID reused for another event");
                return;
            }
            dialogIds.Add(dialogId);
            siteIds.Add(siteId);
        }

        internal bool TryGetSite(int dialogId, out int siteId)
        {
            var index = dialogIds.IndexOf(dialogId);
            siteId = index < 0 ? -1 : siteIds[index];
            return index >= 0;
        }
    }
}
