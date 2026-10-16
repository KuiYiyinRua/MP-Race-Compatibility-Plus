param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Core,[switch]$Baseline,[switch]$LegacyPower,[switch]$Rejoin,[int]$Port=31183,[switch]$TwoMaps,[switch]$AsyncTime,[switch]$IncludeCommander,[int]$Ticks=10100)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Unique run required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config","$run/Client/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){Copy-Item -LiteralPath "$game/$n" -Destination "$run/Game/$n"}
foreach($n in @('2009463077','2934420800','Multiplayer')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
if($IncludeCommander){
 foreach($n in @('839005762','3624805128')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
 New-Item -ItemType Directory -Path "$run/Game/Mods/NivarianMotherShip" | Out-Null
 foreach($n in @('About','1.6','Multiplayer','Royalty','NoRoyalty','LoadFolders.xml')){Copy-Item -LiteralPath "$game/Mods/NivarianMotherShip/$n" -Destination "$run/Game/Mods/NivarianMotherShip/" -Recurse}
}
foreach($n in @('About','1.6','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath $Core -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll"
$coreAbout=Join-Path (Split-Path (Resolve-Path -LiteralPath $Core).Path) 'About.xml'
if(Test-Path -LiteralPath $coreAbout){Copy-Item -LiteralPath $coreAbout -Destination "$run/Game/Mods/Candidate/About/About.xml"}
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net48/Meow.MoteLifecycleProbe.dll" -Destination "$run/Game/Mods/Probe/Assemblies/"
'<ModMetaData><name>Mote lifecycle regression</name><author>Local</author><packageId>local.meow.moteprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$cfg='<ModsConfigData><version>1.6.4850 rev646</version><activeMods><li>zetrith.prepatcher</li><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.moteprobe</li></activeMods></ModsConfigData>'
foreach($p in @('Host','Client')){Copy-Item -LiteralPath "$PSScriptRoot/ModsConfig.xml" -Destination "$run/$p/Config/ModsConfig.xml";'<PrefsData><volumeMaster>0</volumeMaster><runInBackground>True</runInBackground><devMode>False</devMode><langFolderName>English</langFolderName></PrefsData>' | Set-Content "$run/$p/Config/Prefs.xml"}
if($IncludeCommander){foreach($p in @('Host','Client')){
 $path="$run/$p/Config/ModsConfig.xml"; $xml=Get-Content -LiteralPath $path -Raw
 $xml=$xml.Replace('<li>rwmt.multiplayer</li>','<li>erdelf.humanoidalienraces</li><li>keeptpa.nivarianrace</li><li>rwmt.multiplayer</li><li>local.nivarian.mothership.commander</li>')
 Set-Content -LiteralPath $path -Value $xml -Encoding UTF8
}}
$hashes=@(Get-FileHash -LiteralPath $Core,"$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll","$run/Game/Mods/Probe/Assemblies/Meow.MoteLifecycleProbe.dll","$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll")
$hashes+=@(Get-ChildItem -LiteralPath "$run/Game/Mods/Candidate/1.6/Assemblies" -Filter '*.dll' | Get-FileHash)
$hashes+=@(Get-FileHash -LiteralPath "$run/Game/Mods/Candidate/About/About.xml")
if($IncludeCommander){$hashes+=@(Get-FileHash -LiteralPath "$run/Game/Mods/NivarianMotherShip/1.6/Assemblies/NivarianCommander.dll","$run/Game/Mods/NivarianMotherShip/Multiplayer/Assemblies/NivarianCommander.Multiplayer.dll")}
$hashes | ConvertTo-Json | Set-Content "$run/inputs.json" -Encoding UTF8
(Get-FileHash -LiteralPath $Core).Hash | Set-Content "$run/candidate.sha256"
$flags=' -powerport='+$Port+' -moteprobe -moteminimal -powerprobe -motesoakticks='+$Ticks
if($Rejoin){$flags+=' -powerrejoin'}
if($LegacyPower){$flags+=' -powerlegacy'}
if($Baseline){$flags+=' -motebaseline'};if($TwoMaps){$flags+=' -motetwomaps'};if($AsyncTime){$flags+=' -moteasync'}
if(!$IncludeCommander){throw 'Field overlay tests require -IncludeCommander'}
Copy-Item -LiteralPath "$PSScriptRoot/Probe.cs","$PSScriptRoot/Run.ps1","$PSScriptRoot/Probe.csproj" -Destination $run
Copy-Item -LiteralPath "$PSScriptRoot/FieldAddon.cs" -Destination $run
function StartPeer($peer,$extra){$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -screen-fullscreen 0 -screen-width 800 -screen-height 600'+$flags+' '+$extra;$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
$owned=@();$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='timeout'
try{
 $owned+=StartPeer Host '-quicktest -username=MoteHost';$joined=$false;$rejoined=0;$retired=@()
 while($watch.Elapsed.TotalSeconds -lt 1800){
  foreach($peer in @('host','client')){if(Test-Path "$run/$peer.failed"){throw (Get-Content "$run/$peer.failed" -Raw)}}
  $h=if(Test-Path "$run/host.log"){Get-Content "$run/host.log" -Raw}else{''};$c=if(Test-Path "$run/client.log"){Get-Content "$run/client.log" -Raw}else{''}
  if(($h+$c) -match 'Reached max messages limit|Prepatcher Error: Fatal|Error in static constructor of Meow.RaceTrio|Error in static constructor of Meow.TaleNivarian|Error in static constructor of NivarianCommander|MOTE_PROBE FAILED|Desynced after|Sync Error|Inconsistent player|PacketReadException|Could not resolve cross-reference|Exception ticking|Map cmd exception|World cmd exception'){throw 'Runtime failure or truncated log'}
  if(!$joined -and $h -match '(?m)^Server started\.'){$owned+=StartPeer Client ('-moteclient -username=MoteClient -connect=127.0.0.1:'+$Port);$joined=$true}
  if($Rejoin -and $rejoined -lt 3 -and (Test-Path "$run/host.rejoin$($rejoined+1)")){
   $rejoined++; $oldClient=$owned[-1]; Set-Content "$run/client.quit" 'cold rejoin'
   if(!$oldClient.WaitForExit(30000)){throw 'Cold client did not exit gracefully'}
   $retired+=$oldClient.Id
   $archive=Join-Path $run "Cold$rejoined";New-Item -ItemType Directory $archive | Out-Null
   foreach($name in @('client.log','client.ready','client.final','client.complete','Client-process.json')){
    $source=[IO.Path]::GetFullPath((Join-Path $run $name));$dest=[IO.Path]::GetFullPath((Join-Path $archive $name))
    if(!$source.StartsWith($run+[IO.Path]::DirectorySeparatorChar)-or !$dest.StartsWith($run+[IO.Path]::DirectorySeparatorChar)){throw 'Archive path escape'}
    if(Test-Path -LiteralPath $source){Move-Item -LiteralPath $source -Destination $dest}
   }
   [IO.File]::Delete((Join-Path $run 'client.quit'))
   Set-Content "$run/rejoin$rejoined.started" 'controller starting fresh client'
   $owned+=StartPeer Client ('-moteclient -username=MoteClient -powercycle='+$rejoined+' -connect=127.0.0.1:'+$Port)
  }
  if((Test-Path "$run/host.complete") -and (Test-Path "$run/client.complete")){
   if(!$Baseline -and (Get-Content "$run/host.final" -Raw) -ne (Get-Content "$run/client.final" -Raw)){throw 'Final state/Rand mismatch'}
   foreach($r in $hashes){if((Get-FileHash -LiteralPath $r.Path).Hash -ne $r.Hash){throw 'Input changed'}}
   $status=if($Baseline){'BASELINE_REPRODUCED'}else{'PASS'};$reason=if($Baseline){'Both peers reproduced 9 lingering effects on each map after 120 map ticks'}else{'three non-host fixture commands; native generator/cable/lamp power on each map; power and fleck updates once per map tick; frozen state/Rand equality'};break
  }
  foreach($p in $owned){if($p.HasExited -and $p.Id -notin $retired){throw 'Peer exited'}}
  Start-Sleep -Seconds 2
 }
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-moteprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;maps=1+[int][bool]$TwoMaps;async=[bool]$AsyncTime;coldRejoins=$rejoined;commander=[bool]$IncludeCommander;targetSharedWorldTicks=$Ticks;candidateSHA256=(Get-FileHash -LiteralPath $Core).Hash} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content "$run/result.json"
if($status -eq 'FAILED'){throw $reason}
