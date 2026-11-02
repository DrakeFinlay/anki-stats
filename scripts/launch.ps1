param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'artifacts\AnkiStats'
$log = Join-Path $projectRoot '.tools\launch.log'
try {
    New-Item -ItemType Directory -Path (Split-Path -Parent $log) -Force | Out-Null
    # Publish on every launch: MSBuild performs incremental work and includes uncommitted edits.
    # Never silently fall back to an older executable if the current source cannot build.
    & (Join-Path $PSScriptRoot 'publish.ps1') *> $log
    if ($BuildOnly) { Write-Output "Build ready: $output\AnkiStats.exe"; return }
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $output 'AnkiStats.exe'
    $start.WorkingDirectory = $projectRoot
    $start.UseShellExecute = $false
    # Test-session overrides inherited from development must not affect the normal shortcut.
    $start.EnvironmentVariables.Remove('ANKISTATS_DATA_DIR')
    [System.Diagnostics.Process]::Start($start) | Out-Null
} catch {
    $message = "Anki Stats could not build or start. Close other Anki Stats windows and try again. If this is a build error, fix the current changes first. Your Anki collection has not been modified.`n`n$($_.Exception.Message)`n`nBuild log: $log"
    if ($BuildOnly) { throw $message }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($message, 'Anki Stats launcher') | Out-Null
    exit 1
}
