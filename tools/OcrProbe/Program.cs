using System;
using Windows.Media.Ocr;

namespace OcrProbe;

public static class Program
{
    public static int Main()
    {
        Console.WriteLine("=== Windows Media OCR Engine Probe ===");
        try
        {
            var languages = OcrEngine.AvailableRecognizerLanguages;
            Console.WriteLine($"AvailableRecognizerLanguages count: {languages.Count}");
            foreach (var lang in languages)
            {
                Console.WriteLine($" - {lang.LanguageTag} ({lang.DisplayName})");
            }

            Console.WriteLine($"MaxImageDimension: {OcrEngine.MaxImageDimension}");

            var arabicSupported = OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("ar-SA"));
            var englishSupported = OcrEngine.IsLanguageSupported(new Windows.Globalization.Language("en-US"));

            Console.WriteLine($"Arabic (ar-SA) supported: {arabicSupported}");
            Console.WriteLine($"English (en-US) supported: {englishSupported}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Probe error: {ex}");
            return 1;
        }
    }
}
