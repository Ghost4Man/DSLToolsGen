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
    string GetCodeForExtractingValueFromParseTree(PropertyModel property) => property switch {
        TokenTextPropertyModel(_, var token, var opt) =>
            $"context.{token.Name}(){(opt ? "?" : "")}.GetText()",
        TokenTextListPropertyModel(_, var token) =>
            $"Array.ConvertAll(context.{token.Name}(), t => t.GetText())",
        OptionalTokenPropertyModel(_, var token) =>
            $"context.{token.Name}() != null",
        NodeReferencePropertyModel(_, var nodeClass, var opt) =>
            $"Visit(context.{nodeClass.ParserRule.Name}())",
        NodeReferenceListPropertyModel(_, var nodeClass) =>
            $"VisitAll(context.{nodeClass.ParserRule.Name}())",
    };

    /// <summary>
    /// Creates a new <see cref="CSharpModelWriter"/> instance and uses it
    /// to write the specified <paramref name="model"/> to a string of C# code.
    /// </summary>
    /// <param name="model">The codegen model to write to a string.</param>
    /// <returns>A string with C# code representing the <paramref name="model"/>.</returns>
    public static string ModelToString(IModel model)
    {
        var writer = new StringWriter();
        var visitor = new CSharpModelWriter(writer);
        visitor.Visit(model);
        return writer.ToString();
    }

    public static string ModelToString(IEnumerable<IModel> models)
        => ModelToString(models, (v, mm) => v.VisitAll(mm, separator: ""));

    public static string ModelToString<TModel>(TModel model, Action<CSharpModelWriter, TModel> visitorAction)
    {
        var writer = new StringWriter();
        var visitor = new CSharpModelWriter(writer);
        visitorAction(visitor, model);
        return writer.ToString();
    }
}
