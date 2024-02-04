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
                    nodeClass.BaseClass?.Name ?? "IAstNode"};
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

            public partial interface IAstNode { }

            {{_ => VisitAll(astModel.NodeClasses, "")}}

            {{_ => Visit(astModel.AstBuilder)}}

            file static class Extensions
            {
                public static TOut Accept<TIn, TOut>(this TIn parseTreeNode, Func<TIn, TOut> visitFn)
                    => visitFn(parseTreeNode);
            }
            """);
    }

    public override void Visit(AstBuilderModel astBuilderModel)
    {
        Output.WriteCode($$"""
            public class AstBuilder : {{astBuilderModel.AntlrGrammarName}}BaseVisitor<IAstNode>
            {
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
                    public virtual {{astClass.Name}} Visit{{contextName}}({{contextClassName}} context)
                        => ({{astClass.Name}})Visit(context);
                    """);
            }
            else
            {
                Output.WriteCode($$"""
                    public override {{astClass.Name}} Visit{{contextName}}({{contextClassName}} context)
                    {
                        {{astClass.Properties.MakeString("\n", p =>
                            $"var {p.Name} = {GetCodeForExtractingValueFromParseTree(p)};")}}
                        return new {{astClass.Name}}({{astClass.Properties.MakeString(", ", p => p.Name)}});
                    }
                    """);
            }
        }
    }

    // assumes the parse tree context object is available in a `context` variable
    string GetCodeForExtractingValueFromParseTree(PropertyModel property)
    {
        return property switch {
            TokenTextPropertyModel(_, var label, var token, var opt) =>
                $"context.{tokenAccessor(label, token)}{(opt ? "?" : "")}.{tokenTextAccessor(label)}",
            TokenTextListPropertyModel(_, var label, var token) =>
                $"Array.ConvertAll(context.{tokenAccessor(label?.Prepend("_"), token)}, t => t.{tokenTextAccessor(label)})",
            OptionalTokenPropertyModel(_, var label, var token) =>
                $"context.{tokenAccessor(label, token)} != null",
            NodeReferencePropertyModel(_, var label, { Value: var nodeClass }, var opt) =>
                opt ? $"context.{ruleContextAccessor(label, nodeClass)}?.Accept(Visit{nodeClass.SourceContextName})"
                    : $"Visit{nodeClass.SourceContextName}(context.{ruleContextAccessor(label, nodeClass)})",
            NodeReferenceListPropertyModel(_, var label, { Value: var nodeClass }) =>
                $"context.{ruleContextAccessor(label?.Prepend("_"), nodeClass)}.Select(Visit{nodeClass.SourceContextName}).ToList()",
        };

        string tokenAccessor(string? label, ResolvedTokenRef token)
            => Identifier(label) ?? (Identifier(token.Name) + "()");

        string tokenTextAccessor(string? label)
            => label != null ? "Text" : "GetText()";

        string ruleContextAccessor(string? label, NodeClassModel nodeClass)
            => Identifier(label) ?? (Identifier(nodeClass.ParserRule.Name) + "()");
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

    public static string ModelToString(IEnumerable<IModel> models)
        => ModelToString(models, (v, mm) => v.VisitAll(mm, separator: ""));

    public static string ModelToString<TModel>(TModel model,
        Action<CSharpModelWriter, TModel> visitorAction, AstConfiguration? config = null)
    {
        var writer = new StringWriter { NewLine = "\n" };
        var visitor = new CSharpModelWriter(writer, config ?? new());
        visitorAction(visitor, model);
        return writer.ToString();
    }
}
