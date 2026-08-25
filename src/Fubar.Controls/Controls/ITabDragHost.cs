using System.Collections.Generic;
using Avalonia;

namespace Fubar.Controls;

/// <summary>
/// The host bridge a <see cref="TabStrip"/> uses for the operations it can't do generically - moving a
/// tab's underlying data between two strips' collections, tearing a tab off into a brand-new window, and
/// enumerating the sibling strips that can participate in a cross-window drag. The app implements this
/// (e.g. over its window manager); the strip owns all the pointer/gesture mechanics and calls back here
/// only for these domain/windowing decisions, so the strip itself stays app-agnostic.
/// </summary>
public interface ITabDragHost
{
    /// <summary>Every <see cref="TabStrip"/> a tab can currently be dropped into, across all app windows
    /// (including the one the drag started in).</summary>
    IEnumerable<TabStrip> PeerStrips { get; }

    /// <summary>Move <paramref name="item"/> out of <paramref name="from"/>'s collection and into
    /// <paramref name="to"/>'s at <paramref name="index"/> (-1 appends). Called live during a drag when
    /// the cursor crosses from one strip into another.</summary>
    void MoveTab(object item, TabStrip from, TabStrip to, int index);

    /// <summary>The tab was released clear of every strip: detach <paramref name="item"/> from
    /// <paramref name="from"/> and host it in a new window positioned near <paramref name="screenPoint"/>.</summary>
    void TearOff(object item, TabStrip from, PixelPoint screenPoint);

    /// <summary>Called once after any drop completes - a chance to tidy up (e.g. close windows the drag
    /// left empty).</summary>
    void AfterDrop();
}
