namespace Yorehold.Rules;

/// <summary>
/// A recording as the speech model listens to it: one channel at 16 kHz. The file itself is left
/// as it is; it is what the game plays. WAV only for now (PCM 8, 16, 24 or 32 bit, or float).
/// </summary>
public static class VoiceAudio
{
    public const int SampleRate = 16000;

    /// <summary>Mixed to mono and resampled to 16 kHz floats; null (and why) for anything else.</summary>
    public static float[]? Decode(byte[] bytes, out string error)
    {
        error = "";
        float[]? Fail(string why, out string message)
        {
            message = why;
            return null;
        }
        if (bytes.Length < 12 || Tag(bytes, 0) != "RIFF" || Tag(bytes, 8) != "WAVE")
        {
            return Fail("not a recording that can be read here; only WAV is for now", out error);
        }
        int format = 0, channels = 0, rate = 0, bits = 0;
        int dataAt = -1, dataLength = 0;
        for (int at = 12; at + 8 <= bytes.Length;)
        {
            string tag = Tag(bytes, at);
            int length = (int)Math.Min(BitConverter.ToUInt32(bytes, at + 4), (uint)(bytes.Length - at - 8));
            if (tag == "fmt " && length >= 16)
            {
                format = BitConverter.ToUInt16(bytes, at + 8);
                channels = BitConverter.ToUInt16(bytes, at + 10);
                rate = (int)BitConverter.ToUInt32(bytes, at + 12);
                bits = BitConverter.ToUInt16(bytes, at + 22);
                // WAVE_FORMAT_EXTENSIBLE says what it is in its sub-format
                if (format == 0xFFFE && length >= 26)
                {
                    format = BitConverter.ToUInt16(bytes, at + 32);
                }
            }
            else if (tag == "data")
            {
                dataAt = at + 8;
                dataLength = length;
            }
            at += 8 + length + (length & 1);
        }
        if (dataAt < 0 || channels < 1 || rate < 1000 || (format != 1 && format != 3) || (format == 1 && bits is not (8 or 16 or 24 or 32)) || (format == 3 && bits != 32))
        {
            return Fail("a WAV file this can't read: plain PCM or float samples only", out error);
        }
        int frameBytes = channels * bits / 8;
        int frames = dataLength / frameBytes;
        if (frames == 0)
        {
            return Fail("the recording is empty", out error);
        }
        // mixed to one channel
        var mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                sum += Sample(bytes, dataAt + f * frameBytes + c * bits / 8, format, bits);
            }
            mono[f] = sum / channels;
        }
        if (rate == SampleRate)
        {
            return mono;
        }
        // resampled by straight lines between the samples
        int count = (int)((long)frames * SampleRate / rate);
        var samples = new float[count];
        double stepBy = (double)rate / SampleRate;
        for (int i = 0; i < count; i++)
        {
            double at = i * stepBy;
            int whole = (int)at;
            float part = (float)(at - whole);
            float next = whole + 1 < frames ? mono[whole + 1] : mono[whole];
            samples[i] = mono[whole] + (next - mono[whole]) * part;
        }
        return samples;
    }

    private static string Tag(byte[] bytes, int at) => System.Text.Encoding.ASCII.GetString(bytes, at, 4);

    private static float Sample(byte[] bytes, int at, int format, int bits)
    {
        if (format == 3)
        {
            return BitConverter.ToSingle(bytes, at);
        }
        return bits switch
        {
            8 => (bytes[at] - 128) / 128f,
            16 => BitConverter.ToInt16(bytes, at) / 32768f,
            24 => ((bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16) << 8 >> 8) / 8388608f,
            _ => BitConverter.ToInt32(bytes, at) / 2147483648f,
        };
    }
}
