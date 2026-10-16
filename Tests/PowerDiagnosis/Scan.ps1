$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
[void][Reflection.Assembly]::LoadFrom('C:/Users/Admin/.nuget/packages/mono.cecil/0.11.6/lib/net40/Mono.Cecil.dll')
[xml]$config=Get-Content "$repo/BuildValidation/PowerMapDiagnosis_20261016/ModsConfig.snapshot.xml" -Raw
$ids=@($config.ModsConfigData.activeMods.li | ForEach-Object {$_ -replace '_steam$',''})
function ScanTypes($types,$path){foreach($t in $types){
 $attrs=($t.CustomAttributes | ForEach-Object {$_.AttributeType.FullName+':'+(($_.ConstructorArguments | ForEach-Object {$_.Value}) -join ',')}) -join ';'
 if($attrs -match 'CompPower|InspectString|PowerNetManager'){"$path | TYPE $($t.FullName) | $attrs"}
 foreach($m in $t.Methods){if(!$m.HasBody){continue};$s=@($m.Body.Instructions | Where-Object { $_.OpCode.Code -eq 'Ldstr' -and $_.Operand -match '^(get_PowerOn|set_PowerOn|PowerOn|PowerNet|CompInspectStringExtra|InspectStringPartsFromComps|PowerNetsTick|UpdatePowerNetsAndConnections_First)$'});if($s.Count){"$path | METHOD $($m.FullName) | $(($s.Operand) -join ',')"}}
 if($t.HasNestedTypes){ScanTypes $t.NestedTypes $path}
}}
foreach($d in Get-ChildItem "$repo/.." -Directory){
 $about=Join-Path $d.FullName 'About/About.xml';if(!(Test-Path -LiteralPath $about)){continue}
 try{[xml]$a=Get-Content -LiteralPath $about -Raw}catch{continue}
 if([string]$a.ModMetaData.packageId -notin $ids){continue}
 foreach($f in Get-ChildItem -LiteralPath $d.FullName -Recurse -Filter '*.dll' -File | Where-Object {$_.FullName -match '(\\1\.6\\|\\Current\\|\\Assemblies\\)' -and $_.FullName -notmatch '\\(BuildValidation|Tests|bin|obj|Source|Releases|packages)\\'}){
  try{$asm=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($f.FullName);try{ScanTypes $asm.MainModule.Types $f.FullName}finally{$asm.Dispose()}}catch{"ERROR $($f.FullName): $_"}
 }
}
