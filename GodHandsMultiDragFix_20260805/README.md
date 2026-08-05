# God Hands Multi-Drag Fix 2026-08-05

Release candidate for the PA's God Hands multiplayer drag fixes in
MP_MeowOnlineShop.

## Artifact

- `MP_MeowOnlineShop.dll` SHA-256: `43186A684AAB0854D247F24078407834359038BD47DA3CE50386DDF2DF7359B4`
- Deployed to: `1.6/Assemblies/MP_MeowOnlineShop.dll`

## Source authority

- Target mod: PA's God Hands, package `Palpha.godhands`, version 2.0
- Installed assembly: `H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\3610069762\1.6\Assemblies\PA_GodHands.dll`
- Installed assembly SHA-256: `B2A2127B804E64F7F51C40731190746DABB70066B9122E27248FB405A33CDBAF`
- The installed assembly hash matches `Validation_GodHands_20260805/source/1.6/Assemblies/PA_GodHands.dll`,
  so the validation source is authoritative for the patched methods.

## Changes

- God Hands drag sessions are now keyed by map plus issuing player, so two
  players can drag different pawns on the same map without clobbering each
  other's session.
- Grabbed pawns/items are rejected when another player's active session already
  holds them, preventing the same object from being claimed twice.
- Dragged pawns now update `PawnTweener.tweenedPos` and
  `lastTickSpringPos` during drag commands, matching the original mod's visual
  behavior so the pawn remains visible while it is being dragged.
- Normal release and force release now end the grabbed pawns' Wait jobs and
  force release restores despawned grabbed items at the grab start cell.
- The local God Hands/God Wrench designator controller is only populated from
  the local player's own session; commands issued by other players no longer
  rewrite local UI state.
- God Wrench sessions received the same per-player isolation and duplicate
  start guard.

## Verification

- `dotnet build -c Release` succeeds with 0 warnings and 0 errors.
- Metadata inspection confirms the new sync method signatures, `GetLocalPlayerId`,
  `PawnTweenedPosField`, `PawnLastTickSpringPosField`, and all God Hands patch
  classes are present in the DLL.
- No game runtime test was performed, per request.
