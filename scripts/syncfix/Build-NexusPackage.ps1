param([Parameter(Mandatory=$true)][string]$PackageZip)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Package-Safety.ps1')
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$expected='AAA6239F5551B0FC51620FA252673B99C8CB9D406AF754118F9F05735D364D8A'
if((Get-FileHash -LiteralPath $PackageZip -Algorithm SHA256).Hash -ne $expected){throw 'Expected the exact published syncfix.11.1 package'}
$work=Join-Path $repo ('artifacts\NexusBuild_'+[guid]::NewGuid().ToString('N'))
$original=Join-Path $work 'verified-release'
Expand-CoopSafeArchive $PackageZip $original
$payload=Join-Path $original 'payload'
$manifest=Get-Content -LiteralPath (Join-Path $original 'SHA256.json') -Raw|ConvertFrom-Json
Test-CoopPayload $payload $manifest
$stage=Join-Path $work 'manual'
$files=@('Mods/OpenNestCoop.MelonMod.dll','UserLibs/LiteNetLib.dll',
 'UserLibs/OpenNestUIKit.API.dll','UserLibs/SharpGLTF.Core.dll','UserLibs/SharpGLTF.Runtime.dll')
foreach($name in $files){
 $dest=Resolve-CoopSafePath $stage $name
 [IO.Directory]::CreateDirectory((Split-Path -Parent $dest))|Out-Null
 Copy-Item -LiteralPath (Resolve-CoopSafePath $payload $name) -Destination $dest
 if((Get-FileHash -LiteralPath $dest).Hash -ne (Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash){throw 'Released DLL bytes changed'}
}
$docs=Join-Path $stage 'OpenNestCoopDocs';[IO.Directory]::CreateDirectory($docs)|Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repo 'CHANGELOG.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repo 'docs\THIRD_PARTY_NOTICES.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repo 'docs\RELEASE_SECURITY.md') -Destination $docs
Copy-Item -LiteralPath (Join-Path $repo 'docs\SYNCFIX_11_TEST_HU.md') -Destination (Join-Path $docs 'VALIDATION.md')
$readme=[IO.File]::ReadAllText((Join-Path $repo 'docs\INSTALL.md'))
$readme=$readme.Replace('(SYNCFIX_11_TEST_HU.md)','(VALIDATION.md)').Replace('(../CHANGELOG.md)','(CHANGELOG.md)')
[IO.File]::WriteAllText((Join-Path $docs 'README.md'),$readme,[Text.UTF8Encoding]::new($false))
$source=@'
Open Nest Co-op acsokis fork 0.2.1-syncfix.11.1
Binary/source commit: d410603044326fee17dfa39bb285353fd5506b16
Corresponding source ZIP: https://github.com/acsokis/OPEN_NEST_CO-OP/archive/refs/tags/syncfix.11.1.zip
Source browser: https://github.com/acsokis/OPEN_NEST_CO-OP/tree/d410603044326fee17dfa39bb285353fd5506b16
Original upstream: https://github.com/1499501762/OPEN_NEST_CO-OP
Documentation-only updates after the binary release are on the fork's main branch.
This manual repack preserves the released DLL bytes. License: included AGPL-3.0;
separate MIT notices apply to LiteNetLib and SharpGLTF. No game or loader files included.
'@
[IO.File]::WriteAllText((Join-Path $docs 'SOURCE.txt'),$source,[Text.Encoding]::ASCII)
$allowed=$files+@('OpenNestCoopDocs/LICENSE','OpenNestCoopDocs/CHANGELOG.md',
 'OpenNestCoopDocs/THIRD_PARTY_NOTICES.md','OpenNestCoopDocs/RELEASE_SECURITY.md',
 'OpenNestCoopDocs/VALIDATION.md','OpenNestCoopDocs/README.md','OpenNestCoopDocs/SOURCE.txt')
$hashes=@(Get-CoopSafeFiles $stage|ForEach-Object {
 $relative=$_.FullName.Substring($stage.Length+1).Replace('\','/')
 if($relative -cnotin $allowed){throw 'Unexpected Nexus package file'}
 [pscustomobject]@{Path=$relative;SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
if($hashes.Count -ne $allowed.Count){throw 'Nexus payload is incomplete'}
$hashes|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $docs 'SHA256.json') -Encoding UTF8
$output=Join-Path $repo 'artifacts\OPEN_NEST_SYNCFIX_11_1_MANUAL.zip'
if(Test-Path -LiteralPath $output){throw 'Manual archive already exists; preserve it and choose a new release instead of overwriting'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$writer=[IO.Compression.ZipFile]::Open($output,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($file in @(Get-CoopSafeFiles $stage)) {
  $relative=$file.FullName.Substring($stage.Length+1).Replace('\','/')
  $entry=$writer.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
  $inputStream=[IO.File]::OpenRead($file.FullName);$outputStream=$entry.Open()
  try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose();$inputStream.Dispose()}
 }
}finally{$writer.Dispose()}
$archive=[IO.Compression.ZipFile]::OpenRead($output)
try {
 $names=@($archive.Entries|ForEach-Object {$_.FullName.Replace('\','/')})
 if($names.Count -ne $allowed.Count+1){throw 'Archive entry count mismatch'}
 foreach($name in $names){
  if($name -cnotin ($allowed+@('OpenNestCoopDocs/SHA256.json')) -or $name -match '\.(zip|7z|rar|cmd|bat|ps1|exe)$'){throw 'Nexus archive contains an unapproved file'}
 }
}finally{$archive.Dispose()}
Get-Item -LiteralPath $output|Select-Object FullName,Length
Write-Output ('SHA256 '+(Get-FileHash -LiteralPath $output).Hash)
Write-Output 'PASS: five released DLLs unchanged; no nested archives/install scripts; exact 13-file archive.'
