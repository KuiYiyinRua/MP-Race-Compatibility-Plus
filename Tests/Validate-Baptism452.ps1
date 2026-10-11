param([Parameter(Mandatory=$true)][string]$ModDll,
      [Parameter(Mandatory=$true)][string]$CandidateDll)
$ErrorActionPreference='Stop'
Add-Type -LiteralPath 'C:/Users/Admin/.nuget/packages/mono.cecil/0.11.6/lib/netstandard2.0/Mono.Cecil.dll'
$native=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $ModDll).Path)
$candidate=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $CandidateDll).Path)
try {
 $font=$native.MainModule.GetType('ASEL.Building_BaptismalFont')
 $bridge=@($font.Methods | Where-Object Name -ceq '<>n__0')
 if($bridge.Count -ne 1 -or $bridge[0].Parameters.Count -ne 1 -or $bridge[0].Parameters[0].ParameterType.FullName -cne 'Verse.Pawn'){throw 'Native base bridge signature changed'}
 $calls=@($bridge[0].Body.Instructions | Where-Object {$_.OpCode.Name -in @('call','callvirt')})
 if($calls.Count -ne 1 -or $calls[0].OpCode.Name -cne 'call' -or $calls[0].Operand.FullName -cne 'System.Void RimWorld.Building_Enterable::SelectPawn(Verse.Pawn)'){throw 'Bridge must call native base nonvirtually'}
 $actions=$candidate.MainModule.GetType('Meow.RaceTrioCompatibility.MonolynActions')
 $prefix=@($actions.Methods | Where-Object Name -ceq 'BaptismSelectPrefix')
 if($prefix.Count -ne 1){throw 'Regression: synchronized base virtual replay reopens native baptism confirmation'}
 $ops=@($prefix[0].Body.Instructions | ForEach-Object {if($_.Operand){$_.Operand.ToString()}}) -join "`n"
 foreach($required in @('get_IsInMultiplayer','get_IsExecutingSyncCommand','get_InInterface','CanAcceptPawn','get_MapHeld','baptismBaseSelect','Invoke')){if(!$ops.Contains($required)){throw "Missing replay guard/executor: $required"}}
 $apply=@($actions.Methods | Where-Object Name -ceq 'Apply')[0]
 $strings=@($apply.Body.Instructions | Where-Object {$_.OpCode.Name -ceq 'ldstr'} | ForEach-Object {$_.Operand})
 foreach($required in @('SelectPawn','<>n__0','BaptismSelectPrefix')){if($strings -cnotcontains $required){throw "Replay registration missing: $required"}}
 [ordered]@{status='PASS_STATIC';nativeSHA256=(Get-FileHash -LiteralPath $ModDll).Hash;nativeMVID=$native.MainModule.Mvid.ToString();candidateSHA256=(Get-FileHash -LiteralPath $CandidateDll).Hash;candidateVersion=$candidate.Name.Version.ToString();checks=10} | ConvertTo-Json
}finally{$native.Dispose();$candidate.Dispose()}
