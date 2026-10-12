param([Parameter(Mandatory=$true)][string]$RunRoot,
      [Parameter(Mandatory=$true)][string]$Candidate,
      [Parameter(Mandatory=$true)][string]$Trio,
      [int]$Maps=2,[int]$SoakTicks=10000,[int]$Cycles=0,[int]$Port=31261,[switch]$AsyncTime)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if($run.Length -gt 70 -or (Test-Path -LiteralPath $run)){throw 'Unique short RunRoot required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item -LiteralPath "$game/$n" -Destination "$run/Game/$n"}}
foreach($n in @('2009463077','2934420800','Multiplayer','839005762','3624805128','NivarianMotherShip')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Candidate).Path -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/Meow.TaleNivarianCompatibility.dll"
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Trio).Path -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/Meow.RaceTrioCompatibility.dll"
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net48/Meow.PausedMapFogProbe.dll" -Destination "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination $run
'<ModMetaData><name>Paused map fog regression</name><author>Local</author><packageId>local.meow.pausedmapfogprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$config='<?xml version="1.0" encoding="utf-8"?><ModsConfigData><version>1.6.4850 rev646</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>erdelf.humanoidalienraces</li><li>keeptpa.nivarianrace</li><li>rwmt.multiplayer</li><li>local.nivarian.mothership.commander</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.pausedmapfogprobe</li></activeMods></ModsConfigData>'
foreach($peer in @('Host')+@(0..$Cycles | ForEach-Object {"Client$_"})){
 New-Item -ItemType Directory -Force "$run/$peer/Config","$run/$peer/Saves","$run/$peer/MpReplays" | Out-Null
 $config | Set-Content "$run/$peer/Config/ModsConfig.xml"
 '<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
}
$hashes=@(Get-ChildItem "$run/Game/Mods/Candidate" -Filter '*.dll' -Recurse | Get-FileHash)
$hashes+=@(Get-FileHash "$run/Game/Mods/Probe/Assemblies/Meow.PausedMapFogProbe.dll","$game/Mods/3624805128/1.6/Assemblies/Nivarian_Race.dll","$game/Mods/NivarianMotherShip/Multiplayer/Assemblies/NivarianCommander.Multiplayer.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll")
Get-ChildItem "$run/Host/Config/ModsConfig.xml","$run/Client*/Config/ModsConfig.xml","$run/Game/Mods/Candidate/About/About.xml" | Get-FileHash | ConvertTo-Json | Set-Content "$run/config-inputs.json"
$hashes | ConvertTo-Json | Set-Content "$run/inputs.json"
($hashes | Where-Object Path -Match 'RaceTrioCompatibility|TaleNivarianCompatibility' | ForEach-Object {$_.Hash}) -join ';' | Set-Content "$run/candidate-hashes.txt"
$flags=" -pausedmapfogprobe -pfport=$Port -pfmaps=$Maps -pfsoak=$SoakTicks -pfcycles=$Cycles"
if($AsyncTime){$flags+=' -pfasync'}
function StartPeer($peer,$extra){
 $line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -screen-fullscreen 0 -screen-width 800 -screen-height 600'+$flags+' '+$extra
 $p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
 Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate,ExecutablePath | ConvertTo-Json | Set-Content "$run/$peer-process.json"
 return $p
}
function ReadLog($path){if(!(Test-Path -LiteralPath $path)){return ''};return [string](Get-Content -LiteralPath $path -Raw)}
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='incomplete';$current=0
try{
 $owned+=StartPeer Host '-quicktest -username=FogHost';$joined=$false
 while($watch.Elapsed.TotalSeconds -lt 2700){
  $h=ReadLog "$run/host.log";$c=@(Get-ChildItem $run -Filter 'client*.log' | ForEach-Object {ReadLog $_.FullName}) -join "`n"
  if(($h+$c) -match 'PF_FAILED|REQUIRED_TARGET_FAILED|Required target failed|Error in static constructor of Meow.RaceTrio|Error in static constructor of Meow.TaleNivarian|Error in static constructor of PausedMapFogProbe|Prepatcher Error: Fatal|ReflectionTypeLoadException|Desynced after|Sync Error|Inconsistent player|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception'){throw 'Runtime/setup failure; inspect logs'}
  if(!$joined -and $h -match '(?m)^Server started\.'){
   if($h -notmatch 'Optional icy fog and drone chatter random scopes installed' -or $h -notmatch 'reviewed NMC random owner retained: Void OrbitTarget' -or $h -notmatch 'reviewed NMC random owner retained: Void CompTick' -or $h -notmatch 'TaleNivarianCompat.*1.1.8.0 READY'){throw 'Required startup ownership/readiness missing'}
   $owned+=StartPeer Client0 ("-pfclient -pfcycle=0 -username=FogClient -connect=127.0.0.1:"+$Port);$joined=$true
  }
  if((Test-Path "$run/host.complete$current") -and (Test-Path "$run/client$current.complete$current")){
   if((Get-Content "$run/host.final$current" -Raw) -cne (Get-Content "$run/client$current.final$current" -Raw)){throw 'Final snapshot/Rand mismatch'}
   if($current -lt $Cycles){
    Set-Content "$run/client$current.quit" 'cold rejoin';$previous=$owned[$owned.Count-1];if(!$previous.WaitForExit(15000)){throw 'Client did not quit gracefully'}
    $current++;Set-Content "$run/cycle.txt" $current;$owned+=StartPeer "Client$current" ("-pfclient -pfcycle=$current -username=FogClient -connect=127.0.0.1:"+$Port);continue
   }
   $ha=@($h -split "`n" | Where-Object {$_ -match '^PF_ASSERT '} | ForEach-Object {($_ -replace ' shared=.*','').Trim()})
   $ca=@($c -split "`n" | Where-Object {$_ -match '^PF_ASSERT '} | ForEach-Object {($_ -replace ' shared=.*','').Trim()})
   if($ha.Count -ne $Maps*9*($Cycles+1) -or ($ha -join '|') -cne ($ca -join '|')){throw 'Action receipts mismatch'}
   foreach($x in $hashes){if((Get-FileHash -LiteralPath $x.Path).Hash -ne $x.Hash){throw 'Frozen input changed'}}
   foreach($path in Get-ChildItem "$run/Host/Config/ModsConfig.xml","$run/Client*/Config/ModsConfig.xml"){
    if((([xml](Get-Content $path.FullName -Raw)).ModsConfigData.activeMods.li -join '|') -cne (([xml]$config).ModsConfigData.activeMods.li -join '|')){throw 'Loadout changed'}
   }
   $clients=Get-ChildItem $run -Filter 'client*.log' | ForEach-Object {ReadLog $_.FullName}
   foreach($cl in $clients){if($cl -notmatch 'TaleNivarianCompat.*1.1.8.0 READY' -or $cl -notmatch 'Optional icy fog and drone chatter random scopes installed' -or $cl -notmatch 'reviewed NMC random owner retained: Void OrbitTarget'){throw 'Client startup owner/readiness absent'}}
   Get-ChildItem "$run/Host/Config/ModsConfig.xml","$run/Client*/Config/ModsConfig.xml" | Get-FileHash | ConvertTo-Json | Set-Content "$run/final-config-hashes.json"
   $status='PASS';$reason="Native modal workflows on each map, physical Steel transfers, fog Rand invariant, frozen equality and $Cycles cold rejoins";break
  }
  if($owned[0].HasExited -or ($joined -and $owned[$owned.Count-1].HasExited)){throw 'Early peer exit'}
  Start-Sleep -Seconds 2
 }
 if($status -ne 'PASS'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-pausedmapfogprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;maps=$Maps;async=[bool]$AsyncTime;cycles=$current;targetSharedTicks=$SoakTicks} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content "$run/result.json"
if($status -ne 'PASS'){exit 1}
