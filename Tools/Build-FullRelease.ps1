param([Parameter(Mandatory=$true)][string]$OutputRoot,[string]$GameRoot)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(!$GameRoot){$GameRoot=(Resolve-Path (Join-Path $root '../..')).Path}
$out=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $out){throw 'OutputRoot must be new; archived builds are immutable.'}
New-Item -ItemType Directory -Path $out | Out-Null
$projects=Get-Content (Join-Path $PSScriptRoot 'full-release-projects.json') -Raw | ConvertFrom-Json
$core=$projects | Where-Object assembly -eq 'MP_MeowOnlineShop'
$ordered=@($core)+@($projects | Where-Object assembly -ne 'MP_MeowOnlineShop')
$mainDll=Join-Path $out 'MP_MeowOnlineShop/MP_MeowOnlineShop.dll'
$records=@()
foreach($item in $ordered){
 $name=[IO.Path]::GetFileNameWithoutExtension($item.project)
 $dest=Join-Path $out $name
 $project=Join-Path $root $item.project
 $props=@("/p:GameRoot=$GameRoot","/p:MainCompatDllPath=$mainDll","/p:TargetRoot=$GameRoot/Mods/2944488802","/p:HarmonyPath=$GameRoot/Mods/2009463077/Current/Assemblies/0Harmony.dll")
 if($item.assembly -eq 'MP_MeowOnlineShop'){
  & dotnet msbuild $project /t:Rebuild /p:Configuration=Release "/p:OutputPath=$dest/" /v:minimal /nologo @props *> (Join-Path $out "$name.log")
 }else{
  & dotnet build $project -c Release -o $dest --nologo -v minimal @props *> (Join-Path $out "$name.log")
 }
 if($LASTEXITCODE -ne 0){Get-Content (Join-Path $out "$name.log") -Tail 30;throw "Build failed: $name"}
 $dll=Join-Path $dest ($item.assembly+'.dll')
 if(!(Test-Path -LiteralPath $dll)){throw "Missing output: $dll"}
 $records+=[pscustomobject]@{project=$item.project;destination=$item.destination;path=$dll;version=[Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString();sha256=(Get-FileHash -LiteralPath $dll).Hash}
 Write-Output "BUILT $name"
}
$records | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'assemblies.json') -Encoding UTF8
Write-Output "FULL_BUILD_PASS projects=$($records.Count)"
