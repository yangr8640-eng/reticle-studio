[CmdletBinding()]
param([switch]$RemovePreviewCertificate, [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) { throw 'Use a 64-bit Windows PowerShell.' }
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$productRoot = Join-Path $programFiles 'Reticle Studio'
$installRoot = Join-Path $productRoot 'OverlayHost'
$editorRoot = Join-Path $productRoot 'Independent'
$installedEditor = Join-Path $editorRoot 'Editor\Reticle.exe'
function Assert-NoReparsePath([string]$Path) {
    $checkPath = [IO.Path]::GetFullPath($Path)
    while ($checkPath) {
        if (Test-Path -LiteralPath $checkPath) {
            if (((Get-Item -LiteralPath $checkPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing an installation through a reparse point: $checkPath" }
        }
        $parent = [IO.Path]::GetDirectoryName($checkPath)
        if ($parent -eq $checkPath) { break }
        $checkPath = $parent
    }
}
function Test-OwnedShortcut([string]$Path, [string]$EditorExecutable) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    Assert-NoReparsePath $Path
    $shell = $null; $link = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($Path)
        if (-not $link.TargetPath) { return $false }
        return [string]::Equals([IO.Path]::GetFullPath([string]$link.TargetPath),[IO.Path]::GetFullPath($EditorExecutable),[StringComparison]::OrdinalIgnoreCase) -and $link.Arguments -eq '--independent'
    } finally {
        if ($null -ne $link) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
        if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    }
}
$recordPath = if (Test-Path -LiteralPath $installRoot) { Join-Path $installRoot 'installation.json' } elseif (Test-Path -LiteralPath $editorRoot) { Join-Path $editorRoot 'installation.json' } else { $null }
if (-not $recordPath) { Write-Host 'The independent overlay edition is not installed.'; return }
Assert-NoReparsePath $recordPath
$metadata = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
if ($metadata.schemaVersion -notin @(1,2) -or $metadata.identity -ne 'ReticleStudio.IndependentOverlay' -or $metadata.installRoot -ne $installRoot) { throw 'The target is not a recognized independent overlay installation.' }
$removeEditor = $metadata.schemaVersion -eq 2
if ($removeEditor -and ($metadata.editorRoot -ne $editorRoot -or $metadata.editorExecutable -ne $installedEditor)) { throw 'Unexpected independent editor installation path.' }
foreach ($directory in @($installRoot,$editorRoot)) { if (Test-Path -LiteralPath $directory) { Assert-NoReparsePath $directory } }
if ($removeEditor -and (Test-Path -LiteralPath $editorRoot)) {
    $editorRecord = Get-Content -LiteralPath (Join-Path $editorRoot 'installation.json') -Raw | ConvertFrom-Json
    if ($editorRecord.schemaVersion -ne 2 -or $editorRecord.identity -ne $metadata.identity -or $editorRecord.editorRoot -ne $editorRoot -or $editorRecord.installRoot -ne $installRoot) { throw 'Independent editor ownership record does not match.' }
}
$owned = @($metadata.ownedPreviewCertificates)
foreach ($thumbprint in $owned) { if ($thumbprint -notmatch '\A[a-fA-F0-9]{40}\z') { throw 'Invalid certificate ownership record.' } }
Write-Host "Independent overlay helper: $installRoot"
if ($removeEditor) { Write-Host "Independent editor: $installedEditor" }
Write-Host "Version: $($metadata.version)"
Write-Host "Preview certificates originally added by this installer: $($owned -join ', ')"
if ($VerifyOnly) { return }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run the uninstaller in a 64-bit administrator PowerShell.' }
foreach ($process in @(Get-Process -Name Reticle.OverlayHost,Reticle -ErrorAction SilentlyContinue)) {
    if ($process.Path -and [IO.Path]::GetFullPath($process.Path) -in @((Join-Path $installRoot 'Reticle.OverlayHost.exe'),$installedEditor)) { throw 'Close the installed independent editor and crosshair before uninstalling.' }
}
if ($removeEditor -and $metadata.desktopShortcut) {
    $shortcutName = 'Reticle Studio ' + ([string][char]0x72EC) + [char]0x7ACB + [char]0x8986 + [char]0x76D6 + [char]0x7248 + '.lnk'
    $expectedShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)) $shortcutName
    if (-not [string]::Equals([string]$metadata.desktopShortcut,$expectedShortcut,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected managed shortcut location.' }
    if (Test-Path -LiteralPath $expectedShortcut) {
        if (Test-OwnedShortcut $expectedShortcut $installedEditor) { Remove-Item -LiteralPath $expectedShortcut; Write-Host "Removed desktop shortcut: $expectedShortcut" }
        else { Write-Host 'The desktop shortcut was changed by the user and was retained.' }
    }
}
$targets = @($installRoot)
if ($removeEditor) { $targets += $editorRoot }
foreach ($target in $targets) {
    $resolvedInstall = [IO.Path]::GetFullPath($target)
    if ([IO.Path]::GetDirectoryName($resolvedInstall) -ne $productRoot -or [IO.Path]::GetFileName($resolvedInstall) -notin @('OverlayHost','Independent') -or -not $resolvedInstall.StartsWith($programFiles + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected uninstall target.' }
    if (Test-Path -LiteralPath $resolvedInstall) {
        Assert-NoReparsePath $resolvedInstall
        if (@(Get-ChildItem -LiteralPath $resolvedInstall -Recurse -Force | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }).Count) { throw 'Uninstall encountered a reparse point.' }
        Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
        if (Test-Path -LiteralPath $resolvedInstall) { throw 'The independent installation directory could not be completely removed.' }
    }
}
if ($RemovePreviewCertificate) {
    foreach ($thumbprint in $owned) {
        $certificatePath = 'Cert:\LocalMachine\Root\' + $thumbprint
        if (-not (Test-Path -LiteralPath $certificatePath)) { continue }
        $certificate = Get-Item -LiteralPath $certificatePath
        if ($certificate.Subject -ne 'CN=Reticle Studio Independent Preview' -or $certificate.Issuer -ne $certificate.Subject) { throw "Refusing to remove an unexpected certificate: $thumbprint" }
        Remove-Item -LiteralPath $certificatePath
        Write-Host "Removed preview certificate: $thumbprint"
    }
}
if ((Test-Path -LiteralPath $productRoot) -and @(Get-ChildItem -LiteralPath $productRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $productRoot }
Write-Host 'Independent overlay edition removed. Other editions and personal configuration were not removed.'
if ($owned.Count -and -not $RemovePreviewCertificate) { Write-Host 'Preview certificates were retained. To remove them, use the exact recorded thumbprints shown above in Local Computer > Trusted Root Certification Authorities.' }
