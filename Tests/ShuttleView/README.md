# Shuttle world-view regression

Command-line-gated isolated two-peer driver, derived from TradeWindowAutoOpen.
Normal games do not execute the probe. Compile with `dotnet build Probe.csproj -c Release`.
From the mod root run `Tests/ShuttleView/Run.ps1 -RunRoot <new-short-path> -Candidate <archived-DLL>`.

Rounds 0–9 retain native settlement, delayed caravan arrival, encounter, disabled-setting and manual reopen checks.
Rounds 10–12 invoke Multiplayer's native CaravanShuttleUtility.LaunchShuttle from the non-host client.
Rounds 13–15 invoke native CompLaunchable.TryLaunch on a spawned passenger shuttle.
All six flights reach TransportersArrivalAction_Trade through native world ticks.
The uninvolved host starts on the map and must remain there; the issuer receives the native arrival jump and window.
Shared session participants and tradeables must agree; both peers must still manually reopen the session.
After cleanup, both peers run another 120,000 shared ticks with async time and multifaction disabled.

The world-departure assertion fails on the previous patch because its ungated TryJump moves the uninvolved host to Planet mode.
This fixture covers the original/DLC loadout, not the player's complete mod list or cold rejoin.
