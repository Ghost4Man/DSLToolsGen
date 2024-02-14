using System.Text.Json.Serialization;
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

        public IReadOnlyDictionary<string, RuleOptions>? RuleSettings { get; init; }
    }

    public record RuleConflict(IReadOnlyList<string> RuleNames);

    public record RuleOptions(string TextMateScopeName);
}
