namespace DSLToolsGenerator;

public record Diagnostic(DiagnosticSeverity Severity, string Message)
{
    public override string ToString() => $"{Severity}: {Message}";
}

public enum DiagnosticSeverity { Info, Warning, Error }
