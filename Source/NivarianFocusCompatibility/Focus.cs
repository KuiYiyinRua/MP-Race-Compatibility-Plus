using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Meow.NivarianFocusCompatibility
{
    public sealed class FocusRecord : IExposable
    {
        public int PlayerId, TicksLeft;
        public Faction Faction;
        public List<Pawn> Pawns = new List<Pawn>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref PlayerId, "playerId");
            Scribe_Values.Look(ref TicksLeft, "ticksLeft");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Collections.Look(ref Pawns, "pawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Pawns = Pawns ?? new List<Pawn>();
        }
    }

    public sealed class FocusState : MapComponent
    {
        public int Clock;
        public List<FocusRecord> Records = new List<FocusRecord>();
        public FocusState(Map map) : base(map) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref Clock, "meowFocusClock");
            Scribe_Collections.Look(ref Records, "meowFocusRecords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Records = Records ?? new List<FocusRecord>();
        }

        public List<Pawn> ForTick()
        {
            Clock = unchecked(Clock + 1);
            Records.RemoveAll(r => r == null || r.TicksLeft <= 0 || r.Faction != map.ParentFaction);
            var result = new List<Pawn>();
            foreach (var record in Records)
            {
                record.TicksLeft--;
                foreach (var pawn in record.Pawns)
                    if (pawn != null && !pawn.Dead && pawn.Spawned && pawn.Map == map && !result.Contains(pawn))
                        result.Add(pawn);
            }
            result.Sort((a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
            return result;
        }
    }

    [StaticConstructorOnStartup]
    public static class Focus
    {
        public static ISyncMethod UpdateFocus;
        public static Func<Pawn, bool> IsNivarian;
        static PropertyInfo replay, simulating;

        static Focus()
        {
            if (!MP_MeowOnlineShop.CompatibilityPatchCategories.IsEnabled("nivarian")) return;
            if (!MP.enabled || !ModsConfig.IsActive("keeptpa.NivarianRace")) return;
            var harmony = new Harmony("meow.nivarian-focus.multiplayer");
            try
            {
                var tick = AccessTools.DeclaredMethod(AccessTools.TypeByName("Nivarian.MapComp_NivarianSelectionBoost"), "MapComponentTick")
                    ?? throw new MissingMethodException("Nivarian selection tick missing");
                // The installed RaceTrio 1.1.0 lacks this fix. Refuse to stack it on a future integrated version.
                if (Harmony.GetPatchInfo(tick)?.Owners.Contains("meow.trio.nivarian-selection-boost") == true)
                {
                    Log.Message("[Meow.NivarianFocus] integrated RaceTrio focus patch already active; standalone patch skipped.");
                    return;
                }
                var predicate = AccessTools.DeclaredMethod(AccessTools.TypeByName("Nivarian.Helper.NivarianHelper"), "IsNivarian", new[] { typeof(Pawn) })
                    ?? throw new MissingMethodException("Nivarian race predicate missing");
                IsNivarian = (Func<Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Pawn, bool>), predicate);
                replay = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.Multiplayer"), "IsReplay");
                simulating = AccessTools.Property(AccessTools.TypeByName("Multiplayer.Client.TickPatch"), "Simulating");
                if (replay == null || simulating == null) throw new MissingMethodException("MP replay/simulation guards missing");
                harmony.Patch(tick, transpiler: new HarmonyMethod(typeof(Focus), nameof(ReplaceSelection)));
                UpdateFocus = MP.RegisterSyncMethod(typeof(Focus), nameof(SetFocus), new[]
                {
                    new SyncType(typeof(Map)) { contextMap = true }, new SyncType(typeof(int)), new SyncType(typeof(List<Pawn>))
                });
                Log.Message("[Meow.NivarianFocus] 1.0.0 MVID=" + typeof(Focus).Module.ModuleVersionId
                    + "; shared selection installed; map command queue, saved focus, 180 map-tick expiry.");
            }
            catch (Exception e)
            {
                UpdateFocus = null;
                harmony.UnpatchAll(harmony.Id);
                Log.Error("[Meow.NivarianFocus] required target failed; patches rolled back: " + e);
            }
        }

        public static bool CanSend => UpdateFocus != null && MP.IsInMultiplayer && MP.InInterface
            && !(bool)replay.GetValue(null) && !(bool)simulating.GetValue(null);

        public static void SetFocus(Map map, int playerId, List<Pawn> pawns)
        {
            // OfPlayer here is the synchronized command's issuing faction, not a local tick/UI lookup.
            if (map == null || !Find.Maps.Contains(map) || map.ParentFaction != Faction.OfPlayer) return;
            var state = map.GetComponent<FocusState>();
            state.Records.RemoveAll(r => r.PlayerId == playerId);
            var selected = (pawns ?? new List<Pawn>()).Where(p => p != null && p.Spawned && !p.Dead && p.Map == map && IsNivarian(p))
                .Distinct().OrderBy(p => p.thingIDNumber).ToList();
            if (selected.Count != 0) state.Records.Add(new FocusRecord
                { PlayerId = playerId, Faction = Faction.OfPlayer, TicksLeft = 180, Pawns = selected });
        }

        public static List<Pawn> Selected(Selector selector, MapComponent component)
            => MP.IsInMultiplayer ? component.map.GetComponent<FocusState>().ForTick() : selector.SelectedPawns;

        public static IEnumerable<CodeInstruction> ReplaceSelection(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.PropertyGetter(typeof(Selector), nameof(Selector.SelectedPawns));
            int count = 0;
            foreach (var source in instructions)
            {
                var code = new CodeInstruction(source);
                if (code.Calls(original))
                {
                    var component = new CodeInstruction(OpCodes.Ldarg_0);
                    component.labels.AddRange(code.labels); component.blocks.AddRange(code.blocks);
                    code.labels.Clear(); code.blocks.Clear();
                    yield return component;
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(Focus), nameof(Selected));
                    count++;
                }
                yield return code;
            }
            if (count != 1) throw new InvalidOperationException("Expected one Nivarian local selection read; found " + count);
        }
    }

    public sealed class FocusInput : GameComponent
    {
        sealed class Sent { public int Clock; public List<int> Ids; }
        readonly Dictionary<Map, Sent> sent = new Dictionary<Map, Sent>();
        int previousPlayer = -1;
        public FocusInput(Game game) { }
        public override void GameComponentUpdate()
        {
            if (!Focus.CanSend) return;
            var players = MP.GetPlayers().Where(p => p.Username == MP.PlayerName).ToList();
            if (players.Count != 1) return;
            int player = players[0].Id;
            if (previousPlayer != player) { sent.Clear(); previousPlayer = player; }
            var selected = Find.Selector.SelectedPawns.Where(p => p != null && p.Spawned && !p.Dead && Focus.IsNivarian(p))
                .Distinct().OrderBy(p => p.thingIDNumber).ToList();
            foreach (var old in sent.Keys.Where(m => !Find.Maps.Contains(m)).ToList()) sent.Remove(old);
            foreach (var map in Find.Maps)
            {
                if (map.ParentFaction != MP.RealPlayerFaction) continue;
                var state = map.GetComponent<FocusState>();
                var pawns = selected.Where(p => p.Map == map && map == Find.CurrentMap).ToList();
                var ids = pawns.Select(p => p.thingIDNumber).ToList();
                bool hadSent = sent.TryGetValue(map, out var previous);
                bool changed = hadSent ? !ids.SequenceEqual(previous.Ids) : ids.Count > 0 || state.Records.Any(r => r.PlayerId == player);
                if (!changed && (ids.Count == 0 || hadSent && unchecked(state.Clock - previous.Clock) < 60)) continue;
                // A LongEvent can reject DoSync. Do not mark a rejected update as sent.
                if (Focus.UpdateFocus.DoSync(null, map, player, pawns))
                    sent[map] = new Sent { Clock = state.Clock, Ids = ids };
            }
        }
    }
}
