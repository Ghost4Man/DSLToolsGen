namespace DSLToolsGenerator.AST.Tests;

public abstract class CodegenTestFixture(ITestOutputHelper testOutput)
{
    protected AstCodeGenerator GetGeneratorForGrammar(string grammarCode, AstConfiguration? config = null)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        grammar.Analyze();
        Assert.Empty(grammar.ErrorMessages);
        return new AstCodeGenerator(grammar, handleDiagnostic, config ?? new());

        void handleDiagnostic(Diagnostic d)
        {
            if (d.Severity == DiagnosticSeverity.Error)
                Assert.Fail(d.ToString());
            else
                testOutput.WriteLine(d.ToString());
        }
    }
}
