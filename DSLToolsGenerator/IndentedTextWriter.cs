using System.Numerics;
using System.Runtime.CompilerServices;

namespace DSLToolsGenerator;

public class IndentedTextWriter(TextWriter inner)
{
    public int CurrentIndentSize { get; set; }
    public int IndentSpaces { get; set; } = 4;

    internal int CurrentColumn { get; private set; }

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
}

[InterpolatedStringHandler]
public ref struct AutoIndentStringHandler(IndentedTextWriter writer)
{
    public AutoIndentStringHandler(
        int literalLength, int formattedCount, IndentedTextWriter writer)
        : this(writer) { }

    int temporaryInterpolationIndentation;
    bool temporaryNewlineDeduplication;

    public void AppendLiteral(ReadOnlySpan<char> s)
    {
        // If the previous call was a AppendFormatted and it ended with a newline
        // and `s` begins with a newline, skip ("deduplicate") this newline
        if (temporaryNewlineDeduplication)
        {
            s = s is ['\n', .. var rest] ? rest :
                s is ['\r', '\n', .. var rest2] ? rest2 :
                s;
            temporaryNewlineDeduplication = false;
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
    }

    static ReadOnlySpan<char> GetLastLine(ReadOnlySpan<char> text)
    {
        int newLineIndex = text.LastIndexOf('\n');
        // if there's no newline, -1 + 1 = 0, which is okay
        return text[(newLineIndex + 1)..];
    }

    /// <summary>
    /// Simply invokes the provided action. Useful for invoking void-returning
    /// code generation methods form inside string interpolation.
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
        embeddedAction?.Invoke(writer);
        writer.CurrentIndentSize -= temporaryInterpolationIndentation;
        temporaryInterpolationIndentation = 0;
        temporaryNewlineDeduplication = writer.CurrentColumn == 0;
    }

    public void AppendFormatted(ReadOnlySpan<char> str)
    {
        writer.Write(str);
        writer.CurrentIndentSize -= temporaryInterpolationIndentation;
        temporaryInterpolationIndentation = 0;
        temporaryNewlineDeduplication = writer.CurrentColumn == 0;
    }

    public void AppendFormatted<T>(T number) where T : INumber<T>
    {
        writer.Write(number?.ToString());
        writer.CurrentIndentSize -= temporaryInterpolationIndentation;
        temporaryInterpolationIndentation = 0;
        temporaryNewlineDeduplication = writer.CurrentColumn == 0;
    }
}
