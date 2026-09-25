using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RavenRace.Features.RavenConveyor;
using Verse;

namespace Meow.RavenIndustryOptimization
{
    internal static class ConveyorMotion
    {
        private const int MinimumPackets = 8;
        private const int StablePassesBeforeIndexing = 4;
        private sealed class Entry
        {
            internal LineState State;
            internal ConveyorPacket Packet;
            internal long Basis, Limit;
            internal int Position => PositionAt(State.Offset);
            internal int PositionAt(long offset) => (int)Math.Max(0L, Math.Min(Basis + offset, Limit));
        }

        private sealed class LineState
        {
            internal ConveyorLine Line;
            internal readonly List<Entry> Entries = new List<Entry>();
            internal int Speed, Spacing, End;
            internal long Offset, MaximumOffset;
            internal bool Valid;
            internal int StablePasses;
        }

        private static readonly ConditionalWeakTable<ConveyorPacket, Entry> packets = new ConditionalWeakTable<ConveyorPacket, Entry>();
        private static readonly ConditionalWeakTable<ConveyorPacketQueue, LineState> queues = new ConditionalWeakTable<ConveyorPacketQueue, LineState>();
        private static readonly HashSet<LineState> active = new HashSet<LineState>();
        private static Func<MapComponent_RavenConveyorSystem, bool> topologyDirty;
        private static Func<MapComponent_RavenConveyorSystem, List<int>> activeLines;
        private static Game observedGame;
        private static readonly FieldInfo PositionField = AccessTools.Field(typeof(ConveyorPacket), nameof(ConveyorPacket.PositionFixed));

        internal static void Install(Harmony h)
        {
            var system = typeof(MapComponent_RavenConveyorSystem);
            topologyDirty = IndustryBootstrap.Getter<MapComponent_RavenConveyorSystem, bool>(AccessTools.Field(system, "topologyDirty"));
            activeLines = IndustryBootstrap.Getter<MapComponent_RavenConveyorSystem, List<int>>(AccessTools.Field(system, "activeLineIteration"));
            foreach (string name in new[] { "AdvanceRegularPackets", "AdvanceEntryTransitions", "TryAdvancePacketPositionsWithBurst" })
                IndustryBootstrap.RequireUnpatched(IndustryBootstrap.Method(system, name));

            // Every original direct coordinate read/write must pass through the adapter.
            // Unknown external direct readers would observe stale fields: refuse installation.
            var raven = typeof(ConveyorPacket).Assembly;
            int patched = 0;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || assembly == typeof(ConveyorMotion).Assembly) continue;
                if (assembly != raven && !assembly.GetReferencedAssemblies().Any(a => a.Name == raven.GetName().Name)) continue;
                foreach (Type type in assembly.GetTypes())
                foreach (MethodBase method in FieldAccessAudit.Methods(type))
                {
                    if (!FieldAccessAudit.Touches(method, PositionField)) continue;
                    if (assembly != raven)
                        throw new NotSupportedException("外部模组直接访问传送带坐标：" + method.DeclaringType.FullName);
                    h.Patch(method, transpiler: new HarmonyMethod(typeof(ConveyorMotion), nameof(RewriteCoordinates)) { priority = Priority.Last });
                    patched++;
                }
            }
            if (patched < 10) throw new InvalidOperationException("Conveyor coordinate audit incomplete");

            IndustryBootstrap.Patch(h, system, "AdvanceRegularPackets", typeof(ConveyorMotion), nameof(BeforeAdvance), nameof(AfterAdvance));
            IndustryBootstrap.Patch(h, system, "AdvanceEntryTransitions", typeof(ConveyorMotion), nameof(BeforeEntryTransitions));
            IndustryBootstrap.Patch(h, system, "TryAdvancePacketPositionsWithBurst", typeof(ConveyorMotion), nameof(BeforeBurst));
            IndustryBootstrap.Patch(h, system, "EnsureTopology", typeof(ConveyorMotion), nameof(BeforeTopology));
            IndustryBootstrap.Patch(h, system, "SnapshotPacketAnchors", typeof(ConveyorMotion), nameof(BeforeSnapshot));
            IndustryBootstrap.Patch(h, system, "MapComponentTick", typeof(ConveyorMotion), nameof(BeforeMapTick));
            IndustryBootstrap.Patch(h, system, "MapRemoved", typeof(ConveyorMotion), nameof(BeforeSnapshot));
            foreach (string name in new[] { "Add", "Insert", "RemoveAt", "RotateLeft", "Sort", "Clear", "set_Item" })
                IndustryBootstrap.Patch(h, typeof(ConveyorPacketQueue), name, typeof(ConveyorMotion), nameof(BeforeQueueMutation));
            h.Patch(AccessTools.Method(typeof(Map), nameof(Map.ExposeData)),
                prefix: new HarmonyMethod(typeof(ConveyorMotion), nameof(BeforeSnapshot)) { priority = Priority.First });
            h.Patch(IndustryBootstrap.Method(typeof(Game), "ExposeSmallComponents"),
                prefix: new HarmonyMethod(typeof(ConveyorMotion), nameof(BeforeSnapshot)) { priority = Priority.First });
            Log.Message("[Meow.RavenIndustry] coordinate accessors=" + patched + "; queue/save/topology barriers installed");
        }

        private static IEnumerable<CodeInstruction> RewriteCoordinates(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.operand is FieldInfo field && field.Module == PositionField.Module && field.MetadataToken == PositionField.MetadataToken)
                {
                    if (instruction.opcode == OpCodes.Ldfld)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(ConveyorMotion), nameof(ReadPosition));
                    }
                    else if (instruction.opcode == OpCodes.Stfld)
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = AccessTools.Method(typeof(ConveyorMotion), nameof(WritePosition));
                    }
                    else throw new NotSupportedException("Unsupported conveyor coordinate by-ref access");
                }
                yield return instruction;
            }
        }

        internal static int ReadPosition(ConveyorPacket packet)
        {
            if (active.Count != 0 && packets.TryGetValue(packet, out var entry))
            {
                // Capture once: a presentation reader must not dereference State twice
                // across a simulation-side materialization.
                var state = entry.State;
                if (state != null) return entry.PositionAt(state.Offset);
            }
            return packet.PositionFixed;
        }

        internal static void WritePosition(ConveyorPacket packet, int value)
        {
            if (active.Count != 0 && packets.TryGetValue(packet, out var entry) && entry.State != null) Materialize(entry.State);
            packet.PositionFixed = value;
        }

        private static bool BeforeAdvance(ConveyorLine line, int speed, int spacing, out bool __state)
        {
            __state = false;
            if (!IndustrySettings.Active) { FlushAll(); return true; }
            if (queues.TryGetValue(line.Packets, out var state) && state.Valid)
            {
                if (state.Speed == speed && state.Spacing == spacing && state.End == line.EndPositionFixed &&
                    state.Entries.Count == line.Packets.Count && !line.IsClosedLoop)
                {
                    // Saturation is blocked-motion sleep: no packet work and no growing clock.
                    if (state.Offset < state.MaximumOffset)
                        state.Offset = Math.Min(state.MaximumOffset, state.Offset + speed);
                    return false;
                }
                Materialize(state);
            }
            __state = !line.IsClosedLoop && line.Packets.Count >= MinimumPackets && speed > 0 && spacing > 0 &&
                line.EndPositionFixed >= 0 && (long)line.EndPositionFixed + speed <= int.MaxValue;
            return true;
        }

        private static void AfterAdvance(ConveyorLine line, int speed, int spacing, bool __state)
        {
            if (!__state) return;
            var state = queues.GetValue(line.Packets, _ => new LineState());
            if (state.StablePasses < StablePassesBeforeIndexing) return;
            // The original sweep has just normalized spacing. Seed only wholly regular
            // packets; entry and source transitions retain the original implementation.
            for (int i = 0; i < line.Packets.Count; i++)
            {
                var packet = line.Packets[i];
                if (packet.EntrySourceCell.IsValid || packet.ExitProgressFixed != 0 || packet.PositionFixed < 0 ||
                    packet.PositionFixed > line.EndPositionFixed) { state.StablePasses = 0; return; }
            }
            long prefix = long.MaxValue;
            // Verify the algebra before publishing ANY lazy coordinate entries.
            for (int i = 0; i < line.Packets.Count; i++)
            {
                var packet = line.Packets[i];
                long rankSpacing = (long)i * spacing;
                prefix = Math.Min(prefix, packet.PositionFixed + rankSpacing);
                long basis = prefix - rankSpacing, limit = line.EndPositionFixed - rankSpacing;
                if (Math.Max(0L, Math.Min(basis, limit)) != packet.PositionFixed) { state.StablePasses = 0; return; }
            }
            state.Line = line; state.Speed = speed; state.Spacing = spacing; state.End = line.EndPositionFixed;
            state.Offset = 0; state.MaximumOffset = 0;
            state.Entries.Clear();
            prefix = long.MaxValue;
            for (int i = 0; i < line.Packets.Count; i++)
            {
                var packet = line.Packets[i];
                long rankSpacing = (long)i * spacing;
                prefix = Math.Min(prefix, packet.PositionFixed + rankSpacing);
                // Reuse the entry along with Raven's existing packet pool. Recreating
                // one wrapper per packet on every input/output would create GC spikes.
                var entry = packets.GetValue(packet, key => new Entry { Packet = key });
                entry.State = state; entry.Basis = prefix - rankSpacing; entry.Limit = state.End - rankSpacing;
                // For collapsed packets at position zero, the negative basis encodes the
                // exact release delay. Simply moving all zero-position packets is wrong.
                state.Entries.Add(entry);
                if (entry.Limit > 0) state.MaximumOffset = Math.Max(state.MaximumOffset, entry.Limit - entry.Basis);
            }
            state.Valid = true;
            active.Add(state);
        }

        private static bool BeforeEntryTransitions(ConveyorLine line) => !queues.TryGetValue(line.Packets, out var state) || !state.Valid;

        private static bool BeforeBurst(MapComponent_RavenConveyorSystem __instance, ref bool __result)
        {
            if (!IndustrySettings.Active) { FlushAll(); return true; }
            var topology = __instance.TopologyForReading;
            var indices = activeLines(__instance);
            bool hasStableLine = false;
            for (int i = 0; i < indices.Count; i++)
            {
                var line = topology.Lines[indices[i]];
                if (!line.IsClosedLoop && line.Packets.Count >= MinimumPackets)
                {
                    var state = queues.GetValue(line.Packets, _ => new LineState());
                    state.StablePasses = Math.Min(StablePassesBeforeIndexing, state.StablePasses + 1);
                    hasStableLine |= state.Valid || state.StablePasses >= StablePassesBeforeIndexing;
                }
            }
            if (hasStableLine)
            {
                // Avoid packing/copying every packet only to perform constant-time
                // line movement. Churning queues/closed loops retain the native path.
                __result = false;
                return false;
            }
            FlushNetwork(__instance);
            return true;
        }

        private static void BeforeQueueMutation(ConveyorPacketQueue __instance)
        {
            if (queues.TryGetValue(__instance, out var state))
            {
                state.StablePasses = 0;
                if (state.Valid) Materialize(state);
            }
        }

        private static void BeforeTopology(MapComponent_RavenConveyorSystem __instance)
        {
            if (topologyDirty(__instance)) FlushNetwork(__instance);
        }

        private static void BeforeMapTick()
        {
            if (!ReferenceEquals(observedGame, Current.Game))
            {
                FlushAll();
                CapacityIndex.Clear();
                observedGame = Current.Game;
            }
            if (!IndustrySettings.Active) FlushAll();
        }

        private static void BeforeSnapshot() => FlushAll();

        private static void Materialize(LineState state)
        {
            foreach (var entry in state.Entries)
            {
                entry.Packet.PositionFixed = entry.Position;
                entry.State = null;
            }
            state.Valid = false;
            state.StablePasses = 0;
            state.Entries.Clear();
            active.Remove(state);
        }

        internal static void FlushAll()
        {
            if (active.Count == 0) return;
            foreach (var state in active.ToArray()) Materialize(state);
        }

        private static void FlushNetwork(MapComponent_RavenConveyorSystem network)
        {
            if (active.Count == 0) return;
            foreach (var line in network.TopologyForReading.Lines)
                if (queues.TryGetValue(line.Packets, out var state) && state.Valid) Materialize(state);
        }
    }
}
