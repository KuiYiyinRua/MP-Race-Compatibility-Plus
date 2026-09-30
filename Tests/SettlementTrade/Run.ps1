param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Candidate,[switch]$Old,[switch]$Trace,[string]$AboutPath)
$ErrorActionPreference='Stop'
$PSDefaultParameterValues['Set-Content:Encoding']='UTF8'
$run=[IO.Path]::GetFullPath($RunRoot)
$game=(Resolve-Path '../..').Path
if(Test-Path -LiteralPath $run){throw 'Run already exists'}
New-Item -ItemType Directory -Force "$run/Game/Mods","$run/Host/Config","$run/Client/Config","$run/Candidate","$run/Driver" | Out-Null
Copy-Item -LiteralPath $Candidate -Destination "$run/Candidate/MP_MeowOnlineShop.dll"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination "$run/Driver"
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$name" -Target "$game/$name" | Out-Null}
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$name"){Copy-Item -LiteralPath "$game/$name" -Destination "$run/Game/$name"}}
foreach($name in @('2009463077','2934420800','Multiplayer')){New-Item -ItemType Junction -Path "$run/Game/Mods/$name" -Target "$game/Mods/$name" | Out-Null}
foreach($name in @('Candidate','Probe')){New-Item -ItemType Directory -Force "$run/Game/Mods/$name/About","$run/Game/Mods/$name/Assemblies" | Out-Null}
Copy-Item "$run/Candidate/MP_MeowOnlineShop.dll" "$run/Game/Mods/Candidate/Assemblies/"
Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.SettlementTradeProbe.dll" "$run/Game/Mods/Probe/Assemblies/"
if(!$AboutPath){$AboutPath=(Resolve-Path 'About/About.xml').Path}
Copy-Item -LiteralPath $AboutPath -Destination "$run/Game/Mods/Candidate/About/About.xml"
'<ModMetaData><name>Settlement trade diagnostic</name><author>Local</author><packageId>local.meow.settlementtradeprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$cfg='<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.settlementtradeprobe</li></activeMods></ModsConfigData>'
foreach($peer in @('Host','Client')){
 $cfg | Set-Content "$run/$peer/Config/ModsConfig.xml"
 '<PrefsData><volumeMaster>0</volumeMaster><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><volumeAmbient>0</volumeAmbient><volumeUI>0</volumeUI><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
}
(Get-FileHash "$run/Candidate/MP_MeowOnlineShop.dll").Hash | Set-Content "$run/candidate.sha256"
Get-FileHash "$run/Candidate/MP_MeowOnlineShop.dll","$run/Game/Mods/Candidate/Assemblies/MP_MeowOnlineShop.dll","$run/Game/Mods/Probe/Assemblies/Meow.SettlementTradeProbe.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll","$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml","$run/Game/Mods/Candidate/About/About.xml" | ConvertTo-Json | Set-Content "$run/inputs.json"
$owned=@();$status='FAILED';$reason='not completed';$watch=[Diagnostics.Stopwatch]::StartNew()
function ReadLog($path){if(!(Test-Path -LiteralPath $path)){return ''};$stream=$null;$reader=$null;try{$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);return $reader.ReadToEnd()}finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}}
function StartPeer($peer,$extra){$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -settlementprobe -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$extra;$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
try{
 $oldArg=if($Old){' -stold'}elseif($Trace){' -sttrace'}else{''};$owned+=StartPeer Host ('-quicktest'+$oldArg);$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 5400){
  $h=ReadLog "$run/host.log";$c=ReadLog "$run/client.log"
  if(($h+$c) -match 'SETTLEMENT FAILED|REQUIRED_TARGET_FAILURE trade auto-open|Prepatcher Error: Fatal|Error in static constructor of TradeWindowProbe|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception'){throw 'Runtime/setup failure; see logs'}
  if(!$clientStarted -and $h -match '(?m)^Server started\.'){$owned+=StartPeer Client ('-stclient -connect=127.0.0.1:30997'+$oldArg);$clientStarted=$true}
  if((Test-Path "$run/host.complete") -and (Test-Path "$run/client.complete")){
   # RimWorld rewrites formatting, full version and knownExpansions at startup.
   # Preserve the pre-launch hashes, compare the exact active loadout, and record
   # the resulting files separately rather than overwriting the input evidence.
   [xml]$expected=$cfg
   foreach($peer in @('Host','Client')){
    [xml]$actual=Get-Content "$run/$peer/Config/ModsConfig.xml" -Raw
    if(($actual.ModsConfigData.activeMods.li -join ';') -cne ($expected.ModsConfigData.activeMods.li -join ';')){throw 'Active mod loadout changed'}
   }
   if((Get-FileHash "$run/Host/Config/ModsConfig.xml").Hash -ne (Get-FileHash "$run/Client/Config/ModsConfig.xml").Hash){throw 'Peer mod configurations differ'}
   $inputRecords=Get-Content "$run/inputs.json" -Raw | ConvertFrom-Json
   foreach($x in $inputRecords){if($x.Path -notlike '*Config\ModsConfig.xml' -and (Get-FileHash -LiteralPath $x.Path).Hash -ne $x.Hash){throw ('Input changed: '+$x.Path)}}
   Get-FileHash "$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml" | ConvertTo-Json | Set-Content "$run/runtime-config-hashes.json"
   $total=if($Old){4}else{16};for($i=0;$i -lt $total;$i++){$a=Get-Content -Raw -LiteralPath ($run+'/host.assert'+$i);$b=Get-Content -Raw -LiteralPath ($run+'/client.assert'+$i);if($a -cne $b){throw ('Receipt mismatch round '+$i)}};$status='PASS';$reason='Paired native acceptance/physical ownership assertions and shared tick completion';break
  }
  foreach($p in $owned){if($p.HasExited){throw 'Early peer exit'}}
  Start-Sleep -Seconds 3
 }
 if($status -ne 'PASS'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-settlementprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds;scope='fixed quick-test vanilla/DLC world; not user-save validation'} | ConvertTo-Json | Set-Content "$run/result.json"
}
if($status -ne 'PASS'){throw $reason}
