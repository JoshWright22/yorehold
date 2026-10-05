# Speaks the lines in lines.txt with each English voice Windows has, into 16 kHz WAV files, and
# writes where the synthesizer says each word starts beside them (<name>.answer.json). These are the
# known answers yorehold-voice measures the speech models against:
#   powershell -NoProfile -ExecutionPolicy Bypass -File make-lines.ps1 -Out D:\_Projects\Dev\Yorehold\.dev\voice-lines
#   yorehold-voice measure D:\_Projects\Dev\Yorehold\.dev\voice-lines --model ggml-tiny.en-q5_1.bin --model ggml-base.en-q5_1.bin
# The files are made fresh each time and not kept in git: they depend on the voices installed.

param(
    [Parameter(Mandatory = $true)][string]$Out
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
Add-Type -ReferencedAssemblies System.Speech -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading;

public static class LineSpeaker
{
    // Each word the synthesizer reports: where it is in the text, and when it starts in the audio.
    public static List<double[]> Speak(string voice, string text, string path)
    {
        var marks = new List<double[]>();
        using (var synth = new SpeechSynthesizer())
        {
            synth.SelectVoice(voice);
            synth.SetOutputToWaveFile(path, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
            synth.SpeakProgress += (sender, e) => { lock (marks) marks.Add(new double[] { e.CharacterPosition, e.AudioPosition.TotalSeconds }); };
            synth.Speak(text);
            synth.SetOutputToNull();
        }
        // The last events can come in just after Speak returns.
        Thread.Sleep(200);
        lock (marks) return new List<double[]>(marks);
    }

    public static List<string> Voices()
    {
        var names = new List<string>();
        using (var synth = new SpeechSynthesizer())
            foreach (var v in synth.GetInstalledVoices())
                if (v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("en"))
                    names.Add(v.VoiceInfo.Name);
        return names;
    }
}
'@

New-Item -ItemType Directory -Force $Out | Out-Null
$lines = Get-Content (Join-Path $PSScriptRoot 'lines.txt') | Where-Object { $_.Trim() -and -not $_.StartsWith('#') }
Copy-Item (Join-Path $PSScriptRoot 'vocabulary.txt') (Join-Path $Out 'vocabulary.txt')
$voices = [LineSpeaker]::Voices()
if ($voices.Count -eq 0) { Write-Host 'No English voices installed.'; exit 1 }

foreach ($voice in $voices) {
    $short = ($voice -split ' ')[1].ToLower()
    for ($n = 0; $n -lt $lines.Count; $n++) {
        $text = $lines[$n].Trim()
        $name = '{0}-{1:00}' -f $short, ($n + 1)
        $marks = [LineSpeaker]::Speak($voice, $text, (Join-Path $Out "$name.wav"))
        # The written words, split on spaces like the game splits them, each with its first mark.
        $words = @()
        foreach ($m in [regex]::Matches($text, '\S+')) {
            $start = -1
            foreach ($mark in $marks) {
                if ($mark[0] -ge $m.Index -and $mark[0] -lt $m.Index + $m.Length) { $start = [math]::Round($mark[1], 3); break }
            }
            $words += [ordered]@{ text = $m.Value; start = $start }
        }
        [ordered]@{ voice = $voice; text = $text; words = $words } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Out "$name.answer.json") -Encoding UTF8
    }
    Write-Host "$voice : $($lines.Count) lines"
}
