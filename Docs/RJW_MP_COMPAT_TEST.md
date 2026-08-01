# RimJobWorld 6.1.2 multiplayer compatibility test

Scope: RimWorld 1.6, `rim.job.world` 6.1.2, current Multiplayer build, and this
compatibility mod loaded after both Multiplayer and RimJobWorld.

Last automated verification: two clients, two loaded maps, 30 command stages,
all stages successful, both peers `desynced=False`, and no desync archive.

## Startup gate

1. Start RimWorld with developer mode enabled.
2. Confirm the log contains:

   - `[MP-MeowOnlineShop][RJW] patched GenerateBabies`
   - `[MP-MeowOnlineShop][RJW] installed early safe type-enumeration guard`
   - `[MP-MeowOnlineShop][RJW] attraction initialization verified`
   - `[MP-MeowOnlineShop][RJW] patched deterministic target selection`
   - `[MP-MeowOnlineShop][RJW] target resolution complete`
   - `deepTalk=True`
   - `haveSex=True`
   - `bondageJobOrder=True`
   - `deterministicSelection=True`
   - `pregnancy=True`
   - `deterministicRandom=True`
   - `attraction=True`

3. Treat a missing target or a `False` resolution value as a version/signature
   mismatch. Do not continue the multiplayer test until it is understood.

## Synced deep-talk action

1. Host a game and join from a non-host client.
2. On the client, select a controllable pawn and right-click a valid social
   target.
3. Choose the RJW social submenu and then Deep talk.
4. Confirm the interaction happens exactly once on both peers.
5. Repeat three times from the host and three times from the client.
6. Repeat on a second map while the first map remains loaded.
7. With developer mode enabled, command replay should log:
   `[MP-MeowOnlineShop][RJW][debug] replaying synced RMB deep-talk action.`

Expected result: no duplicate interaction, no immediate desync, and the command
is queued against the map that was current when the option was selected.

## Synced sex RMB actions

1. From the non-host client, choose a valid consensual RJW sex interaction.
2. Confirm exactly one job starts on both peers, with the same pawn, partner,
   job def, interaction def, and target cell.
3. Repeat three times from the host and three times from the client.
4. Repeat the same matrix for a masturbation interaction.
5. Repeat one non-solo and one masturbation action on a second loaded map.
6. With developer mode enabled, command replay should log:
   `[MP-MeowOnlineShop][RJW][debug] replaying sex RMB action`.

Expected result: the command contains only stable game references and defs;
RJW reconstructs its transient `SexInteractionResolved` state during simulation.
There must be no serialization error naming `SexInteractionResolved`.

## Synced bondage-gear job order

1. Put a usable RJW bondage item or matching holokey on the map.
2. From the non-host client, use its custom float-menu option on a valid pawn
   or target.
3. Confirm exactly one matching job starts on both peers and the item becomes
   unforbidden on both peers.
4. Repeat three times from the host and three times from the client.
5. Repeat once on a second loaded map.

Expected result: `bondage_gear_extensions.start_job` is replayed against the
map that was current when the command was issued; no duplicate job and no
immediate desync occur. The command must serialize the parent `Thing`, not
`CompUsable`; a log naming `Error writing type: RimWorld.CompUsable` is a
failure.

## Autonomous partner and victim selection

1. Let ordinary hookup, rape-enemy, prisoner-rape, and necrophilia think-tree
   jobs run through normal ticking on host and client.
2. For each job that starts, compare the selected pawn or corpse on both peers.
3. Exercise at least one selection with multiple valid above-average targets.
4. Run the same scenario on a second loaded map.

Expected result: weighted attraction choices and dictionary-backed target choices
enumerate candidates by stable Thing ID before consuming `Verse.Rand`; both
peers select the same target and retain matching random state.

## Deterministic baby trait inheritance

1. Use identical RJW pregnancy and inherited-trait settings on all peers before
   hosting.
2. Prepare pregnancies whose mother and father both have inheritable traits.
3. Confirm creation reaches RJW's production `GenerateBabies` method under a
   synchronized command.
4. Compare the deterministic trait seed and generated newborn data on host and
   client.
5. Repeat at least three times from each peer, including generation on a second
   loaded map.
6. As extended regression coverage, let a birth spawn normally, save, rejoin,
   and verify newborn traits remain identical.

With developer mode enabled, each execution logs the deterministic trait seed.
The same birth must show the same seed on every peer.

Expected result: identical inherited traits and no random-state mismatch.

The final automated run generated six pregnancies across two maps. Host and
client logged the same six seeds in the same order and completed with matching
simulation state and `desynced=False`.

## Regression coverage

From both host and non-host client, toggle RJW comfort, breeding, breeder, and
hero designations three times where applicable. Exercise fight and ordinary
socialize actions once. These paths use RJW's existing sync methods and must not
execute twice after this compatibility patch.

## Evidence to retain

- Full startup log from host and client.
- Multiplayer desync report if generated.
- Which peer initiated the action, current map, tick, pawns involved, and
  whether the action was the first attempt after loading.
