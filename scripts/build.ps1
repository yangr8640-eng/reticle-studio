param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
$localSdk = Join-Path $env:USERPROFILE '.codex\tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $localSdk) { $dotnet = $localSdk }
if (-not $dotnet) { throw 'Install the .NET 10 SDK first.' }
Push-Location $root
try {
    & $dotnet build src/Reticle.App -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet run --project tests/Reticle.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Portable) {
        & $dotnet publish src/Reticle.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/Reticle-win-x64
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
        Copy-Item README.md artifacts/Reticle-win-x64/README.md
        New-Item -ItemType Directory -Force artifacts/Reticle-win-x64/docs | Out-Null
        Copy-Item docs/editor.png artifacts/Reticle-win-x64/docs/editor.png
        Compress-Archive -Path artifacts/Reticle-win-x64/* -DestinationPath artifacts/Reticle-v0.1.0-win-x64.zip -Force
        $hash = (Get-FileHash artifacts/Reticle-v0.1.0-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -Encoding ascii -Path artifacts/Reticle-v0.1.0-win-x64.zip.sha256 -Value "$hash  Reticle-v0.1.0-win-x64.zip"
    }
} finally { Pop-Location }
