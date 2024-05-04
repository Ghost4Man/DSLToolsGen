using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.ComponentModel;

using Humanizer;

using DSLToolsGenerator.Parser;
using DSLToolsGenerator.AST;
using DSLToolsGenerator.LanguageServer;
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

        public HyphenDotIdentifierString? LanguageId { get; init; }

        public string? LanguageDisplayName { get; init; }

        /// <summary>
        /// File extensions for documents in this language, including the dot,
        /// e.g. <c>[".abc"]</c>.
        /// </summary>
        public string[]? LanguageFileExtensions { get; init; }

        /// <summary>
        /// Bare name (without file extension) of the C# project file
        /// (from the root directory of the workspace)
        /// that contains the language server code.
        /// </summary>
        public HyphenDotIdentifierString? CsprojName { get; init; }

        /// <summary>
        /// Specifies which generators to run automatically
        /// on <c>dtg generate</c> or <c>dtg watch</c>.
        /// </summary>
        public OutputSet Outputs { get; init; } = new() {
            AST = true,
            LanguageServer = true,
            TmLanguageJson = true,
            VscodeExtension = false,
        };

        public ParserConfiguration Parser { get; init; } = new();

        [JsonPropertyName("AST")]
        public AstConfiguration Ast { get; init; } = new();

        public LanguageServerConfiguration LanguageServer { get; init; } = new();

        public SyntaxHighlightingConfiguration SyntaxHighlighting { get; init; } = new();

        public VscodeExtensionConfiguration? VscodeExtension { get; init; }

        public HyphenDotIdentifierString GetFallbackLanguageId(Antlr4Ast.Grammar grammar)
            => new(grammar.GetLanguageName(GrammarFile) ?? "untitled");

        public static bool CheckValuePresent<T>(
            [NotNullWhen(true)] T value,
            [NotNullWhen(true)] out T? valueIfPresent,
            Action<Diagnostic> diagnosticHandler,
            Func<T, bool>? customPredicate = null,
            [CallerArgumentExpression(nameof(value))] string configValueName = null!)
        {
            ArgumentException.ThrowIfNullOrEmpty(nameof(configValueName));

            if (!customPredicate?.Invoke(value) ?? (value is null))
            {
                configValueName = configValueName
                    .TrimPrefix("configuration.")
                    .TrimPrefix("config.")
                    .TrimPrefix("c.");
                diagnosticHandler(new(DiagnosticSeverity.Error,
                    $"Missing configuration value for {configValueName}"));
                valueIfPresent = default;
                return false;
            }
            valueIfPresent = value!;
            return true;
        }
    }

    public record OutputSet
    {
        [DefaultValue(true)]
        public bool AST { get; init; }

        [DefaultValue(true)]
        public bool LanguageServer { get; init; }

        [DefaultValue(true)]
        public bool TmLanguageJson { get; init; }

        [DefaultValue(false)]
        public bool VscodeExtension { get; init; }
    }

    [RegexValidatedString]
    public partial record IdentifierString
    {
        public const string ValidationPattern = /* lang=regex */
            """^([a-zA-Z_][a-zA-Z0-9_]*)$""";

        [GeneratedRegex(ValidationPattern)]
        public static partial Regex ValidationRegex();
    }

    [RegexValidatedString]
    public partial record DottedIdentifierString
    {
        public const string ValidationPattern = /* lang=regex */
            """^([a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*)$""";

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

namespace DSLToolsGenerator.Parser
{
    public record ParserConfiguration
    {
        /// <summary>
        /// The namespace of the classes generated by ANTLR.
        /// </summary>
        public DottedIdentifierString? Namespace { get; init; }
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

        public DottedIdentifierString? Namespace { get; init; }

        public ClassNamingOptions NodeClassNaming { get; init; } = new();

        /// <summary>
        /// Class name of the AST's root node, e.g. <c>"ProgramNode"</c>.
        /// </summary>
        public IdentifierString? RootNodeClass { get; init; }

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

namespace DSLToolsGenerator.LanguageServer
{
    public record LanguageServerConfiguration
    {
        /// <summary>
        /// Specifies where to store the LanguageServer generator output
        /// (LanguageServer classes and related functionality), e.g. <c>./LanguageServer.g.cs</c>.
        /// Using the file extension <c>.g.cs</c> is recommended for generated code.
        /// </summary>
        [DefaultValue("LanguageServer.g.cs")]
        public string? OutputPath { get; init; } = "LanguageServer.g.cs";

        public DottedIdentifierString? Namespace { get; init; }
        public IdentifierString? LanguageServerClassName { get; init; }

        public IdentifierString GetFallbackLanguageServerClassName(string languageId)
            => new(languageId.Pascalize() + "LanguageServer");
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

    public record RuleOptions
    {
        [
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
        public string? TextMateScopeName { get; init; }

        /// <summary>
        /// Specifies whether and how to automatically reorder alternatives
        /// within blocks like <c>('.var.' | '.var.mut.' | '.var.mut.global.')</c>
        /// as an attempt to fix shadowing (<c>.var.mut.</c> could never match anything
        /// since the <c>.var.</c> part would always be matched by the first alternative).
        /// </summary>
        [DefaultValue(DefaultAltReorderingMode)]
        public AltReorderingMode? AltReordering { get; init; }

        public const AltReorderingMode DefaultAltReorderingMode = AltReorderingMode.ByPatternLength;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AltReorderingMode
    {
        None = 0,
        LiteralsOnly = 1,
        ByPatternLength = 2,
    }
}

namespace DSLToolsGenerator.EditorExtensions
{
    public record VscodeExtensionConfiguration
    {
        [DefaultValue("vscode-extension")]
        public string OutputDirectory { get; init; } = "vscode-extension";

        public required HyphenDotIdentifierString ExtensionId { get; init; }
        public required string ExtensionDisplayName { get; init; }
        public string? LanguageClientName { get; init; }
        public string? CommandCategoryName { get; init; }

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
