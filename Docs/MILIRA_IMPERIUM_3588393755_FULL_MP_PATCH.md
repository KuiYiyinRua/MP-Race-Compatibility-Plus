# Milira Imperium 3588393755 - Full Multiplayer Patch Audit

## Source authority

- Workshop: `3588393755`
- Package ID: `Ariandel.MiliraImperium`
- Name: Milira Faction: Milira Imperium
- Author: Ariandel
- Installed assemblies:
  - `1.6/Assemblies/MiliraImperium.dll`
    SHA-256 `68054C00B73367F1504688BBCEA78EE8FBE3C610C033C6C58AA2C22A2843BACE`
  - `1.6/Assemblies/Notice.dll`
    SHA-256 `73DE9FE8A7E2F8C1CB71060195277159C70CE365E0B2A33D665505EF63D5195A`
  - `1.6/Assemblies/PLAMiliraSariel.dll`
    SHA-256 `1D4070EF81C68341C17E865829FD2ACE88D4A1E1E7FAE3E7814102B2A20CB140`
- No shipped source or project files exist in the workshop folder, so the
  installed binaries were decompiled with ILSpy 10.1 into the git-ignored
  `P0Decompiled/MiliraImperium_3588393755*` validation workspace.
- The existing port originated from the verified reference patch
  `G:\Steam\steamapps\workshop\content\294100\3725772639\1.6\Assemblies\Milira_Comp.dll`
  (`usamiseika.fixmod.miliramultiplayer`). Its decompiled source is retained in
  `C:\tmp\MiliraMP_Decompiled_20260805`.

## Mutation inventory

The decompile was searched for every command, toggle, float menu, dialog
option, permit worker, ability effect, ticker, and direct `Rand` call in the
target assembly. Existing `MP_MeowOnlineShop` coverage already handled:

- Royal aid dialog and GameComponent gift/trader/upgrade executors.
- `RoyalTitlePermitWorker` targeting through a generic `OrderForceTarget`
  boundary with reconstructed caller/map/faction context.
- Modded ability `Apply(LocalTargetInfo, LocalTargetInfo)` executors.
- `CompSecondaryVerb_Rework`, `CompMaustsAuraEmitter*`, `MI_Psycounter`,
  `Comp_DeflectorShield`, `HediffComp_MovingLockToggleTrait` gizmo fields.
- A hardcoded list of projectile/orbital/aura Rand scopes.
- Base Milira Race weapon/shield/flight/fly patches in `Patch_Milira*`.

## Gaps closed in this pass

### Gizmo state divergence

- `CompMaustsAuraEmitter` and `CompMaustsAuraEmitter_Apparel` toggles now sync
  `lastToggleTick` together with `isActive` through the new registered
  `SyncAuraToggle(ThingComp, string, bool, int)`. Previously only `isActive`
  was replayed, so the aura cooldown field stayed stale on non-issuing peers.
- `HediffComp_MovingLockToggleTrait` toggles now dispatch
  `SyncMovingLockToggle(int pawnId, int degree, bool enabled)`, which replays
  both the `enabled` field and the private `ApplyTraitDegree` mutation.
  Previously the trait degree was applied only on the clicking peer.
- `MI_Psycounter` mode changes now use `SyncPsyMode(ThingComp, bool)`, which
  also dirties the remote map mesh so the visual mode switch matches.
- `Comp_DeflectorShield` keeps its existing render toggle sync; its two dev
  `Command_Action` gizmos are debug-only and remain local.

### GameComponent state

`MiliraImperium_GameComponent` now also registers these fields/methods with
Multiplayer:

- `FeiJiangRecruited`, `DevLydiaRecruited`
- `nextAllowedTick`, `nextAllowedTickI`
- `giftEventPending`, `giftEventExecuteTick`, `warpInCoolDown`
- `StartCooldown`, `ClearCooldown`

### Rand isolation

Hardcoded scopes were added for remaining simulation-affecting draws:

- `OrbitalStrikeShip.SpawnSetup`, `Orbital_Precise_Strike.SpawnSetup` and
  `SpawnFlyOverShadow`, `MI_TradeShip.SpawnShipShadow`
- `RoyalTitlePermitWorker_DropSkyFaller` and
  `RoyalTitlePermitWorker_DropSkyFaller_TargetLine`
  (`OrderForceTarget` + `CallSkyFaller`), `RoyalTitlePermitWorker_Slicing`
- `Projectile_Missile_Fox3` and `Projectile_Missile_StarLight` tick/targeting
- `JobDriver_EatingMichaHalo.ApplyAfterEatingEffects`
- `MiliraImperium_Utility_Milian` weapon/shield replacement rolls
- `PawnsArrivalModeWorker_EdgeWarp_MiliraPirate` arrival and extra-pawn rolls
- `MiliraImperium_PawnGenerator_Patch.Postfix`, `FactionDialogFor_Patch.GetWeightedRandom`
- Visual-only shield break, exhaust fleck, and projectile frame draws

A new `MiliraImperium_AutoRandIsolation` module scans the loaded
`MiliraImperium` assembly at startup, patches every declared method and
constructor whose IL directly references `Verse.Rand` (including
compiler-generated iterator `MoveNext` bodies), and pushes a deterministic seed
scoped to the instance and ticks. This is the no-omission safety net for
future target versions.

### Wall-clock ship countdown

`ShipCountdown` used `Time.deltaTime`, so peers could finish the launch at
different ticks. `Patch_MiliraShipCountdownMp` keeps the original singleplayer
behavior but, in multiplayer, replaces the wall-clock countdown with a shared
`TicksGame` deadline and replays `CountdownEnded` on the exact shared tick.

### Live mod settings

`Settings_MI_Main` writes simulation-affecting fields directly from the GUI
(sliders, checkboxes, difficulty menu, reset buttons). `Patch_MiliraSettingsMp`
snapshots the tracked bool/float/enum fields on every settings-window draw,
detects changes, and dispatches one bounded sync command per changed field.
Dictionary-based config tables remain join-time config hot-sync territory.

## Sync boundaries

- Gizmo clicks: original local action runs for immediate UI, then one
  registered sync method replays durable state (field values, pawn ID, degree).
- Dialog options: Multiplayer's built-in `DiaOption.Activate` index sync
  already covers the modded faction-dialog and choice-letter paths; no custom
  duplicate dialog sync was added.
- Permits: the existing `OrderForceTarget` prefix dispatches one static sync
  command carrying `RoyalTitlePermitDef`, caller pawn, `LocalTargetInfo`,
  host tick, and free flag; the replayed executor reconstructs all permit
  context before invoking the original method.
- Rand: scopes are active only when `MP.IsInMultiplayer`; every pushed state
  is popped in a Harmony finalizer, including exception paths.

## Build and static inspection

```powershell
dotnet build .\Source\MP_MeowOnlineShop\MP_MeowOnlineShop.csproj -c Release `
  -p:OutputPath=...\BuildOutput\candidate1\bin\ `
  -p:BaseIntermediateOutputPath=...\BuildOutput\candidate1\obj\
```

- Result: 0 warnings, 0 errors.
- Candidate:
  `BuildOutput/candidate1/bin/MP_MeowOnlineShop.dll`
  SHA-256 `895E84493938A90BE86FEF8C8197C814C25EE3203B01214813B7AB2150F4A58F`
- ILSpy metadata confirms the new classes and methods:
  `MiliraImperium_AutoRandIsolation`,
  `Patch_MiliraShipCountdownMp`, `Patch_MiliraSettingsMp`,
  `SyncAuraToggle`, `SyncPsyMode`, `SyncMovingLockToggle`,
  the extra GameComponent registrations, and all new Rand-scope entries.

## Verification status

Per user instruction this pass is code adjustment only: no release deployment
and no game runtime test. The release DLL was not overwritten. The next release
step is the full host/client matrix from `references/runtime-testing.md`
(startup/join, three real-action repeats, smoke, and 120k soak).

## Remaining risk

- Dictionary-based `Settings_MI` tables are still covered only by join-time
  config hot sync, not by live field commands.
- The automatic Rand IL scanner is statically verified only; it needs one
  host/client run to confirm the startup patch count and stack balance.
- Visual-only Rand scopes may change particle/effect values in multiplayer;
  they do not change simulation state.
