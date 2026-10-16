param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Replay,[switch]$AsyncTime)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$data=(Resolve-Path "$game/..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Unique run required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){Copy-Item -LiteralPath "$game/$n" -Destination "$run/Game/$n"}
foreach($d in Get-ChildItem "$game/Mods" -Directory){New-Item -ItemType Junction -Path "$run/Game/Mods/$($d.Name)" -Target $d.FullName | Out-Null}
Copy-Item -LiteralPath "$PSScriptRoot/bin/Release/net48/Meow.MoteLifecycleProbe.dll" -Destination "$run/Game/Mods/Probe/Assemblies/"
Copy-Item -LiteralPath $Replay -Destination "$run/Input.zip"
'<ModMetaData><name>Mote lifecycle diagnostic</name><author>Local</author><packageId>local.meow.moteprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Replay).Path)
try{$reader=[IO.StreamReader]::new($archive.GetEntry('info').Open());try{[xml]$info=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$archive.Dispose()}
$ids=@($info.ReplayInfo.modIds.li)+@('local.meow.moteprobe')
Copy-Item "$data/Config/*.xml" "$run/Host/Config/"
$cfg='<ModsConfigData><version>1.6.4850 rev646</version><activeMods>'+($ids | ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods></ModsConfigData>'
$cfg | Set-Content "$run/Host/Config/ModsConfig.xml"
foreach($name in @('HugsLib','OgreStack','MissileGirl')){if(Test-Path "$data/$name"){Copy-Item "$data/$name" "$run/Host/" -Recurse}}
[xml]$prefs=Get-Content "$run/Host/Config/Prefs.xml" -Raw
foreach($pair in @(@('volumeMaster','0'),@('volumeGame','0'),@('volumeMusic','0'),@('runInBackground','True'),@('devMode','False'),@('langFolderName','English'))){$node=$prefs.PrefsData.SelectSingleNode($pair[0]);if(!$node){$node=$prefs.CreateElement($pair[0]);[void]$prefs.PrefsData.AppendChild($node)};$node.InnerText=$pair[1]}
$prefs.Save("$run/Host/Config/Prefs.xml")
Get-FileHash -LiteralPath $Replay,"$run/Input.zip","$repo/1.6/Assemblies/MP_MeowOnlineShop.dll","$run/Host/Config/ModsConfig.xml","$run/Game/Mods/Probe/Assemblies/Meow.MoteLifecycleProbe.dll" | ConvertTo-Json | Set-Content "$run/inputs.json"
$flags=if($AsyncTime){' -moteasync'}else{''}
$line='-savedatafolder="'+$run+'/Host" -logFile "'+$run+'/host.log" -moteprobe -screen-fullscreen 0 -screen-width 800 -screen-height 600'+$flags
$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/host-process.json"
$watch=[Diagnostics.Stopwatch]::StartNew();$status='FAILED';$reason='timeout'
try{
 while($watch.Elapsed.TotalSeconds -lt 1200){
  if(Test-Path "$run/host.failed"){throw (Get-Content "$run/host.failed" -Raw)}
  if(Test-Path "$run/host.complete"){$status='DIAGNOSTIC_COMPLETE';$reason='45 seconds of native lifecycle observations';break}
  if($p.HasExited){throw 'Host exited unexpectedly'}
  Start-Sleep -Seconds 2
 }
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-moteprobe')){Stop-Process -Id $p.Id}}
 [ordered]@{status=$status;reason=$reason;seconds=$watch.Elapsed.TotalSeconds;async=[bool]$AsyncTime} | ConvertTo-Json | Set-Content "$run/result.json"
}
Get-Content "$run/result.json"
