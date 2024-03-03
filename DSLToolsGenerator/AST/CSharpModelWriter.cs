using System.Diagnostics.CodeAnalysis;

using Antlr4Ast;

using DSLToolsGenerator.AST.Models;

namespace DSLToolsGenerator.AST;

public class CSharpModelWriter : CodeGeneratingModelVisitor
{
    readonly AstConfiguration config;

    public CSharpModelWriter(TextWriter output, AstConfiguration config) : base(output)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
    }

    string TypeName(string typeName, bool nullable) => typeName + (nullable ? "?" : "");
    string TypeName(NodeClassModel nodeClass, bool nullable) => TypeName(nodeClass.Name, nullable);

    // make sure the provided string is a valid C# identifier
    // (escape with '@' if needed to avoid conflict with a keyword)
    [return: NotNullIfNotNull(nameof(identifier))]
    string? Identifier(string? identifier) => identifier switch {
        "abstract" or "as" or "base" or "bool" or "break" or "byte" or "case" or
        "catch" or "char" or "checked" or "class" or "const" or "continue" or
        "decimal" or "default" or "delegate" or "do" or "double" or "else" or
        "enum" or "event" or "explicit" or "extern" or "false" or "finally" or
        "fixed" or "float" or "for" or "foreach" or "goto" or "if" or "implicit" or
        "in" or "int" or "interface" or "internal" or "is" or "lock" or "long" or
        "namespace" or "new" or "null" or "object" or "operator" or "out" or
        "override" or "params" or "private" or "protected" or "public" or "readonly" or
        "ref" or "return" or "sbyte" or "sealed" or "short" or "sizeof" or "stackalloc" or
        "static" or "string" or "struct" or "switch" or "this" or "throw" or "true" or
        "try" or "typeof" or "uint" or "ulong" or "unchecked" or "unsafe" or "ushort" or
        "using" or "virtual" or "void" or "volatile" or "while"
            => '@' + identifier,
        _ => identifier
    };

    public override void Visit(NodeReferencePropertyModel prop) => Output.Write($"{TypeName(prop.NodeClass.Value, prop.Optional)} {prop.Name}");
    public override void Visit(TokenTextPropertyModel prop) => Output.Write($"{TypeName("string", prop.Optional)} {prop.Name}");
    public override void Visit(TokenTextListPropertyModel prop) => Output.Write($"IList<string> {prop.Name}");
    public override void Visit(OptionalTokenPropertyModel prop) => Output.Write($"bool {prop.Name}");
    public override void Visit(NodeReferenceListPropertyModel prop) => Output.Write($"IList<{prop.NodeClass.Value.Name}> {prop.Name}");

    public override void Visit(NodeClassModel nodeClass)
    {
        string @abstract = nodeClass.IsAbstract ? "abstract " : "";

        Output.WriteCode($"""
            public {@abstract}partial record {nodeClass.Name}{
                    _ => generateRecordParameterList()} : {
                    nodeClass.BaseClass?.Name ?? "AstNode"};
                {_ => VisitAll(nodeClass.Variants, "")}
            """);

        void generateRecordParameterList()
        {
            if (nodeClass.Properties.Count > 0)
            {
                Output.Write("(");
                VisitAll(nodeClass.Properties, separator: ", ");
                Output.Write(")");
            }
        }
    }

    public override void Visit(AstCodeModel astModel)
    {
        Output.WriteCode($$"""
            #nullable enable
            using System;
            using System.Collections.Generic;
            {{(config.AntlrNamespace is string antlrNS ? $"using {antlrNS};" : null)}}

            {{(config.Namespace is string ns ? $"namespace {ns};" : null)}}

            public abstract partial record AstNode
            {
                public Antlr4.Runtime.ParserRuleContext? ParserContext { get; init; }
                public abstract bool IsMissing { get; }
                public abstract IEnumerable<AstNode?> GetChildNodes();
            }

            {{_ => VisitAll(astModel.NodeClasses, "")}}

            {{_ => VisitAll(astModel.GetAllNodeClasses(), "\n",
                nc => GenerateNodeClassBody(nc, astModel.AstBuilder.ParserClassName))}}

            {{_ => Visit(astModel.AstBuilder)}}

            file static class Extensions
            {
                public static TOut Accept<TIn, TOut>(this TIn parseTreeNode, Func<TIn, TOut> visitFn)
                    => visitFn(parseTreeNode);
            }
            """);
    }

    public void GenerateNodeClassBody(NodeClassModel nodeClass, string parserClassName)
    {
        if (nodeClass.IsAbstract)
            GenerateAbstractNodeClassBody(nodeClass);
        else
            GenerateConcreteNodeClassBody(nodeClass, parserClassName);
    }

    void GenerateAbstractNodeClassBody(NodeClassModel nodeClass)
    {
        Output.WriteCode($$"""
            partial record {{nodeClass.Name}}
            {
                public static readonly {{nodeClass.Name}} Missing = new Missing{{nodeClass.Name}}();
            }

            public sealed partial record Missing{{nodeClass.Name}} : {{nodeClass.Name}}
            {
                public override bool IsMissing => true;
                public override IEnumerable<AstNode?> GetChildNodes() => [];
            }
            """);
    }

    void GenerateConcreteNodeClassBody(NodeClassModel nodeClass, string parserClassName)
    {
        string contextClassFullName = $"{parserClassName}.{nodeClass.SourceContextName}Context";
        string @new = nodeClass.BaseClass != null ? "new " : "";

        Output.WriteCode($$"""
            partial record {{nodeClass.Name}}
            {
                public static {{@new}}readonly {{nodeClass.Name}} Missing = new({{
                    _ => VisitAll(nodeClass.Properties, ", ", generateMissingNodeArguments)}});
                public override bool IsMissing => ReferenceEquals(this, Missing);

                public new {{contextClassFullName}}? ParserContext
                {
                    get => ({{contextClassFullName}}?)base.ParserContext;
                    init => base.ParserContext = value;
                }

                public override IEnumerable<AstNode?> GetChildNodes()
                    => {{_ => generateChildNodesCollectionExpression()}};
            }
            """);

        void generateMissingNodeArguments(PropertyModel property)
        {
            Output.WriteCodeInline($"{property.Name}: ");
            switch (property)
            {
                case TokenTextPropertyModel { Optional: true }:
                case NodeReferencePropertyModel { Optional: true }:
                    Output.Write("null"); break;
                case OptionalTokenPropertyModel:
                    Output.Write("false"); break;
                case NodeReferenceListPropertyModel or TokenTextListPropertyModel:
                    Output.Write("[]"); break;
                case TokenTextPropertyModel:
                    Output.WriteCodeInline($"\"<missing {nodeClass.Name}>\""); break;
                case NodeReferencePropertyModel { NodeClass.Value.Name: string nodeClass }:
                    Output.WriteCodeInline($"{nodeClass}.Missing"); break;
                default:
                    Output.Write("default"); break;
            }
        }

        void generateChildNodesCollectionExpression()
        {
            var nodeRefProperties = nodeClass.Properties
                .Where(p => p is NodeReferencePropertyModel or NodeReferenceListPropertyModel)
                .ToList();

            if (nodeRefProperties is [NodeReferenceListPropertyModel singleProperty])
                Output.WriteCodeInline($"this.{singleProperty.Name}");
            else
            {
                Output.WriteCodeInline($"[{_ =>
                    VisitAll(nodeRefProperties, ", ", p =>
                        p is NodeReferenceListPropertyModel
                            ? $"..this.{p.Name}" // use spread operator `..`
                            : $"this.{p.Name}")
                    }]");
            }
        }
    }

    public override void Visit(AstBuilderModel astBuilderModel)
    {
        Output.WriteCode($$"""
            public class AstBuilder : {{astBuilderModel.AntlrGrammarName}}BaseVisitor<AstNode>
            {
                public string MissingTokenPlaceholderText { get; init; } = "\u2370"; // question mark in a box

                {{_ => VisitAll(astBuilderModel.AstMapping, "\n", visit)}}
            }
            """);

        void visit(AstMappingModel astMapping)
        {
            var (rule, astClass) = astMapping;
            string contextName = astClass.SourceContextName;
            string contextClassName = $"{astBuilderModel.ParserClassName}.{contextName}Context";

            if (astClass.IsAbstract)
            {
                Output.WriteCode($$"""
                    public virtual {{astClass.Name}} Visit{{contextName}}({{contextClassName}}? context)
                    {
                        if (context is null) return {{astClass.Name}}.Missing;
                    
                        return ({{astClass.Name}})Visit(context);
                    }
                    """);
            }
            else
            {
                Output.WriteCode($$"""
                    public override {{astClass.Name}} Visit{{contextName}}({{contextClassName}}? context)
                    {
                        if (context is null) return {{astClass.Name}}.Missing;

                        {{_ => VisitAll(astClass.Properties, "\n", p =>
                            $"var {p.Name} = {GetCodeForExtractingValueFromParseTree(p)};")}}
                        return new {{astClass.Name}}({{
                            _ => VisitAll(astClass.Properties, ", ", p => p.Name)
                            }}) { ParserContext = context };
                    }
                    """);
            }
        }
    }

    // assumes the parse tree context object is available in a `context` variable
    string GetCodeForExtractingValueFromParseTree(PropertyModel property)
    {
        // examples of token/rule-context accessors generated by ANTLR + the corresponding ValueMappingSource
        //         token ref:       ITerminalNode ID()         FromGetter()
        //         token ref list:  ITerminalNode[] ID()       FromGetter()
        //         rule ref:        ExprContext expr()         FromGetter()
        //         rule ref list:   ExprContext[] expr()       FromGetter()
        // labeled token ref:       IToken name                FromLabel("name", LabelKind.Assign)
        // labeled token ref list:  IList<IToken> _names       FromLabel("names", LabelKind.PlusAssign)
        // labeled rule ref:        ExprContext target         FromLabel("target", LabelKind.Assign)
        // labeled rule ref list:   IList<ExprContext> _args   FromLabel("args", LabelKind.PlusAssign)

        return property switch {
            TokenTextPropertyModel(_, var source, var token, var opt) =>
                $"context.{tokenAccessor(source, token)}?.{tokenTextAccessor(source)}{
                    (opt ? "" : " ?? MissingTokenPlaceholderText")}",
            TokenTextListPropertyModel(_, var source, var token) =>
                $"context.{tokenAccessor(source, token)}.Select(t => t.{tokenTextAccessor(source)}).ToList()",
            OptionalTokenPropertyModel(_, var source, var token) =>
                $"context.{tokenAccessor(source, token)} is not {emptyValue(source)}",
            NodeReferencePropertyModel(_, var source, { Value: var nodeClass }, var opt) =>
                opt ? $"context.{ruleContextAccessor(source, nodeClass)}?.Accept(Visit{nodeClass.SourceContextName})"
                    : $"Visit{nodeClass.SourceContextName}(context.{ruleContextAccessor(source, nodeClass)})",
            NodeReferenceListPropertyModel(_, var source, { Value: var nodeClass }) =>
                $"context.{ruleContextAccessor(source, nodeClass)}.Select(Visit{nodeClass.SourceContextName}).ToList()",
        };

        string tokenAccessor(ValueMappingSource source, ResolvedTokenRef token)
            => accessor(source, token.Name);
        string ruleContextAccessor(ValueMappingSource source, NodeClassModel nodeClass)
            => accessor(source, nodeClass.ParserRule.Name);

        string emptyValue(ValueMappingSource source)
            => source is ValueMappingSource.FromLabel(_, LabelKind.PlusAssign) ? "[]" : "null";

        string accessor(ValueMappingSource source, string tokenOrRuleName) => source switch {
            ValueMappingSource.FromLabel(string label, LabelKind.Assign) => Identifier(label),
            ValueMappingSource.FromLabel(string label, LabelKind.PlusAssign) => '_' + label,
            ValueMappingSource.FromGetter(var index) => $"{Identifier(tokenOrRuleName)}({index})"
        };

        string tokenTextAccessor(ValueMappingSource source)
            => source is ValueMappingSource.FromLabel
                ? "Text" /* IToken.Text */
                : "GetText()" /* ITerminalNode.GetText() */;
    }

    /// <summary>
    /// Creates a new <see cref="CSharpModelWriter"/> instance and uses it
    /// to write the specified <paramref name="model"/> to a string of C# code.
    /// </summary>
    /// <param name="model">The codegen model to write to a string.</param>
    /// <returns>A string with C# code representing the <paramref name="model"/>.</returns>
    public static string ModelToString(IModel model, AstConfiguration? config = null)
    {
        var writer = new StringWriter { NewLine = "\n" };
        var visitor = new CSharpModelWriter(writer, config ?? new());
        visitor.Visit(model);
        return writer.ToString();
    }

    public static string ModelToString(IEnumerable<IModel> models, AstConfiguration? config = null)
        => ModelToString(models, (v, mm) => v.VisitAll(mm, separator: ""), config);

    public static string ModelToString<TModel>(TModel model,
        Action<CSharpModelWriter, TModel> visitorAction, AstConfiguration? config = null)
    {
        var writer = new StringWriter { NewLine = "\n" };
        var visitor = new CSharpModelWriter(writer, config ?? new());
        visitorAction(visitor, model);
        return writer.ToString();
    }
}
