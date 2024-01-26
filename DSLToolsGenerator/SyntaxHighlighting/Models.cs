namespace DSLToolsGenerator.SyntaxHighlighting.Models;

public record SyntaxHighlightingConfiguration
{
    public IReadOnlyList<RuleConflict> RuleConflicts { get; init; } = [];
}

public record RuleConflict(IReadOnlyList<string> RuleNames);
