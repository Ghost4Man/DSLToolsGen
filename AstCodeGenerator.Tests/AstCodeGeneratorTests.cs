using static DSLToolsGenerator.CSharpModelWriter;

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
    public void given_1_empty_rule_ー_generates_prolog_and_empty_record()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            break : 'break' ;
            """);
        Assert.Equal($$"""
            {{expectedProlog}}
            public partial record Break : IAstNode;
            """,
            ModelToString(g.GenerateAstCodeModel()).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_labeled_ID_tokens_ー_generates_record_with_2_string_properties()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            stat : 'set' varName=ID '=' expr=ID ;
            """);
        Assert.Equal("""
            public partial record Statement(string VariableName, string Expression) : IAstNode;
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
                expectedPropertyType}} Right{{expectedPropertyNameSuffix}}) : IAstNode;
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
            public partial record Statement(string Identifier1, string Identifier2, string Identifier3) : IAstNode;
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
            public partial record Print({{expectedProperty}}) : IAstNode;
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
            public partial record Break : IAstNode;
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
            public partial record FunctionDefinition(bool IsPublic) : IAstNode;
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
            public partial record ImportStatement(IList<string> Identifiers) : IAstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_comma_delimited_token_list_ー_generates_records_with_string_list_property_with_plural_name()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            importStmt : 'import' ID (',' ID)* ;
            """);
        Assert.Equal("""
            public partial record ImportStatement(IList<string> Identifiers) : IAstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_1_rule_with_multiple_token_refs_with_same_label_ー_generates_records_with_string_list_property_with_plural_name()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            fnCall : 'call' '(' args+=ID ',' args+=ID ',' args+=ID ')' ;
            """);
        Assert.Equal("""
            public partial record FunctionCall(IList<string> Arguments) : IAstNode;
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
            public partial record FunctionCall(IList<Expression> Arguments) : IAstNode;
            public partial record Expression(string Identifier) : IAstNode;
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
            public partial record FunctionDefinition(string Identifier, Command Command) : IAstNode;
            public partial record Command(Expression Expression) : IAstNode;
            public partial record Expression(string Identifier) : IAstNode;
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
            public partial record FunctionDefinition(string Identifier, Command? Command) : IAstNode;
            public partial record Command(Expression? Expression) : IAstNode;
            public partial record Expression(string Identifier) : IAstNode;
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
            public partial record FunctionDefinition(string Identifier, IList<Command> Commands) : IAstNode;
            public partial record Command(IList<Expression> Expressions) : IAstNode;
            public partial record Expression(string Identifier) : IAstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_unlabeled_alts_ー_generates_autonamed_derived_records()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            expr : ID | NUMBER | STR_LIT ;
            """);
        Assert.Equal("""
            public abstract partial record Expression : IAstNode;
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
            public partial record Command(Expression Expression) : IAstNode;
            public abstract partial record Expression : IAstNode;
                public partial record VariableReferenceExpression(string Identifier) : Expression;
                public partial record NumericLiteralExpression(string Number) : Expression;
                public partial record StringLiteralExpression(string StringLiteral) : Expression;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_self_reference_ー_does_not_blow_up()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
            {{grammarProlog}}
            expr : 'not'? expr ;
            """);
        Assert.Equal("""
            public partial record Expression(Expression Expression) : IAstNode;
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
            public partial record Command(Expression Expression) : IAstNode;
            public partial record Expression(Command Command) : IAstNode;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    [Fact]
    public void given_rule_with_labeled_alts_and_one_transparent_alt_ー_generates_multilevel_record_hierarchy()
    {
        AstCodeGenerator g = GetGeneratorForGrammar($$"""
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
        Assert.Equal("""
            public partial record Command(Expression Expression) : IAstNode;
            public abstract partial record Expression : IAstNode;
                public partial record MultiplyExpression(Expression LeftExpression, Expression RightExpression) : Expression;
                public partial record AddExpression(Expression LeftExpression, Expression RightExpression) : Expression;
                public abstract partial record AtomicExpression : Expression;
                    public partial record VariableReferenceExpression(string Identifier) : AtomicExpression;
                    public partial record NumericLiteralExpression(string Number) : AtomicExpression;
                    public partial record StringLiteralExpression(string StringLiteral) : AtomicExpression;
            """,
            ModelToString(g.GenerateAstCodeModel().NodeClasses).TrimEnd());
    }

    AstCodeGenerator GetGeneratorForGrammar(string grammarCode)
    {
        var grammar = Antlr4Ast.Grammar.Parse(grammarCode);
        Assert.Empty(grammar.ErrorMessages);
        return new AstCodeGenerator(grammar);
    }
}
