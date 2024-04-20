using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;

using DSLToolsGenerator.AST;
using DSLToolsGenerator.SyntaxHighlighting;
using DSLToolsGenerator.EditorExtensions;

namespace DSLToolsGenerator
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
    public record Configuration
    {
        /// <summary>
        /// Path to an ANTLR grammar file (<c>.g4</c>) describing the language.
        /// This should be the parser grammar (or a combined grammar).
        /// </summary>
        public string? GrammarFile { get; init; }

        /// <summary>
        /// Specifies which generators to run automatically
        /// on <c>dtg generate</c> or <c>dtg watch</c>.
        /// </summary>
        public OutputSet Outputs { get; init; } = new();

        [JsonPropertyName("AST")]
        public AstConfiguration Ast { get; init; } = new();

        public SyntaxHighlightingConfiguration SyntaxHighlighting { get; init; } = new();

        public VscodeExtensionConfiguration? VscodeExtension { get; init; }
    }

    public record OutputSet
    {
        [DefaultValue(true)]
        public bool AST { get; init; } = true;

        [DefaultValue(true)]
        public bool TmLanguageJson { get; init; } = true;

        [DefaultValue(false)]
        public bool VscodeExtension { get; init; } = false;
    }

    [RegexValidatedString]
    public partial record IdentifierString
    {
        public const string ValidationPattern = /* lang=regex */
            """^([a-zA-Z0-9_]+)$""";

        [GeneratedRegex(ValidationPattern)]
        public static partial Regex ValidationRegex();
    }

    [RegexValidatedString]
    public partial record HyphenIdentifierString
    {
        public const string ValidationPattern = /* lang=regex */
            """^([a-zA-Z0-9_-]+)$""";

        [GeneratedRegex(ValidationPattern)]
        public static partial Regex ValidationRegex();
    }

    [RegexValidatedString]
    public partial record HyphenDotIdentifierString
    {
        public const string ValidationPattern = /* lang=regex */
            """^([a-zA-Z0-9_.-]+)$""";

        [GeneratedRegex(ValidationPattern)]
        public static partial Regex ValidationRegex();
    }

    [RegexValidatedString]
    public partial record HyphenDotSlashIdentifierString
    {
        public const string ValidationPattern = /* lang=regex */
            """^([a-zA-Z0-9_./-]+)$""";

        [GeneratedRegex(ValidationPattern)]
        public static partial Regex ValidationRegex();
    }
}

namespace DSLToolsGenerator.AST
{
    public record AstConfiguration
    {
        /// <summary>
        /// Specifies where to store the AST generator output
        /// (AST classes and related functionality), e.g. <c>./AST.g.cs</c>.
        /// Using the file extension <c>.g.cs</c> is recommended for generated code.
        /// </summary>
        [DefaultValue("AST.g.cs")]
        public string? OutputPath { get; init; } = "AST.g.cs";

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
        [DefaultValue(true)]
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
        public string? OutputPath { get; init; }

        public IReadOnlyList<RuleConflict> RuleConflicts { get; init; } = [];

        /// <summary>
        /// Customize syntax highlighting for specific lexer rules.
        /// </summary>
        public IReadOnlyDictionary<string, RuleOptions>? RuleSettings { get; init; }
    }

    [Snippet("rule conflict",
        """adds a new rule that disambiguates two "conflicting" rules""",
        """{ "RuleNames": ["${1}", "${2}"] }""")]
    public record RuleConflict(IReadOnlyList<string> RuleNames);

    public record RuleOptions(
        [property:
            Snippet("\"keyword\""),
            Snippet("\"keyword.operator\""),
            Snippet("\"keyword.control\""),
            Snippet("\"keyword.other\""),
            Snippet("\"keyword.other.operator\""),
            Snippet("\"keyword.other.unit\""),
            Snippet("\"support.class\""),
            Snippet("\"support.type\""),
            Snippet("\"support.type.property-name\""),
            Snippet("\"support.variable\""),
            Snippet("\"support.function\""),
            Snippet("\"entity\""),
            Snippet("\"entity.name.function\""),
            Snippet("\"entity.name.class\""),
            Snippet("\"entity.name.tag\""),
            Snippet("\"entity.name.label\""),
            Snippet("\"entity.name.operator\""),
            Snippet("\"entity.other.attribute\""),
            Snippet("\"entity.other.attribute-name\""),
            Snippet("\"variable\""),
            Snippet("\"variable.other.constant\""),
            Snippet("\"variable.other.enummember\""),
            Snippet("\"variable.language\""),
            Snippet("\"punctuation\""),
            Snippet("\"punctuation.definition.tag\""),
            Snippet("\"punctuation.definition.template-expression\""),
            Snippet("\"punctuation.section.embedded\""),
            Snippet("\"string\""),
            Snippet("\"string.regexp\""),
            Snippet("\"storage\""),
            Snippet("\"storage.type\""),
            Snippet("\"storage.modifier\""),
            Snippet("\"constant\""),
            Snippet("\"constant.numeric\""),
            Snippet("\"constant.character\""),
            Snippet("\"constant.regexp\""),
            Snippet("\"constant.language\""),
            Snippet("\"constant.character.escape\""),
            Snippet("\"constant.other.placeholder\""),
            Snippet("\"constant.other.option\""),
            Snippet("\"markup.heading\""),
            Snippet("\"markup.italic\""),
            Snippet("\"markup.underline\""),
            Snippet("\"markup.bold\""),
            Snippet("\"meta.template.expression\"", Description =
                    "ensures that the contents of a string interpolation " +
                    "expression aren't 'string'-colored"),
            Snippet("\"strong\""),
            Snippet("\"emphasis\""),
            Snippet("\"invalid\""),
            Snippet("\"comment.line\""),
            Snippet("\"comment.block\""),
        ]
        string TextMateScopeName
    );
}

namespace DSLToolsGenerator.EditorExtensions
{
    public record VscodeExtensionConfiguration(
        HyphenDotIdentifierString ExtensionId, string ExtensionDisplayName,
        HyphenDotIdentifierString LanguageId, string LanguageDisplayName, string[] LanguageFileExtensions)
    {
        [DefaultValue("vscode-extension")]
        public string OutputDirectory { get; init; } = "vscode-extension";

        public HyphenDotIdentifierString LanguageId { get; init; } = new(LanguageId);
        public string LanguageDisplayName { get; init; } = LanguageDisplayName;

        /// <summary>
        /// File extensions for documents in this language, including the dot,
        /// e.g. <c>[".abc"]</c>.
        /// </summary>
        public string[] LanguageFileExtensions { get; init; } = LanguageFileExtensions;

        public HyphenDotIdentifierString ExtensionId { get; init; } = ExtensionId;
        public string ExtensionDisplayName { get; init; } = ExtensionDisplayName;
        public HyphenDotSlashIdentifierString LspCustomCommandPrefix { get; init; } = new($"{LanguageId}/");
        public string LanguageClientName { get; init; } = ExtensionDisplayName;
        public string CommandCategoryName { get; init; } = ExtensionDisplayName;
        public required HyphenDotIdentifierString CsprojName { get; init; }

        [DefaultValue(true)]
        public bool IncludeAstExplorerView { get; init; } = true;
    }
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
