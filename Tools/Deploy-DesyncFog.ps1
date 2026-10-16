param([Parameter(Mandatory=$true)][string]$TestRunRoot)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/..").Path
$evidence=Join-Path $repo 'BuildValidation/DesyncEvidence/Desync466-468_20261012'
$run=(Resolve-Path -LiteralPath $TestRunRoot).Path
$result=Get-Content "$run/result.json" -Raw | ConvertFrom-Json
if($result.status -ne 'PASS' -or $result.maps -ne 2 -or !$result.async -or $result.targetSharedTicks -lt 10000){throw 'Require the targeted two-map async smoke PASS'}
$live=@(Get-CimInstance Win32_Process -Filter "Name='RimWorldWin64.exe'")
if($live.Count){throw 'RimWorld still running; deployment must follow verified process exit'}
$candidate=Join-Path $evidence 'Candidate1'
$expected=@{
 'Meow.RaceTrioCompatibility.dll'='AF603C8EC052B366122D8E8E213F0759CB1E3F55734CC3B9AAF980CAFFB5DD0C'
 'Meow.TaleNivarianCompatibility.dll'='02B4F148512BA3C286F9663F39B5004948C8318C1A51626CD37E7AFAADE50964'
}
$records=@()
foreach($name in $expected.Keys){
 foreach($path in @("$candidate/$name","$run/Game/Mods/Candidate/1.6/Assemblies/$name")){if((Get-FileHash -LiteralPath $path).Hash -ne $expected[$name]){throw "Candidate mismatch: $path"}}
 $records+=[ordered]@{name=$name;version=[Reflection.AssemblyName]::GetAssemblyName("$candidate/$name").Version.ToString();sha256=$expected[$name]}
}
$backup=Join-Path $evidence 'DeploymentBackup'
$stage=Join-Path $evidence 'LocalUpdate-3.0.155'
if((Test-Path $backup) -or (Test-Path $stage)){throw 'Immutable backup and update directories must be new'}
New-Item -ItemType Directory -Path "$backup/1.6/Assemblies","$backup/About","$stage/1.6/Assemblies","$stage/About" -Force | Out-Null
Copy-Item -LiteralPath "$repo/About/About.xml" -Destination "$backup/About/About.xml"
$before=Get-ChildItem "$repo/1.6/Assemblies" -Filter '*.dll' -File | Get-FileHash
$before | ConvertTo-Json | Set-Content "$backup/production-dll-hashes.json"
foreach($name in $expected.Keys){
 Copy-Item -LiteralPath "$repo/1.6/Assemblies/$name" -Destination "$backup/1.6/Assemblies/$name"
 Copy-Item -LiteralPath "$candidate/$name" -Destination "$stage/1.6/Assemblies/$name"
 $symbols=[IO.Path]::ChangeExtension($name,'.pdb')
 if(Test-Path "$repo/1.6/Assemblies/$symbols"){Copy-Item -LiteralPath "$repo/1.6/Assemblies/$symbols" -Destination "$backup/1.6/Assemblies/$symbols"}
 $project=if($name -like '*RaceTrio*'){'RaceTrioCompatibility'}else{'TaleNivarianCompatibility'}
 $build="$repo/Source/$project/bin/Release/net48"
 if((Get-FileHash "$build/$name").Hash -ne $expected[$name]){throw 'Symbols build does not match the archived DLL'}
 Copy-Item -LiteralPath "$build/$symbols" -Destination "$stage/1.6/Assemblies/$symbols"
}
$about=Get-Content "$repo/About/About.xml" -Raw
$about=$about.Replace('<modVersion>3.0.153</modVersion>','<modVersion>3.0.155</modVersion>')
$note='2026-10-12 本地补丁 3.0.155：兼容母舰联机模块 NMC 0.9.8，保留其原生无人机与炮塔随机数所有权，恢复冰雾随机数隔离；冰雾模拟不再读取本地镜头地图的暂停状态，修复交易、更换职位窗口下另一张地图推进时的随机数分叉。RaceTrio 1.1.11、TaleNivarian 1.1.8，核心保持 3.0.151。双端双图异步专项简测通过：18 项配对操作、12 次实际钢材成交、至少 10000 共享 tick 与最终地图/世界随机数一致；更换职位覆盖窗口打开/停留/取消，未验收实际授职仪式结果。按用户要求不扩展长测、冷重连或完整 247 模组回归。所有玩家须更新相同文件并完全重启。'
$about=$about.Replace('<description>',"<description>$note`r`n`r`n")
[xml]$parsed=$about
if($parsed.ModMetaData.modVersion -ne '3.0.155' -or $parsed.ModMetaData.loadAfter.li -notcontains 'local.nivarian.mothership.commander'){throw 'Version or NMC load order mismatch'}
[IO.File]::WriteAllText("$stage/About/About.xml",$about,[Text.UTF8Encoding]::new($false))
$manifest=[ordered]@{packageVersion='3.0.155';coreVersion='3.0.151';motherShipMPVersion='0.9.8';modules=$records;testRoot=$run;testResult=$result;scope='Targeted simple smoke only; extended matrix excluded by user';externalPublication=$false}
$manifest | ConvertTo-Json -Depth 7 | Set-Content "$stage/manifest.json" -Encoding utf8
@'
3.0.155 本地更新包
解压后把 About 和 1.6 合并覆盖到现有 MP-meow-online-shop 目录。
所有玩家安装相同文件，并完全退出后重启 RimWorld。
兼容已安装的母舰联机模块 0.9.8；母舰文件本身无需替换。
包含 RaceTrio 1.1.11 / TaleNivarian 1.1.8；核心保持 3.0.151。
这是针对 Desync-463/464/466/467/468 同一冰雾随机数分叉的修补。
简单双端专项测试通过；未新增完整模组长测、冷重连或实际授职仪式结果验收。
未包含独立的 MoteLifecycle 3.0.154 核心更新，也未发布到 GitHub 或工坊。
'@ | Set-Content "$stage/README.zh-CN.txt" -Encoding utf8
$zip=Join-Path $repo 'Releases/DesyncFog-NMC-3.0.155-update.zip'
if(Test-Path $zip){throw 'Release archive already exists'}
Compress-Archive -Path "$stage/*" -DestinationPath $zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try{
 foreach($name in $expected.Keys){
  $entry=$archive.Entries | Where-Object { $_.FullName.Replace('\','/') -eq "1.6/Assemblies/$name" }
  if(!$entry){throw 'DLL missing from update zip'}
  $stream=$entry.Open();$algorithm=[Security.Cryptography.SHA256]::Create()
  try{$actual=[BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$algorithm.Dispose()}
  if($actual -ne $expected[$name]){throw 'ZIP read-back hash mismatch'}
 }
 if(@($archive.Entries | Where-Object FullName -Match 'Probe|Tests/').Count){throw 'Harness included in update zip'}
}finally{$archive.Dispose()}
foreach($file in Get-ChildItem "$stage/1.6/Assemblies" -File){Copy-Item -LiteralPath $file.FullName -Destination "$repo/1.6/Assemblies/$($file.Name)"}
Copy-Item -LiteralPath "$stage/About/About.xml" -Destination "$repo/About/About.xml"
foreach($name in $expected.Keys){if((Get-FileHash -LiteralPath "$repo/1.6/Assemblies/$name").Hash -ne $expected[$name]){throw 'Deployed hash mismatch'}}
foreach($old in $before){if($expected.Keys -notcontains [IO.Path]::GetFileName($old.Path) -and (Get-FileHash -LiteralPath $old.Path).Hash -ne $old.Hash){throw 'Unrelated production binary changed'}}
if(@(Get-ChildItem "$repo/1.6/Assemblies" -Filter '*Probe*').Count){throw 'Harness found in production'}
[ordered]@{deployed=$records;aboutSHA256=(Get-FileHash "$repo/About/About.xml").Hash;updateZip=$zip;zipSHA256=(Get-FileHash $zip).Hash;testRoot=$run;backup=$backup;deploymentTime=(Get-Date).ToString('o');liveProcesses=$live.Count} | ConvertTo-Json -Depth 5 | Set-Content "$evidence/deployment.json" -Encoding utf8
Get-Content "$evidence/deployment.json"
