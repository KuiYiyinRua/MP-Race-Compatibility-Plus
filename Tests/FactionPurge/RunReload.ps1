param([Parameter(Mandatory=$true)][string]$ActionRoot,[Parameter(Mandatory=$true)][string]$RunRoot,[int]$Port=30934,[int]$Ticks=120000,[switch]$SyncTime,[switch]$Resume)
$ErrorActionPreference='Stop'
if(!$Resume){& "$PSScriptRoot/Prepare.ps1" -RunRoot $RunRoot -InputReplay (Get-Content "$ActionRoot/purged.save" -Raw) -Port $Port -Ticks $Ticks -SyncTime:$SyncTime}
$until=[DateTime]::UtcNow.AddSeconds(900)
while(!(Test-Path "$RunRoot/host.log") -or !(Select-String -Path "$RunRoot/host.log" -SimpleMatch 'Server started.' -Quiet)){
 if(Test-Path "$RunRoot/host.failed"){throw (Get-Content "$RunRoot/host.failed" -Raw)}
 if([DateTime]::UtcNow -gt $until){throw 'host load timeout'}
 Start-Sleep -Seconds 1
}
$line='-savedatafolder="'+$RunRoot+'\Client" -logFile "'+$RunRoot+'\client.log" -purgereload -purgeclient -purgeticks='+$Ticks+' -connect=127.0.0.1:'+$Port+' -screen-fullscreen 0 -screen-width 960 -screen-height 640'
$peer=Start-Process "$RunRoot/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$RunRoot/Game" -WindowStyle Hidden -PassThru
Get-CimInstance Win32_Process -Filter "ProcessId=$($peer.Id)" | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine | ConvertTo-Json | Set-Content "$RunRoot/client-process.json"
$until=[DateTime]::UtcNow.AddSeconds(3600)
while(!(Test-Path "$RunRoot/host.complete") -or !(Test-Path "$RunRoot/client.complete")){
 foreach($failure in @('host.failed','client.failed')){if(Test-Path "$RunRoot/$failure"){throw (Get-Content "$RunRoot/$failure" -Raw)}}
 if([DateTime]::UtcNow -gt $until){throw 'soak timeout'}
 Start-Sleep -Seconds 1
}
Set-Content "$RunRoot/quit" 'done'
foreach($role in @('host','client')){
 $record=Get-Content "$RunRoot/$role-process.json" -Raw | ConvertFrom-Json
 $live=Get-CimInstance Win32_Process -Filter "ProcessId=$($record.ProcessId)"
 if($live -and $live.CommandLine -and $live.CommandLine.Contains($RunRoot)){Wait-Process -Id $record.ProcessId -Timeout 60 -ErrorAction SilentlyContinue}
}
@{status='PASS';action=$ActionRoot;run=$RunRoot;ticks=$Ticks;syncTime=[bool]$SyncTime;candidate=(Get-FileHash "$RunRoot/Candidate/Meow.FactionPurge.dll").Hash} | ConvertTo-Json | Set-Content "$RunRoot/controller-result.json"
