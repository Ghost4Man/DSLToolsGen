using System.CodeDom.Compiler;

using DSLToolsGenerator.Models;

namespace DSLToolsGenerator;

public class CSharpModelWriter(IndentedTextWriter output) : CodeGeneratingModelVisitor(output)
{
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
}
