param([string]$ActionRoot='C:\RWPurge0913\Full01',[string]$ReloadRoot='C:\RWPurge0913\Full01Reload')
$ErrorActionPreference='Stop'
function Wait-File([string]$path,[string]$root,[int]$seconds){
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 while(!(Test-Path -LiteralPath $path)){
  foreach($failure in @('host.failed','client.failed')){if(Test-Path "$root/$failure"){throw (Get-Content "$root/$failure" -Raw)}}
  if([DateTime]::UtcNow -gt $until){throw "Timeout $path"}
  Start-Sleep -Seconds 1
 }
}
try{
 Wait-File "$ActionRoot/purged.save" $ActionRoot 1800
 $candidate=(Get-FileHash "$ActionRoot/Candidate/Meow.FactionPurge.dll").Hash
 Set-Content "$ActionRoot/quit" 'done'
 $pidFile=Get-Content "$ActionRoot/host-process.json" -Raw | ConvertFrom-Json
 Wait-Process -Id $pidFile.ProcessId -Timeout 60 -ErrorAction SilentlyContinue
 & "$PSScriptRoot/Prepare.ps1" -RunRoot $ReloadRoot -InputReplay (Get-Content "$ActionRoot/purged.save" -Raw) -Full
 if((Get-FileHash "$ReloadRoot/Candidate/Meow.FactionPurge.dll").Hash -ne $candidate){throw 'Candidate changed'}
 $until=[DateTime]::UtcNow.AddSeconds(900)
 while(!(Test-Path "$ReloadRoot/host.log") -or !(Select-String -Path "$ReloadRoot/host.log" -SimpleMatch 'Server started.' -Quiet)){
  if(Test-Path "$ReloadRoot/host.failed"){throw (Get-Content "$ReloadRoot/host.failed" -Raw)}
  if([DateTime]::UtcNow -gt $until){throw 'Host startup timeout'}
  Start-Sleep -Seconds 1
 }
 $line='-savedatafolder="'+$ReloadRoot+'\Client" -logFile "'+$ReloadRoot+'\client.log" -purgereload -purgeclient -connect=127.0.0.1:30932 -screen-fullscreen 0 -screen-width 960 -screen-height 640'
 $child=Start-Process "$ReloadRoot/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$ReloadRoot/Game" -WindowStyle Hidden -PassThru
 Get-CimInstance Win32_Process -Filter "ProcessId=$($child.Id)" | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$ReloadRoot/client-process.json"
 Wait-File "$ReloadRoot/client.ready" $ReloadRoot 900
 Wait-File "$ReloadRoot/host.complete" $ReloadRoot 3600
 Wait-File "$ReloadRoot/client.complete" $ReloadRoot 120
 Set-Content "$ReloadRoot/quit" 'done'
 @{status='PASS';candidate=$candidate;action=$ActionRoot;reload=$ReloadRoot} | ConvertTo-Json | Set-Content "$ActionRoot/controller-result.json"
}catch{
 @{status='FAIL';message=$_.Exception.Message} | ConvertTo-Json | Set-Content "$ActionRoot/controller-result.json"
 throw
}
