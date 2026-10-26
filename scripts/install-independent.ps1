[CmdletBinding()]
param([string]$PackageDirectory = $PSScriptRoot, [switch]$VerifyOnly, [switch]$TrustPreviewCertificate, [switch]$CreateDesktopShortcut, [string]$ExpectedCertificateThumbprint = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) { throw 'Use a 64-bit Windows PowerShell on Windows 10/11.' }
if ($VerifyOnly -and ($TrustPreviewCertificate -or $CreateDesktopShortcut)) { throw '-VerifyOnly cannot be combined with installation options.' }
if ($ExpectedCertificateThumbprint -and $ExpectedCertificateThumbprint -notmatch '\A[a-fA-F0-9]{40}\z') { throw 'Expected certificate thumbprint must be exactly 40 hexadecimal characters.' }
$packageRoot = [IO.Path]::GetFullPath($PackageDirectory).TrimEnd('\','/')
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$productRoot = Join-Path $programFiles 'Reticle Studio'
$installRoot = Join-Path $productRoot 'OverlayHost'
$editorRoot = Join-Path $productRoot 'Independent'
$installedEditor = Join-Path $editorRoot 'Editor\Reticle.exe'
$shortcutName = 'Reticle Studio ' + ([string][char]0x72EC) + [char]0x7ACB + [char]0x8986 + [char]0x76D6 + [char]0x7248 + '.lnk'

function Assert-NoReparsePath([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse points are not accepted: $current" }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
function Assert-ExpectedCertificateThumbprint([string]$Actual, [string]$Expected) {
    if (-not $Expected) { return }
    if ($Expected -notmatch '\A[a-fA-F0-9]{40}\z') { throw 'Expected certificate thumbprint must be exactly 40 hexadecimal characters.' }
    if (-not [string]::Equals($Actual,$Expected,[StringComparison]::OrdinalIgnoreCase)) { throw 'The package signing certificate changed after it was displayed. Restart setup and review the certificate again.' }
}
function Resolve-PayloadFile([string]$RelativePath) {
    if (-not $RelativePath -or $RelativePath.Contains(':') -or [IO.Path]::IsPathRooted($RelativePath)) { throw 'Invalid payload path.' }
    foreach ($part in $RelativePath.Split([char[]]@('/','\'))) {
        if (-not $part -or $part -in @('.','..') -or $part.TrimEnd(' ','.') -ne $part) { throw 'Invalid payload path component.' }
    }
    $resolved = [IO.Path]::GetFullPath((Join-Path $packageRoot $RelativePath))
    if (-not $resolved.StartsWith($packageRoot + '\',[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Missing or escaping payload: $RelativePath" }
    Assert-NoReparsePath $resolved
    return $resolved
}
function Get-SignaturePolicy([string]$Path) {
    if (-not ('ReticleIndependent.SignaturePolicy' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace ReticleIndependent {
 public static class SignaturePolicy {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct FileInfo {
   public uint size; [MarshalAs(UnmanagedType.LPWStr)] public string path; public IntPtr file, subject;
  }
  [StructLayout(LayoutKind.Sequential)] struct TrustData {
   public uint size; public IntPtr callback, sip; public uint ui, revocation, choice; public IntPtr file; public uint action;
   public IntPtr state, url; public uint flags, context; public IntPtr signature;
  }
  [DllImport("wintrust.dll", ExactSpelling=true)] static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid action, ref TrustData data);
  public static uint Verify(string path) {
   var file = new FileInfo { size=(uint)Marshal.SizeOf(typeof(FileInfo)), path=path };
   var pointer = Marshal.AllocHGlobal((int)file.size);
   Marshal.StructureToPtr(file,pointer,false);
   var data = new TrustData { size=(uint)Marshal.SizeOf(typeof(TrustData)), ui=2, choice=1, file=pointer, action=1 };
   var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
   try { return WinVerifyTrust(IntPtr.Zero,ref action,ref data); }
   finally { data.action=2; WinVerifyTrust(IntPtr.Zero,ref action,ref data); Marshal.DestroyStructure(pointer,typeof(FileInfo)); Marshal.FreeHGlobal(pointer); }
  }
 }
}
'@
    }
    return [ReticleIndependent.SignaturePolicy]::Verify($Path)
}
function Assert-ProtectedDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Assert-NoReparsePath $Path
    $acl = Get-Acl -LiteralPath $Path
    Assert-ProtectedAcl $acl $Path
}
function Assert-ProtectedAcl([Security.AccessControl.DirectorySecurity]$Acl, [string]$Path) {
    # Read the descriptor's SIDs directly. Translating localized account names
    # back into SIDs can fail for system/application-package identities.
    $owner = $Acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($owner -notin @('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')) { throw "Installation directory has an unexpected owner: $Path ($owner)" }
    $writes = [int64]([Security.AccessControl.FileSystemRights]::WriteData -bor [Security.AccessControl.FileSystemRights]::AppendData -bor [Security.AccessControl.FileSystemRights]::WriteExtendedAttributes -bor [Security.AccessControl.FileSystemRights]::WriteAttributes -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership) -bor 0x50000000L
    foreach ($rule in $Acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        $sid = $rule.IdentityReference.Value
        $rights = [int64]$rule.FileSystemRights -band 0xFFFFFFFFL
        if ($rule.AccessControlType -eq 'Allow' -and ($rights -band $writes) -and $sid -notin @('S-1-5-18','S-1-5-32-544','S-1-3-0','S-1-3-4','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')) { throw "Installation directory grants non-administrative write access: $Path ($sid)" }
    }
}
function Set-ProtectedDirectory([string]$Path) {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    $administrators = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $acl.SetOwner($administrators)
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    $propagation = [Security.AccessControl.PropagationFlags]::None
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) { $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),[Security.AccessControl.FileSystemRights]::FullControl,$inherit,$propagation,[Security.AccessControl.AccessControlType]::Allow)) }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'),[Security.AccessControl.FileSystemRights]::ReadAndExecute,$inherit,$propagation,[Security.AccessControl.AccessControlType]::Allow))
    Set-Acl -LiteralPath $Path -AclObject $acl
}
function Initialize-IndependentShortcut {
    if ('ReticleIndependent.NativeShortcut' -as [type]) { return }
    # IShellLinkW and IPersistFile preserve UTF-16 paths across Windows locales.
    # Keep this definition standalone so the
    # installed uninstaller does not depend on the extracted download.
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ReticleIndependent {
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLinkObject { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemList);
        void SetIDList(IntPtr itemList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    public sealed class ShortcutInfo {
        public string TargetPath { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public string IconLocation { get; set; }
    }

    public static class NativeShortcut {
        public static ShortcutInfo Read(string path) {
            object instance = new ShellLinkObject();
            try {
                ((IPersistFile)instance).Load(path, 0);
                IShellLinkW link = (IShellLinkW)instance;
                StringBuilder target = new StringBuilder(32768);
                StringBuilder arguments = new StringBuilder(32768);
                StringBuilder working = new StringBuilder(32768);
                StringBuilder icon = new StringBuilder(32768);
                int iconIndex;
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
                link.GetArguments(arguments, arguments.Capacity);
                link.GetWorkingDirectory(working, working.Capacity);
                link.GetIconLocation(icon, icon.Capacity, out iconIndex);
                return new ShortcutInfo {
                    TargetPath = target.ToString(), Arguments = arguments.ToString(),
                    WorkingDirectory = working.ToString(),
                    IconLocation = icon.ToString() + "," + iconIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
            } finally { Marshal.FinalReleaseComObject(instance); }
        }

        public static void Write(string path, string target, string working, string arguments) {
            object instance = new ShellLinkObject();
            try {
                IShellLinkW link = (IShellLinkW)instance;
                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(working);
                link.SetIconLocation(target, 0);
                link.SetDescription("Reticle Studio independent fullscreen overlay");
                link.SetShowCmd(1);
                ((IPersistFile)instance).Save(path, true);
            } finally { Marshal.FinalReleaseComObject(instance); }
        }
    }
}
'@
}
function Get-IndependentShortcut([string]$Path) {
    Initialize-IndependentShortcut
    return [ReticleIndependent.NativeShortcut]::Read($Path)
}
function Test-IndependentShortcut([string]$Path, [string]$EditorExecutable) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    Assert-NoReparsePath $Path
    $link = Get-IndependentShortcut $Path
    if (-not $link.TargetPath) { return $false }
    return [string]::Equals([IO.Path]::GetFullPath($link.TargetPath), [IO.Path]::GetFullPath($EditorExecutable), [StringComparison]::OrdinalIgnoreCase) -and $link.Arguments -eq '--independent'
}
function Write-IndependentShortcut([string]$Path, [string]$EditorExecutable) {
    Assert-NoReparsePath $Path
    if ((Test-Path -LiteralPath $Path) -and -not (Test-IndependentShortcut $Path $EditorExecutable)) { throw "A different shortcut already exists: $Path" }
    Initialize-IndependentShortcut
    [ReticleIndependent.NativeShortcut]::Write($Path, $EditorExecutable, [IO.Path]::GetDirectoryName($EditorExecutable), '--independent')
    if (-not (Test-IndependentShortcut $Path $EditorExecutable)) { throw 'Desktop shortcut verification failed.' }
}
function Remove-OwnedInstallDirectory([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $productRoot -or [IO.Path]::GetFileName($resolved) -notmatch '\A(?:OverlayHost|Independent)(?:\.(?:stage|backup)-[a-f0-9]{32})?\z') { throw 'Unexpected installation cleanup path.' }
    if (Test-Path -LiteralPath $resolved) {
        Assert-NoReparsePath $resolved
        if (@(Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count) { throw 'Installation cleanup encountered a reparse point.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

Assert-NoReparsePath $packageRoot
$bundle = Get-Content -LiteralPath (Join-Path $packageRoot 'package-files.json') -Raw | ConvertFrom-Json
if ($bundle.schemaVersion -ne 1 -or $bundle.identity -ne 'ReticleStudio.IndependentOverlay' -or $bundle.hostExecutable -ne 'host/Reticle.OverlayHost.exe' -or $bundle.editorExecutable -ne 'editor/Reticle.exe' -or $bundle.certificateThumbprint -notmatch '\A[a-fA-F0-9]{40}\z' -or $bundle.previewCertificate -isnot [bool]) { throw 'Unexpected independent overlay metadata.' }
$files = @{}
foreach ($file in @($bundle.files)) {
    if ($files.ContainsKey($file.path) -or $file.sha256 -notmatch '\A[a-fA-F0-9]{64}\z') { throw 'Duplicate payload or invalid SHA256.' }
    $path = Resolve-PayloadFile $file.path
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) { throw "SHA256 mismatch: $($file.path)" }
    $files[$file.path] = $path
}
foreach ($required in @($bundle.hostExecutable,$bundle.editorExecutable,$bundle.certificate,'install-independent.ps1','uninstall-independent.ps1','host/Reticle.OverlayHost.dll','host/Reticle.Core.dll')) { if (-not $files.ContainsKey($required)) { throw "Missing required payload: $required" } }
$actualFiles = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -Force -File | Where-Object { $_.FullName -ne (Join-Path $packageRoot 'package-files.json') })
if ($actualFiles.Count -ne $files.Count) { throw 'Unlisted files found in package. Extract into an empty folder and retry.' }
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($files[$bundle.certificate])
Assert-ExpectedCertificateThumbprint $certificate.Thumbprint $ExpectedCertificateThumbprint
if ($certificate.HasPrivateKey -or $certificate.Thumbprint -ne $bundle.certificateThumbprint -or $certificate.Subject -ne $bundle.certificateSubject -or [DateTime]::Now -lt $certificate.NotBefore -or [DateTime]::Now -gt $certificate.NotAfter) { throw 'Unexpected or expired public signing certificate.' }
$eku = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
if ($eku.Count -ne 1 -or -not @($eku[0].EnhancedKeyUsages | Where-Object Value -eq '1.3.6.1.5.5.7.3.3').Count) { throw 'The certificate must permit Code Signing.' }
if ($bundle.previewCertificate -and ($certificate.Subject -ne 'CN=Reticle Studio Independent Preview' -or $certificate.Issuer -ne $certificate.Subject)) { throw 'Unexpected self-signed preview identity.' }
$signedPaths = @('host/Reticle.OverlayHost.exe','host/Reticle.OverlayHost.dll','host/Reticle.Core.dll')
foreach ($relative in $signedPaths) {
    $signature = Get-AuthenticodeSignature -LiteralPath $files[$relative]
    if (-not $signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "Wrong signer: $relative" }
    $policy = Get-SignaturePolicy $files[$relative]
    if ($policy -ne 0 -and -not ($bundle.previewCertificate -and $policy -eq [uint32]2148204809)) { throw "Signature rejected: $relative (0x$($policy.ToString('X8')))" }
}
Write-Host "Reticle independent overlay $($bundle.version)"
Write-Host "Certificate: $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Expires: $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host 'Payload hashes and pinned Authenticode signatures verified.'
if ($VerifyOnly) { return }

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Open a 64-bit administrator PowerShell and run this script again. Preview certificate trust requires the explicit -TrustPreviewCertificate switch.' }
Assert-NoReparsePath $installRoot
Assert-ProtectedDirectory $programFiles
Assert-ProtectedDirectory $productRoot
Assert-ProtectedDirectory $installRoot
Assert-ProtectedDirectory $editorRoot
foreach ($process in @(Get-Process -Name Reticle.OverlayHost,Reticle -ErrorAction SilentlyContinue)) {
    if ($process.Path -and [IO.Path]::GetFullPath($process.Path) -in @((Join-Path $installRoot 'Reticle.OverlayHost.exe'),$installedEditor)) { throw 'Close the installed independent editor and crosshair before installing or updating.' }
}
$previousOwnedCertificates = @()
$previousShortcut = $null
if (Test-Path -LiteralPath $installRoot) {
    $previous = Get-Content -LiteralPath (Join-Path $installRoot 'installation.json') -Raw | ConvertFrom-Json
    if ($previous.schemaVersion -notin @(1,2) -or $previous.identity -ne 'ReticleStudio.IndependentOverlay' -or $previous.installRoot -ne $installRoot) { throw 'Existing directory is not a recognized independent overlay installation.' }
    $previousOwnedCertificates = @($previous.ownedPreviewCertificates)
    foreach ($thumbprint in $previousOwnedCertificates) { if ($thumbprint -notmatch '\A[a-fA-F0-9]{40}\z') { throw 'Invalid certificate ownership record.' } }
    if ($previous.PSObject.Properties.Name -contains 'desktopShortcut') { $previousShortcut = $previous.desktopShortcut }
}
if (Test-Path -LiteralPath $editorRoot) {
    $previousEditor = Get-Content -LiteralPath (Join-Path $editorRoot 'installation.json') -Raw | ConvertFrom-Json
    if ($previousEditor.schemaVersion -ne 2 -or $previousEditor.identity -ne 'ReticleStudio.IndependentOverlay' -or $previousEditor.editorRoot -ne $editorRoot -or $previousEditor.installRoot -ne $installRoot) { throw 'Existing editor directory is not a recognized independent installation.' }
}
$commonDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
$desktopShortcut = if ($commonDesktop) { Join-Path $commonDesktop $shortcutName } else { $null }
$managedShortcut = $null
if ($CreateDesktopShortcut) {
    if (-not $desktopShortcut -or -not (Test-Path -LiteralPath $commonDesktop -PathType Container)) { throw 'The all-users desktop is not available.' }
    Assert-NoReparsePath $desktopShortcut
    if ((Test-Path -LiteralPath $desktopShortcut) -and -not (Test-IndependentShortcut $desktopShortcut $installedEditor)) { throw "A different shortcut already uses the requested name: $desktopShortcut" }
    $managedShortcut = $desktopShortcut
} elseif ($previousShortcut -and $desktopShortcut -and [string]::Equals([string]$previousShortcut,$desktopShortcut,[StringComparison]::OrdinalIgnoreCase) -and (Test-IndependentShortcut $desktopShortcut $installedEditor)) {
    $managedShortcut = $desktopShortcut
}
$trustedCertificatePath = 'Cert:\LocalMachine\Root\' + $certificate.Thumbprint
$certificateAlreadyTrusted = Test-Path -LiteralPath $trustedCertificatePath
if ($bundle.previewCertificate -and -not $certificateAlreadyTrusted -and -not $TrustPreviewCertificate) {
    throw "This UIAccess preview needs its exact certificate in LocalMachine\Root. To explicitly trust '$($certificate.Subject)' ($($certificate.Thumbprint)), run .\install-independent.ps1 -TrustPreviewCertificate. No trust was added."
}
if ($TrustPreviewCertificate -and -not $bundle.previewCertificate) { throw "-TrustPreviewCertificate is only for this package's self-signed preview certificate." }
$stage = Join-Path $productRoot ('OverlayHost.stage-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $productRoot ('OverlayHost.backup-' + [Guid]::NewGuid().ToString('N'))
$editorStage = Join-Path $productRoot ('Independent.stage-' + [Guid]::NewGuid().ToString('N'))
$editorBackup = Join-Path $productRoot ('Independent.backup-' + [Guid]::NewGuid().ToString('N'))
$addedCertificate = $false
$installed = $false
$backedUp = $false; $editorBackedUp = $false
$hostCommitted = $false; $editorCommitted = $false; $shortcutChanged = $false
$shortcutBytes = $null
try {
    New-Item -ItemType Directory -Force -Path $productRoot | Out-Null
    New-Item -ItemType Directory -Path $stage | Out-Null
    New-Item -ItemType Directory -Path $editorStage | Out-Null
    Set-ProtectedDirectory $stage
    Set-ProtectedDirectory $editorStage
    foreach ($relative in $files.Keys) {
        if ($relative.StartsWith('host/',[StringComparison]::Ordinal)) { $destination = Join-Path $stage $relative.Substring(5) }
        elseif ($relative.StartsWith('editor/',[StringComparison]::Ordinal)) { $destination = Join-Path (Join-Path $editorStage 'Editor') $relative.Substring(7) }
        else { continue }
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $files[$relative] -Destination $destination
        $expected = @($bundle.files | Where-Object path -eq $relative)[0].sha256
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) { throw "Copied file changed: $relative" }
    }
    $uninstallerHash = @($bundle.files | Where-Object path -eq 'uninstall-independent.ps1')[0].sha256
    foreach ($directory in @($stage,$editorStage)) {
        Copy-Item -LiteralPath $files['uninstall-independent.ps1'] -Destination $directory
        if ((Get-FileHash -LiteralPath (Join-Path $directory 'uninstall-independent.ps1') -Algorithm SHA256).Hash -ne $uninstallerHash) { throw 'Copied uninstaller changed.' }
    }
    @'
@echo off
start "Reticle Studio Independent" "%~dp0Editor\Reticle.exe" --independent
'@ | Set-Content -LiteralPath (Join-Path $editorStage 'Start-Reticle.cmd') -Encoding ascii
    if ($bundle.previewCertificate -and -not $certificateAlreadyTrusted) {
        Write-Host "Adding ONLY preview certificate $($certificate.Thumbprint) to LocalMachine\Root."
        # Add the already-validated in-memory public certificate. Do not reopen a
        # replaceable certificate path after the signature and identity checks.
        $rootStore = [Security.Cryptography.X509Certificates.X509Store]::new('Root',[Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        try { $rootStore.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite); $rootStore.Add($certificate) }
        finally { $rootStore.Close() }
        $addedCertificate = $true
    }
    $stagedExe = Join-Path $stage 'Reticle.OverlayHost.exe'
    if ((Get-AuthenticodeSignature -LiteralPath $stagedExe).Status.ToString() -ne 'Valid') { throw 'Installed helper signature is not trusted by Windows.' }
    $owned = @($previousOwnedCertificates)
    if ($addedCertificate -and $certificate.Thumbprint -notin $owned) { $owned += $certificate.Thumbprint }
    $record = [ordered]@{ schemaVersion=2; identity=$bundle.identity; version=$bundle.version; installRoot=$installRoot; editorRoot=$editorRoot; editorExecutable=$installedEditor; desktopShortcut=$managedShortcut; certificateThumbprint=$certificate.Thumbprint; ownedPreviewCertificates=@($owned); installedAtUtc=[DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json -Depth 4
    foreach ($directory in @($stage,$editorStage)) { $record | Set-Content -LiteralPath (Join-Path $directory 'installation.json') -Encoding UTF8 }
    if (Test-Path -LiteralPath $installRoot) { Move-Item -LiteralPath $installRoot -Destination $backup; $backedUp = $true }
    if (Test-Path -LiteralPath $editorRoot) { Move-Item -LiteralPath $editorRoot -Destination $editorBackup; $editorBackedUp = $true }
    Move-Item -LiteralPath $stage -Destination $installRoot
    $hostCommitted = $true
    Move-Item -LiteralPath $editorStage -Destination $editorRoot
    $editorCommitted = $true
    if ($CreateDesktopShortcut) {
        if (Test-Path -LiteralPath $desktopShortcut) { $shortcutBytes = [IO.File]::ReadAllBytes($desktopShortcut) }
        $shortcutChanged = $true
        Write-IndependentShortcut $desktopShortcut $installedEditor
    }
    $installed = $true
    if ($backedUp) { Remove-OwnedInstallDirectory $backup }
    if ($editorBackedUp) { Remove-OwnedInstallDirectory $editorBackup }
    Write-Host "Installed UIAccess helper: $installRoot"
    Write-Host "Installed independent editor: $installedEditor"
    if ($CreateDesktopShortcut) { Write-Host "Created all-users desktop shortcut: $desktopShortcut" }
    Write-Host 'Start the installed independent editor as your normal user. The extracted package is no longer required.'
} finally {
    if (-not $installed) {
        if ($shortcutChanged) {
            if ($null -ne $shortcutBytes) { [IO.File]::WriteAllBytes($desktopShortcut,$shortcutBytes) }
            elseif (Test-IndependentShortcut $desktopShortcut $installedEditor) { Remove-Item -LiteralPath $desktopShortcut }
        }
        if ($hostCommitted) { Remove-OwnedInstallDirectory $installRoot }
        if ($editorCommitted) { Remove-OwnedInstallDirectory $editorRoot }
        if ($backedUp -and -not (Test-Path -LiteralPath $installRoot)) { Move-Item -LiteralPath $backup -Destination $installRoot }
        if ($editorBackedUp -and -not (Test-Path -LiteralPath $editorRoot)) { Move-Item -LiteralPath $editorBackup -Destination $editorRoot }
    }
    if (-not $installed -and $addedCertificate) { Remove-Item -LiteralPath $trustedCertificatePath }
    foreach ($directory in @($stage,$editorStage)) { if (Test-Path -LiteralPath $directory) { Remove-OwnedInstallDirectory $directory } }
}
