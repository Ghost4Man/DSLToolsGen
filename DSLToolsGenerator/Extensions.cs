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
    {
        return string.Join(separator, items.Select(selector));
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

