using System.Runtime.CompilerServices;

namespace DSLToolsGenerator;

public class IndentedTextWriter(TextWriter inner)
{
    public int CurrentIndentSize { get; set; }
    public int IndentSpaces { get; set; } = 4;

    int column = 0;

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
        column += spaces;
        return this;
    }

    public void WriteLine(ReadOnlySpan<char> chars)
    {
        bool isFirstLine = true;
        foreach (ReadOnlySpan<char> line in chars.EnumerateLines())
        {
            if (!isFirstLine || column == 0)
                PrintSpaces(CurrentIndentSize);
            isFirstLine = false;
            inner.WriteLine(line);
            column = 0;
        }
    }

    public void Write(ReadOnlySpan<char> chars)
    {
        bool isFirstLine = true;
        foreach (ReadOnlySpan<char> line in chars.EnumerateLines())
        {
            if (!isFirstLine)
            {
                inner.WriteLine();
                column = 0;
                if (line.Length > 0)
                    PrintSpaces(CurrentIndentSize);
            }
            else if (column == 0)
            {
                PrintSpaces(CurrentIndentSize);
            }
            isFirstLine = false;
            inner.Write(line);
            column += line.Length;
        }
    }

    public void WriteCode([InterpolatedStringHandlerArgument("")] AutoIndentStringHandler interpHandler)
    {
        // the text was already written during the construction of the `interpHandler` argument
        _ = interpHandler;
        WriteLine(""); // now just end the line
    }
}

[InterpolatedStringHandler]
public ref struct AutoIndentStringHandler(IndentedTextWriter writer)
{
    public AutoIndentStringHandler(
        int literalLength, int formattedCount, IndentedTextWriter writer)
        : this(writer) { }

    int temporaryInterpolationIndentation;

    public void AppendLiteral(string s)
    {
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
        var lastLine = GetLastLine(s);
        temporaryInterpolationIndentation = lastLine.Length - lastLine.TrimStart().Length;
        writer.CurrentIndentSize += temporaryInterpolationIndentation;
    }

    static ReadOnlySpan<char> GetLastLine(ReadOnlySpan<char> text)
    {
        int newLineIndex = text.LastIndexOf('\n');
        // if there's no newline, -1 + 1 = 0, which is okay
        return text[(newLineIndex + 1)..];
    }

    public void AppendFormatted<T>(T t)
    {
        writer.Write(t?.ToString());
        writer.CurrentIndentSize -= temporaryInterpolationIndentation;
    }
}
