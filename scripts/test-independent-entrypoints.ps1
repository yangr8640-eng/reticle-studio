[CmdletBinding()]
param([string]$SourceDirectory = '')

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess) { throw 'Run these checks in a 64-bit Windows PowerShell.' }
if (-not $PSBoundParameters.ContainsKey('SourceDirectory')) { $SourceDirectory = $PSScriptRoot }
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)

function ConvertTo-NativeArgument([string]$Value) {
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Invoke-EntrypointProcess([string]$Executable, [string]$Arguments, [string]$WorkingDirectory, [string]$ProbePath = '') {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.Arguments = $Arguments
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    if ($ProbePath) { $start.EnvironmentVariables['ReticleEntrypointProbe'] = $ProbePath }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(20000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Entrypoint timed out: $Executable $Arguments"
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        }
    } finally { $process.Dispose() }
}

$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$engines = @($windowsPowerShell)
$currentPwsh = Join-Path $PSHOME 'pwsh.exe'
if (Test-Path -LiteralPath $currentPwsh -PathType Leaf) { $engines += $currentPwsh }
else {
    $pwsh = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($pwsh) { $engines += $pwsh.Source }
}
$engines = @($engines | Select-Object -Unique)
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$fixture = Join-Path $temporaryRoot ('Reticle-Entrypoint-Tests-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixture)
$passed = 0
try {
    $unrelatedDirectory = Join-Path $fixture 'unrelated working directory'
    $separateScripts = Join-Path $fixture 'separate entry scripts'
    $null = [IO.Directory]::CreateDirectory($unrelatedDirectory)
    $null = [IO.Directory]::CreateDirectory($separateScripts)
    foreach ($name in @('setup-independent.ps1','install-independent.ps1')) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $separateScripts
    }
    $localizedName = 'edge' + [char]0x4E0B + [char]0x8F7D + ' with spaces [package] & files'
    $packageName = 'Reticle-Independent-v0.3.0-independent.1-win-x64'
    $layouts = @('plain', $localizedName, (Join-Path $packageName $packageName))
    foreach ($layout in $layouts) {
        $packageRoot = Join-Path $fixture $layout
        $null = [IO.Directory]::CreateDirectory($packageRoot)
        foreach ($name in @('setup-independent.ps1','install-independent.ps1')) {
            Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination $packageRoot
        }
        # Both real scripts must reach this early validation failure. It is
        # before certificate access, elevation, or any installation writes.
        [IO.File]::WriteAllText((Join-Path $packageRoot 'package-files.json'), '{"schemaVersion":-1}')
        foreach ($engine in $engines) {
            foreach ($entry in @(
                @{ Script='setup-independent.ps1'; Option='-InspectOnly' },
                @{ Script='install-independent.ps1'; Option='-VerifyOnly' }
            )) {
                foreach ($explicitDirectory in @($false,$true)) {
                    # Explicit paths use scripts outside the package, so a
                    # silently ignored -PackageDirectory cannot pass the test.
                    $entryRoot = if ($explicitDirectory) { $separateScripts } else { $packageRoot }
                    $arguments = @('-NoLogo','-NoProfile','-STA','-ExecutionPolicy','RemoteSigned','-File',
                        (Join-Path $entryRoot $entry.Script),$entry.Option)
                    if ($explicitDirectory) { $arguments += @('-PackageDirectory',$packageRoot) }
                    $line = ($arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' '
                    $result = Invoke-EntrypointProcess $engine $line $unrelatedDirectory
                    $label = ([IO.Path]::GetFileName($engine)) + ' -File ' + $entry.Script + ' explicit=' + $explicitDirectory + ' layout=' + $layout
                    if ($result.ExitCode -eq 0 -or $result.Output -notmatch 'Unexpected independent overlay metadata\.') {
                        throw ("FAIL $label (exit $($result.ExitCode))`n" + $result.Output)
                    }
                    $passed++
                    Write-Host "PASS $label"
                }
            }
        }

        # Exercise the actual CMD launcher with a harmless script replacing
        # setup. The real setup checks above always use -InspectOnly.
        $launcherRoot = Join-Path $packageRoot 'launcher probe'
        $null = [IO.Directory]::CreateDirectory($launcherRoot)
        Copy-Item -LiteralPath (Join-Path $sourceRoot 'Setup-Independent.cmd') -Destination $launcherRoot
        $probeScript = @'
param([string]$PackageDirectory = '')
[ordered]@{
    ScriptRoot = $PSScriptRoot
    PackageDirectory = $PackageDirectory
    Is64Bit = [Environment]::Is64BitProcess
    Apartment = [Threading.Thread]::CurrentThread.GetApartmentState().ToString()
} | ConvertTo-Json | Set-Content -LiteralPath $env:ReticleEntrypointProbe -Encoding UTF8
exit 0
'@
        [IO.File]::WriteAllText((Join-Path $launcherRoot 'setup-independent.ps1'), $probeScript)
        $probePath = Join-Path $fixture ('probe-' + [Guid]::NewGuid().ToString('N') + '.json')
        $cmdLine = '/d /s /c ""' + (Join-Path $launcherRoot 'Setup-Independent.cmd') + '""'
        $result = Invoke-EntrypointProcess (Join-Path $env:SystemRoot 'System32\cmd.exe') $cmdLine $unrelatedDirectory $probePath
        if ($result.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $probePath)) { throw ("CMD launcher failed for $layout`n" + $result.Output) }
        $probe = Get-Content -LiteralPath $probePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($probe.ScriptRoot -ne $launcherRoot -or -not $probe.Is64Bit -or $probe.Apartment -ne 'STA' -or ($probe.PackageDirectory -and $probe.PackageDirectory -ne $launcherRoot)) {
            throw "CMD launcher changed the script path or host options for $layout."
        }
        $passed++
        Write-Host "PASS actual CMD launcher from unrelated working directory: $layout"
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $temporaryRoot -or [IO.Path]::GetFileName($resolved) -notmatch '\AReticle-Entrypoint-Tests-[a-f0-9]{32}\z') { throw 'Unexpected entrypoint-test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Host "All $passed independent entrypoint checks passed. No installer or UAC launch was performed."
