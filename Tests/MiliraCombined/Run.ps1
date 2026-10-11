param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Candidate,[Parameter(Mandatory=$true)][string]$Batch,[string]$Replay,[string]$ExternalConfig,[string]$ExternalConfigFolder,[string]$ProbeDll,[string]$ProbeSourceFolder,[switch]$FreshFromReplay,[switch]$Features,[switch]$ManualHandbook,[switch]$ManualTwoPeer,[switch]$StudentColdOnly,[switch]$TraceDiagnostic,[switch]$TraceHistory,[switch]$MultiMap,[switch]$AsyncTime,[switch]$ReplayCold3,[switch]$NaglfarOnly,[switch]$FunnelLinesOnly,[switch]$ResearchUiOnly,[switch]$BackupBatteryOnly,[switch]$LowFrequency,[switch]$ResearchLong,[switch]$Defense,[switch]$NativeBooleans,[switch]$TurretDiagnostic,[switch]$IncidentTrace,[switch]$IconTrace,[switch]$IconFix,[switch]$LegacyRegression,[int]$Port=0,[double]$MinFreeMemoryGB=0,[switch]$AllowKnownStartupIconError)
$ErrorActionPreference='Stop'
if($ManualTwoPeer -and !$ManualHandbook){throw 'ManualTwoPeer requires ManualHandbook'}
if($MultiMap -and !($Features -or $ResearchUiOnly)){throw 'MultiMap requires Features or ResearchUiOnly'}
if($TurretDiagnostic -and !($Defense -and $TraceDiagnostic -and $ResearchUiOnly)){throw 'TurretDiagnostic requires Defense, TraceDiagnostic and ResearchUiOnly'}
if($Defense -and !(($ResearchUiOnly -or $Features) -and $MultiMap)){throw 'Defense requires Features or ResearchUiOnly and MultiMap'}
if($AsyncTime -and !$MultiMap){throw 'AsyncTime requires MultiMap'}
if($ReplayCold3 -and (!$Replay -or !$Features)){throw 'ReplayCold3 requires Replay and Features'}
if($NaglfarOnly -and (!$Features -or !$MultiMap)){throw 'NaglfarOnly requires Features and MultiMap'}
if($FunnelLinesOnly -and !$Features){throw 'FunnelLinesOnly requires Features'}
if($ResearchUiOnly -and ($Features -or $ManualHandbook -or $ReplayCold3)){throw 'ResearchUiOnly uses its own short two-peer action path'}
if($LowFrequency -and !$ResearchUiOnly){throw 'LowFrequency requires ResearchUiOnly'}
if($ResearchLong -and !$ResearchUiOnly){throw 'ResearchLong requires ResearchUiOnly'}
if($BackupBatteryOnly -and ($Features -or $ResearchUiOnly -or $ManualHandbook -or $ReplayCold3)){throw 'BackupBatteryOnly uses its own short two-peer action path'}
if($NativeBooleans -and !($Features -or $ResearchUiOnly)){throw 'NativeBooleans requires Features or ResearchUiOnly'}
if($LegacyRegression -and (!$ProbeDll -or !$ProbeSourceFolder -or !$Features)){throw 'LegacyRegression requires frozen ProbeDll, ProbeSourceFolder, and Features'}
$PSDefaultParameterValues['Set-Content:Encoding']='UTF8'
$run=[IO.Path]::GetFullPath($RunRoot)
$game=(Resolve-Path '../..').Path
if(Test-Path -LiteralPath $run){throw 'Run already exists'}
New-Item -ItemType Directory -Force "$run/Game/Mods","$run/Host/Config","$run/Client/Config","$run/Candidate","$run/Driver" | Out-Null
Copy-Item -LiteralPath $Candidate -Destination "$run/Candidate/MP_MeowOnlineShop.dll"
$driverSource=if($ProbeSourceFolder){$ProbeSourceFolder}else{$PSScriptRoot}
Copy-Item "$driverSource/*.cs","$driverSource/*.csproj","$driverSource/*.ps1" -Destination "$run/Driver"
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$name" -Target "$game/$name" | Out-Null}
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$name"){Copy-Item -LiteralPath "$game/$name" -Destination "$run/Game/$name"}}
$modFolders=@('2009463077','2934420800','Multiplayer','839005762','2988801276','3256974620','3626456960','3621922658','3496629836','3665997350','3588393755')
if($Replay -or $ExternalConfig){$modFolders=@(Get-ChildItem "$game/Mods" -Directory | Where-Object Name -ne 'MP-meow-online-shop' | Select-Object -ExpandProperty Name)}
foreach($name in $modFolders){New-Item -ItemType Junction -Path "$run/Game/Mods/$name" -Target "$game/Mods/$name" | Out-Null}
foreach($name in @('Candidate','Probe')){New-Item -ItemType Directory -Force "$run/Game/Mods/$name/About","$run/Game/Mods/$name/Assemblies" | Out-Null}
foreach($payload in @('1.6','Patches','Languages','MeleeAnimation','RavenCompatibility','RatkinUnderground')){
 Copy-Item -LiteralPath $payload -Destination "$run/Game/Mods/Candidate/$payload" -Recurse
}
Copy-Item 'LoadFolders.xml' "$run/Game/Mods/Candidate/"
if($IconFix){Copy-Item "$driverSource/IconFix.xml" "$run/Game/Mods/Candidate/Patches/Milira-Diagnostic-DailyFurniture-IconFix.xml"}
Copy-Item "$run/Candidate/MP_MeowOnlineShop.dll" "$run/Game/Mods/Candidate/1.6/Assemblies/" -Force
$probePayload=if($ProbeDll){$ProbeDll}else{"$PSScriptRoot/bin/Release/net48/Meow.MiliraActionsProbe.dll"}
Copy-Item -LiteralPath $probePayload -Destination "$run/Game/Mods/Probe/Assemblies/"
Copy-Item $Batch "$run/Game/Mods/Candidate/1.6/Assemblies/" -Force
Copy-Item 'About/About.xml' "$run/Game/Mods/Candidate/About/"
'<ModMetaData><name>Trade window diagnostic</name><author>Local</author><packageId>local.meow.miliraactionsprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$cfg='<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>erdelf.humanoidalienraces</li><li>ancot.ancotlibrary</li><li>ancot.milirarace</li><li>ancot.milianmodification</li><li>sleepycot.wingsofdemocracy</li><li>rabiosus.funnelmilian</li><li>ariandel.ariandellibrary</li><li>ariandel.miliraimperium</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.miliraactionsprobe</li></activeMods></ModsConfigData>'
if($ExternalConfig){
 Copy-Item -LiteralPath $ExternalConfig -Destination "$run/external-ModsConfig.xml"
 [xml]$configDoc=Get-Content -LiteralPath "$run/external-ModsConfig.xml" -Raw
 $activeNode=$configDoc.SelectSingleNode('/ModsConfigData/activeMods')
 if($null -eq $activeNode){throw 'ExternalConfig has no activeMods'}
 if(!(@($configDoc.ModsConfigData.activeMods.li) -contains 'local.mp.meowonlineshop.sellslingshot')){throw 'ExternalConfig does not enable MP-meow-online-shop'}
 if(!(@($configDoc.ModsConfigData.activeMods.li) -contains 'local.meow.miliraactionsprobe')){$li=$configDoc.CreateElement('li');$li.InnerText='local.meow.miliraactionsprobe';$null=$activeNode.AppendChild($li)}
 $cfg=$configDoc.OuterXml
}
if($Replay){
 Copy-Item -LiteralPath $Replay "$run/input.zip"
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip=[IO.Compression.ZipFile]::OpenRead("$run/input.zip")
 try{$reader=[IO.StreamReader]::new($zip.GetEntry('info').Open());[xml]$info=$reader.ReadToEnd();$reader.Dispose()}finally{$zip.Dispose()}
 [xml]$configDoc=$cfg
 $activeNode=$configDoc.SelectSingleNode('/ModsConfigData/activeMods');$activeNode.RemoveAll()
 $replayModIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
 foreach($id in @($info.ReplayInfo.modIds.li)+@('local.meow.miliraactionsprobe')){if(!$replayModIds.Add([string]$id)){continue};$li=$configDoc.CreateElement('li');$li.InnerText=$id;$null=$activeNode.AppendChild($li)}
 $cfg=$configDoc.OuterXml
}
[xml]$modCapConfig=$cfg
$activeModCount=@($modCapConfig.ModsConfigData.activeMods.li).Count
if($activeModCount -gt 400){throw "User test limit exceeded: $activeModCount active mods (maximum 400)"}
foreach($peer in @('Host','Client')){
 if($Replay -and !$ExternalConfigFolder){Copy-Item '../../../Config/*' "$run/$peer/Config/" -Recurse -Force}
 if($ExternalConfigFolder){Copy-Item (Join-Path $ExternalConfigFolder '*') "$run/$peer/Config/" -Recurse -Force}
 $cfg | Set-Content "$run/$peer/Config/ModsConfig.xml"
 if($ExternalConfigFolder -and (Test-Path "$run/$peer/Config/Prefs.xml")){
  [xml]$prefs=Get-Content "$run/$peer/Config/Prefs.xml" -Raw
  $prefs.PrefsData.devMode='False';$prefs.PrefsData.runInBackground='True'
  $prefs.Save("$run/$peer/Config/Prefs.xml")
 }else{
  '<PrefsData><volumeMaster>0</volumeMaster><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><volumeAmbient>0</volumeAmbient><volumeUI>0</volumeUI><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
 }
}
if($ExternalConfigFolder){Get-ChildItem -LiteralPath "$run/Host/Config" -Recurse -File | Get-FileHash | ConvertTo-Json | Set-Content "$run/config-input-hashes.json"}
(Get-FileHash "$run/Candidate/MP_MeowOnlineShop.dll").Hash | Set-Content "$run/candidate.sha256"
$hashPaths=@("$run/Candidate/MP_MeowOnlineShop.dll","$run/Game/Mods/Probe/Assemblies/Meow.MiliraActionsProbe.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll","$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml")+@(Get-ChildItem "$run/Game/Mods/Candidate" -Recurse -File | Select-Object -ExpandProperty FullName)
foreach($id in @('3256974620','3626456960','3496629836','3588393755','2988801276','3665997350')){$hashPaths+=@(Get-ChildItem "$game/Mods/$id/1.6" -Recurse -Filter '*.dll' | Select-Object -ExpandProperty FullName)}
$hashPaths+=@(Get-ChildItem "$game/Mods/3621922658" -Recurse -Filter *.dll | Select-Object -ExpandProperty FullName)
if($Replay){$hashPaths+="$run/input.zip"}
if($ExternalConfig){$hashPaths+="$run/external-ModsConfig.xml"}
Get-FileHash -LiteralPath $hashPaths | ConvertTo-Json | Set-Content "$run/inputs.json"
$owned=@();$retired=@();$coldCycle=if($ReplayCold3){3}else{0};$coldWaiting=$false;$clientProcess=$null;$status='FAILED';$reason='not completed';$watch=[Diagnostics.Stopwatch]::StartNew()
if($ReplayCold3){Set-Content "$run/cold-cycle.txt" 3}
function ReadLog($path){if(!(Test-Path -LiteralPath $path)){return ''};$stream=$null;$reader=$null;try{$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);return $reader.ReadToEnd()}finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}}
function StartPeer($peer,$extra){if($TraceDiagnostic){$extra+=' -miliratracediagnostic'};if($TraceHistory){$extra+=' -miliratracehistory'};if($StudentColdOnly){$extra+=' -studentcoldonly'};if($NaglfarOnly){$extra+=' -miliranaglfaronly'};if($FunnelLinesOnly){$extra+=' -milirafunnellinesonly'};if($ResearchUiOnly){$extra+=' -miliraresearchuionly'};if($BackupBatteryOnly){$extra+=' -milirabackupbatteryonly'};if($LowFrequency){$extra+=' -miliralowfrequency'};if($ResearchLong){$extra+=' -miliraresearchlong'};if($Defense){$extra+=' -miliradefense'};if($TurretDiagnostic){$extra+=' -miliraturretdiagnostic'};if($NativeBooleans){$extra+=' -milirabooleans'};if($ManualHandbook){$extra+=' -handbookmanual'};if($ManualTwoPeer){$extra+=' -handbooktwopeer'};if($MultiMap){$extra+=' -miliramultimap'};if($AsyncTime){$extra+=' -miliraasynctime'};if($ReplayCold3){$extra+=' -milirareplaycold3 -coldcycle=3'};if($IncidentTrace){$extra+=' -miliraincidenttrace'};if($IconTrace){$extra+=' -miliraicontrace'};if($Port -gt 0){$extra+=(' -miliraport='+$Port);$extra=$extra.Replace('30997',[string]$Port)};if($Features){$extra+=' -milirafeatures';$extra=$extra.Replace('30997','30998')};$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -miliraactionsprobe -screen-fullscreen 0 -screen-width 1280 -screen-height 900 '+$extra;$style=if($ManualHandbook){'Normal'}else{'Hidden'};$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle $style -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
try{
 $hostArgs='-quicktest';if($Replay -and !$FreshFromReplay){$hostArgs='-milirareplay'}
 $owned+=StartPeer Host $hostArgs;$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 3600){
  if($MinFreeMemoryGB -gt 0){
   $freeGB=(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
   if($freeGB -lt $MinFreeMemoryGB){throw ("Resource gate: free physical memory {0:N2} GB below {1:N2} GB" -f $freeGB,$MinFreeMemoryGB)}
  }
  $h=ReadLog "$run/host.log";$c=ReadLog "$run/client.log";for($ci=1;$ci -le $coldCycle;$ci++){$c+=ReadLog "$run/client$ci.log"}
  if([regex]::Matches($h,'NullReferenceException: Object reference').Count -gt 20 -or [regex]::Matches($c,'NullReferenceException: Object reference').Count -gt 20){throw 'Repeated startup/runtime NullReferenceException; see logs'}
  if(($h+$c) -match 'Desynced after last valid tick'){
   $first=$null
   for($wait=0;$wait -lt 12 -and !$first;$wait++){
    foreach($peerName in @('Client','Client1','Client2','Client3')){
     $folder="$run/$peerName/MpDesyncs"
     if(Test-Path -LiteralPath $folder){$first=Get-ChildItem -LiteralPath $folder -Filter 'Desync-*.zip' -File | Sort-Object LastWriteTime | Select-Object -First 1;if($first){break}}
    }
    if(!$first){Start-Sleep -Seconds 1}
   }
   if($first){Copy-Item -LiteralPath $first.FullName -Destination "$run/first-desync.zip";"source=$($first.FullName) hash=$((Get-FileHash -LiteralPath $first.FullName).Hash)" | Set-Content "$run/desync-capture.txt"}
   else{'No Desync-*.zip produced before controlled shutdown' | Set-Content "$run/desync-capture.txt"}
   throw 'Actual MP desync; see client log and first-desync.zip if captured'
  }
  $fatalHostLog=$h
  if($AllowKnownStartupIconError){
   $knownIconError='(?s)Could not execute post-long-event action\. Exception: System\.NullReferenceException: Object reference not set to an instance of an object\r?\n\[Ref [^\r\n]+\]\r?\n\s+at Verse\.Graphic_Appearances\.get_MatSingle.*?at Verse\.BuildableDef\.<PostLoad>b__78_0.*?at ilyvion\.LoadingProgress\.LongEventHandler_ExecuteToExecuteWhenFinished_Patches\+<ExecuteToExecuteWhenFinished>d__3\.MoveNext[^\r\n]*(?:\r?\n|$)'
   $fatalHostLog=[regex]::Replace($fatalHostLog,$knownIconError,'')
  }
  if(($fatalHostLog+$c) -match 'MILIRA_ACTIONS FAILED|COMBINED FAILED|REQUIRED_TARGET_FAILED|Could not execute post-long-event action|REQUIRED_TARGET_FAILURE trade auto-open|Prepatcher Error: Fatal|Error in static constructor of MiliraActionsProbe|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Collection was modified; enumeration operation may not execute|Random state stack is not empty|Could not resolve cross-reference|Could not resolve reference to object|Tried to register the same load ID twice|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception'){throw 'Runtime/setup failure; see logs'}
  if((!$ManualHandbook -or $ManualTwoPeer) -and !$clientStarted -and $h -match '(?m)^Server started\.'){$clientProcess=StartPeer Client '-twclient -connect=127.0.0.1:30997';$owned+=$clientProcess;$clientStarted=$true}
  if($Features -and !$coldWaiting -and $coldCycle -lt 3 -and (Test-Path "$run/host.coldcomplete$coldCycle") -and (Test-Path "$run/client.coldcomplete$coldCycle")){
   if((Get-Content "$run/host.coldfrozen$coldCycle" -Raw) -cne (Get-Content "$run/client.coldready$coldCycle" -Raw)){throw 'Cold snapshot mismatch'}
   Set-Content "$run/client$coldCycle.quit" 'cold rejoin'
   if(!$clientProcess.WaitForExit(15000)){throw 'Cold client graceful exit timeout'}
   $retired+=$clientProcess.Id;$coldCycle++;Set-Content "$run/cold-cycle.txt" $coldCycle;$coldWaiting=$true
  }
  if($Features -and $coldWaiting -and (Test-Path "$run/host.coldfrozen$coldCycle")){
   $nextPeer="Client$coldCycle";New-Item -ItemType Directory -Force "$run/$nextPeer/Config" | Out-Null
   Copy-Item "$run/Host/Config/*" "$run/$nextPeer/Config/" -Recurse -Force
   Get-FileHash "$run/$nextPeer/Config/ModsConfig.xml","$run/$nextPeer/Config/Prefs.xml" | ConvertTo-Json | Set-Content "$run/cold$coldCycle-inputs.json"
   $clientProcess=StartPeer $nextPeer ("-twclient -coldcycle=$coldCycle -connect=127.0.0.1:30997");$owned+=$clientProcess;$coldWaiting=$false
  }
  if($ManualHandbook -and (Test-Path "$run/manual.complete")){$status='PASS';$reason=if($ManualTwoPeer){'Manual UI observation on host and joined client only; not a simulation soak'}else{'Manual UI observation on one host only; not a two-peer validation'};break}
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
   if($FunnelLinesOnly){
    foreach($ci in 0..2){foreach($phase in 0..1){
     $hostReceipt="$run/host.funnelline$ci.$phase";$clientReceipt="$run/client.funnelline$ci.$phase"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Funnel line mismatch round$ci phase$phase"}
    }}
   }elseif($NaglfarOnly){
    foreach($ci in 0..2){
     $hostReceipt="$run/host.naglfarworld$ci";$clientReceipt="$run/client.naglfarworld$ci"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Naglfar repeated-action mismatch round$ci"}
    }
    if(!(Test-Path "$run/host.abilityworld") -or !(Test-Path "$run/client.abilityworld") -or (Get-Content "$run/host.abilityworld" -Raw) -cne (Get-Content "$run/client.abilityworld" -Raw)){throw 'World ability peer receipt mismatch'}
   }elseif($TurretDiagnostic){foreach($ci in 0..8){if(!(Test-Path "$run/host.defense$ci") -or !(Test-Path "$run/client.defense$ci") -or (Get-Content "$run/host.defense$ci" -Raw) -cne (Get-Content "$run/client.defense$ci" -Raw)){throw "Turret diagnostic mismatch $ci"}}
   }elseif($ResearchUiOnly){
    foreach($ci in 0..3){
     $hostReceipt="$run/host.researchui$ci";$clientReceipt="$run/client.researchui$ci"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Research UI peer mismatch stage$ci"}
    }
    if(!(Test-Path "$run/host.backupbattery") -or !(Test-Path "$run/client.backupbattery") -or (Get-Content "$run/host.backupbattery" -Raw) -cne (Get-Content "$run/client.backupbattery" -Raw)){throw 'Backup battery peer receipt mismatch'}
    if(!(Test-Path "$run/host.abilityself") -or !(Test-Path "$run/client.abilityself") -or (Get-Content "$run/host.abilityself" -Raw) -cne (Get-Content "$run/client.abilityself" -Raw)){throw 'Self-cast ability peer receipt mismatch'}
    if(!(Test-Path "$run/host.installjob") -or !(Test-Path "$run/client.installjob") -or (Get-Content "$run/host.installjob" -Raw) -cne (Get-Content "$run/client.installjob" -Raw)){throw 'Installation job peer receipt mismatch'}
    if(!(Test-Path "$run/host.uninstalljob") -or !(Test-Path "$run/client.uninstalljob") -or (Get-Content "$run/host.uninstalljob" -Raw) -cne (Get-Content "$run/client.uninstalljob" -Raw)){throw 'Uninstallation job peer receipt mismatch'}
    foreach($ci in 0..2){
     $hostReceipt="$run/host.componentdata$ci";$clientReceipt="$run/client.componentdata$ci"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Component data/disassembly peer mismatch round$ci"}
    }
    if($LowFrequency){foreach($ci in 0..11){
     $hostReceipt="$run/host.lowfrequency$ci";$clientReceipt="$run/client.lowfrequency$ci"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Low-frequency peer mismatch round$ci"}
    }}
    if($LowFrequency){foreach($ci in 0..5){
     $hostReceipt="$run/host.wingscontrols$ci";$clientReceipt="$run/client.wingscontrols$ci"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Wings control peer mismatch round$ci"}
    }}
    if($Defense){foreach($ci in 0..26){
     $hostReceipt="$run/host.defense$ci";$clientReceipt="$run/client.defense$ci"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Defense peer mismatch round$ci"}
    }}
    if($ResearchLong){
     if(!(Test-Path "$run/host.researchlong") -or !(Test-Path "$run/client.researchlong") -or (Get-Content "$run/host.researchlong" -Raw) -cne (Get-Content "$run/client.researchlong" -Raw)){throw 'Long research soak peer gates missing'}
    }
   }elseif($BackupBatteryOnly){
    if(!(Test-Path "$run/host.backupbattery") -or !(Test-Path "$run/client.backupbattery") -or (Get-Content "$run/host.backupbattery" -Raw) -cne (Get-Content "$run/client.backupbattery" -Raw)){throw 'Backup battery peer receipt mismatch'}
   }elseif($ReplayCold3){
    if((Get-Content "$run/host.coldfrozen3" -Raw) -cne (Get-Content "$run/client.coldready3" -Raw)){throw 'Replayed cold3 snapshot mismatch'}
    foreach($suffix in @('historyids-aftercold','mermeistercold')){
     if(!(Test-Path "$run/host.$suffix") -or !(Test-Path "$run/client.$suffix") -or (Get-Content "$run/host.$suffix" -Raw) -cne (Get-Content "$run/client.$suffix" -Raw)){throw "Replayed cold3 callback mismatch: $suffix"}
    }
    if($h -notmatch 'STUDENT_COLD ASSERT three native choices after cold' -or $c -notmatch 'STUDENT_COLD ASSERT three native choices after cold'){throw 'Replayed student callback missing'}
   }elseif($Features){
    if($coldCycle -ne 3 -or (!$StudentColdOnly -and (!(Test-Path "$run/host.actionscomplete") -or !(Test-Path "$run/client.actionscomplete")))){throw 'Missing actions or three cold rejoins'}
    if(!$StudentColdOnly -and !$LegacyRegression){foreach($ci in 0..2){foreach($phase in 0..1){
     $hostReceipt="$run/host.funnelline$ci.$phase";$clientReceipt="$run/client.funnelline$ci.$phase"
     if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Combined Funnel line mismatch round$ci phase$phase"}
    }}}
    for($ci=0;$ci -le 3;$ci++){
     if((Get-Content "$run/host.coldfrozen$ci" -Raw) -cne (Get-Content "$run/client.coldready$ci" -Raw)){throw "Cold equality missing cycle$ci"}
     if($ci -gt 0){[xml]$cx=Get-Content "$run/Client$ci/Config/ModsConfig.xml" -Raw;if(($cx.ModsConfigData.activeMods.li -join ';') -cne ($expected.ModsConfigData.activeMods.li -join ';')){throw 'Cold client loadout changed'}}
    }
   }
   if($Defense -and $Features){foreach($ci in 0..26){
    if(!(Test-Path "$run/host.defense$ci") -or !(Test-Path "$run/client.defense$ci") -or (Get-Content "$run/host.defense$ci" -Raw) -cne (Get-Content "$run/client.defense$ci" -Raw)){throw "Feature defense mismatch $ci"}
   }}
   if($NativeBooleans){foreach($suffix in (@('booleans0','booleans1','booleans2') + $(if($Features){@('booleans-aftercold')}else{@()}))){
    $hostReceipt="$run/host.$suffix";$clientReceipt="$run/client3.$suffix"
    if($suffix -ne 'booleans-aftercold'){$clientReceipt="$run/client.$suffix"}
    if(!(Test-Path $hostReceipt) -or !(Test-Path $clientReceipt) -or (Get-Content $hostReceipt -Raw) -cne (Get-Content $clientReceipt -Raw)){throw "Native boolean peer mismatch $suffix"}
   }}
   $status='PASS';$reason=if($FunnelLinesOnly){'Three native line AoE and three native line shield actions plus 10000-tick smoke'}elseif($NaglfarOnly){'Three native Naglfar cross-map actions, native Milian world ability, and 10000-tick smoke'}elseif($TurretDiagnostic){'Diagnostic only: nine native turret controls and 10000 ticks with traces enabled'}elseif($ResearchUiOnly){'Research UI, native jobs, data/disassembly, optional low-frequency and Wings actions; optional 120000 world/map soak; exact gates recorded by frozen driver and receipts'}elseif($BackupBatteryOnly){'Native backup battery gizmo and 1000-tick smoke'}elseif($ReplayCold3){'Replayed cold3 snapshot, restored callbacks, pending launch and postcold soak gates'}else{'Milira action receipts and cold/soak gates'};break
  }
  foreach($p in $owned){if($p.HasExited -and $retired -notcontains $p.Id){throw 'Early peer exit'}}
  Start-Sleep -Seconds 3
 }
 if($status -ne 'PASS'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-miliraactionsprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds;replay=$Replay;freshFromReplay=[bool]$FreshFromReplay;features=[bool]$Features;traceDiagnostic=[bool]$TraceDiagnostic;traceHistory=[bool]$TraceHistory;multiMap=[bool]$MultiMap;asyncTime=[bool]$AsyncTime;replayCold3=[bool]$ReplayCold3;naglfarOnly=[bool]$NaglfarOnly;funnelLinesOnly=[bool]$FunnelLinesOnly;researchUiOnly=[bool]$ResearchUiOnly;lowFrequency=[bool]$LowFrequency;researchLong=[bool]$ResearchLong;defense=[bool]$Defense;turretDiagnostic=[bool]$TurretDiagnostic;nativeBooleans=[bool]$NativeBooleans;additionalScope=$(if($LowFrequency){'Three native jump target/cancel/reopen actions including grouped selection, three native shield energy pulses, three native caravan permit and caravan world ability actions, and three native farewell data jobs plus choices; three Uhlan slot selections and three Argo bind/unbind cycles; exact paired receipts required'}else{'Three native data acquisition and fitting disassembly jobs each; Defense flag additionally requires27 native turret GUI/target/reset, remote condition, Archer, secret reward, god tower job and field ability receipts'});backupBatteryOnly=[bool]$BackupBatteryOnly;incidentTrace=[bool]$IncidentTrace;iconTrace=[bool]$IconTrace;iconFix=[bool]$IconFix;legacyRegression=[bool]$LegacyRegression;minFreeMemoryGB=$MinFreeMemoryGB;allowKnownStartupIconError=[bool]$AllowKnownStartupIconError;coldCycle=$coldCycle;manualHandbook=[bool]$ManualHandbook;manualTwoPeer=[bool]$ManualTwoPeer;scope=$(if($FunnelLinesOnly){'Three client-issued native Funnel line AoE and line shield callbacks each, exact paired receipts and 10000 shared ticks; no full feature sequence or cold rejoin'}elseif($NaglfarOnly){'Three client-issued native Naglfar world-support actions across both map directions, one native Milian world-target ability, and 10000 shared smoke ticks; no full feature sequence or cold rejoin'}elseif($TurretDiagnostic){'Diagnostic only: nine native turret controls and 10000 ticks with traces enabled'}elseif($ResearchUiOnly){'Handbook three opens, research submissions three, native history ID allocation, four client-issued research UI edits, backup battery gizmo, native self-cast ability, native installation and uninstallation jobs, exact peer receipts and 10000 shared ticks; no cold rejoin'}elseif($BackupBatteryOnly){'Single client-issued native backup battery gizmo with exact paired receipt and 1000 shared ticks; diagnostic without other feature actions or cold rejoin'}elseif($ManualHandbook){if($ManualTwoPeer){'Two-peer manual handbook native input diagnosis; loadout and replay mode recorded in inputs.json'}else{'Single-host manual handbook click diagnosis only; loadout and replay mode recorded in inputs.json; no second peer'}}elseif($ReplayCold3){'Cold3 replay diagnostic only: restored frozen snapshot, student/Mermeister/history callbacks, pending launch and 10000 or 120000 postcold ticks; no prior actions or new cold cycles in this run'}elseif($TraceDiagnostic){if($TraceHistory){'Diagnostic only: native feature sequence including history-ID regression and three cold rejoins, trace-enabled 10000 postcold ticks with cached replay exports; not a final120000tick soak'}else{'Diagnostic only: prior native feature sequence and three cold rejoins, trace-enabled 10000 postcold ticks with cached replay exports; history-ID regression deliberately excluded; not a final120000tick soak'}}elseif($StudentColdOnly){'Student-dialogue diagnostic only: native ordered job, open-dialog cold3, three native options, pending launch and120000 normal ticks; prior feature actions not requested'}elseif($LegacyRegression){'Frozen MC35 probe on current candidate: one-map native actions, three fresh cold rejoins, normal 120000 shared ticks; excludes later Funnel line/Naglfar additions'}elseif($Features){'Planned: Milian handbook native button/render/reopen x3; native research submissions x3 with bill/material/skill assertions; native history ID allocator uniqueness across three same-tick deletion cycles; six native technology purchases with duplicate-click cost assertions; three native Funnel cooldown cycles and UI expiry checks; three line AoE and three line shield native callbacks; three paid royal permit calls with competing local targeting contexts; 27 installation/cancellation/checkbox stages; enabled Wings weapon switch and six Funnel toggles x3; support menu only if an enabled Def exists; Bishop II slider/style/color x3; nine airstrikes; three fresh-client cold rejoins; saved pending-launch and dialogue flags; open student dialogue persists and three native choices execute after rejoin; native Mermeister research-option factory persists through cold3 and executes one quest callback with matching response/cooldown; native pending launch resumes once; 120000 normal-settings world/map ticks, traces off'}else{'Four native console summon buttons, three repetitions each, 10000 shared ticks'})} | ConvertTo-Json | Set-Content "$run/result.json"
}
if($status -ne 'PASS'){throw $reason}


