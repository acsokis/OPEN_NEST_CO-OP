param([string]$GamePath,[string]$PackageRoot,[switch]$VerifyOnly)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
try{
 $running=@(Get-Process -Name 'Iron Nest Heavy Turret Simulator' -ErrorAction SilentlyContinue)
 $runningGame=$null
 foreach($process in $running){try{$runningGame=Split-Path -Parent $process.MainModule.FileName;break}catch{}}
 if($running.Count -gt 0){
  Write-Host 'ZARD BE A JATEKOT, majd itt nyomj ENTER-t. A telepito megvar.' -ForegroundColor Yellow
  if($VerifyOnly){throw 'Elobb lepj ki teljesen az Iron Nestbol.'}
  [void](Read-Host)
  if(Get-Process -Name 'Iron Nest Heavy Turret Simulator' -ErrorAction SilentlyContinue){throw 'A jatek meg fut. Nem tortent fajlcsere.'}
 }
 $roots=@()
 $steam=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
 if($steam){$roots+=$steam;$vdf=Join-Path $steam 'steamapps\libraryfolders.vdf';if(Test-Path -LiteralPath $vdf){foreach($m in [regex]::Matches([IO.File]::ReadAllText($vdf),'"path"\s+"([^"]+)"')){$roots+=$m.Groups[1].Value.Replace('\\','\')}}}
 $game=$GamePath
 if(-not $game -and $runningGame){$game=$runningGame}
 if(-not $game){
  $steamGames=@()
  foreach($r in ($roots|Select-Object -Unique)){
   $manifestPath=Join-Path $r 'steamapps\appmanifest_2950790.acf'
   if(-not(Test-Path -LiteralPath $manifestPath)){continue}
   $installDir=[regex]::Match([IO.File]::ReadAllText($manifestPath),'"installdir"\s+"([^"]+)"').Groups[1].Value
   if(-not $installDir){continue}
   $candidate=Join-Path (Join-Path $r 'steamapps\common') $installDir
   if(Test-Path -LiteralPath (Join-Path $candidate 'Iron Nest Heavy Turret Simulator.exe')){$steamGames+=$candidate}
  }
  $steamGames=@($steamGames|Select-Object -Unique)
  if($steamGames.Count -eq 1){$game=$steamGames[0]}
 }
 if(-not $game){
  $d=New-Object Windows.Forms.OpenFileDialog
  $d.Title='A STEAMBEN: Kezeles > Helyi fajlok bongeszese. ABBOL a mappabol valaszd a jatek EXE-jet!'
  $d.Filter='Iron Nest|Iron Nest Heavy Turret Simulator.exe'
  foreach($r in $roots){$p=Join-Path $r 'steamapps\common\Iron Nest Heavy Turret Simulator';if(Test-Path -LiteralPath $p){$d.InitialDirectory=$p;break}}
  if($d.ShowDialog() -ne 'OK'){exit}
  $game=Split-Path $d.FileName
 }
 $game=[IO.Path]::GetFullPath($game).TrimEnd('\')
 if(-not(Test-Path -LiteralPath (Join-Path $game 'Iron Nest Heavy Turret Simulator.exe'))){throw 'Nem a jatek EXE-jenek mappaja.'}
 if(-not(Test-Path -LiteralPath (Join-Path $game 'MelonLoader\net6\MelonLoader.dll'))){throw 'MelonLoader 0.7.3 telepites szukseges.'}
 $root=$PackageRoot
 if(-not $root){$root=Split-Path $env:IRON_NEST_INSTALLER}
 $payload=Join-Path $root 'payload'
 $manifest=Get-Content -LiteralPath (Join-Path $root 'SHA256.json') -Raw|ConvertFrom-Json
 foreach($item in $manifest){
  foreach($base in @($payload,$game)){
   $target=[IO.Path]::GetFullPath((Join-Path $base $item.Path))
   if(-not $target.StartsWith($base.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Hibas csomag utvonal'}
  }
 }
 foreach($item in $manifest){if((Get-FileHash -LiteralPath (Join-Path $payload $item.Path)).Hash -ne $item.SHA256){throw ('Serult javitocsomag: '+$item.Path)}}
 $backup=Join-Path $game ('Before_CoopSyncfix_'+(Get-Date -Format 'yyyyMMdd_HHmmss'))
 [IO.Directory]::CreateDirectory($backup)|Out-Null
 # Disable duplicate copies, including renamed/subfolder copies, before replacing.
 $mod=Join-Path $game 'Mods'
 foreach($dll in Get-ChildItem -LiteralPath $mod -Recurse -File -Filter '*.dll' -ErrorAction SilentlyContinue){
  $isCoop=$false
  try{$isCoop=([Reflection.AssemblyName]::GetAssemblyName($dll.FullName).Name -eq 'OpenNestCoop.MelonMod')}catch{}
  if(-not $isCoop -and $dll.Name -notlike '*OpenNestCoop*.dll'){continue}
  $rel=$dll.FullName.Substring($game.Length+1)
  $saved=[IO.Path]::GetFullPath((Join-Path $backup $rel))
  if(-not $saved.StartsWith($backup+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Hibas mentesi utvonal'}
  [IO.Directory]::CreateDirectory((Split-Path $saved))|Out-Null
  Move-Item -LiteralPath $dll.FullName -Destination $saved
 }
 $results=@('Open Nest Co-op syncfix.10 installer',('Game: '+$game),('Backup: '+$backup))
 foreach($item in $manifest){
  $dest=Join-Path $game $item.Path
  if(Test-Path -LiteralPath $dest){$saved=Join-Path $backup $item.Path;[IO.Directory]::CreateDirectory((Split-Path $saved))|Out-Null;Copy-Item -LiteralPath $dest -Destination $saved -Force}
  [IO.Directory]::CreateDirectory((Split-Path $dest))|Out-Null
  Copy-Item -LiteralPath (Join-Path $payload $item.Path) -Destination $dest -Force
  if((Get-FileHash -LiteralPath $dest).Hash -ne $item.SHA256){throw 'Telepitett fajl ellenorzesi hiba'}
  $results+=('VERIFIED '+$item.Path+' SHA256='+$item.SHA256)
 }
 $installed=Join-Path $game 'Mods\OpenNestCoop.MelonMod.dll'
 $version=(Get-Item -LiteralPath $installed).VersionInfo.ProductVersion
 if($version -notlike '*syncfix.10*'){throw ('Varatlan telepitett verzio: '+$version)}
 $results+=('Installed product version: '+$version)
 $checkScript=Join-Path $game 'OpenNestUpdate\Check-Install.ps1'
 $check=& $checkScript -GamePath $game -PassThru
 $results+=$check.Lines
 $report=Join-Path $root 'TELEPITES_EREDMENY.txt'
 $results|Set-Content -LiteralPath $report -Encoding UTF8
 $desktopReport=Join-Path ([Environment]::GetFolderPath('Desktop')) 'IRON_NEST_UPDATE_10_EREDMENY.txt'
 if(-not $VerifyOnly){try{$results|Set-Content -LiteralPath $desktopReport -Encoding UTF8}catch{Write-Host 'Az Asztalra nem lehetett jelentest irni; a csomag mappajaban megvan.'}}
 Write-Host 'TELEPITVE: Open Nest Co-op 0.2.1-syncfix.10.' -ForegroundColor Green
 Write-Host 'Mindket gepen ezt telepitsetek, majd a host folytassa a mentest es nyisson uj szobat.'
 Write-Host 'A csomag jegyzekeben szereplo fajlok telepultek. HDR es jatekmentesek nem modosultak.'
 Write-Host ('Mentes: '+$backup)
 Write-Host ('ELLENORZOTT VERZIO: '+$version) -ForegroundColor Green
 Write-Host ('Jatek: '+$game)
 Write-Host ('Jelentes: '+$report)
 $status='FAJLOK RENDBEN'
 if(-not $check.Success){$status='CO-OP FRISSULT, DE ELTERO/HIANYZO MODOK VANNAK';foreach($issue in $check.Issues){Write-Host $issue -ForegroundColor Yellow}}
 Write-Host $status
 if(-not $VerifyOnly){[void][Windows.Forms.MessageBox]::Show(($status+[Environment]::NewLine+$version+[Environment]::NewLine+$game+[Environment]::NewLine+'Inditsd ujra Steambol. Reszletek: IRON_NEST_UPDATE_10_EREDMENY.txt'),'Co-op frissites')}
}catch{Write-Host ('HIBA: '+$_.Exception.Message) -ForegroundColor Red;if(-not $VerifyOnly){[void][Windows.Forms.MessageBox]::Show($_.Exception.Message,'SIKERTELEN telepites')};exit 1}
