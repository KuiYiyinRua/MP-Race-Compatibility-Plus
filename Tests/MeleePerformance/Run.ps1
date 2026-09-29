param([switch]$Before)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = Join-Path $root 'BuildValidation/MeleePerformance_20260912'
$source = Join-Path $root 'Source/MeleeAnimationCompat'
$settingsPath = Join-Path $source 'SessionSettings.cs'
$statePath = Join-Path $source 'State.cs'
if ($Before) {
    $settingsPath = Join-Path $evidence 'SessionSettings.before.cs.txt'
    $statePath = Join-Path $evidence 'State.before.cs.txt'
}
$settings = [IO.File]::ReadAllText($settingsPath)
$state = [IO.File]::ReadAllText($statePath)
# Extract complete production classes, not a rewritten implementation.
$settingsStart = $settings.IndexOf('    public sealed class MeleeSessionState')
$settingsEnd = $settings.IndexOf('    [HarmonyPatch]', $settingsStart)
$skillsStart = $state.IndexOf('    [HarmonyPatch(typeof(IdleControllerComp)')
$skillsEnd = $state.IndexOf('    [HarmonyPatch(typeof(AnimRenderer)', $skillsStart)
if ($settingsStart -lt 0 -or $settingsEnd -lt 0 -or $skillsStart -lt 0 -or $skillsEnd -lt 0) { throw 'Source boundaries changed' }
$code = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AM;
using AM.AMSettings;
using AM.Idle;
using AM.UniqueSkills;
using HarmonyLib;
using Verse;
namespace MP_MeowOnlineShop.MeleeAnimation {
'@
$code += $settings.Substring($settingsStart, $settingsEnd - $settingsStart)
$code += $state.Substring($skillsStart, $skillsEnd - $skillsStart) + "`n}"
$extracted = Join-Path $evidence 'Extracted.cs'
[IO.File]::WriteAllText($extracted, $code)
dotnet build (Join-Path $PSScriptRoot 'Probe.csproj') -c Release "-p:ExtractedPath=$extracted" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed' }
& (Join-Path $PSScriptRoot 'bin/Release/net48/Probe.exe')
exit $LASTEXITCODE
