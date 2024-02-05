using System.Diagnostics;
using System.Text.RegularExpressions;

using Antlr4Ast;
using Humanizer;

using DSLToolsGenerator.AST.Models;

namespace DSLToolsGenerator.AST;

public partial class AstCodeGenerator
{
    readonly Grammar grammar;
    readonly Action<Diagnostic> diagnosticHandler;
    readonly AstConfiguration config;
    readonly Dictionary<string, Rule> lexerRulesByLiteral;

    // Mapping from parser rules to codegen models of AST Node classes.
    // Does not include variants (derived classes) of (abstract) node classes
    // for rules with multiple alternatives.
    readonly Dictionary<Rule, NodeClassModel> nodeClassesByRule = new();

    public AstCodeGenerator(Grammar parserGrammar, Action<Diagnostic> diagnosticHandler, AstConfiguration config)
    {
        if (parserGrammar.Kind == GrammarKind.Lexer)
            throw new ArgumentException("cannot generate AST from a lexer grammar");

        this.grammar = parserGrammar;
        this.diagnosticHandler = diagnosticHandler;
        this.config = config;
        this.lexerRulesByLiteral = parserGrammar.GetSingleTokenLexerRules()
            .ToDictionary(r => r.Literal.Text, r => r.Rule);
    }

    public AstCodeModel GenerateAstCodeModel()
    {
        var nodeClasses = grammar.ParserRules.Select(FindOrGenerateAstNodeClass);
        var astBuilder = new AstBuilderModel(grammar.Name, grammar.GetParserClassName(),
            nodeClasses.SelectMany(getAstMappingModels).ToList());
        return new AstCodeModel(nodeClasses.ToList(), astBuilder);

        static IEnumerable<AstMappingModel> getAstMappingModels(NodeClassModel nodeClass) => [
            new AstMappingModel(nodeClass.ParserRule, nodeClass),
            .. nodeClass.Variants.SelectMany(getAstMappingModels)
        ];
    }

    NodeClassModel FindOrGenerateAstNodeClass(Rule parserRule)
    {
        return nodeClassesByRule.TryGetValue(parserRule, out var nodeClass)
            ? nodeClass ?? throw new UnreachableException(
                $"found unexpected rule reference cycle (involving rule '{parserRule.Name}')")
            : (nodeClassesByRule[parserRule] = generateAstNodeClass(parserRule));

        NodeClassModel generateAstNodeClass(Rule parserRule)
        {
            if (parserRule.IsLexer)
                throw new ArgumentException($"{parserRule} is not a parser rule!");

            // use null to mark this as "currently being generated" to prevent stack overflow
            nodeClassesByRule[parserRule] = null!;

            string className = GetGeneratedClassName(parserRule);
            List<Alternative> alts = parserRule.AlternativeList.Items;

            if (alts is [Alternative singleAlt])
            {
                return nodeClassModelForAlternative(parserRule, className, singleAlt);
            }
            else // generate derived record types for multi-alt rules
            {
                var altNames = autoNameAlternatives(parserRule);
                return new NodeClassModel(className, parserRule, SourceAlt: null, []) {
                    Variants = alts.Zip(altNames, (a, altName) => {
                        string variantClassName = ToPascalCase(altName);
                        return nodeClassModelForAlternative(parserRule, variantClassName, a);
                    }).ToList()
                };
            }

            NodeClassModel nodeClassModelForAlternative(Rule parserRule, string className, Alternative alt)
            {
                var parameters = GeneratePostprocessedPropertyListFor(alt);
                return new NodeClassModel(className, parserRule, alt, parameters);
            }

            IEnumerable<string> autoNameAlternatives(Rule parserRule)
            {
                var alts = parserRule.AlternativeList.Items;
                // if one alt has a label, all of the must have a label
                // (otherwise ANTLR would reject the grammar as invalid)
                // so no need to autoname if they all have a label
                if (alts[0].ParserLabel != null)
                    return alts.Select(a => ExpandAllAbbreviations(a.ParserLabel!));

                string ruleClassName = GetGeneratedClassName(parserRule);

                //// auto name from single element
                //if (alts.All(a => a.Elements.Count <= 1))
                //    return alts.ConvertAll(a => a.Elements[0]);

                // fallback
                return alts.Select((_, i) => $"{ruleClassName}_{i}").ToList();
            }
        }
    }

    string? ExpandAbbreviation(string word)
    {
        string? expanded = word.ToLowerInvariant() switch {
            "prog" => "program",
            "stmt" or "stat" => "statement",
            "brk" => "break",
            "ret" => "return",
            "expr" => "expression",
            "param" => "parameter",
            "arg" => "argument",
            "fn" or "fun" or "func" => "function",
            "proc" => "procedure",
            "ns" => "namespace",
            "def" => "definition",
            "decl" => "declaration",
            "attr" => "attribute",
            "prop" => "property",
            "ctor" => "constructor",
            "dtor" => "destructor",
            "ref" => "reference",
            "ptr" => "pointer",
            "var" => "variable",
            "val" => "value",
            "const" => "constant",
            "lit" => "literal",
            "str" => "string",
            "int" => "integer",
            "num" => "number",
            "chr" or "char" => "character",
            "id" or "ident" => "identifier",
            "kw" => "keyword",
            "asgt" or "asmt" or "asnmt" or "asgmt" or "asst" or "assig" or "asgn" => "assignment",
            "cond" => "condition",
            "cmd" => "command",
            "seq" => "sequence",
            "elt" or "elem" => "element",
            "op" => "operator",
            "mul" or "mult" => "multiply",
            "div" => "divide",
            "sub" => "subtract",
            "pow" or "pwr" => "power",
            "bin" => "binary",
            "un" => "unary",
            "esc" => "escape",
            [..([.., not 's'] singular), 's'] => ExpandAbbreviation(singular)?.Pluralize(),
            _ => null
        };
        return expanded?.PreserveCase(word);
    }

    string GetGeneratedClassName(Rule parserRule)
        => ToPascalCase(ExpandAllAbbreviations(parserRule.Name));

    string ExpandAllAbbreviations(string name)
        => WordSplitterRegex().Replace(name, m => ExpandAbbreviation(m.Value) ?? m.Value);

    IEnumerable<PropertyModel> GeneratePropertiesForAll(IReadOnlyList<SyntaxElement> elements,
        bool parentIsOptional, bool parentIsRepeated)
    {
        ArraySegment<SyntaxElement> rest = elements.ToArray();
        while (rest.Count > 0)
        {
            var properties = GeneratePropertiesFor(rest, out rest, parentIsOptional, parentIsRepeated);
            foreach (var property in properties)
                yield return property;
        }
    }

    IEnumerable<PropertyModel> GeneratePropertiesFor(
        ArraySegment<SyntaxElement> elementSpan, out ArraySegment<SyntaxElement> rest,
        bool parentIsOptional, bool parentIsRepeated)
    {
        SyntaxElement element = elementSpan[0];
        Debug.WriteLine($"element of type {element.GetType().Name}: {element}");

        rest = elementSpan[1..];
        bool isRepeated = parentIsRepeated || element.IsMany();
        bool isOptional = parentIsOptional || element.IsOptional();

        // block to limit the scope of variables in patterns
        {
            // e.g.  TOK (',' TOK)*  or  rule (COMMA rule)+
            if (elementSpan is [var a and (RuleRef or TokenRef), Block([Alternative([var delimiter, var b])]) block, ..var newRest]
                && RuleOrTokenRefsAreEqual(a, b)
                && !a.IsMany() && a.Label is null // we already know they are equal, so no need to check both
                && block.IsMany()
                && (delimiter is Literal || (delimiter is TokenRef t && !IsTokenTextImportant(t))))
            {
                diagnosticHandler(new(DiagnosticSeverity.Info,
                    $"recognized a pattern of {delimiter}-delimited list of {a}"));
                rest = newRest;
                isRepeated = true;
                // let execution fall through to the below case of "element is RuleRef" or "element is TokenRef"
            }
        }

        if (element is RuleRef ruleRef)
        {
            if (ruleRef.GetRuleOrNull(grammar) is Rule rule)
            {
                Lazy<NodeClassModel> nodeClass = new(() => FindOrGenerateAstNodeClass(rule));
                string propertyName = makePropertyName(element, rule.Name, list: isRepeated);
                return [isRepeated
                    ? new NodeReferenceListPropertyModel(propertyName, element.Label, nodeClass)
                    : new NodeReferencePropertyModel(propertyName, element.Label, nodeClass, isOptional)];
            }
            else
            {
                diagnosticHandler(new(DiagnosticSeverity.Error,
                    $"{ruleRef.Span.FilePath}:{ruleRef.Span.Begin.Line}: " +
                    $"reference to unknown parser rule '{ruleRef.Name}'"));
            }
        }
        else if (element is TokenRef tokenRef && IsTokenTextImportant(tokenRef))
        {
            ResolvedTokenRef resolvedTokenRef = Resolve(tokenRef);
            string propertyName = makePropertyName(element, tokenRef.Name, list: isRepeated);
            return [isRepeated
                ? new TokenTextListPropertyModel(propertyName, element.Label, resolvedTokenRef)
                : new TokenTextPropertyModel(propertyName, element.Label, resolvedTokenRef, isOptional)];
        }
        else if (element is TokenRef or Literal
            && element.IsOptional()
            && element.Label is string label)
        {
            string propertyName = makePropertyName(element, null!, list: false);
            ResolvedTokenRef resolvedToken = element switch {
                Literal literal => Resolve(literal),
                TokenRef tokenRef_ => Resolve(tokenRef_),
            };
            Debug.Assert(resolvedToken != null); // TODO: what about implicit tokens in combined grammars?
            return [new OptionalTokenPropertyModel(
                Name: propertyName.StartsWithAny("Is", "Has", "Does", "Do", "Should", "Can", "Will")
                    ? propertyName : $"Is{propertyName}",
                Label: label,
                Token: resolvedToken
            )];
        }
        else if (element is Block block) // recurse into blocks (`((a) | b)*`)
        {
            List<Alternative> alts = block.Items;
            return alts.SelectMany(a => GeneratePropertiesForAll(a.Elements,
                isOptional || alts.Count > 1, isRepeated));
        }
        return [];

        string makePropertyName(SyntaxElement element, string refName, bool list)
        {
            // TODO: move the conversion to PascalCase into CSharpModelWriter
            string baseName = (element.Label ?? refName)
                .Trim('_'); // trim any underscores used for avoiding keywords ('public', 'import', etc.)
            string propName = ToPascalCase(ExpandAllAbbreviations(baseName));
            if (list)
            {
                string pluralized = propName.Pluralize(inputIsKnownToBeSingular: false);
                return (propName == pluralized && element.Label is null)
                    // for example `statements+` or `functions*` (rule name is already plural)
                    ? $"{propName}List"
                    : pluralized;
            }
            else
                return propName;
        }
    }

    bool RuleOrTokenRefsAreEqual(SyntaxElement first, SyntaxElement second)
    {
        var ruleRefToTuple = (RuleRef r) => (r.Name, r.Suffix, r.IsNot, r.Label, r.LabelKind);
        var tokenRefToTuple = (TokenRef t) => (t.Name, t.Suffix, t.IsNot, t.Label, t.LabelKind);

        return (first, second) switch {
            (RuleRef a, RuleRef b) => ruleRefToTuple(a) == ruleRefToTuple(b),
            (TokenRef a, TokenRef b) => tokenRefToTuple(a) == tokenRefToTuple(b),
            _ => false
        };
    }

    ResolvedTokenRef Resolve(Literal literal)
    {
        lexerRulesByLiteral.TryGetValue(literal.Text, out Rule? lexerRule);
        return new(lexerRule?.Name, literal, lexerRule);
        // TODO: we might need to try to generate a valid identifier from unnamed literals, including punctuation, etc.
        //static string makeUpTokenNameForLiteral(Literal literal) => ?;
    }

    ResolvedTokenRef Resolve(TokenRef tokenRef)
    {
        bool found = grammar.TryGetRule(tokenRef.Name, out Rule? lexerRule);
        if (!found)
        {
            diagnosticHandler(new(DiagnosticSeverity.Warning,
                "token reference could not find the corresponding " +
                "lexer rule for token reference " + tokenRef));
        }

        return new ResolvedTokenRef(tokenRef.Name, Literal: null, lexerRule);
    }

    static string ToPascalCase(string name)
    {
        if (name.Any(char.IsLower)) // assume camelCase
            return name.Capitalize();
        else // assume ALL_UPPERCASE
            return string.Concat(name.Split('_')
                .Select(word => word.ToLowerInvariant().Capitalize()));
    }

    List<PropertyModel> GeneratePostprocessedPropertyListFor(Alternative alt)
    {
        List<PropertyModel> propertyList =
            GeneratePropertiesForAll(alt.Elements, parentIsOptional: false, parentIsRepeated: false)
            .ToList();

        postprocessPropertyList(propertyList);

        return propertyList;

        static void postprocessPropertyList(List<PropertyModel> propertyList)
        {
            foreach (var duplicateGroup in propertyList.GroupBy(p => p.Name))
            {
                if (!duplicateGroup.Skip(1).Any())
                    return;

                var duplicateProperties = duplicateGroup.ToList();
                if (duplicateProperties is [var left, var right])
                {
                    RenameProperty(left, $"Left{left.Name}");
                    RenameProperty(right, $"Right{right.Name}");
                }
                else
                {
                    for (int i = 0; i < duplicateProperties.Count; i++)
                    {
                        RenameProperty(duplicateProperties[i], $"{duplicateProperties[i].Name}{i + 1}");
                    }
                }
            }

            void RenameProperty(PropertyModel property, string newName)
            {
                propertyList[propertyList.IndexOf(property)] = property with { Name = newName };
            }
        }
    }

    bool IsTokenTextImportant(TokenRef token) // TODO: find a better solution
        => token.Name.Split('_').LastOrDefault()?.ToUpperInvariant()
            is "ID" or "IDENT" or "IDENTIFIER" or "NAME"
            or "LIT" or "LITERAL" or "VALUE" or "CONST" or "CONSTANT"
            or "REF" or "TYPE" or "KIND" or "MODIFIER" or "ATTR" or "ATTRIBUTE"
            or "INT" or "FLOAT" or "NUMBER";

    [GeneratedRegex(@"([A-Z]+(?![a-z])|[A-Z][a-z]+|[0-9]+|[a-z]+)")]
    private static partial Regex WordSplitterRegex();
}
