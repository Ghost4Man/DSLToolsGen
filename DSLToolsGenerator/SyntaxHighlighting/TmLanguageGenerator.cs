using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Antlr4Ast;

namespace DSLToolsGenerator.SyntaxHighlighting;

public partial class TmLanguageGenerator
{
    public required Grammar Grammar { get; init; }
    public required Action<Diagnostic> DiagnosticHandler { get; init; }
    public required HyphenDotIdentifierString LanguageId { get; init; }
    public required string LanguageDisplayName { get; init; }
    public required IReadOnlyList<RuleConflict> RuleConflicts { get; init; }
    public required IReadOnlyDictionary<string, RuleOptions>? RuleSettings { get; init; }

    Dictionary<Rule, bool> ruleIsKeywordCache = new();
    Dictionary<Rule, bool> ruleIsAllowedInKeywordCache = new();

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
        string lowercaseLanguageId = LanguageId.Value.ToLowerInvariant();

        IEnumerable<Rule> implicitTokenRules = Grammar.GetImplicitTokenRules();

        var rulesToHighlight = implicitTokenRules
            .Concat(Grammar.LexerRules.Where(ShouldHighlight));

        List<Pattern> patterns = [];

        foreach (RuleConflict conflict in RuleConflicts)
        {
            if (conflict.RuleNames is not [string ruleName1, string ruleName2])
            {
                DiagnosticHandler(new(DiagnosticSeverity.Error, "config option " +
                    $"{nameof(RuleConflicts)}.{nameof(conflict.RuleNames)}" +
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
                    ["5"] = new Capture(getFullScopeNameForRule(rule1), null),
                    ["6"] = new Capture(getFullScopeNameForRule(rule2), null),
                }
            ));

            string lookahead(Rule rule, string groupName)
                => $@"(?=(?<{groupName}>{MakeRegex(rule, parentRules: [])})(?<rest{groupName}>.*)$)";
        }

        // try to reorder the rules to fix the mismatch between
        // ANTLR's longest-match and TextMate's leftmost/eager match behavior
        patterns.AddRange(rulesToHighlight
            .Select(r => (
                pattern: new Pattern(
                    Comment: $"rule {r.Name}",
                    Match: MakeRegex(r, parentRules: []),
                    Name: getFullScopeNameForRule(r)),
                priority: GetRulePriority(r)
            ))
            .OrderByDescending(p => p.priority)
            .ThenByDescending(p => p.pattern.Match?.Length)
            .Select(p => p.pattern));

        ruleIsAllowedInKeywordCache.Clear();
        ruleIsKeywordCache.Clear();

        return new TmLanguageDocument(
            Name: LanguageDisplayName,
            ScopeName: $"source.{lowercaseLanguageId}",
            Patterns: patterns
        );

        string getFullScopeNameForRule(Rule r)
            => $"{GetScopeNameForRule(r)}.{lowercaseLanguageId}";
    }

    // priority heuristic: the higher the number, the sooner this rule will
    // try to match the input (when tokenizing using a TextMate-like engine)
    int GetRulePriority(Rule rule)
    {
        // try all keyword rules first (we assume they can't match more
        // than they are supposed to, since they have word boundary anchors)
        if (RuleIsKeyword(rule))
            return int.MaxValue;

        // more complex rules have higher priority
        return rule.GetAllDescendants().Count();
    }

    Rule? FindRuleOrError(string ruleName)
    {
        if (!Grammar.TryGetRule(ruleName, out Rule? rule))
        {
            DiagnosticHandler(new(DiagnosticSeverity.Error,
                $"could not find a rule named '{ruleName}'"));
        }
        return rule;
    }

    [GeneratedRegex("""^(?:(ID(ENT(IFIER)?)?|NAME|VAR(IABLE)?(_?NAME)?|.+_NAME|KEY|PROP(ERTY)?))$""", RegexOptions.IgnoreCase)]
    private partial Regex VariablePattern();

    [GeneratedRegex("""^(?:(INT(EGER)?|DECIMAL|FLOAT(ING_?POINT)?|REAL|HEX(ADECIMAL)?|NUM(BER|ERIC)?)(_?LIT(ERAL)?)?)$""", RegexOptions.IgnoreCase)]
    private partial Regex NumericLiteralPattern();

    [GeneratedRegex("""(?<!NON_?)COMMENT$""", RegexOptions.IgnoreCase)]
    private partial Regex CommentPattern();

    [GeneratedRegex("""^(?:((.+_|RAW)?STR(ING)?|TE?XT|CHR|CHAR(ACTER)?)(_?(LIT(ERAL)?|CONST(ANT)?))?)$""", RegexOptions.IgnoreCase)]
    private partial Regex StringLiteralPattern();

    string GetScopeNameForRule(Rule rule)
    {
        if (RuleSettings?.GetValueOrDefault(rule.Name)
                is { TextMateScopeName: string scopeName })
            return scopeName;

        var lowercaseRuleName = rule.Name.ToLowerInvariant();

        // if this is an implicit (keyword) token rule, trim the quotes
        if (lowercaseRuleName is ['\'', .. string literalText, '\'']
                && literalText.All(char.IsLetterOrDigit))
            lowercaseRuleName = literalText;

        string ruleScopeSuffix = lowercaseRuleName.Replace(' ', '_');

        var trimmedName = rule.Name.AsSpan().Trim('_');
        return rule.Name switch {
            _ when RuleIsKeyword(rule) => $"keyword.{ruleScopeSuffix}",
            _ when VariablePattern().IsMatch(trimmedName) => $"variable.{ruleScopeSuffix}",
            _ when NumericLiteralPattern().IsMatch(trimmedName) => $"constant.numeric.{ruleScopeSuffix}",
            _ when CommentPattern().IsMatch(trimmedName) => $"comment.{ruleScopeSuffix}",
            _ when StringLiteralPattern().IsMatch(trimmedName) => $"string.{ruleScopeSuffix}",
            _ => $"other.{ruleScopeSuffix}",
        };
    }

    internal bool RuleIsKeyword(Rule rule)
    {
        return ruleIsKeyword(rule);

        // Consider a rule to represent (match) a keyword iff
        // all of its (non-null) alts are "keyword-like"
        bool ruleIsKeyword(Rule rule)
            => ruleIsKeywordCache.GetValueOrInitialize(rule,
                _ => rule.GetAlts().All(altIsKeywordlike));

        bool altIsKeywordlike(Alternative alt)
            => alt.Elements.Any(containsALetter)
            && alt.Elements.All(isAllowedInKeyword);

        // we only consider "static" letters, not ranges, sets, negations...
        bool containsALetter(SyntaxElement element) => element switch {
            { IsNot: true } => false,
            TokenRef r when r.GetRuleOrNull(Grammar) is Rule rule
                => ruleIsKeyword(rule),
            // `(At 'foo' 'bar' | '!important)`
            Block b => b.Items.WhereNotNull().All(altIsKeywordlike),
            Literal(string value)
                => value.Any(char.IsLetter) || value.Contains('_'),
            var e when MatchesSingleLetter(e) => true,
            _ => false
        };

        // e.g. AT in rule `IMPORT : AT 'import' ;`
        bool ruleIsAllowedInKeyword(Rule rule)
            => ruleIsAllowedInKeywordCache.GetValueOrInitialize(rule,
                _ => ruleIsKeyword(rule) || rule.GetAlts().All(altIsAllowedInKeyword));

        bool altIsAllowedInKeyword(Alternative alt)
            => alt.Elements.All(isAllowedInKeyword);

        bool isAllowedInKeyword(SyntaxElement element) => element switch {
            { IsNot: true } => false, // negations are not allowed
            TokenRef r when r.GetRuleOrNull(Grammar) is Rule rule
                => ruleIsAllowedInKeyword(rule),
            Literal => true,
            _ => containsALetter(element),
        };
    }

    private enum Side { Start, End }

    // Determines whether we can use a word boundary anchor
    // (`\b`) in the pattern for this rule
    bool CanUseWordBoundaryAnchor(Rule rule, Side side)
    {
        HashSet<Rule> visitedRules = [];
        return startsOrEndsWithLetter(rule.AlternativeList);

        // "letter" includes the underscore
        bool startsOrEndsWithLetter(SyntaxNode node) => node switch {
            Rule r => visitedRules.Add(r)
                ? startsOrEndsWithLetter(r.AlternativeList)
                : false, // already visited before -> cycle
            TokenRef r when r.GetRuleOrNull(Grammar) is Rule rule
                => startsOrEndsWithLetter(rule),
            AlternativeList altList => altList.Items
                .All(alt => startsOrEndsWithLetter(alt)),
            Alternative([var first, ..]) when side is Side.Start
                => startsOrEndsWithLetter(first),
            Alternative([.., var last]) when side is Side.End
                => startsOrEndsWithLetter(last),
            Literal([char first, ..]) when side is Side.Start
                => (char.IsLetter(first) || first == '_'),
            Literal([.., char last]) when side is Side.End
                => (char.IsLetter(last) || last == '_'),
            SyntaxElement e when MatchesSingleLetter(e) => true,
            _ => false
        };
    }

    bool MatchesSingleLetter(SyntaxElement element)
    {
        return element switch {
            Literal([char c]) => char.IsLetter(c) || c == '_',
            // letters in case-insensitive keyword rules: [Ss][Ee][Tt]
            LexerCharSet { Value: [char c1, char c2] }
                => areTheSameLetter(c1, c2),
            // letters in case-insensitive keyword rules: ('S'|'s')('E'|'e')('T'|'t')
            LexerBlock { Items: [Literal([char c1]), Literal([char c2])] }
                => areTheSameLetter(c1, c2),
            _ => false,
        };

        static bool areTheSameLetter(char c1, char c2)
            => char.IsLetter(c1)
            && char.ToUpperInvariant(c1) == char.ToUpperInvariant(c2);
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
    internal string MakeRegex(Rule rule, IEnumerable<Rule> parentRules)
    {
        // first check if we're in a cycle
        if (parentRules.Contains(rule))
        {
            return RegexWarningComment(
                $"recursive lexer rules ({rule.Name}) are currently not supported",
                emitWarning: true, forceFail: true);
        }

        Rule? parentRule = parentRules.LastOrDefault();
        bool standalone = parentRule is null;
        string altRegex = RegexForAltList(rule.AlternativeList, parentRules.Append(rule));
        var block = $"(?{getGroupOptions()}:{altRegex})";

        if (standalone && RuleIsKeyword(rule))
        {
            return (CanUseWordBoundaryAnchor(rule, Side.Start) ? @"\b" : "") + block +
                (CanUseWordBoundaryAnchor(rule, Side.End) ? @"\b" : "");
        }
        else
            return block;

        string getGroupOptions()
        {
            bool inheritedCaseInsensitivity =
                parentRule?.GetCaseInsensitivity(Grammar, parentRule)
                ?? false; // ANTLR4 default is caseInsensitivity=false
            bool? ruleCaseInsensitivity = rule.GetCaseInsensitivity(Grammar, parentRule);
            return (inheritedCaseInsensitivity, ruleCaseInsensitivity) switch {
                (false, true) => "i",
                (true, false) => "-i",
                _ => ""
            };
        }
    }

    internal string MakeRegex(Antlr4Ast.SyntaxNode node, IEnumerable<Rule> parentRules)
    {
        string regex = node switch {
            Alternative a => a.Elements.Select(c => MakeRegex(c, parentRules)).MakeString(),
            Block b => $"(?:{RegexForAltList(b, parentRules)})",
            LexerBlock b => // called SetAST in the ANTLR java tool
                CombineSets( // ANTLR only supports single-character literals and character sets
                    b.Items.Select(body => body switch {
                        Literal(string c) => $"[{EscapeAsRegex(c)}]",
                        _ => MakeRegex(body, parentRules)
                    }))
                .ReplaceFirst("[", b.IsNot ? "[^" : "["),
            CharRange r => $"[{r.From}-{r.To}]", //$"[\\u{a:04X}-\\u{b:04X}]",
            DotElement => ".",
            TokenRef(string name) tr =>
                tr.GetRuleOrNull(Grammar) is Rule r ? MakeRegex(r, parentRules) :
                name is "EOF" ? @"\z" :
                RegexWarningComment($"unknown ref {name} at line {tr.Span.Begin.Line}"),
            Literal(string value) { IsNot: true } => $"[^{EscapeAsRegex(value)}]",
            Literal(string value) => EscapeAsRegex(value),
            LexerCharSet(string chars) set =>
                $"{(set.IsNot ? "[^" : "[")}{EscapeAsRegex(chars, "-")}]",
            _ => RegexWarningComment($"unknown node: {node.GetType().Name} {node}"),
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
    }

    string RegexForAltList(AlternativeList altList, IEnumerable<Rule> parentRules)
    {
        var reorderingMode = parentRules
            .Select(r => RuleSettings?.GetValueOrDefault(r.Name)?.AltReordering)
            .WhereNotNull()
            .LastOrDefault(RuleOptions.DefaultAltReorderingMode);

        var alts = altList.Items.WhereNotNull();
        bool shouldReorderAlts = reorderingMode switch {
            AltReorderingMode.ByPatternLength => true,
            AltReorderingMode.LiteralsOnly => allAltsAreLiteralsOnly(alts),
            _ => false,
        };

        var altPatterns = alts.Select(a => MakeRegex(a, parentRules));
        return (shouldReorderAlts
                ? altPatterns.OrderByDescending(a => a.Length)
                : altPatterns)
            .MakeString("|");

        bool allAltsAreLiteralsOnly(IEnumerable<Alternative> alts)
            => alts.All(a =>
                a.Elements.All(e => e is Literal || (e is TokenRef tr
                    && tr.GetRuleOrNull(Grammar) is Rule r
                    && allAltsAreLiteralsOnly(r.GetAlts()))));
    }

    string RegexWarningComment(string text, bool emitWarning = true, bool forceFail = false)
    {
        if (emitWarning)
            DiagnosticHandler(new(DiagnosticSeverity.Warning, text));

        // we need to escape/replace the parentheses (since they end the comment)
        string commentText = text.Replace('(', '{').Replace(')', '}');

        return $"(?# {commentText} )" + (forceFail ? "((?!))" : "");
    }

    string EscapeAsRegex(string text, string charsToKeepUnescaped = "") =>
        RegexControlCharactersPattern()
            .Replace(text, m => m.Groups[1].Value switch {
                [char c] when charsToKeepUnescaped.Contains(c) => c.ToString(),
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

    public static TmLanguageGenerator FromConfig(
        Configuration config, Grammar grammar, Action<Diagnostic> diagnosticHandler)
    {
        var languageId = config.LanguageId ?? config.GetFallbackLanguageId(grammar);

        if (grammar.LexerRules.Count == 0)
        {
            diagnosticHandler(new(DiagnosticSeverity.Warning,
                "no lexer rules found"));
        }

        return new TmLanguageGenerator {
            Grammar = grammar,
            DiagnosticHandler = diagnosticHandler,
            RuleConflicts = config.SyntaxHighlighting.RuleConflicts,
            RuleSettings = config.SyntaxHighlighting.RuleSettings,
            LanguageId = languageId,
            LanguageDisplayName = config.LanguageDisplayName ?? languageId.Value,
        };
    }
}
