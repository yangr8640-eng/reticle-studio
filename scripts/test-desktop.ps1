$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'src\Reticle.App\bin\Release\net10.0-windows\Reticle.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run scripts/build.ps1 first.' }
$outputDir = Join-Path $root 'artifacts\diagnostics'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$report = Join-Path $outputDir 'results.json'
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$process = Start-Process -FilePath $exe -ArgumentList @('--diagnostics', ('"' + $report + '"')) -PassThru -Wait -WindowStyle Hidden
if (-not (Test-Path -LiteralPath $report)) { throw 'Desktop diagnostics did not produce a report.' }
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0 -or (Get-Content -LiteralPath $report -Raw | ConvertFrom-Json).passed -ne $true) { throw 'Desktop diagnostics failed.' }
