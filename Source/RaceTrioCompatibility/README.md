# Race trio compatibility supplement

Optional runtime patches for the installed RimWorld 1.6 builds of `keeptpa.NivarianRace` (3624805128), `ASEL.MonolynRace` (3742031864), and `hatena.VoiceroidAsAnimal` (2073559411). This assembly supplements the existing `MP_MeowOnlineShop.dll`; it does not replace it. Each integration is enabled only when its target package is active.

Build with `dotnet build Source/RaceTrioCompatibility/RaceTrioCompatibility.csproj -c Release`. Override `GameRoot` and `ReferenceRoot` with MSBuild properties on another machine. The latter contains the Harmony and Multiplayer mod directories. The default reference directory points to the existing isolated validation installation, not to redistributed dependencies.

Install only `bin/Release/net48/Meow.RaceTrioCompatibility.dll` into the mod's `1.6/Assemblies` directory. Do not distribute the validation harness, decompiled third-party code, target mod assemblies, or the isolated game directory. Keep the supplement and core identical on all peers.

The registrations intentionally target the inspected binary's exact method names and signatures. Missing required registrations are errors; a new upstream build requires another audit. The startup counter reports explicit registrations in Bootstrap, separately from the worker, field, and wrapper registrations in the supporting modules.

UI field registrations are paired with WatchBegin/WatchEnd at their actual editing boundaries. Recruitment sends a guarded candidate identity rather than an unsaved pawn reference. Mothership targeting sends the support definition, map and chosen cell. Wanderer decisions retain the pending pawn in a saved GameComponent. GUID-based simulation shuffles use Verse.Rand in multiplayer, while generated light-network labels use a stable parent ID seed without advancing simulation randomness.

Validation scripts and immutable per-run logs are under `BuildValidation/RaceTrio_20260909`; see the matching document under `Docs` for the tested scope and limitations.

## 1.1.3 compatibility additions (2026-09-10)

Includes the earlier aid-event, brothel-price and selection-boost fixes. Additional synchronized boundaries cover scale thresholds and regional focus, weapon transformation, flight mode, skill absorption and XP-canister settings/actions, energy-tower battery selections, Nira glow settings, the seven storyteller settings, and manual work priorities. Holo-dice daily refresh no longer mutates shared state from drawing the UI; its existing simulation tick remains responsible for refresh.

Drone approach/reposition/orbit, programmable movement arcs, attached-turret idle rotation and Royalty shield return-shot scatter use Verse randomness in multiplayer, retaining Unity randomness in singleplayer. Spawn-after-load preserves the saved XP-canister skill and flight mode. Wanderer and shelter decisions carry an explicit map. Shelter choices are saved as pending quest/part/map references and consumed once before replaying the original complete outcome.

Candidate: `BuildValidation/NivarianAudit_20260910/Candidate/Meow.RaceTrioCompatibility.dll`, version 1.1.3, SHA256 `7641129FCEABE3005C005F9B1BC7159054E888EBC2AB313993F17DEC1C54E10A`. Compilation succeeded with zero warnings and errors. No tests or game runs were performed for this revision, as requested; earlier runtime results do not validate it. The candidate has not been copied into the live Assemblies directory.

This is an expansion of confirmed coverage, not an exhaustive compatibility certification. Arbitrary development/debug windows, all mod combinations, cross-faction gameplay and long-running reload behavior remain outside validated coverage. A shelter decision already lost before installing this revision cannot be reconstructed unambiguously from an old save. All peers must use the same gameplay-affecting mod settings and binaries.
