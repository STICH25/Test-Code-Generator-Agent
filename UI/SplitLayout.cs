namespace PlaywrightAgentAI.UI;

/// <summary>
/// Keeps a SplitContainer's minimum sizes and distance valid as its parent resizes.
///
/// A SplitContainer's SplitterDistance setter validates against whatever its own Width/Height
/// currently is - not the size it will eventually have once Dock=Fill layout settles - so
/// setting it once at construction time, before that layout has run, throws
/// "SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize" the moment the
/// real layout (or a later resize) produces a size the earlier value no longer fits. This has
/// to be re-applied on every resize, not just once: when Panel1MinSize + Panel2MinSize +
/// SplitterWidth exceeds the control's own extent, it abandons the layout pass entirely and
/// leaves both panels at stale bounds.
/// </summary>
public static class SplitLayout
{
    public static void Configure(SplitContainer split, int minPanel1, int minPanel2, int desired)
    {
        void Apply()
        {
            var extent = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            var available = extent - split.SplitterWidth;
            if (available <= 40)
                return;

            var min1 = minPanel1;
            var min2 = minPanel2;

            // Scale the minimums down proportionally rather than letting the pair exceed
            // what is actually on screen.
            if (min1 + min2 > available)
            {
                var scale = available / (double)(min1 + min2);
                min1 = Math.Max(20, (int)(min1 * scale));
                min2 = Math.Max(20, (int)(min2 * scale));
            }

            // Relax both minimums before moving the splitter: each setter validates against
            // the currently stored values, so tightening first can throw.
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            split.SplitterDistance = Math.Clamp(split.SplitterDistance, min1, Math.Max(min1, available - min2));
            split.Panel1MinSize = min1;
            split.Panel2MinSize = min2;
        }

        // The default Panel1MinSize/Panel2MinSize (25/25) are permissive enough that this
        // initial assignment cannot itself throw, whatever the control's own size still is
        // at this point - Apply() is what makes the real minimums take effect safely.
        split.SplitterDistance = Math.Max(1, desired);
        Apply();
        split.SizeChanged += (_, _) => Apply();
    }
}
