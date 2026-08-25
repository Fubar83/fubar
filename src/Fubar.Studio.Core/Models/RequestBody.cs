namespace Fubar.Studio.Core.Models;

public enum BodyType
{
    None,
    Json,
    FormData,
    UrlEncoded,
    RawText,
    BinaryFile,
}

public sealed class RequestBody
{
    public BodyType Type { get; set; } = BodyType.None;

    /// <summary>Raw text for <see cref="BodyType.Json"/>/<see cref="BodyType.RawText"/>.</summary>
    public string? Raw { get; set; }

    /// <summary>Fields for <see cref="BodyType.FormData"/> - sent as <c>multipart/form-data</c>.</summary>
    public List<KeyValueItem> FormData { get; set; } = [];

    /// <summary>Fields for <see cref="BodyType.UrlEncoded"/> - sent as
    /// <c>application/x-www-form-urlencoded</c>.</summary>
    public List<KeyValueItem> UrlEncoded { get; set; } = [];

    /// <summary>Local path to the file for <see cref="BodyType.BinaryFile"/> - read from disk and
    /// sent as-is at execution time, never itself committed anywhere.</summary>
    public string? BinaryFilePath { get; set; }
}
