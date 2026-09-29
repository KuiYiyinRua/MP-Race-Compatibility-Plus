param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
$gamePath = (Resolve-Path -LiteralPath $GameRoot).Path
$script:assemblyDirs = @(
    (Join-Path $gamePath 'RimWorldWin64_Data/Managed'),
    (Join-Path $gamePath 'Mods/Multiplayer/1.6/AssembliesCustom'),
    (Join-Path $gamePath 'Mods/Multiplayer/1.6/Assemblies'),
    (Join-Path $gamePath 'Mods/2009463077/Current/Assemblies')
)
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve({
    param($sender, $eventArgs)
    $name = New-Object Reflection.AssemblyName($eventArgs.Name)
    foreach ($folder in $script:assemblyDirs) {
        $candidatePath = Join-Path $folder ($name.Name + '.dll')
        if (Test-Path -LiteralPath $candidatePath) { return [Reflection.Assembly]::ReflectionOnlyLoadFrom($candidatePath) }
    }
    return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
})
$gameAssembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $script:assemblyDirs[0] 'Assembly-CSharp.dll'))
$mpAssembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $script:assemblyDirs[1] 'Multiplayer.dll'))
function Assert-Method($assembly, [string]$typeName, [string]$methodName, [string]$returnName, [string[]]$parameters) {
    $type = $assembly.GetType($typeName, $true)
    $matches = @($type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly') | Where-Object {
        $_.Name -eq $methodName -and $_.ReturnType.FullName -eq $returnName -and
        (($_.GetParameters() | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq ($parameters -join '|')
    })
    if ($matches.Count -ne 1) { throw "Signature mismatch: $typeName.$methodName matches=$($matches.Count)" }
    "PASS installed signature $typeName.$methodName"
}
Assert-Method $gameAssembly 'RimWorld.Quest' 'MakeRaw' 'RimWorld.Quest' @()
Assert-Method $gameAssembly 'RimWorld.Quest' 'ExposeData' 'System.Void' @()
Assert-Method $gameAssembly 'RimWorld.Quest' 'Accept' 'System.Void' @('Verse.Pawn')
Assert-Method $gameAssembly 'RimWorld.Quest' 'SetInitiallyAccepted' 'System.Void' @()
Assert-Method $gameAssembly 'RimWorld.QuestGen.QuestNode_GetMap' 'IsAcceptableMap' 'System.Boolean' @('Verse.Map','RimWorld.QuestGen.Slate')
Assert-Method $gameAssembly 'RimWorld.QuestGen.QuestNode_GetMap' 'TryFindMap' 'System.Boolean' @('RimWorld.QuestGen.Slate','Verse.Map&')
Assert-Method $gameAssembly 'RimWorld.QuestGen.QuestNode_GetMap' 'RunInt' 'System.Void' @()
Assert-Method $mpAssembly 'Multiplayer.Client.Comp.SetContextForAccept' 'Prefix' 'System.Void' @('RimWorld.Quest','Multiplayer.Client.AsyncTimeComp&')
'PASS installed metadata only; no game code executed'
Assert-Method $mpAssembly 'Multiplayer.Client.Patches.MainTabWindow_QuestsShouldListNowPatch' 'Prefix' 'System.Boolean' @('RimWorld.Quest','System.Boolean&')
