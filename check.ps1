# Yorehold (Godot) check: builds the C#, runs the unit tests and starts the game headless once.
# Never opens a window and never waits for a key, so it is safe to run unattended.
#   .\check.ps1              build + tests + headless start
#   .\check.ps1 -NoTest      build only
#   .\check.ps1 -Shot a.png  also run the game in a window kept off screen and save ..\.dev\a.png
#                            -Frames N (default 300) is how long it runs, -Script file.txt drives it
#                            (format: tests\visual\scripts, same as the C++ client's)
#                            -Chapter chapters\goblin-keep plays that chapter instead of the scene's
#                            -Screen title starts the run on the title, -Screen create in Create
#                            (default: play, straight into the game)
#                            -Import book.pdf reads that book into a new package, builds it and
#                            opens it in Create's Map mode
#   .\check.ps1 -Playtest fight   sets up that test from playtest\queue.json off screen like
#                            playtest.ps1 would, playtest window and all, and saves
#                            ..\.dev\playtest-fight.png a second and a half after its setup ends
# Exit code 0 and ALL OK = all good. Full output is in ..\.dev\godot-build.log, -test.log, -run.log,
# -shot.log.

param(
    [switch]$NoTest,
    [string]$Shot,
    [int]$Frames = 300,
    [string]$Script,
    [string]$Chapter,
    [string]$Screen = 'play',
    [string]$Playtest,
    [string]$Import
)

# The screenshot runs to make: the one -Shot asks for, or one per playtest (-Playtest all for every one).
$runs = @()
if ($Playtest) {
    $queueFile = Join-Path $PSScriptRoot 'playtest\queue.json'
    $items = @((Get-Content $queueFile -Raw | ConvertFrom-Json).items)
    if ($Playtest -ne 'all') { $items = @($items | Where-Object { $_.id -eq $Playtest }) }
    if ($items.Count -eq 0) { Write-Host "FAIL: no test called $Playtest in playtest\queue.json"; exit 2 }
    $results = Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) '.dev') 'playtest-check.jsonl'
    foreach ($item in $items) {
        # without an until the whole script is the setup: its last frame
        $until = if ($item.until) { [int]$item.until }
                 elseif ($item.script) { (Get-Content (Join-Path $PSScriptRoot $item.script) | Where-Object { $_ -match '^\d+' } | ForEach-Object { [int]($_ -split ' ')[0] } | Measure-Object -Maximum).Maximum }
                 else { 0 }
        $extra = @('--playtest', $item.id, '--queue', "`"$queueFile`"", '--results', "`"$results`"", '--number', 'check')
        if ($item.until) { $extra += @('--until', "$until") }
        $runs += @{
            Shot = if ($Shot -and $items.Count -eq 1) { $Shot } else { "playtest-$($item.id).png" }
            Frames = if ($PSBoundParameters.ContainsKey('Frames')) { $Frames } else { $until + 90 }
            Script = if ($item.script) { Join-Path $PSScriptRoot $item.script } else { '' }
            Chapter = if ($item.chapter) { $item.chapter } else { '' }
            Screen = if ($item.screen) { $item.screen } else { 'play' }
            Extra = $extra
        }
    }
}
elseif ($Shot) {
    # -Import book.pdf reads, builds and opens that book in Create (story model from the settings or YOREHOLD_IMPORT_MODEL)
    $extra = if ($Import) { @('--import', "`"$((Resolve-Path $Import).Path)`"") } else { @() }
    $runs += @{ Shot = $Shot; Frames = $Frames; Script = $Script; Chapter = $Chapter; Screen = $Screen; Extra = $extra }
}

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
# The normal console logger prints each failure's message; the quiet one only names the test.
$testOut = dotnet test (Join-Path $root 'tests\Yorehold.Rules.Tests.csproj') --no-build -nologo -v q `
    --logger 'console;verbosity=normal' 2>&1 | ForEach-Object { "$_" }
$testCode = $LASTEXITCODE
$testOut | Out-File $testLog -Encoding utf8
if ($testCode -ne 0) {
    # 'assets/' picks up the content check's lines, which name the broken file.
    Select-String -Path $testLog -Pattern '\[FAIL\]|Failed |error|assets/' | Select-Object -First 20 | ForEach-Object { $_.Line }
    Write-Host 'FAIL: tests'; exit 1
}
Select-String -Path $testLog -Pattern '^\s*Total tests:|^(Passed|Failed)!' | ForEach-Object { $_.Line }
Write-Host 'tests ok'

# A headless start catches broken scenes and scripts that only fail inside the engine.
# Once per screen the game can start on: the title is what a player gets, the others sit behind it.
$runLog = Join-Path $dev 'godot-run.log'
Remove-Item $runLog -ErrorAction SilentlyContinue
foreach ($startOn in 'title', 'play', 'create') {
    $job = Start-Job { param($g, $p, $s) & $g --headless --path $p --quit-after 30 -- --screen $s 2>&1 } -ArgumentList $godot, $root, $startOn
    if (-not (Wait-Job $job -Timeout 120)) { Stop-Job $job; Write-Host "FAIL: headless run timed out ($startOn)"; exit 1 }
    "--- $startOn" | Out-File $runLog -Append -Encoding utf8
    Receive-Job $job 2>&1 | ForEach-Object { "$_" } | Out-File $runLog -Append -Encoding utf8
    Remove-Job $job
    $errors = Select-String -Path $runLog -Pattern 'ERROR|SCRIPT ERROR|Unhandled exception'
    if ($errors) {
        $errors | Select-Object -First 20 | ForEach-Object { $_.Line }
        Write-Host "FAIL: headless run ($startOn)"; exit 1
    }
}
Write-Host 'run ok'

foreach ($run in $runs) {
    $shotPath = if ([IO.Path]::IsPathRooted($run.Shot)) { $run.Shot } else { Join-Path $dev $run.Shot }
    Remove-Item $shotPath -ErrorAction SilentlyContinue
    # A real window, because headless draws nothing. It sits far off screen so it never gets in the
    # way, and the fixed frame rate makes the same script give the same run every time.
    $arguments = @('--path', "`"$root`"", '--windowed', '--position', '-4000,-4000', '--resolution', '1280x720',
        '--fixed-fps', '60', '--', '--shot', "`"$shotPath`"", '--frames', "$($run.Frames)", '--screen', $run.Screen)
    if ($run.Script) {
        if (-not (Test-Path $run.Script)) { Write-Host "FAIL: script not found: $($run.Script)"; exit 1 }
        $arguments += @('--script', "`"$((Resolve-Path $run.Script).Path)`"")
    }
    if ($run.Chapter) { $arguments += @('--chapter', ($run.Chapter -replace '\\', '/')) }
    $arguments += $run.Extra
    $shotLog = Join-Path $dev 'godot-shot.log'
    $p = Start-Process $godot -ArgumentList $arguments -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput $shotLog -RedirectStandardError "$shotLog.err"
    $null = $p.Handle # without this PowerShell forgets the exit code
    if (-not $p.WaitForExit(180 * 1000)) {
        # The console exe starts the real one as a child, so the whole tree has to go.
        & taskkill /T /F /PID $p.Id *> $null
        Write-Host 'FAIL: screenshot run timed out'; exit 1
    }
    $errors = Select-String -Path $shotLog, "$shotLog.err" -Pattern 'ERROR|SCRIPT ERROR|Unhandled exception'
    if ($errors) { $errors | Select-Object -First 20 | ForEach-Object { $_.Line } }
    if ($p.ExitCode -ne 0 -or $errors -or -not (Test-Path $shotPath)) {
        Write-Host "FAIL: screenshot run (exit $($p.ExitCode))"; exit 1
    }
    Write-Host "screenshot ok: $shotPath"
}

Write-Host 'ALL OK'
exit 0
