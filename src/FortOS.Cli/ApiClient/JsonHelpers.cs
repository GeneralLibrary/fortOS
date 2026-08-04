using System.Text.Json;

namespace FortOS.Cli.ApiClient;

/// <summary>Shared JSON helpers for API responses.</summary>
public static class JsonHelpers
{
    /// <summary>
    /// Recursively searches a JSON document (objects and arrays, depth-first) for the first
    /// string-typed property matching <paramref name="name"/> (case-insensitive).
    /// Used to locate fields such as "token", "error" or "message" that the API may return at
    /// varying nesting depths depending on the endpoint.
    /// </summary>
    public static string? FindString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindString(property.Value, name);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, name);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
