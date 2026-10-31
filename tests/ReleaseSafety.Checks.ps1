$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'scripts\syncfix\Package-Safety.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$script:passed=0
function Reject([scriptblock]$Action){$rejected=$false;try{& $Action}catch{$rejected=$true};if(-not $rejected){throw 'Unsafe input accepted'};$script:passed++}
$work=Join-Path $repo ('artifacts\ReleaseSafety_'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work|Out-Null
foreach($path in @('../evil.dll','/absolute.dll','C:\evil.dll','Mods\x.dll:stream','Mods\..\x','Mods\CON.dll','Mods\x.','Mods//x','Mods\x ')) {Reject {Assert-CoopRelativePath $path|Out-Null}}
$payload=Join-Path $work 'payload';New-Item -ItemType Directory -Path $payload|Out-Null
$manifest=@()
foreach($name in Get-CoopPayloadNames){$path=Join-Path $payload $name;New-Item -ItemType Directory -Path (Split-Path $path) -Force|Out-Null;[IO.File]::WriteAllText($path,'fixture');$manifest+=[pscustomobject]@{Path=$name;SHA256=(Get-FileHash -LiteralPath $path).Hash}}
Test-CoopPayload $payload $manifest;$script:passed++
$fromJson=$manifest|ConvertTo-Json|ConvertFrom-Json
Test-CoopPayload $payload $fromJson;$script:passed++
Reject {Test-CoopPayload $payload @()}
Reject {Test-CoopPayload $payload ($manifest+$manifest[0])}
Reject {Test-CoopPayload $payload $manifest[0..7]}
$bad=@($manifest|ForEach-Object {[pscustomobject]@{Path=$_.Path;SHA256=$_.SHA256}});$bad[0].SHA256=''
Reject {Test-CoopPayload $payload $bad}
$bad[0].SHA256='A'*64;Reject {Test-CoopPayload $payload $bad}
$bad[0].Path='Mods/extra.exe';Reject {Test-CoopPayload $payload $bad}
[IO.File]::WriteAllText((Join-Path $payload 'unlisted.ps1'),'fixture')
Reject {Test-CoopPayload $payload $manifest}
function MakeArchive([string[]]$Names,[switch]$Link,[switch]$Large){
 $zip=Join-Path $work ([guid]::NewGuid().ToString('N')+'.zip')
 $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($name in $Names){$entry=$archive.CreateEntry($name);if($Link){$entry.ExternalAttributes=0xA000 -shl 16};$stream=$entry.Open();try{if($Large){$block=New-Object byte[] (1MB);for($i=0;$i -lt 33;$i++){$stream.Write($block,0,$block.Length)}}else{$stream.WriteByte(65)}}finally{$stream.Dispose()}}}finally{$archive.Dispose()}
 return $zip
}
$roots=@('JAVITAS.cmd','LICENSE','README.md','SHA256.json','source.zip')
foreach($evil in @('../outside.ps1','payload/Mods/x.dll:ads','C:/evil','payload/Mods/OpenNestCoop.MelonMod.dll/','evil.exe')){$zip=MakeArchive ($roots+$evil);$dest=Join-Path $work ([guid]::NewGuid().ToString('N'));Reject {Expand-CoopSafeArchive $zip $dest};if(Test-Path -LiteralPath $dest){throw 'Archive wrote before full preflight rejection'}}
$zip=MakeArchive ($roots+'readme.md');Reject {Expand-CoopSafeArchive $zip (Join-Path $work 'duplicate')}
$zip=MakeArchive $roots -Link;Reject {Expand-CoopSafeArchive $zip (Join-Path $work 'links')}
$zip=MakeArchive @('README.md') -Large;Reject {Expand-CoopSafeArchive $zip (Join-Path $work 'oversize')}
$zip=MakeArchive $roots;$dest=Join-Path $work 'good';Expand-CoopSafeArchive $zip $dest;$script:passed++
Reject {Expand-CoopSafeArchive $zip $dest}
$outside=Join-Path $work 'outside';New-Item -ItemType Directory -Path $outside|Out-Null
$junction=Join-Path $work 'junction';New-Item -ItemType Junction -Path $junction -Target $outside|Out-Null
Reject {Resolve-CoopSafePath $junction 'x.dll'|Out-Null}
Reject {Resolve-CoopSafePath $work 'junction/x.dll'|Out-Null}
Reject {@(Get-CoopSafeFiles $work)|Out-Null}
Write-Output ($script:passed.ToString()+' release safety checks passed (including junction rejection).')
