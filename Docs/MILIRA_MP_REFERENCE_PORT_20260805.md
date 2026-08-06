# Milira_MP Reference Port (2026-08-05)

## Reference source

- Workshop: `G:\Steam\steamapps\workshop\content\294100\3725772639`
- Package ID: `usamiseika.fixmod.miliramultiplayer`
- Assembly: `1.6\Assemblies\Milira_Comp.dll`
- SHA-256: `3749910FA07FDC70675EC3356FD3B9A...` (full hash recorded in
  `BuildValidation` provenance if needed; the DLL contains no shipped source, so
  `ilspycmd` was used and the decompiled tree is kept in `C:\tmp\MiliraMP_Decompiled_20260805`).

## Ported modules

All ported classes live under `Source\MP_MeowOnlineShop\MiliraAddons` in the
unique namespace `MP_MeowOnlineShop.MiliraAddonCompat`, so they do not collide
with the reference assembly if both mods are enabled.

| Reference module | Target mod | Files |
|---|---|---|
| `FunnelBit_Compat` + Rand isolation | Fianchetto Variation (`rabiosus.funnelmilian`) | `FunnelBit_Compat.cs`, `FunnelBit_RandIsolation.cs` |
| `MilianModification_Compat` + Rand isolation | Milira Tech: Milian Modification (`Ancot.MilianModification`) | `MilianModification_Compat.cs`, `MilianModification_RandIsolation.cs` |
| `MiliraExpandedXY_Compat` | Xiyue's Milira Expanded (`XiyueYM.MiliraExpandedXY`) | `MiliraExpandedXY_Compat.cs` |
| `AriandelLibrary_Compat` | Ariandel Library (`Ariandel.AriandelLibrary`) | `AriandelLibrary_Compat.cs` |
| `AriandelMiliraImperium_Compat` + Rand isolation | Milira Imperium (`Ariandel.MiliraImperium`) | `AriandelMiliraImperium_Compat.cs`, `AriandelMiliraImperium_RandIsolation.cs` |
| `ChezhouLib_Compat` | ChezhouLib (`Chezhou.ChezhouLib.lib`) | `ChezhouLib_Compat.cs` |
| `MiliraXian_NeiyuLaw_Compat` + weapon switch | 米莉拉角色拓展 v1.1 (`HeChuanRiver.MiliraXian.NeiyuLaw`) | `MiliraXian_NeiyuLaw_Compat.cs`, `MiliraXian_NeiyuLaw_WeaponSwitch.cs` |
| `PLAMilira_Compat`, `PLAMilira_GizmoCompat`, `PLAMilira_StrikeRandFix` | Wings of Democracy (`sleepycot.wingsofdemocracy`) | `PLAMilira_Compat.cs`, `PLAMilira_GizmoCompat.cs`, `PLAMilira_StrikeRandFix.cs` |
| `PLAMiliraSariel_Compat` + Rand isolation | Sariel Milira attire (gated by `Ariandel.MiliraImperium`) | `PLAMiliraSariel_Compat.cs`, `PLAMiliraSariel_RandIsolation.cs` |
| `ValkyrieGunship_Compat` | Wings of Democracy (`ValkyrieGunship.*`) | `ValkyrieGunship_Compat.cs` |
| `YaoYao_Compat` | Milira Expansion: YaoYao (`ZuoYao.MiliraYaoYao`) | `YaoYao_Compat.cs` |
| `ExileBrandLib_Compat` + task extension | ExileBrand / 布兰特 (`chezhou.Kind.MiliraExpansionExileBrand`) | `ExileBrandLib_Compat.cs`, `ExileBrandTaskExend_Compat.cs` |
| `AriandelPsionicWindow_Compat` | Fianchetto psionic window | `AriandelPsionicWindow_Compat.cs` |
| `TaleOfMilira_Compat` | The Tale of Milira | `TaleOfMilira_Compat.cs` |

`CompatUtility.cs` is the shared rand-scope / sync-registration helper from the
reference, renamed into our namespace.

## Adjustments made during the port

1. Every ported module first checks `MiliraMpCompatGate.ReferenceModActive`
   (`usamiseika.fixmod.miliramultiplayer`) and skips itself when the original
   reference mod is active, preventing duplicate sync registrations.
2. `TaleOfMilira_Compat` intentionally excludes the Milira supply world object;
   the existing `Patch_MiliraSupplyMp` already owns that dialog path. The other
   ten Tale of Milira world objects now get `RegisterSyncDialogNodeTree`.
3. The reference's `MilianModification.Command_MilianAbility.ProcessInput`
   debug-only prefix was removed; it logged per click and did not sync anything.
4. ILSpy artifacts were rewritten to valid C#: `(ref x)` casts, `_002Ector`
   struct constructor calls, implicit-operator calls, and nested
   `DamageWorker.DamageResult` references.

## Deliberately not ported

- `Milira_Compat.cs` / `Milira_RandIsolation.cs`: our mod already ships
  overlapping Milira base patches (weapon/shield/flight/fly, storyteller and
  game-component rand scopes, Milira supply dialog).
- `AncotLibrary_Compat.cs`: our mod already owns the main Ancot command,
  shield, flight, and weapon-mode boundaries; wholesale import would duplicate
  `RegisterSyncMethod` / `RegisterSyncField` calls.
- `Milian_MP.Vanilla_RandIsolation` / `AlienRace_RandIsolation`: broad vanilla
  and AI rand scopes that duplicate existing `MP_MeowOnlineShop` stabilizers and
  depend on the reference mod's settings UI.
- `GizmoSyncDebugger.cs`: diagnostic-only tooling, unsuitable for release.

## Build / verification

Compiled without deploying:

```powershell
dotnet build .\Source\MP_MeowOnlineShop\MP_MeowOnlineShop.csproj -c Release `
  -p:OutputPath=C:\tmp\MiliraAddonCandidate_20260805\bin\ `
  -p:BaseIntermediateOutputPath=C:\tmp\MiliraAddonCandidate_20260805\obj\
```

- Result: `0` warnings, `0` errors.
- Candidate: `C:\tmp\MiliraAddonCandidate_20260805\bin\MP_MeowOnlineShop.dll`
- Candidate SHA-256: `9A93ABF98783D1181D61EEAA1096447465E0AFC803F6CF9E6416F2D6125B4890`
- Release DLL untouched:
  `H:\下载\元之雨RJW全种族拓展\rim\rim\rimworld\Mods\MP-meow-online-shop\1.6\Assemblies\MP_MeowOnlineShop.dll`
  (SHA-256 `8028E7B68BEE89A10B7B1BC74378A1808F4A48BC85C7CA80378D9C46159E7811`, timestamp `2026-08-05 13:34:41`).
- Metadata inspection confirms all ported classes are present in the candidate
  assembly.

## Runtime status

Per user instruction, no game runtime test was run and the formal DLL was not
deployed. The next step before release is a real host/client load with the
installed Milira addon loadout, then targeted smoke and soak.
