using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Import;

/// <summary>Parses a pasted <c>curl</c> command line into a <see cref="RequestModel"/> - the low-friction
/// "bring an existing request in" path. Pure and side-effect free so it is trivially unit-testable.</summary>
public interface ICurlImportService
{
    /// <summary>Parses <paramref name="curlCommand"/>. Throws <see cref="System.FormatException"/> if no URL
    /// can be found.</summary>
    RequestModel Parse(string curlCommand);
}
