param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Core,[string]$Replay,[int]$Maps=2,[int]$SoakTicks=10000,[int]$Port=31109,[int]$Cycles=0,[switch]$AsyncTime,[switch]$Stale,[switch]$Inspect,[switch]$Blocked,[switch]$MainThreadLoad,[switch]$Existing)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$data=(Resolve-Path "$game/..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Unique run required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config","$run/Client/Config","$run/Driver" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item -LiteralPath "$game/$n" -Destination "$run/Game/$n"}}
$folders=@('2009463077','2934420800','Multiplayer','839005762','2988801276','3256974620','3665997350','3588393755')
if($Replay){$folders=@(Get-ChildItem "$game/Mods" -Directory | Where-Object Name -ne 'MP-meow-online-shop' | Select-Object -ExpandProperty Name)}
foreach($n in $folders){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath $Core -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll"
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net48/Meow.MiliraShuttleProbe.dll" -Destination "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination "$run/Driver"
'<ModMetaData><name>Milira shuttle diagnostic</name><author>Local</author><packageId>local.meow.milirashuttleprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$ids=@('brrainz.harmony','zetrith.prepatcher','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','erdelf.humanoidalienraces','ancot.ancotlibrary','ancot.milirarace','ariandel.ariandellibrary','ariandel.miliraimperium','rwmt.multiplayer','local.mp.meowonlineshop.sellslingshot')
if($Replay){
 Copy-Item -LiteralPath $Replay -Destination "$run/input.zip"
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip=[IO.Compression.ZipFile]::OpenRead("$run/input.zip")
 try{$reader=[IO.StreamReader]::new($zip.GetEntry('info').Open());[xml]$info=$reader.ReadToEnd();$reader.Dispose()}finally{$zip.Dispose()}
 $ids=@($info.ReplayInfo.modIds.li)
}
$ids+=@('local.meow.milirashuttleprobe')
$cfg='<ModsConfigData><version>1.6.4850 rev646</version><activeMods>'+($ids | ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods><knownExpansions><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li></knownExpansions></ModsConfigData>'
foreach($peer in @('Host','Client')){
 if($Replay){Copy-Item "$data/Config/*.xml" "$run/$peer/Config/"}
 $cfg | Set-Content "$run/$peer/Config/ModsConfig.xml"
 '<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
}
for($i=0;$i -le $Cycles;$i++){
 New-Item -ItemType Directory -Force "$run/Client$i/Config" | Out-Null
 Copy-Item "$run/Client/Config/*.xml" "$run/Client$i/Config/"
}
$hashes=@(Get-ChildItem "$run/Game/Mods/Candidate","$run/Game/Mods/Probe" -File -Recurse | Where-Object Extension -eq '.dll' | Get-FileHash)
$hashes+=Get-FileHash "$game/RimWorldWin64_Data/Managed/Assembly-CSharp.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll","$game/Mods/2988801276/1.6/Assemblies/AncotLibrary.dll","$game/Mods/3256974620/1.6/Assemblies/Milira.dll"
if($Replay){$hashes+=Get-FileHash "$run/input.zip"}
$hashes | ConvertTo-Json | Set-Content "$run/inputs.json"
function StartPeer($peer,$extra){
 if($peer -ne 'Host'){throw 'Only the single host is authorized'}
 $line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -milirashuttleprobe -screen-fullscreen 0 -screen-width 800 -screen-height 600 -msport='+$Port+' -msmaps='+$Maps+' -mssoak='+$SoakTicks+' -mscycles='+$Cycles+' '+$extra
 if($Replay){$line+=' -msreplay'};if($AsyncTime){$line+=' -msasync'};if($Stale){$line+=' -msstale'}
 if($Inspect){$line+=' -msinspect'}
 if($Blocked){$line+=' -msblocked'}
 if($MainThreadLoad){$line+=' -msmainthreadload'}
 if($Existing){$line+=' -msexisting'}
 $p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
 Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,ExecutablePath,CreationDate,CommandLine | ConvertTo-Json | Set-Content "$run/$peer-process.json"
 return $p
}
function ReadLog($p){if(Test-Path -LiteralPath $p){return [string](Get-Content -LiteralPath $p -Raw)};return ''}
$owned=@();$status='FAILED';$reason='not complete';$watch=[Diagnostics.Stopwatch]::StartNew()
$current=0
try{
 if($Cycles -ne 0){throw 'Single-host runner does not perform client rejoins'}
 $owned+=StartPeer Host $(if($Replay){''}else{'-quicktest'})
 while($watch.Elapsed.TotalSeconds -lt 3600){
  $h=ReadLog "$run/host.log"
  if(Test-Path "$run/host.failed"){throw (ReadLog "$run/host.failed")}
  if($h -match 'MS_PROBE FAILED|REQUIRED_TARGET_FAILED|Prepatcher Error: Fatal|Exception in transport pods arrival|Desynced after|Sync Error|Inconsistent player|PacketReadException|Map cmd exception|World cmd exception'){throw 'Game/action/sync failure: inspect logs'}
  if($Inspect -and (Test-Path "$run/host.inspected")){$status='OBSERVED';$reason='Single-host diagnostic';break}
  if(Test-Path "$run/host.complete0"){
   foreach($v in $hashes){if((Get-FileHash -LiteralPath $v.Path).Hash -ne $v.Hash){throw 'Frozen input changed'}}
   $ha=@($h -split "`n" | Where-Object {$_ -match '^MS_ASSERT'} | ForEach-Object {$_.Trim()})
   if($ha.Count -ne $(if($Existing){7}else{37})){throw 'Missing native action receipts'}
   $status='PASS';$reason=$(if($Existing){'Latest frozen replay, original map 2 Wyvern/Harrier round trips, passenger unload, soak, join-point save'}else{'Single hosted Multiplayer: 12 native round trips, passenger unload, soak, join-point save'});break
  }
  foreach($p in $owned){if($p.HasExited){throw 'Unexpected process exit'}}
  Start-Sleep -Seconds 2
 }
 if($status -eq 'FAILED'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-milirashuttleprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;maps=$Maps;async=[bool]$AsyncTime;stale=[bool]$Stale;blocked=[bool]$Blocked;mainThreadLoad=[bool]$MainThreadLoad;existing=[bool]$Existing;hostOnly=$true;coldRejoins=$current;soakWorldTicks=$SoakTicks;candidate=(Get-FileHash -LiteralPath $Core).Hash} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content "$run/result.json"
if($status -eq 'FAILED'){throw $reason}





