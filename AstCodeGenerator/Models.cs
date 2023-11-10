using Antlr4Ast;

using VisitorPatternGenerator;

namespace DSLToolsGenerator.Models;

public partial interface IModel;

[Visitor<IModel>(voidReturn: true)]
public partial interface IModelVisitor;

[Visitor<IModel>(voidReturn: true)]
public partial interface IModelVisitor<in TArg>;

[Acceptor<IModel>]
public partial record AstCodeModel(IList<NodeClassModel> NodeClasses) : IModel;

[Acceptor<IModel>]
public abstract partial record PropertyModel(string Name) : IModel;

[Acceptor<IModel>]
public partial record NodeReferencePropertyModel(string Name, NodeClassModel NodeClass, bool Optional) : PropertyModel(Name);

[Acceptor<IModel>]
public partial record NodeReferenceListPropertyModel(string Name, NodeClassModel NodeClass) : PropertyModel(Name);

[Acceptor<IModel>]
public partial record TokenTextPropertyModel(string Name, ResolvedTokenRef Token, bool Optional) : PropertyModel(Name);

[Acceptor<IModel>]
public partial record TokenTextListPropertyModel(string Name, ResolvedTokenRef Token) : PropertyModel(Name);

[Acceptor<IModel>]
public partial record OptionalTokenPropertyModel(string Name, ResolvedTokenRef Token) : PropertyModel(Name);

[Acceptor<IModel>]
public partial record NodeClassModel(string Name, Rule ParserRule, IList<PropertyModel> Properties) : IModel
{
    NodeClassModel? _baseClass;
    readonly IReadOnlyList<NodeClassModel> _variants = [];

    public NodeClassModel? BaseClass { get => _baseClass; init => _baseClass = value; }

    public IReadOnlyList<NodeClassModel> Variants
    {
        get => _variants;
        init
        {
            _variants = value;
            // initialize the base class of the variant classes to point to this class model
            // (because we can't construct the base class before we have constructed the variants,
            // but to construct the variants, we need a reference to the base class)
            foreach (var variantClass in _variants)
            {
                variantClass._baseClass = this;
            }
        }
    }

}

public record ResolvedTokenRef(string? Name, Literal? Literal, Rule? LexerRule);
