param(
    [string]$GameRoot,
    [string]$WorkspaceRoot,
    [string]$ModList,
    [string]$ReplayFile,
    [string]$CandidateDll,
    [string]$FixedHarnessDll,
    [string]$HostDir,
    [string]$ClientDir,
    [int]$Port = 30680,
    [int]$ClientTicks = 12000,
    [int]$HostTicks = 17000
)

$ErrorActionPreference = 'Stop'

$gameExe = Join-Path $GameRoot 'RimWorldWin64.exe'
$assembliesDir = Join-Path $WorkspaceRoot '1.6\Assemblies'
$deployedDll = Join-Path $assembliesDir 'MP_MeowOnlineShop.dll'
$deployedHarness = Join-Path $assembliesDir 'MP_MeowOnlineShop.LongRunHarness.dll'
$hostLog = Join-Path $HostDir 'Player-host.smoke.log'
$clientLog = Join-Path $ClientDir 'Player-client.smoke.log'
$readyMarker = Join-Path $WorkspaceRoot ("ready-$Port.marker")
$resultDir = Join-Path $WorkspaceRoot ("BuildValidation\GravshipLandingSmoke_$Port")

function Assert-Path([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label missing: $Path"
    }
}

Assert-Path $GameRoot 'GameRoot'
Assert-Path $ModList 'ModList'
Assert-Path $ReplayFile 'ReplayFile'
Assert-Path $CandidateDll 'CandidateDll'
Assert-Path $FixedHarnessDll 'FixedHarnessDll'
Assert-Path $HostDir 'HostDir'
Assert-Path $ClientDir 'ClientDir'

New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $HostDir 'Config') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $ClientDir 'Config') | Out-Null

Copy-Item -LiteralPath $ModList -Destination (Join-Path $HostDir 'Config\ModsConfig.xml') -Force
Copy-Item -LiteralPath $ModList -Destination (Join-Path $ClientDir 'Config\ModsConfig.xml') -Force
Copy-Item -LiteralPath $CandidateDll -Destination $deployedDll -Force
Copy-Item -LiteralPath $FixedHarnessDll -Destination $deployedHarness -Force

if (Test-Path -LiteralPath $readyMarker) {
    Remove-Item -LiteralPath $readyMarker -Force
}

$hostHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $deployedDll).Hash
$harnessHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $deployedHarness).Hash
Set-Content -LiteralPath (Join-Path $resultDir 'hashes.txt') -Value @(
    "deployedDll=$hostHash",
    "harness=$harnessHash"
) -Encoding ASCII

function Start-TestInstance {
    param(
        [string]$Role,
        [string]$SaveDataDir,
        [string]$LogPath,
        [int]$Ticks
    )

    $arguments = @(
        '-screen-fullscreen', '0',
        '-screen-width', '960',
        '-screen-height', '540',
        "-savedatafolder=$SaveDataDir",
        '-logfile', $LogPath,
        "-mpautotestrole=$Role",
        "-mpautotestport=$Port",
        "-mpautotestticks=$Ticks",
        "-mpautotestpeerticks=$ClientTicks",
        '-mpautotesttraces=false',
        '-mpautotestcaravanui=false',
        '-mpautotestcaravanmassui=false',
        '-mpautotestcompatui=false',
        '-mpautotestrigormortis=false',
        '-mpautotestalertprobe=false',
        '-mpautotestrjwaddons=false',
        '-mpautotesttraderincidents=false',
        '-mpautotestperspectiveshift=false',
        '-mpautotestgravshipsync=true',
        '-mpautotesttwomapviews=false',
        '-mpautotestasync=true',
        "-mpautotestreadymarker=$readyMarker",
        '-mpautotestautoconnectexactmatch=true'
    )
    if ($Role -eq 'client') {
        $arguments += "-connect=127.0.0.1:$Port"
    }
    else {
        $arguments += "-mpautotestreplay=$ReplayFile"
    }

    $launchArguments = @($arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_ -replace '"', '\"') + '"'
        }
        else {
            $_
        }
    })

    $process = Start-Process -FilePath $gameExe -ArgumentList $launchArguments -WindowStyle Minimized -PassThru
    "STARTED $Role pid=$($process.Id)"
    return $process
}

function Wait-LogPattern {
    param(
        [string]$Path,
        [string]$Pattern,
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            return $false
        }
        if (Test-Path -LiteralPath $Path) {
            if (Select-String -LiteralPath $Path -Pattern $Pattern -Quiet) {
                return $true
            }
        }
        Start-Sleep -Seconds 10
    }
    return $false
}

$hostProcess = Start-TestInstance -Role 'host' -SaveDataDir $HostDir -LogPath $hostLog -Ticks $HostTicks
$hostReady = Wait-LogPattern -Path $hostLog -Pattern 'Server started\.' -Process $hostProcess -TimeoutSeconds 3600
if (-not $hostReady) {
    "RESULT host_not_ready exited=$($hostProcess.HasExited)"
    if (-not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force }
    exit 2
}

$clientProcess = Start-TestInstance -Role 'client' -SaveDataDir $ClientDir -LogPath $clientLog -Ticks $ClientTicks

$clientDone = $false
$deadline = (Get-Date).AddHours(2)
while ((Get-Date) -lt $deadline) {
    $hostProcess.Refresh()
    $clientProcess.Refresh()
    if ($clientProcess.HasExited -and $hostProcess.HasExited) {
        break
    }
    if (Test-Path -LiteralPath $clientLog) {
        if (Select-String -LiteralPath $clientLog -Pattern '\[MP-AutoTest\] (COMPLETE|FAILED)' -Quiet) {
            $clientDone = $true
            break
        }
    }
    Start-Sleep -Seconds 10
}

if (-not $clientDone) {
    if (-not $clientProcess.HasExited) { Stop-Process -Id $clientProcess.Id -Force }
    if (-not $hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force }
    "RESULT timeout"
    exit 3
}

$clientWait = $clientProcess.WaitForExit(300000)
$hostWait = $hostProcess.WaitForExit(300000)

$resultLines = @()
foreach ($entry in @(@('host', $hostLog), @('client', $clientLog))) {
    $role = $entry[0]
    $path = $entry[1]
    if (Test-Path -LiteralPath $path) {
        $lines = Select-String -LiteralPath $path -Pattern '\[MP-AutoTest\] (JOINED|PROGRESS|COMPLETE|FAILED)|GRAVSHIP_|GRAVSHIP |desynced|Sync Error|Inconsistent player|Tickable of' |
            Select-Object -Last 60 |
            ForEach-Object { $_.Line }
        $resultLines += "RESULT role=$role"
        $resultLines += $lines
        Copy-Item -LiteralPath $path -Destination (Join-Path $resultDir ("Player-{0}.smoke.log" -f $role)) -Force
    }
    else {
        $resultLines += "RESULT role=$role log_missing"
    }
}

$resultLines
$failed = $resultLines -match 'FAILED|desynced=True|Sync Error|Inconsistent player|Tickable of'
if ($failed.Count -gt 0) {
    exit 1
}
exit 0
