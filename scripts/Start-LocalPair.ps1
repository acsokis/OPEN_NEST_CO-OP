param([Parameter(Mandatory=$true)][string]$TestRoot,[int]$Port=29517)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($TestRoot).TrimEnd('\')
$roles=@('Host','Guest')
foreach($role in $roles){
 $game=Join-Path $root $role
 if(-not(Test-Path -LiteralPath (Join-Path $game 'Iron Nest Heavy Turret Simulator.exe')) -or -not(Test-Path -LiteralPath (Join-Path $game 'DevProfile'))){throw ('Test copy missing: '+$role)}
}
$hashes=@($roles|ForEach-Object{(Get-FileHash -LiteralPath (Join-Path $root ($_+'\Mods\OpenNestCoop.MelonMod.dll'))).Hash})
if($hashes[0] -ne $hashes[1]){throw 'Host and guest mod DLLs differ'}
foreach($process in Get-Process -Name 'Iron Nest Heavy Turret Simulator' -ErrorAction SilentlyContinue){
 try{$path=$process.MainModule.FileName}catch{throw 'Close running Iron Nest instances before starting the isolated pair.'}
 if($path.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'This test pair is already running.'}
 throw 'Close the normal game before starting two local test copies.'
}
$started=@()
foreach($role in $roles){
 $game=Join-Path $root $role
 $mode=if($role -eq 'Host'){'host'}else{'join'}
 $arguments='--local '+$mode+' --localport '+$Port+' --coop-profile "'+(Join-Path $game 'DevProfile')+'" --coop-audit --melonloader.hideconsole -screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile "'+(Join-Path $game 'Player.log')+'"'
 $process=Start-Process -FilePath (Join-Path $game 'Iron Nest Heavy Turret Simulator.exe') -ArgumentList $arguments -WorkingDirectory $game -WindowStyle Hidden -PassThru
 $started+=[pscustomobject]@{Role=$role;PID=$process.Id;Game=$game;Port=$Port}
 if($role -eq 'Host'){
  $deadline=(Get-Date).AddSeconds(45)
  $listening=$false
  while((Get-Date) -lt $deadline){
   $listeners=[Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
   if($listeners|Where-Object {$_.Port -eq $Port -and $_.Address.Equals([Net.IPAddress]::Loopback)}){$listening=$true;break}
   if($process.HasExited){throw 'Host exited before opening its loopback port; inspect Host\MelonLoader\Latest.log'}
   Start-Sleep -Milliseconds 250
  }
  if(-not $listening){throw 'Host did not open its loopback port; host was left running for inspection.'}
 }
}
$started|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'Processes.json') -Encoding UTF8
$started|ConvertTo-Json
