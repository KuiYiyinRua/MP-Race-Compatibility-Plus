# Desync-08 analysis (2026-08-07)

## Evidence

- Original bundle: `H:\下载\元之雨RJW全种族拓展\rim\rim\MpDesyncs\Desync-08.zip`
- Frozen copy: `BuildValidation/DesyncEvidence/2026-08-07-Desync-08/Desync-08.zip`
- SHA-256: `DF110F5EC1991800A1B87127635E687CD482511FC2A5539183CD9DCB9783B3E1`

Multiplayer `0.11.5+a481546`, RimWorld `1.6.4850 rev646`, async time and
multifaction active, 3 maps, 2 players.

## Desync record

Last valid tick: 4542571

Desync tick: 4542632, message `Trace hashes don't match`

The first divergent trace is at tick 4542603:
`Pawn_ApparelTracker.TakeWearoutDamageForDay -> GenMath.RoundRandom -> Rand.get_Value`
for `Axolotl903835`. The map Rand state shown in the trace is identical on both
peers (`2621696633151568`), and every context trace from tick 4542607 onward is
also identical. The only stack difference is:

- local: `Verse.Root_Play.Update_Patch2` (`[0x000a1]`)
- host: `Verse.Root_Play.Update_Patch3` (`[0x000ab]`)

So the desync is a trace-hash false positive caused by a different Harmony
wrapper on `Root_Play.Update`, not by a diverged simulation state. Multiplayer
still treats it as a desync, which forces the client to rejoin, which is the
player disconnect the user observed.

## Root cause

Dubs Performance Analyzer is active in this loadout
(`Dubwise.DubsPerformanceAnalyzer.steam`, `Analyzer` 1.6.0).
`Analyzer.Window_Analyzer.PreOpen` installs `H_RootUpdate` on
`Root_Play.Update` and `H_DoSingleTickUpdate` on `TickManager.DoSingleTick`
the first time the analyzer window is opened. Selecting profiler entries later
installs additional per-entry hooks.

Those hooks are peer-local UI/profiling state. Opening the analyzer on only one
peer (the host, in this bundle) changes that peer's MonoMod wrapper from
`Root_Play.Update_Patch2` to `Root_Play.Update_Patch3`. Multiplayer hashes the
normalized stack, so every Rand draw that includes the root update frame gets a
different trace hash on the two peers even though the Rand states are equal.

Local metadata confirms the asymmetry: the local peer's patch scan shows
`Root_Play.Update: post: PerspectiveShift.Root_Play_Update_Patch.Postfix,
Multiplayer.Client.BootstrapRootPlayUpdatePatch.Postfix` (two postfixes), while
the host trace compiled a third wrapper.

## Proposed fix (not applied)

A source-only `Patch_DubsPerformanceAnalyzerMp.cs` was drafted for this
analysis. At multiplayer startup it would remove every patch owned by DPA's
profiler Harmony id `Dubwise.DubsProfiler`, and it would guard
`Analyzer.Window_Analyzer.PreOpen` plus `Analyzer.Profiling.Entry.PatchMethods`
so DPA's runtime hooks cannot be reinstalled on only one peer.

Per the user's instruction, that patch was removed again and no code files were
changed for this Desync-08 analysis. The diagnosis above remains the
explanation for the desync and the resulting player disconnect.

## Not done and remaining risk

Per the user's instruction, nothing was deployed and no RimWorld host/client
run was started. Runtime behavior is unverified.

Remaining risk:
- DPA runtime profiling is intentionally disabled during multiplayer; a future
  solution that wants profiler data from both peers must synchronize the
  profiler window/entry state through MP commands.
- Other mods that install Harmony patches at runtime on only one peer can cause
  the same trace-hash false desync. The next host/client smoke should watch for
  `Trace hashes don't match` with equal map Rand states.
- The next release must also include the existing TickList/Visual Brutality and
  290-296 source fixes, since this bundle still ran the old
  `3.0.117-tps-fix-nullguard` binary.
