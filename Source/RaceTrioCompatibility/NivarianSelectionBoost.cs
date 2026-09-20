using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.RaceTrioCompatibility
{
    public sealed class NivarianFocusRecord : IExposable
    {
        public int PlayerId;
        public Faction Faction;
        public int TicksLeft;
        public List<Pawn> Pawns = new List<Pawn>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref PlayerId, "playerId");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref TicksLeft, "ticksLeft");
            Scribe_Collections.Look(ref Pawns, "pawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Pawns = Pawns ?? new List<Pawn>();
        }
    }

    public sealed class NivarianFocusMapState : MapComponent
    {
        internal int Clock;
        internal List<NivarianFocusRecord> Records = new List<NivarianFocusRecord>();
        public NivarianFocusMapState(Map map) : base(map) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref Clock, "meowFocusClock");
            Scribe_Collections.Look(ref Records, "meowFocusPlayers", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Records = Records ?? new List<NivarianFocusRecord>();
        }

        internal List<Pawn> SelectedForTick()
        {
            Clock++;
            Records.RemoveAll(r => r.TicksLeft <= 0 || r.Faction != map.ParentFaction);
            foreach (var record in Records) record.TicksLeft--;
            // Several players focusing the same pawn must still produce exactly one ramp per map tick.
            return Records.SelectMany(r => r.Pawns).Where(p => p != null && !p.Dead && p.Spawned && p.Map == map)
                .Distinct().OrderBy(p => p.thingIDNumber).ToList();
        }
    }

    internal static class NivarianSelectionBoost
    {
        internal static Func<Pawn, bool> IsNivarian;
        internal static ISyncMethod UpdateFocus;
        internal static PropertyInfo IsReplay, Simulating;
        internal static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Nivarian.MapComp_NivarianSelectionBoost");
            var tick = type == null ? null : AccessTools.DeclaredMethod(type, "MapComponentTick");
            var helper = AccessTools.TypeByName("Nivarian.Helper.NivarianHelper");
            var predicate = helper == null ? null : AccessTools.DeclaredMethod(helper, "IsNivarian", new[] { typeof(Pawn) });
            IsReplay = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "IsReplay");
            Simulating = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.TickPatch"), "Simulating");
            if (tick == null || predicate == null || IsReplay == null || Simulating == null)
                throw new MissingMethodException("Required Nivarian selection/MP replay targets missing");
            IsNivarian = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), predicate);
            UpdateFocus = MP.RegisterSyncMethod(typeof(NivarianSelectionBoost), nameof(SetFocus));
            harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(NivarianSelectionBoost), nameof(ReplaceSelection)));
            Log.Message("[NivarianFocusCompat] saved per-player focus, map-queue commands and 180-map-tick disconnect expiry installed.");
        }

        static void SetFocus(Map map, int playerId, List<Pawn> pawns)
        {
            if (map == null || !Find.Maps.Contains(map) || map.ParentFaction != Faction.OfPlayer) return;
            var state = map.GetComponent<NivarianFocusMapState>();
            state.Records.RemoveAll(r => r.PlayerId == playerId);
            var selected = (pawns ?? new List<Pawn>()).Where(p => p != null && p.Spawned && !p.Dead && p.Map == map && IsNivarian(p))
                .Distinct().OrderBy(p => p.thingIDNumber).ToList();
            if (selected.Count > 0) state.Records.Add(new NivarianFocusRecord
                { PlayerId = playerId, Faction = Faction.OfPlayer, TicksLeft = 180, Pawns = selected });
        }

        static List<Pawn> SelectedPawns(Selector selector, MapComponent component)
            => MP.IsInMultiplayer ? component.map.GetComponent<NivarianFocusMapState>().SelectedForTick() : selector.SelectedPawns;

        static IEnumerable<CodeInstruction> ReplaceSelection(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.PropertyGetter(typeof(Selector), nameof(Selector.SelectedPawns));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    var loadMapComponent = new CodeInstruction(OpCodes.Ldarg_0);
                    loadMapComponent.labels.AddRange(instruction.labels);
                    loadMapComponent.blocks.AddRange(instruction.blocks);
                    instruction.labels.Clear(); instruction.blocks.Clear();
                    yield return loadMapComponent;
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(NivarianSelectionBoost), nameof(SelectedPawns));
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Nivarian boost expected one local selection read, found " + count);
        }
    }

    // Local selection is input only. Network presence/selection packets never drive simulation ticks.
    public sealed class NivarianFocusInput : GameComponent
    {
        sealed class Sent { internal int Clock; internal List<int> Ids; }
        readonly Dictionary<Map, Sent> sent = new Dictionary<Map, Sent>();
        int previousPlayer = -1;
        public NivarianFocusInput(Game game) { }
        public override void GameComponentUpdate()
        {
            if (!MP.IsInMultiplayer || !MP.InInterface || NivarianSelectionBoost.UpdateFocus == null
                || (bool)NivarianSelectionBoost.IsReplay.GetValue(null) || (bool)NivarianSelectionBoost.Simulating.GetValue(null)) return;
            // Resolve only the local sender through the public API; never consult this list during replay.
            var matches = MP.GetPlayers().Where(p => p.Username == MP.PlayerName).ToList();
            if (matches.Count != 1) return;
            int player = matches[0].Id;
            if (previousPlayer != player) { sent.Clear(); previousPlayer = player; }
            var selected = Find.Selector.SelectedPawns.Where(p => p != null && p.Spawned && !p.Dead && NivarianSelectionBoost.IsNivarian(p))
                .Distinct().OrderBy(p => p.thingIDNumber).ToList();
            foreach (var stale in sent.Keys.Where(m => !Find.Maps.Contains(m)).ToList()) sent.Remove(stale);
            foreach (var map in Find.Maps)
            {
                if (map.ParentFaction != MP.RealPlayerFaction) continue;
                var state = map.GetComponent<NivarianFocusMapState>();
                var pawns = selected.Where(p => p.Map == map && map == Find.CurrentMap).ToList();
                var ids = pawns.Select(p => p.thingIDNumber).ToList();
                bool hadSent = sent.TryGetValue(map, out var previous);
                bool changed = hadSent ? !ids.SequenceEqual(previous.Ids)
                    : ids.Count > 0 || state.Records.Any(r => r.PlayerId == player);
                if (!changed && (ids.Count == 0 || hadSent && unchecked(state.Clock - previous.Clock) < 60)) continue;
                NivarianSelectionBoost.UpdateFocus.DoSync(null, map, player, pawns);
                sent[map] = new Sent { Clock = state.Clock, Ids = ids };
            }
        }
    }
}
