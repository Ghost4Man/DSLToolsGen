using static DSLToolsGenerator.AST.CSharpModelWriter;

namespace DSLToolsGenerator.AST.Tests;

public class AstCodeGeneratorTests_Mapping(ITestOutputHelper testOutput) : CodegenTestFixture(testOutput)
{
    const string grammarProlog = """
        grammar Foo;
        ID : [a-zA-Z_][a-zA-Z0-9_]+ ;
        NUMBER : [0-9]+ ;
        FLOAT : INT '.' INT ;
        STR_LIT : '"' ~["]+ '"' ;
        """;

    readonly string visitMethodForSimpleIdExpression = """
        public override Expression VisitExpr(FooParser.ExprContext? context)
        {
            if (context is null) return Expression.Missing;

            var Identifier = context.ID()?.GetText() ?? MissingTokenPlaceholderText;
            return new Expression(Identifier) { ParserContext = context };
        }
    """.TrimStart();

    const string expectedAstBuilderProlog = """
        public string MissingTokenPlaceholderText { get; init; } = "\u2370"; // question mark in a box
        """;

    [Fact]
    public void given_1_rule_with_two_ID_token_refs_ー_gets_mapped_from_Text_of_indexed_tokens()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'swap' ID 'and' ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Statement VisitStat(FooParser.StatContext? context)
                {
                    if (context is null) return Statement.Missing;

                    var LeftIdentifier = context.ID(0)?.GetText() ?? MissingTokenPlaceholderText;
                    var RightIdentifier = context.ID(1)?.GetText() ?? MissingTokenPlaceholderText;
                    return new Statement(LeftIdentifier, RightIdentifier) { ParserContext = context };
                }
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_parser_grammar_ー_generates_correct_parser_class_references()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            parser grammar FooParser;
            stat : 'break' ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooParserBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}
            
                public override Statement VisitStat(FooParser.StatContext? context)
                {
                    if (context is null) return Statement.Missing;

                    return new Statement() { ParserContext = context };
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
            stat : 'import' ID 'from' ID ('as' ID '.' ID)? ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Statement VisitStat(FooParser.StatContext? context)
                {
                    if (context is null) return Statement.Missing;

                    var Identifier1 = context.ID(0)?.GetText() ?? MissingTokenPlaceholderText;
                    var Identifier2 = context.ID(1)?.GetText() ?? MissingTokenPlaceholderText;
                    var Identifier3 = context.ID(2)?.GetText();
                    var Identifier4 = context.ID(3)?.GetText();
                    return new Statement(Identifier1, Identifier2, Identifier3, Identifier4) { ParserContext = context };
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
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Statement VisitStat(FooParser.StatContext? context)
                {
                    if (context is null) return Statement.Missing;

                    var Identifiers = context.ID().Select(t => t.GetText()).ToList();
                    return new Statement(Identifiers) { ParserContext = context };
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
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Print VisitPrint(FooParser.PrintContext? context)
                {
                    if (context is null) return Print.Missing;

                    var Identifiers = context.ID().Select(t => t.GetText()).ToList();
                    return new Print(Identifiers) { ParserContext = context };
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
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Statement VisitStat(FooParser.StatContext? context)
                {
                    if (context is null) return Statement.Missing;

                    var VariableName = context.varName?.Text ?? MissingTokenPlaceholderText;
                    var Expression = context.expr?.Text ?? MissingTokenPlaceholderText;
                    return new Statement(VariableName, Expression) { ParserContext = context };
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
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Statement VisitStat(FooParser.StatContext? context)
                {
                    if (context is null) return Statement.Missing;

                    var VariableName = context.varName?.Text;
                    var Expression = context.expr?.Text;
                    return new Statement(VariableName, Expression) { ParserContext = context };
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
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Assignment VisitAssignment(FooParser.AssignmentContext? context)
                {
                    if (context is null) return Assignment.Missing;

                    var Lvalue = VisitLvalue(context.lvalue());
                    var Expression = VisitExpr(context.expr());
                    return new Assignment(Lvalue, Expression) { ParserContext = context };
                }

                public override Lvalue VisitLvalue(FooParser.LvalueContext? context)
                {
                    if (context is null) return Lvalue.Missing;

                    var Expression = VisitExpr(context.expr());
                    var Identifier = context.ID()?.GetText() ?? MissingTokenPlaceholderText;
                    return new Lvalue(Expression, Identifier) { ParserContext = context };
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
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override Assignment VisitAssignment(FooParser.AssignmentContext? context)
                {
                    if (context is null) return Assignment.Missing;

                    var Lvalue = VisitLvalue(context.lvalue());
                    var Expression = context.expr()?.Accept(VisitExpr);
                    return new Assignment(Lvalue, Expression) { ParserContext = context };
                }

                public override Lvalue VisitLvalue(FooParser.LvalueContext? context)
                {
                    if (context is null) return Lvalue.Missing;

                    var Expression = context.expr()?.Accept(VisitExpr);
                    var Identifier = context.ID()?.GetText() ?? MissingTokenPlaceholderText;
                    return new Lvalue(Expression, Identifier) { ParserContext = context };
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
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override IfExpression VisitIfExpr(FooParser.IfExprContext? context)
                {
                    if (context is null) return IfExpression.Missing;

                    var Expression1 = VisitExpr(context.expr(0));
                    var Expression2 = VisitExpr(context.expr(1));
                    var Expression3 = context.expr(2)?.Accept(VisitExpr);
                    return new IfExpression(Expression1, Expression2, Expression3) { ParserContext = context };
                }
            
                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_rule_with_multiple_unlabeled_rule_refs_and_delimited_list_ー_gets_mapped_from_collection_getter()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            arrayAssign : arr=expr '[' (expr (',' expr)*)? ']' '=' val=expr ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override ArrayAssign VisitArrayAssign(FooParser.ArrayAssignContext? context)
                {
                    if (context is null) return ArrayAssign.Missing;

                    var Array = VisitExpr(context.arr);
                    var Expressions = context.expr().Select(VisitExpr).ToList();
                    var Value = VisitExpr(context.val);
                    return new ArrayAssign(Array, Expressions, Value) { ParserContext = context };
                }
            
                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_multiple_list_labels_ー_gets_mapped_from_list_fields()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            magicExpr : names+=ID values+=expr '%'
                       (names+=ID values+=expr)?
                       ('from' sources+=ID+)? ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override MagicExpression VisitMagicExpr(FooParser.MagicExprContext? context)
                {
                    if (context is null) return MagicExpression.Missing;

                    var Names = context._names.Select(t => t.Text).ToList();
                    var Values = context._values.Select(VisitExpr).ToList();
                    var Sources = context._sources.Select(t => t.Text).ToList();
                    return new MagicExpression(Names, Values, Sources) { ParserContext = context };
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
            ifExpr : 'if' conds+=expr 'then' then+=expr
                  ('elif' conds+=expr 'then' then+=expr)*
                  ('else' elseExpr=expr)? ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override IfExpression VisitIfExpr(FooParser.IfExprContext? context)
                {
                    if (context is null) return IfExpression.Missing;

                    var Conditions = context._conds.Select(VisitExpr).ToList();
                    var Then = context._then.Select(VisitExpr).ToList();
                    var ElseExpression = context.elseExpr?.Accept(VisitExpr);
                    return new IfExpression(Conditions, Then, ElseExpression) { ParserContext = context };
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
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public override IfExpression VisitIfExpr(FooParser.IfExprContext? context)
                {
                    if (context is null) return IfExpression.Missing;

                    var If = VisitExpr(context.@if);
                    var Do = VisitExpr(context.@do);
                    var Else = context.@else?.Accept(VisitExpr);
                    return new IfExpression(If, Do, Else) { ParserContext = context };
                }
            
                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }

    [Fact]
    public void given_rule_with_labeled_alts_ー_generates_Visit_methods_for_alts_and_abstract_base()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stmt : ID '=' expr ';' 	  #assignmentStmt
                 | 'print' expr ';'   #printStmt
                 ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            public class AstBuilder : FooBaseVisitor<AstNode>
            {
                {{expectedAstBuilderProlog}}

                public virtual Statement VisitStmt(FooParser.StmtContext? context)
                {
                    if (context is null) return Statement.Missing;

                    return (Statement)Visit(context);
                }

                public override AssignmentStatement VisitAssignmentStmt(FooParser.AssignmentStmtContext? context)
                {
                    if (context is null) return AssignmentStatement.Missing;

                    var Identifier = context.ID()?.GetText() ?? MissingTokenPlaceholderText;
                    var Expression = VisitExpr(context.expr());
                    return new AssignmentStatement(Identifier, Expression) { ParserContext = context };
                }

                public override PrintStatement VisitPrintStmt(FooParser.PrintStmtContext? context)
                {
                    if (context is null) return PrintStatement.Missing;

                    var Expression = VisitExpr(context.expr());
                    return new PrintStatement(Expression) { ParserContext = context };
                }
            
                {{visitMethodForSimpleIdExpression}}
            }
            """,
            ModelToString(g.GenerateAstCodeModel().AstBuilder).TrimEnd());
    }
}
