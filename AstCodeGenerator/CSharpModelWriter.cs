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
        Output.Write($"public {@abstract}partial record {nodeClass.Name}");
        if (nodeClass.Properties.Count > 0)
        {
            Output.Write("(");
            VisitAll(nodeClass.Properties, separator: ", ");
            Output.Write(")");
        }
        Output.WriteLine($" : {nodeClass.BaseClass?.Name ?? "IAstNode"};");
        Output.Indent++;
        VisitAll(nodeClass.Variants, "");
        Output.Indent--;
    }

    public override void Visit(AstCodeModel astModel)
    {
        Output.WriteLine("""
            #nullable enable
            using System;
            using System.Collections.Generic;

            public partial interface IAstNode { }

            """);

        VisitAll(astModel.NodeClasses, "");
    }

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
