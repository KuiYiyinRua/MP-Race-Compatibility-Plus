# Desync-287/288/289 analysis (2026-08-07)

## Evidence

- Frozen bundles: `BuildValidation/DesyncEvidence/2026-08-07-Desync-287-288-289/`
- Source/copy SHA-256:
  - Desync-287.zip `287C8AAB61EF7F8D6C335794806B55BD5169494D514C9A1DD857E031BE762DE3`
  - Desync-288.zip `33BD5298122C909FE50B7697534ACB9F267992BE130A90E181479F120C90212C`
  - Desync-289.zip `5275B2871F14DC2AAF58B6BBB78E0F5445543445E28129E07C86D3E469069BF4`
- Multiplayer `0.11.5+a481546`, RimWorld `1.6.4850 rev646`
- Session: 2 players, async time on, multifaction on, 3 maps, host debug build
- Local logs are cumulative from one long client session. The final bundle records:
  - 287: last valid tick 4393591, desync 4393652, map 26
  - 288: last valid tick 4408021, desync 4408080, map 1
  - 289: last valid tick 4416661, desync 4416722, map 1

## Earliest divergent traces

| Bundle | First diff | Observation |
| --- | --- | --- |
| 284 (earlier in same log) | same stack/Rand, map ticks differ by 1 | Axolotl mote draw |
| 285 | same tick/stack, bullet ThingIDs differ by 1 | `bullet_Axolotl_...1060350` vs `...1060349` |
| 286 | same tick, different stacks | bullet despawn vs `HearClamor` Rand |
| 287 | same shared tick, map ticks differ by 1 | host JobID allocation vs local pathfinding |
| 288 | same shared tick, different stacks | host Corpse rare bucket vs local weather |
| 289 | same shared tick, same spawn stack | host `Explosion1111200` vs local `Explosion1111204` |

The shared `TickPatch.Timer` is identical at every first diff; the per-map
`ticksGame` or ThingID counter has already drifted by 1-4 on one peer.

## Root causes

1. Per-map async TickList membership can stay stale for up to a full bucket
   cycle. The compatibility log prints `Restored missing spawned TickList
   members` / `Removed stale...` immediately before several desyncs; the old
   current-bucket-only reconcile fixes the bucket only when it is about to
   execute, which is too late for that execution.
2. ThingID counters drift by 1-4, meaning some object-producing code runs on
   only one peer. Visual Brutality (`Thumb.GoreMod2`) is an active,
   previously unpatched source: `Hediff_MissingPart.PostAdd` runs RWBeheading
   and VB flows that spawn `FlyingFlesh` projectiles, `HeadProjectile`s, meat,
   and `HeadItem`s, each consuming a positive ThingID and map Rand. Jitted
   evidence reaches `MakeFlyingFlesh`/`SpawnFragment` one tick before desync
   285 and VB head graphics immediately before desync 289.

## Code changes (source only, not deployed)

- New `Patch_VisualBrutalityMp.cs`
  - In multiplayer, skips `MakeFlyingFlesh`, `TryDestroyHead`, and
    `LaunchHead`, so Visual Brutality cannot create positive-ID simulation
    objects or consume the shared map Rand stream.
  - Scopes `FlyingFlesh.SpawnSetup` Rand with a deterministic seed so a
    projectile loaded from an older snapshot cannot advance shared Rand.
  - Registered in `MP_MeowOnlineShop.csproj` and bootstrap as an optional
    patch (fails open when the mod or a signature is missing).
- `Patch_DeterministicTickList.cs`
  - Replaced current-bucket-only reconcile with `ReconcileAllBuckets`.
  - Full membership is rebuilt from `listerThings.AllThings` on dirty events
    and on a 60-tick deterministic cadence, so both peers converge before the
    next bucket cycle instead of only repairing the bucket that is executing.

## Verification

- `dotnet build -c Release -p:OutputPath=obj\ReleaseDesync287-289\`
  - 0 warnings, 0 errors
- Built DLL:
  `Source/MP_MeowOnlineShop/obj/ReleaseDesync287-289/MP_MeowOnlineShop.dll`
  - SHA-256 `26547653BAED49473D8BBE3444505E6DE1984C660225019C92C7D4E7E8007678`
- Static inspection confirms `Patch_VisualBrutalityMp` and
  `ReconcileAllBuckets` are present in the built assembly.
- Not deployed to `1.6/Assemblies`; no RimWorld process was started
  (user requested code-only work).

## Remaining risk

- The mapTicks off-by-one seen in 284/287 can still have additional sources
  (rejoin phase or a local-only session pause); the TickList change reduces
  downstream divergence but does not re-phase mapTicks.
- Runtime behavior is unverified. The next gate is a host/client smoke with
  async time and the same 3-map session, followed by a normal-settings soak.
