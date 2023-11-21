namespace DSLToolsGenerator.Tests;

public class IndentedTextWriterTests
{
    [Fact]
    public void Write_does_not_append_newline()
    {
        (var writer, StringWriter output) = CreateNew();
        writer.Write("bar a b");

        Assert.Equal("bar a b", output.ToString());
    }

    [Fact]
    public void WriteLine_appends_newline()
    {
        (var writer, StringWriter output) = CreateNew();
        writer.WriteLine("foo");
        writer.Write("bar");
        writer.WriteLine("baz");

        Assert.Equal("""
            foo
            barbaz

            """, output.ToString());
    }

    [Fact]
    public void after_calling_Indent__WriteLine_appends_newline_with_indentation()
    {
        (var writer, StringWriter output) = CreateNew();
        writer.WriteLine("foo");
        writer.Indent().WriteLine("bar");
        writer.Unindent().WriteLine("baz");

        Assert.Equal("""
            foo
                bar
            baz

            """, output.ToString());
    }

    [Fact]
    public void after_calling_Indent__WriteLine_autoindents_multiple_lines()
    {
        (var writer, StringWriter output) = CreateNew();
        writer.WriteLine("foo");
        writer.Indent().WriteLine("line 1\n line 2");
        writer.Unindent().WriteLine("baz");

        Assert.Equal("""
            foo
                line 1
                 line 2
            baz

            """, output.ToString());
    }

    [Fact]
    public void after_calling_Indent__Write_autoindents_multiple_lines()
    {
        (var writer, StringWriter output) = CreateNew();
        writer.WriteLine("foo");
        writer.Indent().Write("line 1\n line 2\n");
        writer.Unindent().WriteLine("baz");

        Assert.Equal("""
            foo
                line 1
                 line 2
            baz

            """, output.ToString());
    }

    [Fact]
    public void WriteCode_automatically_applies_extra_indentation_to_interpolations()
    {
        (var writer, StringWriter output) = CreateNew();
        string multilineString = "line 1\nline 2";
        writer.Indent().WriteCode($"""
            foo
                {multilineString}
            baz
            """);

        Assert.Equal("""
            foo
                line 1
                line 2
            baz

        """, output.ToString());
    }

    static (IndentedTextWriter, StringWriter) CreateNew()
    {
        var sw = new StringWriter();
        var writer = new IndentedTextWriter(sw);
        return (writer, sw);
    }
}