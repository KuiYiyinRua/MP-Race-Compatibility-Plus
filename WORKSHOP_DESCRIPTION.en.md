# [MP] Multiplayer Compatibility Patches

## 3.0.130 (2026-09-20)

- **Gravship compatibility**: updates Odyssey piloting, takeoff, placement and landing, retained-base ownership and faction context, and gravship/carried-shuttle cooldowns across asynchronous map clocks.
- **Passenger shuttles and rejoining**: improves stale loading-command isolation, serialization and recovery of native unload queues, and passenger state after unloading at an owned or another player's base.
- **Nivarian compatibility**: extends synchronization for research and control panels, Nira modules and metrics, drones, buildings and story interactions, with simulation/render-cache/save-state separation.
- **Milira Imperium event-expansion compatibility**: updates related Milira compatibility, including The Tale of Milira event ownership, caravan-arrival context, recruitment and supply dialogs. Supply reward dialogs use event identity to reduce wrong-option routing when multiple dialogs coexist. Not every story branch has been verified.
- **Compatibility category switches**: adds a master switch and 17 independent categories, all enabled by default, including gravships/transport, Nivarian, Milira, RJW, Ratkin, Wolfein, Raven and melee animation. These settings are separate from optimization presets, require a full restart, and must match on every peer.

Validation: 3.0.130 received build, static-entry, switch-logic and release-file checks. No new in-game testing was performed for this release, as requested. Historical concise host/client checks for the 3.0.129 gravship update covered 42 paired functional records and 12 passenger-ownership scenarios with async time on/off; they do not establish runtime verification of the complete 3.0.130 package. Nivarian and event-expansion coverage does not include every story branch, mod combination or cold-rejoin scenario.

All players must install matching files and fully restart. Disabling compatibility patches can reintroduce desyncs.

Harmony patches for RimWorld 1.6 Multiplayer, supplementing the official compatibility package. Mods with compatibility patch coverage (version and feature limits apply):

- Meow Framework / Meow Online Shop
- MoeLotl Race
- MoeLotl: Rigor Mortis
- Raven Race
- Wolfein Race
- Wolfein Race GFI Expand
- Wolfein Allegiance
- Wolfein Black Science Expand
- Milira Race
- Milira Tech
- Milira Event Story Expand
- Milira Faction
- Xiyue's Milira Expanded
- 米莉拉角色拓展 / MiliraXian NeiyuLaw
- Milira Expansion
- Milira Addon
- Milira
- Sariel Milira Kiiro Attire Expaned
- Valkyrie Gunship
- ExileBrandLib
- ExileBrandTaskExend
- Ancot Library
- Ariandel Library
- ChezhouLib
- Kiiro Race
- Kiiro Story
- NewRatkinPlus
- Ratkin Weapons+
- Ratkin Knights+
- Ratkin Anomaly+
- Ratkin Underground+
- [OA] Ratkin Faction
- [OA] Oberonia Aurea Framework
- [OA] Ratkin Scenario
- Maru Race
- Nivarian Race
- Nivarian Mental Harness
- Nivarian: Apparel Store
- Nivarian Race: Draconiture
- Nivarian Race: DraconicMilitary
- Monolyn Race
- Sylvie Race
- Dragonian Mix
- Smelted Loong
- Insect Girls
- Secretary Nexus a clone race
- Cinders of the Embergarden
- kemomimihouse Kz
- kemomimihouse HardworkingKz
- Voiceroid as Animal
- Shella Backgrounds
- RimJobWorld
- RimJobWorld Pedophilia Extension
- RimJobWorld - Extension
- RJW Sexperience
- RJW Genes
- RJW Animal Gene Inheritance
- RJW Menstruation Cycle
- ElToros RJW Menstruation - Resources
- Cumpilation
- Family Overhaul
- Peculiar Institution
- RimJobWorld - Brothel Colony
- RJW Ero Traders
- RJW-Events
- RJW Consensual Non-Consent
- Privacy, Please!
- RimJobWorld - Onahole Extension
- RJW Now with balls! . . . and Ovaries I guess.
- RJW-SexSlaveCraft
- RJW Unleashed Framework
- Humpmaker Dryad
- RaddusX's Demons
- Nudity Matters More
- Equal Milking
- Romance On The Rim PE
- RimWorld Animations
- Ultimate Animation Pack (With Voice)
- Sized Apparel
- Melee Animation
- Perspective Shift
- PA's God Hands
- Achtung! 4.1.14
- Draft Anything 2.0
- Down For Me
- Defensive Positions
- Search and Destroy (Continued)
- [XND] Targeting Modes (Continued)
- Vanilla Melee Modes
- Tactical Crawling
- AutoBlink
- Sandevistan Implant
- Smart Pistol
- Cluster Projection
- The Dead Man's Switch
- The Dead Man's Switch - Power Armor Expanded
- [RH2] Rimmu-Nation² - Security
- True Shooting-Wall
- Show Weapon Tallies
- Visual Brutality
- Blood Animations
- RW Beheading
- COF's Execute cotinue / More Torture
- [QW] Archotech Implants Expanded
- Eternal Pawns
- WVC - Work Modes
- Auto Dissector
- Auto Cutter
- Hospitality (Continued)
- Go Explore!
- I will be back
- Elite Raid
- Ancient Amorphous Threat
- Quarry
- Utility Columns
- Vanilla Plants Expanded - Mushrooms
- Static Quality
- OgreStack
- Adaptive Storage - Global Settings
- Designator Shapes
- Blueprints / Blueprints Forked - 1.6
- Dubs Mint Menus
- Nice Bill Tab
- Vehicle Framework
- Tactical Fulton Extraction System
- Almost There! Fork
- RPG Style Inventory Revamped
- RPG Dialog
- [NL] Facial Animation - WIP
- [NL] Dynamic Portraits
- Simple FX: Splashes
- Performance Optimizer

- ReGrowth 2
- Yet another Optimizer / Kingfisher

## Earlier 3.0.128 changes

Retains ReGrowth autumn-leaf determinism, thread-local drawing context, diplomacy query optimization and YaOpt/Kingfisher fixes. Earlier performance samples and their separate test conditions are documented in the repository; they are not a long-soak result for this hotfix.

## Multifaction diplomacy (3.0.127)

- Factions using the same starting definition keep separate diplomacy. Reconciliation, gifts and wars do not overwrite unrelated faction pairs.
- Ordinary starts retain permanent hostility toward Milira. Milira and Kiiro starts are exempt. Milira hostility toward the Church applies only to the relevant player faction.
- Removes periodic forced neutrality and separates goodwill limits, recalculation and natural recovery timers by faction pair.
- Existing saves retain base goodwill, and new recovery timers start at zero. Legacy reconciliation without a known owner is not copied; ordinary factions may regain the hostility restriction.
- This covers diplomacy, not separate copies of every Milira story, character or quest. All players must update and restart the game.

## Additional features

- Combat speed unlock: removes forced 1x combat speed while respecting shared speed controls.
- TPS settings: optimization presets with adjustable strength and parameters.
- Experimental map scheduling; disabled by default.
- Lower UI and repeated-log overhead.

Compatibility depends on mod versions, load order and combinations; arbitrary modpacks are not guaranteed desync-free.

Requires Multiplayer; load last. All players need identical mod versions. Avoid other Tick/TPS/time-dilation schedulers.

Author: 尹怨怨

GitHub: https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus

Full coverage and validation details: https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus/blob/main/Docs/releases/3.0.130.md
