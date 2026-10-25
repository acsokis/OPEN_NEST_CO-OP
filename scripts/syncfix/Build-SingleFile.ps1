param([Parameter(Mandatory=$true)][string]$PackageZip,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
$wrapper=@'
@echo off
setlocal
title Open Nest syncfix installer
set "IRON_NEST_UPDATE_SELF=%~f0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -Command "$s=[IO.File]::ReadAllText($env:IRON_NEST_UPDATE_SELF);$parts=[regex]::Split($s,'(?m)^# UPDATE_SCRIPT\r?$',2);$body=[regex]::Split($parts[1],'(?m)^# PAYLOAD_BASE64\r?$',2)[0]; & ([scriptblock]::Create($body))"
pause
exit /b
# UPDATE_SCRIPT
$ErrorActionPreference='Stop'
try{
 $self=[IO.File]::ReadAllText($env:IRON_NEST_UPDATE_SELF)
 $payload=[regex]::Split($self,'(?m)^# PAYLOAD_BASE64\r?$',2)[1]
 $work=Join-Path $env:TEMP ('OpenNestSyncfix_'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory $work|Out-Null
 $zip=Join-Path $work 'update.zip'
 [IO.File]::WriteAllBytes($zip,[Convert]::FromBase64String($payload))
 Expand-Archive -LiteralPath $zip -DestinationPath $work
 $scriptText=[regex]::Split([IO.File]::ReadAllText((Join-Path $work 'JAVITAS.cmd')),'(?m)^# POWERSHELL\r?$',2)[1]
 & ([scriptblock]::Create($scriptText)) -PackageRoot $work
}catch{Write-Host ('HIBA: '+$_.Exception.Message) -ForegroundColor Red;exit 1}
# PAYLOAD_BASE64
'@
$base64=[Convert]::ToBase64String([IO.File]::ReadAllBytes($PackageZip),[Base64FormattingOptions]::InsertLineBreaks)
[IO.File]::WriteAllText($OutputPath,($wrapper -replace "`r?`n","`r`n")+"`r`n"+$base64,[Text.Encoding]::ASCII)
