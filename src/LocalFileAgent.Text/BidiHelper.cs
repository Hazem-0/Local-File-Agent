using System;

namespace LocalFileAgent.Text;

public static class BidiHelper
{
    public const char LeftToRightIsolate = '\u2066';
    public const char RightToLeftIsolate = '\u2067';
    public const char FirstStrongIsolate = '\u2068';
    public const char PopDirectionalIsolate = '\u2069';

    public static string WrapLtrIsolate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return $"{LeftToRightIsolate}{text}{PopDirectionalIsolate}";
    }
}
