param([string]$OutputDirectory = "artifacts\Nakama")
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $dotnetPath = if (Test-Path -LiteralPath '.tools\dotnet\dotnet.exe') { Join-Path $projectRoot '.tools\dotnet\dotnet.exe' } else { (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\cli'
    $env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\packages'
    & $dotnetPath publish src/AnkiStats.App/AnkiStats.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $output = (Resolve-Path -LiteralPath $OutputDirectory).Path
    Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md' -Destination $output
    Copy-Item -LiteralPath 'LICENSES' -Destination $output -Recurse -Force
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path (Split-Path $output -Parent) 'Nakama.lnk'))
    $shortcut.TargetPath = Join-Path $output 'Nakama.exe'
    $shortcut.WorkingDirectory = $output
    $shortcut.IconLocation = (Join-Path $output 'Nakama.exe') + ',0'
    $shortcut.Description = 'Nakama - your Japanese learning companion'
    $shortcut.Save()
    & (Join-Path $PSScriptRoot 'create-shortcut.ps1')
    Write-Output "Ready: $output\Nakama.exe"
} finally { Pop-Location }
