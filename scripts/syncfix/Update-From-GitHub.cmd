@echo off
setlocal
title Iron Nest syncfix.11 frissites
set "IRON_NEST_UPDATE_SELF=%~f0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -Command "$s=[IO.File]::ReadAllText($env:IRON_NEST_UPDATE_SELF);$p=[regex]::Split($s,'(?m)^# UPDATE_SCRIPT\r?$',2)[1]; & ([scriptblock]::Create($p))"
set "IRON_NEST_UPDATE_RESULT=%errorlevel%"
pause
exit /b %IRON_NEST_UPDATE_RESULT%
# UPDATE_SCRIPT
$ErrorActionPreference='Stop'
try{
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 $api='https://api.github.com/repos/acsokis/OPEN_NEST_CO-OP/releases/tags/syncfix.11'
 Write-Host 'Open Nest syncfix.11 letoltese az acsokis fork kiadasabol...'
 $release=Invoke-RestMethod -Uri $api -Headers @{'User-Agent'='IronNestSyncfixUpdater';Accept='application/vnd.github+json'}
 $asset=@($release.assets|Where-Object name -eq 'OPEN_NEST_SYNCFIX_11.zip')
 if($asset.Count -ne 1){throw 'A kiadasban nincs egyertelmu frissitocsomag.'}
 $digest=[string]$asset[0].digest
 if($digest -notmatch '^sha256:([a-fA-F0-9]{64})$'){throw 'A GitHub nem adott SHA256 digestet; ellenorzes nelkul nem fut a telepito.'}
 $expected=$Matches[1]
 $work=Join-Path $env:TEMP ('OpenNestUpdate11_'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory $work|Out-Null
 $zip=Join-Path $work 'update.zip'
 Invoke-WebRequest -UseBasicParsing -Uri $asset[0].browser_download_url -OutFile $zip
 if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected){throw 'A letoltott csomag SHA256 ellenorzese sikertelen.'}
 Expand-Archive -LiteralPath $zip -DestinationPath $work
 $installer=Join-Path $work 'JAVITAS.cmd'
 if(-not(Test-Path -LiteralPath $installer)){throw 'Hianyzik a telepito a csomagbol.'}
 $script=[regex]::Split([IO.File]::ReadAllText($installer),'(?m)^# POWERSHELL\r?$',2)[1]
 if(-not $script){throw 'Hibas telepitocsomag.'}
 & ([scriptblock]::Create($script)) -PackageRoot $work
}catch{Write-Host ('HIBA: '+$_.Exception.Message) -ForegroundColor Red;exit 1}
