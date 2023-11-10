using System.CodeDom.Compiler;

using DSLToolsGenerator.Models;

namespace DSLToolsGenerator;

public abstract class CodeGeneratingModelVisitor(IndentedTextWriter output) : IModelVisitor
{
    protected IndentedTextWriter Output { get; } = output;

    public void Visit(IModel model) => model.Accept(this);
    public void Visit(PropertyModel model) => Visit((IModel)model);

    public void VisitAll(IEnumerable<IModel> models, Action? separatorAction)
    {
        bool first = true;
        foreach (IModel model in models)
        {
            if (!first)
                separatorAction?.Invoke();
            Visit(model);
            first = false;
        }
    }

    public void VisitAll(IEnumerable<IModel> models, string separator)
        => VisitAll(models, () => Output.Write(separator));

    public abstract void Visit(AstCodeModel value);
    public abstract void Visit(OptionalTokenPropertyModel value);
    public abstract void Visit(NodeClassModel value);
    public abstract void Visit(NodeReferencePropertyModel value);
    public abstract void Visit(NodeReferenceListPropertyModel value);
    public abstract void Visit(TokenTextPropertyModel value);
    public abstract void Visit(TokenTextListPropertyModel value);
}