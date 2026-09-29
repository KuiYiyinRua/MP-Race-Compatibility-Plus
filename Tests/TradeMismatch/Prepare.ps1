param([string]$RunName="R1")
$ErrorActionPreference='Stop'
$project=(Get-Location).Path;$game=(Resolve-Path '../..').Path;$data=(Resolve-Path '../../..').Path
$evidence=Join-Path $project 'BuildValidation/TradeMismatch_20260921'
$run=Join-Path $evidence $RunName
New-Item -ItemType Directory -Force "$run/Game/Mods","$run/Host/Config","$run/Host/Saves","$run/Client" | Out-Null
if(!(Test-Path C:/RWTrade0921)){New-Item -ItemType Junction -Path C:/RWTrade0921 -Target $evidence | Out-Null}
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$name" -Target "$game/$name" | Out-Null}
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$name"){Copy-Item -LiteralPath "$game/$name" -Destination "$run/Game/$name"}}
[xml]$cfg=Get-Content "$data/Config/ModsConfig.xml" -Raw
$ids=@($cfg.ModsConfigData.activeMods.li);$index=@{}
foreach($dir in Get-ChildItem "$game/Mods" -Directory){$about=Join-Path $dir.FullName 'About/About.xml';if(Test-Path -LiteralPath $about){try{[xml]$meta=Get-Content -LiteralPath $about -Raw;$id=[string]$meta.ModMetaData.packageId;if($id){$index[$id]=$dir.FullName}}catch{}}}
foreach($id in $ids){
 if($id.StartsWith('ludeon.')){continue};if(!$index.ContainsKey($id)){throw "Missing $id"}
 $source=$index[$id];$dest=Join-Path "$run/Game/Mods" (Split-Path $source -Leaf)
 if($id -eq 'local.mp.meowonlineshop.sellslingshot'){
  New-Item -ItemType Directory -Force $dest | Out-Null
  foreach($name in @('About','Languages','Patches','MeleeAnimation','RavenCompatibility','RatkinUnderground','LoadFolders.xml','1.6')){Copy-Item -LiteralPath "$source/$name" -Destination "$dest/$name" -Recurse}
 }else{New-Item -ItemType Junction -Path $dest -Target $source | Out-Null}
}
New-Item -ItemType Directory -Force "$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies" | Out-Null
'<ModMetaData><name>Trade mismatch diagnostic</name><author>Local</author><packageId>local.meow.trademismatchprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
Copy-Item Tests/TradeMismatch/bin/Release/net48/Meow.TradeMismatchProbe.dll "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$data/Config/*" "$run/Host/Config/" -Recurse
$node=$cfg.CreateElement('li');$node.InnerText='local.meow.trademismatchprobe';[void]$cfg.ModsConfigData.activeMods.AppendChild($node);$cfg.Save("$run/Host/Config/ModsConfig.xml")
[xml]$prefs=Get-Content "$run/Host/Config/Prefs.xml" -Raw;$prefs.PrefsData.devMode='False';$prefs.PrefsData.runInBackground='True';$prefs.Save("$run/Host/Config/Prefs.xml")

Copy-Item -LiteralPath "$run/Host/Config" -Destination "$run/Client/Config" -Recurse -Force
Copy-Item -LiteralPath "$evidence/input.zip" -Destination "$run/input.zip"
foreach($peer in @('Host','Client')) {Copy-Item "$run/$peer/Config/ModsConfig.xml" "$run/$peer/InitialModsConfig.xml"}
(Get-FileHash "$run/Game/Mods/MP-meow-online-shop/1.6/Assemblies/MP_MeowOnlineShop.dll").Hash | Set-Content "$run/candidate.sha256"
Get-ChildItem "$run/Game/Mods/MP-meow-online-shop" -Recurse -File -Filter *.dll | Get-FileHash | ConvertTo-Json | Set-Content "$run/binaries.json"
Write-Output "Prepared $($ids.Count) mods: $run"
