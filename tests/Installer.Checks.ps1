param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture=Join-Path $repo ('artifacts\installer-fixture-'+[guid]::NewGuid().ToString('N'))
$package=Join-Path $fixture 'package'
$game=Join-Path $fixture 'Steam library with spaces\steamapps\common\Iron Nest Heavy Turret Simulator'
New-Item -ItemType Directory $package,(Join-Path $game 'Mods'),(Join-Path $game 'MelonLoader\net6') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PackageDirectory 'payload') -Destination $package -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'scripts\syncfix\Check-Install.ps1') -Destination (Join-Path $package 'payload\OpenNestUpdate\Check-Install.ps1') -Force
$expectedPath=Join-Path $package 'payload\OpenNestUpdate\ExpectedMods.json'
# The fixture tests our loader/dependency update; external Modernization binaries
# are neither needed nor copied. Actual game packaging retains all fingerprints.
$all=Get-Content -LiteralPath $expectedPath -Raw|ConvertFrom-Json
$expected=@($all|Where-Object Path -NotLike 'Mods\IronNest*.dll')
$expected|ConvertTo-Json|Set-Content -LiteralPath $expectedPath -Encoding UTF8
$payload=Join-Path $package 'payload'
Get-ChildItem -LiteralPath $payload -Recurse -File|ForEach-Object{
 [pscustomobject]@{Path=$_.FullName.Substring($payload.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $package 'SHA256.json') -Encoding UTF8
[IO.File]::WriteAllText((Join-Path $game 'Iron Nest Heavy Turret Simulator.exe'),'isolated fixture')
[IO.File]::WriteAllText((Join-Path $game 'MelonLoader\net6\MelonLoader.dll'),'loader fixture')
[IO.File]::WriteAllText((Join-Path $game 'dxgi.dll'),'HDR sentinel - preserve')
$hdrHash=(Get-FileHash -LiteralPath (Join-Path $game 'dxgi.dll')).Hash
Copy-Item -LiteralPath (Join-Path $payload 'Mods\OpenNestCoop.MelonMod.dll') -Destination (Join-Path $game 'Mods\renamed-old.dll')
$installer=[IO.File]::ReadAllText((Join-Path $PackageDirectory 'JAVITAS.cmd'))
$body=[regex]::Split($installer,'(?m)^# POWERSHELL\r?$',2)[1]
& ([scriptblock]::Create($body)) -GamePath $game -PackageRoot $package -VerifyOnly
$checker=Join-Path $game 'OpenNestUpdate\Check-Install.ps1'
$result=& $checker -GamePath $game -PassThru
if(-not $result.Success){throw ($result.Issues -join '; ')}
if(Test-Path -LiteralPath (Join-Path $game 'Mods\renamed-old.dll')){throw 'Renamed duplicate was not backed up'}
if((Get-FileHash -LiteralPath (Join-Path $game 'dxgi.dll')).Hash -ne $hdrHash){throw 'HDR sentinel changed'}
if(-not(Get-ChildItem -LiteralPath $game -Directory -Filter 'Before_CoopSyncfix_*')){throw 'Backup missing'}
$library=Join-Path $game 'UserLibs\LiteNetLib.dll'
Move-Item -LiteralPath $library -Destination ($library+'.fixture-hidden')
if((& $checker -GamePath $game -PassThru).Success){throw 'Missing dependency was accepted'}
Move-Item -LiteralPath ($library+'.fixture-hidden') -Destination $library
Copy-Item -LiteralPath (Join-Path $game 'Mods\OpenNestCoop.MelonMod.dll') -Destination (Join-Path $game 'Mods\duplicate.dll')
if((& $checker -GamePath $game -PassThru).Success){throw 'Duplicate loader was accepted'}
Move-Item -LiteralPath (Join-Path $game 'Mods\duplicate.dll') -Destination (Join-Path $fixture 'duplicate.dll')
[IO.File]::AppendAllText($library,'tamper fixture')
if((& $checker -GamePath $game -PassThru).Success){throw 'Wrong hash was accepted'}
$invalid=Join-Path $fixture 'invalid-manifest.json'
[IO.File]::WriteAllText($invalid,'{"value":[]}')
$rejected=$false
try{& $checker -GamePath $game -ExpectedManifest $invalid -PassThru|Out-Null}catch{$rejected=$true}
if(-not $rejected){throw 'Invalid manifest schema was accepted'}
Write-Host 'PASS: update, renamed duplicate backup, hash validation, missing dependency, duplicate detection and HDR preservation.'
Write-Host ('Fixture retained: '+$fixture)
