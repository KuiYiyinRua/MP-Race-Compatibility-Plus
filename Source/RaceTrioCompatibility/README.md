# Race trio compatibility supplement

Optional runtime patches for the installed RimWorld 1.6 builds of `keeptpa.NivarianRace` (3624805128), `ASEL.MonolynRace` (3742031864), and `hatena.VoiceroidAsAnimal` (2073559411). This assembly supplements the existing `MP_MeowOnlineShop.dll`; it does not replace it. Each integration is enabled only when its target package is active.

Version 1.1.0 extends the Nivarian integration to Mental Harness (3720877013), Apparel Store (3747540804), Draconiture (3686517288), and DraconicMilitary (3735573834). See [the expansion notes](../../Docs/Nivarian-Expansions-Multiplayer.md) for synchronization boundaries, binary provenance and runtime coverage. Expansion registrations emit their own `NivarianExpansionCompat` startup records.

Build with `dotnet build Source/RaceTrioCompatibility/RaceTrioCompatibility.csproj -c Release`. Override `GameRoot` and `ReferenceRoot` with MSBuild properties on another machine. The latter contains the Harmony and Multiplayer mod directories. The default reference directory points to the existing isolated validation installation, not to redistributed dependencies.

Install only `bin/Release/net48/Meow.RaceTrioCompatibility.dll` into the mod's `1.6/Assemblies` directory. Do not distribute the validation harness, decompiled third-party code, target mod assemblies, or the isolated game directory. Keep the supplement and core identical on all peers.

The registrations intentionally target the inspected binary's exact method names and signatures. Missing required registrations are errors; a new upstream build requires another audit. The startup counter reports the 56 explicit registrations in Bootstrap, separately from the worker, field, and wrapper registrations in the supporting modules.

UI field registrations are paired with WatchBegin/WatchEnd at their actual editing boundaries. Recruitment sends a guarded candidate identity rather than an unsaved pawn reference. Mothership targeting sends the support definition, map and chosen cell. Wanderer decisions retain the pending pawn in a saved GameComponent. GUID-based simulation shuffles use Verse.Rand in multiplayer, while generated light-network labels use a stable parent ID seed without advancing simulation randomness.

Validation scripts and immutable per-run logs are under `BuildValidation/RaceTrio_20260909`; see the matching document under `Docs` for the tested scope and limitations.
