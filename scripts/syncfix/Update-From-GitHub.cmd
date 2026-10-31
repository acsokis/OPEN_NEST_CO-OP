@echo off
setlocal
title Iron Nest syncfix.11.1 frissites
set "IRON_NEST_UPDATE_SELF=%~f0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -Command "$s=[IO.File]::ReadAllText($env:IRON_NEST_UPDATE_SELF);$p=[regex]::Split($s,'(?m)^# UPDATE_SCRIPT\r?$',2)[1]; & ([scriptblock]::Create($p))"
set "IRON_NEST_UPDATE_RESULT=%errorlevel%"
pause
exit /b %IRON_NEST_UPDATE_RESULT%
# UPDATE_SCRIPT
$ErrorActionPreference='Stop'
# EMBEDDED_PACKAGE_SAFETY
try{
 $pinned='__PACKAGE_SHA256__'
 if($pinned -notmatch '^[a-fA-F0-9]{64}$' -or -not(Get-Command Expand-CoopSafeArchive -ErrorAction SilentlyContinue)){throw 'Unbuilt updater template; download the published CMD.'}
 [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
 $api='https://api.github.com/repos/acsokis/OPEN_NEST_CO-OP/releases/tags/syncfix.11.1'
 Write-Host 'Open Nest syncfix.11.1 letoltese az acsokis fork kiadasabol...'
 $release=Invoke-RestMethod -Uri $api -Headers @{'User-Agent'='IronNestSyncfixUpdater';Accept='application/vnd.github+json'}
 if($release.tag_name -cne 'syncfix.11.1' -or $release.draft){throw 'Unexpected release identity'}
 $asset=@($release.assets|Where-Object name -eq 'OPEN_NEST_SYNCFIX_11_1.zip')
 if($asset.Count -ne 1){throw 'A kiadasban nincs egyertelmu frissitocsomag.'}
 $digest=[string]$asset[0].digest
 if($digest -notmatch '^sha256:([a-fA-F0-9]{64})$'){throw 'A GitHub nem adott SHA256 digestet; ellenorzes nelkul nem fut a telepito.'}
 $expected=$Matches[1]
 if($expected -ne $pinned -or $asset[0].size -gt 16MB -or $asset[0].size -le 0){throw 'Release asset differs from this updater build'}
 if($asset[0].browser_download_url -cne 'https://github.com/acsokis/OPEN_NEST_CO-OP/releases/download/syncfix.11.1/OPEN_NEST_SYNCFIX_11_1.zip'){throw 'Unexpected download URL'}
 $work=Join-Path $env:TEMP ('OpenNestUpdate11_1_'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory $work|Out-Null
 $zip=Join-Path $work 'update.zip'
 Invoke-WebRequest -UseBasicParsing -Uri $asset[0].browser_download_url -OutFile $zip
 if((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $pinned){throw 'A letoltott csomag SHA256 ellenorzese sikertelen.'}
 $unpacked=Join-Path $work 'verified'
 Expand-CoopSafeArchive $zip $unpacked
 $installer=Join-Path $unpacked 'JAVITAS.cmd'
 if(-not(Test-Path -LiteralPath $installer)){throw 'Hianyzik a telepito a csomagbol.'}
 $script=[regex]::Split([IO.File]::ReadAllText($installer),'(?m)^# POWERSHELL\r?$',2)[1]
 if(-not $script){throw 'Hibas telepitocsomag.'}
 & ([scriptblock]::Create($script)) -PackageRoot $unpacked
}catch{Write-Host ('HIBA: '+$_.Exception.Message) -ForegroundColor Red;exit 1}
