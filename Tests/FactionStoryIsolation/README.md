# Offline stage-1 routing checks

Run `dotnet run --project Tests/FactionStoryIsolation/Offline.csproj -c Release` from the mod root.

This compiles the actual production routing/session/acceptance sources with small game, MP and Scribe stand-ins, using the installed Harmony 2.4.1 on .NET Framework 4.8. Acceptance tests install real Harmony hooks on the stand-in Quest executor and an MP-shaped context prefix, verifying that denied requests reach neither the executor nor the map context. It also tests routing, disabled behavior, slate selection, save-owned preferences, persisted ownership and exception cleanup.

It does not run Unity, serialize real save files, or prove multiplayer determinism. The production projects must also build against the installed DLLs, followed by isolated host/client runtime validation before deployment. The installed Harmony failed to initialize under .NET 8; use this project's net48 target.
