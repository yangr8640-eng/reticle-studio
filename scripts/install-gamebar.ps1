[CmdletBinding()]
param(
    [string]$PackageDirectory = $PSScriptRoot,
    [switch]$VerifyOnly,
    [switch]$InstallTestCertificate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($VerifyOnly -and $InstallTestCertificate) { throw 'Choose either -VerifyOnly or -InstallTestCertificate.' }
if ($env:OS -ne 'Windows_NT') { throw 'This preview requires Windows 10/11 x64.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\', '/')
$manifestPath = Join-Path $packageRoot 'package-files.json'
$bundle = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($bundle.schemaVersion -ne 1 -or $bundle.identity -ne 'ReticleStudio.GameBar' -or
    $bundle.publisher -ne 'CN=ReticleStudio' -or $bundle.version -ne '0.3.0.0') {
    throw 'Unexpected preview package metadata.'
}

function Resolve-PayloadFile([string]$RelativePath) {
    if ([IO.Path]::IsPathRooted($RelativePath)) { throw 'Absolute payload paths are not allowed.' }
    $resolved = [IO.Path]::GetFullPath((Join-Path $packageRoot $RelativePath))
    if (-not $resolved.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Payload path escapes its package directory.'
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Missing payload: $RelativePath" }
    return $resolved
}

function Read-AppxManifest([string]$Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw "Package has no AppxManifest.xml: $Path" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

function Get-AuthenticodePolicyResult([string]$Path) {
    # PowerShell collapses CERT_E_UNTRUSTEDROOT into UnknownError. Retrieve the
    # actual policy result rather than accepting unrelated UnknownError failures.
    if (-not ('ReticlePreview.NativeSignatureVerifier' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ReticlePreview
{
    public static class NativeSignatureVerifier
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WINTRUST_FILE_INFO
        {
            public uint cbStruct;
            [MarshalAs(UnmanagedType.LPWStr)] public string filePath;
            public IntPtr fileHandle;
            public IntPtr knownSubject;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct WINTRUST_DATA
        {
            public uint cbStruct;
            public IntPtr policyCallbackData;
            public IntPtr sipClientData;
            public uint uiChoice;
            public uint revocationChecks;
            public uint unionChoice;
            public IntPtr fileInfo;
            public uint stateAction;
            public IntPtr stateData;
            public IntPtr urlReference;
            public uint providerFlags;
            public uint uiContext;
            public IntPtr signatureSettings;
        }
        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref WINTRUST_DATA data);
        public static uint Verify(string path)
        {
            WINTRUST_FILE_INFO file = new WINTRUST_FILE_INFO();
            file.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            file.filePath = path;
            IntPtr filePointer = Marshal.AllocHGlobal((int)file.cbStruct);
            Marshal.StructureToPtr(file, filePointer, false);
            WINTRUST_DATA data = new WINTRUST_DATA();
            data.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
            data.uiChoice = 2; // WTD_UI_NONE
            data.unionChoice = 1; // WTD_CHOICE_FILE
            data.fileInfo = filePointer;
            data.stateAction = 1; // WTD_STATEACTION_VERIFY
            Guid action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try { return WinVerifyTrust(IntPtr.Zero, ref action, ref data); }
            finally
            {
                data.stateAction = 2; // WTD_STATEACTION_CLOSE
                WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                Marshal.DestroyStructure(filePointer, typeof(WINTRUST_FILE_INFO));
                Marshal.FreeHGlobal(filePointer);
            }
        }
    }
}
'@
    }
    return [ReticlePreview.NativeSignatureVerifier]::Verify($Path)
}

$filesByName = @{}
foreach ($file in @($bundle.files)) {
    if ($filesByName.ContainsKey($file.path) -or $file.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'Duplicate file or invalid SHA256 in package-files.json.'
    }
    $path = Resolve-PayloadFile $file.path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "SHA256 mismatch: $($file.path)"
    }
    $filesByName[$file.path] = $path
}
foreach ($required in @($bundle.mainPackage, $bundle.certificate, 'install-gamebar.ps1')) {
    if (-not $filesByName.ContainsKey($required)) { throw "Missing checksum entry: $required" }
}
$appxPath = $filesByName[$bundle.mainPackage]
$certificatePath = $filesByName[$bundle.certificate]
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
if ($certificate.HasPrivateKey -or $certificate.Subject -ne 'CN=ReticleStudio' -or
    $certificate.Thumbprint -ne $bundle.certificateThumbprint -or
    [DateTime]::Now -lt $certificate.NotBefore -or [DateTime]::Now -gt $certificate.NotAfter) {
    throw 'Unexpected or expired preview signing certificate.'
}
$eku = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
if ($eku.Count -ne 1 -or -not @($eku[0].EnhancedKeyUsages | Where-Object Value -eq '1.3.6.1.5.5.7.3.3').Count) {
    throw 'The preview certificate must have Code Signing usage.'
}
$basicConstraints = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.19' })
if ($basicConstraints.Count -and $basicConstraints[0].CertificateAuthority) { throw 'CA certificates are not accepted.' }
$appManifest = Read-AppxManifest $appxPath
$identity = $appManifest.Package.Identity
if ($identity.Name -ne $bundle.identity -or $identity.Publisher -ne $bundle.publisher -or
    $identity.Version -ne $bundle.version -or $identity.ProcessorArchitecture -ne 'x64') {
    throw 'The APPX identity does not match this x64 preview.'
}
$signature = Get-AuthenticodeSignature -LiteralPath $appxPath
if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw "APPX signature verification failed: $($signature.Status) $($signature.StatusMessage)"
}
$signatureAccepted = $signature.Status.ToString() -eq 'Valid'
if ($signature.Status.ToString() -eq 'UnknownError') {
    $policyResult = Get-AuthenticodePolicyResult $appxPath
    # Accept only a not-yet-trusted root on the pinned preview certificate.
    # In particular, NotTrusted means EXPLICIT_DISTRUST and must never pass.
    $signatureAccepted = $policyResult -eq [uint32]2148204809 # CERT_E_UNTRUSTEDROOT (0x800B0109)
}
if (-not $signatureAccepted) { throw "APPX signature verification failed: $($signature.Status) $($signature.StatusMessage)" }

$dependencyPaths = @()
$dependencyIdentities = @{}
foreach ($relativePath in @($bundle.dependencies)) {
    if (-not $filesByName.ContainsKey($relativePath) -or $relativePath -notmatch '^Dependencies[/\\].+\.(appx|msix)$') {
        throw "Invalid dependency entry: $relativePath"
    }
    $path = $filesByName[$relativePath]
    $dependencyManifest = Read-AppxManifest $path
    $dependencyIdentity = $dependencyManifest.Package.Identity
    $dependencySignature = Get-AuthenticodeSignature -LiteralPath $path
    if ($dependencyIdentity.Name -notlike 'Microsoft.*' -or $dependencyIdentity.ProcessorArchitecture -notin @('x64','neutral') -or
        $dependencySignature.Status.ToString() -ne 'Valid') {
        throw "Untrusted or incompatible Microsoft framework dependency: $relativePath"
    }
    $dependencyPaths += $path
    $dependencyIdentities[$dependencyIdentity.Name] = $dependencyIdentity
}
$namespaceManager = [Xml.XmlNamespaceManager]::new($appManifest.NameTable)
$namespaceManager.AddNamespace('a', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
foreach ($required in @($appManifest.SelectNodes('/a:Package/a:Dependencies/a:PackageDependency', $namespaceManager))) {
    if (-not $dependencyIdentities.ContainsKey($required.Name)) { throw "Missing bundled framework: $($required.Name)" }
    $provided = $dependencyIdentities[$required.Name]
    if ($provided.Publisher -ne $required.Publisher -or [Version]$provided.Version -lt [Version]$required.MinVersion) {
        throw "Bundled framework does not satisfy dependency: $($required.Name)"
    }
}

Write-Host 'Reticle Game Bar v0.3.0-preview.1 - preview only; CS fullscreen compatibility needs testing.'
Write-Host "Signing certificate: $($certificate.Subject)"
Write-Host "Certificate thumbprint: $($certificate.Thumbprint)"
Write-Host "Certificate expires: $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host 'Package identity, SHA256 hashes, signing certificate and framework signatures verified.'
if ($VerifyOnly) { return }

$identityContext = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identityContext)
$elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$trustedCertificatePath = 'Cert:\LocalMachine\TrustedPeople\' + $certificate.Thumbprint
if ($InstallTestCertificate) {
    if (-not $elevated) { throw 'To trust this test certificate, explicitly run this script with -InstallTestCertificate in an administrator PowerShell.' }
    if (-not (Test-Path -LiteralPath $trustedCertificatePath)) {
        Import-Certificate -FilePath $certificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
    }
    Write-Host 'Test certificate trusted in LocalMachine\TrustedPeople. No application was installed.'
    Write-Host 'Close this administrator window. Run install-gamebar.ps1 without switches in your normal user PowerShell.'
    return
}
if ($elevated) {
    throw 'Run application installation from a non-administrator PowerShell under the account that uses Game Bar. Administrator mode is only for -InstallTestCertificate.'
}
if (-not (Test-Path -LiteralPath $trustedCertificatePath)) {
    Write-Host 'This is a self-signed preview, not a Microsoft Store release.'
    Write-Host 'Compare the certificate thumbprint and ZIP SHA256 with the preview release before choosing to trust it.'
    Write-Host 'If you choose to proceed, open an administrator PowerShell in this folder and run:'
    Write-Host '  .\install-gamebar.ps1 -InstallTestCertificate'
    throw 'Test certificate is not trusted. No certificate, app, security policy or developer-mode setting was changed.'
}
$trustedSignature = Get-AuthenticodeSignature -LiteralPath $appxPath
if ($trustedSignature.Status.ToString() -ne 'Valid') { throw "Trusted package signature failed: $($trustedSignature.StatusMessage)" }
if (-not (Get-AppxPackage -Name Microsoft.XboxGamingOverlay)) { throw 'Install Xbox Game Bar from Microsoft Store for this user, then retry.' }
$installed = Get-AppxPackage -Name ReticleStudio.GameBar
if ($installed -and [Version]$installed.Version -gt [Version]$bundle.version) { throw 'A newer Game Bar package is already installed; downgrades are not performed.' }
$missingDependencies = @()
foreach ($required in @($appManifest.SelectNodes('/a:Package/a:Dependencies/a:PackageDependency', $namespaceManager))) {
    $available = @(Get-AppxPackage -Name $required.Name -PackageTypeFilter Framework | Where-Object {
        $_.Publisher -eq $required.Publisher -and [Version]$_.Version -ge [Version]$required.MinVersion -and
        $_.Architecture.ToString() -in @('X64','Neutral')
    })
    if ($available.Count) {
        Write-Host "Using installed framework: $($required.Name)"
        continue
    }
    foreach ($relativePath in @($bundle.dependencies)) {
        $dependencyPath = $filesByName[$relativePath]
        $dependencyManifest = Read-AppxManifest $dependencyPath
        if ($dependencyManifest.Package.Identity.Name -eq $required.Name) { $missingDependencies += $dependencyPath }
    }
}
$parameters = @{ Path = $appxPath; ErrorAction = 'Stop' }
if ($missingDependencies.Count) { $parameters.DependencyPath = [string[]]$missingDependencies }
Add-AppxPackage @parameters
$installed = Get-AppxPackage -Name ReticleStudio.GameBar
if (-not $installed -or $installed.Version -ne $bundle.version) { throw 'Installation did not produce the expected package version.' }
Write-Host "Installed for $($identityContext.Name): $($installed.PackageFullName)"
Write-Host 'Open Xbox Game Bar, open Reticle Studio from its widgets menu, then pin the crosshair widget.'
