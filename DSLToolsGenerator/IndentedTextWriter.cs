using System.Numerics;
using System.Runtime.CompilerServices;

namespace DSLToolsGenerator;

public class IndentedTextWriter(TextWriter inner)
{
    public int CurrentIndentSize { get; set; }
    public int IndentSpaces { get; set; } = 4;

    internal int CurrentColumn { get; private set; } // index (starts at zero)
    internal int CurrentLine { get; private set; } // index (starts at zero)

    public IndentedTextWriter Indent()
    {
        CurrentIndentSize += IndentSpaces;
        return this;
    }

    public IndentedTextWriter Unindent()
    {
        CurrentIndentSize = Math.Max(0, CurrentIndentSize - IndentSpaces);
        return this;
    }

    public IndentedTextWriter PrintSpaces(int spaces)
    {
        for (int i = 0; i < spaces; i++)
        {
            inner.Write(' ');
        }
        CurrentColumn += spaces;
        return this;
    }

    public void WriteLine(ReadOnlySpan<char> chars)
    {
        bool isFirstLine = true;
        foreach (ReadOnlySpan<char> line in chars.EnumerateLines())
        {
            if (!isFirstLine || CurrentColumn == 0)
                PrintSpaces(CurrentIndentSize);
            isFirstLine = false;
            inner.WriteLine(line);
            CurrentColumn = 0;
            CurrentLine++;
        }
    }

    public void Write(ReadOnlySpan<char> chars)
    {
        if (chars.Length == 0)
            return;

        bool isFirstLine = true;
        foreach (ReadOnlySpan<char> line in chars.EnumerateLines())
        {
            if (!isFirstLine)
            {
                inner.WriteLine();
                CurrentColumn = 0;
                CurrentLine++;
                if (line.Length > 0)
                    PrintSpaces(CurrentIndentSize);
            }
            else if (CurrentColumn == 0)
            {
                if (line.Length > 0)
                    PrintSpaces(CurrentIndentSize);
            }
            isFirstLine = false;
            inner.Write(line);
            CurrentColumn += line.Length;
        }
    }

    public void WriteCode([InterpolatedStringHandlerArgument("")] AutoIndentStringHandler interpHandler)
    {
        // the text was already written during the construction of the `interpHandler` argument
        _ = interpHandler;
        if (CurrentColumn != 0) // ensure any output after this WriteCode call starts on a new line
            WriteLine("");
    }

    public void WriteCodeInline([InterpolatedStringHandlerArgument("")] AutoIndentStringHandler interpHandler)
    {
        // the text was already written during the construction of the `interpHandler` argument
        _ = interpHandler;
    }
}

[InterpolatedStringHandler]
public ref struct AutoIndentStringHandler(IndentedTextWriter writer)
{
    public AutoIndentStringHandler(
        int literalLength, int formattedCount, IndentedTextWriter writer)
        : this(writer) { }

    int temporaryInterpolationIndentation;
    int prefixNewlinesToTrim;

    public void AppendLiteral(ReadOnlySpan<char> s)
    {
        var originalString = s;

        for (int i = 0; i < prefixNewlinesToTrim; i++)
        {
            s = s is ['\n', .. var rest] ? rest :
                s is ['\r', '\n', .. var rest2] ? rest2 :
                s;
        }

        var lastLine = GetLastLine(s);
        temporaryInterpolationIndentation = lastLine.Length - lastLine.TrimStart().Length;

        // if the last line is indentation-only, don't print it immediately, just set the indent
        if (lastLine.IsWhiteSpace())
        {
            s = s[..^temporaryInterpolationIndentation]; // trim the indentation characters
        }

        writer.Write(s);

        // Detect what indentation should be applied inside interpolations (AppendFormatted)
        // from existing indentation in the AppendLiteral.
        // For example, here the interpolated value `someMultilineString` will be indented with 4 spaces
        //   $$"""
        //   unindented line 1
        //     indented line 2
        //       {{someMultilineString}}
        //   unindented again
        //   """
        writer.CurrentIndentSize += temporaryInterpolationIndentation;

        // Count how many newlines there are at the end of this string
        // (to trim the same number of newlines next time
        // if the AppendFormatted call in between did not print anything)
        // For example, this will only print a single empty line between "one" and "two"
        //   $$"""
        //   one            |  AppendLiteral("one\n");    // prints "one\n"; prefixNewlinesToTrim=1
        //   {{null}}       |  AppendFormatted(null);     // AfterAppendFormatted(wasEmpty: true)
        //                  |  AppendLiteral("\n\n");     // prints "\n" (1 newline was trimmed); prefixNewlinesToTrim=2
        //   {{null}}       |  AppendFormatted(null);     // AfterAppendFormatted(wasEmpty: true)
        //                  |          
        //   two            |  AppendLiteral("\n\ntwo");  // prints "two" (2 newlines were trimmed)
        if (getTrailingWhitespace(originalString) is { IsEmpty: false } trailingWhitespace)
        {
            prefixNewlinesToTrim = trailingWhitespace.Count('\n');
        }

        ReadOnlySpan<char> getTrailingWhitespace(ReadOnlySpan<char> text)
            => text.LastIndexOfAnyExcept(['\r', '\n', ' ', '\t']) is int index and >= 0
                ? text[index..]
                : text;
    }

    static ReadOnlySpan<char> GetLastLine(ReadOnlySpan<char> text)
    {
        int newLineIndex = text.LastIndexOf('\n');
        // if there's no newline, -1 + 1 = 0, which is okay
        return text[(newLineIndex + 1)..];
    }

    /// <summary>
    /// Simply invokes the provided action. Useful for invoking void-returning
    /// code generation methods from inside string interpolation.
    /// </summary>
    /// <example>
    /// <code>
    /// $$"""
    /// class Example
    /// {
    ///     {{w => gen.GenerateProlog(w, "Example")}}
    ///
    ///     {{gen.GenerateClassBody}}
    /// }
    /// """
    /// </code>
    /// </example>
    public void AppendFormatted(Action<IndentedTextWriter> embeddedAction)
    {
        var before = (writer.CurrentLine, writer.CurrentColumn);
        embeddedAction?.Invoke(writer);
        var after = (writer.CurrentLine, writer.CurrentColumn);
        AfterAppendFormatted(wasEmpty: after == before);
    }

    public void AppendFormatted(ReadOnlySpan<char> str)
    {
        writer.Write(str);
        AfterAppendFormatted(wasEmpty: str.IsEmpty);
    }

    public void AppendFormatted<T>(T number) where T : INumber<T>
    {
        writer.Write(number?.ToString());
        AfterAppendFormatted(wasEmpty: false);
    }

    void AfterAppendFormatted(bool wasEmpty)
    {
        writer.CurrentIndentSize -= temporaryInterpolationIndentation;
        temporaryInterpolationIndentation = 0;

        if (!wasEmpty)
            prefixNewlinesToTrim = 0;

        // If the printed text ends with a newline,
        // skip ("deduplicate") one newline in the next AppendLiteral call
        if (!wasEmpty && writer.CurrentColumn == 0)
            prefixNewlinesToTrim++;
    }
}
