using System.Collections.Generic;
using Multiplayer.API;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    public sealed class MonolynCodexMenuComponent : GameComponent
    {
        // UI-only windows are deliberately neither saved nor sent to other peers.
        readonly Queue<Window> pending = new Queue<Window>();
        public MonolynCodexMenuComponent(Game game) { }
        internal void Enqueue(Window window) => pending.Enqueue(window);
        public override void GameComponentUpdate()
        {
            if (pending.Count == 0 || MP.IsExecutingSyncCommand || !MP.InInterface ||
                Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            while (pending.Count != 0) Find.WindowStack.Add(pending.Dequeue());
        }
    }
}
