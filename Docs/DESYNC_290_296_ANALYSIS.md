# Desync-290/291/293/294/295/296 analysis (2026-08-07)

## Evidence

Frozen bundles:
`BuildValidation/DesyncEvidence/2026-08-07-Desync-290-296/`

`Desync-292.zip` was not present in the workspace; the analysis covers the six
bundles that were generated (290, 291, 293, 294, 295, 296).

Source/copy SHA-256:
- Desync-290.zip `11B987016C92858B9F79618920C01AA2698F411F6A2A42E96DAA187C4466E1BB`
- Desync-291.zip `A6EC05A5B6ACDB5EB7657708F8C33E344753BFDADC5EA85E48261512AB5E8270`
- Desync-293.zip `E2EA781FA2155A563D7B770FE9D94A398838B67E27651DE96693BDEF7F2C6D7C`
- Desync-294.zip `4C61E9B5BD5A28CC86BB6A320634794B38CC90115F3B50C53626DA1BDD503B17`
- Desync-295.zip `7F8B0BBF9A1579425D0CE028A5A5DEC4C82160A213A68BAC1A047A27B1AC655A`
- Desync-296.zip `6F461FF325081CB9500429993B38B4A972BB2A3423E4D3B2F162584CA722FAB8`

Multiplayer `0.11.5+a481546`, RimWorld `1.6.4850 rev646`, async time and
multifaction active, 3 maps, 2 players.

## Loaded build

Every bundle's client log reports the same deployed build identity:
`3.0.117-tps-fix-nullguard`. The 287/289 source changes (Visual Brutality guard
and `ReconcileAllBuckets`) were present in the working tree but were not loaded
by these sessions, so the new bundles were produced by the older binary.

## Desync records

| Bundle | Last valid tick | Desync tick | Map | First divergence |
| --- | ---: | ---: | ---: | --- |
| 290 | 4439101 | 4439165 | 1 | local `Fire1113197.SpawnSmokeParticles` vs host `GenList.Shuffle`; Visual Brutality head graphics and FactionRepeater NRE immediately before |
| 291 | 4478971 | 4479030 | 1 | local `Fire1117146.DoComplexCalcs` vs host `Fire1117043.SpawnSmokeParticles`; stale `Fire1116257` removed and `Corpse_Squirrel1117710` restored just before |
| 293 | 4501891 | 4501950 | 1 | trace counts differ but existing traces equal (one tick ended sooner); stale `Bullet_SwordAura_Rock1116698` removed, `Corpse_Caribou1118158` restored, then `EliteRaid.GenerateAnything` |
| 294 | 4528021 | 4528080 | 27 | host `Bullet_VastSeaCelestialWhaleCannon-359` despawn vs local pawn snow-grid Rand; rejoin snapshot later cannot resolve `Thing_Axolotl_VastSeaCelestialWhaleCannon955067` from whale-cannon bullets/explosion |
| 295 | 4532414 | 4532491 | 27 | no call-level traces; `Random state from commands doesn't match`, rejoin snapshot still carrying unresolved whale-cannon references |
| 296 | -1 | 4534710 | 27 | local `MiliraBullet_PlasmaRifleCharged1125934` despawn at tick 4534681 vs host `Embergarden.HediffComp_Regen.CompPostTick -> InRandomOrder` at 4534682; last valid -1 means the rejoin baseline diverged before one validated tick |

## Root causes

1. The deployed 3.0.117 binary still has the current-bucket-only TickList
   reconcile. `Removed stale` / `Restored missing` messages appear immediately
   before several desyncs, and the "traces differ in amount" record in 293 is
   the tick-list length signature. The source tree's `ReconcileAllBuckets`
   change already targets this and is not yet deployed.
2. Visual Brutality head graphics run immediately before desyncs 290 and 291.
   `Patch_VisualBrutalityMp` already exists in the source tree but was not
   loaded.
3. `Patch_AxolotlCrossbowVerbDeterminism.ResolveTargets` throws
   `AmbiguousMatchException` at startup in every bundle (one of the Axolotl
   properties hides a base property), so the Desync-285 bullet single-shot
   guard never installs. The exception is logged at line 1765 of the 291 log
   with stack `...Patch_AxolotlCrossbowVerbDeterminism.ResolveTargets`.
4. Destroyed projectile launchers survive in host memory but cannot be
   resolved from a rejoin snapshot. `Bullet_VastSeaCelestialWhaleCannon` is a
   vanilla `Projectile_Explosive`; `MiliraBullet_PlasmaRifleCharged` is
   `AncotLibrary.Projectile_Custom`, and its impact effecter dereferences
   `launcher.Map`, so a host-side stale launcher versus a client-side null
   launcher produces a one-sided NRE/effecter path.
5. `Embergarden.HediffComp_Regen.CompPostTick` calls
   `GenCollection.InRandomOrder` on every check interval, consuming the
   synchronized per-map Rand stream. Desync-296's first host trace reaches
   `InRandomOrder.MoveNext` at the diverged tick.

## Code changes (source only, not deployed)

- `Patch_AxolotlCrossbowVerbDeterminism.cs`
  - `ResolveTargets` now resolves properties through declared-only,
    base-walking lookup and fails closed instead of aborting bootstrap with
    `AmbiguousMatchException`.
- `Patch_EmbergardenRegenMp.cs` (new)
  - Wraps `Embergarden.HediffComp_Regen.CompPostTick` in
    `DeterministicRandScope` seeded from pawn/map/def/shared tick, so
    `InRandomOrder` cannot advance synchronized map/world Rand.
- `Patch_ProjectileLauncherDeterminism.cs` (new)
  - Clears destroyed `launcher`/`equipment` references on `Projectile.ExposeData`
    while saving in MP, so future snapshots and live hosts agree on null.
  - Replaces the impact effecter path of `AncotLibrary.Projectile_Custom` and
    `AncotLibrary.Projectile_ExplosiveCustom` with a null-safe effecter that
    uses the projectile map and skips destroyed launchers.
- `MP_MeowOnlineShop.csproj` and bootstrap register both new patches as
  optional, fail-open third-party compatibility.

No changes were made to the deployed `1.6/Assemblies` folder.

## Static verification

`dotnet build MP_MeowOnlineShop.csproj -c Release -p:OutputPath=obj\ReleaseDesync290-296\`
completed with 0 warnings and 0 errors.

Built DLL:
`Source/MP_MeowOnlineShop/obj/ReleaseDesync290-296/MP_MeowOnlineShop.dll`
SHA-256 `E0E198CBCA7B4682314D971E92D3D4CF64BE613D0207AD68B6FB6057A31B00CC`

Metadata inspection confirms `Patch_EmbergardenRegenMp`,
`Patch_ProjectileLauncherDeterminism`, and
`Patch_AxolotlCrossbowVerbDeterminism.FindPropertySafe` are compiled into the
assembly.

## Not done and remaining risk

Per the user's instruction, this work is code-only: nothing was deployed and no
RimWorld host/client run was started. Runtime behavior is unverified.

Remaining risk:
- Rejoin baselines in 295/296 still need a host/client smoke with async time
  and the same 3-map session to prove the snapshot now loads without launcher
  reference divergence.
- The old TickList and Visual Brutality fixes must be included in the next
  deployed binary before judging the remaining desyncs.
- The FactionRepeater NRE before 290 is caught by Multiplayer and is not
  patched here; it should be monitored during the next soak.
- `AncotLibrary.Bullet_Pierce` and other custom projectiles that dereference
  `launcher` were not replayed; only the two Ancot classes present in the
  logged Milira/whale-cannon paths are patched.
