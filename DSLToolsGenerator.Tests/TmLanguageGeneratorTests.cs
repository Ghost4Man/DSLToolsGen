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
    [InlineData(@"('a' | 'b' | ('c' | [xyz]))", @"(?:(?:[xyz]|c)|a|b)")]
    [InlineData(@"('Get' | ('GetFirstKey' | 'GetFirst'))", @"(?:(?:GetFirstKey|GetFirst)|Get)")]
    [InlineData(@"('{' 'a' '}')+", @"(?:\{a\})+")]
    [InlineData(@".*", @".*")]
    [InlineData(@".+?", @".+?")]
    [InlineData(@"'a'..'z'", @"[a-z]")]
    [InlineData(@"[a-z]", @"[a-z]")]
    [InlineData(@"[^'\n]", @"[\^'\n]")]
    [InlineData(@"[-]", @"[-]")]
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
    [InlineData(@"'IF'", @"\b(?:IF)\b")]
    [InlineData(@"'@import'", @"(?:@import)\b")]
    [InlineData(@"'do:'", @"\b(?:do:)")]
    [InlineData(@"'.sync.'", @"(?:\.sync\.)")]
    [InlineData(@"[Ss] [eE] T", @"\b(?:[Ss][eE](?:[Tt]))\b")]
    public void MakeRegex_given_keyword_rule_ー_generates_regex_with_correct_word_boundary_anchors(string antlrRuleBody, string expectedRegex)
    {
        (TmLanguageGenerator gen, var grammar) = GetTmLanguageGeneratorForGrammar(
            $"lexer grammar ExampleLexer; ABC : {antlrRuleBody} ; T : [Tt] ;");
        Assert.Equal(expectedRegex, gen.MakeRegex(grammar.LexerRules[0], parentRules: []));
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
    [InlineData("'if'", true)]
    [InlineData("'SELECT'", true)]
    [InlineData("('SELECT' | 'select')", true)]
    [InlineData("[Ff][oO][Rr]", true)]
    [InlineData("('F'|'f')('o'|'O')('R'|'r')", true)]
    [InlineData("F ('o'|'O') [rR]", true)]
    [InlineData("'@import'", true)]
    [InlineData("'@' 'import'", true)]
    [InlineData("AT 'import'", true)]
    [InlineData("F AT", true)]
    [InlineData("'[import]'", true)]
    [InlineData("'_'", true)]
    [InlineData("AT", false)]
    [InlineData("AT | 'at'", false)]
    [InlineData("[a-z]", false)]
    [InlineData("[az]", false)]
    [InlineData("'/*' (ABC | .) '*/'", false)]
    public void RuleIsKeyword_ー_correctly_recognizes_keyword_rules(string antlrRuleBody, bool expectedIsKeyword)
    {
        (TmLanguageGenerator gen, var grammar) = GetTmLanguageGeneratorForGrammar(
            $"lexer grammar ExampleLexer; ABC : {antlrRuleBody} ; F : 'f' ; AT : '@' ;");
        Assert.Equal(expectedIsKeyword, gen.RuleIsKeyword(grammar.LexerRules[0]));
    }

    [Theory]
    [InlineData("grammar")]
    [InlineData("lexer grammar")]
    public void given_simple_grammar_with_keywords_ー_generated_TM_grammar_tokenizes_correctly(string grammarKind)
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
    public void given_implicit_token_literals_with_symbols_ー_generated_TM_grammar_correctly_tokenizes_all_keywords()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            grammar ExampleLexer;
            stmt : '<if>' ID 'then:' stmt+ '</if>'  #ifStmt
                 | PRINT ID                         #printStmt
                 | '.lock' ID                       #lockStmt ;
            PRINT : At 'print' ;
            fragment At : '@' ;
            ID : [a-zA-Z_]+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = "<if> if then: @print x .lock y </if>";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("<if>", ["keyword.'<if>'.example"]),
            ExpectedToken("if", ["variable.id.example"]),
            ExpectedToken("then:", ["keyword.'then:'.example"]),
            ExpectedToken("@print", ["keyword.print.example"]),
            ExpectedToken("x", ["variable.id.example"]),
            ExpectedToken(".lock", ["keyword.'.lock'.example"]),
            ExpectedToken("y", ["variable.id.example"]),
            ExpectedToken("</if>", ["keyword.'</if>'.example"]));
    }

    [Fact]
    public void given_manual_case_insensitive_keyword_rules_ー_generated_TM_grammar_correctly_tokenizes_all_keywords()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            grammar ExampleLexer;
            stmt : ('for each' | FOR) ID DO stmt   #ifStmt
                 | PRINT ID                        #printStmt ;
            FOR : 'for' | 'FOR' ;
            DO : ([dD][Oo]) ;
            PRINT : (At [Pp] [rR] I [Nn] T) ;
            T : ('T' | 't') ;
            fragment I : 'i' | 'I' ;
            At : '@' ;
            ID : [a-zA-Z_]+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = "for each a_b Do @PriNt DogName";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("for each", ["keyword.'for_each'.example"]),
            ExpectedToken("a_b", ["variable.id.example"]),
            ExpectedToken("Do", ["keyword.do.example"]),
            ExpectedToken("@PriNt", ["keyword.print.example"]),
            ExpectedToken("DogName", ["variable.id.example"]));
    }

    [Fact]
    public void given_lexer_grammar_with_nonword_keywords_ー_generated_TM_grammar_tokenizes_correctly()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            IF : '#if' ;
            OVERRIDE : '@override' ;
            INVALID_KW : [#@] ID ;
            FOREACH : 'for each' ;
            ID : [a-zA-Z]+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = """
            #if if
            @override foo;
            for each #ifo@overrideableitems;
            afor eachb;
            """;
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("#if", ["keyword.if.example"]),
            ExpectedToken("if", ["variable.id.example"]),
            ExpectedToken("@override", ["keyword.override.example"]),
            ExpectedToken("foo", ["variable.id.example"]),
            ExpectedToken("for each", ["keyword.foreach.example"]),
            ExpectedToken("#ifo", ["other.invalid_kw.example"]),
            ExpectedToken("@overrideableitems", ["other.invalid_kw.example"]),
            ExpectedToken("afor", ["variable.id.example"]),
            ExpectedToken("eachb", ["variable.id.example"]));
    }

    [Fact]
    public void given_lexer_grammar_with_alternation_of_literals_ー_generated_TM_grammar_finds_longest_match_within_rule()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            CMD : '$Get' | '$Set' | '$GetValue' | '$SetValue' ;
            ID : '$'? [a-zA-Z]+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = """
            $Set x
            $GetValue x
            """;
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("$Set", ["keyword.cmd.example"]),
            ExpectedToken("x", ["variable.id.example"]),
            ExpectedToken("$GetValue", ["keyword.cmd.example"]),
            ExpectedToken("x", ["variable.id.example"]));
    }

    [Fact]
    public void given_lexer_grammar_with_alternation_of_literals_ー_generated_TM_grammar_finds_longest_match_between_rules()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            CMD : '$For' | '$Set' | '$ForEach' | '$SetValue' ;
            ID : '$'? [a-zA-Z]+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """);
        const string input = """
            $Settlement $Fortress
            $Set x
            """;
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("$Settlement", ["variable.id.example"]),
            ExpectedToken("$Fortress", ["variable.id.example"]),
            ExpectedToken("$Set", ["keyword.cmd.example"]),
            ExpectedToken("x", ["variable.id.example"]));
    }

    [Fact]
    public void given_rules_with_character_sets_ー_generated_TM_grammar_correctly_tokenizes_identifiers()
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
    public void given_implicit_token_literals_ー_generated_TM_grammar_correctly_tokenizes_all_keywords()
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
    public void given_customized_TM_scopes_ー_generated_TM_grammar_tokenizes_correctly()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            TAG : '<' ID '>' ;
            BOLD : '**' .*? '**' ;
            ID : [a-zA-Z_][a-zA-Z0-9_]* ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """,
            config: new() {
                SyntaxHighlighting = new() {
                    RuleSettings = new Dictionary<string, RuleOptions>() {
                        ["TAG"] = new() { TextMateScopeName = "entity.name.tag" },
                        ["BOLD"] = new() { TextMateScopeName = "markup.bold" },
                    }
                }
            });
        const string input = "abc **this is bold** <sometag>";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeStringFused(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("abc", ["variable.id.example"]),
            ExpectedToken("**this is bold**", ["markup.bold.example"]),
            ExpectedToken("<sometag>", ["entity.name.tag.example"]));
    }

    [Fact]
    public void given_combined_grammar_and_customized_TM_scopes_including_implicit_tokens_ー_generated_TM_grammar_tokenizes_correctly()
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
                SyntaxHighlighting = new() {
                    RuleSettings = new Dictionary<string, RuleOptions>() {
                        ["TAG"] = new() { TextMateScopeName = "entity.name.tag" },
                        ["BOLD"] = new() { TextMateScopeName = "markup.bold" },
                        ["'if'"] = new() { TextMateScopeName = "keyword.control.if" },
                        ["':'"] = new() { TextMateScopeName = "punctuation.colon" },
                    }
                }
            });
        const string input = "<script> if a: **b**";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeStringFused(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("<script>", ["entity.name.tag.example"]),
            ExpectedToken("if", ["keyword.control.if.example"]),
            ExpectedToken("a", ["variable.id.example"]),
            ExpectedToken(":", ["punctuation.colon.example"]),
            ExpectedToken("**b**", ["markup.bold.example"]));
    }

    [Fact]
    public void given_RuleConflicts_setting_ー_generated_TM_grammar_correctly_picks_the_longer_match()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            grammar ExampleLexer;
            HThree : '#' [0-9] [0-9] [0-9] ;
            HNums : '#' [0-9]+ ;
            FLOAT : NUM '.' NUM ;
            VERSION : NUM | NUM '.' NUM '.' NUM ;
            fragment NUM : [0-9]+ ;
            WS : [ \t\r\n] -> channel(HIDDEN) ;
            """,
            config: new() {
                SyntaxHighlighting = new() {
                    RuleConflicts = [
                        new(["HThree", "HNums"]),
                        new(["FLOAT", "VERSION"]),
                    ]
                }
            });
        const string input = """
            #01  #012  #0123  #01234
            1.2.3  1.2  1  2.3
            """;
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("#01", ["other.hnums.example"]),
            ExpectedToken("#012", ["other.hthree.example"]),
            ExpectedToken("#0123", ["other.hnums.example"]),
            ExpectedToken("#01234", ["other.hnums.example"]),
            ExpectedToken("1.2.3", ["other.version.example"]),
            ExpectedToken("1.2", ["constant.numeric.float.example"]),
            ExpectedToken("1", ["other.version.example"]),
            ExpectedToken("2.3", ["constant.numeric.float.example"]));
    }

    [Fact]
    public void given_recursive_rules_ー_ignores_them()
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
            ExpectedToken("// comment here", ["comment.comment.example"]));
    }

    [Fact]
    public void given_grammar_where_naive_approach_would_not_work_ー_tokenizes_like_ANTLR()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar("""
            lexer grammar ExampleLexer;
            ID : [a-zA-Z]+ ;
            DIV : '/' ;
            AND : '/\\' ;
            SET_CMD : '/set' ;
            INT : [0-9]+ ;
            FLOAT : [0-9]+ '.' [0-9]+ ;
            ACCESS : ('read.' | 'read.write.' | 'read.only.') ;
            COMMENT: '//' ~[\r\n]* -> skip ;
            """);
        // a naive translation (without e.g. reordering) into a TextMate grammar would lead to
        // incorrect matches: [`/` (DIV), `div` (skipped), `read.` (ACCESS), `write.` (skipped),
        //                     `x` (ID), `21` (INT), `.` (skipped), `0` (INT)]
        // instead of         [`/set` (SET_CMD), `read.write.` (ACCESS), `x` (ID), `21.0` (FLOAT)]
        const string input = """
            abc // comment here
            /set read.write. x  21.0
            /set read.only.  y  42 / x
            /\
            """;
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeString(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("abc", ["variable.id.example"]),
            ExpectedToken("// comment here", ["comment.comment.example"]),
            ExpectedToken("/set", ["keyword.set_cmd.example"]),
            ExpectedToken("read.write.", ["keyword.access.example"]),
            ExpectedToken("x", ["variable.id.example"]),
            ExpectedToken("21.0", ["constant.numeric.float.example"]),
            ExpectedToken("/set", ["keyword.set_cmd.example"]),
            ExpectedToken("read.only.", ["keyword.access.example"]),
            ExpectedToken("y", ["variable.id.example"]),
            ExpectedToken("42", ["constant.numeric.int.example"]),
            ExpectedToken("/", ["other.div.example"]),
            ExpectedToken("x", ["variable.id.example"]),
            ExpectedToken(@"/\", ["other.and.example"]));
    }

    [Fact]
    public void given_grammar_with_lexer_rules_matching_multiple_lines_ー_matches_full_tokens()
    {
        (TmLanguageGenerator g, _) = GetTmLanguageGeneratorForGrammar(""""
            lexer grammar ExampleLexer;
            ID : [a-zA-Z]+ ;
            DIV : '/' ;
            CMD : '/set' | '/print' ;
            INT : [0-9]+ ;
            FLOAT : [0-9]+ '.' [0-9]+ ;
            STRING : '"' ~["\r\n]* '"'
                   | '"""' STRCHAR*? '"""'
                   ;
            fragment STRCHAR : ('\\' [rnt\"] | .) ;
            RAW_STRING : '[[' .*? ']]' ;
            // a basic block-comment rule (without support for nested comments)
            BLOCK_COMMENT: '/*' .*? '*/' -> skip ;
            """");

        const string input = """"
            abc /* comment 0 /*
            x 66.0 """bbcc""" /*
            """ */
            y 2.0 / x """line "one"
            line /*two*/""" 21.0 y 42 /**/
            /print [[
                line /*one*/
                line "two"
              ]] end
            """";
        string generatedTextMateGrammar = g.GenerateTextMateLanguageJson();
        var tokens = TokenizeStringFused(generatedTextMateGrammar, "source.example", input);
        Assert.Collection(tokens,
            ExpectedToken("abc", ["variable.id.example"]),
            ExpectedToken(""""
                /* comment 0 /*
                x 66.0 """bbcc""" /*
                """ */
                """", ["comment.block_comment.example"]),
            ExpectedToken("y", ["variable.id.example"]),
            ExpectedToken("2.0", ["constant.numeric.float.example"]),
            ExpectedToken("/", ["other.div.example"]),
            ExpectedToken("x", ["variable.id.example"]),
            ExpectedToken(""""
                """line "one"
                line /*two*/"""
                """", ["string.string.example"]),
            ExpectedToken("21.0", ["constant.numeric.float.example"]),
            ExpectedToken("y", ["variable.id.example"]),
            ExpectedToken("42", ["constant.numeric.int.example"]),
            ExpectedToken("/**/", ["comment.block_comment.example"]),
            ExpectedToken("/print", ["keyword.cmd.example"]),
            ExpectedToken(""""
                [[
                    line /*one*/
                    line "two"
                  ]]
                """", ["string.raw_string.example"]),
            ExpectedToken("end", ["variable.id.example"]));
    }

    Action<TokenInfo> ExpectedToken(string text, string[] scopes) => token => {
        Assert.Equal(text, token.Text);
        Assert.Equal(scopes, token.Scopes);
    };

    // merges adjacent tokens with the same scopes
    IEnumerable<TokenInfo> TokenizeStringFused(
        string tmLanguageJson, string initialScopeName, string input)
    {
        TokenInfo? prevToken = null;

        foreach (var token in TokenizeString(tmLanguageJson, initialScopeName, input))
        {
            if (prevToken is null)
            {
                prevToken = token;
            }
            else if (prevToken.Value.Scopes.SequenceEqual(token.Scopes)) // if they can be fused
            {
                prevToken = token with {
                    Text = prevToken.Value.Text +
                        (token.Line != prevToken.Value.Line ? ("\n" + token.Text) : token.Text)
                };
            }
            else
            {
                yield return prevToken.Value;
                prevToken = token;
            }
        }
        if (prevToken is not null)
            yield return prevToken.Value;
    }

    record struct TokenInfo(string Text, IReadOnlyList<string> Scopes, int Line)
    {
        public override readonly string ToString() => $"`{Text}` ({Scopes.MakeString(", ")})";
    }

    // based on TextMateSharp demo code
    IEnumerable<TokenInfo> TokenizeString(
        string tmLanguageJson, string initialScopeName, string input)
    {
        testOutput.WriteLine("Generated TextMate grammar:");
        testOutput.WriteLine(tmLanguageJson);

        Registry registry = new(new SingleGrammarRegistryOptions(tmLanguageJson, initialScopeName));
        var grammar = registry.LoadGrammar(initialScopeName);

        IStateStack? ruleStack = null;

        foreach (var (line, lineIndex) in input.Split(["\r\n", "\n"], default).Select((l, i) => (l, i)))
        {
            ITokenizeLineResult result = grammar.TokenizeLine(line, ruleStack, TimeSpan.MaxValue);

            ruleStack = result.RuleStack;

            foreach (IToken token in result.Tokens)
            {
                var nonDefaultScopes = token.Scopes.Except([initialScopeName]).ToList();
                if (nonDefaultScopes.Count is 0)
                    continue;

                int startIndex = int.Min(token.StartIndex, line.Length);
                int endIndex = int.Min(token.EndIndex, line.Length);
                yield return new TokenInfo(line[startIndex..endIndex], nonDefaultScopes, lineIndex);
            }
        }
    }

    (TmLanguageGenerator, Antlr4Ast.Grammar) GetTmLanguageGeneratorForGrammar(
        string grammarCode, Configuration? config = null,
        Action<Diagnostic>? diagnosticHandler = null)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        var generator = TmLanguageGenerator.FromConfig(
            config ?? new(), grammar, diagnosticHandler ?? defaultHandleDiagnostic);
        return (generator, grammar);

        void defaultHandleDiagnostic(Diagnostic d)
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
