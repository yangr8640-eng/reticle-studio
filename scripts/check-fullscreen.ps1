[CmdletBinding()]
param([string]$GameExecutable = '', [string]$OutputPath = '')

# Read-only environment checks. No game, registry, certificate or security settings are changed.
$ErrorActionPreference = 'Stop'
$candidatePaths = [Collections.Generic.List[string]]::new()
if ($GameExecutable) {
    $gameFile = Get-Item -LiteralPath $GameExecutable
    if ($gameFile.Extension -ne '.exe' -or $gameFile.PSIsContainer) { throw 'GameExecutable must name an existing .exe file.' }
    $candidatePaths.Add($gameFile.FullName)
} else {
    foreach ($gameProcess in @(Get-Process -Name cs2,csgo -ErrorAction SilentlyContinue)) {
        if ($gameProcess.Path) { $candidatePaths.Add($gameProcess.Path) }
    }
    $steamRoot = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if ($steamRoot) {
        $libraries = [Collections.Generic.List[string]]::new()
        $libraries.Add($steamRoot)
        $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryFile) {
            $libraryText = Get-Content -LiteralPath $libraryFile -Raw
            foreach ($match in [regex]::Matches($libraryText, '"path"\s+"([^"\r\n]+)"')) {
                $libraries.Add($match.Groups[1].Value.Replace('\\', '\'))
            }
        }
        foreach ($library in ($libraries | Select-Object -Unique)) {
            $manifestPath = Join-Path $library 'steamapps\appmanifest_730.acf'
            if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
            $manifest = Get-Content -LiteralPath $manifestPath -Raw
            $installDir = [regex]::Match($manifest, '"installdir"\s+"([^"\r\n]+)"').Groups[1].Value
            if (-not $installDir -or $installDir.Contains('..') -or [IO.Path]::IsPathRooted($installDir)) { continue }
            foreach ($relative in @('game\bin\win64\cs2.exe', 'csgo.exe')) {
                $path = Join-Path $library ('steamapps\common\' + $installDir + '\' + $relative)
                if (Test-Path -LiteralPath $path -PathType Leaf) { $candidatePaths.Add([IO.Path]::GetFullPath($path)) }
            }
        }
    }
}

$games = @($candidatePaths | Select-Object -Unique | ForEach-Object {
    $path = $_
    $layers = @()
    foreach ($hive in @('HKCU:', 'HKLM:')) {
        $key = $hive + '\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers'
        if (-not (Test-Path -LiteralPath $key)) { continue }
        $properties = Get-ItemProperty -LiteralPath $key
        $entry = $properties.PSObject.Properties | Where-Object Name -eq $path | Select-Object -First 1
        if ($entry) { $layers += [pscustomobject]@{ scope=$hive; flags=[string]$entry.Value } }
    }
    [pscustomobject]@{
        executable=[IO.Path]::GetFileName($path)
        # The path stays local unless the user chooses to share the optional report.
        path=$path
        compatibility=$layers
        fullscreenOptimizationsExplicitlyDisabled=@($layers | Where-Object { $_.flags -match '(^|\s)DISABLEDXMAXIMIZEDWINDOWEDMODE(\s|$)' }).Count -gt 0
        runAsAdministratorFlag=@($layers | Where-Object { $_.flags -match '(^|\s)RUNASADMIN(\s|$)' }).Count -gt 0
    }
})
$os = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$gameBar = @(Get-AppxPackage -Name Microsoft.XboxGamingOverlay -ErrorAction SilentlyContinue | Select-Object Name,Version,Status)
$report = [ordered]@{
    capturedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
    windowsBuild=$os.CurrentBuildNumber
    windowsVersion=$os.DisplayVersion
    gameBar=$gameBar
    games=$games
    limitation='These settings cannot prove the actual presentation path. Test Win+G while CS is fullscreen. No settings were changed.'
}
$json = $report | ConvertTo-Json -Depth 6
$json
if ($OutputPath) {
    $outputFile = [IO.Path]::GetFullPath($OutputPath)
    if (Test-Path -LiteralPath $outputFile) { throw 'OutputPath already exists; choose a new report filename.' }
    [IO.File]::WriteAllText($outputFile, $json, [Text.UTF8Encoding]::new($false))
    Write-Host "Saved local report: $outputFile"
}
