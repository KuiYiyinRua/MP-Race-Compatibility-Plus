param(
    [string]$GameRoot,
    [string]$AlmostThereRoot
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$GameRoot) { $GameRoot = (Resolve-Path (Join-Path $root '../..')).Path }
if (!$AlmostThereRoot) { $AlmostThereRoot = (Resolve-Path (Join-Path $root '../3515165298')).Path }
$evidence = Join-Path $root 'BuildValidation/DesyncEvidence/Desync32-35_20260912'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$gameDll = Join-Path $GameRoot 'RimWorldWin64_Data/Managed/Assembly-CSharp.dll'
$modDll = Join-Path $AlmostThereRoot '1.6/Assemblies/AT1.6.dll'
# Extract the actual installed method bodies; surrounding Unity/world services
# are test doubles. This is an offline Harmony regression, not a game soak.
$estimator = (& ilspycmd -t RimWorld.Planet.CaravanArrivalTimeEstimator $gameDll) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Game decompilation failed' }
$nightRest = (& ilspycmd -t CaravanDontRest.Caravan_NightResting_Patch $modDll) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Almost There decompilation failed' }
function Extract-Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing installed method: $signature" }
    $brace = $source.IndexOf('{', $start)
    $depth = 0
    for ($pos = $brace; $pos -lt $source.Length; $pos++) {
        if ($source[$pos] -eq '{') { $depth++ }
        if ($source[$pos] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $source.Substring($start, $pos - $start + 1) }
        }
    }
    throw 'Unbalanced installed method'
}
$eta = Extract-Method $estimator 'public static int EstimatedTicksToArrive(Caravan caravan, bool allowCaching)'
$rest = Extract-Method $nightRest 'public static void Postfix(Caravan __instance, ref bool __result)'
$code = "using System; using Verse; using RimWorld.Planet;`nnamespace RimWorld.Planet { public static partial class CaravanArrivalTimeEstimator {`n$eta`n} }`nnamespace CaravanDontRest { public static class Caravan_NightResting_Patch {`n$rest`n} }"
$extracted = Join-Path $evidence 'InstalledRegressionMethods.cs'
[IO.File]::WriteAllText($extracted, $code)
Get-FileHash $gameDll,$modDll | Format-List | Out-File (Join-Path $evidence 'regression-input-hashes.txt')
dotnet build (Join-Path $PSScriptRoot 'Probe.csproj') -c Release "-p:ExtractedPath=$extracted" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Regression build failed' }
& (Join-Path $PSScriptRoot 'bin/Release/net48/Probe.exe')
exit $LASTEXITCODE
