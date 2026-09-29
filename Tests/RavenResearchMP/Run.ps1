param([Parameter(Mandatory=$true)][string]$GameRoot,[Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Candidate,[switch]$PrepareOnly)
$ErrorActionPreference='Stop';$run=[IO.Path]::GetFullPath($RunRoot)
if(!(Test-Path "$run/inputs.json")){
 New-Item -ItemType Directory -Force "$run/Game/Mods","$run/Host/Config","$run/Client/Config" | Out-Null
 foreach($n in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$n" -Target "$GameRoot/$n" | Out-Null}
 foreach($n in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$GameRoot/$n"){Copy-Item -LiteralPath "$GameRoot/$n" -Destination "$run/Game/$n"}}
 $ids=@('zetrith.prepatcher','brrainz.harmony','ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey','rwmt.multiplayer','erdelf.humanoidalienraces','chezhou.chezhoulib.lib','zuoyao.ravenrace','local.mp.meowonlineshop.sellslingshot','local.meow.ravenresearchpair')
 $index=@{};foreach($dir in Get-ChildItem "$GameRoot/Mods" -Directory){$about=Join-Path $dir.FullName 'About/About.xml';if(Test-Path $about){try{[xml]$m=Get-Content $about -Raw;if($m.ModMetaData.packageId){$index[[string]$m.ModMetaData.packageId]=$dir.FullName}}catch{}}}
 foreach($id in $ids){if($id.StartsWith('ludeon.') -or $id -eq 'local.meow.ravenresearchpair'){continue};if(!$index.ContainsKey($id)){throw "Missing $id"};$source=$index[$id];$dest=Join-Path "$run/Game/Mods" (Split-Path $source -Leaf)
  if($id -eq 'local.mp.meowonlineshop.sellslingshot'){New-Item -ItemType Directory -Force $dest | Out-Null;foreach($n in @('About','Languages','Patches','RavenCompatibility','LoadFolders.xml','1.6')){Copy-Item -LiteralPath "$source/$n" -Destination "$dest/$n" -Recurse};Copy-Item -LiteralPath $Candidate -Destination "$dest/RavenCompatibility/Assemblies/Meow.RavenCompatibility.dll"}
  else{New-Item -ItemType Junction -Path $dest -Target $source | Out-Null}
 }
 New-Item -ItemType Directory -Force "$run/Game/Mods/Probe/About","$run/Game/Mods/Probe/Assemblies" | Out-Null
 '<ModMetaData><name>Raven research pair</name><author>Local</author><packageId>local.meow.ravenresearchpair</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
 Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.RavenResearchMPProbe.dll" "$run/Game/Mods/Probe/Assemblies/"
 $cfg='<ModsConfigData><version>1.6.4850</version><activeMods>'+ (($ids | ForEach-Object {'<li>'+$_+'</li>'}) -join '')+'</activeMods></ModsConfigData>'
 foreach($peer in @('Host','Client')){$cfg | Set-Content "$run/$peer/Config/ModsConfig.xml";Copy-Item "$run/$peer/Config/ModsConfig.xml" "$run/$peer/InitialModsConfig.xml";'<PrefsData><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode><screenWidth>800</screenWidth><screenHeight>600</screenHeight><fullscreen>False</fullscreen></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"}
 @("$run/Game/Mods/MP-meow-online-shop/RavenCompatibility/Assemblies/Meow.RavenCompatibility.dll","$run/Game/Mods/Probe/Assemblies/Meow.RavenResearchMPProbe.dll","$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml","$GameRoot/Mods/3781005562/Assemblies/ZuoYao_RavenRace.dll") | Get-FileHash | ConvertTo-Json | Set-Content "$run/inputs.json"
}
if($PrepareOnly){return}
$owned=@();$status='FAILED';$reason='not completed';$watch=[Diagnostics.Stopwatch]::StartNew()
function ReadLiveLog($path){if(!(Test-Path -LiteralPath $path)){return ''};$stream=$null;$reader=$null;try{$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);return $reader.ReadToEnd()}catch [IO.IOException]{return ''}finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}}
function StartPeer($peer,$extra){$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -rrpair -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$extra;$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Normal -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
try{
 $owned+=StartPeer Host '-quicktest';$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 1500){
  $h=ReadLiveLog "$run/host.log";$c=ReadLiveLog "$run/client.log"
  if(($h+$c) -match 'RAVEN_PAIR FAILED|Prepatcher Error: Fatal|Error in static constructor of MP_MeowOnlineShop.RavenCompatibilityBootstrap|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception'){throw 'Runtime/setup failure; see logs'}
  if(!$clientStarted -and $h -match '(?m)^Server started\.'){$owned+=StartPeer Client '-rrclient -connect=127.0.0.1:30992';$clientStarted=$true}
  if((Test-Path "$run/host.complete") -and (Test-Path "$run/client.complete")){if([IO.File]::ReadAllText("$run/host.receipt") -ne [IO.File]::ReadAllText("$run/client.receipt")){throw 'Receipts differ'};$status='PASS';$reason='10k shared ticks, paired native research and resource conservation';break}
  foreach($p in $owned){if($p.HasExited){throw 'Early peer exit'}}
  Start-Sleep -Seconds 3
 }
 if($status -ne 'PASS'){throw 'Timeout'}
 foreach($x in Get-Content "$run/inputs.json" -Raw | ConvertFrom-Json){
  if((Get-FileHash -LiteralPath $x.Path).Hash -eq $x.Hash){continue}
  if(!(Split-Path $x.Path -Leaf).Equals('ModsConfig.xml')){throw "Binary input hash changed: $($x.Path)"}
  $initial=Join-Path (Split-Path (Split-Path $x.Path -Parent) -Parent) 'InitialModsConfig.xml'
  if((Get-FileHash -LiteralPath $initial).Hash -ne $x.Hash){throw 'Initial configuration hash changed'}
  [xml]$before=Get-Content -LiteralPath $initial -Raw;[xml]$after=Get-Content -LiteralPath $x.Path -Raw
  if(($before.ModsConfigData.activeMods.li -join '|') -cne ($after.ModsConfigData.activeMods.li -join '|')){throw 'Active package IDs or order changed'}
  if(([string]$after.ModsConfigData.version).Split(' ')[0] -cne [string]$before.ModsConfigData.version){throw 'Game version changed'}
  foreach($node in $after.ModsConfigData.ChildNodes){if($node.Name -notin @('version','activeMods','knownExpansions')){throw 'Unexpected configuration mutation'}}
  Write-Output "Verified native config normalization only: $($x.Path)"
 }
}catch{$status='FAILED';$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup';foreach($p in $owned){if(!$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-rrpair')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds} | ConvertTo-Json | Set-Content "$run/result.json"
}
if($status -ne 'PASS'){throw $reason}
