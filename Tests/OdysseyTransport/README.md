# Odyssey transport functional regression

Scope: passenger shuttles and gravships, two local Multiplayer peers, player factions A/B, independent map clocks. No long soak is enabled by default.

Build the probe with `dotnet build Probe.csproj -c Release /p:GameRoot="<game>"`. Build the production candidate separately with both OutputPath and OutDir redirected; never overwrite the installed DLL before validation.

Run `Run.ps1 -GameRoot <game> -RunRoot <new-directory> -Candidate <candidate-dll> -BaselineSave <two-map-save> -PilotAfter -RecoveryCycles 1` for combined native functional coverage: refuel dialogs, loading and hauling, shuttle flights, gravship transfer cooldowns, full pilot/ritual/destination/landing, warm snapshot and one fresh client recovery with a pending native unload job.

Run `-UnloadControl` for six passenger home/foreign faction cases with asynchronous time enabled. Add `-ControlMaps 2` for the same cases with asynchronous time disabled. Use different `-Port` values for concurrent isolated runs.

Run `-StaleOnly` for cancel/reopen session generation and delayed Reset regression. `-ControlMaps 1` covers native refuel/loading/hauling in single-map, single-faction, synchronous mode.

Each run freezes candidate/probe/config hashes and source, keeps separate saves and logs, verifies paired records and cleans up only its owned game processes. Result status DIAGNOSTIC means the selected functional scenario completed; it is not a claim about untested mods or unlimited play duration. The two-map fixture used during development is recorded in BuildValidation/OdysseyTransport_20260920/R9-ClockRefuelSnapshot/Host/Saves/OdysseyBaseline.rws.

The test mod is diagnostic only and must not be enabled in normal saves. Optional soak switches are retained for future diagnostics, but are not required for this delivery.
