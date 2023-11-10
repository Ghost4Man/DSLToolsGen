namespace DSLToolsGenerator.Tests;

public class AstCodeGeneratorTests
{
    const string expectedProlog = """
        #nullable enable
        using System;
        using System.Collections.Generic;

        public partial interface IAstNode { }

        """;

    const string grammarProlog = """
        grammar TestGrammar;
        ID : [a-zA-Z_][a-zA-Z0-9_]+ ;
        NUMBER : [0-9]+ ;
        FLOAT : INT '.' INT ;
        STR_LIT : '"' ~["]+ '"' ;
        """;

    /*
     useful Unicode letters valid in C# (test name) identifiers:
        ǃ  (U+01C3)
        ǀ  (U+01C0)
        ー (U+30FC)
     */

    [Fact]
    public void given_1_rule_with_labeled_ID_tokens_ー_generates_record_with_2_string_properties()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            stat : 'set' varName=ID '=' expr=ID ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Statement(string VariableName, string Expression) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Theory]
    [InlineData("ID", "string", "Identifier")]
    [InlineData("NUMBER", "int", "Number")]
    public void given_1_rule_with_2_unlabeled_tokens_ー_generates_record_with_2_properties_with_prefixes_Left_and_Right(
        string tokenType, string expectedPropertyType, string expectedPropertyNameSuffix)
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            stat : 'set' {{tokenType}} '=' {{tokenType}} ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Statement({{expectedPropertyType}} Left{{expectedPropertyNameSuffix}}, {{
                expectedPropertyType}} Right{{expectedPropertyNameSuffix}}) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd(), ignoreWhiteSpaceDifferences: true);
    }

    [Fact]
    public void given_1_rule_with_3_unlabeled_tokens_ー_generates_record_with_3_numbered_properties()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            stat : 'if' ID 'then' ID 'else' ID ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Statement(string Identifier1, string Identifier2, string Identifier3) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd(), ignoreWhiteSpaceDifferences: true);
    }

    [Theory]
    [InlineData("ID", "string Identifier")]
    [InlineData("NUMBER", "int Number")]
    public void given_1_rule_with_1_labeled_token_ー_generates_record_with_a_property_of_the_correct_type_and_name(
        string tokenType, string expectedProperty)
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            print : 'print' {{tokenType}} ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Print({{expectedProperty}}) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_just_literals_ー_generates_record_without_parameter_list()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            break : 'break' ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Break : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_labeled_optional_literal_token_ー_generates_record_with_bool_property()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            fnDef : isPublic='public'? 'fn' 'foo' '{' '}' ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record FunctionDefinition(bool IsPublic) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_simple_rules_with_unlabeled_rule_references_without_repetition_ー_generates_records_with_Node_reference_properties()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            fnDef : 'fn' ID '{' cmd '}' ;
            cmd : 'print' expr ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record FunctionDefinition(string Identifier, Command Command) : IAstNode;
            public partial record Command(Expression Expression) : IAstNode;
            public partial record Expression(string Identifier) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_simple_rules_with_unlabeled_optional_rule_references_ー_generates_records_with_nullable_Node_reference_properties()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            fnDef : 'fn' ID '{' cmd? '}' ;
            cmd : 'print' expr? ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record FunctionDefinition(string Identifier, Command? Command) : IAstNode;
            public partial record Command(Expression? Expression) : IAstNode;
            public partial record Expression(string Identifier) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_simple_rules_with_unlabeled_star_rule_references_ー_generates_records_with_Node_reference_list_properties_with_plural_name()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            fnDef : 'fn' ID '{' cmd* '}' ;
            cmd : 'print' expr+ ;
            expr : ID ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record FunctionDefinition(string Identifier, IList<Command> Commands) : IAstNode;
            public partial record Command(IList<Expression> Expressions) : IAstNode;
            public partial record Expression(string Identifier) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_rule_with_unlabeled_alts_ー_generates_autonamed_derived_records()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            expr : ID | NUMBER | STR_LIT ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public abstract partial record Expression : IAstNode;
                public partial record IdentifierExpression(string Identifier) : Expression;
                public partial record NumberExpression(string Number) : Expression;
                public partial record StringLiteralExpression(string StringLiteral) : Expression;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_rule_with_labeled_alts_ー_generates_derived_records_with_names_from_labels()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            cmd : 'print' expr ;
            expr : ID      #varRefExpr
                 | NUMBER  #numericLiteralExpr
                 | STR_LIT #strLitExpr ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Command(Expression Expression) : IAstNode;
            public abstract partial record Expression : IAstNode;
                public partial record VariableReferenceExpression(string Identifier) : Expression;
                public partial record NumericLiteralExpression(string Number) : Expression;
                public partial record StringLiteralExpression(string StringLiteral) : Expression;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_rule_with_self_reference_ー_does_not_blow_up()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            expr : 'not'? expr ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Expression(Expression Expression) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_2_rules_with_reference_cycle_ー_does_not_blow_up()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            cmd : 'print' expr ;
            expr : '{' cmd '}' ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Command(Expression Expression) : IAstNode;
            public partial record Expression(Command Command) : IAstNode;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    [Fact]
    public void given_rule_with_labeled_alts_and_one_transparent_alt_ー_generates_multilevel_record_hierarchy()
    {
        AstCodeGenerator c = GetConverterForGrammar($$"""
            {{grammarProlog}}
            cmd : 'print' expr ;
            expr : expr '*' expr  #multExpr
                 | expr '+' expr  #addExpr 
                 | atomicExpr     #atomicExpr ;
            atomicExpr
                : ID       #varRefExpr
                | NUMBER   #numericLiteralExpr
                | STR_LIT  #strLitExpr ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Command(Expression Expression) : IAstNode;
            public abstract partial record Expression : IAstNode;
                public partial record MultiplyExpression(Expression LeftExpression, Expression RightExpression) : Expression;
                public partial record AddExpression(Expression LeftExpression, Expression RightExpression) : Expression;
                public abstract partial record AtomicExpression : Expression;
                    public partial record VariableReferenceExpression(string Identifier) : AtomicExpression;
                    public partial record NumericLiteralExpression(string Number) : AtomicExpression;
                    public partial record StringLiteralExpression(string StringLiteral) : AtomicExpression;
            """, c.GenerateFullAstCode().TrimEnd());
    }

    AstCodeGenerator GetConverterForGrammar(string grammarCode)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        Assert.Empty(grammar.ErrorMessages);
        return new AstCodeGenerator(grammar);
    }
}
