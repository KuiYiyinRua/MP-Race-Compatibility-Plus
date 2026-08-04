# [MP] Multiplayer Compatibility Patches

A collection of Harmony compatibility patches designed for **RimWorld Multiplayer (ZWMultiplayer)**.

This mod mainly provides targeted compatibility fixes for the **Meow Shop / Meow Framework ecosystem**, the RJW series, selected race and story expansions, and other third-party mods currently used in the intended modpack environment.

Its primary goals are to improve the following aspects of multiplayer gameplay:

* Stability of synchronized player actions
* Deterministic random number behavior
* Consistency of jobs, commands, and pawn actions
* Usability of UI elements, Gizmos, and interaction windows
* Synchronization of Pawns, items, Hediffs, relationships, and reproductive states
* TPS performance in multi-map environments
* Multiplayer stability of shuttles, spacecraft, and special movement systems

This mod **does not replace** the official **Multiplayer Compatibility** package. Instead, it provides targeted supplementary fixes and deeper compatibility support for the mods actually being used.

---

## Required Dependency

* **Multiplayer**

  * Package ID: `rwmt.Multiplayer`

---

# Main Compatibility Features

---

# Perspective Shift Multiplayer Optimization

Multiplayer operation compatibility has been added for the following mod:

* **Perspective Shift**

  * Steam Workshop:
  * `https://steamcommunity.com/sharedfiles/filedetails/?id=3686618980`

Main improvements include:

* Multiplayer synchronization of WASD controls
* Deterministic handling of movement and camera-related operations
* Prevention of inconsistent input states between clients
* Reduced risk of desync caused by continuous key presses and special movement actions

Additional support includes:

* **Milira Race flight compatibility when using Perspective Shift**
* Improved synchronization of Milira flight states, movement input, and multiplayer commands

---

# Race and Story Expansion Compatibility

This mod provides more extensive Multiplayer compatibility improvements for the following races and their related expansions.

## Milira Race

* Core Milira race gameplay
* Special abilities, weapon modes, and flight behavior
* Perspective Shift flight controls
* Anti-gravity spacecraft behavior
* Shuttle entry, exit, and state transitions
* Suspected disconnects or client drops during multiplayer sessions

### Additional Compatibility

* **Milira Event Story Expand**

---

## Kiiro Race

* Core race states and abilities
* Special events and story progression
* Synchronization of Pawns, events, and delayed actions
* Reduced risk of desyncs or disconnects during story event triggers

### Additional Compatibility

* **Kiiro Story: Events Expanded**

---

## Ratkin

* Ratkin-related Pawn states and interactions
* Jobs, equipment, and combat behavior
* Weapon operation and generation processes

### Additional Compatibility

* **Ratkin Weapons**
* Ratkin Anomaly-related content is not included

---

## MoeLotl Race

* Core race behavior and states
* Special jobs, abilities, and interactions
* Improved stability during long multiplayer sessions

### Additional Compatibility

* **MoeLotl: Rigor Mortis**

---

## Insect Girls

* Insect Girl race states and special abilities
* Pawn generation, jobs, and combat interactions
* Deterministic random behavior and consistent multiplayer states
* Reduced risk of suspected disconnects during multiplayer sessions

---

# Spacecraft and Shuttle Stability

This mod includes multiplayer fixes for special vehicles and movement processes, with particular focus on:

* Disconnects caused by anti-gravity spacecraft operations
* Disconnects during shuttle launch, landing, or map transitions
* Inconsistent Pawn states after entering or leaving spacecraft
* Execution order of map transitions and vehicle state updates
* Synchronization of special flight behavior and multiplayer commands
* Duplicate commands or invalid states caused by repeated operations

Because spacecraft and shuttles often modify Pawns, maps, world objects, and container states at the same time, these patches prioritize consistent operation order and state-writing behavior.

---

## Meow Shop / Meow Framework Ecosystem

Multiplayer synchronization and random number fixes have been added for Meow Shop-related features, including but not limited to:

* Product selling and special selling interactions
* Slingshot-type operations
* Product purchases and confirmation processes
* Comms consoles and multiplayer Comp injection
* Shop pop-up windows and interaction interfaces
* Job assignment and order processing
* Random product generation, result generation, and state updates
* Protection against repeated clicks, repeated confirmations, and simultaneous multiplayer operations

---

## RJW Series Multiplayer Compatibility

This mod now includes targeted multiplayer compatibility improvements for the RJW series and its commonly used expansions.

The main areas of compatibility work include:

* Synchronization of core actions and long-running activities
* Job assignment and target selection
* Hediffs, needs, experience, and skill data
* Pawn relationships, age restrictions, and state changes
* Animation action chains and completion callbacks
* Reproduction, pregnancy, cycles, and generated products
* Trading, events, letters, and delayed actions
* Gene assignment, inheritance, and Pawn generation
* Furniture Gizmos, menus, confirmation windows, and repeated operations
* Deterministic random numbers, generation order, and serialization
* State stability during long multiplayer sessions

### RJW Mods Included in the Optimization Scope

#### RJW Core and Action Systems

* **RJW Core**

  * RimJobWorld
  * Covers core jobs, Hediffs, relationships, and interaction states
  * Handles the complete action list, core synchronization, and long-term multiplayer stability

* **RJW Sexperience**

  * Core sexual experience system
  * Covers experience, skills, reward data, and related UI operations

* **Rimworld Animations 2.0**

  * RJW Animations 2.0 core framework
  * Focuses on animation-driven action chains, state boundaries, and completion callbacks
  * Prevents situations where only the window is synchronized while the actual action is not

#### Cycles, Reproduction, and Body States

* **RJW Menstruation Cycle**

  * Menstrual cycle system
  * Covers cycle ticks, pregnancy, reproductive states, generated products, and related random processes

* **Sized Apparel for RJW**

  * Sar0 apparel sizing system
  * Covers apparel sizes, body states, equipment changes, and cache recalculation paths

* **RJW Cumpilation**

  * Fluid and status expansion
  * Covers product generation, Hediffs, random processing, and cleanup procedures

#### Furniture, Jobs, and Gameplay Expansions

* **RJW Onahole**

  * Adult restraint furniture
  * Covers furniture Gizmos, target selection, user states, item states, and repeated operations

* **RJW PE**

  * Heavy metal smelting core
  * Covers relationships, age restrictions, and related gameplay mechanics

* **RJW SexSlaveCraft**

  * Starfury crafting
  * Covers job assignment, slave or colonist states, and item production processes

* **RJW Brothel Colony 1.6 Test**

  * Brothel Colony
  * Covers business operations, quests, work assignment, buildings, and relationship states
  * Adds multiplayer handling for repeated confirmations and continuous business processes

#### Events and Trading Systems

* **RJW Events**

  * Adult events
  * Covers event randomness, letters, generated objects, and delayed actions

* **RJW Ero Traders**

  * Adult traders
  * Covers trading UI, inventory, silver changes, and Pawn generation order
  * Focuses on synchronizing trade confirmation commands and final trade results

#### Genes and Race Expansions

* **RJW Genes**

  * RJW gene core
  * Covers gene assignment, inheritance, Pawn generation, birth, and gene-editing UI

* **RJW Fantasy Races**

  * Fantasy race genes
  * Works together with RJW Genes to handle race generation, gene generation, and interactions involving multiple prerequisites

#### Relationships and Romance Systems

* **RJW Romance Tweaks / More Options**

  * Romance adjustments and additional options
  * Handles local menus, relationship modifications, and various confirmation callbacks

* **Rimder Romance PE Patch**

  * Rimder Romance PE patch
  * Modifies relationship and romance processes and is therefore considered a high-risk compatibility project
  * Corresponding fixes are applied according to the assemblies and patch sources that are actually loaded

> The RJW series contains many mods, and different versions may use different assemblies, method signatures, and dependencies. These patches attempt to enable themselves safely through type and method detection. If the target structure does not match, the corresponding patch will be disabled instead of forcefully modifying an unknown version.

# Additional Multiplayer Stability Fixes

In addition to the mods listed above, this compatibility patch also includes:

* Milira weapon mode synchronization
* Rigor Mortis-related compatibility
* Stabilization of world and region random numbers
* Multiplayer synchronization for selected Gizmos
* Inspection tab and interface state handling
* Synchronization of target selection and right-click menus
* Protection against duplicate clicks and duplicate commands
* Deterministic fixes for random generation order
* Isolation of UI behavior that should only execute locally
* Synchronization of UI callbacks that modify the actual game state

---

# Built-In Multiplayer TPS Optimization

This mod includes built-in TPS optimization for multiplayer environments. It is enabled by default.

The current system uses deterministic:

## Fair Map Scheduling

When multiple maps are active, map ticks are distributed according to a shared deterministic rule, reducing the continuous performance cost of non-primary maps.

Main features:

* All clients use the same scheduling rules
* The system does not depend on local frame rate or real-world time
* Prevents clients from using different tick frequencies
* Fair rotation between multiple maps
* Combat maps also participate in the optimization
* Combat maps run at least approximately once every 4 ticks
* Preserves critical state updates and multiplayer determinism

## Important Notice

Do not use this mod together with performance optimizations that modify:

* Time dilation
* Tick throttling
* Map sleeping
* Global tick processing
* Pawn tick frequency
* Other multiplayer TPS scheduling systems

When multiple mods modify tick scheduling at the same time, the following problems may occur:

* Abnormal game logic execution frequency
* Delayed jobs or combat behavior
* Inconsistent client states
* Multiplayer desyncs
* Multiplayer disconnects

---

# 3.0.18 — Multiplayer-Safe Alert UI Throttling

Added configurable throttling for repeated checks of medium-priority alerts.

Main rules:

* Medium-priority alerts may be checked less frequently
* High-priority and critical alerts always retain vanilla behavior
* Vanilla behavior is preserved when alerts are forcibly removed
* Vanilla behavior is preserved while Multiplayer synchronized commands are executing
* Vanilla behavior is preserved during ultra-fast simulation
* Actual game states are not changed; only some repeated alert UI calculations are reduced

Safety mechanisms:

* Automatically falls back to vanilla behavior if the target method signature changes
* Falls back when other Harmony patches are detected modifying the same target logic
* The patch is not forcefully applied when its safety cannot be confirmed

> MissileGirl / RocketMan itself remains incompatible with Multiplayer.
> This version only uses an independently reviewed alert UI throttling approach. It does not mean that RocketMan itself has been made compatible with Multiplayer.

---

# Usage Notes

## Load Order

Place this mod:

1. **After Multiplayer**
2. **After Meow Framework**
3. **After the main Meow Shop mod**
4. After the RJW, race, story, and gameplay mods that require compatibility patches

Recommended general rule:

> Load the mod being patched first, then load this compatibility patch afterward.

The final load order should still follow the dependency requirements of the relevant mods.

---

## Before Subscribing

Please confirm that:

* Your game version matches the `supportedVersions` entry in this mod's `About.xml`
* The currently supported version is **RimWorld 1.6**
* Multiplayer is installed correctly
* Meow Framework is installed when using Meow Shop features
* The host and all clients use exactly the same mod list
* The host and all clients use exactly the same mod versions
* No similar tick, TPS, or time-dilation optimization is enabled at the same time
* Different sources or versions of identically named RJW assemblies are not being mixed

---

# Troubleshooting

When a conflict, disconnect, or desync occurs, check the following first:

1. Whether all players are using the same Multiplayer version
2. Whether this mod and all dependencies are using the same versions
3. Whether the load order is correct
4. Whether similar performance optimizations are enabled
5. Whether duplicate Harmony compatibility patches are present
6. Whether all RJW assemblies come from the same modpack or distribution
7. Whether a mod update changed a method signature
8. Whether the issue can be reproduced consistently after disabling a specific add-on mod
9. This mod will always produce a large number of warnings during game startup. This is normal. The mod contains compatibility patches for many different mods, and yellow warning messages will be logged when those mods are not detected.

When reporting an issue, please provide:

* The complete mod list
* `Player.log`
* Multiplayer logs
* Desync logs or synchronization trace files
* The exact operations performed before the issue occurred
* Whether the issue can be reproduced consistently
* Whether the host and clients are using completely identical files

---

# Compatibility Disclaimer

The goal of this mod is to improve the multiplayer experience as much as possible within the intended modpack environment. However, it cannot guarantee that every mod combination, every version, and every gameplay process will always remain completely free of desyncs.

Some mods have the following characteristics:

* Extensive use of random numbers
* Direct modification of game states inside UI callbacks
* Use of static fields to store runtime states
* Tick-based logic that depends on local execution order
* Dynamic generation of Pawns, Hediffs, items, or world objects
* Frequent changes to assemblies and method signatures between versions

As a result, actual compatibility may still be affected by mod versions, load order, and other Harmony patches.

When a patch cannot be applied safely, it will attempt to fall back to vanilla behavior instead of forcefully injecting changes that could cause save corruption or more severe multiplayer desynchronization.

---

**Author: 尹怨怨**