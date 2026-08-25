namespace Fubar.Studio.Core.Models;

/// <summary>A single row in a Params/Headers/Variables key-value-description grid.</summary>
public sealed class KeyValueItem
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;
}
