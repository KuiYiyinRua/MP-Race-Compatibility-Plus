param([Parameter(Mandatory=$true)][string]$RunRoot,[string]$CacheFrom,[string]$Replay,[string]$Core,[string]$ExtraFlags='',[switch]$KeepLanguage)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
$game=(Resolve-Path "$repo/../..").Path
$data=(Resolve-Path "$game/..").Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Unique run required'}
New-Item -ItemType Directory -Force "$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies","$run/Host/Config" | Out-Null
foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$game/$n" | Out-Null}
foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$n"){Copy-Item "$game/$n" "$run/Game/$n"}}
foreach($d in Get-ChildItem "$game/Mods" -Directory){
 if($d.Name -in @('MP-meow-online-shop','NivarianMotherShip')){
  New-Item -ItemType Directory -Force "$run/Game/Mods/$($d.Name)" | Out-Null
  foreach($n in @('About','1.6','MeleeAnimation','RavenCompatibility','RatkinUnderground','Multiplayer','Royalty','NoRoyalty','Patches','Languages','LoadFolders.xml','Defs','Textures','Sounds','Assemblies')){if(Test-Path "$($d.FullName)/$n"){Copy-Item -LiteralPath "$($d.FullName)/$n" -Destination "$run/Game/Mods/$($d.Name)" -Recurse}}
 }else{New-Item -ItemType Junction -Path "$run/Game/Mods/$($d.Name)" -Target $d.FullName | Out-Null}
}
Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.PowerDiagnosis.dll" "$run/Game/Mods/Probe/Assemblies/"
if($Core){Copy-Item -LiteralPath $Core -Destination "$run/Game/Mods/MP-meow-online-shop/1.6/Assemblies/MP_MeowOnlineShop.dll"; $about=Join-Path (Split-Path $Core) 'About.xml';if(Test-Path -LiteralPath $about){Copy-Item -LiteralPath $about -Destination "$run/Game/Mods/MP-meow-online-shop/About/About.xml"}}
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination $run
if(!$Replay){$Replay="$repo/BuildValidation/PowerMapDiagnosis_20261016/Autosave-3.snapshot.zip"}
Copy-Item -LiteralPath $Replay -Destination "$run/Input.zip"
'<ModMetaData><name>Power diagnosis (isolated)</name><author>Local</author><packageId>local.meow.powerdiagnosis</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
Copy-Item "$data/Config/*.xml" "$run/Host/Config/"
Copy-Item "$repo/BuildValidation/PowerMapDiagnosis_20261016/ModsConfig.snapshot.xml" "$run/Host/Config/ModsConfig.xml"
[xml]$cfg=Get-Content "$run/Host/Config/ModsConfig.xml" -Raw
$node=$cfg.CreateElement('li');$node.InnerText='local.meow.powerdiagnosis';[void]$cfg.ModsConfigData.activeMods.AppendChild($node);$cfg.Save("$run/Host/Config/ModsConfig.xml")
foreach($name in @('HugsLib','OgreStack','MissileGirl')){if(Test-Path "$data/$name"){Copy-Item "$data/$name" "$run/Host/" -Recurse}}
[xml]$prefs=Get-Content "$run/Host/Config/Prefs.xml" -Raw
foreach($pair in @(@('volumeMaster','0'),@('volumeGame','0'),@('volumeMusic','0'),@('runInBackground','True'),@('devMode','False'),@('langFolderName','English'))){if($KeepLanguage -and $pair[0] -eq 'langFolderName'){continue};$node=$prefs.PrefsData.SelectSingleNode($pair[0]);if(!$node){$node=$prefs.CreateElement($pair[0]);[void]$prefs.PrefsData.AppendChild($node)};$node.InnerText=$pair[1]}
$prefs.Save("$run/Host/Config/Prefs.xml")
if($CacheFrom -and (Test-Path "$CacheFrom/DefLoadCache")){Copy-Item "$CacheFrom/DefLoadCache" "$run/Host/" -Recurse}
Get-ChildItem "$run/Game/Mods/MP-meow-online-shop","$run/Game/Mods/NivarianMotherShip" -Recurse -File | Where-Object Extension -eq '.dll' | Get-FileHash | ConvertTo-Json | Set-Content "$run/assemblies.json"
Get-FileHash "$run/Input.zip","$run/Host/Config/ModsConfig.xml","$run/Game/Mods/Probe/Assemblies/Meow.PowerDiagnosis.dll" | ConvertTo-Json | Set-Content "$run/inputs.json"
$line='-savedatafolder="'+$run+'/Host" -logFile "'+$run+'/host.log" -powerdiagnosis -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$ExtraFlags
$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru
Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/process.json"
Get-Content "$run/process.json"
