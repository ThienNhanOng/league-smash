# Stops the running Slap City (by exact PID), rebuilds and deploys the plugin, relaunches and waits until it has loaded.
# Then sends any dev commands given (one per argument) and waits for each to finish.
param([string[]]$Commands = @(), [int]$Wait = 40)
$ErrorActionPreference = 'Stop'
$bep = 'D:\steam\steamapps\common\Slap City\BepInEx'
$p = Get-Process 'Slap City' -ErrorAction SilentlyContinue
if ($p) { Stop-Process -Id $p.Id -Force; Start-Sleep 2 }
& "$PSScriptRoot\build.ps1"
Start-Process 'steam://rungameid/725480'
$t0 = Get-Date
do { Start-Sleep 1; $log = Get-Content "$bep\LogOutput.log" -Raw -ErrorAction SilentlyContinue }
until (((Get-Item "$bep\LogOutput.log").LastWriteTime -gt $t0 -and $log -match 'Chainloader startup complete') -or ((Get-Date) - $t0).TotalSeconds -gt $Wait)
Start-Sleep 4
foreach ($c in $Commands) {
    Set-Content "$bep\slaprift-cmd.txt" $c
    $t1 = Get-Date
    do { Start-Sleep 1 } until (-not (Test-Path "$bep\slaprift-cmd.txt") -or ((Get-Date) - $t1).TotalSeconds -gt 30)
    Start-Sleep 2
}
Get-Content "$bep\LogOutput.log" | Select-String 'Slap Rift|Error|Exception' | Select-Object -Last 15
