using DSLToolsGenerator.AST.Models;

namespace DSLToolsGenerator.AST;

public abstract class CodeGeneratingModelVisitor(TextWriter output) : IModelVisitor
{
    protected IndentedTextWriter Output { get; } = new IndentedTextWriter(output);

    public void Visit(IModel model) => model.Accept(this);
    public void Visit(PropertyModel model) => Visit((IModel)model);

    public void VisitAll<TModel>(IEnumerable<TModel> models, Action? separatorAction, Action<TModel> visitAction)
    {
        bool first = true;
        foreach (TModel model in models)
        {
            if (!first)
                separatorAction?.Invoke();
            visitAction(model);
            first = false;
        }
    }

    public void VisitAll<TModel>(IEnumerable<TModel> models, string separator, Action<TModel> visitAction)
        => VisitAll(models, () => Output.Write(separator), visitAction);

    public void VisitAll<TModel>(IEnumerable<TModel> models, string separator, Func<TModel, string> textSelector)
        => VisitAll(models, () => Output.Write(separator), m => Output.Write(textSelector(m)));

    public void VisitAll(IEnumerable<IModel> models, Action? separatorAction)
        => VisitAll(models, separatorAction, Visit);

    public void VisitAll(IEnumerable<IModel> models, string separator)
        => VisitAll(models, () => Output.Write(separator));

    public abstract void Visit(AstCodeModel value);
    public abstract void Visit(OptionalTokenPropertyModel value);
    public abstract void Visit(NodeClassModel value);
    public abstract void Visit(NodeReferencePropertyModel value);
    public abstract void Visit(NodeReferenceListPropertyModel value);
    public abstract void Visit(TokenTextPropertyModel value);
    public abstract void Visit(TokenTextListPropertyModel value);
    public abstract void Visit(AstBuilderModel value);
}
