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
    public void MakeRegex_ー_generates_correct_regex(string antlrRuleBody, string expectedRegex)
    {
        (TmLanguageGenerator gen, var grammar) = GetTmLanguageGeneratorForGrammar(
            $"lexer grammar ExampleLexer; ABC : {antlrRuleBody} ;");
        Assert.Equal(expectedRegex, gen.MakeRegex(grammar.LexerRules[0].AlternativeList.Items[0]));
    }

    [Fact]
    public void given_lexer_grammar_with_keywords_ー_generated_TM_grammar_tokenizes_correctly()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
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

    (TmLanguageGenerator, Antlr4Ast.Grammar) GetTmLanguageGeneratorForGrammar(string grammarCode)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        Assert.Empty(grammar.ErrorMessages);
        return (new TmLanguageGenerator(grammar, d => {
            if (d.Severity == DiagnosticSeverity.Error)
                Assert.Fail(d.ToString());
            else
                testOutput.WriteLine(d.ToString());
        }), grammar);
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
