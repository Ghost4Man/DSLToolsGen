using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSLToolsGenerator.SyntaxHighlighting;

/// <summary>
/// Schema for language grammar description files in Textmate and compatible editors.
/// See <a href="https://github.com/Septh/tmlanguage/blob/478ad124a21933cd4b0b65f1ee7ee18ee1f87473/tmlanguage.json">tmLanguage JSON schema</a>.
/// </summary>
/// <param name="ScopeName">
///     This should be a unique name for the grammar, following the convention of being a
///     dot-separated name where each new (left-most) part specializes the name. Normally it
///     would be a two-part name where the first is either <c>text</c> or <c>source</c> and the second is
///     the name of the language or document type. But if you are specializing an existing type,
///     you probably want to derive the name from the type you are specializing. For example
///     Markdown is <c>text.html.markdown</c> and Ruby on Rails (rhtml files) is <c>text.html.rails</c>.
///     The advantage of deriving it from (in this case) <c>text.html</c> is that everything which
///     works in the <c>text.html</c> scope will also work in the <c>text.html.«something»</c> scope (but
///     with a lower precedence than something specifically targeting <c>text.html.«something»</c>).</param>
/// <param name="Patterns">
///     An array with the actual rules used to parse the document.</param>
/// <param name="Uuid">
///     When the grammer is part of a larger bundle (ie., grammer + theme + whatever),
///     the uuid helps classify which file is a part of which bundle.</param>
/// <param name="FileTypes">
///     An array of file type extensions that the grammar should (by default) be
///     used with.</param>
/// <param name="FirstLineMatch">
///     A regular expression which is matched against the first line of the document
///     when it is first loaded. If it matches, the grammar is used for the document.</param>
/// <param name="FoldingStartMarker">
///     Regular expression that lines (in the document) are matched against.
///     If a line matches pattern, it starts a foldable block.</param>
/// <param name="FoldingStopMarker">
///     Regular expressions that lines (in the document) are matched against.
///     If a line matches pattern, it ends a foldable block.</param>
/// <param name="Injections">
///     [VS Code only, it seems] A dictionary (i.e. key/value pairs) of rules
///     which will be injected into an existing grammar. The key is the target scope of the
///     parent grammar and the value is the actual rule to inject.</param>
/// <param name="InjectionSelector">
///     The key is a scope selector that specifies which scope(s) the
///     current grammar should be injected in.</param>
/// <param name="Repository">
///     A dictionary (i.e. key/value pairs) of rules which can be included from
///     other places in the grammar. The key is the name of the rule and the value is the
///     actual rule.</param>
public record TmLanguageDocument(
    string Name,
    string ScopeName,
    IReadOnlyList<Pattern> Patterns,
    string? Uuid = null,
    IReadOnlyList<string>? FileTypes = null,
    string? FirstLineMatch = null,
    string? FoldingStartMarker = null,
    string? FoldingStopMarker = null,
    Dictionary<string, Pattern>? Injections = null,
    string? InjectionSelector = null,
    Dictionary<string, Pattern>? Repository = null)
{
    [JsonPropertyName("$schema"), JsonPropertyOrder(-1)]
    public string JsonSchema { get; init; } = "https://json.schemastore.org/tmlanguage.json";
}

/// <summary>
/// A single pattern/rule of a TextMate grammar (<c>tmLanguage</c>).
/// </summary>
/// <param name="Comment">
///     A generic text used to describe or explain the rule.</param>
/// <param name="Match">
///     A regular expression which is used to identify the portion of text to which the name
///     should be assigned.</param>
/// <param name="Name">
///     The scope name which gets assigned to the capture matched. This should generally be
///     derived from one of the standard names.</param>
/// <param name="Captures">
///     This key allows you to assign attributes to the captures of the <c>match</c>,
///     <c>begin</c>, <c>end</c> and <c>while</c>patterns. Using the <c>captures</c> key
///     for a <c>begin</c>/<c>end</c> rule is short-hand for giving both <c>beginCaptures</c>
///     and <c>endCaptures</c> with same values. The value of this key is a dictionary
///     with the key being the capture number and the value being a dictionary of attributes
///     to assign to the captured text.</param>
/// <param name="Disabled">
///     Marks the rule as disabled. A disabled rule should be ignored by the tokenization engine.
///     Set this property to 1 to disable the current pattern.</param>
/// <param name="Begin">
///     The <c>begin</c> key is a regular expression pattern that allows matches which span several
///     lines. Captures from the <c>begin</c> pattern can be referenced in the corresponding
///     <c>end</c> or <c>while</c> pattern by using normal regular expression back-references,
///     eg. <c>\1$</c>.</param>
/// <param name="BeginCaptures">
///     This key allows you to assign attributes to the captures of the <c>begin</c> pattern. The
///     value of this key is a dictionary with the key being the capture number and the value
///     being a dictionary of attributes to assign to the captured text.</param>
/// <param name="End">
///     A regular expression pattern that, when matched, ends the multi-line block started by the
///     <c>begin</c> key.</param>
/// <param name="EndCaptures">
///     This key allows you to assign attributes to the captures of the <c>end</c> pattern. The value
///     of this key is a dictionary with the key being the capture number and the value being a
///     dictionary of attributes to assign to the captured text.</param>
/// <param name="ApplyEndPatternLast">
///     Tests the <c>end</c> pattern after the other patterns in the <c>begin</c>/<c>end</c> block.</param>
/// <param name="ContentName">
///     This key is similar to the <c>name</c> key but it only assigns the name to the text between
///     what is matched by the <c>begin</c>/<c>end</c> patterns.</param>
/// <param name="While">
///     A regular expression pattern that, while matched, continues the multi-line block started
///     by the <c>begin</c> key.</param>
/// <param name="WhileCaptures">
///     This key allows you to assign attributes to the captures of the <c>while</c> pattern. The
///     value of this key is a dictionary with the key being the capture number and the value
///     being a dictionary of attributes to assign to the captured text.</param>
/// <param name="Patterns">
///     Applies to the region between the begin and end matches.</param>
/// <param name="Include">
///     This key allows you to reference a different language (value == scope name), recursively
///     reference the grammar itself (value == "$self") or a rule declared in this file’s
///     repository (value starts with a pound (#) sign).</param>
/// <param name="Repository">
///     A dictionary (i.e. key/value pairs) of rules which can be included from other places in
///     the grammar. The key is the name of the rule and the value is the actual rule.</param>
public record Pattern(
    string? Comment = null,
    string? Match = null,
    string? Name = null,
    Dictionary<string, Capture>? Captures = null,
    int? Disabled = null,
    string? Begin = null,
    Dictionary<string, Capture>? BeginCaptures = null,
    string? End = null,
    Dictionary<string, Capture>? EndCaptures = null,
    [property: JsonConverter(typeof(BoolAsIntConverter))]
    bool? ApplyEndPatternLast = null,
    string? ContentName = null,
    string? While = null,
    Dictionary<string, Capture>? WhileCaptures = null,
    IReadOnlyList<Pattern>? Patterns = null,
    string? Include = null,
    Dictionary<string, Pattern>? Repository = null
);

/// <summary>
/// What scope (and other patterns) to apply to a captured match.
/// </summary>
/// <param name="Name">The scope name which gets assigned to the capture matched.
///     This should generally be derived from one of the standard names.</param>
/// <param name="Patterns">Yes, captures can be further matched against additional patterns, too.</param>
public record Capture(string? Name, IReadOnlyList<Pattern>? Patterns);

public class BoolAsIntConverter : JsonConverter<bool>
{
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value ? 1 : 0);

    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => (reader.TokenType == JsonTokenType.Number
            && reader.TryGetInt32(out int value))
            ? value != 0
            : throw new JsonException("Expected a 0 or 1.");
}
