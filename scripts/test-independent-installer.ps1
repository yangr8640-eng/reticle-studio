[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$scriptPath = Join-Path $PSScriptRoot 'install-independent.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Installer has syntax errors.' }
foreach ($name in @('Assert-NoReparsePath','Assert-ProtectedDirectory','Assert-ProtectedAcl','Initialize-IndependentShortcut','Get-IndependentShortcut','Test-IndependentShortcut','Write-IndependentShortcut','Assert-ExpectedCertificateThumbprint')) {
    $definition = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | Where-Object Name -eq $name
    if (@($definition).Count -ne 1) { throw "Missing installer function: $name" }
    . ([scriptblock]::Create($definition.Extent.Text))
}
$script:passed = 0
function Check([string]$Name, [scriptblock]$Body) { & $Body; $script:passed++; Write-Host "PASS $Name" }
function New-TestAcl([string]$Sddl) {
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetSecurityDescriptorSddlForm($Sddl)
    return $acl
}
function Reject([Security.AccessControl.DirectorySecurity]$Acl, [string]$Expected) {
    $rejected = $false
    try { Assert-ProtectedAcl $Acl 'in-memory fixture' }
    catch { if ($_.Exception.Message -notlike ('*' + $Expected + '*')) { throw }; $rejected = $true }
    if (-not $rejected) { throw 'Expected the ACL to be rejected.' }
}
$unknownSid = 'S-1-5-21-3131313131-3232323232-3333333333-4141'
Check 'Actual Program Files ACL can be checked without account-name lookup' {
    Assert-ProtectedDirectory ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles))
}
Check 'Unmapped read-only SID does not cause IdentityNotMappedException' {
    Assert-ProtectedAcl (New-TestAcl "O:BAG:BAD:(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;$unknownSid)") 'unmapped read fixture'
}
Check 'Application package SIDs are accepted with read-only access' {
    Assert-ProtectedAcl (New-TestAcl 'O:BAG:BAD:(A;;FA;;;BA)(A;;GRGX;;;S-1-15-2-1)(A;;GRGX;;;S-1-15-2-2)') 'package SID fixture'
}
Check 'Unmapped SID with ordinary write permission remains rejected' {
    Reject (New-TestAcl "O:BAG:BAD:(A;;FA;;;BA)(A;;FW;;;$unknownSid)") 'non-administrative write access'
}
Check 'GenericWrite on an untrusted SID remains rejected' {
    Reject (New-TestAcl "O:BAG:BAD:(A;;FA;;;BA)(A;;GW;;;$unknownSid)") 'non-administrative write access'
}
Check 'GenericAll on an untrusted SID remains rejected' {
    Reject (New-TestAcl "O:BAG:BAD:(A;;FA;;;BA)(A;;GA;;;$unknownSid)") 'non-administrative write access'
}
Check 'An untrusted owner remains rejected without name resolution' {
    Reject (New-TestAcl "O:$($unknownSid)G:BAD:(A;;FR;;;BU)") 'unexpected owner'
}
Check 'A deny rule does not grant write permission' {
    Assert-ProtectedAcl (New-TestAcl "O:BAG:BAD:(D;;GA;;;$unknownSid)(A;;FA;;;BA)(A;;GRGX;;;$unknownSid)") 'deny fixture'
}
Check 'VerifyOnly refuses shortcut creation before touching the package' {
    $rejected = $false
    try { & $scriptPath -PackageDirectory 'Z:\reticle-check-package-does-not-exist' -VerifyOnly -CreateDesktopShortcut }
    catch { if ($_.Exception.Message -notlike '*cannot be combined*') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'VerifyOnly accepted an installation option.' }
}
Check 'CLI remains compatible when no expected certificate was supplied' {
    Assert-ExpectedCertificateThumbprint ('a' * 40) ''
}
Check 'Displayed certificate pin matches without depending on hexadecimal case' {
    Assert-ExpectedCertificateThumbprint ('aB' * 20) ('Ab' * 20)
}
Check 'Replacing the displayed certificate is rejected' {
    $rejected = $false
    try { Assert-ExpectedCertificateThumbprint ('a' * 40) ('b' * 40) }
    catch { if ($_.Exception.Message -notlike '*certificate changed*') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'A different displayed certificate was accepted.' }
}
Check 'Malformed expected thumbprint is rejected before reading a package' {
    foreach ($bad in @(('z' * 40), ('a' * 39), ('a' * 41), (('a' * 40) + ' '))) {
        $rejected = $false
        try { & $scriptPath -PackageDirectory 'Z:\reticle-check-package-does-not-exist' -VerifyOnly -ExpectedCertificateThumbprint $bad }
        catch { if ($_.Exception.Message -notlike '*40 hexadecimal characters*') { throw }; $rejected = $true }
        if (-not $rejected) { throw 'An invalid expected certificate was accepted.' }
    }
}
$uninstallerPath = Join-Path $PSScriptRoot 'uninstall-independent.ps1'
$uninstallerAst = [Management.Automation.Language.Parser]::ParseFile($uninstallerPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Uninstaller has syntax errors.' }
Check 'Installer and standalone uninstaller use the same Unicode COM contract' {
    $installerInitializer = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Initialize-IndependentShortcut' }, $true)
    $uninstallerInitializer = $uninstallerAst.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Initialize-IndependentShortcut' }, $true)
    if (@($uninstallerInitializer).Count -ne 1 -or $installerInitializer.Extent.Text.Replace("`r`n","`n") -ne $uninstallerInitializer.Extent.Text.Replace("`r`n","`n")) { throw 'Installer and uninstaller shortcut interop declarations differ.' }
}
$definition = $uninstallerAst.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-OwnedShortcut' }, $true)
if (@($definition).Count -ne 1) { throw 'Missing owned-shortcut check in uninstaller.' }
. ([scriptblock]::Create($definition.Extent.Text))
$shortcutTestRoot = Join-Path ([IO.Path]::GetTempPath()) ('reticle-shortcut-check-' + [Guid]::NewGuid().ToString('N'))
$shortcutPath = Join-Path $shortcutTestRoot ('Reticle Studio ' + [char]0x6D4B + [char]0x8BD5 + '.lnk')
$target = Join-Path $env:SystemRoot 'System32\notepad.exe'
$otherTarget = Join-Path $env:SystemRoot 'System32\cmd.exe'
New-Item -ItemType Directory -Path $shortcutTestRoot | Out-Null
try {
    Check 'Native shortcut supports an ASCII file name' {
        $asciiPath = Join-Path $shortcutTestRoot 'Reticle Studio ASCII.lnk'
        Write-IndependentShortcut $asciiPath $target
        if (-not (Test-OwnedShortcut $asciiPath $target)) { throw 'ASCII shortcut did not round trip.' }
        Remove-Item -LiteralPath $asciiPath
    }
    Check 'Shortcut saves target, independent argument, working directory and icon' {
        Write-IndependentShortcut $shortcutPath $target
        $saved = Get-IndependentShortcut $shortcutPath
        if (-not (Test-IndependentShortcut $shortcutPath $target) -or $saved.WorkingDirectory -ne [IO.Path]::GetDirectoryName($target) -or $saved.IconLocation -ne ($target + ',0')) { throw 'Shortcut properties did not round trip.' }
    }
    Check 'Managed shortcut can be refreshed without duplicate files' {
        Write-IndependentShortcut $shortcutPath $target
        if (@(Get-ChildItem -LiteralPath $shortcutTestRoot -File).Count -ne 1) { throw 'Shortcut update created extra files.' }
    }
    Check 'Uninstaller recognizes only its own target and independent argument' {
        if (-not (Test-OwnedShortcut $shortcutPath $target) -or (Test-OwnedShortcut $shortcutPath $otherTarget)) { throw 'Shortcut ownership check failed.' }
    }
    Check 'Installer refuses to overwrite a shortcut for a different application' {
        [ReticleIndependent.NativeShortcut]::Write($shortcutPath,$otherTarget,[IO.Path]::GetDirectoryName($otherTarget),'')
        $rejected = $false
        try { Write-IndependentShortcut $shortcutPath $target }
        catch { if ($_.Exception.Message -notlike '*different shortcut*') { throw }; $rejected = $true }
        if (-not $rejected -or (Get-IndependentShortcut $shortcutPath).TargetPath -ne $otherTarget -or (Test-OwnedShortcut $shortcutPath $target)) { throw 'Foreign shortcut was overwritten or accepted for removal.' }
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($shortcutTestRoot)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -notmatch '\Areticle-shortcut-check-[a-f0-9]{32}\z') { throw 'Unexpected shortcut-test cleanup target.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host "All $script:passed installer checks passed. Temporary shortcut fixtures removed; no installation, real desktop, certificate or protected ACL was changed."
