# Builds the release archive: the plugin, its native helpers, BepInEx (Melty installs no loader for Slap City),
# readme and licences. Nothing from Slap City or League of Legends goes in.
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot\build.ps1" -NoDeploy
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist ('stage-' + $Version + '-' + (Get-Date -Format 'HHmmss'))
$dl = Join-Path $env:USERPROFILE 'tools\dl'
New-Item -ItemType Directory -Force (Join-Path $stage 'BepInEx\plugins\SlapRift'), (Join-Path $stage 'BepInEx\SlapRift\licenses'), (Join-Path $stage 'BepInEx\SlapRift\vgmstream') | Out-Null
Copy-Item (Join-Path $dl 'bepinex\x\*') $stage -Recurse -Force
foreach ($junk in @((Join-Path $stage 'changelog.txt')) + (Get-ChildItem (Join-Path $stage 'BepInEx\core') -Filter *.xml | ForEach-Object FullName)) {
    if (Test-Path -LiteralPath $junk) { [IO.File]::Delete($junk) }
}
Copy-Item (Join-Path $root 'src\bin\out\SlapRift.dll') (Join-Path $stage 'BepInEx\plugins\SlapRift')
Copy-Item (Join-Path $dl 'zstd\x\zstd-v1.5.7-win64\dll\libzstd.dll') (Join-Path $stage 'BepInEx\SlapRift')
Copy-Item (Join-Path $dl 'vgmstream\x\*') (Join-Path $stage 'BepInEx\SlapRift\vgmstream') -Recurse
Copy-Item (Join-Path $dl 'licenses\*') (Join-Path $stage 'BepInEx\SlapRift\licenses')
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'THIRD-PARTY.md') (Join-Path $stage 'BepInEx\plugins\SlapRift')
$zip = Join-Path $dist ('SlapRift-' + $Version + '.zip')
if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$z = [IO.Compression.ZipFile]::OpenRead($zip)
$entries = $z.Entries | Where-Object { $_.Name -ne '' } | ForEach-Object { [pscustomobject]@{ path = $_.FullName.Replace('\', '/'); size = $_.Length } }
$z.Dispose()
$entries | ConvertTo-Json -Compress | Set-Content (Join-Path $dist 'entries.json') -Encoding ascii
$h = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$zip  $((Get-Item $zip).Length) bytes  sha256 $h  $($entries.Count) files"
