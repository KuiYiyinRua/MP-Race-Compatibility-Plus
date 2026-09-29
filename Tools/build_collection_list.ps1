$ErrorActionPreference = 'Stop'
$api='https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/'
$body=@{collectioncount='1'; 'publishedfileids[0]'='3798455086'}
$data=(Invoke-RestMethod -Method Post -Uri $api -Body $body).response.collectiondetails[0].children | Sort-Object {[int]$_.sortorder}
$root='G:\Steam\steamapps\workshop\content\294100'
$rows=@()
foreach($item in $data){
  $dir=Join-Path $root $item.publishedfileid
  $about=Get-ChildItem -LiteralPath $dir -Filter About.xml -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1
  $pkg=$null; $title=$null
  if($about){
    try { $xml=[xml](Get-Content -Raw -LiteralPath $about.FullName); $pkg=[string]$xml.ModMetaData.packageId; $title=[string]$xml.ModMetaData.name } catch {}
  }
  $rows += [pscustomobject]@{SortOrder=[int]$item.sortorder; WorkshopId=[string]$item.publishedfileid; PackageId=$pkg; Title=$title}
}
$missing=$rows | Where-Object {!$_.PackageId}
$desktop='H:\'+([char]0x684c)+([char]0x9762)
$out=Join-Path $desktop 'list_3798455086.xml'
$settings=New-Object System.Xml.XmlWriterSettings
$settings.Indent=$true; $settings.Encoding=New-Object System.Text.UTF8Encoding($false)
$writer=[System.Xml.XmlWriter]::Create($out,$settings)
$writer.WriteStartDocument(); $writer.WriteStartElement('ModsConfigData')
$writer.WriteElementString('version','1.6.4850 rev645')
$writer.WriteStartElement('activeMods')
$writer.WriteElementString('li','ludeon.rimworld')
$writer.WriteElementString('li','ludeon.rimworld.royalty')
$writer.WriteElementString('li','ludeon.rimworld.ideology')
$writer.WriteElementString('li','ludeon.rimworld.biotech')
$writer.WriteElementString('li','ludeon.rimworld.anomaly')
$writer.WriteElementString('li','ludeon.rimworld.odyssey')
foreach($r in $rows | Where-Object {$_.PackageId}) { $writer.WriteElementString('li',$r.PackageId) }
$writer.WriteEndElement(); $writer.WriteStartElement('knownExpansions')
foreach($x in @('ludeon.rimworld','ludeon.rimworld.royalty','ludeon.rimworld.ideology','ludeon.rimworld.biotech','ludeon.rimworld.anomaly','ludeon.rimworld.odyssey')) { $writer.WriteElementString('li',$x) }
$writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndDocument(); $writer.Close()
$rows | ConvertTo-Csv -NoTypeInformation | Set-Content -Encoding UTF8 (Join-Path $desktop 'list_3798455086_mapping.csv')
Write-Output "TOTAL=$($rows.Count) FOUND=$($rows.Count-$missing.Count) MISSING=$($missing.Count)"
$missing | Format-Table SortOrder,WorkshopId,Title -AutoSize
