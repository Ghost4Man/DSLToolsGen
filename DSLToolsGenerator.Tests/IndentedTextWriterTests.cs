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

    [Fact]
    public void WriteCode_invokes_embedded_actions()
    {
        (var writer, StringWriter output) = CreateNew();
        string multilineString = "line 1\nline 2";

        static void writeLine3And4(IndentedTextWriter output)
        {
            output.WriteLine("line 3");
            output.WriteLine("line 4");
        }

        writer.Indent().WriteCode($"""
            foo
                {multilineString}

                num = {_ => output.Write("123")};

                {writeLine3And4}
            baz
            """);

        Assert.Equal("""
            foo
                line 1
                line 2

                num = 123;

                line 3
                line 4
            baz

        """, output.ToString());
    }

    [Fact]
    public void when_given_empty_embedded_action_on_standalone_line__WriteCode_removes_extra_newline()
    {
        (var writer, StringWriter output) = CreateNew();

        writer.Indent().WriteCode($$"""
            foo
                {{_ => { }}}
            baz
            """);

        Assert.Equal("""
            foo
            baz

        """, output.ToString());
    }

    [Fact]
    public void when_given_empty_embedded_action_surrounded_by_empty_lines__WriteCode_removes_extra_empty_lines()
    {
        (var writer, StringWriter output) = CreateNew();

        writer.WriteCode($$"""
            one

            {{_ => { }}}

            two
            """);

        Assert.Equal("""
            one

            two

            """, output.ToString());
    }

    [Fact]
    public void when_given_null_interpolation_surrounded_by_empty_lines__WriteCode_removes_extra_empty_lines()
    {
        (var writer, StringWriter output) = CreateNew();

        writer.WriteCode($$"""
            one

            {{(false ? "x" : null)}}

            two
            """);

        Assert.Equal("""
            one

            two

            """, output.ToString());
    }

    [Fact]
    public void when_given_two_null_interpolations_surrounded_by_empty_lines__WriteCode_removes_extra_empty_lines()
    {
        (var writer, StringWriter output) = CreateNew();

        writer.WriteCode($$"""
            one

            {{(false ? "x" : null)}}

            {{(false ? "x" : null)}}

            two
            """);

        Assert.Equal("""
            one

            two

            """, output.ToString());
    }

    [Fact]
    public void when_given_two_nonempty_interpolations_surrounded_by_empty_lines__WriteCode_does_not_remove_newlines()
    {
        (var writer, StringWriter output) = CreateNew();

        writer.Indent().WriteCode($$"""
            one

            {{_ => writer.WriteCode($"using System;")}}

            {{"namespace Abc;"}}

            two
            """);

        Assert.Equal("""
            one

            using System;

            namespace Abc;

            two

        """, output.ToString());
    }

    [Fact]
    public void when_given_empty_action_before_empty_line__WriteCode_preserves_empty_line()
    {
        (var writer, StringWriter output) = CreateNew();

        writer.WriteCode($$"""
            using System;
            {{_ => { }}}

            foo
            """);

        Assert.Equal("""
            using System;

            foo

            """, output.ToString());
    }

    [Fact]
    public void when_given_two_empty_actions_before_empty_line__WriteCode_preserves_one_empty_line()
    {
        (var writer, StringWriter output) = CreateNew();

        writer.WriteCode($$"""
            one
            {{_ => { }}}

            {{_ => { }}}

            two
            """);

        Assert.Equal("""
            one

            two

            """, output.ToString());
    }

    static (IndentedTextWriter, StringWriter) CreateNew()
    {
        var sw = new StringWriter { NewLine = "\n" };
        var writer = new IndentedTextWriter(sw);
        return (writer, sw);
    }
}
