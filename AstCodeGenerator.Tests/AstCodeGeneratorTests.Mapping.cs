using static DSLToolsGenerator.CSharpModelWriter;

namespace DSLToolsGenerator.Tests;

public class AstCodeGeneratorTests_Mapping
{
    const string grammarProlog = """
        grammar Foo;
        ID : [a-zA-Z_][a-zA-Z0-9_]+ ;
        NUMBER : [0-9]+ ;
        FLOAT : INT '.' INT ;
        STR_LIT : '"' ~["]+ '"' ;
        """;

    [Fact]
    public void given_1_rule_with_two_ID_tokens_ー_gets_mapped_from_Text_of_tokens()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'swap' ID 'and' ID ;
            """);
        Assert.Equal("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Statement VisitStat(FooParser.StatContext context)
                {
                    var LeftIdentifier = context.ID(0).GetText();
                    var RightIdentifier = context.ID(1).GetText();
                    return new Statement(LeftIdentifier, RightIdentifier);
                }
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_multiple_ID_tokens_ー_gets_mapped_from_Text_of_tokens()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            print : 'print' ID+ ;
            """);
        Assert.Equal("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Print VisitPrint(FooParser.PrintContext context)
                {
                    var Identifiers = Array.ConvertAll(context.ID(), t => t.GetText());
                    return new Print(Identifiers);
                }
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_multiple_labeled_ID_tokens_ー_gets_mapped_from_Text_of_labeled_tokens()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'set' varName=ID '=' expr=ID ;
            """);
        Assert.Equal("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Statement VisitStat(FooParser.StatContext context)
                {
                    var VariableName = context.varName.Text;
                    var Expression = context.expr.Text;
                    return new Statement(VariableName, Expression);
                }
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_optional_labeled_ID_tokens_ー_gets_mapped_from_nullable_Text_of_labeled_tokens()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'var' varName=ID? ('=' expr=ID)? ;
            """);
        Assert.Equal("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Statement VisitStat(FooParser.StatContext context)
                {
                    var VariableName = context.varName?.Text;
                    var Expression = context.expr?.Text;
                    return new Statement(VariableName, Expression);
                }
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_rule_refs_ー_gets_mapped_from_rule_getter_methods()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            assignment : lvalue '=' expr ;
            lvalue : expr '.' ID ;
            expr : ID ;
            """);
        Assert.StartsWith("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Assignment VisitAssignment(FooParser.AssignmentContext context)
                {
                    var Lvalue = Visit(context.lvalue());
                    var Expression = Visit(context.expr());
                    return new Assignment(Lvalue, Expression);
                }

                public override Lvalue VisitLvalue(FooParser.LvalueContext context)
                {
                    var Expression = Visit(context.expr());
                    var Identifier = context.ID().GetText();
                    return new Lvalue(Expression, Identifier);
                }

            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_optional_rule_refs_ー_gets_mapped_from_nullable_rule_getter_methods()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            assignment : lvalue '=' expr? ;
            lvalue : (expr '.')? ID ;
            expr : ID ;
            """);
        Assert.StartsWith("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Assignment VisitAssignment(FooParser.AssignmentContext context)
                {
                    var Lvalue = Visit(context.lvalue());
                    var Expression = context.expr()?.Accept(this);
                    return new Assignment(Lvalue, Expression);
                }

                public override Lvalue VisitLvalue(FooParser.LvalueContext context)
                {
                    var Expression = context.expr()?.Accept(this);
                    var Identifier = context.ID().GetText();
                    return new Lvalue(Expression, Identifier);
                }

            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    AstCodeGenerator GetGeneratorForGrammar(string grammarCode)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        Assert.Empty(grammar.ErrorMessages);
        return new AstCodeGenerator(grammar);
    }
}
