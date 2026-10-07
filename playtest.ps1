# Playtest: opens the game on each test in playtest\queue.json in turn, already set up, with a
# window beside it saying what to try. Good, Problem (with a note and a picture) or Skip goes to
# feedback\playtest-results.jsonl next to this repo, then the next test opens.
#
#   playtest.ps1              every test without an answer yet, in order
#   playtest.ps1 -From fight  starts at that test, answered or not
#   playtest.ps1 -Again       every test, answered or not
#   playtest.ps1 -Problems    only the tests whose last answer was a problem (to check fixes)
#
# Start this test again reopens it from the start; Stop (or closing the window) ends the run.
# Each test starts from nothing: its own characters, saves and settings, none of yours.
param(
    [string]$From,
    [switch]$Again,
    [switch]$Problems
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$queueFile = Join-Path $root 'playtest\queue.json'
$feedback = Join-Path (Split-Path $root -Parent) 'feedback'
$resultsFile = Join-Path $feedback 'playtest-results.jsonl'
$godot = 'D:\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64.exe'
if (-not (Test-Path $godot)) { Write-Host "Godot not found at $godot"; exit 2 }

# the game has to be built as it is now
dotnet build (Join-Path $root 'Yorehold.slnx') -nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'The game does not build; run check.ps1 to see why.'; exit 1 }

$items = @((Get-Content $queueFile -Raw | ConvertFrom-Json).items)
$last = @{}
if (Test-Path $resultsFile) {
    foreach ($line in Get-Content $resultsFile) {
        if ($line.Trim()) { $answer = $line | ConvertFrom-Json; $last[$answer.id] = $answer.verdict }
    }
}

$start = 0
if ($From) {
    $start = [Array]::FindIndex([object[]]$items, [Predicate[object]] { param($i) $i.id -eq $From })
    if ($start -lt 0) { Write-Host "No test called $From in the queue."; exit 2 }
}

$index = $start
while ($index -lt $items.Count) {
    $item = $items[$index]
    $wanted = if ($Problems) { $last[$item.id] -eq 'problem' }
              elseif ($Again -or $From) { $true }
              else { -not $last.ContainsKey($item.id) }
    if (-not $wanted) { $index++; continue }

    Write-Host "Test $($index + 1) of $($items.Count): $($item.name)"
    $arguments = @('--path', "`"$root`"", '--resolution', '1280x720', '--fixed-fps', '60', '--',
        '--playtest', $item.id, '--queue', "`"$queueFile`"", '--results', "`"$resultsFile`"",
        '--number', "`"$($index + 1) of $($items.Count)`"",
        '--screen', $(if ($item.screen) { $item.screen } else { 'play' }))
    if ($item.chapter) { $arguments += @('--chapter', $item.chapter) }
    if ($item.script) { $arguments += @('--script', "`"$(Join-Path $root $item.script)`"") }
    if ($item.until) { $arguments += @('--until', "$($item.until)") }
    $game = Start-Process $godot -ArgumentList $arguments -PassThru -Wait
    switch ($game.ExitCode) {
        3 { continue }                                    # start this test again
        4 { Write-Host 'Stopped.'; exit 0 }
        default { $index++ }
    }
}
Write-Host "No more tests. Answers are in $resultsFile"
