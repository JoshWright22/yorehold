# Draws the default UI images in assets\ui from the colours below (the same chunky frames the
# game used to draw in code). Run it again after changing a colour; skins replace these files.
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\assets\ui')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null

$border = 8, 7, 10, 255
$panel = 34, 30, 40, 240
$button = 82, 68, 102, 255
$hover = 104, 88, 128, 255
$pressed = 64, 52, 80, 255
$disabled = 44, 40, 50, 255
$accent = 255, 196, 64, 255
$text = 242, 234, 216, 255

function Color($c, $shift = 0) {
    $v = $c[0..2] | ForEach-Object { [Math]::Max(0, [Math]::Min(255, $_ + $shift)) }
    [System.Drawing.Color]::FromArgb($c[3], $v[0], $v[1], $v[2])
}

function Fill($bitmap, $x, $y, $w, $h, $color) {
    for ($j = $y; $j -lt $y + $h; $j++) { for ($i = $x; $i -lt $x + $w; $i++) { $bitmap.SetPixel($i, $j, $color) } }
}

function Save($bitmap, $name) {
    $bitmap.Save((Join-Path $Out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

# An outlined box with a light edge top-left and a dark one bottom-right (swapped when sunken).
function Frame($name, $fill, $size = 24, $outline = 3, $bevel = 2, [switch]$Sunken, $edge = $border) {
    $b = New-Object System.Drawing.Bitmap $size, $size
    Fill $b 0 0 $size $size (Color $edge)
    $inner = $size - 2 * $outline
    Fill $b $outline $outline $inner $inner (Color $fill)
    if ($bevel -gt 0) {
        $light = Color $fill $(if ($Sunken) { -28 } else { 34 })
        $dark = Color $fill $(if ($Sunken) { 34 } else { -28 })
        Fill $b $outline $outline $inner $bevel $light
        Fill $b $outline ($outline + $bevel) $bevel ($inner - $bevel) $light
        Fill $b $outline ($outline + $inner - $bevel) $inner $bevel $dark
        Fill $b ($outline + $inner - $bevel) $outline $bevel ($inner - $bevel) $dark
    }
    if ($name) { Save $b $name } else { $b }
}

Frame 'panel' $panel
Frame 'button' $button
Frame 'button-hover' $hover
Frame 'button-pressed' $pressed -Sunken
Frame 'button-disabled' $disabled
Frame 'textbox' $disabled -outline 1 -bevel 0
Frame 'textbox-focus' $disabled -outline 1 -bevel 0 -edge $accent
Frame 'bar-back' $disabled -size 12 -bevel 0

# The ring over a toggle that is on: an accent outline with nothing inside.
$ring = New-Object System.Drawing.Bitmap 24, 24
Fill $ring 0 0 24 24 (Color $accent)
Fill $ring 3 3 18 18 ([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
Save $ring 'button-selected'

Frame 'checkbox-off' $disabled -size 20 -Sunken
$on = Frame $null $disabled -size 20 -Sunken
Fill $on 6 6 8 8 (Color $accent)
Save $on 'checkbox-on'

# Tinted with the bar's colour: a bright strip along the top, the rest slightly darker.
$fill = New-Object System.Drawing.Bitmap 8, 10
Fill $fill 0 0 8 3 ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
Fill $fill 0 3 8 7 ([System.Drawing.Color]::FromArgb(255, 214, 214, 214))
Save $fill 'bar-fill'

$knob = New-Object System.Drawing.Bitmap 8, 18
Fill $knob 0 0 8 18 (Color $border)
Fill $knob 1 1 6 16 (Color $text)
Save $knob 'slider-knob'

Get-ChildItem $Out -Filter *.png | ForEach-Object { "$($_.Name)  $($_.Length) bytes" }
