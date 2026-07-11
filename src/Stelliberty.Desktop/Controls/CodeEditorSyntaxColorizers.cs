using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Stelliberty.Desktop.Controls;

internal sealed class SyntaxPalette
{
    public required IBrush Key { get; init; }
    public required IBrush Marker { get; init; }
    public required IBrush Function { get; init; }
    public required IBrush Boolean { get; init; }
    public required IBrush Number { get; init; }
    public required IBrush String { get; init; }
    public required IBrush Comment { get; init; }
}

internal abstract class CodeEditorSyntaxColorizer : DocumentColorizingTransformer
{
    protected SyntaxPalette Palette { get; private set; } = null!;

    public void UpdatePalette(SyntaxPalette palette) => Palette = palette;

    protected void HighlightMatches(
        DocumentLine line,
        string text,
        int start,
        int end,
        Regex regex,
        IBrush brush,
        FontWeight? weight = null,
        FontStyle? style = null)
    {
        foreach (Match match in regex.Matches(text, start))
        {
            if (!match.Success || match.Index + match.Length > end)
            {
                continue;
            }

            ColorizeRange(line, match.Index, match.Index + match.Length, brush, weight, style);
        }
    }

    protected void ColorizeRange(
        DocumentLine line,
        int start,
        int end,
        IBrush brush,
        FontWeight? weight = null,
        FontStyle? style = null)
    {
        if (end <= start)
        {
            return;
        }

        ChangeLinePart(line.Offset + start, line.Offset + end, element =>
        {
            element.TextRunProperties.SetForegroundBrush(brush);
            if (weight is null && style is null)
            {
                return;
            }

            var typeface = element.TextRunProperties.Typeface;
            element.TextRunProperties.SetTypeface(new Typeface(
                typeface.FontFamily,
                style ?? typeface.Style,
                weight ?? typeface.Weight,
                typeface.Stretch));
        });
    }

    protected static bool IsEscaped(string text, int index)
    {
        var slashCount = 0;
        for (var current = index - 1; current >= 0 && text[current] == '\\'; current--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }
}

internal sealed class YamlSyntaxColorizer : CodeEditorSyntaxColorizer
{
    private static readonly Regex BooleanRegex = new(
        @"\b(?:true|false|yes|no|on|off|null|~)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex NumberRegex = new(
        @"(?<![\w.])[-+]?\d+(?:_\d+)*(?:\.\d+(?:_\d+)*)?(?:[eE][-+]?\d+)?(?![\w.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StringRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:''|[^'])*'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line.Offset, line.Length);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var commentStart = FindCommentStart(text);
        var contentEnd = commentStart >= 0 ? commentStart : text.Length;
        var valueStart = HighlightListMarker(line, text, contentEnd);
        var colonIndex = FindMappingColon(text, valueStart, contentEnd);
        if (colonIndex >= 0)
        {
            var keyEnd = colonIndex;
            while (keyEnd > valueStart && char.IsWhiteSpace(text[keyEnd - 1]))
            {
                keyEnd--;
            }

            if (keyEnd > valueStart)
            {
                ColorizeRange(line, valueStart, keyEnd, Palette.Key, FontWeight.SemiBold);
            }

            HighlightScalars(line, text, colonIndex + 1, contentEnd);
        }
        else
        {
            HighlightScalars(line, text, valueStart, contentEnd);
        }

        if (commentStart >= 0)
        {
            ColorizeRange(line, commentStart, text.Length, Palette.Comment, style: FontStyle.Italic);
        }
    }

    private int HighlightListMarker(DocumentLine line, string text, int contentEnd)
    {
        var markerIndex = 0;
        while (markerIndex < contentEnd && char.IsWhiteSpace(text[markerIndex]))
        {
            markerIndex++;
        }

        if (markerIndex < contentEnd
            && text[markerIndex] == '-'
            && (markerIndex + 1 == contentEnd || char.IsWhiteSpace(text[markerIndex + 1])))
        {
            ColorizeRange(line, markerIndex, markerIndex + 1, Palette.Marker, FontWeight.SemiBold);
            markerIndex++;
            while (markerIndex < contentEnd && char.IsWhiteSpace(text[markerIndex]))
            {
                markerIndex++;
            }
        }

        return markerIndex;
    }

    private void HighlightScalars(DocumentLine line, string text, int start, int end)
    {
        if (start >= end)
        {
            return;
        }

        HighlightMatches(line, text, start, end, BooleanRegex, Palette.Boolean, FontWeight.SemiBold);
        HighlightMatches(line, text, start, end, NumberRegex, Palette.Number);
        HighlightMatches(line, text, start, end, StringRegex, Palette.String);
    }

    private static int FindCommentStart(string text)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote && !IsEscaped(text, index))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (current == '#'
                && !inSingleQuote
                && !inDoubleQuote
                && (index == 0 || char.IsWhiteSpace(text[index - 1])))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindMappingColon(string text, int start, int end)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var bracketDepth = 0;
        var braceDepth = 0;
        for (var index = start; index < end; index++)
        {
            var current = text[index];
            if (current == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote && !IsEscaped(text, index))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (current == '[')
            {
                bracketDepth++;
                continue;
            }

            if (current == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
                continue;
            }

            if (current == '{')
            {
                braceDepth++;
                continue;
            }

            if (current == '}')
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                continue;
            }

            if (current != ':' || bracketDepth > 0 || braceDepth > 0)
            {
                continue;
            }

            if (index + 1 == end)
            {
                return index;
            }

            var next = text[index + 1];
            if (char.IsWhiteSpace(next) || next is '[' or '{' or ']' or '}' or ',')
            {
                return index;
            }
        }

        return -1;
    }
}

internal sealed class JavaScriptSyntaxColorizer : CodeEditorSyntaxColorizer
{
    private static readonly Regex KeywordRegex = new(
        @"\b(?:function|return|const|let|var|if|else|for|while|switch|case|break|continue|new|class|async|await|try|catch|finally|throw)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FunctionRegex = new(
        @"\b[A-Za-z_$][\w$]*(?=\s*\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BooleanRegex = new(
        @"\b(?:true|false|null|undefined)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NumberRegex = new(
        @"(?<![\w.])[-+]?\d+(?:_\d+)*(?:\.\d+(?:_\d+)*)?(?:[eE][-+]?\d+)?(?![\w.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex StringRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line.Offset, line.Length);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var commentStart = FindCommentStart(text);
        var contentEnd = commentStart >= 0 ? commentStart : text.Length;
        HighlightMatches(line, text, 0, contentEnd, StringRegex, Palette.String);
        HighlightMatches(line, text, 0, contentEnd, NumberRegex, Palette.Number);
        HighlightMatches(line, text, 0, contentEnd, BooleanRegex, Palette.Boolean, FontWeight.SemiBold);
        HighlightMatches(line, text, 0, contentEnd, KeywordRegex, Palette.Key, FontWeight.SemiBold);
        HighlightMatches(line, text, 0, contentEnd, FunctionRegex, Palette.Function);

        if (commentStart >= 0)
        {
            ColorizeRange(line, commentStart, text.Length, Palette.Comment, style: FontStyle.Italic);
        }
    }

    private static int FindCommentStart(string text)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inTemplateString = false;
        for (var index = 0; index < text.Length - 1; index++)
        {
            var current = text[index];
            if (current == '\'' && !inDoubleQuote && !inTemplateString && !IsEscaped(text, index))
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote && !inTemplateString && !IsEscaped(text, index))
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (current == '`' && !inSingleQuote && !inDoubleQuote && !IsEscaped(text, index))
            {
                inTemplateString = !inTemplateString;
                continue;
            }

            if (inSingleQuote || inDoubleQuote || inTemplateString)
            {
                continue;
            }

            if (current == '/' && text[index + 1] == '/')
            {
                return index;
            }
        }

        return -1;
    }
}
