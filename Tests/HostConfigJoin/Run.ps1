param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Core,[Parameter(Mandatory=$true)][string]$Turret,[switch]$OptOut)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Unique run directory required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Candidate","$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/User/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item "$game/$n" "$run/Game/$n"}}
foreach($n in @('2009463077','2934420800','Multiplayer')){New-Item -ItemType Junction -Path "$run/Game/Mods/$n" -Target "$game/Mods/$n" | Out-Null}
foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Patches','Languages','LoadFolders.xml')){Copy-Item -LiteralPath "$repo/$n" -Destination "$run/Game/Mods/Candidate/" -Recurse}
Copy-Item -LiteralPath $Core -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/MP_MeowOnlineShop.dll"
Copy-Item -LiteralPath $Turret -Destination "$run/Game/Mods/Candidate/1.6/Assemblies/Meow.TurretCombatSleep.dll"
Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.HostConfigJoinProbe.dll" "$run/Game/Mods/Probe/Assemblies/"
'<ModMetaData><name>Host config admission regression</name><author>Local</author><packageId>local.meow.hostconfigjoinprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
'<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.hostconfigjoinprobe</li></activeMods></ModsConfigData>' | Set-Content "$run/User/Config/ModsConfig.xml"
'<PrefsData><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/User/Config/Prefs.xml"
Get-ChildItem "$run/Game/Mods/Candidate" -Recurse -File | Where-Object Extension -eq '.dll' | Get-FileHash | ConvertTo-Json | Set-Content "$run/candidate-hashes.json"
$line='-savedatafolder="'+$run+'/User" -logFile "'+$run+'/player.log" -hostconfigjoinprobe -screen-fullscreen 0 -screen-width 800 -screen-height 600'
if($OptOut){$line+=' -mpmeowhotcfg=false'}
$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/process.json"
$watch=[Diagnostics.Stopwatch]::StartNew()
try{
 while(!$p.HasExited -and $watch.Elapsed.TotalSeconds -lt 300){Start-Sleep -Seconds 2}
 if(!$p.HasExited){throw 'Startup/window test timed out'}
 if(!(Test-Path "$run/User/result.txt")){throw 'No result; inspect player.log'}
 Get-Content "$run/User/result.txt"
 $records=Get-Content "$run/candidate-hashes.json" -Raw | ConvertFrom-Json
 foreach($record in $records){if((Get-FileHash -LiteralPath $record.Path).Hash -ne $record.Hash){throw 'Candidate changed during run'}}
}finally{
 if(!$p.HasExited){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-hostconfigjoinprobe')){Stop-Process -Id $p.Id}}
}
