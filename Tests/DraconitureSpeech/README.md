# Draconiture engineering speech regression

The driver is enabled only by `-draconiturespeechprobe` and lives in an isolated test mod. It extends the Perspective ownership fixture so the previous rehosting/claim fix remains covered.

Use an absolute, short run directory, such as `H:\MPValidation\D410\R05`. Mono can fail to load the target mod's long resource filenames from deeply nested validation directories.

`Run.ps1` requires `-Core` (the unchanged main compatibility DLL) and `-Tale` (the candidate `Meow.TaleNivarianCompatibility.dll`). Use `-Old` only with the frozen 1.1.3 module to reproduce the regression. Other options are `-TwoMaps`, `-AsyncTime`, `-Traces`, `-Cycles 3`, `-SoakTicks 120000`, and an isolated localhost `-Port`.

The native `EngineeringDroneThing.TickThrowWords` runs on a real spawned `Nivarian_Engineer` from a remote synchronized action. Each repetition checks:

- Host rate-limit queue full / client empty.
- Host speech enabled / client speech disabled.
- Host queue empty / client full.
- An injected exception immediately after the native interval draw, proving finalizer restoration.

Every branch must leave the active shared Rand state exactly unchanged. The observer also checks naturally occurring speech ticks. Native engineering hubs repair damaged buildings on every map; repair execution must be observed by both peers after every cold join. No repair, construction, target-search, movement, or whole-map-tick method is wrapped in a random scope by the release patch.

Traced smoke: one map, then two maps with normal/async time and three fresh client processes. Each join has a frozen world/map/Rand equality barrier, native claim/release and speech assertions, camera switching, and at least 10,000 shared world ticks. Long soaks use traces off and at least 120,000 shared world ticks. The controller rejects Rand leaks, desyncs, command errors, unresolved references, missing patch targets, input hash changes, and unequal action receipts.

`Tests/DraconitureReplay` separately loads the frozen user replay with the original mod order. Its speech observer is read-only. Save-reference errors remain fatal and are not hidden to obtain a passing result.
