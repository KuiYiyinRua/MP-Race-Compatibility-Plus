param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Candidate,[Parameter(Mandatory=$true)][string]$Trio,[int]$Maps=1,[int]$Cycles=0,[int]$SoakTicks=10000,[int]$Port=31171,[switch]$AsyncTime,[switch]$IncludeRaven,[string]$ClientLanguage='English',[string]$BaselineSave,[switch]$GuestFixture,[switch]$IncludeCommander)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if($run.Length -gt 70 -or (Test-Path -LiteralPath $run)){throw 'Unique short RunRoot required'}
$candidatePath=(Resolve-Path -LiteralPath $Candidate).Path
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item -LiteralPath "$game/$n" -Destination "$run/Game/$n"}}
foreach($n in @('2009463077','2934420800','Multiplayer','839005762','3624805128')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
if($IncludeCommander){New-Item -ItemType Junction -Path "$run/Game/Mods/NivarianMotherShip" -Target "$game/Mods/NivarianMotherShip" | Out-Null}
if($IncludeRaven){foreach($n in @('3595247479','3781005562')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}}
if($BaselineSave){
 foreach($folder in Get-ChildItem "$game/Mods" -Directory){
  if($folder.FullName -eq $repo -or !(Test-Path "$($folder.FullName)/About/About.xml") -or (Test-Path "$run/Game/Mods/$($folder.Name)")){continue}
  New-Item -ItemType Junction -Path "$run/Game/Mods/$($folder.Name)" -Target $folder.FullName | Out-Null
 }
}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath $candidatePath -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/Meow.TaleNivarianCompatibility.dll"
Copy-Item -LiteralPath $Trio -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/Meow.RaceTrioCompatibility.dll"
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net48/Meow.NivarianUpdateProbe.dll" -Destination "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination $run
'<ModMetaData><name>Nivarian update regression</name><author>Local</author><packageId>local.meow.nivarianupdateprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$config='<?xml version="1.0" encoding="utf-8"?><ModsConfigData><version>1.6.4850 rev646</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>erdelf.humanoidalienraces</li><li>keeptpa.nivarianrace</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.nivarianupdateprobe</li></activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>'
if($IncludeCommander){$config=$config.Replace('<li>local.mp.meowonlineshop.sellslingshot</li>','<li>local.nivarian.mothership.commander</li><li>local.mp.meowonlineshop.sellslingshot</li>')}
if($IncludeRaven){$config=$config.Replace('<li>keeptpa.nivarianrace</li>','<li>chezhou.chezhoulib.lib</li><li>zuoyao.ravenrace</li><li>keeptpa.nivarianrace</li>')}
if($BaselineSave){
 $baseline=(Resolve-Path -LiteralPath $BaselineSave).Path
 [xml]$saveHeader=Get-Content -LiteralPath $baseline -Raw
 $saveIds=@($saveHeader.savegame.meta.modIds.li | ForEach-Object {$_.ToLowerInvariant()})
 if($saveIds -notcontains 'keeptpa.nivarianrace' -or $saveIds -notcontains 'local.mp.meowonlineshop.sellslingshot'){throw 'Representative save must contain Nivarian and this compatibility package'}
 [xml]$configXml=$config
 $activeNode=$configXml.SelectSingleNode('/ModsConfigData/activeMods')
 $activeNode.RemoveAll()
 foreach($id in $saveIds+@('local.meow.nivarianupdateprobe')){$entry=$configXml.CreateElement('li');$entry.InnerText=$id;$activeNode.AppendChild($entry) | Out-Null}
 $config=$configXml.OuterXml
}
foreach($peer in @('Host')+@(0..$Cycles | ForEach-Object {"Client$_"})){
 New-Item -ItemType Directory -Force "$run/$peer/Config","$run/$peer/Saves","$run/$peer/MpReplays" | Out-Null
 if($BaselineSave){Copy-Item "$game/../Config/*.xml" -Destination "$run/$peer/Config/"}
 $config | Set-Content "$run/$peer/Config/ModsConfig.xml"
 $peerLanguage=if($peer -eq 'Host'){'English'}else{$ClientLanguage}
 ('<PrefsData><volumeMaster>0</volumeMaster><langFolderName>'+$peerLanguage+'</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>') | Set-Content "$run/$peer/Config/Prefs.xml"
}
if($BaselineSave){Copy-Item -LiteralPath $baseline -Destination "$run/Host/Saves/NivarianInput.rws";Get-FileHash -LiteralPath $baseline,"$run/Host/Saves/NivarianInput.rws" | ConvertTo-Json | Set-Content "$run/baseline-hashes.json"}
$hashes=@(Get-FileHash -LiteralPath $candidatePath,"$run/Game/Mods/Candidate/1.6/Assemblies/Meow.TaleNivarianCompatibility.dll","$run/Game/Mods/Probe/Assemblies/Meow.NivarianUpdateProbe.dll","$game/Mods/3624805128/1.6/Assemblies/Nivarian_Race.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll","$run/Game/Mods/Candidate/About/About.xml")
$hashes+=Get-ChildItem "$run/Game/Mods/Candidate/1.6/Assemblies" -Filter '*.dll' | Get-FileHash
$hashes+=Get-ChildItem "$game/Mods/3624805128" -Recurse -File | Where-Object {$_.Extension -in '.dll','.xml'} | Get-FileHash
$hashes+=Get-FileHash "$run/Game/Mods/Candidate/LoadFolders.xml","$run/Probe.cs","$run/Probe.csproj","$run/Run.ps1"

if($IncludeRaven){foreach($n in @('3595247479','3781005562')){$hashes+=Get-ChildItem "$game/Mods/$n" -Recurse -File | Where-Object {$_.Extension -in '.dll','.xml'} | Get-FileHash}}
if($IncludeCommander){foreach($folder in @('1.6','Royalty','NoRoyalty','Multiplayer/Assemblies','About')){$hashes+=Get-ChildItem "$game/Mods/NivarianMotherShip/$folder" -Recurse -File | Where-Object {$_.Extension -in '.dll','.xml'} | Get-FileHash};$hashes+=Get-FileHash "$game/Mods/NivarianMotherShip/LoadFolders.xml"}
$hashes | ConvertTo-Json | Set-Content "$run/inputs.json"
$flags=" -nivarianupdateprobe -nuport=$Port -numaps=$Maps -nucycles=$Cycles -nusoak=$SoakTicks"
if($AsyncTime){$flags+=' -nuasync'}
if($BaselineSave){$flags+=' -nurws'}
if($GuestFixture){$flags+=' -nuguestfixture'}
function StartPeer($peer,$extra){$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -screen-fullscreen 0 -screen-width 800 -screen-height 600'+$flags+' '+$extra;$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Minimized -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate,ExecutablePath | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
function ReadLog($path){if(!(Test-Path -LiteralPath $path)){return ''};return [string](Get-Content -LiteralPath $path -Raw)}
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='incomplete';$current=0
try{
 $hostExtra=if($BaselineSave){'-username=NuHost'}else{'-quicktest -username=NuHost'}
 $owned+=StartPeer Host $hostExtra;$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 3900){
  $logs=@(Get-ChildItem -LiteralPath $run -Filter '*.log' -File | ForEach-Object {ReadLog $_.FullName}) -join "`n"
  if($logs -match 'Root level exception|Exception in UIRootUpdate|FileNotFoundException: Cannot resolve dependency|NU_FAILED|REQUIRED_TARGET_FAILED|Required target failed|Error in static constructor of Meow.RaceTrio|Error in static constructor of Meow.TaleNivarian|Error in static constructor of NivarianUpdateProbe|Prepatcher Error: Fatal|ReflectionTypeLoadException|Multiplayer.API.UninitializedAPI|Desynced after|Sync Error|Inconsistent player|desynced=True|PacketReadException|Exception: SyncMethod|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception'){throw 'Runtime/setup failure; inspect peer logs'}
  if(!$clientStarted -and $logs -match '(?m)^Server started\.'){$owned+=StartPeer Client0 "-nuclient -nucycle=0 -username=NuClient -connect=127.0.0.1:$Port";$clientStarted=$true}
  if((Test-Path "$run/host.complete$current") -and (Test-Path "$run/client$current.complete")){
   if($current -eq $Cycles){$status='PASS';$reason="Native update action receipts, frozen snapshots, $Cycles cold rejoins and soak gates passed";break}
   Set-Content "$run/client$current.quit" 'cold rejoin';$previousClient=$owned[$owned.Count-1];if(!$previousClient.WaitForExit(15000)){throw 'Client did not quit gracefully'}
   $current++;Set-Content "$run/cycle.txt" $current;$owned+=StartPeer "Client$current" "-nuclient -nucycle=$current -username=NuClient -connect=127.0.0.1:$Port"
  }
  if($owned[0].HasExited){throw 'Host exited unexpectedly'}
  if($clientStarted -and $owned[$owned.Count-1].HasExited){throw 'Client exited unexpectedly'}
  Start-Sleep -Seconds 2
 }
 if($status -ne 'PASS'){throw 'Timeout'}
 foreach($hash in $hashes){if((Get-FileHash -LiteralPath $hash.Path).Hash -ne $hash.Hash){throw 'Frozen binary changed during run'}}
 $h=@((ReadLog "$run/host.log") -split "`n" | Where-Object {$_ -match '^NU_ASSERT cycle='} | ForEach-Object {$_.Trim()} | Sort-Object -Unique)
 $c=@(Get-ChildItem -LiteralPath $run -Filter 'client*.log' | ForEach-Object {(ReadLog $_.FullName) -split "`n" | Where-Object {$_ -match '^NU_ASSERT cycle='} | ForEach-Object {$_.Trim()}} | Sort-Object -Unique)
 if($h.Count -ne $Maps*48*($Cycles+1) -or ($h -join "`n") -ne ($c -join "`n")){throw 'Native action receipts mismatch'}
 $expected=([xml]$config).ModsConfigData.activeMods.li -join '|'
 foreach($path in Get-ChildItem "$run/Host/Config/ModsConfig.xml","$run/Client*/Config/ModsConfig.xml"){if((([xml](Get-Content $path.FullName -Raw)).ModsConfigData.activeMods.li -join '|') -ne $expected){throw 'Paired ordered mod configuration drift'}}
 Get-ChildItem "$run/Host/Config/ModsConfig.xml","$run/Client*/Config/ModsConfig.xml" | Get-FileHash | ConvertTo-Json | Set-Content "$run/final-config-hashes.json"
}catch{$status='FAILED';$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(15000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-nivarianupdateprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;maps=$Maps;async=[bool]$AsyncTime;cycles=$current;targetWorldTicks=$SoakTicks;candidateSHA256=(Get-FileHash -LiteralPath $candidatePath).Hash} | ConvertTo-Json | Set-Content "$run/result.json"
}
if($status -ne 'PASS'){exit 1}


