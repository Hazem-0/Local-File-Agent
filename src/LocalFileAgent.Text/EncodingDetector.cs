using System;
using System.Text;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Text;

public sealed class EncodingDetector : IEncodingDetector
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding Windows1256;
    private static readonly Encoding Iso8859_6;

    static EncodingDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1256 = Encoding.GetEncoding(1256);
        Iso8859_6 = Encoding.GetEncoding("iso-8859-6");
    }

    public DetectedText Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return new DetectedText(string.Empty, "utf-8", 1.0f);
        }

        // 1. Check for standard Byte Order Marks (BOM)
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            var text = Encoding.UTF8.GetString(bytes[3..]);
            return new DetectedText(text, "utf-8", 1.0f);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            var text = Encoding.Unicode.GetString(bytes[2..]);
            return new DetectedText(text, "utf-16le", 1.0f);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            var text = Encoding.BigEndianUnicode.GetString(bytes[2..]);
            return new DetectedText(text, "utf-16be", 1.0f);
        }

        // 2. Strict UTF-8 validation
        try
        {
            var utf8String = StrictUtf8.GetString(bytes);
            // Verify that this is not pure binary masquerading as UTF-8
            var controlRatio = CalculateControlRatio(utf8String);
            if (controlRatio < 0.05f)
            {
                var arabicRatio = CalculateArabicRatio(utf8String);
                var confidence = arabicRatio > 0.1f ? 0.98f : 0.90f;
                return new DetectedText(utf8String, "utf-8", confidence);
            }
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8, proceed to legacy and heuristic detection
        }

        // 3. UTF-16 heuristic (without BOM)
        if (bytes.Length >= 4 && LooksLikeUtf16Le(bytes))
        {
            var utf16String = Encoding.Unicode.GetString(bytes);
            if (CalculateControlRatio(utf16String) < 0.05f)
            {
                return new DetectedText(utf16String, "utf-16le", 0.85f);
            }
        }

        // 4. Test Arabic legacy encodings: Windows-1256 (CP1256) vs ISO-8859-6
        var cp1256Candidate = Windows1256.GetString(bytes);
        var cp1256ArabicRatio = CalculateArabicRatio(cp1256Candidate);
        var cp1256ControlRatio = CalculateControlRatio(cp1256Candidate);

        var isoCandidate = Iso8859_6.GetString(bytes);
        var isoArabicRatio = CalculateArabicRatio(isoCandidate);
        var isoControlRatio = CalculateControlRatio(isoCandidate);

        var isValidIsoBytes = IsValidIso8859_6ByteSequence(bytes);

        if (cp1256ArabicRatio > 0.05f || isoArabicRatio > 0.05f)
        {
            var cp1256Score = CalculateArabicCoherenceScore(cp1256Candidate);
            var isoScore = isValidIsoBytes ? CalculateArabicCoherenceScore(isoCandidate) : -1;

            if (isoScore > cp1256Score && isoControlRatio < 0.05f)
            {
                var confidence = Math.Clamp(0.75f + isoArabicRatio * 0.2f, 0.75f, 0.98f);
                return new DetectedText(isoCandidate, "iso-8859-6", confidence);
            }

            if (cp1256ControlRatio < 0.05f)
            {
                var confidence = Math.Clamp(0.75f + cp1256ArabicRatio * 0.2f, 0.75f, 0.98f);
                return new DetectedText(cp1256Candidate, "windows-1256", confidence);
            }
        }

        // 5. Latin-1 / UTF-8 fallback
        var fallbackText = Encoding.UTF8.GetString(bytes);
        return new DetectedText(fallbackText, "utf-8", 0.50f);
    }

    private static bool LooksLikeUtf16Le(ReadOnlySpan<byte> bytes)
    {
        var zeroCount = 0;
        var sampleSize = Math.Min(bytes.Length, 512);
        for (var i = 1; i < sampleSize; i += 2)
        {
            if (bytes[i] == 0x00)
            {
                zeroCount++;
            }
        }
        return (float)zeroCount / (sampleSize / 2) > 0.40f;
    }

    private static float CalculateArabicRatio(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        var arabicCount = 0;
        var letterCount = 0;

        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                letterCount++;
                // Arabic unicode blocks: U+0600..U+06FF, U+0750..U+077F, U+08A0..U+08FF, U+FB50..U+FDFF, U+FE70..U+FEFF
                if ((ch >= '\u0600' && ch <= '\u06FF') ||
                    (ch >= '\u0750' && ch <= '\u077F') ||
                    (ch >= '\u08A0' && ch <= '\u08FF') ||
                    (ch >= '\uFB50' && ch <= '\uFDFF') ||
                    (ch >= '\uFE70' && ch <= '\uFEFF'))
                {
                    arabicCount++;
                }
            }
        }

        return letterCount == 0 ? 0f : (float)arabicCount / letterCount;
    }

    private static bool IsValidIso8859_6ByteSequence(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            // Undefined in ISO-8859-6: 0x80..0xA0, 0xA1..0xBA, 0xBC..0xC0, 0xDB..0xDF, 0xF3..0xFF
            if ((b >= 0x80 && b <= 0xA0) ||
                (b >= 0xA1 && b <= 0xBA) ||
                (b >= 0xBC && b <= 0xC0) ||
                (b >= 0xDB && b <= 0xDF) ||
                (b >= 0xF3 && b <= 0xFF))
            {
                return false;
            }
        }
        return true;
    }

    private static int CalculateArabicCoherenceScore(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // Check for highly frequent Arabic functional bigrams and particles
        var score = 0;
        var frequentBigrams = new[] { "ال", "في", "من", "عل", "ان", "ات", "ين", "ون", "ية", "ما", "لا", "وا" };
        foreach (var bigram in frequentBigrams)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(bigram, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += bigram.Length;
            }
            score += count;
        }

        return score;
    }

    private static float CalculateControlRatio(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0f;
        }

        var controlCount = 0;
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
            {
                controlCount++;
            }
        }

        return (float)controlCount / text.Length;
    }
}
