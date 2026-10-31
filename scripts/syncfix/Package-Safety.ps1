# Shared by the package builder, installer and embedded CMD wrappers.
# No execution, network access or extraction happens merely by loading this file.
function Get-CoopPayloadNames {
 @('Mods/OpenNestCoop.MelonMod.dll','UserLibs/LiteNetLib.dll','UserLibs/OpenNestUIKit.API.dll',
 'UserLibs/SharpGLTF.Core.dll','UserLibs/SharpGLTF.Runtime.dll','OpenNestUpdate/Check-Install.ps1',
 'OpenNestUpdate/Package-Safety.ps1','OpenNestUpdate/ExpectedMods.json','IRON_NEST_ELLENORZES.cmd')
}
function Assert-CoopRelativePath([string]$Path) {
 if(-not $Path -or $Path.Length -gt 240 -or $Path -match '[\x00-\x1f:<>"|?*]' -or $Path -match '^[\\/]'){throw 'Invalid relative package path'}
 $name=$Path.Replace('\','/')
 foreach($part in $name.Split('/')) {
  if(-not $part -or $part -in @('.','..') -or $part -match '[. ]$' -or $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)'){throw 'Invalid package path component'}
 }
 return $name
}
function Assert-CoopNoReparse([string]$Path) {
 $full=[IO.Path]::GetFullPath($Path)
 if($full.StartsWith('\\')){throw 'Network installation paths are not supported'}
 $cursor=$full
 while($cursor) {
  $item=Get-Item -LiteralPath $cursor -Force -ErrorAction SilentlyContinue
  if($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw ('Reparse point rejected: '+$cursor)}
  $parent=Split-Path -Parent $cursor
  if($parent -eq $cursor){break};$cursor=$parent
 }
}
function Resolve-CoopSafePath([string]$Root,[string]$Relative) {
 $relativeName=Assert-CoopRelativePath $Relative
 $base=[IO.Path]::GetFullPath($Root).TrimEnd('\')
 $target=[IO.Path]::GetFullPath((Join-Path $base $relativeName.Replace('/','\')))
 if(-not $target.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Package path escaped root'}
 Assert-CoopNoReparse $target
 return $target
}
function Get-CoopSafeFiles([string]$Root) {
 Assert-CoopNoReparse $Root
 $queue=New-Object 'Collections.Generic.Queue[string]';$queue.Enqueue($Root)
 $count=0
 while($queue.Count) {
  foreach($item in Get-ChildItem -LiteralPath $queue.Dequeue() -Force) {
   if(++$count -gt 4096){throw 'Directory entry limit exceeded'}
   if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Reparse point rejected: '+$item.FullName)}
   if($item.PSIsContainer){$queue.Enqueue($item.FullName)}else{$item}
  }
 }
}
function Test-CoopPayload([string]$Payload,[object[]]$Manifest) {
 $required=@(Get-CoopPayloadNames)
 $allowed=$required+@('Mods/IronNestCatCollision.dll','Mods/IronNestHighTide.dll')
 if(-not $Manifest -or $Manifest.Count -gt $allowed.Count){throw 'Invalid payload manifest size'}
 $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
 foreach($entry in $Manifest) {
  $name=Assert-CoopRelativePath ([string]$entry.Path)
  if($name -cnotin $allowed -or -not $seen.Add($name) -or [string]$entry.SHA256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Invalid, duplicate or unapproved manifest entry'}
  $file=Resolve-CoopSafePath $Payload $name
  $info=Get-Item -LiteralPath $file
  if($info.PSIsContainer -or $info.Length -gt 32MB){throw 'Invalid payload file size/type'}
  if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.SHA256){throw ('Payload hash mismatch: '+$name)}
 }
 foreach($name in $required){if(-not $seen.Contains($name)){throw ('Required payload file missing: '+$name)}}
 $base=[IO.Path]::GetFullPath($Payload).TrimEnd('\')
 foreach($file in @(Get-CoopSafeFiles $base)) {
  $name=$file.FullName.Substring($base.Length+1).Replace('\','/')
  if(-not $seen.Contains($name)){throw ('Unlisted payload file: '+$name)}
 }
}
function Expand-CoopSafeArchive([string]$Zip,[string]$Destination) {
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 Assert-CoopNoReparse $Destination
 if((Test-Path -LiteralPath $Destination) -and @(Get-ChildItem -LiteralPath $Destination -Force).Count){throw 'Extraction directory must be empty'}
 $allowed=@('JAVITAS.cmd','LICENSE','README.md','SHA256.json','source.zip')+@((Get-CoopPayloadNames)|ForEach-Object {'payload/'+$_})
 $allowed+=@('payload/Mods/IronNestCatCollision.dll','payload/Mods/IronNestHighTide.dll')
 $archive=[IO.Compression.ZipFile]::OpenRead($Zip)
 try {
  if($archive.Entries.Count -gt 32){throw 'Archive entry limit exceeded'}
  $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
  [long]$total=0
  # Validate the entire archive before creating any entry.
  foreach($entry in $archive.Entries) {
   $name=Assert-CoopRelativePath $entry.FullName
   if($name -cnotin $allowed -or -not $seen.Add($name)){throw 'Unapproved or duplicate archive entry'}
   if((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000 -or ($entry.ExternalAttributes -band 0x400)){throw 'Archive links are not supported'}
   $total+=$entry.Length
   if($entry.Length -gt 32MB -or $total -gt 96MB){throw 'Archive expansion limit exceeded'}
   [void](Resolve-CoopSafePath $Destination $name)
  }
  foreach($name in @('JAVITAS.cmd','LICENSE','README.md','SHA256.json','source.zip')){if(-not $seen.Contains($name)){throw ('Required archive file missing: '+$name)}}
  [IO.Directory]::CreateDirectory($Destination)|Out-Null
  foreach($entry in $archive.Entries) {
   $dest=Resolve-CoopSafePath $Destination $entry.FullName
   [IO.Directory]::CreateDirectory((Split-Path -Parent $dest))|Out-Null
   $inputStream=$entry.Open();$outputStream=$null
   try {
    $outputStream=[IO.File]::Open($dest,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $buffer=New-Object byte[] 65536;[long]$written=0
    while(($read=$inputStream.Read($buffer,0,$buffer.Length)) -gt 0){$written+=$read;if($written -gt $entry.Length -or $written -gt 32MB){throw 'Archive stream exceeded declared limit'};$outputStream.Write($buffer,0,$read)}
    if($written -ne $entry.Length){throw 'Truncated archive entry'}
   } finally {if($outputStream){$outputStream.Dispose()};$inputStream.Dispose()}
  }
 } finally {$archive.Dispose()}
}
