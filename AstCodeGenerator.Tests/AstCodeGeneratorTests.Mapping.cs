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

    readonly string visitMethodForSimpleIdExpression = """
        public override Expression VisitExpr(FooParser.ExprContext context)
        {
            var Identifier = context.ID().GetText();
            return new Expression(Identifier);
        }
    """.TrimStart();

    [Fact]
    public void given_1_rule_with_two_ID_token_refs_ー_gets_mapped_from_Text_of_indexed_tokens()
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
    public void given_1_rule_with_multiple_ID_token_refs_ー_gets_mapped_from_Text_of_indexed_tokens()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'import' ID ('from' (ID | ID '.' ID) | 'as' ID)? ;
            """);
        Assert.Equal("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Statement VisitStat(FooParser.StatContext context)
                {
                    var Identifier1 = context.ID(0).GetText();
                    var Identifier2 = context.ID(1)?.GetText();
                    var Identifier3 = context.ID(2)?.GetText();
                    return new Statement(Identifier1, Identifier2, Identifier3);
                }
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_unmappable_ID_token_refs_ー_falls_back_to_list_of_all_ID_tokens()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'import' (ID '.')? ID ('from' (ID | ID '.' ID) | 'as' ID)? ;
            """);
        Assert.Equal("""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override Statement VisitStat(FooParser.StatContext context)
                {
                    var Identifiers = Array.ConvertAll(context.ID(), t => t.GetText());
                    return new Statement(Identifiers);
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
    public void given_1_rule_with_rule_refs_ー_gets_mapped_from_rule_getters()
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

                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_optional_rule_refs_ー_gets_mapped_from_nullable_rule_getters()
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
            
                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_multiple_rule_refs_ー_gets_mapped_from_visited_indexed_rule_getters()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            ifExpr : 'if' expr 'then' expr ('else' expr)? ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override IfExpression VisitIfExpr(FooParser.IfExprContext context)
                {
                    var Expression1 = Visit(context.expr(0));
                    var Expression2 = Visit(context.expr(1));
                    var Expression3 = context.expr(2)?.Accept(this);
                    return new IfExpression(Expression1, Expression2, Expression3);
                }
            
                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_labeled_rule_refs_ー_gets_mapped_from_visited_labeled_rule_contexts()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            ifExpr : 'if' conds+=expr 'then' then+=expr ('elif' conds+=expr then+=expr)* ('else' elseExpr=expr)? ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override IfExpression VisitIfExpr(FooParser.IfExprContext context)
                {
                    var Conditions = Array.ConvertAll(context._conds, VisitExpr);
                    var Then = Array.ConvertAll(context._then, VisitExpr);
                    var ElseExpression = context.elseExpr?.Accept(this);
                    return new IfExpression(Conditions, Then, ElseExpression);
                }
            
                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_rule_refs_with_labels_that_are_CSharp_keywords_ー_gets_mapped_from_escaped_labels()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            ifExpr : 'if' if=expr 'do' do=expr ('else' else=expr)? ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<IAstNode>
            {
                public override IfExpression VisitIfExpr(FooParser.IfExprContext context)
                {
                    var If = Visit(context.@if);
                    var Do = Visit(context.@do);
                    var Else = context.@else?.Accept(this);
                    return new IfExpression(If, Do, Else);
                }
            
                {{visitMethodForSimpleIdExpression}}
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
