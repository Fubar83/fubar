using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fubar.Studio.Core.Models;

/// <summary>
/// One level's contribution to an inherited list of JSON paths: what it ADDS, and what it REMOVES from
/// whatever it inherited.
/// </summary>
/// <remarks>
/// <para>
/// This replaced wholesale replacement, where a level's list stood in for everything above it. That
/// was deliberate once - reading one file then told you what applied there - but it made adding a
/// single rule mean restating every inherited one, and the UI did that silently: it saved the RESOLVED
/// list, so adding one path to a request that inherited three copied all four onto the request and
/// ended inheritance for that setting with nothing on screen saying so.
/// </para>
/// <para>
/// What is given up is that one file no longer tells you what applies - only what that level changes.
/// The resolved view answers the other question, per entry and with the level each one came from,
/// which is a better answer than one file could give. Every other setting in this hierarchy already
/// works this way: a nullable <c>IgnoreWhitespace</c> says nothing about the effective value either.
/// </para>
/// <para>
/// Reads a bare JSON array for compatibility with files written before this existed, treating it as
/// <see cref="Add"/> - see <see cref="InheritedPathsJsonConverter"/>, which is also where the one
/// behavioural difference is written down.
/// </para>
/// </remarks>
[JsonConverter(typeof(InheritedPathsJsonConverter))]
public sealed class InheritedPaths
{
    public List<string> Add { get; set; } = [];

    public List<string> Remove { get; set; } = [];

    [JsonIgnore]
    public bool IsEmpty => Add.Count == 0 && Remove.Count == 0;

    public static InheritedPaths FromAdded(IEnumerable<string> paths) => new() { Add = [.. paths] };

    public InheritedPaths Clone() => new() { Add = [.. Add], Remove = [.. Remove] };

    /// <summary>
    /// Applies this level to what it inherited: removals first, then additions.
    ///
    /// <para>Removing something nothing added is NOT an error. A level is allowed to say "not here"
    /// about a rule an ancestor might grow later, and failing over it would make the outcome depend on
    /// the order the levels were written in.</para>
    /// </summary>
    public void ApplyTo(List<string> inherited)
    {
        ArgumentNullException.ThrowIfNull(inherited);

        foreach (var path in Remove)
        {
            inherited.RemoveAll(existing => string.Equals(existing, path, StringComparison.Ordinal));
        }

        foreach (var path in Add)
        {
            if (!inherited.Contains(path, StringComparer.Ordinal))
            {
                inherited.Add(path);
            }
        }
    }
}

/// <summary>
/// Reads both shapes and writes only the new one.
///
/// <para>A bare array (<c>["$.a", "$.b"]</c>) is the pre-hierarchy form and is read as
/// <c>{ "add": [...] }</c>. That is NOT equivalent: the old form also removed everything inherited,
/// so a workspace that relied on a shorter child list to drop an ancestor's rule reads differently
/// now. It can only reach a running app through a hand-edited file - no workspace is converted
/// automatically - and the reader reports it rather than fixing it silently.</para>
/// </summary>
public sealed class InheritedPathsJsonConverter : JsonConverter<InheritedPaths>
{
    public override InheritedPaths? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var paths = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? [];
            return InheritedPaths.FromAdded(paths);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an array or an object with \"add\"/\"remove\".");
        }

        var result = new InheritedPaths();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            var values = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? [];
            if (string.Equals(name, "add", StringComparison.OrdinalIgnoreCase))
            {
                result.Add = values;
            }
            else if (string.Equals(name, "remove", StringComparison.OrdinalIgnoreCase))
            {
                result.Remove = values;
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, InheritedPaths value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        // Only the halves that say something, so a file shows what the level changes and nothing else -
        // the same reason every other member here is nullable.
        if (value.Add.Count > 0)
        {
            writer.WritePropertyName("add");
            JsonSerializer.Serialize(writer, value.Add, options);
        }

        if (value.Remove.Count > 0)
        {
            writer.WritePropertyName("remove");
            JsonSerializer.Serialize(writer, value.Remove, options);
        }

        writer.WriteEndObject();
    }
}
