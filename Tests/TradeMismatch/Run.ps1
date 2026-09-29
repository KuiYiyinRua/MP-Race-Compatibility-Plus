param([Parameter(Mandatory=$true)][string]$RunRoot,[switch]$Regression)
$ErrorActionPreference="Stop";$run=[IO.Path]::GetFullPath($RunRoot)
$owned=@();$status='FAILED';$reason='not completed';$watch=[Diagnostics.Stopwatch]::StartNew()
function ReadLiveLog($path){if(!(Test-Path -LiteralPath $path)){return ''};$stream=$null;$reader=$null;try{$stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete));$reader=[IO.StreamReader]::new($stream);return $reader.ReadToEnd()}catch [IO.IOException]{return ''}finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}}
function StartPeer($peer,$extra){if($Regression){$extra+=" -tradefixed -tradecontrol"};$line='-savedatafolder="'+$run+'/'+$peer+'" -logFile "'+$run+'/'+$peer.ToLower()+'.log" -tradeprobe -screen-fullscreen 0 -screen-width 800 -screen-height 600 '+$extra;$p=Start-Process "$run/Game/RimWorldWin64.exe" -ArgumentList $line -WorkingDirectory "$run/Game" -WindowStyle Hidden -PassThru;Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)" | Select-Object ProcessId,CommandLine,CreationDate | ConvertTo-Json | Set-Content "$run/$peer-process.json";return $p}
try{
 $owned+=StartPeer Host '';$clientStarted=$false
 while($watch.Elapsed.TotalSeconds -lt 2100){
  $h=ReadLiveLog "$run/host.log";$c=ReadLiveLog "$run/client.log"
  if(($h+$c) -match 'TRADE_PROBE FAILED|Prepatcher Error: Fatal|Error in static constructor of TradeMismatchProbe|Desynced after|Sync Error|PacketReadException|Exception ticking|Exception in GameComponentTick|Could not resolve cross-reference|Multiplayer ClientTask exception'){throw 'Runtime/setup failure; see logs'}
  if(!$clientStarted -and $h -match '(?m)^Server started\.'){if(Test-Path "$run/Host/DefLoadCache"){Copy-Item "$run/Host/DefLoadCache" "$run/Client/DefLoadCache" -Recurse -Force};$owned+=StartPeer Client '-tradeclient -connect=127.0.0.1:30993';$clientStarted=$true}
  if((Test-Path "$run/host.complete") -and (Test-Path "$run/client.complete")){
   if([IO.File]::ReadAllText("$run/host.transfers") -cne [IO.File]::ReadAllText("$run/client.transfers")){throw 'Peer transfer records differ'}
   if($Regression){
    if([IO.File]::ReadAllText("$run/host.original-control") -cne [IO.File]::ReadAllText("$run/client.original-control")){throw 'Original-code control differs'}
    if([IO.File]::ReadAllText("$run/host.receipts") -cne [IO.File]::ReadAllText("$run/client.receipts")){throw 'Peer ownership receipts differ'}
    if(@(Get-Content "$run/host.receipts").Count -ne 10){throw 'Expected ten ownership assertions'}
    $status='PASS';$reason='Ten native accept operations, paired ownership/stock assertions and 10000 shared ticks'
   }else{$status='DIAGNOSTIC';$reason='Native accept and transfers captured'}
   break
  }
  foreach($p in $owned){if($p.HasExited){throw 'Early peer exit'}}
  Start-Sleep -Seconds 3
 }
 if($status -notin @('DIAGNOSTIC','PASS')){throw 'Timeout'}
 foreach($x in Get-Content "$run/binaries.json" -Raw | ConvertFrom-Json){if((Get-FileHash -LiteralPath $x.Path).Hash -ne $x.Hash){throw 'Binary changed'}}
}catch{$status='FAILED';$reason=$_.Exception.Message}
finally{
 Set-Content "$run/quit" 'cleanup';foreach($p in $owned){if($p -and !$p.HasExited -and !$p.WaitForExit(10000)){$live=Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)";if($live -and $live.CommandLine -and $live.CommandLine.Contains($run) -and $live.CommandLine.Contains('-tradeprobe')){Stop-Process -Id $p.Id}}}
 [ordered]@{status=$status;reason=$reason;elapsedSeconds=$watch.Elapsed.TotalSeconds} | ConvertTo-Json | Set-Content "$run/result.json"
}
if($status -notin @('DIAGNOSTIC','PASS')){throw $reason}
