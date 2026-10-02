using System;
using System.Collections.Generic;
using System.Text;
using LocalFileAgent.Domain.Text;

namespace LocalFileAgent.Text;

public sealed class ArabicTextNormalizer : ITextNormalizer
{
    public NormalizedText Normalize(string text, NormalizationProfile profile)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new NormalizedText(text ?? string.Empty, string.Empty, Array.Empty<int>());
        }

        if (profile == NormalizationProfile.Display)
        {
            // Display profile: perform only safe sanitization of dangerous control characters, preserving original typography
            return NormalizeDisplay(text);
        }

        return NormalizeSearch(text);
    }

    private static NormalizedText NormalizeDisplay(string text)
    {
        var sb = new StringBuilder(text.Length);
        var offsetMap = new List<int>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            // Strip invisible or rogue formatting characters that corrupt layout
            if (IsInvisibleOrBidiControl(ch) && ch != '\n' && ch != '\r' && ch != '\t')
            {
                continue;
            }

            sb.Append(ch);
            offsetMap.Add(i);
        }

        return new NormalizedText(text, sb.ToString(), offsetMap);
    }

    private static NormalizedText NormalizeSearch(string text)
    {
        // Step 1: Unicode NFKC decomposition/composition to resolve ligatures & presentation forms
        // In order to accurately map offsets back to the raw input, we inspect characters
        // and expand presentation forms while tracking original indices.
        var sb = new StringBuilder(text.Length);
        var offsetMap = new List<int>(text.Length);
        var inWhitespace = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            // 1. Remove invisible / bidi control codes
            if (IsInvisibleOrBidiControl(ch))
            {
                continue;
            }

            // 2. Remove Tashkeel (diacritics) and Tatweel (kashida)
            if (IsTashkeel(ch) || ch == '\u0640')
            {
                continue;
            }

            // 3. Handle whitespace collapsing
            if (char.IsWhiteSpace(ch))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    offsetMap.Add(i);
                    inWhitespace = true;
                }
                continue;
            }
            inWhitespace = false;

            // 4. Map presentation forms (NFKC equivalent decomposition)
            if (IsArabicPresentationForm(ch))
            {
                var nfkc = ch.ToString().Normalize(NormalizationForm.FormKC);
                foreach (var normChar in nfkc)
                {
                    if (IsTashkeel(normChar) || normChar == '\u0640')
                    {
                        continue;
                    }

                    var mapped = MapCharacter(normChar);
                    sb.Append(mapped);
                    offsetMap.Add(i);
                }
                continue;
            }

            // 5. Standard mapping for base Arabic letters, digits, punctuation, and Latin case-folding
            var mappedChar = MapCharacter(ch);
            sb.Append(mappedChar);
            offsetMap.Add(i);
        }

        var normalizedString = sb.ToString().Trim();
        // If trimming occurred at ends, adjust offset map accordingly
        var startIndex = 0;
        while (startIndex < sb.Length && char.IsWhiteSpace(sb[startIndex]))
        {
            startIndex++;
        }

        var endIndex = sb.Length;
        while (endIndex > startIndex && char.IsWhiteSpace(sb[endIndex - 1]))
        {
            endIndex--;
        }

        if (startIndex > 0 || endIndex < sb.Length)
        {
            var count = endIndex - startIndex;
            var trimmedOffsetMap = new List<int>(count);
            for (var k = startIndex; k < endIndex; k++)
            {
                trimmedOffsetMap.Add(offsetMap[k]);
            }
            return new NormalizedText(text, normalizedString, trimmedOffsetMap);
        }

        return new NormalizedText(text, normalizedString, offsetMap);
    }

    private static char MapCharacter(char ch)
    {
        // Unify Alef variants: أ (U+0623), إ (U+0625), آ (U+0622), ٱ (U+0671) -> ا (U+0627)
        if (ch is '\u0622' or '\u0623' or '\u0625' or '\u0671')
        {
            return '\u0627';
        }

        // Unify Alef Maqsura and Farsi Yeh: ى (U+0649), ی (U+06CC) -> ي (U+064A)
        if (ch is '\u0649' or '\u06CC')
        {
            return '\u064A';
        }

        // Unify Persian Keheh: ک (U+06A9) -> ك (U+0643)
        if (ch == '\u06A9')
        {
            return '\u0643';
        }

        // Arabic-Indic Digits: ٠..٩ (U+0660..U+0669) -> 0..9
        if (ch is >= '\u0660' and <= '\u0669')
        {
            return (char)('0' + (ch - '\u0660'));
        }

        // Eastern Arabic / Persian Digits: ۰..۹ (U+06F0..U+06F9) -> 0..9
        if (ch is >= '\u06F0' and <= '\u06F9')
        {
            return (char)('0' + (ch - '\u06F0'));
        }

        // Arabic punctuation to standard equivalents
        if (ch == '\u060C') // Arabic Comma '،'
        {
            return ',';
        }
        if (ch == '\u061B') // Arabic Semicolon '؛'
        {
            return ';';
        }
        if (ch == '\u061F') // Arabic Question Mark '؟'
        {
            return '?';
        }

        // Latin case folding
        if (char.IsAsciiLetterUpper(ch))
        {
            return char.ToLowerInvariant(ch);
        }

        return ch;
    }

    private static bool IsTashkeel(char ch)
    {
        // Arabic Tashkeel: Fathatan, Dammatan, Kasratan, Fatha, Damma, Kasra, Shadda, Sukun, etc.
        // U+064B to U+065F, plus Superscript Alef U+0670, and Quranic marks U+06D6 to U+06ED
        return (ch >= '\u064B' && ch <= '\u065F') ||
               ch == '\u0670' ||
               (ch >= '\u06D6' && ch <= '\u06ED');
    }

    private static bool IsArabicPresentationForm(char ch)
    {
        // Arabic Presentation Forms-A (U+FB50..U+FDFF) and Presentation Forms-B (U+FE70..U+FEFF)
        return (ch >= '\uFB50' && ch <= '\uFDFF') ||
               (ch >= '\uFE70' && ch <= '\uFEFF');
    }

    private static bool IsInvisibleOrBidiControl(char ch)
    {
        return (ch >= '\u200B' && ch <= '\u200F') || // ZWSP, ZWNJ, ZWJ, LRM, RLM
               (ch >= '\u202A' && ch <= '\u202E') || // LRE, RLE, PDF, LRO, RLO
               (ch >= '\u2066' && ch <= '\u2069') || // LRI, RLI, FSI, PDI
               ch == '\uFEFF' ||                     // Byte Order Mark / ZWNBSP
               ch == '\u00AD';                       // Soft hyphen
    }
}
