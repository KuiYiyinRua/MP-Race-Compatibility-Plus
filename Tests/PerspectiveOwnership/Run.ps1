param(
 [Parameter(Mandatory=$true)][string]$RunRoot,
 [Parameter(Mandatory=$true)][string]$Core,
 [int]$Cycles=3,[int]$SoakTicks=120000,[int]$Port=31105,
 [switch]$TwoMaps,[switch]$AsyncTime,[switch]$Baseline,[switch]$Traces,[string]$Replay,[string]$Save
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Unique run directory required'}
$candidate=(Resolve-Path -LiteralPath $Core).Path
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config","$run/Client0/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item -LiteralPath "$game/$n" -Destination "$run/Game/$n"}}
foreach($n in @('2009463077','2934420800','Multiplayer','3232415388','3696859196','3686618980')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath $candidate -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll"
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net48/Meow.PerspectiveOwnershipProbe.dll" -Destination "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination $run
'<ModMetaData><name>Perspective ownership regression</name><author>Local</author><packageId>local.meow.perspectiveownershipprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
function PreparePeer($peer){
 New-Item -ItemType Directory -Force "$run/$peer/Config","$run/$peer/Saves","$run/$peer/MpReplays" | Out-Null
 Copy-Item -LiteralPath "$PSScriptRoot/ModsConfig.xml" -Destination "$run/$peer/Config/ModsConfig.xml"
 '<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
}
PreparePeer Host
for($i=0;$i -le $Cycles;$i++){PreparePeer "Client$i"}
$hashes=@(Get-FileHash -LiteralPath $candidate,"$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll","$game/Mods/3686618980/1.6/Assemblies/PerspectiveShift.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll","$run/Host/Config/ModsConfig.xml","$run/Client0/Config/ModsConfig.xml","$run/Game/Mods/Probe/Assemblies/Meow.PerspectiveOwnershipProbe.dll")
if($Replay){Copy-Item -LiteralPath $Replay -Destination "$run/input.zip"; $hashes+=Get-FileHash -LiteralPath "$run/input.zip"}
if($Save){Copy-Item -LiteralPath $Save -Destination "$run/Host/Saves/PerspectiveOldOwnership.rws"; Copy-Item -LiteralPath $Save -Destination "$run/input.rws"; $hashes+=Get-FileHash -LiteralPath "$run/input.rws"}
for($i=1;$i -le $Cycles;$i++){$hashes+=Get-FileHash -LiteralPath "$run/Client$i/Config/ModsConfig.xml"}
$hashes+=Get-FileHash -LiteralPath "$run/Game/Mods/Candidate/About/About.xml"
$hashes | ConvertTo-Json -Compress | Set-Content "$run/inputs.json"
$flags=' -perspectiveownershipprobe -psport='+$Port+' -pslastcycle='+$Cycles+' -pssoakticks='+$SoakTicks
if($TwoMaps){$flags+=' -pstwomaps'};if($AsyncTime){$flags+=' -psasync'};if($Baseline){$flags+=' -psbaseline'};if($Traces){$flags+=' -pstraces'};if($Replay){$flags+=' -psreplay'};if($Save){$flags+=' -pssave'}
function StartPeer($peer,$extra){
 $line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -screen-fullscreen 0 -screen-width 800 -screen-height 600'+$flags+' '+$extra
 $p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
 Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate,ExecutablePath | ConvertTo-Json | Set-Content "$run/$peer-process.json"
 return $p
}
function ReadLog($path){if(!(Test-Path -LiteralPath $path)){return ''};return [string](Get-Content -LiteralPath $path -Raw)}
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='incomplete';$current=0
try{
 $hostExtra=if($Replay -or $Save){'-username=PsHost'}else{'-quicktest -username=PsHost'}
 $owned+=StartPeer Host $hostExtra;$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 2250){
  $logs=@(Get-ChildItem -LiteralPath $run -Filter '*.log' -File | ForEach-Object {ReadLog $_.FullName}) -join "`n"
  if($logs -match 'PS_OWN FAILED|REQUIRED_TARGET_FAILED|Prepatcher Error: Fatal|Error in static constructor of PerspectiveOwnershipProbe|Desynced after|Sync Error|Inconsistent player|desynced=True|PacketReadException|Exception: SyncMethod|Perspective Shift .*failed|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception'){throw 'Runtime/setup failure; inspect peer logs'}
  if($Baseline -and (Test-Path "$run/baseline.reproduced")){$status='BASELINE_REPRODUCED';$reason='Old owner registry survives opening a new room';break}
  if(!$Baseline -and !$clientStarted -and $logs -match '(?m)^Server started\.'){
   $owned+=StartPeer Client0 "-psclient -pscycle=0 -username=PsClient -connect=$('127.0.0.1:'+$Port)";$clientStarted=$true
  }
  if(!$Baseline -and (Test-Path "$run/host.complete$current") -and (Test-Path "$run/client$current.complete")){
   if($current -eq $Cycles){$status='PASS';$reason="New-room cleanup; native claim/release; snapshot preservation; $Cycles cold rejoins; $SoakTicks final shared ticks";break}
   Set-Content "$run/client$current.quit" 'cold rejoin'
   $old=$owned[$owned.Count-1]
   if(!$old.WaitForExit(15000)){throw 'Client did not quit gracefully'}
   $current++;Set-Content "$run/cycle.txt" $current
   $owned+=StartPeer "Client$current" "-psclient -pscycle=$current -username=PsClient -connect=$('127.0.0.1:'+$Port)"
  }
  foreach($p in $owned){if($p.HasExited -and $p.Id -eq $owned[0].Id){throw 'Host exited unexpectedly'}}
  if($clientStarted -and $owned[$owned.Count-1].HasExited){throw 'Client exited unexpectedly'}
  Start-Sleep -Seconds 2
 }
 if($status -eq 'FAILED'){throw 'Timeout'}
 foreach($hash in $hashes){if((Get-FileHash -LiteralPath $hash.Path).Hash -ne $hash.Hash){throw 'Input binary/config changed during run'}}
 if($status -eq 'PASS'){
  $hostAssertions=@((ReadLog "$run/host.log") -split "`n" | Where-Object {$_ -match '^PS_OWN ASSERT'} | ForEach-Object {$_.Trim()})
  $clientAssertions=@(Get-ChildItem -LiteralPath $run -Filter 'client*.log' | Sort-Object Name | ForEach-Object {(ReadLog $_.FullName) -split "`n" | Where-Object {$_ -match '^PS_OWN ASSERT'} | ForEach-Object {$_.Trim()}})
  # Cold clients replay prior commands only up to each fresh join point; compare
  # the current cycle assertions, excluding any replayed historical records.
  $clientAssertions=@($clientAssertions | Sort-Object -Unique);$uniqueHost=@($hostAssertions | Sort-Object -Unique)
  $expectedSteps=if($TwoMaps){13}else{7}
  if($uniqueHost.Count -ne $expectedSteps*($Cycles+1) -or ($uniqueHost -join "`n") -ne ($clientAssertions -join "`n")){throw 'Host/client action receipts mismatch'}
  if($TwoMaps){for($i=0;$i -le $Cycles;$i++){if(!(Test-Path -LiteralPath "$run/client$i.offmap.complete")){throw 'Missing cross-map movement acknowledgment'}}}
 }
}catch{$status='FAILED';$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-perspectiveownershipprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;maps=1+[int][bool]$TwoMaps;async=[bool]$AsyncTime;cycles=$current;targetTicks=$SoakTicks;candidateSHA256=(Get-FileHash -LiteralPath $candidate).Hash} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content -LiteralPath "$run/result.json"
if($status -eq 'FAILED'){throw $reason}
