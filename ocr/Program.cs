using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Yorehold.Ocr;

/// <summary>
/// yorehold-ocr page1.jpg page2.png ...: one line of JSON per picture, in the order given,
/// {"width", "height", "lines": [{"text", "x", "y", "w", "h"}]} in the picture's pixels from its
/// top left. A picture it can't read gives {"error": "..."} and the rest still go on.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: yorehold-ocr <picture> [<picture> ...]");
            return 2;
        }
        OcrEngine? engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            Console.Error.WriteLine("Windows has no text recognition language installed (Settings > Time & language > Language).");
            return 3;
        }
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        foreach (string path in args)
        {
            string line;
            try
            {
                line = await ReadAsync(engine, Path.GetFullPath(path));
            }
            catch (Exception error)
            {
                line = JsonSerializer.Serialize(new { error = error.Message });
            }
            Console.Out.WriteLine(line);
        }
        return 0;
    }

    private static async Task<string> ReadAsync(OcrEngine engine, string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        uint width = decoder.PixelWidth, height = decoder.PixelHeight;
        // the engine refuses pictures past its limit; a smaller copy is read and placed back at full size
        double scale = Math.Min(1.0, (double)OcrEngine.MaxImageDimension / Math.Max(width, height));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, width * scale),
            ScaledHeight = (uint)Math.Max(1, height * scale),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        OcrResult result = await engine.RecognizeAsync(bitmap);
        var lines = new List<object>();
        foreach (OcrLine read in result.Lines)
        {
            double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
            foreach (OcrWord word in read.Words)
            {
                left = Math.Min(left, word.BoundingRect.X);
                top = Math.Min(top, word.BoundingRect.Y);
                right = Math.Max(right, word.BoundingRect.X + word.BoundingRect.Width);
                bottom = Math.Max(bottom, word.BoundingRect.Y + word.BoundingRect.Height);
            }
            if (read.Words.Count == 0)
            {
                continue;
            }
            lines.Add(new
            {
                text = read.Text,
                x = Math.Round(left / scale, 1),
                y = Math.Round(top / scale, 1),
                w = Math.Round((right - left) / scale, 1),
                h = Math.Round((bottom - top) / scale, 1),
            });
        }
        return JsonSerializer.Serialize(new { width, height, lines });
    }
}
