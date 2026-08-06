# Multiplayer TPS 0 / time-speed regression (2026-08-05)

## Symptom

Multiplayer TPS is permanently 0 and time-speed controls have no effect, including on a brand-new multiplayer save. The 16:00-ish build (for example `C:\tmp\mp-build-gravship-headfix\MP_MeowOnlineShop.dll`, SHA-256 `07990DB06BFB2375C4D357773E7FA874B0DD2E4859CB2EAF5857228E0562261E`) works.

## Regression boundary

`Patch_AsyncTickSchedulerPhase.cs` was introduced in commit `323b184` (2026-08-05 19:31). The working 16:24 build does not contain the type at all. The first broken deployed candidate we can prove is the 19:31 build `43186A684AAB0854D247F24078407834359038BD47DA3CE50386DDF2DF7359B4`, and the 21:50 build `A8B5F5F28B2329C8` still contains the same bug.

## Root cause

`Multiplayer.Client.TickPatch.DoTick` adds `1f` to every tickable's `TimeToTickThrough` before calling `TickTickable`:

```csharp
tickable.TimeToTickThrough += 1f;
TickTickable(tickable);
```

`Patch_AsyncTickSchedulerPhase.TickTickablePrefix` then overwrote the accumulator with `-timePerTick`. Because the prefix runs after the `+1f`, the vanilla `while (TimeToTickThrough >= 0)` loop always sees a negative value and never executes the tick body. At every time speed the map/world tickables are skipped, so shared simulation stops and TPS stays 0.

## Fix

Set the normalized accumulator to `1f - timePerTick` instead of `-timePerTick`. This reproduces the intended phase as if the normalization had happened before `DoTick`'s `+1f`:

- Normal speed (`timePerTick == 1`): runs exactly one tick per pass.
- Faster speeds (`timePerTick < 1`): runs `1 / timePerTick` ticks per pass.
- Slower speeds (`timePerTick > 1`): runs every `timePerTick` passes, deterministically.

The phase guard now also applies during join/rejoin catch-up simulation, which is exactly when a cold client's accumulator phase differs from the long-running host.

## Artifact

- Source: `Source/MP_MeowOnlineShop/Patch_AsyncTickSchedulerPhase.cs`
- Deployed DLL: `1.6/Assemblies/MP_MeowOnlineShop.dll`
- Informational version: `3.0.117-tps-fix-1f-phase`
- SHA-256: `C3606F5B29DF0FA6BB4160A0981F1C2E6D88303A4CA0B1E80A54703CC2F5DE11`
- Build: `dotnet build -c Release`, 0 warnings, 0 errors.

## Status

Compiled, statically inspected, and deployed. Runtime host/client smoke verification is still required after the fix.
