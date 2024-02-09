﻿using Antlr4Ast;

using VisitorPatternGenerator;

namespace DSLToolsGenerator.AST.Models;

public partial interface IModel;

[Visitor<IModel>(voidReturn: true)]
public partial interface IModelVisitor;

[Visitor<IModel>(voidReturn: true)]
public partial interface IModelVisitor<in TArg>;

[Acceptor<IModel>]
public partial record AstCodeModel(IList<NodeClassModel> NodeClasses, AstBuilderModel AstBuilder) : IModel;

[Acceptor<IModel>]
public abstract partial record PropertyModel(string Name, ValueMappingSource Source) : IModel;

[Acceptor<IModel>]
public partial record NodeReferencePropertyModel(string Name, ValueMappingSource Source,
    // The Lazy is needed so that we don't get stuck while
    // fetching references to self (or mutual rule references).
    // This assumes we only access the NodeClass property after all NodeClasses are generated
    Lazy<NodeClassModel> NodeClass,
    bool Optional
    ) : PropertyModel(Name, Source);

[Acceptor<IModel>]
public partial record NodeReferenceListPropertyModel(string Name, ValueMappingSource Source,
    Lazy<NodeClassModel> NodeClass) : PropertyModel(Name, Source);

[Acceptor<IModel>]
public partial record TokenTextPropertyModel(string Name, ValueMappingSource Source,
    ResolvedTokenRef Token, bool Optional) : PropertyModel(Name, Source);

[Acceptor<IModel>]
public partial record TokenTextListPropertyModel(string Name, ValueMappingSource Source,
    ResolvedTokenRef Token) : PropertyModel(Name, Source);

[Acceptor<IModel>]
public partial record OptionalTokenPropertyModel(string Name, ValueMappingSource Source,
    ResolvedTokenRef Token) : PropertyModel(Name, Source);

[Acceptor<IModel>]
public partial record NodeClassModel(string Name, Rule ParserRule, Alternative? SourceAlt, IList<PropertyModel> Properties) : IModel
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

    /// <summary>
    /// Returns a value indicating whether this node class is abstract, i.e. it has
    /// concrete subclasses (<see cref="Variants"/>) that correspond to rule alternatives.
    /// </summary>
    public bool IsAbstract => Variants.Count > 0;

    // Visit{SourceContextName}, FooParser.{SourceContextName}Context, etc.
    public string SourceContextName => (SourceAlt?.ParserLabel ?? ParserRule.Name).Capitalize();
}

/// <summary>
/// Model of an AstBuilder class, which is a Visitor pattern implementation
/// that converts parse trees (created by ANTLR-generated parsers) to ASTs.
/// </summary>
/// <param name="AntlrGrammarName">Name of the input grammar (used by ANTLR to generate names of listeners, visitors, etc.), e.g. "Foo" or "FooParser"</param>
/// <param name="ParserClassName">Name of the parser class generated by ANTLR, e.g. "FooParser"</param>
/// <param name="AstMapping"></param>
[Acceptor<IModel>]
public partial record AstBuilderModel(string AntlrGrammarName, string ParserClassName, IList<AstMappingModel> AstMapping) : IModel;

public record AstMappingModel(Rule Rule, NodeClassModel Ast);

/// <summary>
/// Describes how to get a value for a property of the AST from the parse tree.
/// </summary>
public abstract record ValueMappingSource
{
    private ValueMappingSource() { }

    public sealed record FromLabel(string Label, LabelKind Kind) : ValueMappingSource;
    public sealed record FromGetter(int? Index = null) : ValueMappingSource;
}
