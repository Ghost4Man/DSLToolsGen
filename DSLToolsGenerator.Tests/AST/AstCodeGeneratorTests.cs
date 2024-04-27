using static DSLToolsGenerator.AST.CSharpModelWriter;

namespace DSLToolsGenerator.AST.Tests;

public class AstCodeGeneratorTests(ITestOutputHelper testOutput) : CodegenTestFixture(testOutput)
{
    const string expectedProlog = """
        #nullable enable
        using System;
        using System.Linq;
        using System.Collections.Generic;
        """;

    const string expectedAstNodeClassDeclaration = """
        public abstract partial record AstNode
        {
            public Antlr4.Runtime.ParserRuleContext? ParserContext { get; init; }
            public abstract bool IsMissing { get; }
            public abstract IEnumerable<AstNode?> GetChildNodes();
        
            public IEnumerable<AstNode> GetAllDescendantNodes()
                => GetChildNodes().SelectMany(GetNonNullDescendantNodesAndSelf);
        
            public IEnumerable<AstNode> GetAllDescendantNodesAndSelf()
                => GetChildNodes().SelectMany(GetNonNullDescendantNodesAndSelf).Prepend(this);
        
            static IEnumerable<AstNode> GetNonNullDescendantNodesAndSelf(AstNode? node)
                => node?.GetChildNodes().SelectMany(GetNonNullDescendantNodesAndSelf).Prepend(node) ?? [];
        }
        """;

    const string grammarProlog = """
        grammar TestGrammar;
        ID : [a-zA-Z_][a-zA-Z0-9_]+ ;
        NUMBER : [0-9]+ ;
        FLOAT : INT '.' INT ;
        STR_LIT : '"' ~["]+ '"' ;
        COMMA : ',' ;
        """;

    /*
     useful Unicode letters valid in C# (test name) identifiers:
        ǃ  (U+01C3)
        ǀ  (U+01C0)
        ー (U+30FC)
     */

    [Fact]
    public void given_1_empty_rule_ー_generates_prolog_and_parameterless_node_class_and_AstBuilder_and_Extensions_class()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            break : 'break' ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}

            {{expectedAstNodeClassDeclaration}}

            public partial record Break : AstNode;

            partial record Break
            {
                public static readonly Break Missing = new();
                public override bool IsMissing => ReferenceEquals(this, Missing);

                public new TestGrammarParser.BreakContext? ParserContext
                {
                    get => (TestGrammarParser.BreakContext?)base.ParserContext;
                    init => base.ParserContext = value;
                }

                public override IEnumerable<AstNode?> GetChildNodes()
                    => [];
            }

            public class AstBuilder : TestGrammarBaseVisitor<AstNode>
            {
                public string MissingTokenPlaceholderText { get; init; } = "\u2370"; // question mark in a box

                public override Break VisitBreak(TestGrammarParser.BreakContext? context)
                {
                    if (context is null) return Break.Missing;

                    return new Break() { ParserContext = context };
                }
            }

            file static class Extensions
            {
                public static TOut Accept<TIn, TOut>(this TIn parseTreeNode, Func<TIn, TOut> visitFn)
                    => visitFn(parseTreeNode);
            }
            """,
            ModelToString(g.GenerateAstCodeModel()).TrimEnd());
    }

    [Fact]
    public void given_1_empty_rule_and_namespace_config_ー_generates_prolog_and_using_and_namespace_declaration()
    {
        Configuration config = new() {
            Ast = new() { Namespace = new("Foo.AST") },
            Parser = new() { Namespace = new("Foo.Parser") }
        };
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            break : 'break' ;
            """, config);
        Assert.StartsWith($$"""
            {{expectedProlog}}
            using Foo.Parser;

            namespace Foo.AST;

            {{expectedAstNodeClassDeclaration}}

            public partial record Break : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel(), config).TrimEnd());
    }

    [Fact]
    public void given_abbreviation_expansion_config_ー_generates_node_classes_and_properties_with_expanded_names()
    {
        Configuration config = new() {
            Ast = new() {
                AutomaticAbbreviationExpansion = {
                    CustomWordExpansionsRaw = {
                        [new(["comptime", "ct"])] = "compileTime",
                        [new(["stat"])] = "statistic",
                        [new(["exp"])] = "expression",
                        [new(["aggr"])] = "aggressivity",
                        [new(["id"])] = "id",
                        [new(["Δ"])] = "delta",
                    }
                }
            }
        };
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            comptimeIf : '#if' '(' cond=ctExp ')' ;
            ctExp : ID           #idExp
                  | txt=STR_LIT  #strLitExp ;
            stats : 'stats' '{' 'aggresivity' ':' aggr=NUMBER ',' 'delta' ':' Δ=NUMBER '}' ;
            """, config);
        Assert.Equal("""
            public partial record CompileTimeIf(CompileTimeExpression Condition) : AstNode;
            public abstract partial record CompileTimeExpression : AstNode;
                public partial record IdExpression(string Id) : CompileTimeExpression;
                public partial record StringLiteralExpression(string Text) : CompileTimeExpression;
            public partial record Statistics(string Aggressivity, string Delta) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses, config).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_labeled_ID_tokens_ー_generates_record_with_2_string_properties()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'set' varName=ID '=' expr=ID ;
            """);
        Assert.Equal("""
            public partial record Statement(string VariableName, string Expression) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Theory]
    [InlineData("ID", "string", "Identifier")]
    [InlineData("NUMBER", "int", "Number")]
    public void given_1_rule_with_2_unlabeled_tokens_ー_generates_record_with_2_properties_with_prefixes_Left_and_Right(
        string tokenType, string expectedPropertyType, string expectedPropertyNameSuffix)
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'set' {{tokenType}} '=' {{tokenType}} ;
            """);
        Assert.Equal($$"""
            public partial record Statement({{expectedPropertyType}} Left{{expectedPropertyNameSuffix}}, {{
                expectedPropertyType}} Right{{expectedPropertyNameSuffix}}) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_3_unlabeled_tokens_ー_generates_record_with_3_numbered_properties()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'if' ID 'then' ID 'else' ID ;
            """);
        Assert.Equal("""
            public partial record Statement(string Identifier1, string Identifier2, string Identifier3) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Theory]
    [InlineData("ID", "string Identifier")]
    [InlineData("NUMBER", "int Number")]
    public void given_1_rule_with_1_labeled_token_ー_generates_record_with_a_property_of_the_correct_type_and_name(
        string tokenType, string expectedProperty)
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            print : 'print' {{tokenType}} ;
            """);
        Assert.Equal($$"""
            public partial record Print({{expectedProperty}}) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_just_literals_ー_generates_record_without_parameter_list()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            break : 'break' ;
            """);
        Assert.Equal("""
            public partial record Break : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_labeled_optional_literal_token_ー_generates_record_with_bool_property()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            fnDef : isPublic='public'? 'fn' 'foo' '{' '}' ;
            """);
        Assert.Equal("""
            public partial record FunctionDefinition(bool IsPublic) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_token_list_ー_generates_records_with_string_list_property_with_plural_name()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            importStmt : 'import' ID+ ;
            """);
        Assert.Equal("""
            public partial record ImportStatement(IList<string> Identifiers) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void considers_tokens_nested_in_parentheses()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            assignment : (ID) '=' ((ID)) ;
            """);
        Assert.Equal("""
            public partial record Assignment(string LeftIdentifier, string RightIdentifier) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void considers_rule_refs_nested_in_parentheses()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            assignment : (expr) '=' ((expr)) ;
            expr : ID ;
            """);
        Assert.Equal("""
            public partial record Assignment(Expression LeftExpression, Expression RightExpression) : AstNode;
            public partial record Expression(string Identifier) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Theory]
    [InlineData("ID", "'.'", "string")]
    [InlineData("ID", "commas+=','", "string")]
    [InlineData("id", "COMMA", "Identifier")]
    public void given_1_rule_with_delimited_element_list_ー_generates_records_with_list_property_with_plural_name(
        string element, string delimiter, string expectedListElementType)
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            importStmt : 'import' {{element}} ({{delimiter}} {{element}})* ;
            id : value=ID ;
            """);
        Assert.Equal($$"""
            public partial record ImportStatement(IList<{{expectedListElementType}}> Identifiers) : AstNode;
            public partial record Identifier(string Value) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_token_ref_with_list_label_ー_generates_record_with_string_list_property_with_plural_name()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            idInAList : ids+=ID ;
            """);
        Assert.Equal("""
            public partial record IdentifierInAList(IList<string> Identifiers) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_multiple_token_refs_with_same_label_ー_generates_records_with_string_list_property_with_plural_name()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            fnCall : 'call' '(' args+=ID ',' args+=ID ',' args+=ID ')' ;
            """);
        Assert.Equal("""
            public partial record FunctionCall(IList<string> Arguments) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_multiple_rule_refs_with_same_label_ー_generates_records_with_string_list_property_with_plural_name()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            fnCall : 'call' '(' args+=expr ',' args+=expr ',' args+=expr ')' ;
            expr : ID ;
            """);
        Assert.Equal("""
            public partial record FunctionCall(IList<Expression> Arguments) : AstNode;
            public partial record Expression(string Identifier) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_simple_rules_with_unlabeled_rule_references_without_repetition_ー_generates_records_with_Node_reference_properties()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            fnDef : 'fn' ID '{' cmd '}' ;
            cmd : 'print' expr ;
            expr : ID ;
            """);
        Assert.Equal("""
            public partial record FunctionDefinition(string Identifier, Command Command) : AstNode;
            public partial record Command(Expression Expression) : AstNode;
            public partial record Expression(string Identifier) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_simple_rules_with_unlabeled_optional_rule_references_ー_generates_records_with_nullable_Node_reference_properties()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            fnDef : 'fn' ID '{' cmd? '}' ;
            cmd : 'print' expr? ;
            expr : ID ;
            """);
        Assert.Equal("""
            public partial record FunctionDefinition(string Identifier, Command? Command) : AstNode;
            public partial record Command(Expression? Expression) : AstNode;
            public partial record Expression(string Identifier) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_simple_rules_with_unlabeled_star_rule_references_ー_generates_records_with_Node_reference_list_properties_with_plural_name()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            fnDef : 'fn' ID '{' cmd* '}' ;
            cmd : 'print' expr+ ;
            expr : ID ;
            """);
        Assert.Equal("""
            public partial record FunctionDefinition(string Identifier, IList<Command> Commands) : AstNode;
            public partial record Command(IList<Expression> Expressions) : AstNode;
            public partial record Expression(string Identifier) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact(Skip = "Low priority")]
    public void given_rule_with_unlabeled_alts_ー_generates_autonamed_derived_records()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            expr : ID | NUMBER | STR_LIT ;
            """);
        Assert.Equal("""
            public abstract partial record Expression : AstNode;
                public partial record IdentifierExpression(string Identifier) : Expression;
                public partial record NumberExpression(string Number) : Expression;
                public partial record StringLiteralExpression(string StringLiteral) : Expression;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_labeled_alts_ー_generates_derived_records_with_names_from_labels()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            cmd : 'print' expr ;
            expr : ID      #varRefExpr
                 | NUMBER  #numericLiteralExpr
                 | STR_LIT #strLitExpr ;
            """);
        Assert.Equal("""
            public partial record Command(Expression Expression) : AstNode;
            public abstract partial record Expression : AstNode;
                public partial record VariableReferenceExpression(string Identifier) : Expression;
                public partial record NumericLiteralExpression(string Number) : Expression;
                public partial record StringLiteralExpression(string StringLiteral) : Expression;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_labeled_alts_ー_generates_extra_Missing_variant()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            expr : NUMBER   #numExpr
                 | STR_LIT  #strExpr ;
            """);
        Assert.StartsWith($$"""
            {{expectedProlog}}

            {{expectedAstNodeClassDeclaration}}

            public abstract partial record Expression : AstNode;
                public partial record NumberExpression(string Number) : Expression;
                public partial record StringExpression(string StringLiteral) : Expression;

            partial record Expression
            {
                public static readonly Expression Missing = new MissingExpression();
            }

            public sealed partial record MissingExpression : Expression
            {
                public override bool IsMissing => true;
                public override IEnumerable<AstNode?> GetChildNodes() => [];
            }
            """,
            ModelToString(g.GenerateAstCodeModel()).TrimEnd());
    }

    public class RulesWithReferenceLoops(ITestOutputHelper testOutput) : CodegenTestFixture(testOutput)
    {
        [Fact]
        public void given_rule_with_self_reference_ー_does_not_blow_up()
        {
            AstCodeGenerator g = GetGeneratorForGrammar($$"""
                {{grammarProlog}}
                expr : 'not'? expr ;
                """);
            Assert.Equal("""
                public partial record Expression(Expression Expression) : AstNode;
                """,
                ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
        }

        [Fact]
        public void given_2_rules_with_reference_cycle_ー_does_not_blow_up()
        {
            AstCodeGenerator g = GetGeneratorForGrammar($$"""
                {{grammarProlog}}
                cmd : 'print' expr ;
                expr : '{' cmd '}' ;
                """);
            Assert.Equal("""
                public partial record Command(Expression Expression) : AstNode;
                public partial record Expression(Command Command) : AstNode;
                """,
                ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
        }

        [Fact]
        public void given_multiple_rules_with_reference_cycles_ー_does_not_blow_up()
        {
            AstCodeGenerator g = GetGeneratorForGrammar($$"""
                {{grammarProlog}}
                stmt : expr '=' expr ';'   #assignmentStmt
                     | expr ';'            #exprStmt
                     | block               #blockStmt ;
                expr : (ID | NUMBER | STR_LIT)       #atomicExpr
                     | expr '+' expr                 #addExpr 
                     | expr '(' argList ')'          #fnCallExpr
                     | '(' paramList ')' '=>' block  #lambdaExpr ;
                argList : (expr (',' expr)*)? ;
                paramList : (ID (',' ID)*)? ;
                block : '{' stmt* '}' ;
                """);
            Assert.Equal("""
                public abstract partial record Statement : AstNode;
                    public partial record AssignmentStatement(Expression LeftExpression, Expression RightExpression) : Statement;
                    public partial record ExpressionStatement(Expression Expression) : Statement;
                    public partial record BlockStatement(Block Block) : Statement;
                public abstract partial record Expression : AstNode;
                    public partial record AtomicExpression(string? Identifier, string? Number, string? StringLiteral) : Expression;
                    public partial record AddExpression(Expression LeftExpression, Expression RightExpression) : Expression;
                    public partial record FunctionCallExpression(Expression Expression, ArgumentList ArgumentList) : Expression;
                    public partial record LambdaExpression(ParameterList ParameterList, Block Block) : Expression;
                public partial record ArgumentList(IList<Expression> Expressions) : AstNode;
                public partial record ParameterList(IList<string> Identifiers) : AstNode;
                public partial record Block(IList<Statement> Statements) : AstNode;
                """,
                ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
        }
    }

    [Fact]
    public void given_rule_with_labeled_list_rule_refs_inside_repeated_block_ー_generates_records_with_list_properties()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            program : (statements+=stat | imports+=importDirective)* EOF ;
            stat : 'break' ;
            importDirective : 'import' ID ;
            """);
        Assert.Equal("""
            public partial record Program(IList<Statement> Statements, IList<ImportDirective> Imports) : AstNode;
            public partial record Statement : AstNode;
            public partial record ImportDirective(string Identifier) : AstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_setting_for_node_class_naming_ー_generated_class_names_uses_given_prefix_and_suffix()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            printStmt : 'print' expr ;
            expr : STR_LIT       #stringLiteral
                 | expr '+' expr #addExpr
                 ;
            """,
            new Configuration {
                Ast = new() {
                    NodeClassNaming = new() { Prefix = "My", Suffix = "Node" }
                }
            });
        Assert.Equal("""
            public partial record MyPrintStatementNode(MyExpressionNode Expression) : AstNode;
            public abstract partial record MyExpressionNode : AstNode;
                public partial record MyStringLiteralNode(string StringLiteral) : MyExpressionNode;
                public partial record MyAddExpressionNode(MyExpressionNode LeftExpression, MyExpressionNode RightExpression) : MyExpressionNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }
}
