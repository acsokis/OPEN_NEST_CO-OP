param([Parameter(Mandatory=$true)][string]$Repo)
$ErrorActionPreference='Stop'
$env:GCM_INTERACTIVE='Never'
$env:GIT_TERMINAL_PROMPT='0'
$credential=$null; $headers=$null; $parts=$null
try {
 if(git -C $Repo status --porcelain){throw 'Clean committed checkout required'}
 $parts="protocol=https`nhost=github.com`n`n"|git -c credential.interactive=never credential fill 2>$null
 if($LASTEXITCODE){throw 'GitHub credential unavailable'}
 $credential=@{}
 foreach($line in $parts){$pair=$line -split '=',2;if($pair.Count -eq 2){$credential[$pair[0]]=$pair[1]}}
 if(-not $credential.password){throw 'GitHub credential unavailable'}
 $headers=@{Authorization=('Bearer '+$credential.password);Accept='application/vnd.github+json';'User-Agent'='acsokis-opennest-release';'X-GitHub-Api-Version'='2022-11-28'}
 $user=Invoke-RestMethod -Uri 'https://api.github.com/user' -Headers $headers
 if($user.login -ne 'acsokis'){throw 'Unexpected GitHub account; publish cancelled'}
 $commit=git -C $Repo rev-parse HEAD
 $version=(Get-Item -LiteralPath (Join-Path $Repo 'src\OpenNestCoop.MelonMod\bin\Debug\net6.0\OpenNestCoop.MelonMod.dll')).VersionInfo.ProductVersion
 if(($version -split '\+',2)[0] -ne '0.2.1-syncfix.11.1' -or -not $version.Contains($commit)){throw 'Release binary is not built from current syncfix.11.1 commit'}
 $zip=Join-Path $Repo ('artifacts\OpenNestCoop-'+$version+'.zip')
 if(-not(Test-Path -LiteralPath $zip)){throw 'Package missing'}
 $releaseZip=Join-Path $Repo 'artifacts\OPEN_NEST_SYNCFIX_11_1.zip'
 Copy-Item -LiteralPath $zip -Destination $releaseZip -Force
 $cmd=Join-Path $Repo 'artifacts\IRON_NEST_UPDATE.cmd'
 $cmdText=[IO.File]::ReadAllText((Join-Path $Repo 'scripts\syncfix\Update-From-GitHub.cmd'))
 $cmdText=$cmdText.Replace('# EMBEDDED_PACKAGE_SAFETY',[IO.File]::ReadAllText((Join-Path $Repo 'scripts\syncfix\Package-Safety.ps1'))).Replace('__PACKAGE_SHA256__',(Get-FileHash -LiteralPath $releaseZip -Algorithm SHA256).Hash)
 [IO.File]::WriteAllText($cmd,($cmdText -replace "`r?`n","`r`n"),[Text.Encoding]::ASCII)
 $body=@'
Experimental acsokis fork: 0.2.1-syncfix.11.1 release hardening, contributed by Gabor Kocsis (@acsokis) with AI-assisted implementation. Upstream attribution and AGPL-3.0 are preserved.

Also adds independently implemented magazine improvements: native-accepted turn filtering, per-gun queued retries, bounded/deduplicated mission-scoped actions and deferred host magazine corrections. Entity health/lifecycle and magazine state reject guest overwrites. 148 managed/network checks and 30 package-safety checks pass. This is not a full port of the separate BepInEx 2.5.1 mod; native gameplay verification and newer game-mechanic coverage remain incomplete.

The new updater pins the ZIP hash at build time as well as checking GitHub's digest. Archive extraction rejects unexpected/duplicate paths, links, traversal, alternate data streams and oversized content. Installation restricts files to an exact allowlist, checks manifest completeness and refuses reparse-point destinations. These are hardening measures, not a malware-free certification or a substitute for trusting the downloaded updater itself. See docs/RELEASE_SECURITY.md and CHANGELOG.md.

Download the NEW IRON_NEST_UPDATE.cmd and run it with Iron Nest closed on both PCs. Older copies remain pinned to older releases. This command downloads the release ZIP, verifies GitHub's SHA256 digest, backs up replaced co-op files and installs IRON_NEST_ELLENORZES.cmd in the game directory.

Fixes native mission selection by following the host's exact MissionID instead of the first card sharing a Unity scene. The tester confirmed matching background maps in two complete game instances on one PC.

Also implements acknowledged document replication, local TCP reconnection fixes, stable gun routing, guest door/light intents, native marker endpoints, exact requisition card/slot routing, Modernization map purchase commands and experimental scout lifecycle/audio replication.

Validation: MelonLoader build passed; 136 automated checks passed; native local host/guest handshake and matching backgrounds were verified. Full F1-F3, purchase, drawing, door/light, scout and independent-PC regressions remain incomplete. See docs/SYNCFIX_11_TEST_HU.md. This is not a claim that every multiplayer function is fixed.

The public ZIP includes corresponding committed source and license. It excludes HDR/ReShade, Modernization and other third-party mod implementations, the game, generated interop assemblies, personal saves and private test logs. Both PCs need matching desired third-party mods. Normal launches do not enable the isolated test profile's 960x540/20 FPS cap.
'@
 $base='https://api.github.com/repos/acsokis/OPEN_NEST_CO-OP'
 $release=$null
 try{$release=Invoke-RestMethod -Uri ($base+'/releases/tags/syncfix.11.1') -Headers $headers}catch{if([int]$_.Exception.Response.StatusCode -ne 404){throw}}
 if(-not $release){
  $json=@{tag_name='syncfix.11.1';target_commitish=$commit;name='syncfix.11.1 - release hardening';body=$body;draft=$true;prerelease=$true}|ConvertTo-Json
  $release=Invoke-RestMethod -Method Post -Uri ($base+'/releases') -Headers $headers -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json))
 }
 foreach($file in @($releaseZip,$cmd)){
  $name=[IO.Path]::GetFileName($file)
  $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
  $asset=@($release.assets|Where-Object name -eq $name)
  if($asset.Count){if($asset[0].digest -ne ('sha256:'+$hash)){throw ('Existing release asset differs: '+$name)};continue}
  $upload='https://uploads.github.com/repos/acsokis/OPEN_NEST_CO-OP/releases/'+$release.id+'/assets?name='+[uri]::EscapeDataString($name)
  $added=Invoke-RestMethod -Method Post -Uri $upload -Headers $headers -ContentType 'application/octet-stream' -InFile $file
  if($added.digest -ne ('sha256:'+$hash)){throw ('Uploaded digest mismatch: '+$name)}
 }
 $published=Invoke-RestMethod -Method Patch -Uri ($base+'/releases/'+$release.id) -Headers $headers -ContentType 'application/json' -Body (@{draft=$false;prerelease=$true;body=$body}|ConvertTo-Json)
 [pscustomobject]@{Release=$published.html_url;Commit=$commit;Installer=$cmd;PackageSHA256=(Get-FileHash -LiteralPath $releaseZip).Hash}|ConvertTo-Json
} finally {
 $headers=$null;$parts=$null
 if($credential){$credential.Clear()};$credential=$null
}
