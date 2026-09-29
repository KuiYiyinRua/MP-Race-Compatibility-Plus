param([Parameter(Mandatory=$true)][string]$RunRoot,[string]$InputReplay,[switch]$Full,[switch]$SyncTime,[int]$Port=30932,[int]$Ticks=120000)
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$game=(Resolve-Path (Join-Path $project '..\..')).Path
$run=[IO.Path]::GetFullPath($RunRoot)
if(Test-Path -LiteralPath $run){throw 'Use a new run directory'}
New-Item -ItemType Directory -Force "$run\Game\Mods","$run\Host\Config","$run\Host\Saves","$run\Client\Config","$run\Candidate" | Out-Null
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run\Game\$name" -Target "$game\$name" | Out-Null}
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){Copy-Item -LiteralPath "$game\$name" -Destination "$run\Game\$name"}
if($Full){
 [xml]$fullConfig=Get-Content "$game/../Config/ModsConfig.xml" -Raw
 $fullIds=@($fullConfig.ModsConfigData.activeMods.li | ForEach-Object {[string]$_})
 $index=@{}
 foreach($dir in Get-ChildItem "$game/Mods" -Directory){
  $about="$($dir.FullName)/About/About.xml"
  if(Test-Path $about){try{[xml]$meta=Get-Content $about -Raw;$index[[string]$meta.ModMetaData.packageId]=$dir.FullName}catch{}}
 }
 foreach($id in $fullIds){
  if($id.StartsWith('ludeon.',[StringComparison]::OrdinalIgnoreCase)){continue}
  if(!$index.ContainsKey($id)){throw "Missing active mod $id"}
  $src=$index[$id];$dest="$run/Game/Mods/$(Split-Path $src -Leaf)"
  if($id -eq 'local.mp.meowonlineshop.sellslingshot'){
   New-Item -ItemType Directory $dest | Out-Null
   foreach($name in @('About','Languages','Patches','MeleeAnimation','RavenCompatibility','RatkinUnderground','LoadFolders.xml','1.6')){Copy-Item "$src/$name" "$dest/$name" -Recurse}
  }else{New-Item -ItemType Junction -Path $dest -Target $src | Out-Null}
 }
 foreach($peer in @('Host','Client')){Copy-Item "$game/../Config/*" "$run/$peer/Config/" -Recurse}
}else{
 foreach($name in @('2009463077','2934420800','3610069762','Multiplayer')){Copy-Item -LiteralPath "$game\Mods\$name" -Destination "$run\Game\Mods\$name" -Recurse}
}
foreach($name in @('Candidate','Probe')){New-Item -ItemType Directory -Force "$run\Game\Mods\$name\About","$run\Game\Mods\$name\Assemblies" | Out-Null}
Copy-Item -LiteralPath "$project\Source\FactionPurge\bin\Release\net48\Meow.FactionPurge.dll" -Destination "$run\Candidate"
Copy-Item -LiteralPath "$run\Candidate\Meow.FactionPurge.dll" -Destination "$run\Game\Mods\Candidate\Assemblies"
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net48\Meow.FactionPurgeProbe.dll" -Destination "$run\Game\Mods\Probe\Assemblies"
'<ModMetaData><name>MP Faction Purge candidate</name><author>Local</author><packageId>local.meow.factionpurge</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run\Game\Mods\Candidate\About\About.xml"
'<ModMetaData><name>Purge probe</name><author>Local</author><packageId>local.meow.purgeprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run\Game\Mods\Probe\About\About.xml"
$ids=@('brrainz.harmony','zetrith.prepatcher','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','rwmt.multiplayer','palpha.godhands','local.meow.factionpurge','local.meow.purgeprobe')
if($Full){$ids=$fullIds+@('local.meow.factionpurge','local.meow.purgeprobe')}
$config='<ModsConfigData><version>1.6.4850</version><activeMods>'+($ids | ForEach-Object {'<li>'+$_+'</li>'})+'</activeMods></ModsConfigData>'
foreach($peer in @('Host','Client')){
 $config | Set-Content "$run\$peer\Config\ModsConfig.xml"
 '<PrefsData><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>True</devMode></PrefsData>' | Set-Content "$run\$peer\Config\Prefs.xml"
}
$baseline="$project\BuildValidation\GodHands_20260911\VerifiedSoak\Host\Saves\autostart.rws"
if($Full){$baseline='C:/RWPerf0913/Full316-NoDPA-List-120k-R1/Host/Saves/DiplomacyBaseline.rws'}
Copy-Item -LiteralPath $baseline -Destination "$run\original-baseline.rws"
$doc=New-Object System.Xml.XmlDocument
$doc.Load($baseline)
foreach($node in @($doc.SelectNodes('//components/li'))){
 if((!$Full -and ($node.GetAttribute('Class').StartsWith('MP_MeowOnlineShop.') -or $node.GetAttribute('Class') -eq 'GHState')) -or ($Full -and $node.GetAttribute('Class') -eq 'DiplomacyProbeState')){$node.ParentNode.RemoveChild($node) | Out-Null}
}
$modIds=$doc.SelectSingleNode('/savegame/meta/modIds');$modIds.RemoveAll()
foreach($id in $ids){$li=$doc.CreateElement('li');$li.InnerText=$id;$modIds.AppendChild($li) | Out-Null}
$doc.Save("$run\Host\Saves\PurgeBaseline.rws")
Get-ChildItem "$run\Game\Mods" -Recurse -File | Where-Object {$_.Extension -eq '.dll' -or $_.Name -eq 'About.xml'} | Get-FileHash | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$run\assembly-inputs.json"
Get-FileHash "$run\Host\Config\ModsConfig.xml","$run\Client\Config\ModsConfig.xml","$run\Host\Saves\PurgeBaseline.rws","$run\original-baseline.rws" | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$run\data-inputs.json"
$flags='-purgeprobe'
if($InputReplay){Copy-Item -LiteralPath $InputReplay -Destination "$run\input.zip";$flags='-purgereload -purgeinput="'+$run+'\input.zip"';Get-FileHash "$run\input.zip" | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$run\replay-input.json"}
if($InputReplay){$flags+=' -purgeport='+$Port+' -purgeticks='+$Ticks;if($SyncTime){$flags+=' -purgesynctime'}}
$line='-savedatafolder="'+$run+'\Host" -logFile "'+$run+'\host.log" '+$flags+' -screen-fullscreen 0 -screen-width 960 -screen-height 640'
$process=Start-Process -FilePath "$run\Game\RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run\Game" -WindowStyle Hidden -PassThru
Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)" | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$run\host-process.json"
Write-Output "Run=$run PID=$($process.Id)"
