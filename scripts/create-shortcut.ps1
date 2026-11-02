$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $projectRoot 'Anki Stats.lnk'))
$shortcut.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$launcher = Join-Path $PSScriptRoot 'launch.ps1'
$shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcher + '"'
$shortcut.WorkingDirectory = $projectRoot
$shortcut.WindowStyle = 7
$shortcut.IconLocation = (Join-Path $projectRoot 'artifacts\AnkiStats\AnkiStats.exe') + ',0'
$shortcut.Description = 'Build and open the current checkout of Anki Stats using your normal saved profile'
$shortcut.Save()
Write-Output "Shortcut ready: $projectRoot\Anki Stats.lnk"
