using System;
using System.Collections.ObjectModel;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Bottom collapsible Status &amp; Log dock: real-time network telemetry (Phase 5) and
/// debug console output. Phase 1 wires it up as a plain rolling log so the shell is interactive
/// end-to-end; HTTP timing/telemetry entries land here once <c>IHttpExecutionService</c> exists.
/// </summary>
public partial class StatusLogViewModel : ViewModelBase
{
    public ObservableCollection<string> Entries { get; } = [];

    public void Log(string message) => Entries.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
}
