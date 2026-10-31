param([Parameter(Mandatory=$true)][string]$GamePath,[switch]$IncludeCatMods)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Package-Safety.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$changes=git -C $repo status --porcelain
if($LASTEXITCODE -or $changes){throw 'A clean Git checkout is required for matching corresponding source.'}
$project=Join-Path $repo 'src\OpenNestCoop.MelonMod\OpenNestCoop.MelonMod.csproj'
$xml=[xml][IO.File]::ReadAllText($project)
$projectVersion=$xml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$netSource=[IO.File]::ReadAllText((Join-Path $repo 'src\OpenNestCoop\Core\NetConfig.cs'))
$runtimeVersion=[regex]::Match($netSource,'const string Version\s*=\s*"([^"]+)"').Groups[1].Value
if(-not $runtimeVersion -or $projectVersion -ne $runtimeVersion){throw 'Runtime/network version and package version differ; cancelled.'}
dotnet build $project "-p:ClientGame=$GamePath" -p:DeployToMods=false --nologo -v:q
if($LASTEXITCODE){throw 'Build failed'}
$bin=Join-Path $repo 'src\OpenNestCoop.MelonMod\bin\Debug\net6.0'
$binary=Join-Path $bin 'OpenNestCoop.MelonMod.dll'
$version=(Get-Item $binary).VersionInfo.ProductVersion
$flavor=if($IncludeCatMods){'-private-friend'}else{''}
$stage=Join-Path $repo ('artifacts\OpenNestCoop-'+$version+$flavor)
if(Test-Path -LiteralPath $stage){throw ('Generated staging directory already exists; use a fresh checkout/output: '+$stage)}
New-Item -ItemType Directory (Join-Path $stage 'payload\Mods'),(Join-Path $stage 'payload\UserLibs') -Force|Out-Null
Copy-Item $binary (Join-Path $stage 'payload\Mods') -Force
foreach($name in @('LiteNetLib.dll','SharpGLTF.Core.dll','SharpGLTF.Runtime.dll')){
 Copy-Item (Join-Path $bin $name) (Join-Path $stage 'payload\UserLibs') -Force
}
$api=Join-Path $repo 'src\OpenNestUIKit.API\bin\Debug\net6.0\OpenNestUIKit.API.dll'
Copy-Item $api (Join-Path $stage 'payload\UserLibs') -Force
$payload=Join-Path $stage 'payload'
if($IncludeCatMods){
 foreach($name in @('IronNestCatCollision.dll','IronNestHighTide.dll')){
  Copy-Item -LiteralPath (Join-Path $GamePath ('Mods\'+$name)) -Destination (Join-Path $payload 'Mods') -Force
 }
}
# Fingerprints are metadata, not redistributed third-party implementations.
$expected=@(Get-ChildItem -LiteralPath (Join-Path $GamePath 'Mods') -File -Filter 'IronNest*.dll'|ForEach-Object{
 [pscustomobject]@{Path=('Mods\'+$_.Name);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
$expected+=Get-ChildItem -LiteralPath $payload -File -Recurse|ForEach-Object{
 [pscustomobject]@{Path=$_.FullName.Substring($payload.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
}
foreach($relative in @('Iron Nest Heavy Turret Simulator.exe','GameAssembly.dll','MelonLoader\net6\MelonLoader.dll')){
 $expected+=[pscustomobject]@{Path=$relative;SHA256=(Get-FileHash -LiteralPath (Join-Path $GamePath $relative)).Hash}
}
$expected=$expected|Group-Object Path|ForEach-Object{$_.Group[-1]}
$checkDir=Join-Path $payload 'OpenNestUpdate'
New-Item -ItemType Directory $checkDir -Force|Out-Null
$expected|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $checkDir 'ExpectedMods.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Check-Install.ps1') -Destination $checkDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Package-Safety.ps1') -Destination $checkDir -Force
$checkCmd='@echo off'+"`r`n"+'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0OpenNestUpdate\Check-Install.ps1" -GamePath "%~dp0." -ReportPath "%~dp0IRON_NEST_ELLENORZES.txt"'+"`r`n"+'pause'+"`r`n"
[IO.File]::WriteAllText((Join-Path $payload 'IRON_NEST_ELLENORZES.cmd'),$checkCmd,[Text.Encoding]::ASCII)
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
$install=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Install.ps1')).Replace('# EMBEDDED_PACKAGE_SAFETY',[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Package-Safety.ps1')))
[IO.File]::WriteAllText((Join-Path $stage 'JAVITAS.cmd'),($header -replace "`r?`n","`r`n")+"`r`n"+$install,[Text.UTF8Encoding]::new($false))
Copy-Item (Join-Path $repo 'LICENSE') $stage -Force
Copy-Item (Join-Path $repo 'docs\FORK_STATUS.md') (Join-Path $stage 'README.md') -Force
git -C $repo archive --format=zip -o (Join-Path $stage 'source.zip') HEAD
if($LASTEXITCODE){throw 'Corresponding source archive failed'}
$zip=$stage+'.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Test-CoopPayload $payload @(Get-Content (Join-Path $stage 'SHA256.json') -Raw|ConvertFrom-Json)
$auditExtract=Join-Path $repo ('artifacts\ArchiveAudit_'+[guid]::NewGuid().ToString('N'))
Expand-CoopSafeArchive $zip $auditExtract
& (Join-Path $PSScriptRoot 'Build-SingleFile.ps1') -PackageZip $zip -OutputPath ($stage+'.cmd')
Get-Item $zip,($stage+'.cmd') | Select-Object FullName,Length
