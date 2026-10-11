# Builds the plugin and copies it into the local Slap City install for testing.
param([switch]$NoDeploy)
$ErrorActionPreference = 'Stop'
$env:PATH = "$env:USERPROFILE\tools\dotnet;$env:PATH"
$env:DOTNET_ROOT = "$env:USERPROFILE\tools\dotnet"
$root = Split-Path $PSScriptRoot -Parent
$game = 'D:\steam\steamapps\common\Slap City'
$out = "$root\src\bin\out"
$blog = "$env:TEMP\slaprift-build.log"
dotnet build "$root\src\SlapRift.csproj" -c Release -o $out -nologo -v q "-flp:logfile=$blog;verbosity=quiet" | Out-Null
$errs = Get-Content $blog | ForEach-Object { if ($_ -match '\\src\\(.+?): error (CS\d+): (.+?) \[') { "$($Matches[1]): $($Matches[2]) $($Matches[3])" } } | Sort-Object -Unique
if ($LASTEXITCODE -ne 0) { $errs; throw 'build failed' }
'build ok'
if (-not $NoDeploy) {
    $dst = "$game\BepInEx\plugins\SlapRift"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item "$out\SlapRift.dll" $dst -Force
    # third-party runtime pieces that ship with the release (see THIRD-PARTY.md)
    $nat = "$game\BepInEx\SlapRift"; New-Item -ItemType Directory -Force $nat | Out-Null
    Copy-Item "$env:USERPROFILE\tools\dl\zstd\x\zstd-v1.5.7-win64\dll\libzstd.dll" $nat -Force
    New-Item -ItemType Directory -Force "$nat\vgmstream" | Out-Null
    Copy-Item "$env:USERPROFILE\tools\dl\vgmstream\x\*" "$nat\vgmstream" -Recurse -Force
    'deployed to ' + $dst
}

