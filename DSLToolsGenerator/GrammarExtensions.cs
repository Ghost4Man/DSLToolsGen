using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

using Antlr4Ast;

namespace DSLToolsGenerator;

file class GrammarAttachedData
{
    // essentially a memory-leak-safe dictionary for attaching custom fields to a Grammar
    public static readonly ConditionalWeakTable<Grammar, GrammarAttachedData> Storage = new();

    public string? ParserClassName { get; set; }

    public Dictionary<string, Rule> LexerRulesByLiteral { get; set; } = new();
}

file class SyntaxElementAttachedData
{
    public static readonly ConditionalWeakTable<SyntaxElement, SyntaxElementAttachedData> Storage = new();

    public ElementIndexInfo? Index { get; set; }

    public bool? IsOnlyOfType { get; set; }
}

/// <summary>
/// Stores the indices of this element within the enclosing ANTLR rule context.
/// For example, for an <c>ID</c> token,
/// <c>IndexByType</c> says that it is the nth <c>ID</c> token in the parse tree
/// and <c>ChildIndex</c> says that it is the nth child node of the parse tree.
/// </summary>
public record struct ElementIndexInfo(int? IndexByType, int? ChildIndex);

public record ResolvedTokenRef(string? Name, Literal? Literal, Rule? LexerRule);

static class GrammarExtensions
{
    /// <summary>
    /// Computes data for certain extension methods.
    /// Must be called before changing the <see cref="Grammar.Kind"/>.
    /// </summary>
    public static void Analyze(this Grammar grammar)
    {
        GrammarAttachedData.Storage.AddOrUpdate(grammar, new() {
            // based on Grammar.getRecognizerName method in the ANTLR4 tool
            ParserClassName = grammar.Kind == GrammarKind.Full
                ? grammar.Name + "Parser"
                : grammar.Name,
        });

        grammar.ParserRules.ForEach(AssignElementIndices);
    }

    record struct IndexCounterState(
        // It is not always possible to assign an unambiguous index
        // to an element (e.g. after a repeated (star) block)
        (int count, bool ambiguous) ChildCounter,
        ImmutableDictionary<string, (int count, bool ambiguous)> CounterPerTokenOrRuleType)
    {
        public static IndexCounterState Combine(IEnumerable<IndexCounterState> states)
        {
            var tokenOrRuleTypes = states
                .SelectMany(s => s.CounterPerTokenOrRuleType.Keys)
                .Distinct();

            (int count, bool ambiguous) value = default; // workaround (cannot use out param from select)
            return new IndexCounterState(
                ChildCounter: combine(states.Select(s => s.ChildCounter)),
                CounterPerTokenOrRuleType:
                    tokenOrRuleTypes.ToImmutableDictionary(t => t, t => combine(
                        // Compute agreement only among those states (paths)
                        // that have an index for that token/rule type
                        from state in states
                        where state.CounterPerTokenOrRuleType.TryGetValue(t, out value)
                        select value
                    )));

            static (int count, bool ambiguous) combine(IEnumerable<(int count, bool ambiguous)> votes)
            {
                int? countAgreement = agreementOrNull(votes.Select(v => (int?)v.count));
                int count = countAgreement ?? votes.Max(v => v.count);
                bool ambiguous = countAgreement == null || votes.Any(v => v.ambiguous);
                return (count, ambiguous);
            }

            // [1] -> 1
            // [2,2,2,2] -> 2
            // [1,2] -> null
            // [null] -> null
            // [1,null,1] -> null
            static int? agreementOrNull(IEnumerable<int?> votes)
                => votes.Distinct().SingleOrDefaultIfMore();
        }
    }

    static void AssignElementIndices(Rule parserRule)
    {
        var emptyState = new IndexCounterState((0, ambiguous: false),
            ImmutableDictionary<string, (int count, bool ambiguous)>.Empty);

        var alts = parserRule.AlternativeList.Items;
        if (alts is [Alternative { ParserLabel: not null }, ..])
        {
            // For rules with labeled alternatives,
            // each alternative gets its own ParserRuleContext, so
            // the "only-ness" is considered per-alternative, not per-rule
            foreach (var alt in alts)
            {
                var state = emptyState;
                var endState = assignElementIndicesInAlt(alt, state);
                markSingletons(alt.GetAllDescendants(), endState);
            }
        }
        else
        {
            var state = emptyState;
            var endState = assignElementIndices(parserRule.AlternativeList, state);
            // for unlabeled alternatives, an element is a singleton iff
            // it's a singleton in all of the alternatives
            markSingletons(parserRule.GetAllDescendants(), endState);
        }

        static IndexCounterState assignElementIndicesInAlt(
            Alternative alt, IndexCounterState startState,
            bool parentIsOptional = false, bool parentIsRepeated = false)
        {
            return alt.Elements.Aggregate(startState, (s, el) =>
                assignElementIndices(el, s, parentIsOptional, parentIsRepeated));
        }

        static IndexCounterState assignElementIndices(
            SyntaxElement element, IndexCounterState startState,
            bool parentIsOptional = false, bool parentIsRepeated = false)
        {
            var state = startState;

            if (element is AlternativeList { Items: var alts })
            {
                return IndexCounterState.Combine(alts.Select(a =>
                    assignElementIndicesInAlt(a, state, element.IsOptional(), element.IsMany())));
            }

            if (element is EmptyElement)
                return state;

            if (element is not (Literal or TokenRef or RuleRef or DotElement))
            {
                Debug.Fail("encountered an unexpected grammar AST node type: " +
                    element.GetType().Name);
                return state;
            }

            string? refName = getRefName(element);
            (int count, bool ambiguous) childCounter = state.ChildCounter;
            (int count, bool ambiguous) counterPerType = state.CounterPerTokenOrRuleType
                .GetValueOrDefault(refName ?? "", (0, ambiguous: false));
            ElementIndexInfo newIndex;
            newIndex = new(toIndex(counterPerType), toIndex(childCounter));

            if (element.IsOptional() || parentIsOptional)
            {
                childCounter.ambiguous = true;
                counterPerType.ambiguous = true;
            }
            else if (element.IsMany() || parentIsRepeated)
            {
                newIndex = new(null, null);
                childCounter = (int.MaxValue, ambiguous: true);
                counterPerType = (int.MaxValue, ambiguous: true);
            }

            setElementIndex(element, newIndex);
            tryIncrement(ref childCounter.count);
            tryIncrement(ref counterPerType.count);

            state.ChildCounter = childCounter;
            if (refName is not null)
            {
                state.CounterPerTokenOrRuleType =
                    state.CounterPerTokenOrRuleType.SetItem(refName, counterPerType);
            }

            return state;

            static int? toIndex((int count, bool ambiguous) counter)
                => counter.ambiguous ? null : counter.count;

            static void tryIncrement(ref int counter)
            {
                if (counter != int.MaxValue)
                    counter++;
            }
        }

        static void markSingletons(IEnumerable<SyntaxNode> nodes, IndexCounterState endState)
        {
            var singletons = nodes.OfType<SyntaxElement>()
                .Where(n => getRefName(n) is string refName
                            && endState.CounterPerTokenOrRuleType[refName].count == 1)
                .WhereNotNull();

            foreach (var singleton in singletons)
            {
                var elementData = SyntaxElementAttachedData.Storage.GetOrCreateValue(singleton);
                elementData.IsOnlyOfType = true;
            }
        }

        static string? getRefName(SyntaxNode node)
            => (node as RuleRef)?.Name ?? (node as TokenRef)?.Name;

        static void setElementIndex(SyntaxElement element, ElementIndexInfo index)
        {
            var elementData = SyntaxElementAttachedData.Storage.GetOrCreateValue(element);
            elementData.Index = index;
            elementData.IsOnlyOfType = false;

            foreach (var descendant in element.GetAllDescendants().OfType<SyntaxElement>())
            {
                setElementIndex(descendant, default);
            }
        }
    }

    public static string GetParserClassName(this Grammar grammar)
    {
        // this cannot be computed on the fly because the grammar's Kind
        // might have changed after importing the lexer grammar

        GrammarAttachedData.Storage.TryGetValue(grammar, out GrammarAttachedData? grammarData);
        if (grammarData?.ParserClassName is not string parserClassName)
        {
            throw new InvalidOperationException(
                "this extension method requires first calling Analyze on the Grammar");
        }

        return parserClassName;
    }

    public static IEnumerable<(Rule Rule, Literal Literal)> GetSingleTokenLexerRules(this Grammar grammar)
    {
        foreach (var r in grammar.LexerRules)
        {
            if (r.AlternativeList.Items is [Alternative { Elements: [Literal literal] }])
                yield return (r, literal);
        }
    }

    // In a combined grammar, ANTLR creates implicit lexer rules for all literals
    // without a corresponding lexer rule that matches only that literal.
    // e.g. for a parser rule `stmt : await_kw='await'? expr ;`, an implicit rule
    // like `AWAIT : 'await' ;` (but unnamed) is generated
    public static IEnumerable<Rule> GetImplicitTokenRules(this Grammar grammar)
    {
        var literalsWithCorrespondingLexerRule =
            grammar.GetSingleTokenLexerRules().Select(r => r.Literal);

        // find literals without corresponding (single-token) lexer rules
        IEnumerable<Literal> implicitTokenLiterals = grammar.ParserRules
            .SelectMany(r => r.GetAllDescendants().OfType<Literal>())
            .Except(literalsWithCorrespondingLexerRule);

        return implicitTokenLiterals.Select(createSingleTokenLexerRule);

        static Rule createSingleTokenLexerRule(Literal originalLiteral)
        {
            // we have to create a copy without the suffix (quantity), label, etc.
            Literal literal = new(originalLiteral.Text) { Span = originalLiteral.Span };
            return new Rule(name: $"'{literal.Text}'",
                new() { Items = { new() { Elements = { literal } } } });
        }
    }

    [return: NotNullIfNotNull(nameof(grammarFileName))]
    public static string? GetLanguageName(this Grammar grammar, string? grammarFileName = null)
    {
        string? name = grammar.Name ?? Path.GetFileNameWithoutExtension(grammarFileName);
        return name is not (null or "")
            ? name.TrimSuffix("Lexer").TrimSuffix("Parser")
            : null;
    }
}

static class RuleRefExtensions
{
    public static Rule? GetRuleOrNull(this RuleRef ruleRef, Grammar grammar)
        => grammar.TryGetRule(ruleRef.Name, out Rule? rule) ? rule : null;
}

static class RuleExtensions
{
    public static bool? GetCaseInsensitivity(this Rule rule, Grammar grammar, Rule? parentRule)
    {
        bool? grammarCaseInsensitivity = grammar.Options.FindBool("caseInsensitive");
        bool? parentRuleCaseInsensitivity = parentRule?.Options.FindBool("caseInsensitive");
        bool? ruleCaseInsensitivity = rule.Options.FindBool("caseInsensitive");
        return ruleCaseInsensitivity ?? parentRuleCaseInsensitivity ?? grammarCaseInsensitivity;
    }
}

static class OptionListExtensions
{
    public static OptionSpec? Find(this IEnumerable<OptionSpecList> optionLists, string optionName)
    {
        foreach (var optionList in optionLists)
        {
            if (optionList.Items.LastOrDefault(o => o.Name == optionName) is OptionSpec option)
                return option;
        }
        return null;
    }

    public static bool? FindBool(this IEnumerable<OptionSpecList> optionLists, string optionName)
    {
        return Find(optionLists, optionName)?.Value is string value
            ? value is "true" or "True"
            : null;
    }
}

static class TokenRefExtensions
{
    public static Rule? GetRuleOrNull(this TokenRef tokenRef, Grammar grammar)
        => grammar.TryGetRule(tokenRef.Name, out Rule? rule) ? rule : null;
}

static class SyntaxNodeExtensions
{
    public static IEnumerable<SyntaxNode> GetAllDescendants(this SyntaxNode node)
        => node.Children().SelectMany(GetAllDescendantsAndSelf);

    public static IEnumerable<SyntaxNode> GetAllDescendantsAndSelf(this SyntaxNode node)
        => node.Children().SelectMany(GetAllDescendantsAndSelf).Prepend(node);
}

static class SyntaxElementExtensions
{
    public static bool IsMany(this SyntaxElement element)
        => element.Suffix is SuffixKind.Plus or SuffixKind.Star
            or SuffixKind.PlusNonGreedy or SuffixKind.StarNonGreedy;

    public static bool IsOptional(this SyntaxElement element)
        => element.Suffix is SuffixKind.Optional or SuffixKind.OptionalNonGreedy;

    /// <summary>
    /// Retrieves an index of this rule or token reference or literal
    /// within the enclosing ANTLR rule context.
    /// This index can be used to retrieve the corresponding parse tree node
    /// from an ANTLR parse tree.
    /// <para>
    ///   It is necessary to call
    ///   <see cref="GrammarExtensions.Analyze(Grammar)"/> on the grammar first.
    /// </para>
    /// </summary>
    public static ElementIndexInfo GetElementIndex(this SyntaxElement element)
    {
        SyntaxElementAttachedData.Storage.TryGetValue(element, out var elementData);
        return elementData?.Index
            ?? throw new InvalidOperationException(
                "this extension method requires first calling Analyze on the grammar");
    }

    /// <summary>
    /// Gets a value indicating whether the element is
    /// the only token/rule of its type in the rule context.
    /// <para>
    ///   It is necessary to call
    ///   <see cref="GrammarExtensions.Analyze(Grammar)"/> on the grammar first.
    /// </para>
    /// </summary>
    public static bool IsOnlyOfType(this SyntaxElement element)
    {
        SyntaxElementAttachedData.Storage.TryGetValue(element, out var elementData);
        return elementData?.IsOnlyOfType
            ?? throw new InvalidOperationException(
                "this extension method requires first calling Analyze on the grammar");
    }

    // Deconstruct extensions for pattern matching:

    public static void Deconstruct(this Block block, out IReadOnlyList<Alternative> alts) => alts = block.Items;
    public static void Deconstruct(this Alternative alt, out IReadOnlyList<SyntaxElement> elements) => elements = alt.Elements;
    public static void Deconstruct(this TokenRef tokenRef, out string name) => name = tokenRef.Name;
    public static void Deconstruct(this RuleRef ruleRef, out string name) => name = ruleRef.Name;
    public static void Deconstruct(this Literal literal, out string value) => value = literal.GetValue();
}

static class LiteralExtensions
{
    public static ResolvedTokenRef Resolve(this Literal literal, Grammar grammar)
    {
        var grammarData = GrammarAttachedData.Storage.GetOrCreateValue(grammar);
        grammarData.LexerRulesByLiteral ??= grammar.GetSingleTokenLexerRules()
            .ToDictionary(r => r.Literal.Text, r => r.Rule);

        Rule? lexerRule = grammarData.LexerRulesByLiteral.GetValueOrDefault(literal.Text);
        return new(lexerRule?.Name, literal, lexerRule);
    }

    /// <summary>
    /// Evaluates the contents of this literal including any escape
    /// sequences into the actual characters they represent.
    /// </summary>
    public static string GetValue(this Literal literal)
    {
        StringBuilder output = new(literal.Text.Length, literal.Text.Length);
        Span<char> buffer = stackalloc char[2];
        ReadOnlySpan<char> rest = literal.Text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (tryReplace(@"\n", '\n', ref rest)
                || tryReplace(@"\r", '\r', ref rest)
                || tryReplace(@"\t", '\t', ref rest)
                || tryReplace(@"\f", '\f', ref rest)
                || tryReplace(@"\b", '\b', ref rest)
                || tryReplace(@"\\", '\\', ref rest))
            { }
            else if (rest is ['\\', .. var esc])
            {
                if (esc is ['u', ..] && esc.Length >= 5 // e.g. "u0400"
                    && esc[1..5] is var hexCode
                    && int.TryParse(hexCode, NumberStyles.HexNumber, null, out int value))
                {
                    output.Append((char)value);
                    rest = esc[5..]; // esc="u12345", rest="5"
                }
                else if (esc is ['u', '{', .. var escRest] // e.g. "u{1F600}"
                    && escRest.TrySliceUntil("}", out var extHexCode)
                    && int.TryParse(extHexCode, NumberStyles.HexNumber, null, out value)
                    && Rune.TryCreate(value, out Rune rune)
                    && rune.EncodeToUtf16(buffer) is int charsWritten and (1 or 2))
                {
                    output.Append(buffer[..charsWritten]);
                    rest = escRest[(extHexCode.Length + 1)..];
                }
                else
                    rest = rest[1..];
            }
            else
            {
                output.Append(rest[0]);
                rest = rest[1..];
            }
        }

        return output.ToString();

        bool tryReplace(ReadOnlySpan<char> escapeSequence, char value, ref ReadOnlySpan<char> rest)
        {
            if (rest.StartsWith(escapeSequence))
            {
                output.Append(value);
                rest = rest[escapeSequence.Length..];
                return true;
            }
            return false;
        }
    }
}
