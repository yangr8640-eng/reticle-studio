[CmdletBinding()]
param([string]$MSBuildPath = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'The Game Bar preview must be built on Windows with UWP build tools.' }
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$artifactsRoot = Join-Path $root 'artifacts'
$projectPath = Join-Path $root 'src\Reticle.GameBar\Reticle.GameBar.csproj'
$sdkVersion = '10.0.26100.0'
$archiveName = 'Reticle-GameBar-v0.3.0-preview.1-win-x64.zip'
if (-not $MSBuildPath) {
    $msbuildCommand = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
    if ($msbuildCommand) { $MSBuildPath = $msbuildCommand.Source }
    else {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $MSBuildPath = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
        }
    }
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath)) { throw 'Install Visual Studio UWP build tools or build with .github/workflows/gamebar.yml.' }
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$signTool = Join-Path $sdkRoot "bin\$sdkVersion\x64\signtool.exe"
if (-not (Test-Path -LiteralPath $signTool)) { throw "Windows SDK $sdkVersion with SignTool is required." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Read-AppxManifest([string]$Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "No AppxManifest.xml in $Path" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
$workRoot = Join-Path $artifactsRoot ('gamebar-build-' + [Guid]::NewGuid().ToString('N'))
$appxOutput = Join-Path $workRoot 'AppPackages'
$payloadRoot = Join-Path $workRoot 'Payload'
New-Item -ItemType Directory -Force -Path $appxOutput,$payloadRoot | Out-Null
$certificate = $null
Push-Location $root
try {
    & $MSBuildPath $projectPath /restore /m /p:Configuration=Release /p:Platform=x64 "/p:TargetPlatformVersion=$sdkVersion" /p:AppxBundle=Never /p:GenerateAppxPackageOnBuild=true /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxPackageSigningEnabled=false "/p:AppxPackageDir=$appxOutput\"
    if ($LASTEXITCODE -ne 0) { throw 'Game Bar build or APPX packaging failed.' }
    $mainPackages = @()
    $frameworks = @{}
    foreach ($package in @(Get-ChildItem -LiteralPath $appxOutput -Recurse -File | Where-Object Extension -in @('.appx','.msix'))) {
        $manifest = Read-AppxManifest $package.FullName
        $identity = $manifest.Package.Identity
        if ($identity.Name -eq 'ReticleStudio.GameBar') {
            if ($identity.Publisher -ne 'CN=ReticleStudio' -or $identity.Version -ne '0.3.0.0' -or $identity.ProcessorArchitecture -ne 'x64') {
                throw 'Unexpected Game Bar package identity, version or architecture.'
            }
            $mainPackages += $package
        } elseif ($identity.Name -like 'Microsoft.*' -and $identity.ProcessorArchitecture -in @('x64','neutral')) {
            $existing = $frameworks[$identity.Name]
            if (-not $existing -or [Version]$identity.Version -gt [Version]$existing.Version) {
                $frameworks[$identity.Name] = @{ File=$package; Version=$identity.Version }
            }
        }
    }
    if ($mainPackages.Count -ne 1) { throw "Expected one Game Bar APPX, found $($mainPackages.Count)." }
    $mainName = 'ReticleStudio.GameBar_0.3.0.0_x64.appx'
    $mainPath = Join-Path $payloadRoot $mainName
    Copy-Item -LiteralPath $mainPackages[0].FullName -Destination $mainPath
    $dependencyPaths = @()
    New-Item -ItemType Directory -Path (Join-Path $payloadRoot 'Dependencies') | Out-Null
    foreach ($framework in $frameworks.Values) {
        $relativePath = 'Dependencies/' + $framework.File.Name
        Copy-Item -LiteralPath $framework.File.FullName -Destination (Join-Path $payloadRoot $relativePath)
        $dependencyPaths += $relativePath
    }

    # Generate a preview-only, non-exportable key. Only the public .cer is distributed.
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=ReticleStudio' -FriendlyName 'Reticle Game Bar preview build' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(1)
    & $signTool sign /fd SHA256 /s My /sha1 $certificate.Thumbprint $mainPath
    if ($LASTEXITCODE -ne 0) { throw 'Game Bar preview signing failed.' }
    $certificateName = 'ReticleStudio.GameBar.Preview.cer'
    Export-Certificate -Cert $certificate -FilePath (Join-Path $payloadRoot $certificateName) -Type CERT | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install-gamebar.ps1') -Destination $payloadRoot
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-fullscreen.ps1') -Destination $payloadRoot
    $guidePath = Join-Path $root 'docs\fullscreen-gamebar.md'
    if (Test-Path -LiteralPath $guidePath) { Copy-Item -LiteralPath $guidePath -Destination (Join-Path $payloadRoot 'README.md') }
    @"
Reticle Studio Game Bar v0.3.0-preview.1

This is a signed preview package for testing the Game Bar overlay. It is not a
verified CS fullscreen release and is not a Microsoft Store package.

ZIP SHA256 is provided alongside the download. Certificate thumbprint:
$($certificate.Thumbprint)

1. Extract the ZIP. Run install-gamebar.ps1 from a normal Windows PowerShell.
2. The installer verifies the package identity, hashes and signatures first.
3. If you choose to trust this preview certificate, open an administrator
   PowerShell in this folder and explicitly run:
      .\install-gamebar.ps1 -InstallTestCertificate
   This only imports the public certificate into LocalMachine\TrustedPeople.
   It does not install the app. Close the administrator window afterwards.
4. Run .\install-gamebar.ps1 in your normal user PowerShell to install the app
   and its bundled Microsoft frameworks for that account.
5. Open Xbox Game Bar, choose Reticle Studio from the widget menu, and pin it.

Optional read-only environment report:
   .\check-fullscreen.ps1
This reports Game Bar and game compatibility settings without changing them.

The installer does not alter developer mode, execution policy, or security
settings. If your policy blocks running scripts, inspect the script and follow
your organization's policy; this package does not bypass it.

Each preview build has its own temporary signing key. Do not trust a certificate
unless you obtained this package from the project's verified release or CI run.
The private signing key is never included. To uninstall the app, use Windows
Settings > Apps > Reticle Studio. The public test certificate can be removed
separately from Local Computer > Trusted People after uninstalling the preview.
"@ | Set-Content -LiteralPath (Join-Path $payloadRoot 'INSTALL.txt') -Encoding UTF8

    $files = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path=$_.FullName.Substring($payloadRoot.Length + 1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [ordered]@{
        schemaVersion=1; identity='ReticleStudio.GameBar'; publisher='CN=ReticleStudio'; version='0.3.0.0'
        preview='0.3.0-preview.1'; mainPackage=$mainName; certificate=$certificateName
        certificateThumbprint=$certificate.Thumbprint; dependencies=@($dependencyPaths | Sort-Object); files=$files
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payloadRoot 'package-files.json') -Encoding UTF8
    & (Join-Path $PSScriptRoot 'install-gamebar.ps1') -PackageDirectory $payloadRoot -VerifyOnly

    $archivePath = Join-Path $artifactsRoot $archiveName
    Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $archivePath -Force
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$archivePath.sha256" -Encoding ascii -Value "$hash  $archiveName"
    [ordered]@{ archive=$archiveName; sha256=$hash; certificateThumbprint=$certificate.Thumbprint; certificateSubject=$certificate.Subject; preview='0.3.0-preview.1' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsRoot 'Reticle-GameBar-v0.3.0-preview.1-signing.json') -Encoding UTF8
    Write-Host "Created $archivePath"
    Write-Host "SHA256: $hash"
    Write-Host "Preview certificate thumbprint: $($certificate.Thumbprint)"
} finally {
    Pop-Location
    if ($certificate) { Remove-Item -LiteralPath ('Cert:\CurrentUser\My\' + $certificate.Thumbprint) -DeleteKey -ErrorAction Stop }
    # This is a fresh GUID directory created by this invocation, always under artifacts.
    $resolvedWorkRoot = [IO.Path]::GetFullPath($workRoot)
    if ($resolvedWorkRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedWorkRoot) -match '^gamebar-build-[a-f0-9]{32}$') {
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
    } else { throw 'Refusing to remove an unexpected build directory.' }
}
