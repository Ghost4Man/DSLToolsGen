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

    // a variant of SingleOrDefault that doesn't throw an exception if there's more than 1 item
    public static T? SingleOrDefaultIfMore<T>(this IEnumerable<T> items)
    {
        var enumerator = items.GetEnumerator();
        if (!enumerator.MoveNext()) return default; // empty
        var first = enumerator.Current;
        if (enumerator.MoveNext()) return default; // more than one
        return first; // exactly one
    }
}

public static class ActionExtensions
{
    public static T InvokeAndReturn<T>(this Action action, T value)
    {
        action.Invoke();
        return value;
    }
}
