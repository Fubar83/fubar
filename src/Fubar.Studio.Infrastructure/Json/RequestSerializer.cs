using System.Text.Json;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Infrastructure.Json;

/// <summary>
/// <see cref="IRequestSerializer"/> over <see cref="FubarJson.Options"/> - the same settings the
/// storage adapters use, so the preview shows byte-for-byte what would land in `request.json`.
/// </summary>
public sealed class RequestSerializer : IRequestSerializer
{
    public string ToJson(RequestModel request) => JsonSerializer.Serialize(request, FubarJson.Options);
}
