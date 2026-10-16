# Native multi-map power regression

Build `Probe.csproj` against the installed RimWorld/Multiplayer assemblies before launching. The probe runs only with `-moteprobe -powerprobe`; it must never be deployed to the player's mod.

`Run.ps1` creates a fresh, isolated host/client pair. `-Core` identifies the immutable candidate. Use a unique `-RunRoot` and free `-Port`. Both peers use the same copied mod payload. The loadout is Core/DLCs, Prepatcher, Harmony, Multiplayer and this compatibility mod; this is not the full user modpack.

Example from the repository root:

```powershell
dotnet build Tests/PowerMap/Probe.csproj -c Release
& Tests/PowerMap/Run.ps1 -RunRoot G:\PM04 -Core BuildValidation/PowerMapFix_20261013/CandidateR2/MP_MeowOnlineShop.dll -Port 31184 -TwoMaps -Ticks 120000
```

The non-host issues three synchronized fixture commands. Each constructs a native wood generator, fueled normally, eight conduits and a lamp on each map. The probe checks that the generator and lamp share a native power net and become powered without forcing `PowerOn`. Each round selects one of the three legacy scheduler modes, with the legacy enable flags set, to prove that old saves cannot reactivate throttling.

Assertions cover actual power-manager calls per map tick, correct ticking-map ownership, native static/thrown fleck age and expiry, independent client camera changes, and exact frozen world/map tick and random-state equality before and after the run. Completion uses shared world ticks from the last fixture command. `-AsyncTime` exercises asynchronous dispatch; `-Rejoin` performs three cold client restarts, comparing the frozen baseline after each join and repeating all three commands. Intermediate rejoin cycles run for 10,000 world ticks; the final cycle uses `-Ticks`.

`-LegacyPower` is only for the old-binary negative control: it allows the old skipped power-manager calls so the test can reach the lamp-startup assertion. PM02 reproduces map 0 powered / map 1 lamp unpowered. It is not a passing release test.

`ValidateRelease.py` checks the actual DLL for retirement of the whole-map prefix and exact preservation of the serialized game component against a specified old DLL. It complements runtime assertions, not replaces them.

Keep `result.json`, input hashes, command lines, ready/final markers, logs and screenshots. Cold-client logs are archived under `Cold1`–`Cold3`. Do not change any input during a run or reuse an existing run directory. Cleanup is restricted to PIDs owned by the controller.
