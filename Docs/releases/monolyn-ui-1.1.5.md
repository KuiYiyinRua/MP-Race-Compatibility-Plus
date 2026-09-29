# Monolyn UI hotfix — RaceTrio 1.1.5 (2026-09-29)

修正 Monolyn 信息制造机切换生产模式时的 `selectedOption not found` 异常，同时修正重力柱半径滑条的同类同步字段查找错误。监听使用与注册一致的父类类型，保留原生按钮行为及多人同步回滚/执行机制。

Fixes the Monolyn information-fabricator production-mode menu exception and the equivalent inherited-field lookup in the gravity-pillar radius slider. Uses the registered base type when watching SyncFields; native callbacks and multiplayer rollback/replay are preserved.

Only `Meow.RaceTrioCompatibility.dll` changes in the Workshop runtime payload (1.1.4 → 1.1.5). The complete existing 3.0.136 Workshop package is preserved. The existing core already registers the corresponding base-type fields. No host-config candidate, turret optimization update, or other local modules are bundled.

Validation: compilation and DLL/asset integrity checks passed; an isolated host/client run reproduced the original failure with 1.1.4. Candidate 1.1.5 loaded and registered its targets successfully. New-client action assertions, full multiplayer smoke, and long soak were not completed. No additional gameplay tests were run for publication.

All players must install the same update and fully restart RimWorld.

The GitHub asset is the original incremental ZIP, byte-for-byte unchanged. Its README documents the local complete 3.0.140 installation used to build/check it. Workshop users receive the complete existing Workshop installation with the same replacement DLL automatically.

- DLL SHA256: `3EAC3184A18A5AA3A9C8A2A0D4C4C82FB5ACCD0A51DD14ABC516D5E435C9F91C`
- ZIP SHA256: `E7DAC6DB8A25B6BE5D3E456F8C5280F8FAF694462A7051AC4938BA29C41BD094`
