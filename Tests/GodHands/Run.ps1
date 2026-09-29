param(
    [Parameter(Mandatory=$true)][string]$CandidatePath,
    [Parameter(Mandatory=$true)][string]$BaselineSave,
    [Parameter(Mandatory=$true)][string]$RunRoot,
    [int]$TimeoutSeconds = 2700
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$gameRoot = (Resolve-Path (Join-Path $projectRoot '..\..')).Path
$candidate = (Resolve-Path -LiteralPath $CandidatePath).Path
$baseline = (Resolve-Path -LiteralPath $BaselineSave).Path
$run = [IO.Path]::GetFullPath($RunRoot)
if (Test-Path -LiteralPath $run) { throw 'Use a new run directory; evidence is never overwritten.' }
$candidateHash = (Get-FileHash -LiteralPath $candidate).Hash
& dotnet build (Join-Path $PSScriptRoot 'Probe.csproj') -c Release --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }
New-Item -ItemType Directory -Force "$run\Game\Mods", "$run\Host\Config", "$run\Client\Config", "$run\Host\Saves", "$run\Candidate" | Out-Null
foreach ($name in @('Data','MonoBleedingEdge','RimWorldWin64_Data')) {
    New-Item -ItemType Junction -Path "$run\Game\$name" -Target "$gameRoot\$name" | Out-Null
}
foreach ($name in @('RimWorldWin64.exe','UnityPlayer.dll','UnityCrashHandler64.exe','steam_appid.txt','Version.txt')) {
    Copy-Item -LiteralPath "$gameRoot\$name" -Destination "$run\Game\$name"
}
foreach ($name in @('2009463077','2934420800','3610069762','Multiplayer')) {
    Copy-Item -LiteralPath "$gameRoot\Mods\$name" -Destination "$run\Game\Mods\$name" -Recurse
}
foreach ($name in @('Candidate','Probe')) {
    New-Item -ItemType Directory -Force "$run\Game\Mods\$name\About", "$run\Game\Mods\$name\Assemblies" | Out-Null
}
Copy-Item -LiteralPath $candidate -Destination "$run\Candidate\MP_MeowOnlineShop.dll"
Copy-Item -LiteralPath "$run\Candidate\MP_MeowOnlineShop.dll" -Destination "$run\Game\Mods\Candidate\Assemblies"
Copy-Item -LiteralPath "$PSScriptRoot\bin\Release\net48\Meow.GodHandProbe.dll" -Destination "$run\Game\Mods\Probe\Assemblies"
'<ModMetaData><name>MP Race Compatibility Plus</name><author>Local</author><packageId>local.mp.meowonlineshop.sellslingshot</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run\Game\Mods\Candidate\About\About.xml"
'<ModMetaData><name>GodHand Probe</name><author>Local</author><packageId>local.godhand.probe</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>' | Set-Content "$run\Game\Mods\Probe\About\About.xml"
$config = '<ModsConfigData><version>1.6.4850</version><activeMods><li>brrainz.harmony</li><li>zetrith.prepatcher</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>ludeon.rimworld.ideology</li><li>ludeon.rimworld.biotech</li><li>ludeon.rimworld.anomaly</li><li>ludeon.rimworld.odyssey</li><li>rwmt.multiplayer</li><li>palpha.godhands</li><li>local.mp.meowonlineshop.sellslingshot</li><li>local.godhand.probe</li></activeMods></ModsConfigData>'
foreach ($peer in @('Host','Client')) {
    $config | Set-Content "$run\$peer\Config\ModsConfig.xml"
    '<PrefsData><langFolderName>English</langFolderName><runInBackground>True</runInBackground><devMode>False</devMode></PrefsData>' | Set-Content "$run\$peer\Config\Prefs.xml"
}
Copy-Item -LiteralPath $baseline -Destination "$run\Host\Saves\autostart.rws"
Get-ChildItem "$run\Game\Mods" -Recurse -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'About.xml' } |
    Get-FileHash | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$run\assembly-inputs.json"
Get-FileHash "$run\Host\Config\ModsConfig.xml", "$run\Client\Config\ModsConfig.xml", "$run\Host\Saves\autostart.rws" |
    Select-Object Path,Hash | ConvertTo-Json | Set-Content "$run\data-inputs.json"
$owned = @()
$clock = [Diagnostics.Stopwatch]::StartNew()
$status = 'FAILED'
$reason = 'Controller did not complete'
function Start-Peer([string]$peer, [string]$flags) {
    $line = '-savedatafolder="' + $run + '\' + $peer + '" -logFile "' + $run + '\' + $peer.ToLowerInvariant() + '.log" -ghprobe -ghsoak -screen-fullscreen 0 -screen-width 960 -screen-height 640 ' + $flags
    $process = Start-Process -FilePath "$run\Game\RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run\Game" -WindowStyle Hidden -PassThru
    Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)" | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine |
        ConvertTo-Json | Set-Content "$run\$peer-process.json"
    return $process
}
try {
    $owned += Start-Peer 'Host' ''
    $clientStarted = $false
    while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $hostLog = if (Test-Path "$run\host.log") { Get-Content "$run\host.log" -Raw } else { '' }
        $clientLog = if (Test-Path "$run\client.log") { Get-Content "$run\client.log" -Raw } else { '' }
        if (($hostLog + $clientLog) -match 'GHPROBE FAILED|Desynced after|Sync Error|Inconsistent player|PacketReadException') { throw 'Runtime failure; inspect peer logs' }
        foreach ($process in $owned) { if ($process.HasExited) { throw 'Peer exited before completion' } }
        if (!$clientStarted -and $hostLog -match '(?m)^Server started\.') {
            $owned += Start-Peer 'Client' '-ghclient -connect=127.0.0.1:30794'
            $clientStarted = $true
        }
        if ((Test-Path "$run\host.complete") -and (Test-Path "$run\client.complete")) {
            if (!$hostLog.Contains("hash=$candidateHash") -or !$clientLog.Contains("hash=$candidateHash")) { throw 'Loaded-candidate hash record missing' }
            foreach ($log in @($hostLog,$clientLog)) {
                if ($log -notmatch 'GHPROBE COMPLETE desynced=False sharedMapTicks=(\d+)' -or [int]$Matches[1] -lt 120000) { throw 'Insufficient shared map simulation ticks' }
                if (!$log.Contains('GHPROBE ASSERT stage=18') -or !$log.Contains('GHPROBE HELD_SNAPSHOT_LOADED')) { throw 'Target assertions missing' }
            }
            $hostBaseline = [regex]::Match($hostLog,'GHPROBE BASELINE map=\d+ tick=\d+ rand=\d+').Value
            $clientBaseline = [regex]::Match($clientLog,'GHPROBE BASELINE map=\d+ tick=\d+ rand=\d+').Value
            if (!$hostBaseline -or $hostBaseline -ne $clientBaseline) { throw 'Frozen baseline differs' }
            if ((Get-FileHash "$run\Game\Mods\Candidate\Assemblies\MP_MeowOnlineShop.dll").Hash -ne $candidateHash) { throw 'Candidate changed during run' }
            $status = 'PASS'; $reason = 'Both peers passed actions and 120000 shared map simulation ticks'; break
        }
        Start-Sleep -Seconds 5
    }
    if ($status -ne 'PASS') { throw 'Controller timeout' }
} catch { $reason = $_.Exception.Message; throw }
finally {
    Set-Content "$run\quit" 'controller cleanup'
    foreach ($process in $owned) {
        if (!$process.WaitForExit(20000)) {
            $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($process.Id)"
            if ($current -and $current.CommandLine.Contains($run) -and $current.CommandLine.Contains('-ghprobe')) {
                Stop-Process -Id $process.Id
            }
        }
    }
    [ordered]@{status=$status;reason=$reason;candidate=$candidateHash;elapsedSeconds=$clock.Elapsed.TotalSeconds;scope='isolated single-map, async off, multifaction off, traces off'} |
        ConvertTo-Json | Set-Content "$run\result.json"
}
