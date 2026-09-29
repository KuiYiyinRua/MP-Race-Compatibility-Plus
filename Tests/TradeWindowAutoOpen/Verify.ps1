param([Parameter(Mandatory=$true)][string]$RunRoot)
$ErrorActionPreference='Stop'
$run=(Resolve-Path -LiteralPath $RunRoot).Path
function Require($condition,$why){if(!$condition){throw $why}}
# R1-R5 were written by Windows PowerShell 5 in the machine ANSI code page.
$bytes=[IO.File]::ReadAllBytes("$run/inputs.json")
$raw=if($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191){[Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)}else{[Text.Encoding]::GetEncoding(936).GetString($bytes)}
$inputs=$raw | ConvertFrom-Json
$script=Get-Content "$run/Driver/Run.ps1" -Raw
$match=[regex]::Match($script,'\$cfg=''([^'']+)''')
Require $match.Success 'Original configuration template missing'
[xml]$expected=$match.Groups[1].Value
$expectedIds=@($expected.ModsConfigData.activeMods.li)
$hashes=@()
foreach($peer in @('Host','Client')){
 [xml]$actual=Get-Content "$run/$peer/Config/ModsConfig.xml" -Raw
 Require (($actual.ModsConfigData.activeMods.li -join ';') -ceq ($expectedIds -join ';')) 'Active mod IDs/order changed'
 Require (([string]$actual.ModsConfigData.version).StartsWith([string]$expected.ModsConfigData.version)) 'Game version changed'
 Require (($actual.ModsConfigData.knownExpansions.li -join ';') -ceq (($expectedIds | Where-Object {$_ -like 'ludeon.*' -and $_ -ne 'ludeon.rimworld'}) -join ';')) 'Unexpected expansion configuration'
 Require (@($actual.ModsConfigData.ChildNodes | Where-Object {$_.Name -notin @('version','activeMods','knownExpansions')}).Count -eq 0) 'Unexpected configuration fields'
 $hashes+=Get-FileHash "$run/$peer/Config/ModsConfig.xml"
 Require (Test-Path "$run/$($peer.ToLower()).complete") 'Shared-tick completion missing'
 $log=Get-Content "$run/$($peer.ToLower()).log" -Raw
 Require ($log -match 'TRADE_WINDOW COMPLETE tick=\d+ desynced=False') 'Clean terminal assertion missing'
 Require ($log -notmatch 'TRADE_WINDOW FAILED|World cmd exception|Map cmd exception|Desynced after|Sync Error|PacketReadException|Inconsistent player|desynced=True|REQUIRED_TARGET_FAILURE trade auto-open') 'Runtime failure marker'
 Require ([regex]::Matches($log,'TRADE_WINDOW AUTO_ASSERT round=').Count -eq 10) 'Automatic window assertion count'
 Require ([regex]::Matches($log,'TRADE_WINDOW MANUAL_ASSERT round=').Count -eq 10) 'Manual reopen assertion count'
 Require ($log -match 'Trade auto-open routing ready: settlement, arrival and caravan encounter') 'Patch registration missing'
 Require ($log -match [regex]::Escape((Get-Content "$run/candidate.sha256" -Raw).Trim())) 'Loaded candidate provenance missing'
}
Require ($hashes[0].Hash -eq $hashes[1].Hash) 'Peer configurations differ'
foreach($entry in $inputs){
 if($entry.Path -like '*Config\ModsConfig.xml'){continue}
 Require ((Get-FileHash -LiteralPath $entry.Path).Hash -eq $entry.Hash) ('Binary/About input changed: '+$entry.Path)
}
for($i=0;$i -lt 10;$i++){
 Require ((Get-Content "$run/host.observed$i" -Raw) -ceq (Get-Content "$run/client.observed$i" -Raw)) "Shared trade state differs in round $i"
 Require ((Get-Content "$run/host.manual$i" -Raw).Trim() -eq 'PASS') 'Host manual marker'
 Require ((Get-Content "$run/client.manual$i" -Raw).Trim() -eq 'PASS') 'Client manual marker'
}
$hashes | ConvertTo-Json | Set-Content "$run/runtime-config-hashes-verified.json" -Encoding UTF8
[ordered]@{status='PASS';scope='Official DLC minimal loadout; 10 paired scenarios; 10000 shared ticks';configNormalization='Only formatting, full version suffix and knownExpansions were added by RimWorld. Original input hashes preserved; active mod IDs/order unchanged; resulting peer configuration hashes equal.';candidate=(Get-FileHash "$run/Candidate/MP_MeowOnlineShop.dll").Hash} | ConvertTo-Json | Set-Content "$run/FINAL-VERIFICATION.json" -Encoding UTF8
Write-Output 'PASS: paired window/state assertions, 10000 shared ticks, binary integrity and exact runtime loadout verified'
