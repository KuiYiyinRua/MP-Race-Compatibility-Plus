param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Core,[Parameter(Mandatory=$true)][string]$Turret)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Unique run required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config","$run/Client/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item "$game/$n" "$run/Game/$n"}}
foreach($n in @('2009463077','2934420800','Multiplayer')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath $Core -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll"
Copy-Item -LiteralPath $Turret -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/Meow.TurretCombatSleep.dll"
Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.HostConfigNetworkProbe.dll" "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination $run
'<ModMetaData><name>Host config network regression</name><author>Local</author><packageId>local.meow.hostconfignetwork</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$cfg='<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.hostconfignetwork</li></activeMods></ModsConfigData>'
foreach($peer in @('Host','Client')){
 $cfg | Set-Content "$run/$peer/Config/ModsConfig.xml"
 '<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
 $on=($peer -eq 'Host').ToString()
 ('<SettingsBlock><ModSettings Class="Meow.TurretCombatSleep.TurretCombatSleepSettings"><turretCombatSleepEnabled>'+$on+'</turretCombatSleepEnabled></ModSettings></SettingsBlock>') | Set-Content "$run/$peer/Config/Mod_Candidate_TurretCombatSleepMod.xml"
}
((Get-FileHash -LiteralPath $Core).Hash+':' +(Get-FileHash -LiteralPath $Turret).Hash) | Set-Content "$run/candidate.sha256"
Get-ChildItem "$run/Game/Mods/Candidate" -Recurse -File | Where-Object Extension -eq '.dll' | Get-FileHash | ConvertTo-Json | Set-Content "$run/candidate-hashes.json"
Get-FileHash "$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml","$run/Host/Config/Mod_Candidate_TurretCombatSleepMod.xml","$run/Client/Config/Mod_Candidate_TurretCombatSleepMod.xml" | ConvertTo-Json | Set-Content "$run/config-before.json"
function StartPeer($peer,$extra){$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -hostconfignetworkprobe -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$extra;$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
function ReadLog($path){if(!(Test-Path $path)){return ''};return [string](Get-Content -LiteralPath $path -Raw)}
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='not complete'
try{
 $owned+=StartPeer Host '-quicktest';$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 1200){
  $h=ReadLog "$run/host.log";$c=ReadLog "$run/client.log"
  if(($h+$c) -match 'MP config hot sync cannot safely continue|CFG_NETWORK FAILED|REQUIRED_TARGET_FAILED|Prepatcher Error: Fatal|Error in static constructor of HostConfigNetworkProbe|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception'){throw 'Runtime/setup failure; inspect logs'}
  if(!$clientStarted -and $h -match '(?m)^Server started\.'){$owned+=StartPeer Client '-cfgclient -connect=127.0.0.1:31029';$clientStarted=$true}
  if((Test-Path "$run/host.complete") -and (Test-Path "$run/client.complete")){
   if($c -notmatch 'TurretCombatSleepMod: outcome=HotApplied'){throw 'Client never hot-applied host switch'}
   $records=Get-Content "$run/candidate-hashes.json" -Raw | ConvertFrom-Json
   foreach($record in $records){if((Get-FileHash -LiteralPath $record.Path).Hash -ne $record.Hash){throw 'Candidate changed'}}
   $ht=@($h -split "`n" | Where-Object {$_ -match '^CFG_NETWORK TURRET'});$ct=@($c -split "`n" | Where-Object {$_ -match '^CFG_NETWORK TURRET'})
   if($ht.Count -ne 3 -or ($ht -join "`n") -ne ($ct -join "`n")){throw 'Turret state assertions differ'}
   Get-FileHash "$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml","$run/Host/Config/Mod_Candidate_TurretCombatSleepMod.xml","$run/Client/Config/Mod_Candidate_TurretCombatSleepMod.xml" | ConvertTo-Json | Set-Content "$run/config-after.json"
   $status='PASS';$reason='Host enabled/client disabled; real join hot sync; frozen Rand baseline equal; 3 turret sleep assertions; 10000 shared ticks';break
  }
  foreach($p in $owned){if($p.HasExited){throw 'Unexpected peer exit'}}
  Start-Sleep -Seconds 3
 }
 if($status -ne 'PASS'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-hostconfignetworkprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;scope='one-map vanilla/DLC real MP admission smoke; not full user replay/soak'} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content "$run/result.json"
if($status -ne 'PASS'){throw $reason}
