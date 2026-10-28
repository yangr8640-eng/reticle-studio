[CmdletBinding()]
param([string]$DotnetPath = '', [string]$SigningCertificateThumbprint = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) { throw 'Build this x64 preview from a 64-bit Windows PowerShell.' }
& (Join-Path $PSScriptRoot 'test-independent-installer.ps1')
& (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -STA -File (Join-Path $PSScriptRoot 'test-independent-setup.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Graphical installer checks failed.' }
& (Join-Path $PSScriptRoot 'test-independent-entrypoints.ps1')
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$artifacts = Join-Path $root 'artifacts'
if (-not $DotnetPath) {
    $localSdk = Join-Path $env:USERPROFILE '.codex\tools\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localSdk) { $DotnetPath = $localSdk }
    else { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
$version = [string]([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:-(?:preview|independent)\.[1-9][0-9]*)?\z') { throw 'Unexpected project version.' }
$packageName = "Reticle-Independent-v$version-win-x64"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$workRoot = Join-Path $artifacts ('independent-build-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $workRoot $packageName
New-Item -ItemType Directory -Path $payload | Out-Null
$temporaryCertificate = $null
try {
    & $DotnetPath publish (Join-Path $root 'src\Reticle.OverlayHost\Reticle.OverlayHost.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o (Join-Path $payload 'host')
    if ($LASTEXITCODE -ne 0) { throw 'Independent overlay publish failed.' }
    & $DotnetPath publish (Join-Path $root 'src\Reticle.App\Reticle.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o (Join-Path $payload 'editor')
    if ($LASTEXITCODE -ne 0) { throw 'Editor publish failed.' }

    if ($SigningCertificateThumbprint) {
        if ($SigningCertificateThumbprint -notmatch '\A[a-fA-F0-9]{40}\z') { throw 'Expected a 40-character certificate thumbprint.' }
        $certificate = Get-Item -LiteralPath ('Cert:\CurrentUser\My\' + $SigningCertificateThumbprint)
    } else {
        $temporaryCertificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Reticle Studio Independent Preview' -FriendlyName 'Reticle independent overlay preview' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(1)
        $certificate = $temporaryCertificate
    }
    if (-not $certificate.HasPrivateKey) { throw 'The signing certificate has no private key.' }
    foreach ($relative in @('host/Reticle.OverlayHost.exe','host/Reticle.OverlayHost.dll','host/Reticle.Core.dll')) {
        $file = Join-Path $payload $relative
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing application output: $relative" }
        $signed = Set-AuthenticodeSignature -FilePath $file -Certificate $certificate -HashAlgorithm SHA256
        if (-not $signed.SignerCertificate -or $signed.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or $signed.Status.ToString() -in @('HashMismatch','NotSigned','NotSupported','Incompatible')) { throw "Signing failed: $relative ($($signed.Status))" }
    }
    $certificateName = 'ReticleStudio.Independent.Preview.cer'
    Export-Certificate -Cert $certificate -FilePath (Join-Path $payload $certificateName) -Type CERT | Out-Null
    foreach ($script in @('install-independent.ps1','uninstall-independent.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $payload }
    foreach ($script in @('setup-independent.ps1','Setup-Independent.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $payload }
    Copy-Item -LiteralPath (Join-Path $root 'docs\fullscreen-independent.md') -Destination (Join-Path $payload 'README.md')
    @'
@echo off
start "Reticle Studio Independent" "%~dp0editor\Reticle.exe" --independent
'@ | Set-Content -LiteralPath (Join-Path $payload 'Start-Reticle.cmd') -Encoding ascii
    @"
Reticle Studio independent overlay v$version

Certificate subject: $($certificate.Subject)
Certificate thumbprint: $($certificate.Thumbprint)
Certificate expires: $($certificate.NotAfter.ToString('yyyy-MM-dd'))

1. Read README.md. Run Setup-Independent.cmd for the graphical installer.
   It offers desktop shortcut creation and explicit preview certificate trust.
   Run install-independent.ps1 -VerifyOnly for a read-only package inspection.
2. If this is a self-signed preview, the explicit command to trust its exact
   certificate and install the helper is:
      .\install-independent.ps1 -TrustPreviewCertificate -CreateDesktopShortcut
   Use a 64-bit administrator PowerShell. This adds this public certificate to
   LocalMachine\Root, needed for UIAccess. The default installer never adds trust.
3. Use the installed desktop shortcut, or run the installed editor normally:
      %ProgramFiles%\Reticle Studio\Independent\Editor\Reticle.exe
   This package also includes Start-Reticle.cmd for running its portable editor.
4. Keep fullscreen optimizations enabled and test the game yourself.

Only the public certificate is distributed. The private preview signing key is
not exportable and is removed after this build. No FSO, Game Bar, game, UAC or
driver settings are changed by this package.
"@ | Set-Content -LiteralPath (Join-Path $payload 'INSTALL.txt') -Encoding UTF8
    $fileList = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=$_.FullName.Substring($payload.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [ordered]@{
        schemaVersion=1; identity='ReticleStudio.IndependentOverlay'; version=$version; installerRevision=4
        hostExecutable='host/Reticle.OverlayHost.exe'; editorExecutable='editor/Reticle.exe'
        certificate=$certificateName; certificateThumbprint=$certificate.Thumbprint
        certificateSubject=$certificate.Subject; previewCertificate=($null -ne $temporaryCertificate)
        files=$fileList
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'package-files.json') -Encoding UTF8
    & (Join-Path $PSScriptRoot 'install-independent.ps1') -PackageDirectory $payload -VerifyOnly

    $archive = Join-Path $artifacts ($packageName + '.zip')
    Compress-Archive -LiteralPath $payload -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath ($archive + '.sha256') -Encoding ascii
    [ordered]@{ version=$version; archive=[IO.Path]::GetFileName($archive); sha256=$hash; certificateSubject=$certificate.Subject; certificateThumbprint=$certificate.Thumbprint; certificateNotAfter=$certificate.NotAfter.ToUniversalTime().ToString('O'); selfSignedPreview=($null -ne $temporaryCertificate) } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts "Reticle-Independent-v$version-signing.json") -Encoding UTF8
    $published = Join-Path $artifacts $packageName
    if (Test-Path -LiteralPath $published) {
        $resolved = [IO.Path]::GetFullPath($published)
        if ([IO.Path]::GetDirectoryName($resolved) -ne $artifacts -or [IO.Path]::GetFileName($resolved) -ne $packageName) { throw 'Unexpected output cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    Move-Item -LiteralPath $payload -Destination $published
    [ordered]@{ archive=$archive; sha256=$hash; packageDirectory=$published; certificateThumbprint=$certificate.Thumbprint; certificateSubject=$certificate.Subject } | ConvertTo-Json
} finally {
    if ($temporaryCertificate) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $temporaryCertificate.Thumbprint) -DeleteKey }
    if (Test-Path -LiteralPath $workRoot) {
        $resolved = [IO.Path]::GetFullPath($workRoot)
        if ([IO.Path]::GetDirectoryName($resolved) -ne $artifacts -or [IO.Path]::GetFileName($resolved) -notmatch '\Aindependent-build-[a-f0-9]{32}\z') { throw 'Unexpected temporary cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
