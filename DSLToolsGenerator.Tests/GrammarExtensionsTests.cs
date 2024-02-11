using Humanizer;
using Antlr4Ast;

namespace DSLToolsGenerator.Tests;

public class GrammarExtensionsTests
{
    [Fact]
    public void given_rule_with_single_alt_ー_Analyze_assigns_correct_indexes_to_syntax_elements()
    {
        var grammar = Grammar.Parse("""
            grammar Example;
            stmt : 'import' ID // #0, child #1
                ('from' ID     // #1, child #3
                | 'as' ID      // #1, child #3
                ) 'except' ID  // #2, child #5
                ;
            ID : [a-zA-Z_]+ ;
            """);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        AssertElementMatchesComment(grammar, (_, i) =>
            $"#{i.IndexByType?.ToString() ?? "null"}, child #{i.ChildIndex?.ToString() ?? "null"}");
    }

    [Fact]
    public void given_complex_rule_with_single_alt_ー_Analyze_assigns_correct_indexes_to_syntax_elements()
    {
        var grammar = Grammar.Parse("""
            grammar Example;
            stmt : 'import' ID       // #0, child #1
                   ('from' (ID       // #1, child #3
                           | ID      // #1, child #3
                             '.' ID  // #2, child #5
                           )
                   | 'as' ID         // #1, child #3
                   )
                   'except' ID       // #null, child #null
                   ;
            ID : [a-zA-Z_]+ ;
            """);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        AssertElementMatchesComment(grammar, (_, i) =>
            $"#{i.IndexByType?.ToString() ?? "null"}, child #{i.ChildIndex?.ToString() ?? "null"}");
    }

    [Fact]
    public void given_rule_with_tokens_inside_optional_block_ー_Analyze_does_NOT_mark_indices_as_ambiguous()
    {
        var grammar = Grammar.Parse("""
            grammar Example;
            stmt : 'import' ID   // #0, child #1
                   'from' ID     // #1, child #3
                   ('as' ID      // #2, child #5
                    '.' ID       // #3, child #7
                   )?
                   ;
            ID : [a-zA-Z_]+ ;
            """);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        AssertElementMatchesComment(grammar, (_, i) =>
            $"#{i.IndexByType?.ToString() ?? "null"}, child #{i.ChildIndex?.ToString() ?? "null"}");
    }

    [Fact]
    public void given_rule_with_optional_tokens_ー_Analyze_marks_indices_after_optional_element_as_ambiguous()
    {
        var grammar = Grammar.Parse("""
            grammar Example;
            stmt : '#' isAsync='async'? // #0, child #1
                   'import' ID          // #0, child #null
                   ('from' ID           // #1, child #null
                   | )
                   ('as' ID             // #null, child #null
                   )?
                   ;
            ID : [a-zA-Z_]+ ;
            """);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        AssertElementMatchesComment(grammar, (_, i) =>
            $"#{i.IndexByType?.ToString() ?? "null"}, child #{i.ChildIndex?.ToString() ?? "null"}");
    }

    [Fact]
    public void given_rule_with_single_alt_ー_Analyze_marks_singletons_correctly()
    {
        var grammar = Grammar.Parse("""
            grammar Example;
            stmt : 'import' ID   // first ID
                ('as' (ID        // second ID
                      | STR      // only STR
                      )
                | 'from' module  // only module
                )
                'except' ID      // some ID
                ;
            module : ID '.' ID ;
            ID : [a-zA-Z_]+ ;
            """);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        AssertElementMatchesComment(grammar, (el, i) =>
            (el.IsOnlyOfType()
                ? "only "
                : $"{(i.IndexByType + 1)?.ToOrdinalWords() ?? "some"} ")
            + el);
    }

    [Fact]
    public void given_rule_with_multiple_unlabeled_alts_ー_Analyze_marks_singletons_correctly()
    {
        var grammar = Grammar.Parse("""
            grammar Example;
            stmt : 'import' ID // first ID
                   ('as' ID    // second ID
                   )?
                 | 'if' expr   // only expr
                   'goto' ID   // first ID
                 | ID          // first ID
                   '=' expr    // only expr
                 ;
            expr : ID ;
            ID : [a-zA-Z_]+ ;
            """);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        AssertElementMatchesComment(grammar, (el, i) =>
            (el.IsOnlyOfType()
                ? "only "
                : $"{(i.IndexByType + 1)?.ToOrdinalWords() ?? "some"} ")
            + el);
    }

    [Fact]
    public void given_rule_with_multiple_labeled_alts_ー_Analyze_marks_singletons_correctly()
    {
        var grammar = Grammar.Parse("""
            grammar Example;
            stmt : 'import' ID // first ID
                   ('as' ID    // second ID
                   )?         #importStmt
                 | 'if' expr   // only expr
                   'goto' ID   // only ID
                              #ifGotoStmt
                 | expr        // first expr
                   '.' ID      // only ID
                   '=' expr    // second expr
                              #memberAssignStmt
                 ;
            expr : ID ;
            ID : [a-zA-Z_]+ ;
            """);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        AssertElementMatchesComment(grammar, (el, i) =>
            (el.IsOnlyOfType()
                ? "only "
                : $"{(i.IndexByType + 1)?.ToOrdinalWords() ?? "some"} ")
            + el);
    }

    static void AssertElementMatchesComment(Grammar grammar,
        Func<SyntaxElement, ElementIndexInfo, string> commentTextFormat)
    {
        var checks = grammar.GetAllDescendants().OfType<SyntaxElement>()
            .Where(el => !el.Children().Any()) // leaf nodes only
            .Select(el => {
                if (el.CommentsAfter is [{ Text: string comment }, ..])
                    return (actual: commentTextFormat(el, el.GetElementIndex()), expected: comment.Trim());
                return ((string actual, string expected)?)null;
            })
            .WhereNotNull();
        Assert.Equal(checks.Select(c => c.expected), checks.Select(c => c.actual));
    }
}
