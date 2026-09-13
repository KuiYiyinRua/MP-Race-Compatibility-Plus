# [MP] Multiplayer Compatibility Patches

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

## 3.0.128 update (2026-09-13)

- ReGrowth 2: removes autumn leaf simulation's dependency on a local rendering cache, addressing view-dependent spawned objects and Thing IDs.
- Multifaction diplomacy: reduces reflection calls, temporary allocations and repeated lookups while preserving faction order, diplomacy rules and recovery timers. Simulation tick frequency is unchanged.
- New separate parallel-render compatibility module: uses thread-local Multiplayer drawing context with nested scope restoration and target-shape checks. This does not authorize arbitrary multithreaded simulation optimizers.
- Retains the melee-animation component lookup optimization and the YaOpt / Kingfisher item-index removal boundary fix.

Validation: RimWorld 1.6.4850 rev646 and Multiplayer 0.11.5+4a3be27-dirty. A 316-mod combination ran two game processes on one PC, with two maps, multiple factions and async time OFF, for over 120,000 map/world ticks. Final random states, Thing IDs and 28 pawn/diplomacy snapshots matched; three non-host recruitment actions were included. ReGrowth was checked separately in a 318-mod, three-map, async-ON smoke run of 10,008 shared ticks. Cross-PC, cold-rejoin and long ReGrowth runs remain unverified; this is not certification of every feature in every listed mod.

Fixed-work Dubs Performance Analyzer samples showed approximately 29–31% less time in the diplomacy recovery hotspot and 27% less in record lookups. Overall TPS stayed around 153–155; an overall TPS gain has NOT been demonstrated. The profiler is a measurement tool and is not shipped with this mod.

Known limitations: this combination still reports a Kiiro story null-map warning, a Defensive Positions legacy Multiplayer API warning and EliteRaid patch warnings. Patch coverage does not mean all such warnings are resolved. All players should update to identical versions and fully restart the game.


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

Full coverage and validation details: https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus/blob/main/Docs/releases/3.0.128.md
