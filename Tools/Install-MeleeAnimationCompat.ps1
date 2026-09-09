param(
    [Parameter(Mandatory=$true)][string]$TargetModRoot,
    [string]$SourceDllPath = (Join-Path $PSScriptRoot '../MeleeAnimation/Assemblies/MP_MeowOnlineShop.MeleeAnimation.dll')
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $TargetModRoot).Path
$source = (Resolve-Path -LiteralPath $SourceDllPath).Path
$expected = '2FC449C67ECC944AC37099806068474B29C2CA19EECA8DF643DA53F44201BD32'
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expected) {
    throw 'Source DLL does not match the verified release.'
}
$aboutPath = Join-Path $root 'About/About.xml'
[xml]$about = Get-Content -LiteralPath $aboutPath -Raw -Encoding UTF8
if ($about.ModMetaData.packageId -ne 'local.mp.meowonlineshop.sellslingshot') {
    throw 'Select the MP-Race-Compatibility-Plus mod root, not the game or Melee Animation root.'
}
$foldersPath = Join-Path $root 'LoadFolders.xml'
if (Test-Path -LiteralPath $foldersPath) {
    [xml]$folders = Get-Content -LiteralPath $foldersPath -Raw -Encoding UTF8
} else {
    [xml]$folders = '<loadFolders><v1.6><li>1.6</li><li>/</li></v1.6></loadFolders>'
}
if (!$folders.DocumentElement -or $folders.DocumentElement.Name -ne 'loadFolders') {
    throw 'Invalid LoadFolders.xml; no files changed.'
}
$version = $folders.SelectSingleNode('/loadFolders/v1.6')
if (!$version) {
    throw 'Existing LoadFolders.xml has no v1.6 section; no files changed.'
}
$entry = @($version.SelectNodes('li') | Where-Object { $_.InnerText.Trim().TrimEnd('/') -eq 'MeleeAnimation' })
if ($entry.Count -gt 1) { throw 'Duplicate MeleeAnimation entries; no files changed.' }
if ($entry.Count -eq 0) {
    $node = $folders.CreateElement('li')
    $node.InnerText = 'MeleeAnimation'
    $node.SetAttribute('IfModActive', 'co.uk.epicguru.meleeanimation')
    [void]$version.AppendChild($node)
} else {
    $entry[0].SetAttribute('IfModActive', 'co.uk.epicguru.meleeanimation')
}
$after = $about.SelectSingleNode('/ModMetaData/loadAfter')
if (!$after) {
    $after = $about.CreateElement('loadAfter')
    [void]$about.DocumentElement.AppendChild($after)
}
if (!@($after.SelectNodes('li') | Where-Object { $_.InnerText -eq 'co.uk.epicguru.meleeanimation' }).Count) {
    $node = $about.CreateElement('li')
    $node.InnerText = 'co.uk.epicguru.meleeanimation'
    [void]$after.AppendChild($node)
}
$destination = Join-Path $root 'MeleeAnimation/Assemblies/MP_MeowOnlineShop.MeleeAnimation.dll'
$backup = Join-Path $root ('MeleeCompatBackup/' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $backup -Force)
foreach ($path in @($aboutPath, $foldersPath, $destination)) {
    if (Test-Path -LiteralPath $path) {
        Copy-Item -LiteralPath $path -Destination $backup
    }
}
[void](New-Item -ItemType Directory -Path (Split-Path $destination) -Force)
if ($source -ne $destination) { Copy-Item -LiteralPath $source -Destination $destination -Force }
$folders.Save($foldersPath)
$about.Save($aboutPath)
if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) {
    throw "Installed hash mismatch; original files are in $backup"
}
Write-Output "Installed Melee Animation compatibility: $expected"
Write-Output "Backup: $backup"
Write-Output 'Restart BOTH players. Player.log must contain: [MeleeAnimationMP] 1.0.0 synchronization and deterministic simulation patches registered.'
