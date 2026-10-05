# Yorehold (Godot) check: builds the C#, runs the unit tests and starts the game headless once.
# Never opens a window and never waits for a key, so it is safe to run unattended.
#   .\check.ps1              build + tests + headless start
#   .\check.ps1 -NoTest      build only
# Exit code 0 and ALL OK = all good. Full output is in ..\.dev\godot-build.log, -test.log, -run.log.

param(
    [switch]$NoTest
)

$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$dev = Join-Path (Split-Path $root -Parent) '.dev'
New-Item -ItemType Directory -Force $dev | Out-Null
$godot = 'D:\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { Write-Host "FAIL: Godot not found at $godot"; exit 2 }

$buildLog = Join-Path $dev 'godot-build.log'
dotnet build (Join-Path $root 'Yorehold.slnx') -nologo -v q *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Select-String -Path $buildLog -Pattern ': error ' | Select-Object -First 20 | ForEach-Object { $_.Line }
    Write-Host 'FAIL: build'; exit 1
}
Write-Host 'build ok'
if ($NoTest) { Write-Host 'ALL OK'; exit 0 }

$testLog = Join-Path $dev 'godot-test.log'
dotnet test (Join-Path $root 'tests\Yorehold.Rules.Tests.csproj') --no-build -nologo -v q *> $testLog
if ($LASTEXITCODE -ne 0) {
    Select-String -Path $testLog -Pattern 'Failed |error' | Select-Object -First 20 | ForEach-Object { $_.Line }
    Write-Host 'FAIL: tests'; exit 1
}
Select-String -Path $testLog -Pattern '^(Passed|Failed)!' | ForEach-Object { $_.Line }
Write-Host 'tests ok'

# A headless start catches broken scenes and scripts that only fail inside the engine.
$runLog = Join-Path $dev 'godot-run.log'
$job = Start-Job { param($g, $p) & $g --headless --path $p --quit-after 30 2>&1 } -ArgumentList $godot, $root
if (-not (Wait-Job $job -Timeout 120)) { Stop-Job $job; Write-Host 'FAIL: headless run timed out'; exit 1 }
Receive-Job $job *> $runLog
Remove-Job $job
$errors = Select-String -Path $runLog -Pattern 'ERROR|SCRIPT ERROR|Unhandled exception'
if ($errors) {
    $errors | Select-Object -First 20 | ForEach-Object { $_.Line }
    Write-Host 'FAIL: headless run'; exit 1
}
Write-Host 'run ok'
Write-Host 'ALL OK'
exit 0
