param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Candidate,[string]$BaselineSave,[switch]$Cold,[switch]$SnapshotOnly,[switch]$PilotOnly,[switch]$StaleOnly,[int]$SoakTicks=0,[int]$ControlMaps=0,[int]$ColdCycles=3,[int]$Port=30987,[int]$RecoveryCycles=0,[switch]$UnloadControl,[switch]$PilotAfter)
$ErrorActionPreference='Stop'
if($Cold -and $RecoveryCycles -gt 0){throw 'Choose paused-queue cold test or active recovery cycles, not both'}
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Run already exists'}
New-Item -ItemType Directory -Force "$run/Game/Mods","$run/Host/Config","$run/Client/Config","$run/Candidate","$run/Driver" | Out-Null
Copy-Item -LiteralPath $Candidate -Destination "$run/Candidate/MP_MeowOnlineShop.dll"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1","$PSScriptRoot/ModsConfig.xml" -Destination "$run/Driver"
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$name" -Target "$GameRoot/$name" | Out-Null}
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$GameRoot/$name"){Copy-Item -LiteralPath "$GameRoot/$name" -Destination "$run/Game/$name"}}
foreach($name in @('2009463077','2934420800','Multiplayer')){New-Item -ItemType Junction -Path "$run/Game/Mods/$name" -Target "$GameRoot/Mods/$name" | Out-Null}
foreach($name in @('Candidate','Probe')){New-Item -ItemType Directory -Force "$run/Game/Mods/$name/About","$run/Game/Mods/$name/Assemblies" | Out-Null}
Copy-Item "$run/Candidate/MP_MeowOnlineShop.dll" "$run/Game/Mods/Candidate/Assemblies/"
Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.OdysseyProbe.dll" "$run/Game/Mods/Probe/Assemblies/"
'<ModMetaData><name>Odyssey candidate</name><author>Local</author><packageId>local.mp.meowonlineshop.sellslingshot</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Candidate/About/About.xml"
'<ModMetaData><name>Odyssey diagnostic</name><author>Local</author><packageId>local.meow.odysseyprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$cfg='<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.odysseyprobe</li></activeMods></ModsConfigData>'
foreach($peer in @('Host','Client')){
 Copy-Item -LiteralPath "$PSScriptRoot/ModsConfig.xml" -Destination "$run/$peer/Config/ModsConfig.xml"
 '<PrefsData><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
}
$inputs=@("$run/Candidate/MP_MeowOnlineShop.dll","$run/Game/Mods/Candidate/Assemblies/MP_MeowOnlineShop.dll","$run/Game/Mods/Probe/Assemblies/Meow.OdysseyProbe.dll","$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml","$GameRoot/RimWorldWin64_Data/Managed/Assembly-CSharp.dll","$GameRoot/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll")
$inputs | Get-FileHash | ConvertTo-Json | Set-Content "$run/inputs.json"
if($BaselineSave){New-Item -ItemType Directory -Force "$run/Host/Saves" | Out-Null;Copy-Item -LiteralPath $BaselineSave -Destination "$run/Host/Saves/OdysseyInput.rws";Set-Content "$run/baseline" 'OdysseyInput';Get-FileHash "$run/Host/Saves/OdysseyInput.rws" | ConvertTo-Json | Set-Content "$run/baseline-hash.json"}
$owned=@();$status='FAILED';$reason='not complete';$watch=[Diagnostics.Stopwatch]::StartNew()
function Start-Peer([string]$peer,[string]$extra){
 $line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLowerInvariant()+'.log" -odysseyprobe -screen-fullscreen 0 -screen-width 960 -screen-height 640 '+$extra
 $line+=' -opport='+$Port
 if($PilotAfter){$line+=' -oppilotafter'}
 if($UnloadControl){$line+=' -opunloadcontrol'}
 if($RecoveryCycles -gt 0){$line+=' -oprecovery='+$RecoveryCycles}
 if($Cold){$line+=' -opcold'}
 if($ControlMaps -gt 0){$line+=' -opcontrolmaps='+$ControlMaps}
 if($StaleOnly){$line+=' -opstaleonly'}
 if($PilotOnly){$line+=' -oppilotonly'}
 if($SnapshotOnly){$line+=' -opsnapshotonly'}
 if($SoakTicks -gt 0){$line+=' -opsoak='+$SoakTicks}
 $p=Start-Process -FilePath "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
 Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run/$peer-process.json"
 return $p
}
try{
 $owned+=Start-Peer 'Host' $(if($BaselineSave){''}else{'-quicktest'});$clientStarted=$false
 $coldPhase=0;$coldPending=$false;$coldFinished=$false;$initialClientLog=''
 $recoveryPhase=0;$recoveryPending=$false;$recoveryReady=$false;$recoveryFinished=$false;$recoveryHistory=''
 while($watch.Elapsed.TotalSeconds -lt [Math]::Max(1800,800+($SoakTicks+10000*$RecoveryCycles)/30)){
  $h=if(Test-Path "$run/host.log"){[string](Get-Content "$run/host.log" -Raw)}else{''}
  $c=if(Test-Path "$run/client.log"){[string](Get-Content "$run/client.log" -Raw)}else{''}
  if(($h+$c) -match 'Gravship landing MP guard install failed|REQUIRED_TARGET_FAILED|Prepatcher Error: Fatal|Exception ticking|Exception in WorldComponentTick|Exception while ticking|Exception filling window|ODYSSEY_PROBE FAILED|Map cmd exception|World cmd exception|Desynced after|Sync Error|Inconsistent player|PacketReadException|Could not resolve cross-reference|Error in static constructor of OdysseyProbe'){throw 'Setup/action/sync failure: see logs'}
  if(($h+$c) -match 'd3d11[^\r\n]*(?:failed to create|Failed to create RenderTexture)[^\r\n]*(?:0x)?887a0005'){throw 'INVALID_GRAPHICS_DEVICE'}
  foreach($p in $owned){if($p.HasExited){throw 'Early peer exit'}}
  if(!$clientStarted -and $h -match '(?m)^Server started\.'){$owned+=Start-Peer 'Client' ('-opclient -connect=127.0.0.1:'+$Port);$clientStarted=$true}
  if($Cold -and !$coldFinished){
   if($coldPending -and (Test-Path "$run/cold.host$coldPhase")){
    $owned+=Start-Peer 'Client' ('-opclient -oprejoin -connect=127.0.0.1:'+$Port);$coldPending=$false
   }
   if(!$coldPending -and (Test-Path "$run/cold.host$coldPhase") -and (Test-Path "$run/cold.client$coldPhase")){
    if([IO.File]::ReadAllText("$run/cold.host$coldPhase") -ne [IO.File]::ReadAllText("$run/cold.client$coldPhase")){throw "COLD_REJOIN_DRIFT phase=$coldPhase"}
    if($coldPhase -eq $ColdCycles){Set-Content "$run/cold.phase" '4';$coldFinished=$true}
    else{
     Set-Content "$run/client.quit" 'restart';$peer=$owned[-1]
     if(!$peer.WaitForExit(15000)){throw 'Client failed to disconnect gracefully'}
     Copy-Item "$run/client.log" "$run/client-phase$coldPhase.log";Copy-Item "$run/Client-process.json" "$run/Client-phase$coldPhase-process.json"
     if($coldPhase -eq 0){$initialClientLog=[IO.File]::ReadAllText("$run/client.log")}
     $owned=@($owned | Where-Object {$_.Id -ne $peer.Id})
     Remove-Item -LiteralPath "$run/client.quit"
     $coldPhase++;Set-Content "$run/cold.phase" $coldPhase;$coldPending=$true
    }
   }
  }
  if($Cold -and $coldFinished){$c=$initialClientLog+"`n"+$c}
  if($RecoveryCycles -gt 0 -and !$recoveryFinished){
   if($recoveryPending -and (Test-Path "$run/recovery.host$recoveryPhase")){
    $owned+=Start-Peer 'Client' ('-opclient -oprejoin -connect=127.0.0.1:'+$Port);$recoveryPending=$false
   }
   if(!$recoveryPending -and !$recoveryReady -and (Test-Path "$run/recovery.host$recoveryPhase") -and (Test-Path "$run/recovery.client$recoveryPhase")){
    if([IO.File]::ReadAllText("$run/recovery.host$recoveryPhase") -ne [IO.File]::ReadAllText("$run/recovery.client$recoveryPhase")){throw "RECOVERY_BASELINE_DRIFT phase=$recoveryPhase"}
    $recoveryReady=$true
    if($recoveryPhase -gt 0){Set-Content "$run/recovery.resume$recoveryPhase" 'resume'}
   }
   $cycleDone=$recoveryPhase -eq 0 -or ((Test-Path "$run/recovery.done.host$recoveryPhase") -and (Test-Path "$run/recovery.done.client$recoveryPhase") -and ($recoveryPhase -eq $RecoveryCycles -or (Test-Path "$run/recovery.frozen$recoveryPhase")))
   if($recoveryReady -and $cycleDone){
    if($recoveryPhase -gt 0 -and [IO.File]::ReadAllText("$run/recovery.done.host$recoveryPhase") -ne [IO.File]::ReadAllText("$run/recovery.done.client$recoveryPhase")){throw "RECOVERY_ACTION_DRIFT phase=$recoveryPhase"}
    if($recoveryPhase -eq $RecoveryCycles){$recoveryFinished=$true}
    else{
     Set-Content "$run/client.quit" 'restart';$peer=$owned[-1]
     if(!$peer.WaitForExit(15000)){throw 'Recovery client failed to disconnect gracefully'}
     Copy-Item "$run/client.log" "$run/client-recovery$recoveryPhase.log";Copy-Item "$run/Client-process.json" "$run/Client-recovery$recoveryPhase-process.json"
     $recoveryHistory+=[IO.File]::ReadAllText("$run/client.log")+"`n"
     $owned=@($owned | Where-Object {$_.Id -ne $peer.Id});Remove-Item -LiteralPath "$run/client.quit"
     $recoveryPhase++;$recoveryReady=$false;$recoveryPending=$true;Set-Content "$run/recovery.phase" $recoveryPhase
    }
   }
  }
  if($RecoveryCycles -gt 0 -and $recoveryFinished){$c=$recoveryHistory+$c}
  if((Test-Path "$run/host.complete") -and (Test-Path "$run/client.complete")){
   if(!$UnloadControl -and $ControlMaps -eq 0 -and !$SnapshotOnly -and !$PilotOnly -and !$StaleOnly -and [IO.File]::ReadAllText("$run/host.receipt") -ne [IO.File]::ReadAllText("$run/client.receipt")){throw 'Peer receipts differ'}
   $hr=@($h -split "`n" | Where-Object {$_ -match '^ODYSSEY_EFFECT|^ODYSSEY_LIFECYCLE|^ODYSSEY_SNAPSHOT|^ODYSSEY_REFUEL|^ODYSSEY_FLIGHT|^ODYSSEY_LOADING|^ODYSSEY_CARRIED|^ODYSSEY_ACCEPT|^ODYSSEY_SOAK|^ODYSSEY_PILOT|^ODYSSEY_CONTROL|^ODYSSEY_RECOVERY|^ODYSSEY_UNLOAD_CONTROL'});$cr=@($c -split "`n" | Where-Object {$_ -match '^ODYSSEY_EFFECT|^ODYSSEY_LIFECYCLE|^ODYSSEY_SNAPSHOT|^ODYSSEY_REFUEL|^ODYSSEY_FLIGHT|^ODYSSEY_LOADING|^ODYSSEY_CARRIED|^ODYSSEY_ACCEPT|^ODYSSEY_SOAK|^ODYSSEY_PILOT|^ODYSSEY_CONTROL|^ODYSSEY_RECOVERY|^ODYSSEY_UNLOAD_CONTROL'})
   $expected=if($UnloadControl){6}elseif($ControlMaps -gt 0){10+3*($ControlMaps-1)}elseif($StaleOnly -and $PilotOnly){4}elseif($StaleOnly){3}elseif($PilotOnly){1}elseif($SnapshotOnly){2}else{35};$expected+=6*$RecoveryCycles;if($PilotAfter){$expected++};if($SoakTicks -gt 0){$expected+=2+4*[Math]::Floor(($SoakTicks-1)/12000)}
   if($hr.Count -ne $expected -or ($hr -join "`n") -ne ($cr -join "`n")){throw 'Combined effect/lifecycle receipts differ'}
   foreach($input in Get-Content "$run/inputs.json" -Raw | ConvertFrom-Json){if((Get-FileHash -LiteralPath $input.Path).Hash -ne $input.Hash){throw ('Input changed: '+$input.Path)}}
   $status='DIAGNOSTIC';$reason='Paired native unload observations collected; not a compatibility pass';break
  }
  Start-Sleep -Seconds 3
 }
 if($status -eq 'FAILED'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message;Write-Output $reason}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-odysseyprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds;scope='official DLC minimal loadout; diagnostic only'} | ConvertTo-Json | Set-Content "$run/result.json"
}
if($status -eq 'FAILED'){exit 1}
