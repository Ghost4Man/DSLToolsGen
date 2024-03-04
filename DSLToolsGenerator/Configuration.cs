using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using DSLToolsGenerator.AST;
using DSLToolsGenerator.SyntaxHighlighting;

namespace DSLToolsGenerator
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
    public record Configuration
    {
        [JsonPropertyName("AST")]
        public AstConfiguration Ast { get; init; } = new();

        public SyntaxHighlightingConfiguration SyntaxHighlighting { get; init; } = new();
    }
}

namespace DSLToolsGenerator.AST
{
    public record AstConfiguration
    {
        public string? Namespace { get; init; }

        public string? AntlrNamespace { get; init; }

        public ClassNamingOptions NodeClassNaming { get; init; } = new();

        /// <summary>
        /// By default, when generating names of AST classes and properties based on
        /// rules and labels in the grammar, DTG automatically expands certain common
        /// abbreviations (e.g. <c>stmt</c>, <c>expr</c>, <c>kw</c>, <c>cmd</c>)
        /// to get more readable and idiomatic C# code.
        /// This behavior can be disabled or customized by overriding what
        /// a word expands to.
        /// </summary>
        public WordExpansionOptions AutomaticAbbreviationExpansion { get; init; } = new();
    }

    public record WordExpansionOptions
    {
        public bool UseDefaultWordExpansions { get; init; } = true;

        [JsonIgnore]
        public Dictionary<string, string> DefaultWordExpansions
            => UseDefaultWordExpansions ? WordExpansionHelper.Defaults : [];

        /// <summary>
        /// Specify custom abbreviation/word expansion map.
        /// The key can contain multiple abbreviations delimited by vertical bar.
        /// For example: <c>{ "fn|func": "function" }</c>
        /// </summary>
        [JsonPropertyName("CustomWordExpansions")]
        public Dictionary<SimplePattern, string> CustomWordExpansionsRaw { get; init; } = [];

        [JsonIgnore]
        public IReadOnlyDictionary<string, string> CustomWordExpansions
            => customWordExpansions ??= WordExpansionHelper.Flatten(CustomWordExpansionsRaw);

        IReadOnlyDictionary<string, string>? customWordExpansions;
    }

    [JsonConverter(typeof(SimplePatternConverter))]
    public record struct SimplePattern(string[] Options);

    file class SimplePatternConverter : JsonConverter<SimplePattern>
    {
        public override SimplePattern Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()?.Split('|') ?? []);

        public override void Write(Utf8JsonWriter writer, SimplePattern value, JsonSerializerOptions options)
            => writer.WriteStringValue(string.Join("|", value.Options));

        public override SimplePattern ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => Read(ref reader, typeToConvert, options);

        public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] SimplePattern value, JsonSerializerOptions options)
            => Write(writer, value, options);
    }

    public record ClassNamingOptions
    {
        public string? Prefix { get; init; }
        public string? Suffix { get; init; }
    }
}

namespace DSLToolsGenerator.SyntaxHighlighting
{
    public record SyntaxHighlightingConfiguration
    {
        public IReadOnlyList<RuleConflict> RuleConflicts { get; init; } = [];

        /// <summary>
        /// Customize syntax highlighting for specific lexer rules.
        /// </summary>
        public IReadOnlyDictionary<string, RuleOptions>? RuleSettings { get; init; }
    }

    public record RuleConflict(IReadOnlyList<string> RuleNames);

    public record RuleOptions(string TextMateScopeName);
}

file static class WordExpansionHelper
{
    public static readonly Dictionary<string, string> Defaults = Flatten(new() {
        [new(["prog"])] = "program",
        [new(["stmt", "stat"])] = "statement",
        [new(["brk"])] = "break",
        [new(["ret"])] = "return",
        [new(["expr"])] = "expression",
        [new(["param"])] = "parameter",
        [new(["arg"])] = "argument",
        [new(["fn", "fun", "func"])] = "function",
        [new(["proc"])] = "procedure",
        [new(["intf"])] = "interface",
        [new(["ns", "nmsp"])] = "namespace",
        [new(["pkg"])] = "package",
        [new(["def", "defn", "dfn"])] = "definition",
        [new(["decl", "dcl"])] = "declaration",
        [new(["descr"])] = "description",
        [new(["annot"])] = "annotation",
        [new(["fwd"])] = "forward",
        [new(["bwd"])] = "backward",
        [new(["err"])] = "error",
        [new(["wrn"])] = "warning",
        [new(["diag"])] = "diagnostic",
        [new(["exc", "excep"])] = "exception",
        [new(["lbl"])] = "label",
        [new(["attr"])] = "attribute",
        [new(["prop"])] = "property",
        [new(["ctor"])] = "constructor",
        [new(["dtor"])] = "destructor",
        [new(["ref"])] = "reference",
        [new(["ptr"])] = "pointer",
        [new(["var"])] = "variable",
        [new(["val"])] = "value",
        [new(["const"])] = "constant",
        [new(["lit"])] = "literal",
        [new(["str"])] = "string",
        [new(["txt"])] = "text",
        [new(["int"])] = "integer",
        [new(["flt"])] = "float",
        [new(["dbl"])] = "double",
        [new(["num"])] = "number",
        [new(["chr", "char"])] = "character",
        [new(["id", "ident"])] = "identifier",
        [new(["idx"])] = "index",
        [new(["kw", "kwd"])] = "keyword",
        [new(["pwd"])] = "password",
        [new(["asgt", "asmt", "asnmt", "asgmt", "asst", "assig", "asgn"])] = "assignment",
        [new(["cond"])] = "condition",
        [new(["pred"])] = "predicate",
        [new(["cmd"])] = "command",
        [new(["seq"])] = "sequence",
        [new(["arr"])] = "array",
        [new(["elt", "elem"])] = "element",
        [new(["tbl"])] = "table",
        [new(["op"])] = "operator",
        [new(["aggr"])] = "aggregate",
        [new(["mul", "mult"])] = "multiply",
        [new(["div"])] = "divide",
        [new(["sub"])] = "subtract",
        [new(["pow", "pwr"])] = "power",
        [new(["bin"])] = "binary",
        [new(["un"])] = "unary",
        [new(["esc"])] = "escape",
        [new(["msg"])] = "message",
        [new(["pkt"])] = "packet",
        [new(["src"])] = "source",
        [new(["dest"])] = "destination",
        [new(["loc"])] = "location",
        [new(["pos"])] = "position",
    });

    public static Dictionary<string, string> Flatten(Dictionary<SimplePattern, string> patternKeyedDict)
        => patternKeyedDict
            .SelectMany(kvp => kvp.Key.Options.Select(o => (o, kvp.Value)))
            .ToDictionary(StringComparer.InvariantCultureIgnoreCase);
}
