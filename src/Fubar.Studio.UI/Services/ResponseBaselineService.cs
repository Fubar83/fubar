using System;

namespace Fubar.Studio.UI.Services;

/// <summary>
/// The one pinned response, shared app-wide. Registered as a singleton precisely so a response pinned
/// on one request is still there after the user opens another.
/// </summary>
public sealed class ResponseBaselineService : IResponseBaselineService
{
    public PinnedResponse? Pinned { get; private set; }

    public event EventHandler? Changed;

    public void Pin(PinnedResponse response)
    {
        Pinned = response;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        Pinned = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
