using System;

namespace MarkdownPad.Helpers;

public readonly record struct TextStatistics(int Words, int Characters, int Lines, int ReadingMinutes);

public static class TextStats
{
    /// <summary>
    /// Counts words for mixed Chinese/Latin text: a CJK ideograph counts as one
    /// word, runs of Latin letters/digits count as one word each.
    /// </summary>
    public static TextStatistics Measure(string? text)
    {
        text ??= string.Empty;

        var words = 0;
        var characters = text.Length;
        var lines = 1;
        var inWord = false;

        foreach (var c in text)
        {
            if (c == '\n')
            {
                lines++;
                inWord = false;
                continue;
            }

            if (IsCjk(c))
            {
                words++;
                inWord = false;
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }

                continue;
            }

            inWord = false;
        }

        // ~300 CJK / 200 Latin words per minute, good enough for an estimate.
        var minutes = Math.Max(1, (int)Math.Ceiling(words / 250.0));
        return new TextStatistics(words, characters, lines, minutes);
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||     // CJK unified ideographs
        (c >= 0x3400 && c <= 0x4DBF) ||     // extension A
        (c >= 0xF900 && c <= 0xFAFF) ||     // compatibility ideographs
        (c >= 0x3040 && c <= 0x30FF) ||     // kana
        (c >= 0xAC00 && c <= 0xD7AF);       // hangul syllables
}
