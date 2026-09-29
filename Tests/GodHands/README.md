# God Hands multiplayer regression

`Run.ps1` builds a command-line-gated probe and creates a fresh isolated host/client installation. Supply `-CandidatePath`, `-BaselineSave` and an unused absolute `-RunRoot`. The baseline must match the script's Core/DLC, Harmony, Prepatcher, Multiplayer, God Hands and compatibility loadout. Port 30794 must be free. Normal gameplay never enables this probe.

The non-host drives the original GodHandController entry points. Assertions cover hostile rejection with opposite local settings, twenty repeated input calls producing one command, three grab/drop cycles, preview without changing Position, a held item surviving Multiplayer's official join-point save/reload, and three tool cancellations. Both peers must complete 120,000 shared **map simulation ticks** from the same serialized baseline; logs separately report Multiplayer's network timer. Async time, multifaction, developer mode and long-run desync traces are off.

The script preserves binaries, hashes, configs, baseline, command lines, peer logs and result.json. It manages only the processes it starts and never deploys to the live mod. A pass does not claim coverage of a full user mod collection, cold reconnects, wrench bulk state, or physical mouse input. The scripted test disables the designator's physical-input polling while exercising its controller boundaries.

Historical evidence is in `BuildValidation/GodHands_20260911`: `OldControl` reproduces the old implementation's twenty duplicate commands; `R4` and `R5` are successful short tests. Earlier failed fixture setups are kept separately and are not release evidence.
