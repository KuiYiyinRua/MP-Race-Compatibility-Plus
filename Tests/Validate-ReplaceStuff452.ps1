param([Parameter(Mandatory=$true)][string]$ModDll,[Parameter(Mandatory=$true)][string]$CandidateDll)
$ErrorActionPreference='Stop'
Add-Type -LiteralPath 'C:/Users/Admin/.nuget/packages/mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll'
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $ModDll).Path)
$candidate=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $CandidateDll).Path)
try {
 $callback=$native.MainModule.GetType('Replace_Stuff.OverMineable.UnFogFix').Methods | Where-Object Name -ceq 'Postfix'
 if(($callback.Parameters.ParameterType.FullName -join ',') -cne 'Verse.FogGrid,Verse.IntVec3,Verse.Map'){throw 'Unfog callback map argument changed'}
 $fogCalls=@($callback.Body.Instructions | Where-Object {$_.OpCode.Name -in @('call','callvirt') -and $_.Operand.FullName -ceq 'System.Boolean Replace_Stuff.OverMineable.FogUtil::IsUnderFog(Verse.Thing)'})
 if($fogCalls.Count -ne 1){throw 'Native unfog check changed'}
 $type=$candidate.MainModule.GetType('Meow.DesyncBatchCompatibility.ReplaceStuffUnfogMap')
 if(!$type){throw 'Regression: native simulation still reads local camera map'}
 $method=$type.Methods | Where-Object Name -ceq 'IsUnderFog'
 $body=($method.Body.Instructions | ForEach-Object {$_.Operand}) -join "`n"
 if(!$body.Contains('Verse.FogGrid::IsFogged') -or $body.Contains('Verse.Find::get_CurrentMap')){throw 'Unfog decision must use explicit owner map'}
 $iterator=@($type.NestedTypes | Where-Object Name -like '<OwnerMap>d__*')
 if($iterator.Count -ne 1){throw 'Missing owner-map transpiler iterator'}
 $transpiler=$iterator[0].Methods | Where-Object Name -ceq 'MoveNext'
 if(!(($transpiler.Body.Instructions | ForEach-Object {$_.Operand}) -join "`n").Contains('Ldarg_2')){throw 'Map argument is not supplied to replacement'}
 [ordered]@{status='PASS_STATIC';nativeSHA256=(Get-FileHash -LiteralPath $ModDll).Hash;nativeMVID=$native.MainModule.Mvid.ToString();candidateSHA256=(Get-FileHash -LiteralPath $CandidateDll).Hash;candidateVersion=$candidate.Name.Version.ToString();checks=5} | ConvertTo-Json
}finally{$native.Dispose();$candidate.Dispose()}
