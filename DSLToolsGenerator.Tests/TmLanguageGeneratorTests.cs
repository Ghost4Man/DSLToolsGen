using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace DSLToolsGenerator.SyntaxHighlighting.Tests;

public class TmLanguageGeneratorTests(ITestOutputHelper testOutput)
{
    [Theory]
    [InlineData(@"'a'", @"a")]
    [InlineData(@"'\n'", @"\n")]
    [InlineData(@"'a'+", @"a+")]
    [InlineData(@"'a'*", @"a*")]
    [InlineData(@"'{a}'+", @"(?:\{a\})+")]
    [InlineData(@"'for each'", @"for each")]
    [InlineData(@"'a' 'bb' '!'+ 'c'", @"abb!+c")]
    [InlineData(@"'a' 'bb'? 'c'", @"a(?:bb)?c")]
    [InlineData(@"('a' | 'b')+", @"(?:a|b)+")]
    [InlineData(@"('a' | 'b' | ('c' | [xyz]))", @"(?:a|b|(?:c|[xyz]))")]
    [InlineData(@"('{' 'a' '}')+", @"(?:\{a\})+")]
    [InlineData(@".*", @".*")]
    [InlineData(@".+?", @".+?")]
    [InlineData(@"'a'..'z'", @"[a-z]")]
    [InlineData(@"[a-z]", @"[a-z]")]
    [InlineData(@"[a-zA-Z]", @"[a-zA-Z]")]
    [InlineData(@"[a-zA-Z-]", @"[a-zA-Z-]")]
    [InlineData(@"EOF", @"\z")]
    [InlineData(@"~[a-zA-Z]", @"[^a-zA-Z]")]
    [InlineData(@"~[a-zA-Z]+", @"[^a-zA-Z]+")]
    [InlineData(@"~('-' | ']' | '^')", @"[^\-\]\^]")]
    [InlineData(@"~([0-9] | '#' | '+')", @"[^0-9#\+]")]
    [InlineData(@"'%' (~'%' | '%%')* '%'", @"%(?:[^%]|%%)*%")] // escape by doubling, e.g. `%(a * b = 10%%)%`
    public void MakeRegex_ー_generates_correct_regex(string antlrRuleBody, string expectedRegex)
    {
        (TmLanguageGenerator gen, var grammar) = GetTmLanguageGeneratorForGrammar(
            $"lexer grammar ExampleLexer; ABC : {antlrRuleBody} ;");
        Assert.Equal(expectedRegex, gen.MakeRegex(grammar.LexerRules[0].AlternativeList.Items[0], parentRules: []));
    }

    [Theory]
    [InlineData(null, null, null,   @"(?:x(?:[A-Z])+|@abc)")]
    [InlineData(null, false, false, @"(?:x(?:[A-Z])+|@abc)")]
    [InlineData(true, false, null,  @"(?:x(?:[A-Z])+|@abc)")]
    [InlineData(null, null, true,   @"(?:x(?i:[A-Z])+|@abc)")]
    [InlineData(true, false, true,  @"(?:x(?i:[A-Z])+|@abc)")]
    [InlineData(null, true, null,   @"(?i:x(?:[A-Z])+|@abc)")]
    [InlineData(null, true, true,   @"(?i:x(?:[A-Z])+|@abc)")]
    [InlineData(true, null, null,   @"(?i:x(?:[A-Z])+|@abc)")]
    [InlineData(null, true, false,  @"(?i:x(?-i:[A-Z])+|@abc)")]
    [InlineData(true, null, false,  @"(?i:x(?-i:[A-Z])+|@abc)")]
    public void MakeRegex_on_rules_with_case_sensitivity_options_ー_generates_correct_regex(
        bool? grammarCaseInsensitive, bool? ruleCaseInsensitive, bool? fragmentRuleCaseInsensitive, string expectedRegex)
    {
        (TmLanguageGenerator gen, var grammar) = GetTmLanguageGeneratorForGrammar($"""
            lexer grammar ExampleLexer;
            {optionList(grammarCaseInsensitive)}
            ABC {optionList(ruleCaseInsensitive)} : 'x' LETTER+ | '@abc' ;
            fragment LETTER {optionList(fragmentRuleCaseInsensitive)} : [A-Z] ;
            """);
        testOutput.WriteLine(grammar.ToString());
        Assert.Equal(expectedRegex, gen.MakeRegex(grammar.LexerRules[0], parentRules: []));

        static string optionList(bool? caseInsensitive)
            => caseInsensitive is bool value ? $"options {{ caseInsensitive={value}; }}" : "";
    }

    [Theory]
    [InlineData("grammar")]
    [InlineData("lexer grammar")]
    public void given_lexer_grammar_with_keywords_ー_generated_TM_grammar_tokenizes_correctly(string grammarKind)
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar($"""
            {grammarKind} ExampleLexer;
            IF_KW : 'if' ;
            THEN_KW : 'then' ;
            ELSE_KW : 'else' ;
            ID : ('a'..'z' | '_')+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = """
            if a then b else c;
            ;if(ifoo barthen)then[elseveer]
               else ELSE _else_;
            """;
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("if", ["keyword.if_kw.example"]),
            ExpectedToken("a", ["variable.id.example"]),
            ExpectedToken("then", ["keyword.then_kw.example"]),
            ExpectedToken("b", ["variable.id.example"]),
            ExpectedToken("else", ["keyword.else_kw.example"]),
            ExpectedToken("c", ["variable.id.example"]),
            ExpectedToken("if", ["keyword.if_kw.example"]),
            ExpectedToken("ifoo", ["variable.id.example"]),
            ExpectedToken("barthen", ["variable.id.example"]),
            ExpectedToken("then", ["keyword.then_kw.example"]),
            ExpectedToken("elseveer", ["variable.id.example"]),
            ExpectedToken("else", ["keyword.else_kw.example"]),
            ExpectedToken("_else_", ["variable.id.example"]));
    }

    [Fact]
    public void given_ANTLR_lexer_grammar_with_character_sets_ー_generated_TM_grammar_correctly_tokenizes_identifiers()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            ID : [a-zA-Z_][a-zA-Z0-9_]* ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = """
            abc..(def)  FooBAR01 __a_0b__99X
                line2 _
            """;
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("abc", ["variable.id.example"]),
            ExpectedToken("def", ["variable.id.example"]),
            ExpectedToken("FooBAR01", ["variable.id.example"]),
            ExpectedToken("__a_0b__99X", ["variable.id.example"]),
            ExpectedToken("line2", ["variable.id.example"]),
            ExpectedToken("_", ["variable.id.example"]));
    }

    [Fact]
    public void given_ANTLR_combined_grammar_with_implicit_token_literals_ー_generated_TM_grammar_correctly_tokenizes_all_keywords()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            grammar ExampleLexer;
            stmt : 'if' ID 'then' stmt    #ifStmt
                 | ('print' | 'PRINT') ID #printStmt ;
            ID : [a-zA-Z_]+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = "if a then PRINT x";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("if", ["keyword.if.example"]),
            ExpectedToken("a", ["variable.id.example"]),
            ExpectedToken("then", ["keyword.then.example"]),
            ExpectedToken("PRINT", ["keyword.print.example"]),
            ExpectedToken("x", ["variable.id.example"]));
    }

    [Fact]
    public void given_ANTLR_grammar_and_customized_TM_scopes_ー_generated_TM_grammar_tokenizes_correctly()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            TAG : '<' ID '>' ;
            BOLD : '**' .*? '**' ;
            ID : [a-zA-Z_][a-zA-Z0-9_]* ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """,
            config: new() {
                RuleSettings = new Dictionary<string, RuleOptions>() {
                    ["TAG"] = new(TextMateScopeName: "entity.name.tag"),
                    ["BOLD"] = new(TextMateScopeName: "markup.bold"),
                }
            });
        const string input = "abc **this is bold** <sometag>";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("abc", ["variable.id.example"]),
            ExpectedToken("**this is bold**", ["markup.bold.example"]),
            ExpectedToken("<sometag>", ["entity.name.tag.example"]));
    }

    [Fact]
    public void given_ANTLR_combined_grammar_and_customized_TM_scopes_including_implicit_tokens_ー_generated_TM_grammar_tokenizes_correctly()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            grammar ExampleLexer;
            stmt : 'if' ID ':' ID ;
            TAG : '<' ID '>' ;
            BOLD : '**' .*? '**' ;
            ID : [a-zA-Z_][a-zA-Z0-9_]* ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """,
            config: new() {
                RuleSettings = new Dictionary<string, RuleOptions>() {
                    ["TAG"] = new(TextMateScopeName: "entity.name.tag"),
                    ["BOLD"] = new(TextMateScopeName: "markup.bold"),
                    ["'if'"] = new(TextMateScopeName: "keyword.control.if"),
                    ["':'"] = new(TextMateScopeName: "punctuation.colon"),
                }
            });
        const string input = "<script> if a: **b**";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("<script>", ["entity.name.tag.example"]),
            ExpectedToken("if", ["keyword.control.if.example"]),
            ExpectedToken("a", ["variable.id.example"]),
            ExpectedToken(":", ["punctuation.colon.example"]),
            ExpectedToken("**b**", ["markup.bold.example"]));
    }

    [Fact]
    public void given_ANTLR_lexer_grammar_with_recursive_rules_ー_ignores_them()
    {
        // note: here we essentially just check that the generator
        // does not crash due to infinite recursion of MakeRegex calls,
        // but we might want to add proper support for recursive lexer rules later
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            ID : [a-zA-Z]+ ;
            COMMENT : BLOCK_COMMENT | '//' ~[\r\n]* ;
            fragment BLOCK_COMMENT: '/*' COMMENT_CONTENT*? '*/' -> channel(HIDDEN);
            fragment COMMENT_CONTENT : (BLOCK_COMMENT | .) ;
            """);
        const string input = "abc // comment here";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("abc", ["variable.id.example"]),
            ExpectedToken("// comment here", ["comment.line.comment.example"]));
    }

    Action<(string text, IReadOnlyList<string> scopes)> ExpectedToken(string text, string[] scopes) => token => {
        Assert.Equal(text, token.text);
        Assert.Equal(scopes, token.scopes);
    };

    // based on TextMateSharp demo code
    IEnumerable<(string text, IReadOnlyList<string> scopes)> TokenizeString(
        string tmLanguageJson, string initialScopeName, string input)
    {
        testOutput.WriteLine("Generated TextMate grammar:");
        testOutput.WriteLine(tmLanguageJson);

        Registry registry = new(new SingleGrammarRegistryOptions(tmLanguageJson, initialScopeName));
        var grammar = registry.LoadGrammar(initialScopeName);

        IStateStack? ruleStack = null;

        foreach (var line in input.Split(["\r\n", "\n"], default))
        {
            ITokenizeLineResult result = grammar.TokenizeLine(line, ruleStack, TimeSpan.MaxValue);

            ruleStack = result.RuleStack;

            foreach (IToken token in result.Tokens)
            {
                var nonDefaultScopes = token.Scopes.Except([initialScopeName]).ToList();
                if (nonDefaultScopes.Count > 0)
                    yield return (line[token.StartIndex..token.EndIndex], nonDefaultScopes);
            }
        }
    }

    (TmLanguageGenerator, Antlr4Ast.Grammar) GetTmLanguageGeneratorForGrammar(
        string grammarCode, SyntaxHighlightingConfiguration? config = null)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        Assert.Empty(grammar.ErrorMessages);
        return (new TmLanguageGenerator(grammar, handleDiagnostic, config ?? new()), grammar);

        void handleDiagnostic(Diagnostic d)
        {
            if (d.Severity == DiagnosticSeverity.Error)
                Assert.Fail(d.ToString());
            else
                testOutput.WriteLine(d.ToString());
        }
    }
}

class SingleGrammarRegistryOptions(string tmLanguageJson, string grammarScopeName) : IRegistryOptions
{
    public IRawGrammar GetGrammar(string scopeName)
    {
        if (scopeName != grammarScopeName)
            throw new NotImplementedException($"unknown scope name '{scopeName}', I only recognize '{grammarScopeName}'");
        using var stream = GenerateStreamFromString(tmLanguageJson);
        using var reader = new StreamReader(stream);
        return GrammarReader.ReadGrammarSync(reader);
    }

    public IRawTheme GetDefaultTheme() => new ThemeRaw();
    public ICollection<string> GetInjections(string scopeName) => [];
    public IRawTheme GetTheme(string scopeName) => throw new NotImplementedException();

    // borrowed from TextMateSharp (src/TextMateSharp.Tests/Internal/Grammars/Reader/GrammarReaderTests.cs)
    static MemoryStream GenerateStreamFromString(string s)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
