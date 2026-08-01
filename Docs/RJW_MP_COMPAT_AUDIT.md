# RimJobWorld 1.6 multiplayer compatibility audit

Target:

- Package: `rim.job.world`
- Mod version: 6.1.2
- Runtime assembly observed: `RJW 1.6.9576.22392`
- Multiplayer observed: `0.11.5+a481546`

## Source review

RJW already calls `MP.RegisterAll()` and contains 110 `[SyncMethod]`
attributes. Existing designation and sexuality-card UI actions generally route
through RJW's own sync methods, and RJW disables settings editing and its sex
gizmo in multiplayer. Those registrations are not duplicated by this patch.

The additional high-risk paths found during review were:

1. The deep-talk float-menu callback mutates simulation state without routing
   through one of RJW's registered action methods.
2. The non-solo and masturbation RMB callbacks call the registered `HaveSex`
   method with `SexInteractionResolved`. That value is a transient custom object
   graph and RJW registers no SyncWorker for it.
3. `Hediff_BasePregnancy.GenerateBabies(DnaGivingParent)` creates a
   parameterless `System.Random`, whose time-derived seed is not deterministic
   between peers.
4. Attraction partner selection randomizes over `HashSet<AppraisalResult>`, and
   three rape/necro selectors randomize over filtered dictionaries. Object hash
   bucket order is not a stable multiplayer ordering.
5. `AttractionUtility` initializes through `GenTypes.AllTypes`. A single
   partially unloadable third-party assembly can throw from `Assembly.GetTypes`
   and permanently poison the RJW attraction type for the process.
6. The bondage-gear float-menu action calls
   `bondage_gear_extensions.start_job(CompUsable, Pawn, LocalTargetInfo)`
   directly. Unlike RJW's ordinary designation commands, this custom job order
   had no sync registration. A first dual-client run also proved that
   `CompUsable` itself is not a safe command argument: Multiplayer could not
   serialize RJW's concrete `CompStampedApparelKey`.

## Implemented compatibility

- Registers the exact deep-talk closure and its captured `Pawn` and
  `LocalTargetInfo`, with `SyncContext.CurrentMap`.
- Replaces both sex RMB callbacks in multiplayer with a four-argument command:
  `Pawn`, `JobDef`, `LocalTargetInfo`, and `InteractionDef`.
- Replaces the enabled bondage-gear float-menu callback with a current-map
  command containing the stable parent `Thing`, `Pawn`, and `LocalTargetInfo`.
  The replay resolves `CompUsable` from the parent Thing and then calls the
  exact original `start_job` method. No transient ThingComp is serialized.
- Replays RJW's original `HaveSex` with a null resolved value, allowing RJW to
  reconstruct the transient interaction state during deterministic simulation.
- Replaces the one parameterless `System.Random` construction in
  `GenerateBabies` with `new Random(Rand.Int)`.
- Orders the three weighted `HashSet` draws and three dictionary-backed target
  draws by stable Pawn/Corpse Thing ID before consuming `Verse.Rand`.
- Installs a Mod-constructor-stage `GenTypes.AllTypes` guard before RimWorld's
  static-constructor sweep. It excludes assemblies that throw
  `ReflectionTypeLoadException`; even their apparently loadable Type objects can
  fail later during `IsAssignableFrom` or method reflection. This prevents an
  unrelated broken compatibility assembly from disabling RJW attraction.
- Resolves every RJW type, nested closure, field, and method by its verified
  RimWorld 1.6 signature; a version mismatch logs a warning instead of applying
  a partial patch.

## Build and runtime evidence

- Configuration: Release
- Compiler result: 0 warnings, 0 errors
- Output: `1.6/Assemblies/MP_MeowOnlineShop.dll`
- Mod version: 3.0.8
- SHA-256:
  `37615B85BB7226B6BCDD36C714C664E9BE889566C153873BA06D918469099C63`
- `About.xml`: parsed successfully as XML

Observed in the final minimal RJW + Multiplayer startup:

```text
[MP-MeowOnlineShop][RJW] installed early safe type-enumeration guard for RJW attraction initialization.
[MP-MeowOnlineShop][RJW] safe type enumeration cached 36344 types; excludedAssemblies=0.
[MP-MeowOnlineShop][RJW] patched deterministic target selection: 3 HashSet weighted draws and 3 Dictionary target draws now use stable Thing IDs.
[MP-MeowOnlineShop][RJW] patched GenerateBabies: System.Random() now receives a deterministic Verse.Rand seed.
[MP-MeowOnlineShop][RJW] attraction initialization verified: standardApplicators=33.
[MP-MeowOnlineShop][RJW] target resolution complete: assembly=RJW 1.6.9576.22392, deepTalk=True, haveSex=True, bondageJobOrder=True, deterministicSelection=True, pregnancy=True, deterministicRandom=True, attraction=True.
```

The larger integrated mod list was also started successfully before the final
bondage serialization correction. In that environment the guard excluded one
partially unloadable third-party assembly and attraction still initialized with
42 standard applicators.

## Final dual-client action evidence

The final run used a clean quick-test save, a direct local Multiplayer server,
one host (`RJWHost`), one non-host client (`RJWClient`), and two simultaneously
loaded maps (`uniqueID` 0 and 1).

- Deep talk: host issued 3 commands; client issued 3 commands.
- Non-solo sex: host issued 3 commands; client issued 3 commands.
- Masturbation: host issued 3 commands; client issued 3 commands.
- Bondage/holokey job: host issued 3 commands; client issued 3 commands.
- Every action family ran on both map 0 and map 1.
- Every bondage command replayed on both peers without a `CompUsable`
  serialization error.
- Pregnancy creation called RJW's production
  `Hediff_BasePregnancy.Create<Hediff_HumanlikePregnancy>` /
  `GenerateBabies` path 6 times: 3 host-issued and 3 client-issued, split over
  both maps.
- The two logs recorded the same six deterministic trait seeds, in order:
  `-128896578`, `-1782340445`, `1556569765`, `1737122330`,
  `-659984707`, `643835859`.
- Final pawn/job/relation state strings were identical on both peers.
- Host completion: `desynced=False`, tick 3519.
- Client completion: `desynced=False`, tick 3531.
- No `stage=... failed`, `Sync Error`, or hash-mismatch line occurred.
- Both isolated `MpDesyncs` directories contained zero files.

The full host and client logs remain in the isolated Multiplayer
`BuildValidation/RjwRuntimeHost` and `RjwRuntimeClient` directories. Messages
from unrelated compatibility suites in this compatibility collection are
outside the RJW patch and did not affect the RJW action matrix.

## Remaining risk

The six patched unordered-selection call sites were verified by exact runtime
target counts and exercised indirectly by the RMB interaction generation.
The final automated matrix did not wait for every autonomous rape, prisoner,
enemy, and necrophilia think-tree variant to arise naturally. Those scenarios
remain useful extended regression coverage when the broader adult mod pack is
changed, but they are no longer a blocker for this RJW 6.1.2 compatibility
artifact.
