using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Antlr4Ast;

using DSLToolsGenerator.AST.Models;
using DSLToolsGenerator.SyntaxHighlighting.Models;

using GrammarAST = Antlr4Ast.SyntaxNode;

namespace DSLToolsGenerator.SyntaxHighlighting;

public partial class TmLanguageGenerator(
    Grammar grammar,
    Action<Diagnostic> diagnosticHandler,
    SyntaxHighlightingConfiguration config)
{
    public TmLanguageGenerator(Grammar grammar, Action<Diagnostic> diagnosticHandler)
        : this(grammar, diagnosticHandler, new()) { }

    readonly JsonSerializerOptions jsonOptions = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // by default, System.Text.Json escapes "unsafe" characters like '+' as \u002B...
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string GenerateTextMateLanguageJson()
        => JsonSerializer.Serialize(GenerateTextMateLanguage(), jsonOptions);

    public Task GenerateTextMateLanguageJsonAsync(Stream outputStream)
        => JsonSerializer.SerializeAsync(outputStream, GenerateTextMateLanguage(), jsonOptions);

    public TmLanguageDocument GenerateTextMateLanguage()
    {
        var languageName = grammar.Name is null or "" ? "untitled" : grammar.Name.TrimSuffix("Lexer");

        IEnumerable<Rule> implicitTokenRules = grammar.GetImplicitTokenRules();

        var rulesToHighlight = implicitTokenRules
            .Concat(grammar.LexerRules.Where(ShouldHighlight));

        // TODO: reorder and/or transform the rules to fix the mismatch between
        //       ANTLR's longest-match and TextMate's leftmost-match behavior

        List<Pattern> patterns = [];

        foreach (RuleConflict conflict in config.RuleConflicts)
        {
            if (conflict.RuleNames is not [string ruleName1, string ruleName2])
            {
                diagnosticHandler(new(DiagnosticSeverity.Error, "config option " +
                    $"{nameof(config.RuleConflicts)}.{nameof(conflict.RuleNames)}" +
                    " currently only supports 2 rules"));
                continue;
            }

            if (FindRuleOrError(ruleName1) is not Rule rule1
                || FindRuleOrError(ruleName2) is not Rule rule2)
                continue; // an error diagnostic was produced by `FindRuleOrError`

            patterns.Add(new Pattern(
                Comment: $"rule conflict ({rule1.Name}, {rule2.Name}) resolver pattern",
                Match: lookahead(rule1, "L1") + lookahead(rule2, "L2") +
                    @"(?:(?<L1match>\k<L1>)(?!.+?\k<restL2>$)|(?<L2match>\k<L2>))",
                Captures: new() {
                    ["5"] = new Capture(GetScopeNameForRule(rule1), null),
                    ["6"] = new Capture(GetScopeNameForRule(rule2), null),
                }
            ));

            string lookahead(Rule rule, string groupName)
                => $@"(?=(?<{groupName}>{MakeRegex(rule, parentRule: null)})(?<rest{groupName}>.*)$)";
        }

        patterns.AddRange(rulesToHighlight.Select(r => new Pattern(
            Comment: $"rule {r.Name}",
            Match: MakeRegex(r, parentRule: null),
            Name: $"{GetScopeNameForRule(r)}.{languageName.ToLowerInvariant()}")));

        return new TmLanguageDocument(
            Name: languageName,
            ScopeName: $"source.{languageName.ToLowerInvariant()}",
            Patterns: patterns
        );
    }

    Rule? FindRuleOrError(string ruleName)
    {
        if (!grammar.TryGetRule(ruleName, out Rule? rule))
        {
            diagnosticHandler(new(DiagnosticSeverity.Error,
                $"could not find a rule named '{ruleName}'"));
        }
        return rule;
    }

    [GeneratedRegex("""^(?:(ID(ENT(IFIER)?)?|NAME|VAR(IABLE)?(_?NAME)?|.+_NAME|KEY|PROP(ERTY)?))$""", RegexOptions.IgnoreCase)]
    private partial Regex VariablePattern();

    [GeneratedRegex("""^(?:(INT(EGER)?|DECIMAL|FLOAT(ING_?POINT)?|REAL|HEX(ADECIMAL)?|NUM(BER|ERIC)?)(_?LIT(ERAL)?)?)$""", RegexOptions.IgnoreCase)]
    private partial Regex NumericLiteralPattern();

    [GeneratedRegex("""^(?:((SINGLE_?)?LINE|SL)?_?COMMENT)$""", RegexOptions.IgnoreCase)]
    private partial Regex LineCommentPattern();

    [GeneratedRegex("""^(?:(STR(ING)?|TE?XT|CHR|CHAR(ACTER)?)(_?(LIT(ERAL)?|CONST(ANT)?))?)$""", RegexOptions.IgnoreCase)]
    private partial Regex StringLiteralPattern();

    string GetScopeNameForRule(Rule rule)
    {
        var lowercaseRuleName = rule.Name.ToLowerInvariant();

        // if this is an implicit (keyword) token rule, trim the quotes
        if (lowercaseRuleName is ['\'', .. string literalText, '\'']
                && literalText.All(char.IsLetterOrDigit))
            lowercaseRuleName = literalText;

        var trimmedName = rule.Name.AsSpan().Trim('_');
        return rule.Name switch {
            _ when RuleIsKeyword(rule) => $"keyword.{lowercaseRuleName}",
            _ when VariablePattern().IsMatch(trimmedName) => $"variable.{lowercaseRuleName}",
            _ when NumericLiteralPattern().IsMatch(trimmedName) => $"constant.numeric.{lowercaseRuleName}",
            _ when LineCommentPattern().IsMatch(trimmedName) => $"comment.line.{lowercaseRuleName}",
            _ when StringLiteralPattern().IsMatch(trimmedName) => $"string.{lowercaseRuleName}",
            _ => $"other.{lowercaseRuleName}",
        };
    }

    bool RuleIsKeyword(Rule rule)
    {
        var alts = rule.AlternativeList.Items.Where(x => x != null);
        // check whether all non-null alternatives are all alphanumeric literals
        var allTerminals = alts.SelectMany(a => a.Elements);
        return allTerminals.All(t => t switch {
            TokenRef r when r.GetRuleOrNull(grammar) is Rule rule => RuleIsKeyword(rule),
            Literal(string value) => KeywordlikePattern().IsMatch(value),
            _ => false
        });
    }

    // Whether to generate a syntax highlighting pattern for this lexer rule
    internal bool ShouldHighlight(Rule rule) =>
        !rule.IsFragment &&
        (rule.Name switch {
            "WS" or "WHITESPACE" => false,
            _ => true
        });

    /// <summary>
    /// Returns a Oniguruma regex pattern for matching the specified lexer rule.
    /// </summary>
    internal string MakeRegex(Rule rule, Rule? parentRule)
    {
        bool standalone = parentRule is null;
        var alts = rule.AlternativeList.Items.Where(x => x != null);
        var block = alts.Select(a => MakeRegex(a, rule))
            .MakeString($"(?{getGroupOptions()}:", "|", ")");
        return (standalone && RuleIsKeyword(rule))
            ? $@"\b{block}\b"
            : block;

        string getGroupOptions()
        {
            bool inheritedCaseInsensitivity =
                parentRule?.GetCaseInsensitivity(grammar, parentRule)
                ?? false; // ANTLR4 default is caseInsensitivity=false
            bool? ruleCaseInsensitivity = rule.GetCaseInsensitivity(grammar, parentRule);
            return (inheritedCaseInsensitivity, ruleCaseInsensitivity) switch {
                (false, true) => "i",
                (true, false) => "-i",
                _ => ""
            };
        }
    }

    internal string MakeRegex(GrammarAST node, Rule? parentRule)
    {
        string regex = node switch {
            Alternative a => a.Elements.Select(c => MakeRegex(c, parentRule)).MakeString(),
            Block b => $"(?:{b.Items.Select(c => MakeRegex(c, parentRule)).MakeString("|")})",
            LexerBlock b => // called SetAST in the ANTLR java tool
                CombineSets( // ANTLR only supports single-character literals and character sets
                    b.Items.Select(body => body switch {
                        Literal(string c) => $"[{EscapeAsRegex(c)}]",
                        _ => MakeRegex(body, parentRule)
                    }))
                .ReplaceFirst("[", b.IsNot ? "[^" : "["),
            CharRange r => $"[{r.From}-{r.To}]", //$"[\\u{a:04X}-\\u{b:04X}]",
            DotElement => ".",
            TokenRef(string name) tr =>
                tr.GetRuleOrNull(grammar) is Rule r ? MakeRegex(r, parentRule) :
                name is "EOF" ? @"\z" :
                regexInlineComment($"unknown ref {name} at line {tr.Span.Begin.Line}"),
            Literal(string value) { IsNot: true } => $"[^{EscapeAsRegex(value)}]",
            Literal(string value) => EscapeAsRegex(value),
            LexerCharSet set => $"[{(set.IsNot ? "^" : "")}{set.Value}]",
            //_ => throw new NotImplementedException($"{node.GetType().Name} {node}: not yet implemented"),
            _ => regexInlineComment($"unknown node: {node.GetType().Name} {node}"),
        };

        if (node is SyntaxElement { Suffix: var suffix and not SuffixKind.None })
        {
            // only enclose in (non-capturing) group if it's needed
            return node is Block or CharRange or LexerCharSet or LexerBlock or TokenRef
                    || regex is [_] or ['\\', _] // a+, .*?, \n+ etc.
                ? regex + suffix.ToText()
                : $"(?:{regex}){suffix.ToText()}";
        }
        else
            return regex;

        string regexInlineComment(string text, bool emitWarning = true)
        {
            if (emitWarning)
                diagnosticHandler(new(DiagnosticSeverity.Warning, text));

            return $"(?# {text} )";
        }
    }

    string EscapeAsRegex(string text) =>
        RegexControlCharactersPattern()
            .Replace(text, m => m.Groups[1].Value switch {
                "\n" => """\n""",
                "\r" => """\r""",
                "\f" => """\f""",
                "\t" => """\t""",
                string s => '\\' + s
            });

    // combine multiple set patterns (e.g. ["[a-z]", "[,.=]", "[\u0000-\uFFFF]"] into one
    string CombineSets(IEnumerable<string> setPatterns)
    {
        Debug.Assert(setPatterns.All(s => s.StartsWith('[') && s.EndsWith(']')));
        return setPatterns.MakeString().Replace("][", "");
        // this assumes that all square brackets are escaped, otherwise
        // for example "[\][]" and "[a-z]"" would be combined into "[\a-z]"
    }

    [GeneratedRegex(@"([\n\r\f\t(){}\[\]\-\\.?*+/|^$])")]
    private static partial Regex RegexControlCharactersPattern();

    [GeneratedRegex(@"^([a-zA-Z]|\w[\w\s\-]*[a-zA-Z][\w\s\-]*)$")]
    private static partial Regex KeywordlikePattern();
}
