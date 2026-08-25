using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One node in the Response Pane's Interactive Tree view (ResponsePane.md §4.1.B): a JSON
/// object/array/value with its own children, JSONPath location (for "Copy Path"), and a
/// <see cref="Kind"/> the view resolves to a <c>Json*</c> color token via <c>JsonKindToBrushConverter</c>.</summary>
public sealed class JsonTreeNodeViewModel
{
    public required string Key { get; init; }

    public required string DisplayValue { get; init; }

    public required string Kind { get; init; }

    /// <summary>e.g. <c>$.data.users[0].id</c> - what "Copy Path" puts on the clipboard.</summary>
    public required string JsonPath { get; init; }

    /// <summary>The underlying node, for "Copy Value" (raw) / "Copy Node as JSON" (subtree).</summary>
    public JsonNode? RawNode { get; init; }

    public ObservableCollection<JsonTreeNodeViewModel> Children { get; } = [];

    /// <summary>Builds the tree for <paramref name="root"/> (a parsed response body) rooted at JSONPath <c>$</c>.</summary>
    public static JsonTreeNodeViewModel Build(JsonNode? root) => BuildNode("$", "$", root);

    private static JsonTreeNodeViewModel BuildNode(string key, string path, JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var objectNode = new JsonTreeNodeViewModel { Key = key, DisplayValue = "{ }", Kind = "Object", JsonPath = path, RawNode = node };
                foreach (var (propertyKey, propertyValue) in obj)
                {
                    objectNode.Children.Add(BuildNode(propertyKey, $"{path}.{propertyKey}", propertyValue));
                }

                return objectNode;

            case JsonArray array:
                var arrayNode = new JsonTreeNodeViewModel { Key = key, DisplayValue = $"[ {array.Count} ]", Kind = "Array", JsonPath = path, RawNode = node };
                for (var i = 0; i < array.Count; i++)
                {
                    arrayNode.Children.Add(BuildNode($"[{i}]", $"{path}[{i}]", array[i]));
                }

                return arrayNode;

            case JsonValue value:
                var kind = value.GetValueKind();
                var (display, kindName) = kind switch
                {
                    JsonValueKind.String => (value.ToJsonString(), "String"),
                    JsonValueKind.Number => (value.ToJsonString(), "Number"),
                    JsonValueKind.True or JsonValueKind.False => (value.ToJsonString(), "Boolean"),
                    _ => (value.ToJsonString(), "String"),
                };
                return new JsonTreeNodeViewModel { Key = key, DisplayValue = display, Kind = kindName, JsonPath = path, RawNode = node };

            default:
                return new JsonTreeNodeViewModel { Key = key, DisplayValue = "null", Kind = "Null", JsonPath = path, RawNode = null };
        }
    }
}
