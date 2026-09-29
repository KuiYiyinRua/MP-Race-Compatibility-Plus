# UF / Vehicle Framework / Miho integration — 3.0.125

This is a partial integration, not a claim that these entire mods are multiplayer compatible.

## Sources and attribution

- UF series MP patch: HKXluo & GPT5.4, Workshop uploader MAO_LIULI. [Workshop 3737883119](https://steamcommunity.com/sharedfiles/filedetails/?id=3737883119), [source](https://git.liulikeji.cn/xingluo/uf-multiplayer-compat-pack), commit `dbd0d3509fa19dc8da0e6c9f6bec889dce8a2789`. Upstream About.xml explicitly permits viewing, learning and secondary modification; no separate LICENSE was present. `Patch_UFSeriesMp.cs` is an adaptation and retains this attribution, not an MIT relicensing.
- Thaipho: [Multiplayer Vehicle Framework Patch source](https://github.com/Thaipho/Multiplayer-Vehicle-framework-Compatibility-patch), commit `b6a33884f0aa07c84e890c199016f84311fc375a`, `PreventAsyncVehiclePathRequestInMp`. The new patch is independently implemented against the installed DLL. The repository's MIT attribution for copied MPCompat helpers was not assumed to cover its new vehicle code.
- VSauce Michael: [Vehicle Framework — Multiplayer Desync Fix](https://steamcommunity.com/sharedfiles/filedetails/?id=3779000026). Its synchronous pathfinding description is a design reference; no source code was copied.
- Thaipho: [Multiplayer Miho Patch](https://steamcommunity.com/sharedfiles/filedetails/?id=3768871549). This was a research lead, but its source was not obtainable. Our Miho changes are independent fixes based on the installed native code by Outremer / Fortified_Home; they are not represented as a port of Thaipho's implementation.

## Included

- UF: turret hold fire/target commands, top turret toggle, hologram next/autoplay, pulse armed/target/cancel, laser ADS mode/threshold/target/cancel, AT field switches/radius, weapon switching, turbojet mode/interceptor/jump commands, and timed-condition clearing.
- UF replacements preserve original command objects and disabled metadata; custom replacement controls require the original controller to be present. UI replacement is active only in multiplayer. If the standalone `hkxluo.ufseries.multiplayer.compat` is active, our UF integration yields to it.
- UF projectile Unity RNG: deterministic per-projectile/tick scope, restored by a Harmony finalizer even on exceptions. No mismatched `Thing` prefix is attached to `MoltenFlowProcess`.
- UF hologram simulation: transition state now advances on game ticks; drawing restores its temporary changes. Starting a transition no longer requires a locally initialized graphics cache. This fixes an additional native state dependency that the upstream UI patch did not address.
- Vehicle Framework: `RequestNewPath` invokes `GeneratePath` synchronously in MP, preserves `Calculating` state and cancellation-token lifecycle, and does not fall back to an asynchronous worker on error. Single player keeps the native path. Existing caravan proxy/seat/send guards remain.
- Miho: isolate `ThrowEffect` and `ThrowDustPuffThick` in four arcing projectile classes. Normal and exceptional exits restore Verse RNG; damage, homing, fragmentation and the outer projectile tick retain their original simulation randomness.

## Deliberately not included

UF remote-monitor map generation, the ordering dialog's skyfaller side effect, orbital laser closures and lava behavior require full transaction/context validation and were not merged. The original standalone patch's broad window/session workarounds and exception suppression were not imported. VF seat assignment, actual vehicle driving, world routes, launch/landing and caravan dialog completion are not certified by this update. Miho's complete combat/ability suite is not certified either.

## Validation

User requested a short combined test instead of a long soak. All runs use an isolated game directory and separate host/client save folders. Production game processes and saves are not used.

Evidence is under `BuildValidation/Integration_20260909`. The test dispatches actual UF gizmo callbacks from the non-host. It also tests Miho RNG boundaries and the native VF request prefix with an instrumented `GeneratePath` receiver. That VF check verifies synchronous dispatch/status/token behavior; it is not a driving test.

`combined-r1` is an obsolete failed test: its assertion incorrectly required all Miho methods to throw on a null map; two native methods intentionally handle that input. It must not be used as final evidence.

`combined-r2` is also not final evidence: its single-transition assertion used shared-tick timing under Superfast while the hologram had enough map ticks for several automatic transitions. The next run uses Normal speed with the same candidate DLL. Earlier Almost There / MaruTrap source edits remain in this build, but those target mods are not covered by this test configuration.

Final result and artifact identity are recorded in `BuildValidation/Integration_20260909/FINAL_RESULT.md` after the final run.
