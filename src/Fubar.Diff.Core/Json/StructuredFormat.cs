using System;
using Fubar.Diff.Core.Comparison;

namespace Fubar.Diff.Core.Json;

/// <summary>Which structured format a side of a comparison should be read as, if any.</summary>
public enum StructuredFormat
{
    /// <summary>Plain text. Nothing is parsed and no structural comparison is attempted.</summary>
    None,

    Json,

    Yaml,
}

/// <summary>
/// Decides how each side of a comparison should be read.
///
/// The asymmetry between the two formats is the whole content of this file, and it is not an
/// oversight. JSON is recognised by TRYING to parse it: almost nothing that is not JSON is valid
/// JSON, so a failed parse is a reliable "this is not that". YAML cannot be recognised that way at
/// all - a plain English sentence is a valid YAML document, and so is a log file, and so is a
/// Dockerfile - so sniffing it would turn every text comparison in the app into a comparison of two
/// one-scalar documents with, usually, nothing to report. YAML is therefore taken from the file's
/// name and never guessed at.
///
/// Per SIDE rather than per pair, which costs nothing and buys something real: a JSON config against
/// its YAML translation is a comparison people actually want, and YAML being a superset of JSON means
/// the two trees line up without anything special.
/// </summary>
public static class StructuredFormatDetector
{
    /// <summary>How this side should be read, given the mode the user chose.</summary>
    public static StructuredFormat For(string? path, ComparisonMode mode) => mode switch
    {
        ComparisonMode.Text => StructuredFormat.None,
        ComparisonMode.Json => StructuredFormat.Json,
        ComparisonMode.Yaml => StructuredFormat.Yaml,

        // Auto: the name decides for YAML, and everything else is offered to the JSON parser, which
        // will decline politely if it is not JSON.
        _ => IsYamlName(path) ? StructuredFormat.Yaml : StructuredFormat.Json,
    };

    private static bool IsYamlName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(path);

        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }
}
