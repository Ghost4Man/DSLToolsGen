using System.Diagnostics;
using System.Text.RegularExpressions;

using Antlr4Ast;
using Humanizer;

using DSLToolsGenerator.Models;

namespace DSLToolsGenerator;

public partial class AstCodeGenerator
{
    readonly Grammar grammar;
    readonly Dictionary<string, Rule> lexerRulesByLiteral;

    // Mapping from parser rules to codegen models of AST Node classes.
    // Does not include variants (derived classes) of (abstract) node classes
    // for rules with multiple alternatives.
    readonly Dictionary<Rule, NodeClassModel> nodeClassesByRule = new();

    public AstCodeGenerator(Grammar parserGrammar)
    {
        if (parserGrammar.Kind == GrammarKind.Lexer)
            throw new ArgumentException("cannot generate AST from a lexer grammar");

        this.grammar = parserGrammar;
        this.lexerRulesByLiteral = parserGrammar.LexerRules
            .Select(r => r.AlternativeList.Items is [Alternative { Elements: [Literal literal] }]
                ? new { Rule = r, Literal = literal }
                : null)
            .WhereNotNull()
            .ToDictionary(r => r.Literal.Text, r => r.Rule);
    }

    public AstCodeModel GenerateAstCodeModel()
    {
        var nodeClasses = grammar.ParserRules.Select(FindOrGenerateAstNodeClass);
        var astBuilder = new AstBuilderModel(grammar.Name, getParserClassNameFromGrammar(grammar),
            nodeClasses.Select(nc => new AstMappingModel(nc.ParserRule, nc)).ToList());
        return new AstCodeModel(nodeClasses.ToList(), astBuilder);

        static string getParserClassNameFromGrammar(Grammar grammar)
            // TODO: check if this is actually how ANTLR computes the name
            => grammar.Kind == GrammarKind.Full
                ? grammar.Name + "Parser"
                : grammar.Name;
    }

    NodeClassModel FindOrGenerateAstNodeClass(Rule parserRule)
    {
        return nodeClassesByRule.TryGetValue(parserRule, out var nodeClass)
            ? nodeClass ?? throw new NotImplementedException("found rule reference cycle " +
                $"(involving rule '{parserRule.Name}'), which is currently not handled")
            : (nodeClassesByRule[parserRule] = generateAstNodeClass(parserRule));

        NodeClassModel generateAstNodeClass(Rule parserRule)
        {
            if (parserRule.IsLexer)
                throw new ArgumentException($"{parserRule} is not a parser rule!");

            nodeClassesByRule[parserRule] = null!; // mark this as null to prevent stack overflow

            string className = GetGeneratedClassName(parserRule);
            List<Alternative> alts = parserRule.AlternativeList.Items;

            if (alts is [Alternative singleAlt])
            {
                return nodeClassModelForAlternative(parserRule, className, singleAlt);
            }
            else // generate derived record types for multi-alt rules
            {
                var altNames = autoNameAlternatives(parserRule);
                return new NodeClassModel(className, parserRule, []) {
                    Variants = alts.Zip(altNames, (a, altName) => {
                        string variantClassName = ToPascalCase(altName);
                        return nodeClassModelForAlternative(parserRule, variantClassName, a);
                    }).ToList()
                };
            }

            NodeClassModel nodeClassModelForAlternative(Rule parserRule, string className, Alternative alt)
            {
                var parameters = GeneratePropertyListFor(alt);
                return new NodeClassModel(className, parserRule, parameters);
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
            "asgt" or "asmt" or "asnmt" or "asgmt" or "asst" or "assig" or "asgn" => "assignment",
            "cond" => "condition",
            "cmd" => "command",
            "seq" => "sequence",
            "elt" or "elem" => "element",
            "op" => "operator",
            "mul" or "mult" => "multiply",
            "div" => "divide",
            "sub" => "subtract",
            "bin" => "binary",
            "un" => "unary",
            "esc" => "escape",
            _ => null
        };
        return expanded?.PreserveCase(word);
    }

    string GetGeneratedClassName(Rule parserRule)
        => ToPascalCase(ExpandAllAbbreviations(parserRule.Name));

    string ExpandAllAbbreviations(string name)
        => WordSplitterRegex().Replace(name, m => ExpandAbbreviation(m.Value) ?? m.Value);

    IEnumerable<PropertyModel> GeneratePropertiesFor(SyntaxElement element, bool parentIsOptional, bool parentIsRepeated)
    {
        // TODO: move the ToPascalCase calls into the CSharpModelWriter

        Debug.WriteLine($"element of type {element.GetType().Name}: {element}");

        bool isRepeated = parentIsRepeated || element.IsMany();
        bool isOptional = parentIsOptional || element.IsOptional();

        if ((element as RuleRef)?.GetRuleOrNull(grammar) is Rule rule)
        {
            string elementName = element.Label ?? rule.Name;
            NodeClassModel nodeClass = FindOrGenerateAstNodeClass(rule);
            yield return isRepeated
                ? new NodeReferenceListPropertyModel(MakeListName(ToPascalCase(ExpandAllAbbreviations(elementName))), element.Label, nodeClass)
                : new NodeReferencePropertyModel(ToPascalCase(ExpandAllAbbreviations(elementName)), element.Label, nodeClass, isOptional);
        }
        else if (element is TokenRef tokenRef && IsTokenTextImportant(tokenRef))
        {
            string elementName = tokenRef.Label ?? tokenRef.Name;
            ResolvedTokenRef resolvedTokenRef = Resolve(tokenRef);
            yield return isRepeated
                ? new TokenTextListPropertyModel(MakeListName(ToPascalCase(ExpandAllAbbreviations(elementName))), element.Label, resolvedTokenRef)
                : new TokenTextPropertyModel(ToPascalCase(ExpandAllAbbreviations(elementName)), element.Label, resolvedTokenRef, isOptional);
        }
        else if (element is TokenRef or Literal
            && element.IsOptional()
            && element.Label is string label)
        {
            string name = ToPascalCase(ExpandAllAbbreviations(label)).Trim('_'); // trim any underscores used for avoiding keywords
            ResolvedTokenRef resolvedToken = element switch {
                Literal literal => Resolve(literal),
                TokenRef tokenRef_ => Resolve(tokenRef_),
            };
            Debug.Assert(resolvedToken != null); // TODO: what about implicit tokens in combined grammars?
            yield return new OptionalTokenPropertyModel(
                Name: name.StartsWithAny("Is", "Has", "Does", "Do", "Should", "Can", "Will") ? name : $"Is{name}",
                Label: label,
                Token: resolvedToken
            );
        }
        else if (element is Block block) // recurse into blocks (`((a) | b)*`)
        {
            List<Alternative> alts = block.Items;
            foreach (SyntaxElement child in alts.SelectMany(a => a.Elements))
            {
                var properties = GeneratePropertiesFor(child, isOptional || alts.Count > 1, isRepeated);
                foreach (var property in properties)
                    yield return property;
            }
        }
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
            Console.Error.WriteLine("warning: token reference could not find " +
                "the corresponding lexer rule for token reference " + tokenRef);

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

    string MakeListName(string singular)
    {
        string pluralized = singular.Pluralize(inputIsKnownToBeSingular: false);
        return (singular == pluralized) ? $"{singular}List" : pluralized;
    }

    //=> singular switch {
    //    "index" => "indices",
    //    "matrix" => "matrices",
    //    [.., 's'] => singular + "es",
    //    [.., 'y'] => singular + "ies",
    //    _ => singular + "s"
    //};

    List<PropertyModel> GeneratePropertyListFor(Alternative alt)
    {
        List<PropertyModel> propertyList = alt.Elements
            .SelectMany(el => GeneratePropertiesFor(el,
                parentIsOptional: false, parentIsRepeated: false))
            .ToList();

        postProcessPropertyList(propertyList);

        return propertyList;

        static void postProcessPropertyList(List<PropertyModel> propertyList)
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

static class RuleRefExtensions
{
    public static Rule? GetRuleOrNull(this RuleRef ruleRef, Grammar grammar)
    {
        return grammar.TryGetRule(ruleRef.Name, out Rule? rule) ? rule : null;
    }
}

static class TokenRefExtensions
{
    public static bool IsMany(this SyntaxElement element)
        => element.Suffix is SuffixKind.Plus or SuffixKind.Star
            or SuffixKind.PlusGreedy or SuffixKind.StarGreedy;

    public static bool IsOptional(this SyntaxElement element)
        => element.Suffix is SuffixKind.Optional or SuffixKind.OptionalGreedy;
}
