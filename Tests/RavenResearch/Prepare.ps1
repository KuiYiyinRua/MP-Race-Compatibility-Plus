param([string]$RunName="R1")
$ErrorActionPreference='Stop'
$project=(Get-Location).Path;$game=(Resolve-Path '../..').Path;$data=(Resolve-Path '../../..').Path
$evidence=Join-Path $project 'BuildValidation/RavenResearch_20260921'
$run=Join-Path $evidence $RunName
New-Item -ItemType Directory -Force "$run/Game/Mods","$run/Single/Config","$run/Single/Saves" | Out-Null
if(!(Test-Path C:/RWRaven0921)){New-Item -ItemType Junction -Path C:/RWRaven0921 -Target $evidence | Out-Null}
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
'<ModMetaData><name>Raven research diagnostic</name><author>Local</author><packageId>local.meow.ravenresearchprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
Copy-Item Tests/RavenResearch/bin/Release/net48/Meow.RavenResearchProbe.dll "$run/Game/Mods/Probe/Assemblies/"
Copy-Item "$data/Config/*" "$run/Single/Config/" -Recurse
$node=$cfg.CreateElement('li');$node.InnerText='local.meow.ravenresearchprobe';[void]$cfg.ModsConfigData.activeMods.AppendChild($node);$cfg.Save("$run/Single/Config/ModsConfig.xml")
[xml]$prefs=Get-Content "$run/Single/Config/Prefs.xml" -Raw;$prefs.PrefsData.devMode='False';$prefs.PrefsData.runInBackground='True';$prefs.Save("$run/Single/Config/Prefs.xml")
@("$evidence/input.zip","$game/Version.txt","$game/Mods/3781005562/Assemblies/ZuoYao_RavenRace.dll","$run/Game/Mods/MP-meow-online-shop/RavenCompatibility/Assemblies/Meow.RavenCompatibility.dll","$run/Single/Config/ModsConfig.xml","$run/Game/Mods/Probe/Assemblies/Meow.RavenResearchProbe.dll") | Get-FileHash | ConvertTo-Json | Set-Content "$run/inputs.json"
Write-Output "Prepared $($ids.Count) mods"
