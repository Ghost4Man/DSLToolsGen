namespace DSLToolsGenerator.SyntaxHighlighting.Models;

public record SyntaxHighlightingConfiguration
{
    public IReadOnlyList<RuleConflict> RuleConflicts { get; init; } = [];

    public IReadOnlyDictionary<string, RuleOptions>? RuleSettings { get; init; }
}

public record RuleConflict(IReadOnlyList<string> RuleNames);

public record RuleOptions(string TextMateScopeName);
