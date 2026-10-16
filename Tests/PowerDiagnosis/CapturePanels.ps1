param([Parameter(Mandatory=$true)][string]$RunRoot,[string]$WaitForRun)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../..").Path
if($WaitForRun){
 $deadline=(Get-Date).AddMinutes(30)
 while(!(Test-Path "$WaitForRun/result.json")){if((Get-Date)-gt $deadline){throw 'Previous run timeout'};Start-Sleep -Seconds 2}
 $previous=Get-Content "$WaitForRun/result.json" -Raw | ConvertFrom-Json
 if($previous.status -ne 'PASS'){throw 'Previous regression did not pass'}
}
& "$PSScriptRoot/Prepare.ps1" -RunRoot $RunRoot -CacheFrom 'H:\MPValidation\PD05\Host' -Replay "$repo/BuildValidation/PowerMapDiagnosis_20261016/Latest-020807.snapshot.zip" -Core "$repo/BuildValidation/PowerMapDiagnosis_20261016/Candidate/MP_MeowOnlineShop.dll" -KeepLanguage
$deadline=(Get-Date).AddMinutes(20)
while(!(Test-Path "$RunRoot/complete")){
 if((Get-Date)-gt $deadline){Set-Content "$RunRoot/quit" 'UI load timeout';throw 'Full replay load timeout'}
 if((Test-Path "$RunRoot/diagnosis.txt") -and (Select-String -LiteralPath "$RunRoot/diagnosis.txt" -Pattern '^FATAL' -Quiet)){Set-Content "$RunRoot/quit" 'UI diagnostic failed';throw 'Diagnostic failure'}
 Start-Sleep -Seconds 2
}
foreach($id in @('CommsConsole307492','ElectricStove274836','StandingLamp261133')){
 Set-Content "$RunRoot/inspect.request" $id
 $limit=(Get-Date).AddSeconds(60)
 while(!(Test-Path "$RunRoot/inspect-$id.png")){
  if((Get-Date)-gt $limit){throw "Panel capture timeout: $id"}
  Start-Sleep -Seconds 1
 }
 Start-Sleep -Seconds 2
 if(!(Select-String -LiteralPath "$RunRoot/diagnosis.txt" -SimpleMatch "STAGE inspected selected=$id" -Quiet)){throw "Wrong selected object: $id"}
 Write-Output "CAPTURE_READY $id"
}
Write-Output 'UI_CAPTURE_READY: isolated process left open for image inspection; send quit marker after review.'
