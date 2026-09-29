param([Parameter(Mandatory=$true)][string]$RunRoot,[Parameter(Mandatory=$true)][string]$Candidate,[Parameter(Mandatory=$true)][string]$Batch,[string]$Replay,[switch]$FreshFromReplay,[switch]$Features,[int]$Port=0)
$ErrorActionPreference='Stop'
$PSDefaultParameterValues['Set-Content:Encoding']='UTF8'
$run=[IO.Path]::GetFullPath($RunRoot)
$game=(Resolve-Path '../..').Path
if(Test-Path -LiteralPath $run){throw 'Run already exists'}
New-Item -ItemType Directory -Force "$run/Game/Mods","$run/Host/Config","$run/Client/Config","$run/Candidate","$run/Driver" | Out-Null
Copy-Item -LiteralPath $Candidate -Destination "$run/Candidate/MP_MeowOnlineShop.dll"
Copy-Item "$PSScriptRoot/*.cs","$PSScriptRoot/*.csproj","$PSScriptRoot/*.ps1" -Destination "$run/Driver"
foreach($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')){New-Item -ItemType Junction -Path "$run/Game/$name" -Target "$game/$name" | Out-Null}
foreach($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')){if(Test-Path "$game/$name"){Copy-Item -LiteralPath "$game/$name" -Destination "$run/Game/$name"}}
$modFolders=@('2009463077','2934420800','Multiplayer','839005762','2988801276','3256974620','3626456960','3665997350','3588393755')
if($Replay){$modFolders=@(Get-ChildItem "$game/Mods" -Directory | Where-Object Name -ne 'MP-meow-online-shop' | Select-Object -ExpandProperty Name)}
foreach($name in $modFolders){New-Item -ItemType Junction -Path "$run/Game/Mods/$name" -Target "$game/Mods/$name" | Out-Null}
foreach($name in @('Candidate','Probe')){New-Item -ItemType Directory -Force "$run/Game/Mods/$name/About","$run/Game/Mods/$name/Assemblies" | Out-Null}
foreach($payload in @('1.6','Patches','Languages','MeleeAnimation','RavenCompatibility','RatkinUnderground')){
 Copy-Item -LiteralPath $payload -Destination "$run/Game/Mods/Candidate/$payload" -Recurse
}
Copy-Item 'LoadFolders.xml' "$run/Game/Mods/Candidate/"
Copy-Item "$run/Candidate/MP_MeowOnlineShop.dll" "$run/Game/Mods/Candidate/1.6/Assemblies/" -Force
Copy-Item "$PSScriptRoot/bin/Release/net48/Meow.MiliraActionsProbe.dll" "$run/Game/Mods/Probe/Assemblies/"
Copy-Item $Batch "$run/Game/Mods/Candidate/1.6/Assemblies/" -Force
Copy-Item 'About/About.xml' "$run/Game/Mods/Candidate/About/"
'<ModMetaData><name>Trade window diagnostic</name><author>Local</author><packageId>local.meow.miliraactionsprobe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run/Game/Mods/Probe/About/About.xml"
$cfg='<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>erdelf.humanoidalienraces</li><li>ancot.ancotlibrary</li><li>ancot.milirarace</li><li>sleepycot.wingsofdemocracy</li><li>ariandel.ariandellibrary</li><li>ariandel.miliraimperium</li><li>rwmt.multiplayer</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.meow.miliraactionsprobe</li></activeMods></ModsConfigData>'
if($Replay){
 Copy-Item -LiteralPath $Replay "$run/input.zip"
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip=[IO.Compression.ZipFile]::OpenRead("$run/input.zip")
 try{$reader=[IO.StreamReader]::new($zip.GetEntry('info').Open());[xml]$info=$reader.ReadToEnd();$reader.Dispose()}finally{$zip.Dispose()}
 [xml]$configDoc=$cfg
 $activeNode=$configDoc.SelectSingleNode('/ModsConfigData/activeMods');$activeNode.RemoveAll()
 foreach($id in @($info.ReplayInfo.modIds.li)+@('local.meow.miliraactionsprobe')){$li=$configDoc.CreateElement('li');$li.InnerText=$id;$null=$activeNode.AppendChild($li)}
 $cfg=$configDoc.OuterXml
}
foreach($peer in @('Host','Client')){
 if($Replay){Copy-Item '../../../Config/*' "$run/$peer/Config/" -Recurse -Force}
 $cfg | Set-Content "$run/$peer/Config/ModsConfig.xml"
 '<PrefsData><volumeMaster>0</volumeMaster><volumeGame>0</volumeGame><volumeMusic>0</volumeMusic><volumeAmbient>0</volumeAmbient><volumeUI>0</volumeUI><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run/$peer/Config/Prefs.xml"
}
(Get-FileHash "$run/Candidate/MP_MeowOnlineShop.dll").Hash | Set-Content "$run/candidate.sha256"
$hashPaths=@("$run/Candidate/MP_MeowOnlineShop.dll","$run/Game/Mods/Probe/Assemblies/Meow.MiliraActionsProbe.dll","$game/Mods/Multiplayer/1.6/AssembliesCustom/Multiplayer.dll","$run/Host/Config/ModsConfig.xml","$run/Client/Config/ModsConfig.xml")+@(Get-ChildItem "$run/Game/Mods/Candidate" -Recurse -File | Select-Object -ExpandProperty FullName)
foreach($id in @('3256974620','3626456960','3588393755','2988801276','3665997350')){$hashPaths+=@(Get-ChildItem "$game/Mods/$id/1.6" -Recurse -Filter '*.dll' | Select-Object -ExpandProperty FullName)}
if($Replay){$hashPaths+="$run/input.zip"}
Get-FileHash -LiteralPath $hashPaths | ConvertTo-Json | Set-Content "$run/inputs.json"
$owned=@();$status='FAILED';$reason='not completed';$watch=[Diagnostics.Stopwatch]::StartNew()
function ReadLog($path){if(!(Test-Path -LiteralPath $path)){return ''};$stream=$null;$reader=$null;try{$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);return $reader.ReadToEnd()}finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}}
function StartPeer($peer,$extra){if($Port -gt 0){$extra+=(' -miliraport='+$Port);$extra=$extra.Replace('30997',[string]$Port)};if($Features){$extra+=' -milirafeatures';$extra=$extra.Replace('30997','30998')};$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -miliraactionsprobe -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$extra;$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
try{
 $hostArgs='-quicktest';if($Replay -and !$FreshFromReplay){$hostArgs='-milirareplay'}
 $owned+=StartPeer Host $hostArgs;$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 3600){
  $h=ReadLog "$run/host.log";$c=ReadLog "$run/client.log"
  if(($h+$c) -match 'MILIRA_ACTIONS FAILED|REQUIRED_TARGET_FAILED|REQUIRED_TARGET_FAILURE trade auto-open|Prepatcher Error: Fatal|Error in static constructor of MiliraActionsProbe|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception|Exception in MpTick|Error while executing|World cmd exception|Map cmd exception'){throw 'Runtime/setup failure; see logs'}
  if(!$clientStarted -and $h -match '(?m)^Server started\.'){$owned+=StartPeer Client '-twclient -connect=127.0.0.1:30997';$clientStarted=$true}
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
   $status='PASS';$reason='Milira action receipts';break
  }
  foreach($p in $owned){if($p.HasExited){throw 'Early peer exit'}}
  Start-Sleep -Seconds 3
 }
 if($status -ne 'PASS'){throw 'Timeout'}
}catch{$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup'
 foreach($p in $owned){if(!$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-miliraactionsprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds;replay=$Replay;freshFromReplay=[bool]$FreshFromReplay;features=[bool]$Features;scope=$(if($Features){'Bishop II slider/style/color three repetitions; nine airstrike kinds; over 10000 shared ticks'}else{'Four native console summon buttons, three repetitions each, 10000 shared ticks'})} | ConvertTo-Json | Set-Content "$run/result.json"
}
if($status -ne 'PASS'){throw $reason}
