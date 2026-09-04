namespace Fubar.Studio.Core.Models;

/// <summary>What a form field's <see cref="KeyValueItem.Value"/> means.</summary>
public enum FieldKind
{
    /// <summary>The value is the text to send. Everything except a multipart file part.</summary>
    Text,

    /// <summary>
    /// The value is a PATH, and the file's contents are sent as a file part.
    ///
    /// <para>Only meaningful in a <see cref="BodyType.FormData"/> body. Every form field was sent as
    /// <c>StringContent</c>, so the one thing <c>multipart/form-data</c> exists for was not reachable -
    /// picking the body type offered it and then could not do it.</para>
    /// </summary>
    File,
}

/// <summary>A single row in a Params/Headers/Variables key-value-description grid.</summary>
public sealed class KeyValueItem
{
    public string Key { get; set; } = "";

    /// <summary>The value, or - for <see cref="FieldKind.File"/> - the path to the file to upload.</summary>
    public string Value { get; set; } = "";

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Text unless this is a multipart file part. Defaults to Text and is omitted from the file when
    /// it is, so no existing request.json changes shape.
    /// </summary>
    public FieldKind Kind { get; set; } = FieldKind.Text;
}
