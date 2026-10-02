param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Core,[switch]$Control,[switch]$Soak,[switch]$Beacon,[switch]$Diagnostic)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
$probeBinary=Join-Path $PSScriptRoot 'bin/Release/net48/Meow.SmeltedTraceProbe.dll'
if(!(Test-Path -LiteralPath $probeBinary) -or (Get-Item -LiteralPath $probeBinary).LastWriteTimeUtc -lt (Get-Item -LiteralPath "$PSScriptRoot/Probe.cs").LastWriteTimeUtc){throw 'Probe DLL missing or stale; build successfully before launch'}
if(Test-Path -LiteralPath $run){throw 'Unique run required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item "$game/$n" "$run/Game/$n"}}
foreach($n in @('2009463077','2934420800','Multiplayer','3578170180')+$(if($Beacon){@('839005762','2988801276','3256974620','3496629836','3626456960')}else{@()})){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath $Core -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll"
[xml]$about=Get-Content "$run/Game/Mods/Candidate/About/About.xml" -Raw
$about.ModMetaData.modVersion=[Reflection.AssemblyName]::GetAssemblyName((Resolve-Path $Core).Path).Version.ToString(3)
$about.ModMetaData.SelectSingleNode('description').InnerText='Isolated Smelted Loong desync regression candidate; see this run evidence.'
$about.Save("$run/Game/Mods/Candidate/About/About.xml")
$probe=(Resolve-Path "$PSScriptRoot/bin/Release/net48/Meow.SmeltedTraceProbe.dll").Path
Copy-Item -LiteralPath $probe -Destination "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination $run
'<ModMetaData><name>Smelted Trace regression</name><author>Local</author><packageId>local.meow.smeltedtraceprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$cfg='<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>ny.smeltedloong</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.smeltedtraceprobe</li></activeMods></ModsConfigData>'
if($Beacon){$cfg=$cfg.Replace('<li>ny.smeltedloong</li>','<li>erdelf.humanoidalienraces</li><li>ancot.ancotlibrary</li><li>ancot.milirarace</li><li>ancot.milianmodification</li><li>sleepycot.wingsofdemocracy</li><li>ny.smeltedloong</li>')}
function ConfigPeer($peer){New-Item -ItemType Directory -Force "$run/$peer/Config" | Out-Null; $cfg | Set-Content "$run/$peer/Config/ModsConfig.xml"; '<PrefsData><volumeMaster>0</volumeMaster><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"}
ConfigPeer Host
(Get-FileHash -LiteralPath $Core).Hash | Set-Content "$run/candidate.sha256"
Get-ChildItem "$run/Game/Mods/Candidate" -Recurse -File | Where-Object Extension -eq '.dll' | Get-FileHash | ConvertTo-Json | Set-Content "$run/candidate-hashes.json"
Get-FileHash -LiteralPath "$game/RimWorldWin64_Data/Managed/Assembly-CSharp.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll","$game/Mods/3578170180/1.6/Assemblies/SmeltedLoong.dll",$Core,$probe | ConvertTo-Json | Set-Content "$run/input-hashes.json"
function StartPeer($peer,$cycle,$extra){
 $line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -smeltedtraceprobe -slcycle='+$cycle+' -noaudio -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$extra
 if($Soak){$line+=' -slsoak'};if($Diagnostic){$line+=' -sldiag'};if($Beacon){$line+=' -milianbeacon'}
 $p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
 Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json"
 return $p
}
function ReadLog($path){if(!(Test-Path $path)){return ''};return [string](Get-Content -LiteralPath $path -Raw)}
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='not complete';$cycle=0;$stage='host';$clientProcess=$null
try{
 $hostProcess=StartPeer Host 0 '-quicktest';$owned+=$hostProcess
 while($watch.Elapsed.TotalSeconds -lt 7200){
  $h=ReadLog "$run/host.log"; $c=ReadLog "$run/client$cycle.log"; $logs=$h+$c
  if($Control -and $logs -match 'DRAW_CONSUMED_SHARED_RAND|GENERATION_USED_VIEW_MAP'){$status='EXPECTED_FAIL';$reason=$(if($logs -match 'GENERATION_USED_VIEW_MAP'){'Old generation reads the local viewing map instead of the shared event map after equal frozen baseline'}else{'Old native DrawAt consumes shared Rand after equal frozen baseline'});break}
  if($Diagnostic -and $logs -match 'Desynced after'){ $deadline=[DateTime]::UtcNow.AddSeconds(30);do{if(Get-ChildItem "$run/Client$cycle/MpDesyncs" -Filter '*.zip' -ErrorAction SilentlyContinue){break};Start-Sleep -Milliseconds 500}while([DateTime]::UtcNow -lt $deadline);throw 'SIMULATION_DESYNC: diagnostic bundle preserved or bounded capture deadline reached'}
  if($logs -match 'SL FAILED|REQUIRED_TARGET_FAILED|Prepatcher Error: Fatal|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Could not resolve reference to object|Tried to register (the )?same load ID twice|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception|Error in static constructor of SmeltedTraceProbe'){throw 'Runtime/setup failure; inspect logs'}
  if($stage -eq 'host' -and $h -match '(?m)^Server started\.'){
   ConfigPeer Client0;$clientProcess=StartPeer Client0 0 '-slclient -connect=127.0.0.1:31039';$owned+=$clientProcess;$stage='pair'
  }
  if($stage -eq 'pair' -and (Test-Path "$run/host.complete$cycle") -and (Test-Path "$run/client$cycle.complete$cycle")){
   if($h -notmatch "SL BASELINE_EQUAL cycle=$cycle" -or $c -notmatch "SL CLIENT_LOADED_FROZEN cycle=$cycle"){throw 'Missing frozen baseline'}
   foreach($side in @($h,$c)){if(!$Control -and $side -notmatch 'Desync335.*READY'){throw 'Required projectile patch not loaded'}}
   if(!$Soak -or $cycle -eq 3){$status='PASS';$reason='Two maps, actual native weapon/mech impacts, asymmetric native draw, shared-Rand invariant; '+($cycle+1)+' equal frozen baselines; targeted smoke and requested soak';break}
   Set-Content "$run/client$cycle.quit" 'cold rejoin'
   if(!$clientProcess.WaitForExit(15000)){throw 'Client graceful exit timeout'}
   $cycle++;Set-Content "$run/cycle.txt" $cycle;$stage='advance'
  }
  if($stage -eq 'advance' -and (Test-Path "$run/host.frozen$cycle")){
   ConfigPeer "Client$cycle";$clientProcess=StartPeer "Client$cycle" $cycle '-slclient -connect=127.0.0.1:31039';$owned+=$clientProcess;$stage='pair'
  }
  if($hostProcess.HasExited){throw 'Unexpected host exit'}
  if($stage -eq 'pair' -and $clientProcess.HasExited){throw 'Unexpected client exit'}
  Start-Sleep -Seconds 2
 }
 if($status -eq 'FAILED'){throw 'Timeout'}
 $records=Get-Content "$run/candidate-hashes.json" -Raw | ConvertFrom-Json
 foreach($record in $records){if((Get-FileHash -LiteralPath $record.Path).Hash -ne $record.Hash){throw 'Candidate changed during run'}}
 if($status -eq 'PASS'){
  $all=@();foreach($i in 0..$cycle){$all+=ReadLog "$run/client$i.log"}
  $ha=@($h -split "`n" | Where-Object {$_ -match '^SL ASSERT|^SL FUNCTIONAL|^SL STAGE|^MB PAWN|^MB GENERATED|^MB DEPLOY'});$ca=@(($all -join "`n") -split "`n" | Where-Object {$_ -match '^SL ASSERT|^SL FUNCTIONAL|^SL STAGE|^MB PAWN|^MB GENERATED|^MB DEPLOY'})
  if($Beacon -and (@($h -split "`n" | Where-Object {$_ -match '^MB GENERATED'})).Count -ne (($cycle+1)*6)){throw 'Beacon reinforcement stages incomplete'}
  if($ha.Count -lt (($cycle+1)*7) -or ($ha -join "`n") -ne ($ca -join "`n")){throw 'Native functional/offset receipts differ'}
  foreach($i in 0..$cycle){if((Get-FileHash "$run/Host/Config/ModsConfig.xml").Hash -ne (Get-FileHash "$run/Client$i/Config/ModsConfig.xml").Hash){throw 'Loadout hashes differ'}}
 }
}catch{$status='FAILED';$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-smeltedtraceprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;cycle=$cycle;scope=$(if($Beacon){'exact native Milira + MilianModification + Wings + SmeltedLoong; two maps; no full user replay claim'}else{'clean SmeltedLoong exact-loadout control; no full user replay claim'})} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content "$run/result.json"
if($status -eq 'FAILED'){throw $reason}
