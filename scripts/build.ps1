param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
$localSdk = Join-Path $env:USERPROFILE '.codex\tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localSdk) { $dotnet = $localSdk }
if (-not $dotnet) { throw 'Install the .NET 10 SDK first.' }
Push-Location $root
try {
    $version = ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
    $archiveName = "Reticle-v$version-win-x64.zip"
    $archivePath = Join-Path 'artifacts' $archiveName
    & $dotnet build src/Reticle.App -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet run --project tests/Reticle.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & $dotnet run --project tests/Reticle.GameBar.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Game Bar tests failed.' }
    if ($Portable) {
        & $dotnet publish src/Reticle.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/Reticle-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item README.md artifacts/Reticle-win-x64/README.md
        New-Item -ItemType Directory -Force artifacts/Reticle-win-x64/docs | Out-Null
        Copy-Item docs/*.png artifacts/Reticle-win-x64/docs/
        Copy-Item docs/fullscreen-gamebar.md artifacts/Reticle-win-x64/docs/
        Compress-Archive -Path artifacts/Reticle-win-x64/* -DestinationPath $archivePath -Force
        $hash = (Get-FileHash $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -Encoding ascii -Path "$archivePath.sha256" -Value "$hash  $archiveName"
    }
} finally { Pop-Location }
