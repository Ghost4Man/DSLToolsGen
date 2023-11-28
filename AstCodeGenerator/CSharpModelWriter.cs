using DSLToolsGenerator.Models;

namespace DSLToolsGenerator;

public class CSharpModelWriter : CodeGeneratingModelVisitor
{
    public CSharpModelWriter(TextWriter output) : base(output) { }

    string TypeName(string typeName, bool nullable) => typeName + (nullable ? "?" : "");
    string TypeName(NodeClassModel nodeClass, bool nullable) => TypeName(nodeClass.Name, nullable);

    public override void Visit(NodeReferencePropertyModel prop) => Output.Write($"{TypeName(prop.NodeClass, prop.Optional)} {prop.Name}");
    public override void Visit(TokenTextPropertyModel prop) => Output.Write($"{TypeName("string", prop.Optional)} {prop.Name}");
    public override void Visit(TokenTextListPropertyModel prop) => Output.Write($"IList<string> {prop.Name}");
    public override void Visit(OptionalTokenPropertyModel prop) => Output.Write($"bool {prop.Name}");
    public override void Visit(NodeReferenceListPropertyModel prop) => Output.Write($"IList<{prop.NodeClass.Name}> {prop.Name}");

    public override void Visit(NodeClassModel nodeClass)
    {
        string @abstract = nodeClass.Variants.Count > 0 ? "abstract " : "";

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

            public partial interface IAstNode { }

            {{_ => VisitAll(astModel.NodeClasses, "")}}

            {{_ => Visit(astModel.AstBuilder)}}
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
            Output.WriteCode($$"""
                public override {{astClass.Name}} Visit{{rule.Name.Capitalize()}}({{
                    astBuilderModel.GetRuleContextClassName(rule)}} context)
                {
                    {{astClass.Properties.MakeString("\n", p =>
                        $"var {p.Name} = {GetCodeForExtractingValueFromParseTree(p)};")}}
                    return new {{astClass.Name}}({{astClass.Properties.MakeString(", ", p => p.Name)}});
                }
                """);
        }
    }

    // assumes the parse tree context object is available in a `context` variable
    string GetCodeForExtractingValueFromParseTree(PropertyModel property)
    {
        return property switch {
            TokenTextPropertyModel(_, var label, var token, var opt) =>
                $"context.{tokenAccessor(label, token)}{(opt ? "?" : "")}.{tokenTextAccessor(label)}",
            TokenTextListPropertyModel(_, var label, var token) =>
                $"Array.ConvertAll(context.{tokenAccessor(label, token)}, t => t.{tokenTextAccessor(label)})",
            OptionalTokenPropertyModel(_, var label, var token) =>
                $"context.{tokenAccessor(label, token)} != null",
            NodeReferencePropertyModel(_, var label, var nodeClass, var opt) =>
                opt ? $"context.{ruleContextAccessor(label, nodeClass)}?.Accept(this)"
                    : $"Visit(context.{ruleContextAccessor(label, nodeClass)})",
            NodeReferenceListPropertyModel(_, var label, var nodeClass) =>
                $"VisitAll(context.{ruleContextAccessor(label, nodeClass)})",
        };

        string tokenAccessor(string? label, ResolvedTokenRef token) => label ?? (token.Name + "()");
        string tokenTextAccessor(string? label) => label != null ? "Text" : "GetText()";
        string ruleContextAccessor(string? label, NodeClassModel nodeClass) => label ?? (nodeClass.ParserRule.Name + "()");
    }

    /// <summary>
    /// Creates a new <see cref="CSharpModelWriter"/> instance and uses it
    /// to write the specified <paramref name="model"/> to a string of C# code.
    /// </summary>
    /// <param name="model">The codegen model to write to a string.</param>
    /// <returns>A string with C# code representing the <paramref name="model"/>.</returns>
    public static string ModelToString(IModel model)
    {
        var writer = new StringWriter { NewLine = "\n" };
        var visitor = new CSharpModelWriter(writer);
        visitor.Visit(model);
        return writer.ToString();
    }

    public static string ModelToString(IEnumerable<IModel> models)
        => ModelToString(models, (v, mm) => v.VisitAll(mm, separator: ""));

    public static string ModelToString<TModel>(TModel model, Action<CSharpModelWriter, TModel> visitorAction)
    {
        var writer = new StringWriter { NewLine = "\n" };
        var visitor = new CSharpModelWriter(writer);
        visitorAction(visitor, model);
        return writer.ToString();
    }
}
