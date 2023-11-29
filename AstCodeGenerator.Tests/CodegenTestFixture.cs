namespace DSLToolsGenerator.Tests;

public abstract class CodegenTestFixture(ITestOutputHelper testOutput)
{
    protected AstCodeGenerator GetGeneratorForGrammar(string grammarCode)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        Assert.Empty(grammar.ErrorMessages);
        return new AstCodeGenerator(grammar);
    }
}