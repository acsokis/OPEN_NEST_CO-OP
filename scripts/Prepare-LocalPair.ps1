param([Parameter(Mandatory=$true)][string]$GamePath,[Parameter(Mandatory=$true)][string]$TestRoot,[string]$ProgressionSeed)
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$target=[IO.Path]::GetFullPath($TestRoot).TrimEnd('\')
if($target -eq $source -or $target.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'TestRoot must be outside the installed game.'}
if(-not(Test-Path -LiteralPath (Join-Path $source 'Iron Nest Heavy Turret Simulator.exe'))){throw 'Game EXE missing'}
if(Test-Path -LiteralPath $target){throw 'Use a new empty test directory; existing test data is retained.'}
New-Item -ItemType Directory $target|Out-Null
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dll=Join-Path $repo 'src\OpenNestCoop.MelonMod\bin\Debug\net6.0\OpenNestCoop.MelonMod.dll'
if(-not(Test-Path -LiteralPath $dll)){throw 'Build the development mod first.'}
function Copy-Tree([string]$relative,[string]$destination){
 $from=Join-Path $source $relative
 if(-not(Test-Path -LiteralPath $from)){return}
 & robocopy.exe $from (Join-Path $destination $relative) /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
 if($LASTEXITCODE -ge 8){throw ('Copy failed: '+$relative)}
}
foreach($role in @('Host','Guest')){
 $game=Join-Path $target $role;New-Item -ItemType Directory $game|Out-Null
 foreach($name in @('Iron Nest Heavy Turret Simulator.exe','UnityPlayer.dll','GameAssembly.dll','baselib.dll','version.dll','UnityCrashHandler64.exe')){
  Copy-Item -LiteralPath (Join-Path $source $name) -Destination $game
 }
 foreach($dir in @('Iron Nest Heavy Turret Simulator_Data','D3D12','Mods','UserLibs','Models','UserData','MelonLoader\net6','MelonLoader\Dependencies','MelonLoader\Il2CppAssemblies')){Copy-Tree $dir $game}
 Copy-Item -LiteralPath $dll -Destination (Join-Path $game 'Mods\OpenNestCoop.MelonMod.dll') -Force
 New-Item -ItemType Directory (Join-Path $game 'DevProfile\Live'),(Join-Path $game 'DevProfile\Legacy'),(Join-Path $game 'DevProfile\Checkpoints') -Force|Out-Null
 if($ProgressionSeed){Get-ChildItem -LiteralPath $ProgressionSeed -File -Filter '*.dat'|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $game 'DevProfile\Live')}}
 [IO.File]::WriteAllText((Join-Path $game 'steam_appid.txt'),'2950790')
 [pscustomobject]@{Role=$role;Game=$game;ModVersion=(Get-Item -LiteralPath (Join-Path $game 'Mods\OpenNestCoop.MelonMod.dll')).VersionInfo.ProductVersion}|ConvertTo-Json -Compress
}
exit 0
