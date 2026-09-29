# Passenger shuttle shared flight clock regression

Run against the exact archived production DLL. Build Probe.csproj with GameRoot pointing to the installed RimWorld root. Run.ps1 requires a fresh RunRoot and the two-map baseline recorded in BuildValidation/OdysseyTransport_20260920/R9-ClockRefuelSnapshot/Host/Saves/OdysseyBaseline.rws.

Use -FlightGuard for three real CompLaunchable.TryLaunch flights from the non-host. The probe deliberately halves ONLY the client's native TraveledPctStepPerTick result. It logs FLIGHT_ARRIVED with world/shared ticks and asserts native arrival, fuel, faction, passenger and cooldown outcomes. Compare the three FLIGHT_ARRIVED lines exactly across peers, in addition to the controller's paired functional records.

Add -ControlMaps 2 to test synchronous mode; omit it for asynchronous mode. Use distinct ports for parallel pairs. The old installed DLL fails under this injected fault. The new flight-plan patch must pass both modes despite the unequal local rate.

Add -FlightSnapshot to slow the fixture's rate and request one genuine server joinpoint while the first flight is in progress. Both peers must log identical nonzero FLIGHT_SNAPSHOT LOADED duration/elapsed and finish all three flights at equal world ticks. This is a warm snapshot regression, not a cold-client rejoin claim.

All isolated Prefs are muted (master/game/music/ambient/UI=0). No long soak is enabled. Original live saves/configs remain untouched. The diagnostic mod is test-only and must not be copied into normal release assemblies.
