param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Core,[switch]$Soak)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$data=(Resolve-Path "$game/..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
$candidate=(Resolve-Path $Core).Path
$replay=(Resolve-Path "$repo/BuildValidation/DesyncEvidence/Desync310-324_20260930/RepresentativeInput/Player6554-game.zip").Path
if(Test-Path -LiteralPath $run){throw 'Unique run required'}
$mod="$run/Game/Mods/MP-meow-online-shop"
New-Item -ItemType Directory -Force $mod,"$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config","$run/Client/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item "$game/$n" "$run/Game/$n"}}
foreach($d in Get-ChildItem "$game/Mods" -Directory){if($d.FullName -eq $repo){continue};New-Item -ItemType Junction -Path "$run/Game/Mods/$($d.Name)" -Target $d.FullName | Out-Null}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination $mod -Recurse}
Copy-Item -LiteralPath $candidate -Destination "$mod/1.6/Assemblies/MP_MeowOnlineShop.dll"
Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.HostConfigNetworkProbe.dll" "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination $run
Copy-Item -LiteralPath $replay -Destination "$run/Input.zip"
Get-FileHash -LiteralPath $replay,"$run/Input.zip" | ConvertTo-Json | Set-Content "$run/input-hashes.json"
'<ModMetaData><name>Representative desync regression probe</name><author>Local</author><packageId>local.meow.desync310probe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
[xml]$info=Get-Content "$repo/BuildValidation/DesyncEvidence/Desync310-324_20260930/RepresentativeInput/info.xml" -Raw
$ids=@($info.ReplayInfo.modIds.li)+@('local.meow.desync310probe')
$cfg='<ModsConfigData><version>1.6.4850</version><activeMods>'+($ids | ForEach-Object {'<li>'+$_+'</li>'})+' </activeMods></ModsConfigData>'
foreach($peer in @('Host','Client')){
 Copy-Item "$data/Config/*.xml" "$run/$peer/Config/"
 $cfg | Set-Content "$run/$peer/Config/ModsConfig.xml"
 foreach($name in @('HugsLib','OgreStack','MissileGirl')){if(Test-Path "$data/$name"){Copy-Item "$data/$name" "$run/$peer/" -Recurse}}
 [xml]$prefs=Get-Content "$run/$peer/Config/Prefs.xml" -Raw
 foreach($pair in @(@('volumeMaster','0'),@('volumeGame','0'),@('volumeMusic','0'),@('runInBackground','True'),@('devMode','False'),@('langFolderName','English'))){$node=$prefs.PrefsData.SelectSingleNode($pair[0]);if(!$node){$node=$prefs.CreateElement($pair[0]);[void]$prefs.PrefsData.AppendChild($node)};$node.InnerText=$pair[1]}
 $prefs.Save("$run/$peer/Config/Prefs.xml")
}
(Get-FileHash -LiteralPath $candidate).Hash | Set-Content "$run/candidate.sha256"
Get-ChildItem $mod -Recurse -File | Where-Object Extension -eq '.dll' | Get-FileHash | ConvertTo-Json | Set-Content "$run/candidate-hashes.json"
Get-FileHash "$run/Host/Config/*.xml","$run/Client/Config/*.xml" | ConvertTo-Json | Set-Content "$run/config-before.json"
function StartPeer($peer,$extra){$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -desync310probe -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$extra;if($Soak){$line+=' -probefullsoak'};$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
function ReadLog($path){if(!(Test-Path $path)){return ''};return [string](Get-Content -LiteralPath $path -Raw)}
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='not complete'
try{
 $owned+=StartPeer Host ''; $clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 6000){
  $h=ReadLog "$run/host.log";$c=ReadLog "$run/client.log"
  if(($h+$c) -match 'MP config hot sync cannot safely continue|REPLAY325 FAILED|REQUIRED_TARGET_FAILED|Prepatcher Error: Fatal|Error in static constructor of Desync310Probe|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception|Tried to register (the )?same load\s*ID twice'){throw 'Runtime or snapshot/setup failure; inspect logs'}
  if(!$clientStarted -and $h -match '(?m)^Server started\.'){$owned+=StartPeer Client '-probeclient -connect=127.0.0.1:31036';$clientStarted=$true}
  if((Test-Path "$run/host.complete") -and (Test-Path "$run/client.complete")){
   foreach($log in @($h,$c)){if($log -notmatch 'Desync325.*READY Raven'){throw 'Missing new Raven snapshot target'}}
   foreach($r in (Get-Content "$run/candidate-hashes.json" -Raw | ConvertFrom-Json)){if((Get-FileHash -LiteralPath $r.Path).Hash -ne $r.Hash){throw 'Candidate changed'}}
   $status='PASS';$reason='Representative replay; exact frozen Rand baseline; unpause at one-times speed; shared health target reached';break
  }
  foreach($p in $owned){if($p.HasExited){throw 'Unexpected peer exit'}}
  Start-Sleep -Seconds 3
 }
 if($status -ne 'PASS'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-desync310probe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;scope=$(if($Soak){'representative replay Gate D; traces OFF; >=120000 shared ticks'}else{'representative replay Gate B; traces ON; >=10000 shared ticks'});activeCount=$ids.Count} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content "$run/result.json"
if($status -ne 'PASS'){throw $reason}
