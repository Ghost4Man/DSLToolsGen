namespace DSLToolsGenerator.SyntaxHighlighting.Models;

public record SyntaxHighlightingConfiguration
{
    public IList<RuleConflict> RuleConflicts { get; init; } = [];
}

public record RuleConflict((string First, string Second) RuleNames);
