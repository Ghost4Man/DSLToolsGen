using System.Text.RegularExpressions;

using NJsonSchema;
using NJsonSchema.Generation;
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
