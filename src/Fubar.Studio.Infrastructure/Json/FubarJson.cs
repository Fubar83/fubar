using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fubar.Studio.Infrastructure.Json;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used for every on-disk Fubar document
/// (<c>fubar.json</c>, <c>auth.json</c>, <c>request.json</c>). camelCase + indented + enums-as-strings
/// keeps files clean, human-readable, and diffable in Git, per Fubar's Git-native storage model.
/// </summary>
public static class FubarJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
