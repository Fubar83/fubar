using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Import;

/// <summary>
/// PORT. Renders a request as the JSON that would be written to disk.
///
/// Exists so the import preview can show an existing request against the one a spec would import,
/// using exactly the on-disk representation - what the user would see in a `git diff` after applying
/// the import, rather than a rendering invented for the dialog.
/// </summary>
public interface IRequestSerializer
{
    /// <summary>The request as its stored JSON document.</summary>
    string ToJson(RequestModel request);
}
