using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MarkdownPad.Helpers;

public readonly record struct EditResult(string Text, int SelectionStart, int SelectionLength);

/// <summary>
/// Markdown aware text transformations used by the toolbar and keyboard
/// shortcuts. Everything is a pure function of (text, selection) so the
/// behaviour can be reasoned about (and tested) without a UI.
/// </summary>
public static class MarkdownEditor
{
    private const string BoldMarker = "**";
    private const string ItalicMarker = "*";
    private const string StrikeMarker = "~~";
    private const string CodeMarker = "`";

    public static EditResult ToggleBold(string text, int start, int length) => ToggleWrap(text, start, length, BoldMarker);

    public static EditResult ToggleItalic(string text, int start, int length) => ToggleWrap(text, start, length, ItalicMarker);

    public static EditResult ToggleStrike(string text, int start, int length) => ToggleWrap(text, start, length, StrikeMarker);

    public static EditResult ToggleInlineCode(string text, int start, int length) => ToggleWrap(text, start, length, CodeMarker);

    public static EditResult InsertLink(string text, int start, int length)
    {
        var selected = Slice(text, start, length);
        var label = string.IsNullOrEmpty(selected) ? "链接文字" : selected;
        var snippet = "[" + label + "](url)";
        var replaced = Replace(text, start, length, snippet);

        // Caret on the "url" placeholder: "[" + label + "](" then the url.
        var urlStart = start + label.Length + 3;
        return new EditResult(replaced, urlStart, 3);
    }

    public static EditResult InsertImage(string text, int start, int length)
    {
        var selected = Slice(text, start, length);
        var alt = string.IsNullOrEmpty(selected) ? "图片说明" : selected;
        var snippet = "![" + alt + "](image.png)";
        var replaced = Replace(text, start, length, snippet);
        var urlStart = start + alt.Length + 4;
        return new EditResult(replaced, urlStart, "image.png".Length);
    }

    /// <summary>Adds or removes a line prefix. Removing applies to the whole contiguous block.</summary>
    public static EditResult ToggleLinePrefix(string text, int start, int length, string prefix)
    {
        var (firstLineStart, selectionEnd) = LineBounds(text, start, length);
        var block = text.Substring(firstLineStart, selectionEnd - firstLineStart);
        var lines = SplitLines(block);

        // Only a selection that already carries the markers on every line removes
        // them. A caret is handled below, where the whole contiguous run matters.
        var removing = length > 0 && lines.Count > 0 && lines.All(line => HasPrefix(line, prefix));

        if (length == 0 && HasPrefix(FirstLine(text, firstLineStart), prefix))
        {
            // A caret inside prefixed lines toggles the whole contiguous run, which
            // is what users expect from toolbar buttons and keyboard shortcuts.
            removing = true;

            var blockStart = firstLineStart;
            while (blockStart > 0)
            {
                var previousStart = PreviousLineStart(text, blockStart);
                if (!HasPrefix(LineAt(text, previousStart), prefix))
                {
                    break;
                }

                blockStart = previousStart;
            }

            var blockEnd = selectionEnd;
            while (blockEnd < text.Length)
            {
                var nextStart = text[blockEnd] == '\n' ? blockEnd + 1 : blockEnd;
                var nextLine = LineAt(text, nextStart);
                if (!HasPrefix(nextLine, prefix))
                {
                    break;
                }

                blockEnd = Math.Min(text.Length, nextStart + nextLine.Length);
            }

            firstLineStart = blockStart;
            block = text.Substring(firstLineStart, blockEnd - firstLineStart);
            lines = SplitLines(block);
        }

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(removing ? RemovePrefix(line, prefix) : prefix + line);
        }

        var replacement = builder.ToString();
        var newText = Replace(text, firstLineStart, block.Length, replacement);

        // With a caret the result is always a caret: just after the inserted
        // prefix when adding, at the start of the line when removing.
        if (length == 0)
        {
            return new EditResult(newText, removing ? firstLineStart : firstLineStart + prefix.Length, 0);
        }

        // With a selection keep the rewritten block selected so the next toggle
        // still targets the same lines.
        return new EditResult(newText, firstLineStart, replacement.Length);
    }

    public static EditResult ToggleBulletList(string text, int start, int length) =>
        ToggleLinePrefix(text, start, length, "- ");

    public static EditResult ToggleTaskList(string text, int start, int length)
    {
        var (firstLineStart, _) = LineBounds(text, start, length);
        var line = FirstLine(text, firstLineStart);
        if (line.StartsWith("- [ ] ", StringComparison.Ordinal))
        {
            return ToggleLinePrefix(text, start, length, "- [ ] ");
        }

        if (line.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
        {
            return ToggleLinePrefix(text, start, length, "- [x] ");
        }

        return ToggleLinePrefix(text, start, length, "- [ ] ");
    }

    public static EditResult ToggleQuote(string text, int start, int length) =>
        ToggleLinePrefix(text, start, length, "> ");

    /// <summary>Applies a heading level to the current line. Level 0 removes the heading.</summary>
    public static EditResult SetHeading(string text, int start, int length, int level)
    {
        level = Math.Clamp(level, 0, 6);
        var (firstLineStart, selectionEnd) = LineBounds(text, start, length);
        var block = text.Substring(firstLineStart, selectionEnd - firstLineStart);
        var lines = SplitLines(block);

        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = StripHeading(lines[i]);
            if (i == 0 && level > 0)
            {
                line = new string('#', level) + " " + line;
            }

            builder.Append(line);
        }

        var replacement = builder.ToString();
        var newText = Replace(text, firstLineStart, selectionEnd - firstLineStart, replacement);
        return new EditResult(newText, firstLineStart + replacement.Length, 0);
    }

    /// <summary>Removes an existing ATX heading marker ("## " and up to three leading spaces).</summary>
    private static string StripHeading(string line)
    {
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ')
        {
            index++;
        }

        var hashes = 0;
        while (index + hashes < line.Length && hashes < 6 && line[index + hashes] == '#')
        {
            hashes++;
        }

        if (hashes == 0)
        {
            return line;
        }

        var after = index + hashes;
        if (after < line.Length && line[after] != ' ')
        {
            return line;
        }

        while (after < line.Length && line[after] == ' ')
        {
            after++;
        }

        return line.Substring(0, index) + line.Substring(after);
    }

    public static EditResult ToggleCodeBlock(string text, int start, int length)
    {
        var (firstLineStart, selectionEnd) = LineBounds(text, start, length);
        var block = text.Substring(firstLineStart, selectionEnd - firstLineStart);
        var fence = "```";

        if (block.TrimStart().StartsWith(fence, StringComparison.Ordinal) &&
            block.TrimEnd().EndsWith(fence, StringComparison.Ordinal))
        {
            var trimmed = block.Trim();
            var inner = trimmed.Substring(3, trimmed.Length - 6).Trim('\r', '\n');
            var newTextInner = Replace(text, firstLineStart, selectionEnd - firstLineStart, inner);
            return new EditResult(newTextInner, firstLineStart, inner.Length);
        }

        var replacement = fence + "\n" + block + "\n" + fence;
        var replaced = Replace(text, firstLineStart, selectionEnd - firstLineStart, replacement);
        var innerStart = firstLineStart + 4;
        return new EditResult(replaced, innerStart, block.Length);
    }

    public static EditResult InsertSnippet(string text, int start, int length, string snippet, int caretOffset, int caretLength = 0)
    {
        var replaced = Replace(text, start, length, snippet);
        var caret = Math.Clamp(start + caretOffset, 0, replaced.Length);
        var caretLen = Math.Clamp(caretLength, 0, replaced.Length - caret);
        return new EditResult(replaced, caret, caretLen);
    }

    public static string TableSnippet =>
        "| 列 1 | 列 2 | 列 3 |\n| --- | --- | --- |\n| 内容 | 内容 | 内容 |";

    public static string TaskSnippet => "- [ ] 待办事项";

    private static EditResult ToggleWrap(string text, int start, int length, string marker)
    {
        if (length == 0)
        {
            var snippet = marker + marker;
            var replaced = Replace(text, start, 0, snippet);
            return new EditResult(replaced, start + marker.Length, 0);
        }

        var selected = Slice(text, start, length);
        var markerLength = marker.Length;
        var before = start - markerLength;

        var alreadyWrapped =
            before >= 0 &&
            start + length + markerLength <= text.Length &&
            text.AsSpan(before, markerLength).SequenceEqual(marker) &&
            text.AsSpan(start + length, markerLength).SequenceEqual(marker);

        if (alreadyWrapped)
        {
            // Drop the surrounding markers, keep the inner text selected.
            var newText = Replace(text, before, markerLength, string.Empty);
            newText = Replace(newText, start + length - markerLength, markerLength, string.Empty);
            return new EditResult(newText, before, length);
        }

        // Reuse markers that are already directly inside the selection.
        if (selected.StartsWith(marker, StringComparison.Ordinal) &&
            selected.EndsWith(marker, StringComparison.Ordinal) &&
            selected.Length >= markerLength * 2)
        {
            var inner = selected.Substring(markerLength, selected.Length - (markerLength * 2));
            var newText = Replace(text, start, length, inner);
            return new EditResult(newText, start, inner.Length);
        }

        var wrapped = marker + selected + marker;
        var result = Replace(text, start, length, wrapped);
        return new EditResult(result, start + markerLength, length);
    }

    private static bool HasPrefix(string line, string prefix)
    {
        var trimmed = line.TrimStart();
        if (prefix == "- [ ] ")
        {
            return trimmed.StartsWith("- [ ] ", StringComparison.Ordinal) ||
                   trimmed.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase);
        }

        return trimmed.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string RemovePrefix(string line, string prefix)
    {
        var index = 0;
        while (index < line.Length && (line[index] == ' ' || line[index] == '\t'))
        {
            index++;
        }

        if (prefix == "- [ ] ")
        {
            if (line.AsSpan(index).StartsWith("- [ ] ", StringComparison.Ordinal))
            {
                return line.Remove(index, 6);
            }

            if (line.AsSpan(index).StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                return line.Remove(index, 6);
            }

            return line;
        }

        return line.AsSpan(index).StartsWith(prefix, StringComparison.Ordinal) ? line.Remove(index, prefix.Length) : line;
    }

    private static string FirstLine(string text, int lineStart)
    {
        var end = text.IndexOf('\n', lineStart);
        return end < 0 ? text.Substring(lineStart) : text.Substring(lineStart, end - lineStart);
    }

    private static int PreviousLineStart(string text, int lineStart)
    {
        var index = lineStart - 2;
        while (index > 0 && text[index] != '\n')
        {
            index--;
        }

        return Math.Max(0, index + 1);
    }

    private static string LineAt(string text, int lineStart)
    {
        if (lineStart >= text.Length)
        {
            return string.Empty;
        }

        var end = text.IndexOf('\n', lineStart);
        return end < 0 ? text.Substring(lineStart) : text.Substring(lineStart, end - lineStart);
    }

    private static (int Start, int End) LineBounds(string text, int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length);
        var end = Math.Clamp(start + length, 0, text.Length);

        var lineStart = start;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        var selectionEnd = end;

        // A selection that ends exactly on a line break does not include the next line.
        if (selectionEnd > lineStart && selectionEnd - 1 < text.Length && text[selectionEnd - 1] == '\n')
        {
            selectionEnd--;
        }

        // Prefer whole lines: extend to the end of the last touched line (including
        // its line break) so prefix toggles always rewrite complete lines.
        var lastLineEnd = selectionEnd;
        while (lastLineEnd < text.Length && text[lastLineEnd] != '\n')
        {
            lastLineEnd++;
        }

        if (lastLineEnd < text.Length)
        {
            lastLineEnd++;
        }

        return (lineStart, Math.Max(lastLineEnd, lineStart));
    }

    private static List<string> SplitLines(string block)
    {
        var lines = new List<string>();
        var builder = new StringBuilder();
        foreach (var c in block)
        {
            if (c == '\n')
            {
                builder.Append(c);
                lines.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        if (builder.Length > 0)
        {
            lines.Add(builder.ToString());
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        return lines;
    }

    private static string Slice(string text, int start, int length)
    {
        if (length <= 0 || start < 0 || start >= text.Length)
        {
            return string.Empty;
        }

        var count = Math.Min(length, text.Length - start);
        return text.Substring(start, count);
    }

    private static string Replace(string text, int start, int length, string replacement)
    {
        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);
        return string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));
    }
}
