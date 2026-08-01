# [MP] Meow Online Shop Ecosystem — Multiplayer Compatibility Patches

A **Harmony** patch collection for **RimWorld Multiplayer (ZWMultiplayer)**. It improves **sync stability** and **playability** for the **Meow Framework / Meow Online Shop** ecosystem and several third‑party features in multiplayer—focusing on determinism, RNG, and UI/interaction alignment. It is **not** a full replacement for the official *Multiplayer Compatibility* pack; it targets mods you actually run in your list.

---

## Required

- **Multiplayer** (`rwmt.Multiplayer`)

## Optional

- **Meow Framework** (`EoralMilk.MeowFramework`)  
  Meow shop–related patches (including the comms-console multiplayer comp) apply only when this is enabled. Other fixes that detect target types at runtime can still work without it.

---

## What it covers (may change by version)

- Meow shop: sell/slingshot-style interactions, purchase flow, comms console & float menu, work assignments—sync and RNG-related fixes (requires Meow Framework)
- Milira weapon mode and similar desync-risk handling
- Rigor Mortis and other mods in MP
- World/region RNG and some gizmos/inspect labels for deterministic MP behavior
- **Built-in MP TPS optimization** (on by default): deterministic “fair map scheduling” to reduce multi-map tick cost

---

## Notes

- **TPS optimization:** Do **not** stack with other TPS/performance mods (time dilation, global tick throttling, heavy performance overhauls)—risk of conflicts or desyncs. Combat maps participate under the current policy (roughly at least one tick every ~4 ticks minimum).
- **Load order:** Load **after Multiplayer**; if you use the Meow stack, put **Meow Framework** and the shop mod(s) **before** this patch mod.

---

## Before you subscribe

- Match your game version to **supportedVersions** in `About.xml` (currently **1.6**).
- If something conflicts or desyncs, check dependencies, load order, and whether another performance/TPS mod is active.

---

*Author: 尹怨怨*

## 3.0.18 — MP-safe alert UI throttling

- Adds a configurable throttle for repeated medium-priority alert UI checks.
- High/critical alerts, forced removals, sync commands, and ultrafast simulation always use vanilla behavior.
- Automatically falls back to vanilla when the target signature changes or another Harmony owner patches the same method.
- MissileGirl/RocketMan itself remains incompatible with Multiplayer; this release adopts only the independently audited alert-UI concept.
