using System;

namespace Fubar.Studio.UI.Services;

/// <summary>A response body set aside to compare later, with the text naming it in a diff header.</summary>
/// <param name="Body">The response body exactly as it was received.</param>
/// <param name="Label">Where it came from, e.g. <c>GET /orders · 200 OK · 14:03:11</c>.</param>
public sealed record PinnedResponse(string Body, string Label);

/// <summary>
/// Holds one response aside so a later one can be diffed against it.
///
/// API Studio has a single request canvas - <see cref="ViewModels.MainViewModel.ActiveEditor"/> is
/// one editor, not a tab set - so at any moment exactly one response exists on screen. Comparing two
/// therefore needs somewhere to keep the first, and pinning is that place: send against staging, pin,
/// switch environment or request, send again, compare. Because it lives above the editor it survives
/// switching requests, which is what makes cross-request and cross-environment comparison work at all.
///
/// Deliberately in-memory only. A pinned response is a scratch comparison, and persisting response
/// bodies outside the workspace's own history would put whatever they contain somewhere the user did
/// not ask for.
/// </summary>
public interface IResponseBaselineService
{
    /// <summary>The pinned response, or null when nothing is pinned.</summary>
    PinnedResponse? Pinned { get; }

    /// <summary>Pins a response, replacing any previous one.</summary>
    void Pin(PinnedResponse response);

    /// <summary>Discards the pin.</summary>
    void Clear();

    /// <summary>Raised whenever <see cref="Pinned"/> changes, so every response pane can re-evaluate.</summary>
    event EventHandler? Changed;
}
