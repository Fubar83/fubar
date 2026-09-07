using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Diagnostics;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Version, environment, and one button that copies the lot.
///
/// <para>--version existed on the command line and nowhere in the window, so the first message of
/// every support conversation - "which version, which OS" - had no accurate answer available to the
/// person being asked. Half a day's work that changes the shape of every bug report received.</para>
/// </summary>
public partial class AboutViewModel : ViewModelBase
{
    private readonly IClipboardService? _clipboard;
    private readonly ILogSink? _log;
    private readonly IMachinePolicyService? _policy;
    private readonly StatusLogViewModel? _statusLog;

    public AboutViewModel(
        IClipboardService? clipboard = null,
        ILogSink? log = null,
        IMachinePolicyService? policy = null,
        StatusLogViewModel? statusLog = null)
    {
        _clipboard = clipboard;
        _log = log;
        _policy = policy;
        _statusLog = statusLog;
    }

    public string Version => Assembly()?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
        .FirstOrDefault()?.InformationalVersion
        ?? Assembly()?.GetName().Version?.ToString()
        ?? "unknown";

    public string Runtime => RuntimeInformation.FrameworkDescription;

    public string OperatingSystem => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    public string LogLocation => _log?.Location ?? "not being written to a file";

    /// <summary>Shown so an administrator can confirm the policy file was found, and a user can see
    /// why something is refused.</summary>
    public string PolicyLocation => _policy?.Source ?? "none";

    /// <summary>
    /// Everything above plus the last few log entries, as plain text.
    ///
    /// <para>Recent log lines are included because the question after "which version" is always "what
    /// were you doing", and a user who can paste both answers at once is a user whose bug gets fixed
    /// on the first exchange.</para>
    /// </summary>
    public string Diagnostics
    {
        get
        {
            var text = new StringBuilder()
                .AppendLine("Fubar API Studio")
                .AppendLine($"Version:  {Version}")
                .AppendLine($"Runtime:  {Runtime}")
                .AppendLine($"OS:       {OperatingSystem}")
                .AppendLine($"Log:      {LogLocation}")
                .AppendLine($"Policy:   {PolicyLocation}");

            if (_statusLog is { } log && log.Entries.Count > 0)
            {
                text.AppendLine().AppendLine("Recent log:");
                foreach (var entry in log.Entries.Take(20).Reverse())
                {
                    text.AppendLine($"  {entry.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z  {entry.Level}  {entry.Message}");
                }
            }

            return text.ToString();
        }
    }

    [RelayCommand]
    private async Task CopyDiagnosticsAsync()
    {
        if (_clipboard is not null)
        {
            await _clipboard.SetTextAsync(Diagnostics);
        }
    }

    private static System.Reflection.Assembly? Assembly() => typeof(AboutViewModel).Assembly;
}
