<#
    Build / run helper for "Android on Windows".

    It makes sure a .NET SDK is available (user-local, no admin) and then builds.
    Usage:
        .\build.ps1              # build Release
        .\build.ps1 -Run         # build and launch
        .\build.ps1 -Publish     # produce a self-contained folder in .\dist
#>
param(
    [switch]$Run,
    [switch]$Publish,
    [switch]$Debug
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$proj = Join-Path $PSScriptRoot 'src\AndroidOnWindows\AndroidOnWindows.csproj'
$config = if ($Debug) { 'Debug' } else { 'Release' }

function Get-Dotnet {
    $local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (Test-Path $local) { return $local }
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$dotnet = Get-Dotnet
$hasSdk = $false
if ($dotnet) {
    $sdks = & $dotnet --list-sdks 2>$null
    $hasSdk = [bool]$sdks
}

if (-not $hasSdk) {
    Write-Host 'No .NET SDK found - installing a user-local .NET 10 SDK...' -ForegroundColor Yellow
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    if (-not (Test-Path $installer)) {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    }
    $installDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    & $installer -Channel 10.0 -InstallDir $installDir
    $dotnet = Join-Path $installDir 'dotnet.exe'
}

Write-Host "Using dotnet: $dotnet" -ForegroundColor Cyan

if ($Publish) {
    $out = Join-Path $PSScriptRoot 'dist'
    & $dotnet publish $proj -c $config -r win-x64 --self-contained true -p:PublishSingleFile=false -o $out
    Write-Host "Published to $out" -ForegroundColor Green
    if ($Run) { & (Join-Path $out 'AndroidOnWindows.exe') }
    return
}

& $dotnet build $proj -c $config
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if ($Run) {
    $exe = Join-Path $PSScriptRoot "src\AndroidOnWindows\bin\$config\net10.0-windows\AndroidOnWindows.exe"
    & $dotnet run --project $proj -c $config
}
