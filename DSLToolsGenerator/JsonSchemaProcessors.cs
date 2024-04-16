using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using NJsonSchema;
using NJsonSchema.Generation;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Namotion.Reflection;

namespace DSLToolsGenerator;

abstract class SchemaPropertyProcessor : ISchemaProcessor
{
    public virtual void Process(SchemaProcessorContext context)
    {
        ProcessProperties(context, ProcessProperty);
    }

    protected void ProcessProperties(SchemaProcessorContext context, PropertyProcessorAction callback)
    {
        foreach (var property in context.ContextualType.Properties)
        {
            string jsonPropertyName = context.Settings.ReflectionService
                .GetPropertyName(property, context.Settings);

            if (context.Schema.ActualProperties
                .TryGetValue(jsonPropertyName, out var propertySchema))
            {
                callback(property, propertySchema, context);
            }
        }
    }

    protected virtual void ProcessProperty(ContextualPropertyInfo property,
        JsonSchema schema, SchemaProcessorContext typeContext)
    {
    }

    public delegate void PropertyProcessorAction(ContextualPropertyInfo property,
        JsonSchema schema, SchemaProcessorContext typeContext);
}

partial class MarkdownDescriptionSchemaProcessor : SchemaPropertyProcessor
{
    public override void Process(SchemaProcessorContext context)
    {
        var options = context.Settings.GetXmlDocsOptions();
        options.FormattingMode = XmlDocsFormattingMode.Markdown;

        ProcessProperties(context, (property, schema, _) => {
            string? markdownDescription = property.GetXmlDocsSummary(options);

            if (!string.IsNullOrWhiteSpace(markdownDescription))
            {
                schema.ExtensionData ??= new Dictionary<string, object?>();
                schema.ExtensionData["markdownDescription"] =
                    // remove extra line breaks
                    SingleNewlinePattern().Replace(markdownDescription, " ");
            }
        });
    }

    [GeneratedRegex("""\r?\n(?!\r?\n)""")]
    private partial Regex SingleNewlinePattern();
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Struct,
    Inherited = false, AllowMultiple = true)]
public sealed class SnippetAttribute(
    string? label, string? description, string? markdownDescription, string body
    ) : Attribute
{
    public SnippetAttribute(string? label, string? description, string body)
        : this(label, description, null, body) { }

    public SnippetAttribute(string body)
        : this(null, null, null, body) { }

    public string? Label { get; init; } = label;
    public string? Description { get; init; } = description;
    public string? MarkdownDescription { get; init; } = markdownDescription;

    /// <summary>
    /// The code (JSON value) that is inserted after accepting
    /// the snippet. Can contain <c>\t</c> and <c>\n</c>, and also
    /// VSCode snippet placeholders like <c>${1}</c>, <c>${2:SomeDefault}</c>.
    /// </summary>
    [StringSyntax(StringSyntaxAttribute.Json)]
    public string Body { get; } = body;
}

// `defaultSnippets` is a custom (VSCode-specific) JSON Schema property
// that defines snippets/templates used for code completion while editing
// a JSON document with the given schema
// (in our case when editing the DTG configuration file)
class DefaultSnippetsSchemaProcessor : SchemaPropertyProcessor
{
    static JsonSerializer jsonSerializer = JsonSerializer.Create(new() {
        NullValueHandling = NullValueHandling.Ignore,
    });

    static JObject SnippetToJson(SnippetAttribute snippet) => JObject.FromObject(new {
        label = snippet.Label,
        description = snippet.Description,
        markdownDescription = snippet.MarkdownDescription,
        bodyText = snippet.Body,
    }, jsonSerializer);

    public override void Process(SchemaProcessorContext context)
    {
        var snippets = context.ContextualType
            .GetAttributes<SnippetAttribute>(inherit: false);
        if (snippets.Any())
        {
            context.Schema.ExtensionData ??= new Dictionary<string, object?>();
            context.Schema.ExtensionData["defaultSnippets"] = new JArray(
                snippets.Select(SnippetToJson).ToArray());
        }

        base.Process(context);
    }

    protected override void ProcessProperty(ContextualPropertyInfo property,
        JsonSchema schema, SchemaProcessorContext typeContext)
    {
        var snippets = property.GetAttributes<SnippetAttribute>(inherit: false);
        if (snippets.Any())
        {
            schema.ExtensionData ??= new Dictionary<string, object?>();
            schema.ExtensionData["defaultSnippets"] = new JArray(
                snippets.Select(SnippetToJson).ToArray());
        }
    }
}
