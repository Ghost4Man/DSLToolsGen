using System.Globalization;
using System.Text;

using Antlr4Ast;

namespace DSLToolsGenerator;

static class StringExtensions
{
    public static string Capitalize(this string str) => str is [var first, .. var rest]
        ? char.ToUpperInvariant(first) + rest
        : str;

    public static bool StartsWithAny(this string str, params string[] prefixes)
        => prefixes.Any(str.StartsWith);

    public static string PreserveCase(this string str, string source)
    {
        if (string.IsNullOrWhiteSpace(str) || string.IsNullOrWhiteSpace(source))
            return str;
        bool allUpper = (source.All(c => char.IsUpper(c) || c == '_'));
        bool allLower = (source.All(c => char.IsLower(c) || c == '_'));
        bool capitalized = !allUpper
            && source is [char first, char second, ..]
            && char.IsUpper(first) && !char.IsUpper(second);
        return allUpper ? str.ToUpperInvariant() :
            allLower ? str.ToLowerInvariant() :
            capitalized ? str.Capitalize() :
            str;
    }

    // useful for null coalescing, e.g. `nullableStr?.Prepend("pre"))`
    public static string Prepend(this string str, string prefix)
    {
        ArgumentNullException.ThrowIfNull(str);
        return prefix + str;
    }

    public static string Append(this string str, string suffix)
    {
        ArgumentNullException.ThrowIfNull(str);
        return str + suffix;
    }
    public static string TrimSuffix(this string str, string suffix) => str.EndsWith(suffix) ? str[..^suffix.Length] : str;

    public static string ReplaceFirst(this string str, string oldValue, string newValue)
        => str.IndexOf(oldValue) is int index and >= 0
            ? str[0..index] + newValue + str[(index + oldValue.Length)..]
            : str;
}

static class SpanExtensions
{
    public static bool TrySliceUntil(this ReadOnlySpan<char> chars, string delimiter, out ReadOnlySpan<char> output)
    {
        if (chars.IndexOf(delimiter) is int index and >= 0)
        {
            output = chars[..index];
            return true;
        }
        else
        {
            output = default;
            return false;
        }
    }
}

static class EnumerableExtensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> items)
        where T : struct
    {
        return from item in items
               where item.HasValue
               select item.Value;
    }

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> items)
    {
        return from item in items
               where item is not null
               select item;
    }

    public static string MakeString<T>(this IEnumerable<T> items, string separator, Func<T, string> selector)
        => string.Join(separator, items.Select(selector));

    public static string MakeString<T>(this IEnumerable<T> items, string separator)
        => string.Join(separator, items);

    public static string MakeString<T>(this IEnumerable<T> items)
        => string.Concat(items);

    public static string MakeString<T>(this IEnumerable<T> items, string prefix, string separator, string suffix)
        => prefix + string.Join(separator, items) + suffix;
}

public static class ActionExtensions
{
    public static T InvokeAndReturn<T>(this Action action, T value)
    {
        action.Invoke();
        return value;
    }
}

static class RuleRefExtensions
{
    public static Rule? GetRuleOrNull(this RuleRef ruleRef, Grammar grammar)
        => grammar.TryGetRule(ruleRef.Name, out Rule? rule) ? rule : null;
}

static class TokenRefExtensions
{
    public static Rule? GetRuleOrNull(this TokenRef tokenRef, Grammar grammar)
        => grammar.TryGetRule(tokenRef.Name, out Rule? rule) ? rule : null;

    public static bool IsMany(this SyntaxElement element)
        => element.Suffix is SuffixKind.Plus or SuffixKind.Star
            or SuffixKind.PlusGreedy or SuffixKind.StarGreedy;

    public static bool IsOptional(this SyntaxElement element)
        => element.Suffix is SuffixKind.Optional or SuffixKind.OptionalGreedy;
}

static class SyntaxElementExtensions
{
    // Deconstruct extensions for pattern matching:

    public static void Deconstruct(this Block block, out IReadOnlyList<Alternative> alts) => alts = block.Items;
    public static void Deconstruct(this Alternative alt, out IReadOnlyList<SyntaxElement> elements) => elements = alt.Elements;
    public static void Deconstruct(this TokenRef tokenRef, out string name) => name = tokenRef.Name;
    public static void Deconstruct(this RuleRef ruleRef, out string name) => name = ruleRef.Name;
    public static void Deconstruct(this Literal literal, out string value) => value = literal.GetValue();

    /// <summary>
    /// Evaluates the contents of this literal including any escape
    /// sequences into the actual characters they represent.
    /// </summary>
    public static string GetValue(this Literal literal)
    {
        StringBuilder output = new(literal.Text.Length, literal.Text.Length);
        Span<char> buffer = stackalloc char[2];
        ReadOnlySpan<char> rest = literal.Text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (tryReplace(@"\n", '\n', ref rest)
                || tryReplace(@"\r", '\r', ref rest)
                || tryReplace(@"\t", '\t', ref rest)
                || tryReplace(@"\f", '\f', ref rest)
                || tryReplace(@"\b", '\b', ref rest)
                || tryReplace(@"\\", '\\', ref rest))
            { }
            else if (rest is ['\\', .. var esc])
            {
                if (esc is ['u', ..] && esc.Length >= 5 // e.g. "u0400"
                    && esc[1..5] is var hexCode
                    && int.TryParse(hexCode, NumberStyles.HexNumber, null, out int value))
                {
                    output.Append((char)value);
                    rest = esc[5..]; // esc="u12345", rest="5"
                }
                else if (esc is ['u', '{', ..var escRest] // e.g. "u{1F600}"
                    && escRest.TrySliceUntil("}", out var extHexCode)
                    && int.TryParse(extHexCode, NumberStyles.HexNumber, null, out value)
                    && Rune.TryCreate(value, out Rune rune)
                    && rune.EncodeToUtf16(buffer) is int charsWritten and (1 or 2))
                {
                    output.Append(buffer[..charsWritten]);
                    rest = escRest[(extHexCode.Length + 1)..];
                }
                else
                    rest = rest[1..];
            }
            else
            {
                output.Append(rest[0]);
                rest = rest[1..];
            }
        }

        return output.ToString();

        bool tryReplace(ReadOnlySpan<char> escapeSequence, char value, ref ReadOnlySpan<char> rest)
        {
            if (rest.StartsWith(escapeSequence))
            {
                output.Append(value);
                rest = rest[escapeSequence.Length..];
                return true;
            }
            return false;
        }
    }
}
