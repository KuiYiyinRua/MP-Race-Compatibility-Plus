# 3.0.129 — Odyssey gravships and passenger shuttles

This change targets RimWorld 1.6 Odyssey gravships and passenger shuttles with the installed Multiplayer build. It preserves retained foreign-faction bases, restores faction context after gravship removal, and rebases gravship and carried-shuttle cooldowns between asynchronous map/world clocks. Carried shuttle cooldown state is serialized, including valid negative rebased timestamps.

Passenger loading sessions now use native serialized session generations, preventing delayed commands from an old cancelled dialog from resetting a reopened manifest. Native gravship ritual initialization is allowed when the pilot job creates its shared session during ticking, and initialization/outcomes run under the ship owner's faction.

Native shuttle unloads are deduplicated and deferred to the real destination-map tick when asynchronous time is active. Pending operations survive snapshots and rebind to their originating native unload job. Passenger draft/inventory behavior uses the passenger's owner in both clock modes; another player's base, grav-engine, or historic gravship landing does not become that passenger's home.

Functional verification uses isolated host/client processes, official DLCs, Harmony, Prepatcher, Multiplayer, the candidate and a diagnostic probe. The concise regression source is in Tests/OdysseyTransport. Evidence and exact input hashes are in BuildValidation/OdysseyTransport_20260920. No long soak is required for this delivery, per the user's requested scope. This does not assert compatibility with every unrelated third-party mod.

Final verification: R38 (async off) and R40 (async on) passed all twelve ownership cases. R39 passed 42 paired native functional records, including full pilot/landing and fresh-client recovery; the recovered queue emptied once and remained bound to its native job. Candidate and installed DLL SHA256: 8BE00524B0D2AC1B736536962E46D2972C1C1A85A00DDF8879D5498D0112F3EA. The previous 3.0.128 DLL is archived under BuildValidation/OdysseyTransport_20260920/DeploymentBackup.
