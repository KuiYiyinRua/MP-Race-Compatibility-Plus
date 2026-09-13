# [MP] Multiplayer Compatibility Patches

## 3.0.128 desync hotfix (2026-09-13)

- **Nivarian Race**: selection boost now uses focus delivered through Multiplayer map commands instead of each computer's local selection during simulation. Original hediff creation and ramping remain intact; duplicate selection does not multiply the effect, and abandoned focus expires after 180 map ticks.
- **Milira Imperium / MiliraXian NeiyuLaw**: automatic special-pawn ideology conversion uses the pawn's owning player faction. Existing conversion queues and delays are preserved, avoiding branches based on the local player's faction.
- **Raven Race**: includes the previously deployed apparel-render cache concurrency guard. It protects shared dictionary access; no overall TPS improvement is claimed.

Resuming time alone can trigger the original issues because both automatic culture checks and selection boosts run during simulation ticks. The two diagnosed causes cover reports 62 and 65–67. The Kiiro job-ending divergence in reports 63/64 remains unresolved; this is not a claim that all reports are fixed.

Validation: 45 offline regression checks passed. Three Nivarian host/client smoke runs covering one/two maps and sync/async time each passed 12,000 shared ticks. A representative 316-mod, three-map, multifaction, async-ON save passed 10,008 shared ticks. A separate Kiiro warm-rejoin comparison matched pawn snapshots but failed its final world-clock measurement assertion; it is not counted as a complete pass. Further testing was stopped at the maintainer's request before publication. The 120,000-shared-tick soak and three cold-rejoin cycles remain incomplete; long-term stability is unverified.

All players must install identical files and fully restart RimWorld. For config mismatches, use Multiplayer's native Fix and Restart instead of bypassing startup-setting differences. Keep a pre-update save backup.


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

Full coverage and validation details: https://github.com/KuiYiyinRua/MP-Race-Compatibility-Plus/blob/main/Docs/releases/3.0.128-desync-hotfix.md
