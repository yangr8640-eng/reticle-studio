[CmdletBinding()]
param([string]$RenderPath = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = Join-Path $PSScriptRoot 'setup-independent.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Setup script has syntax errors.' }
foreach ($name in @('ConvertTo-ProcessArgument','New-SetupWorkerArguments','New-SetupWindow','Invoke-SetupWorker')) {
    $definition = @($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true) | Where-Object Name -eq $name)
    if ($definition.Count -ne 1) { throw "Missing setup function: $name" }
    . ([scriptblock]::Create($definition[0].Extent.Text))
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ReticleSetupCommandLine {
 [DllImport("shell32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(string text,out int count);
 [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
 public static string[] Parse(string text) {
  int count; IntPtr values=CommandLineToArgvW(text,out count);
  if(values==IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
  try { var result=new string[count]; for(int i=0;i<count;i++) result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(values,i*IntPtr.Size)); return result; }
  finally { LocalFree(values); }
 }
}
'@
$script:passed = 0
function Check([string]$Name,[scriptblock]$Body) { & $Body; $script:passed++; Write-Host "PASS $Name" }
function Assert([bool]$Value) { if (-not $Value) { throw 'Assertion failed.' } }
$thumbprint = '0123456789ABCDEF0123456789ABCDEF01234567'
Check 'Native command-line parsing preserves spaces, quotes, slashes and empty values' {
    $inputs = @('', 'simple', 'C:\directory with spaces\', 'has"quote', 'two\\"quotes', "C:\Unicode-$([char]0x4E2D)\file.ps1", '$(not-a-command)', '`not-a-command')
    $line = 'powershell.exe ' + (($inputs | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join ' ')
    $parsed = [ReticleSetupCommandLine]::Parse($line)
    Assert ($parsed.Length -eq $inputs.Length+1)
    for($i=0;$i -lt $inputs.Length;$i++) { Assert ($parsed[$i+1] -ceq $inputs[$i]) }
}
Check 'Both unchecked options stay absent from elevated worker arguments' {
    $arguments = @(New-SetupWorkerArguments 'C:\new folder\setup.ps1' 'C:\package' 'C:\Temp\result.json' $false $false $thumbprint)
    Assert ('-InstallWorker' -in $arguments -and '-CreateDesktopShortcut' -notin $arguments -and '-TrustPreviewCertificate' -notin $arguments)
    Assert ($arguments[($arguments.IndexOf('-File')+1)] -eq 'C:\new folder\setup.ps1')
}
Check 'Selected installation options are explicitly propagated' {
    $arguments = @(New-SetupWorkerArguments 'C:\setup.ps1' 'C:\package' 'C:\Temp\result.json' $true $true $thumbprint)
    Assert (@($arguments | Where-Object { $_ -eq '-CreateDesktopShortcut' }).Count -eq 1)
    Assert (@($arguments | Where-Object { $_ -eq '-TrustPreviewCertificate' }).Count -eq 1)
    Assert ($arguments[($arguments.IndexOf('-ExecutionPolicy')+1)] -eq 'RemoteSigned')
}
Check 'Shortcut selection cannot silently opt into certificate trust' {
    $arguments = @(New-SetupWorkerArguments 'C:\setup.ps1' 'C:\package' 'C:\Temp\result.json' $true $false $thumbprint)
    Assert ('-CreateDesktopShortcut' -in $arguments -and '-TrustPreviewCertificate' -notin $arguments)
}
Check 'The displayed certificate thumbprint stays pinned in elevated arguments' {
    $arguments = @(New-SetupWorkerArguments 'C:\setup.ps1' 'C:\package' 'C:\Temp\result.json' $true $true $thumbprint)
    Assert (@($arguments | Where-Object {$_ -eq '-ExpectedCertificateThumbprint'}).Count -eq 1)
    Assert ($arguments[$arguments.IndexOf('-ExpectedCertificateThumbprint')+1] -ceq $thumbprint)
}
Check 'Missing and malformed certificate authorization pins are rejected' {
    foreach($invalid in @('', '123', ('G'*40), ('A'*39), ('A'*41), (('A'*40)+"`n"))) {
        $rejected = $false
        try { New-SetupWorkerArguments 'C:\setup.ps1' 'C:\package' 'C:\Temp\result.json' $true $true $invalid | Out-Null }
        catch { $rejected = $true }
        Assert $rejected
    }
}
Check 'The worker forwards the exact displayed pin and options to the installer' {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
    $fixture = Join-Path $temporaryRoot ('Reticle-Setup-Tests-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $fixture | Out-Null
    try {
        @'
param([string]$PackageDirectory,[string]$ExpectedCertificateThumbprint,[switch]$CreateDesktopShortcut,[switch]$TrustPreviewCertificate)
[ordered]@{ pin=$ExpectedCertificateThumbprint; shortcut=[bool]$CreateDesktopShortcut; trust=[bool]$TrustPreviewCertificate; package=$PackageDirectory } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PackageDirectory 'received.json') -Encoding UTF8
'@ | Set-Content -LiteralPath (Join-Path $fixture 'install-independent.ps1') -Encoding UTF8
        $result = Join-Path $fixture ('result-' + [Guid]::NewGuid().ToString('N') + '.json')
        $exitCode = Invoke-SetupWorker $fixture $result $true $false $thumbprint
        $received = Get-Content -LiteralPath (Join-Path $fixture 'received.json') -Raw | ConvertFrom-Json
        $outcome = Get-Content -LiteralPath $result -Raw -Encoding UTF8 | ConvertFrom-Json
        Assert ($exitCode -eq 0 -and $outcome.success -eq $true)
        Assert ($received.pin -ceq $thumbprint -and $received.shortcut -eq $true -and $received.trust -eq $false)
        Assert ($received.package -eq $fixture)
    } finally {
        $resolved = [IO.Path]::GetFullPath($fixture)
        if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot -or [IO.Path]::GetFileName($resolved) -notmatch '\AReticle-Setup-Tests-[a-f0-9]{32}\z') { throw 'Unexpected fixture cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
Check 'PowerShell 5.1 can read the localized setup source as UTF-8 BOM' {
    $bytes = [IO.File]::ReadAllBytes($source)
    Assert ($bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf)
}
$package = [pscustomobject]@{
    Version='0.3.0-independent.1'; CertificateSubject='CN=Reticle Studio Independent Preview';
    CertificateThumbprint='0123456789ABCDEF0123456789ABCDEF01234567'; CertificateExpires='2027-10-26';
    TrustRequired=$true; EditorPath='C:\Program Files\Reticle Studio\Independent\Editor\Reticle.exe'
}
$window = New-SetupWindow $package
try {
    Check 'Installation window leaves root trust unchecked and optional shortcut selected' {
        Assert ($window.FindName('TrustOption').IsChecked -eq $false)
        Assert ($window.FindName('TrustOption').IsEnabled)
        Assert ($window.FindName('ShortcutOption').IsChecked -eq $true)
        Assert ($window.FindName('ThumbprintText').Text.Contains($package.CertificateThumbprint))
    }
    if ($RenderPath) {
        if (-not [IO.Path]::IsPathRooted($RenderPath) -or (Test-Path -LiteralPath $RenderPath)) { throw 'RenderPath must be a new absolute filename.' }
        $window.ShowActivated = $false
        $window.Show(); $window.UpdateLayout()
        $frame = [Windows.Threading.DispatcherFrame]::new()
        $window.Dispatcher.BeginInvoke([Windows.Threading.DispatcherPriority]::ApplicationIdle,[Action]{$frame.Continue=$false}) | Out-Null
        [Windows.Threading.Dispatcher]::PushFrame($frame)
        $visual = $window
        $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int]$visual.ActualWidth,[int]$visual.ActualHeight,96,96,[Windows.Media.PixelFormats]::Pbgra32)
        $bitmap.Render($visual)
        $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
        $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
        $file = [IO.FileStream]::new($RenderPath,[IO.FileMode]::CreateNew)
        try {$encoder.Save($file)}finally{$file.Dispose()}
    }
} finally { $window.Close() }
$package.TrustRequired = $false
$trustedWindow = New-SetupWindow $package
try {
    Check 'An already trusted certificate does not add a new trust selection' {
        Assert (-not $trustedWindow.FindName('TrustOption').IsEnabled)
        Assert ($trustedWindow.FindName('TrustOption').IsChecked -eq $false)
    }
} finally {$trustedWindow.Close()}
Write-Host "All $script:passed setup checks passed. No installer or UAC launch was performed."
