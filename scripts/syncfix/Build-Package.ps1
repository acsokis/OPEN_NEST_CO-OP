param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$changes=git -C $repo status --porcelain
if($LASTEXITCODE -or $changes){throw 'A clean Git checkout is required for matching corresponding source.'}
$project=Join-Path $repo 'src\OpenNestCoop.MelonMod\OpenNestCoop.MelonMod.csproj'
dotnet build $project "-p:ClientGame=$GamePath" -p:DeployToMods=false --nologo -v:q
if($LASTEXITCODE){throw 'Build failed'}
$bin=Join-Path $repo 'src\OpenNestCoop.MelonMod\bin\Debug\net6.0'
$binary=Join-Path $bin 'OpenNestCoop.MelonMod.dll'
$version=(Get-Item $binary).VersionInfo.ProductVersion
$stage=Join-Path $repo ('artifacts\OpenNestCoop-'+$version)
New-Item -ItemType Directory (Join-Path $stage 'payload\Mods'),(Join-Path $stage 'payload\UserLibs') -Force|Out-Null
Copy-Item $binary (Join-Path $stage 'payload\Mods') -Force
foreach($name in @('LiteNetLib.dll','SharpGLTF.Core.dll','SharpGLTF.Runtime.dll')){
 Copy-Item (Join-Path $bin $name) (Join-Path $stage 'payload\UserLibs') -Force
}
$api=Join-Path $repo 'src\OpenNestUIKit.API\bin\Debug\net6.0\OpenNestUIKit.API.dll'
Copy-Item $api (Join-Path $stage 'payload\UserLibs') -Force
$payload=Join-Path $stage 'payload'
Get-ChildItem $payload -Recurse -File|ForEach-Object{
 [pscustomobject]@{Path=$_.FullName.Substring($payload.Length+1);SHA256=(Get-FileHash $_.FullName).Hash}
}|ConvertTo-Json|Set-Content (Join-Path $stage 'SHA256.json') -Encoding UTF8
$header=@'
@echo off
set "IRON_NEST_INSTALLER=%~f0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -Command "$s=[IO.File]::ReadAllText($env:IRON_NEST_INSTALLER);$p=[regex]::Split($s,'(?m)^# POWERSHELL\r?$',2)[1]; & ([scriptblock]::Create($p))"
pause
exit /b
# POWERSHELL
'@
[IO.File]::WriteAllText((Join-Path $stage 'JAVITAS.cmd'),($header -replace "`r?`n","`r`n")+"`r`n"+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Install.ps1')),[Text.UTF8Encoding]::new($false))
Copy-Item (Join-Path $repo 'LICENSE') $stage -Force
Copy-Item (Join-Path $repo 'docs\FORK_STATUS.md') (Join-Path $stage 'README.md') -Force
git -C $repo archive --format=zip -o (Join-Path $stage 'source.zip') HEAD
if($LASTEXITCODE){throw 'Corresponding source archive failed'}
$zip=$stage+'.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
& (Join-Path $PSScriptRoot 'Build-SingleFile.ps1') -PackageZip $zip -OutputPath ($stage+'.cmd')
Get-Item $zip,($stage+'.cmd') | Select-Object FullName,Length
