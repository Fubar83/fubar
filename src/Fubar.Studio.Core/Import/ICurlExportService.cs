using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Import;

/// <summary>Renders a <see cref="RequestModel"/> as a runnable <c>curl</c> command - the mirror of
/// <see cref="ICurlImportService"/>. <paramref name="resolve"/> substitutes <c>{{variable}}</c> tokens
/// (URL, header values, body) so the emitted command is ready to paste into a shell.</summary>
public interface ICurlExportService
{
    string ToCurl(RequestModel request, Func<string?, string> resolve);
}
