# Yorehold (Godot) exports. Never opens a window and never waits for a key.
#   .\export.ps1 windows          ..\.dev\export\windows\Yorehold.exe, then a headless start of it
#   .\export.ps1 windows -Shot a.png   also a screenshot run of the exported exe (..\.dev\a.png)
#   .\export.ps1 android          ..\.dev\export\android\Yorehold.apk, signed with the release keystore
#   .\export.ps1 ios              the iOS export, which needs a Mac (docs/EXPORT.md)
# Add -DebugBuild for a debug build. Exit code 0 and ALL OK = all good. Logs: ..\.dev\godot-export-*.log.
# docs/EXPORT.md says what each one needs.

param(
    [Parameter(Mandatory = $true)][ValidateSet('windows', 'android', 'ios')][string]$Platform,
    [switch]$DebugBuild,
    [string]$Shot,
    [int]$Frames = 300,
    [string]$Screen = 'play'
)

$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$dev = Join-Path (Split-Path $root -Parent) '.dev'
$godot = 'D:\Downloads\Godot_v4.6.1-stable_mono_win64\Godot_v4.6.1-stable_mono_win64_console.exe'
if (-not (Test-Path $godot)) { Write-Host "FAIL: Godot not found at $godot"; exit 2 }
$templates = Join-Path $env:APPDATA 'Godot\export_templates\4.6.1.stable.mono'
if (-not (Test-Path (Join-Path $templates 'version.txt'))) {
    Write-Host "FAIL: no export templates in $templates (docs/EXPORT.md says how to get them)"; exit 2
}

$preset = @{ windows = 'Windows Desktop'; android = 'Android'; ios = 'iOS' }[$Platform]
$file = @{ windows = 'Yorehold.exe'; android = 'Yorehold.apk'; ios = 'Yorehold.ipa' }[$Platform]
$outDir = Join-Path $dev "export\$Platform"
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir $file

if ($Platform -eq 'android' -and -not $DebugBuild) {
    # The release keystore lives in export\keystore, which git ignores. Made once here; keep a copy
    # of the folder somewhere safe, since an app signed with a lost key can't be updated.
    $keys = Join-Path $root 'export\keystore'
    $store = Join-Path $keys 'yorehold-release.keystore'
    $passFile = Join-Path $keys 'password.txt'
    if (-not (Test-Path $store)) {
        New-Item -ItemType Directory -Force $keys | Out-Null
        # Godot scans the project's folders; this keeps it out of export\
        New-Item -ItemType File -Force (Join-Path $root 'export\.gdignore') | Out-Null
        $pass = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
        Set-Content -Path $passFile -Value $pass -NoNewline
        & keytool -genkeypair -v -keystore $store -alias yorehold -keyalg RSA -keysize 2048 -validity 10000 `
            -storepass $pass -keypass $pass -dname 'CN=Yorehold' *> (Join-Path $dev 'godot-keytool.log')
        if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL: keytool (see ..\.dev\godot-keytool.log)'; exit 1 }
        Write-Host "made the release keystore: $store"
    }
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_PATH = $store
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_USER = 'yorehold'
    $env:GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD = (Get-Content $passFile -Raw).Trim()
}

$mode = if ($DebugBuild) { '--export-debug' } else { '--export-release' }
$log = Join-Path $dev "godot-export-$Platform.log"
Remove-Item $out -ErrorAction SilentlyContinue
$job = Start-Job { param($g, $p, $m, $n, $o) & $g --headless --path $p $m $n $o 2>&1 } -ArgumentList $godot, $root, $mode, $preset, $out
if (-not (Wait-Job $job -Timeout 900)) { Stop-Job $job; Write-Host 'FAIL: export timed out'; exit 1 }
Receive-Job $job 2>&1 | ForEach-Object { "$_" -replace '\x1b\[[0-9;]*m', '' } | Out-File $log -Encoding utf8
Remove-Job $job
# The editor always prints this one on its way out after a headless export; it means nothing.
$errors = Select-String -Path $log -Pattern 'ERROR|error MSB|error CS' | Where-Object { $_.Line -notmatch 'EditorSettings not instantiated' }
if ($errors -or -not (Test-Path $out)) {
    $errors | Select-Object -First 20 | ForEach-Object { $_.Line }
    Write-Host "FAIL: export ($Platform)"; exit 1
}
Write-Host "export ok: $out"

if ($Platform -eq 'windows') {
    # The console wrapper prints the game's output, the plain exe doesn't.
    $console = Join-Path $outDir 'Yorehold.console.exe'
    $runLog = Join-Path $dev 'godot-export-run.log'
    $job = Start-Job { param($e) & $e --headless --quit-after 30 -- --screen play 2>&1 } -ArgumentList $console
    if (-not (Wait-Job $job -Timeout 120)) { Stop-Job $job; Write-Host 'FAIL: exported game timed out'; exit 1 }
    Receive-Job $job 2>&1 | ForEach-Object { "$_" } | Out-File $runLog -Encoding utf8
    Remove-Job $job
    $errors = Select-String -Path $runLog -Pattern 'ERROR|SCRIPT ERROR|Unhandled exception'
    if ($errors -or -not (Select-String -Path $runLog -Pattern 'Unpacked \d+ content files' -Quiet)) {
        $errors | Select-Object -First 20 | ForEach-Object { $_.Line }
        Write-Host 'FAIL: exported game headless start'; exit 1
    }
    Write-Host 'exported game starts ok'

    if ($Shot) {
        $shotPath = if ([IO.Path]::IsPathRooted($Shot)) { $Shot } else { Join-Path $dev $Shot }
        Remove-Item $shotPath -ErrorAction SilentlyContinue
        $arguments = @('--windowed', '--position', '-4000,-4000', '--resolution', '1280x720', '--fixed-fps', '60',
            '--', '--shot', "`"$shotPath`"", '--frames', "$Frames", '--screen', $Screen)
        $shotLog = Join-Path $dev 'godot-export-shot.log'
        $p = Start-Process $console -ArgumentList $arguments -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput $shotLog -RedirectStandardError "$shotLog.err"
        $null = $p.Handle
        if (-not $p.WaitForExit(180 * 1000)) {
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
}

Write-Host 'ALL OK'
exit 0
