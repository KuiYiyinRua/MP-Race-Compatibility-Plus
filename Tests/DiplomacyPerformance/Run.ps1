$ErrorActionPreference='Stop'
$project=(Resolve-Path "$PSScriptRoot/../..").Path
$code=[IO.File]::ReadAllText("$project/Source/FactionDiplomacyIsolation/Diplomacy.cs")
$start=$code.IndexOf('    public sealed class FactionDiplomacyState')
if($start -lt 0){throw 'Production class boundary missing'}
$extracted="$project/BuildValidation/DpaOptimization_20260912/DiplomacyExtracted.cs"
[IO.File]::WriteAllText($extracted,"using System; using System.Collections.Generic; using System.Runtime.CompilerServices; using Verse; namespace Meow.FactionDiplomacy {`n"+$code.Substring($start))
dotnet build "$PSScriptRoot/Probe.csproj" -c Release "-p:ExtractedPath=$extracted" --nologo
if($LASTEXITCODE -ne 0){throw 'Build failed'}
& "$PSScriptRoot/bin/Release/net48/Probe.exe"
if($LASTEXITCODE -ne 0){throw 'Behavior checks failed'}
