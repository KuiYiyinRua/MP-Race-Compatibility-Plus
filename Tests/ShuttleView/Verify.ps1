param([Parameter(Mandatory=$true)][string]$RunRoot,[switch]$TargetedSmoke)
$ErrorActionPreference='Stop'
$run=(Resolve-Path -LiteralPath $RunRoot).Path
function Require($ok,$why){if(!$ok){throw $why}}
foreach($peer in @('host','client')){
 $log=Get-Content "$run/$peer.log" -Raw
 Require ($log -match 'Shuttle camera routing ready') 'Shuttle patch registration missing'
 Require ($log -notmatch 'TRADE_WINDOW FAILED|World cmd exception|Map cmd exception|Desynced after|Sync Error|PacketReadException|Inconsistent player|desynced=True|REQUIRED_TARGET_FAILURE trade auto-open') 'Runtime failure marker'
 Require ([regex]::Matches($log,'TRADE_WINDOW AUTO_ASSERT round=').Count -eq 16) 'Missing automatic presentation assertions'
 Require ([regex]::Matches($log,'TRADE_WINDOW MANUAL_ASSERT round=').Count -eq 16) 'Missing manual reopen assertions'
 Require ([regex]::Matches($log,'TRADE_WINDOW SHUTTLE_DEPARTURE round=').Count -eq 3) 'Missing world-departure assertions'
 if($TargetedSmoke){
  $start=[regex]::Match($log,'TRADE_WINDOW PREPARE round=16 tick=(\d+)')
  $checkpoint=[regex]::Matches($log,'TRADE_WINDOW SHARED_CHECKPOINT tick=(\d+) target=(\d+) players=2 desynced=False') | Select-Object -Last 1
  Require ($start.Success -and $checkpoint -and ([int]$checkpoint.Groups[1].Value-[int]$start.Groups[1].Value -ge 10000)) 'Targeted smoke below 10000 shared ticks'
  Require (Test-Path "$run/TARGETED-SMOKE.json") 'Pre-shutdown smoke evidence missing'
 }else{
  Require ($log -match 'TRADE_WINDOW COMPLETE tick=\d+ desynced=False') 'Shared completion missing'
  Require ((Get-Content "$run/$peer.complete" -Raw).Trim() -eq '120000 shared ticks; desynced=False') 'Soak duration marker missing'
 }
 Require ($log -match [regex]::Escape((Get-Content "$run/candidate.sha256" -Raw).Trim())) 'Loaded candidate provenance missing'
}
for($n=0;$n -lt 16;$n++){
 Require ((Get-Content "$run/host.observed$n" -Raw) -ceq (Get-Content "$run/client.observed$n" -Raw)) "Shared trade mismatch round $n"
 Require ((Get-Content "$run/host.manual$n" -Raw).Trim() -eq 'PASS') 'Host manual reopen missing'
 Require ((Get-Content "$run/client.manual$n" -Raw).Trim() -eq 'PASS') 'Client manual reopen missing'
}
Require ((Get-FileHash "$run/Host/Config/ModsConfig.xml").Hash -eq (Get-FileHash "$run/Client/Config/ModsConfig.xml").Hash) 'Peer loadout mismatch'
$hash=(Get-FileHash "$run/Candidate/MP_MeowOnlineShop.dll").Hash
Require ($hash -eq (Get-FileHash "$run/Game/Mods/Candidate/Assemblies/MP_MeowOnlineShop.dll").Hash) 'Candidate changed'
if($TargetedSmoke){
 [ordered]@{status='TARGETED_SMOKE_PASS';candidate=$hash;scope='16 paired actions including 6 native shuttle flights; at least 10000 shared ticks; full 120000 tick gate incomplete'} | ConvertTo-Json | Set-Content "$run/TARGETED-VERIFICATION.json" -Encoding UTF8
}else{
 [ordered]@{status='PASS';candidate=$hash;scope='16 paired actions including 6 native shuttle flights; 120000 shared ticks; original game and five DLCs only'} | ConvertTo-Json | Set-Content "$run/FINAL-VERIFICATION.json" -Encoding UTF8
}
