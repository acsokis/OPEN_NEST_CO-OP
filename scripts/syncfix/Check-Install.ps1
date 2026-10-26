param([string]$GamePath,[string]$ExpectedManifest,[string]$ReportPath,[switch]$PassThru)
$ErrorActionPreference='Stop'
# Read-only inspection. It never changes the loader, saves, mods or HDR.
if(-not $GamePath){$GamePath=Split-Path -Parent $PSScriptRoot}
$game=[IO.Path]::GetFullPath($GamePath).TrimEnd('\')
if(-not $ExpectedManifest){$ExpectedManifest=Join-Path $PSScriptRoot 'ExpectedMods.json'}
$issues=New-Object 'Collections.Generic.List[string]'
$lines=New-Object 'Collections.Generic.List[string]'
$lines.Add('IRON NEST FILE CHECK / '+(Get-Date -Format o))
$lines.Add('Game: '+$game)
if(-not(Test-Path -LiteralPath (Join-Path $game 'Iron Nest Heavy Turret Simulator.exe'))){$issues.Add('Nem a jatek EXE-jenek mappaja: '+$game)}
if(-not(Test-Path -LiteralPath (Join-Path $game 'MelonLoader\net6\MelonLoader.dll'))){$issues.Add('Hianyzik a MelonLoader net6 betolto')}
if((Test-Path -LiteralPath (Join-Path $game 'BepInEx\core')) -and (Test-Path -LiteralPath (Join-Path $game 'winhttp.dll'))){$issues.Add('Aktiv BepInEx is van a MelonLoader mellett; ellenorizendo betoltoutkozes')}
$expected=Get-Content -LiteralPath $ExpectedManifest -Raw|ConvertFrom-Json
if(-not $expected){throw 'Ures ellenorzesi jegyzek'}
foreach($item in $expected){
 if(-not $item.Path -or [string]$item.SHA256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Hibas ellenorzesi jegyzek: utvonal/SHA256 hianyzik'}
 $relative=[string]$item.Path
 $path=[IO.Path]::GetFullPath((Join-Path $game $relative))
 if(-not $path.StartsWith($game+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Hibas ellenorzesi jegyzek'}
 if(-not(Test-Path -LiteralPath $path -PathType Leaf)){$issues.Add('HIANYZIK: '+$relative);continue}
 $actual=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
 $version=(Get-Item -LiteralPath $path).VersionInfo.ProductVersion
 $lines.Add($relative+' | '+$version+' | SHA256='+$actual)
 if($actual -ne $item.SHA256){$issues.Add('ELTERO/REGI FAJL: '+$relative)}
}
$coopCopies=@()
foreach($dll in Get-ChildItem -LiteralPath (Join-Path $game 'Mods') -Filter '*.dll' -Recurse -File -ErrorAction SilentlyContinue){
 try{if([Reflection.AssemblyName]::GetAssemblyName($dll.FullName).Name -eq 'OpenNestCoop.MelonMod'){$coopCopies+=$dll.FullName}}catch{}
}
if($coopCopies.Count -ne 1){$issues.Add('Co-op DLL peldanyok szama: '+$coopCopies.Count+' (pontosan 1 kell)')}
foreach($process in Get-Process -Name 'Iron Nest Heavy Turret Simulator' -ErrorAction SilentlyContinue){
 try{$exe=$process.MainModule.FileName;$lines.Add('Running EXE: '+$exe);if((Split-Path -Parent $exe) -ne $game){$issues.Add('MASIK MAPPAJABOL FUT A JATEK: '+$exe)}}catch{$lines.Add('A futo EXE utvonala nem olvashato')}
}
$log=Join-Path $game 'MelonLoader\Latest.log'
if(Test-Path -LiteralPath $log){
 $lines.Add('Latest.log time: '+(Get-Item -LiteralPath $log).LastWriteTime.ToString('o'))
 Get-Content -LiteralPath $log -TotalCount 250|Select-String 'OpenNestCoop|syncfix\.|MelonLoader v|ModernizationSync'|ForEach-Object{$lines.Add('LOG: '+$_.Line)}
}
$success=$issues.Count -eq 0
$lines.Add('File validation: '+$(if($success){'PASS'}else{'FAIL'}))
foreach($issue in $issues){$lines.Add('ERROR: '+$issue)}
$lines.Add('Ez fajl/verzioellenorzes. A ket gepes jatekmenet szinkronjat kulon kell kiprobalni.')
if($ReportPath){$lines|Set-Content -LiteralPath $ReportPath -Encoding UTF8}
if($PassThru){[pscustomobject]@{Success=$success;Issues=@($issues.ToArray());Lines=@($lines.ToArray());GamePath=$game};return}
foreach($line in $lines){Write-Host $line}
if(-not $success){exit 2}
